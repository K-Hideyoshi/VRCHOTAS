#include <Windows.h>
#include <array>
#include <cstdint>
#include <openvr_driver.h>
#include "driver_constants.h"
#include "driver_logging.h"
#include "driver_openvr_helpers.h"
#include "hotas_server_driver.h"

namespace
{
    constexpr ULONGLONG kAppHeartbeatTimeoutMs = 5000;

    const char* const kCompatibleInterfaceVersions[] =
    {
        vr::IVRSettings_Version,
        vr::ITrackedDeviceServerDriver_Version,
        vr::IVRDisplayComponent_Version,
        vr::IVRDriverDirectModeComponent_Version,
        vr::IVRCameraComponent_Version,
        vrchotas::driver::kServerTrackedDeviceProviderVersion004,
        vr::IServerTrackedDeviceProvider_Version,
        vr::IVRWatchdogProvider_Version,
        vr::IVRVirtualDisplay_Version,
        vr::IVRDriverManager_Version,
        vr::IVRResources_Version,
        vr::IVRCompositorPluginProvider_Version,
        vr::IVRIPCResourceManagerClient_Version,
        nullptr
    };
}

bool HotasServerDriver::ShouldExposeVirtualControllers(const vrchotas::VirtualControllerState& snapshot) const
{
    if (snapshot.app_heartbeat_tick_ms == 0)
    {
        return false;
    }

    const auto now = GetTickCount64();
    const auto age = now >= snapshot.app_heartbeat_tick_ms
        ? now - snapshot.app_heartbeat_tick_ms
        : 0;
    return age <= kAppHeartbeatTimeoutMs;
}

void HotasServerDriver::EnsureVirtualControllersRegistered()
{
    if (_controllersRegistered)
    {
        return;
    }

    _left = std::make_unique<HotasControllerDevice>(vr::TrackedControllerRole_LeftHand);
    _right = std::make_unique<HotasControllerDevice>(vr::TrackedControllerRole_RightHand);

    const bool leftAdded = vr::VRServerDriverHost()->TrackedDeviceAdded("vrchotas_left", vr::TrackedDeviceClass_Controller, _left.get());
    const bool rightAdded = vr::VRServerDriverHost()->TrackedDeviceAdded("vrchotas_right", vr::TrackedDeviceClass_Controller, _right.get());
    DriverLogF("[vrchotas] TrackedDeviceAdded(%s) => %s", "vrchotas_left", leftAdded ? "true" : "false");
    DriverLogF("[vrchotas] TrackedDeviceAdded(%s) => %s", "vrchotas_right", rightAdded ? "true" : "false");
    _controllersRegistered = leftAdded && rightAdded;
    _loggedWaitingForAppHeartbeat = false;
    if (_controllersRegistered)
    {
        SetVirtualControllersConnected(true, true);
    }
}

void HotasServerDriver::SetVirtualControllersConnected(bool leftConnected, bool rightConnected)
{
    if (!_left || !_right)
    {
        return;
    }

    _left->SetDeviceConnected(leftConnected);
    _right->SetDeviceConnected(rightConnected);
    _lastDesiredLeftControllerConnection = leftConnected;
    _lastDesiredRightControllerConnection = rightConnected;
}

vr::EVRInitError HotasServerDriver::Init(vr::IVRDriverContext* pDriverContext)
{
    VR_INIT_SERVER_DRIVER_CONTEXT(pDriverContext);
    DriverLog("[vrchotas] Server driver initialization started.");

    _mapping = CreateFileMappingW(
        INVALID_HANDLE_VALUE,
        nullptr,
        PAGE_READWRITE,
        0,
        sizeof(vrchotas::VirtualControllerState),
        vrchotas::kSharedMemoryName);
    const DWORD mappingError = GetLastError();
    _mutex = CreateMutexW(nullptr, FALSE, vrchotas::kSharedMemoryMutexName);
    const DWORD mutexError = GetLastError();
    _view = static_cast<vrchotas::VirtualControllerState*>(
        MapViewOfFile(_mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(vrchotas::VirtualControllerState)));

    if (_mapping)
    {
        DriverLogF("[vrchotas] Shared memory handle ready. alreadyExists=%s", mappingError == ERROR_ALREADY_EXISTS ? "true" : "false");
    }
    else
    {
        DriverLogLastError("CreateFileMappingW");
    }

    if (_mutex)
    {
        DriverLogF("[vrchotas] Shared mutex handle ready. alreadyExists=%s", mutexError == ERROR_ALREADY_EXISTS ? "true" : "false");
    }
    else
    {
        DriverLogLastError("CreateMutexW");
    }

    if (_view)
    {
        DriverLog("[vrchotas] Shared memory view mapped successfully.");
    }
    else
    {
        DriverLogLastError("MapViewOfFile");
    }

    if (!_mapping || !_mutex || !_view)
    {
        DriverLog("[vrchotas] Failed to create or open shared memory resources.");
    }

    DriverLog("[vrchotas] Server driver initialization completed.");

    return vr::VRInitError_None;
}

void HotasServerDriver::Cleanup()
{
    DriverLog("[vrchotas] Server driver cleanup started.");
    _left.reset();
    _right.reset();

    if (_view)
    {
        UnmapViewOfFile(_view);
        _view = nullptr;
    }

    if (_mapping)
    {
        CloseHandle(_mapping);
        _mapping = nullptr;
    }

    if (_mutex)
    {
        CloseHandle(_mutex);
        _mutex = nullptr;
    }

    VR_CLEANUP_SERVER_DRIVER_CONTEXT();
    DriverLog("[vrchotas] Server driver cleanup completed.");
}

const char* const* HotasServerDriver::GetInterfaceVersions()
{
    return kCompatibleInterfaceVersions;
}

void HotasServerDriver::RunFrame()
{
    if (!_view || !_mutex)
    {
        if (!_loggedMissingRuntimeResources)
        {
            DriverLog("[vrchotas] RunFrame skipped because runtime resources are not ready.");
            _loggedMissingRuntimeResources = true;
        }

        return;
    }

    if (!_loggedFirstRunFrame)
    {
        DriverLog("[vrchotas] RunFrame entered for the first time.");
        _loggedFirstRunFrame = true;
    }

    const DWORD waitResult = WaitForSingleObject(_mutex, 1);
    if (waitResult == WAIT_OBJECT_0)
    {
        const auto snapshot = *_view;
        _view->driver_heartbeat_tick_ms = GetTickCount64();
        ReleaseMutex(_mutex);

        if (!ShouldExposeVirtualControllers(snapshot))
        {
            if (!_loggedWaitingForAppHeartbeat)
            {
                DriverLog("[vrchotas] Waiting for VRCHOTAS heartbeat before exposing virtual controllers.");
                _loggedWaitingForAppHeartbeat = true;
            }

            if (_controllersRegistered && (_lastDesiredLeftControllerConnection || _lastDesiredRightControllerConnection))
            {
                DriverLog("[vrchotas] VRCHOTAS heartbeat lost. Marking virtual controllers disconnected.");
                SetVirtualControllersConnected(false, false);
            }

            _consecutiveMutexWaitFailures = 0;
            return;
        }

        EnsureVirtualControllersRegistered();
        if (!_left || !_right)
        {
            return;
        }

        const bool useVirtualLeft = ShouldUseVirtualController(snapshot, vr::TrackedControllerRole_LeftHand);
        const bool useVirtualRight = ShouldUseVirtualController(snapshot, vr::TrackedControllerRole_RightHand);

        if (snapshot.pose_source != _lastLoggedPoseSource)
        {
            DriverLogF("[vrchotas] Pose handoff mode changed: %s", PoseSourceToString(snapshot.pose_source));
            _left->PrepareForReconnect();
            _right->PrepareForReconnect();
        }

        if (useVirtualLeft != _lastDesiredLeftControllerConnection || useVirtualRight != _lastDesiredRightControllerConnection)
        {
            SetVirtualControllersConnected(useVirtualLeft, useVirtualRight);
        }

        const std::int32_t leftHandSelectionPriority = useVirtualLeft
            ? vrchotas::driver::kMappedHandSelectionPriority
            : vrchotas::driver::kRealControllerPreferredHandSelectionPriority;
        const std::int32_t rightHandSelectionPriority = useVirtualRight
            ? vrchotas::driver::kMappedHandSelectionPriority
            : vrchotas::driver::kRealControllerPreferredHandSelectionPriority;

        if (snapshot.pose_source != _lastLoggedPoseSource)
        {
            _left->ForceReannounceHandSelectionPriority(leftHandSelectionPriority, "pose-source-changed");
            _right->ForceReannounceHandSelectionPriority(rightHandSelectionPriority, "pose-source-changed");
            DriverLogF(
                "[vrchotas] Hand selection routing (mode=%s): leftVirtual=%s rightVirtual=%s leftPriority=%d rightPriority=%d",
                PoseSourceToString(snapshot.pose_source),
                useVirtualLeft ? "true" : "false",
                useVirtualRight ? "true" : "false",
                leftHandSelectionPriority,
                rightHandSelectionPriority);
            _lastLoggedPoseSource = snapshot.pose_source;
        }

        _left->SetHandSelectionPriority(leftHandSelectionPriority, PoseSourceToString(snapshot.pose_source));
        _right->SetHandSelectionPriority(rightHandSelectionPriority, PoseSourceToString(snapshot.pose_source));

        const auto& leftInput = snapshot.left;
        const auto& rightInput = snapshot.right;

        if (!_loggedButtonAxisMirrorLimitation && (ShouldKeepRealController(snapshot, vr::TrackedControllerRole_LeftHand)
            || ShouldKeepRealController(snapshot, vr::TrackedControllerRole_RightHand)))
        {
            DriverLog("[vrchotas] Hybrid mode keeps one real controller active directly in SteamVR. VRCHOTAS does not mirror or override that hand; it only drives the opposite virtual hand.");
            _loggedButtonAxisMirrorLimitation = true;
        }

        if (useVirtualLeft)
        {
            _left->UpdateState(leftInput, nullptr);
        }

        if (useVirtualRight)
        {
            _right->UpdateState(rightInput, nullptr);
        }
        _consecutiveMutexWaitFailures = 0;
        return;
    }

    ++_consecutiveMutexWaitFailures;
    if (!_loggedMutexWaitFailure || _consecutiveMutexWaitFailures == 1000)
    {
        DriverLogF("[vrchotas] WaitForSingleObject on shared mutex failed or timed out. result=%lu consecutiveFailures=%lu", waitResult, _consecutiveMutexWaitFailures);
        _loggedMutexWaitFailure = true;
        if (_consecutiveMutexWaitFailures == 1000)
        {
            _consecutiveMutexWaitFailures = 1;
        }
    }
}

bool HotasServerDriver::ShouldBlockStandbyMode()
{
    DriverLog("[vrchotas] ShouldBlockStandbyMode called.");
    return false;
}

void HotasServerDriver::EnterStandby()
{
    DriverLog("[vrchotas] Server driver entering standby.");
}

void HotasServerDriver::LeaveStandby()
{
    DriverLog("[vrchotas] Server driver leaving standby.");
}
