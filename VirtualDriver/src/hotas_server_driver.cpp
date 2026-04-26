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

    _left = std::make_unique<HotasControllerDevice>(vr::TrackedControllerRole_LeftHand);
    _right = std::make_unique<HotasControllerDevice>(vr::TrackedControllerRole_RightHand);

    const bool leftAdded = vr::VRServerDriverHost()->TrackedDeviceAdded("vrchotas_left", vr::TrackedDeviceClass_Controller, _left.get());
    const bool rightAdded = vr::VRServerDriverHost()->TrackedDeviceAdded("vrchotas_right", vr::TrackedDeviceClass_Controller, _right.get());
    DriverLogF("[vrchotas] TrackedDeviceAdded(%s) => %s", "vrchotas_left", leftAdded ? "true" : "false");
    DriverLogF("[vrchotas] TrackedDeviceAdded(%s) => %s", "vrchotas_right", rightAdded ? "true" : "false");
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
    if (!_view || !_left || !_right || !_mutex)
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

        if (!_loggedButtonAxisMirrorLimitation)
        {
            DriverLog("[vrchotas] Real-controller button/axis mirroring is unavailable in current OpenVR driver APIs. In MirrorRealControllers mode, virtual button/axis values are forced to neutral.");
            _loggedButtonAxisMirrorLimitation = true;
        }

        if (snapshot.pose_source != _lastLoggedPoseSource)
        {
            DriverLogF("[vrchotas] Pose handoff mode changed: %s", PoseSourceToString(snapshot.pose_source));
            _lastLoggedPoseSource = snapshot.pose_source;
        }

        const std::int32_t handSelectionPriority = ShouldUseMirroredRealControllerPose(snapshot)
            ? vrchotas::driver::kMirroredHandSelectionPriority
            : vrchotas::driver::kMappedHandSelectionPriority;

        if (snapshot.pose_source != _lastLoggedPoseSource)
        {
            DriverLogF(
                "[vrchotas] Hand selection routing (mode=%s): virtualPriority=%d leftRealIndex=%u rightRealIndex=%u",
                PoseSourceToString(snapshot.pose_source),
                handSelectionPriority,
                _lastLeftRealControllerIndex,
                _lastRightRealControllerIndex);
        }

        _left->SetHandSelectionPriority(handSelectionPriority, PoseSourceToString(snapshot.pose_source));
        _right->SetHandSelectionPriority(handSelectionPriority, PoseSourceToString(snapshot.pose_source));

        vr::DriverPose_t mirroredLeftPose{};
        vr::DriverPose_t mirroredRightPose{};
        const vr::DriverPose_t* leftPoseOverride = nullptr;
        const vr::DriverPose_t* rightPoseOverride = nullptr;
        vr::TrackedDeviceIndex_t realLeftDeviceIndex = vr::k_unTrackedDeviceIndexInvalid;
        vr::TrackedDeviceIndex_t realRightDeviceIndex = vr::k_unTrackedDeviceIndexInvalid;

        std::array<vr::TrackedDevicePose_t, vr::k_unMaxTrackedDeviceCount> trackedPoses{};

        if (ShouldUseMirroredRealControllerPose(snapshot))
        {
            vr::VRServerDriverHost()->GetRawTrackedDevicePoses(0.0f, trackedPoses.data(), static_cast<std::uint32_t>(trackedPoses.size()));
        }

        if (ShouldUseMirroredRealControllerPose(snapshot))
        {
            const auto leftFound = TryFindRealControllerPose(
                vr::TrackedControllerRole_LeftHand,
                trackedPoses.data(),
                static_cast<std::uint32_t>(trackedPoses.size()),
                mirroredLeftPose,
                &realLeftDeviceIndex);
            const auto rightFound = TryFindRealControllerPose(
                vr::TrackedControllerRole_RightHand,
                trackedPoses.data(),
                static_cast<std::uint32_t>(trackedPoses.size()),
                mirroredRightPose,
                &realRightDeviceIndex);

            if (leftFound)
            {
                leftPoseOverride = &mirroredLeftPose;
            }

            if (rightFound)
            {
                rightPoseOverride = &mirroredRightPose;
            }

            if (leftFound != _lastLeftRealControllerFound || rightFound != _lastRightRealControllerFound
                || realLeftDeviceIndex != _lastLeftRealControllerIndex || realRightDeviceIndex != _lastRightRealControllerIndex)
            {
                DriverLogF(
                    "[vrchotas] Real controller discovery (mode=%s): left=%s(index=%u) right=%s(index=%u)",
                    PoseSourceToString(snapshot.pose_source),
                    leftFound ? "found" : "missing",
                    leftFound ? realLeftDeviceIndex : vr::k_unTrackedDeviceIndexInvalid,
                    rightFound ? "found" : "missing",
                    rightFound ? realRightDeviceIndex : vr::k_unTrackedDeviceIndexInvalid);

                _lastLeftRealControllerFound = leftFound;
                _lastRightRealControllerFound = rightFound;
                _lastLeftRealControllerIndex = leftFound ? realLeftDeviceIndex : vr::k_unTrackedDeviceIndexInvalid;
                _lastRightRealControllerIndex = rightFound ? realRightDeviceIndex : vr::k_unTrackedDeviceIndexInvalid;
            }
        }

        const auto& leftInput = snapshot.left;
        const auto& rightInput = snapshot.right;

        _left->UpdateState(leftInput, leftPoseOverride);
        _right->UpdateState(rightInput, rightPoseOverride);
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
