#include <Windows.h>
#include <array>
#include <cstdarg>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <memory>
#include <mutex>
#include <string>
#include <openvr_driver.h>
#include "virtual_controller_state.h"

namespace
{
    constexpr ULONGLONG kPoseSampleIntervalMs = 5000;
    constexpr int32_t kMappedHandSelectionPriority = 1000;
    constexpr int32_t kMirroredHandSelectionPriority = -1000;
    constexpr const char* kControllerType = "vrchotas_virtual";
    constexpr const char* kInputProfilePath = "vrchotas/input/vrchotas_virtual_profile.json";
    constexpr const char* kTrackingSystemName = "vrchotas";
    constexpr const char* kManufacturerName = "VRCHOTAS";
    constexpr const char* kServerTrackedDeviceProviderVersion004 = "IServerTrackedDeviceProvider_004";

    const char* const kCompatibleInterfaceVersions[] =
    {
        vr::IVRSettings_Version,
        vr::ITrackedDeviceServerDriver_Version,
        vr::IVRDisplayComponent_Version,
        vr::IVRDriverDirectModeComponent_Version,
        vr::IVRCameraComponent_Version,
        kServerTrackedDeviceProviderVersion004,
        vr::IServerTrackedDeviceProvider_Version,
        vr::IVRWatchdogProvider_Version,
        vr::IVRVirtualDisplay_Version,
        vr::IVRDriverManager_Version,
        vr::IVRResources_Version,
        vr::IVRCompositorPluginProvider_Version,
        vr::IVRIPCResourceManagerClient_Version,
        nullptr
    };

    std::filesystem::path ResolveDriverLogFilePath()
    {
        HMODULE moduleHandle = nullptr;
        if (!GetModuleHandleExW(
                GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                reinterpret_cast<LPCWSTR>(&ResolveDriverLogFilePath),
                &moduleHandle))
        {
            return {};
        }

        std::array<wchar_t, MAX_PATH> modulePath{};
        const auto pathLength = GetModuleFileNameW(moduleHandle, modulePath.data(), static_cast<DWORD>(modulePath.size()));
        if (pathLength == 0 || pathLength >= modulePath.size())
        {
            return {};
        }

        auto path = std::filesystem::path(modulePath.data()).parent_path();
        if (path.empty())
        {
            return {};
        }

        path = path.parent_path();
        if (path.empty())
        {
            return {};
        }

        path = path.parent_path();
        if (path.empty())
        {
            return {};
        }

        auto logDirectory = path.parent_path();
        if (logDirectory.empty())
        {
            return path / "vrchotas-driver.log";
        }

        return logDirectory / "vrchotas-driver.log";
    }

    void AppendDriverFileLog(const char* message)
    {
        static std::mutex logMutex;
        static const std::filesystem::path logFilePath = ResolveDriverLogFilePath();

        if (logFilePath.empty())
        {
            return;
        }

        std::error_code errorCode;
        std::filesystem::create_directories(logFilePath.parent_path(), errorCode);

        std::lock_guard<std::mutex> lock(logMutex);
        std::ofstream stream(logFilePath, std::ios::app);
        if (!stream.is_open())
        {
            return;
        }

        SYSTEMTIME localTime{};
        GetLocalTime(&localTime);
        char timestamp[64]{};
        snprintf(
            timestamp,
            sizeof(timestamp),
            "%04u-%02u-%02u %02u:%02u:%02u.%03u",
            localTime.wYear,
            localTime.wMonth,
            localTime.wDay,
            localTime.wHour,
            localTime.wMinute,
            localTime.wSecond,
            localTime.wMilliseconds);

        stream << timestamp << ' ' << message << std::endl;
    }

    void DriverLog(const char* message)
    {
        AppendDriverFileLog(message);

        if (!vr::VRDriverContext())
        {
            return;
        }

        if (auto* log = vr::VRDriverLog())
        {
            log->Log(message);
        }
    }

    void DriverLogF(const char* format, ...)
    {
        char buffer[512]{};
        va_list args;
        va_start(args, format);
        vsnprintf(buffer, sizeof(buffer), format, args);
        va_end(args);
        DriverLog(buffer);
    }

    void DriverLogLastError(const char* action)
    {
        DriverLogF("[vrchotas] %s failed. GetLastError=%lu", action, GetLastError());
    }

    const char* RoleToString(vr::ETrackedControllerRole role)
    {
        return role == vr::TrackedControllerRole_LeftHand ? "LeftHand" : "RightHand";
    }

    bool HasMeaningfulInput(const vrchotas::ControllerHandState& hand)
    {
        for (bool button : hand.buttons)
        {
            if (button)
            {
                return true;
            }
        }

        for (double axis : hand.axes)
        {
            if (std::abs(axis) > 0.001)
            {
                return true;
            }
        }

        for (double value : hand.position)
        {
            if (std::abs(value) > 0.001)
            {
                return true;
            }
        }

        for (int i = 1; i < vrchotas::kQuat; ++i)
        {
            if (std::abs(hand.quaternion[i]) > 0.001)
            {
                return true;
            }
        }

        for (double value : hand.linear_velocity)
        {
            if (std::abs(value) > 0.001)
            {
                return true;
            }
        }

        for (double value : hand.angular_velocity)
        {
            if (std::abs(value) > 0.001)
            {
                return true;
            }
        }

        return false;
    }

    bool ShouldUseMirroredRealControllerPose(const vrchotas::VirtualControllerState& state)
    {
        return state.pose_source == vrchotas::VirtualPoseSource::MirrorRealControllers;
    }

    const char* PoseSourceToString(vrchotas::VirtualPoseSource poseSource)
    {
        return poseSource == vrchotas::VirtualPoseSource::MirrorRealControllers
            ? "MirrorRealControllers"
            : "Mapped";
    }

    bool TryGetTrackedDeviceStringProperty(vr::TrackedDeviceIndex_t deviceIndex, vr::ETrackedDeviceProperty property, std::string& value)
    {
        value.clear();
        if (!vr::VRProperties())
        {
            return false;
        }

        const auto container = vr::VRProperties()->TrackedDeviceToPropertyContainer(deviceIndex);
        char buffer[vr::k_unMaxPropertyStringSize]{};
        vr::ETrackedPropertyError error = vr::TrackedProp_Success;
        vr::VRProperties()->GetStringProperty(container, property, buffer, sizeof(buffer), &error);
        if (error != vr::TrackedProp_Success)
        {
            return false;
        }

        value = buffer;
        return true;
    }

    bool TryGetTrackedDeviceRole(vr::TrackedDeviceIndex_t deviceIndex, vr::ETrackedControllerRole& role)
    {
        role = vr::TrackedControllerRole_Invalid;
        if (!vr::VRProperties())
        {
            return false;
        }

        const auto container = vr::VRProperties()->TrackedDeviceToPropertyContainer(deviceIndex);
        vr::ETrackedPropertyError error = vr::TrackedProp_Success;
        const auto rawRole = vr::VRProperties()->GetInt32Property(container, vr::Prop_ControllerRoleHint_Int32, &error);
        if (error != vr::TrackedProp_Success)
        {
            return false;
        }

        role = static_cast<vr::ETrackedControllerRole>(rawRole);
        return true;
    }

    bool IsVrchotasTrackedDevice(vr::TrackedDeviceIndex_t deviceIndex)
    {
        std::string trackingSystemName;
        if (TryGetTrackedDeviceStringProperty(deviceIndex, vr::Prop_TrackingSystemName_String, trackingSystemName)
            && trackingSystemName == kTrackingSystemName)
        {
            return true;
        }

        std::string serialNumber;
        return TryGetTrackedDeviceStringProperty(deviceIndex, vr::Prop_SerialNumber_String, serialNumber)
            && serialNumber.rfind("vrchotas_", 0) == 0;
    }

    void NormalizeQuaternion(double& w, double& x, double& y, double& z)
    {
        const double len = std::sqrt(w * w + x * x + y * y + z * z);
        if (len > 1e-10)
        {
            w /= len;
            x /= len;
            y /= len;
            z /= len;
        }
        else
        {
            w = 1;
            x = y = z = 0;
        }
    }

    void FillDriverPoseFromHand(const vrchotas::ControllerHandState& hand, vr::DriverPose_t& pose)
    {
        pose = {};
        pose.poseTimeOffset = 0;
        pose.qWorldFromDriverRotation.w = 1;
        pose.vecWorldFromDriverTranslation[0] = 0;
        pose.vecWorldFromDriverTranslation[1] = 0;
        pose.vecWorldFromDriverTranslation[2] = 0;
        pose.qDriverFromHeadRotation.w = 1;
        pose.vecDriverFromHeadTranslation[0] = 0;
        pose.vecDriverFromHeadTranslation[1] = 0;
        pose.vecDriverFromHeadTranslation[2] = 0;

        for (int i = 0; i < vrchotas::kVec3; ++i)
        {
            pose.vecPosition[i] = hand.position[i];
            pose.vecVelocity[i] = hand.linear_velocity[i];
            pose.vecAngularVelocity[i] = hand.angular_velocity[i];
        }

        double qw = hand.quaternion[0];
        double qx = hand.quaternion[1];
        double qy = hand.quaternion[2];
        double qz = hand.quaternion[3];
        NormalizeQuaternion(qw, qx, qy, qz);
        pose.qRotation.w = qw;
        pose.qRotation.x = qx;
        pose.qRotation.y = qy;
        pose.qRotation.z = qz;

        pose.vecAcceleration[0] = pose.vecAcceleration[1] = pose.vecAcceleration[2] = 0;
        pose.vecAngularAcceleration[0] = pose.vecAngularAcceleration[1] = pose.vecAngularAcceleration[2] = 0;

        pose.result = vr::TrackingResult_Running_OK;
        pose.poseIsValid = true;
        pose.willDriftInYaw = false;
        pose.shouldApplyHeadModel = false;
        pose.deviceIsConnected = true;
    }

    void FillDriverPoseFromTrackedPose(const vr::TrackedDevicePose_t& trackedPose, vr::DriverPose_t& pose)
    {
        pose = {};
        pose.poseTimeOffset = 0;
        pose.qWorldFromDriverRotation.w = 1;
        pose.vecWorldFromDriverTranslation[0] = 0;
        pose.vecWorldFromDriverTranslation[1] = 0;
        pose.vecWorldFromDriverTranslation[2] = 0;
        pose.qDriverFromHeadRotation.w = 1;
        pose.vecDriverFromHeadTranslation[0] = 0;
        pose.vecDriverFromHeadTranslation[1] = 0;
        pose.vecDriverFromHeadTranslation[2] = 0;

        pose.vecPosition[0] = trackedPose.mDeviceToAbsoluteTracking.m[0][3];
        pose.vecPosition[1] = trackedPose.mDeviceToAbsoluteTracking.m[1][3];
        pose.vecPosition[2] = trackedPose.mDeviceToAbsoluteTracking.m[2][3];

        pose.vecVelocity[0] = trackedPose.vVelocity.v[0];
        pose.vecVelocity[1] = trackedPose.vVelocity.v[1];
        pose.vecVelocity[2] = trackedPose.vVelocity.v[2];
        pose.vecAngularVelocity[0] = trackedPose.vAngularVelocity.v[0];
        pose.vecAngularVelocity[1] = trackedPose.vAngularVelocity.v[1];
        pose.vecAngularVelocity[2] = trackedPose.vAngularVelocity.v[2];

        const double m00 = trackedPose.mDeviceToAbsoluteTracking.m[0][0];
        const double m01 = trackedPose.mDeviceToAbsoluteTracking.m[0][1];
        const double m02 = trackedPose.mDeviceToAbsoluteTracking.m[0][2];
        const double m10 = trackedPose.mDeviceToAbsoluteTracking.m[1][0];
        const double m11 = trackedPose.mDeviceToAbsoluteTracking.m[1][1];
        const double m12 = trackedPose.mDeviceToAbsoluteTracking.m[1][2];
        const double m20 = trackedPose.mDeviceToAbsoluteTracking.m[2][0];
        const double m21 = trackedPose.mDeviceToAbsoluteTracking.m[2][1];
        const double m22 = trackedPose.mDeviceToAbsoluteTracking.m[2][2];
        const double trace = m00 + m11 + m22;

        double qw = 1;
        double qx = 0;
        double qy = 0;
        double qz = 0;
        if (trace > 0)
        {
            const double s = 0.5 / std::sqrt(trace + 1.0);
            qw = 0.25 / s;
            qx = (m21 - m12) * s;
            qy = (m02 - m20) * s;
            qz = (m10 - m01) * s;
        }
        else if (m00 > m11 && m00 > m22)
        {
            const double s = 2.0 * std::sqrt(1.0 + m00 - m11 - m22);
            qw = (m21 - m12) / s;
            qx = 0.25 * s;
            qy = (m01 + m10) / s;
            qz = (m02 + m20) / s;
        }
        else if (m11 > m22)
        {
            const double s = 2.0 * std::sqrt(1.0 + m11 - m00 - m22);
            qw = (m02 - m20) / s;
            qx = (m01 + m10) / s;
            qy = 0.25 * s;
            qz = (m12 + m21) / s;
        }
        else
        {
            const double s = 2.0 * std::sqrt(1.0 + m22 - m00 - m11);
            qw = (m10 - m01) / s;
            qx = (m02 + m20) / s;
            qy = (m12 + m21) / s;
            qz = 0.25 * s;
        }

        NormalizeQuaternion(qw, qx, qy, qz);
        pose.qRotation.w = qw;
        pose.qRotation.x = qx;
        pose.qRotation.y = qy;
        pose.qRotation.z = qz;

        pose.vecAcceleration[0] = pose.vecAcceleration[1] = pose.vecAcceleration[2] = 0;
        pose.vecAngularAcceleration[0] = pose.vecAngularAcceleration[1] = pose.vecAngularAcceleration[2] = 0;
        pose.result = trackedPose.eTrackingResult;
        pose.poseIsValid = trackedPose.bPoseIsValid;
        pose.willDriftInYaw = false;
        pose.shouldApplyHeadModel = false;
        pose.deviceIsConnected = trackedPose.bDeviceIsConnected;
    }

    bool TryFindRealControllerPose(
        vr::ETrackedControllerRole targetRole,
        const vr::TrackedDevicePose_t* trackedPoses,
        uint32_t trackedPoseCount,
        vr::DriverPose_t& pose,
        vr::TrackedDeviceIndex_t* foundDeviceIndex = nullptr)
    {
        if (!trackedPoses || trackedPoseCount == 0)
        {
            return false;
        }

        for (uint32_t deviceIndex = 0; deviceIndex < trackedPoseCount; ++deviceIndex)
        {
            const auto& trackedPose = trackedPoses[deviceIndex];
            if (!trackedPose.bDeviceIsConnected || !trackedPose.bPoseIsValid)
            {
                continue;
            }

            if (IsVrchotasTrackedDevice(deviceIndex))
            {
                continue;
            }

            vr::ETrackedControllerRole role = vr::TrackedControllerRole_Invalid;
            if (!TryGetTrackedDeviceRole(deviceIndex, role) || role != targetRole)
            {
                continue;
            }

            FillDriverPoseFromTrackedPose(trackedPose, pose);
            if (foundDeviceIndex)
            {
                *foundDeviceIndex = static_cast<vr::TrackedDeviceIndex_t>(deviceIndex);
            }

            return true;
        }

        return false;
    }
}

class HotasControllerDevice final : public vr::ITrackedDeviceServerDriver
{
public:
    explicit HotasControllerDevice(vr::ETrackedControllerRole role)
        : _role(role),
          _serialNumber(role == vr::TrackedControllerRole_LeftHand ? "vrchotas_left" : "vrchotas_right")
    {
        _buttonHandles.fill(vr::k_ulInvalidInputComponentHandle);
        _axisHandles.fill(vr::k_ulInvalidInputComponentHandle);
        ResetCachedPose();
    }

    vr::EVRInitError Activate(vr::TrackedDeviceIndex_t unObjectId) override
    {
        _trackedDeviceIndex = unObjectId;
        DriverLogF("[vrchotas] Activate started for %s with object id %u.", _serialNumber, unObjectId);
        auto container = vr::VRProperties()->TrackedDeviceToPropertyContainer(unObjectId);
        _propertyContainer = container;
        vr::VRProperties()->SetStringProperty(container, vr::Prop_SerialNumber_String, _serialNumber);
        vr::VRProperties()->SetStringProperty(container, vr::Prop_TrackingSystemName_String, kTrackingSystemName);
        vr::VRProperties()->SetStringProperty(container, vr::Prop_ManufacturerName_String, kManufacturerName);
        vr::VRProperties()->SetStringProperty(container, vr::Prop_ModelNumber_String, "VRCHOTAS Virtual Controller");
        vr::VRProperties()->SetInt32Property(container, vr::Prop_ControllerRoleHint_Int32, static_cast<int32_t>(_role));
        vr::VRProperties()->SetStringProperty(container, vr::Prop_ControllerType_String, kControllerType);
        vr::VRProperties()->SetStringProperty(container, vr::Prop_InputProfilePath_String, kInputProfilePath);
        SetHandSelectionPriority(kMappedHandSelectionPriority, "activate");
        CreateInputComponents(container);
        ResetCachedPose();
        DriverLogF("[vrchotas] Activated tracked device %s (role=%s, profile=%s).", _serialNumber, RoleToString(_role), kInputProfilePath);
        return vr::VRInitError_None;
    }

    void Deactivate() override
    {
        DriverLogF("[vrchotas] Deactivate called for %s.", _serialNumber);
        _trackedDeviceIndex = vr::k_unTrackedDeviceIndexInvalid;
        _propertyContainer = vr::k_ulInvalidPropertyContainer;
    }

    void EnterStandby() override
    {
        DriverLogF("[vrchotas] EnterStandby called for %s.", _serialNumber);
    }

    void* GetComponent(const char* pchComponentNameAndVersion) override { return nullptr; }

    void DebugRequest(const char* pchRequest, char* pchResponseBuffer, uint32_t unResponseBufferSize) override
    {
        DriverLogF("[vrchotas] DebugRequest for %s: %s", _serialNumber, pchRequest ? pchRequest : "<null>");
        if (pchResponseBuffer && unResponseBufferSize > 0)
        {
            pchResponseBuffer[0] = '\0';
        }
    }

    vr::DriverPose_t GetPose() override { return _cachedDriverPose; }

    void SetHandSelectionPriority(int32_t priority, const char* reason)
    {
        if (_propertyContainer == vr::k_ulInvalidPropertyContainer)
        {
            return;
        }

        if (_controllerHandSelectionPriority == priority)
        {
            return;
        }

        vr::VRProperties()->SetInt32Property(_propertyContainer, vr::Prop_ControllerHandSelectionPriority_Int32, priority);
        _controllerHandSelectionPriority = priority;
        DriverLogF(
            "[vrchotas] Hand selection priority updated for %s: priority=%d reason=%s",
            _serialNumber,
            priority,
            reason ? reason : "<unspecified>");
    }

    void UpdateState(const vrchotas::ControllerHandState& hand, const vr::DriverPose_t* poseOverride = nullptr)
    {
        if (_trackedDeviceIndex == vr::k_unTrackedDeviceIndexInvalid)
        {
            return;
        }

        if (!_loggedFirstStateUpdate)
        {
            DriverLogF("[vrchotas] First state update received for %s.", _serialNumber);
            _loggedFirstStateUpdate = true;
        }

        if (!_loggedFirstActiveInput && HasMeaningfulInput(hand))
        {
            DriverLogF(
                "[vrchotas] First active input for %s: button0=%s axis0=%.3f pos=(%.3f, %.3f, %.3f)",
                _serialNumber,
                hand.buttons[0] ? "true" : "false",
                hand.axes[0],
                hand.position[0],
                hand.position[1],
                hand.position[2]);
            _loggedFirstActiveInput = true;
        }

        for (int i = 0; i < vrchotas::kButtonCount; ++i)
        {
            vr::VRDriverInput()->UpdateBooleanComponent(_buttonHandles[static_cast<size_t>(i)], hand.buttons[i], 0.0);
        }

        for (int i = 0; i < vrchotas::kAxisCount; ++i)
        {
            vr::VRDriverInput()->UpdateScalarComponent(
                _axisHandles[static_cast<size_t>(i)],
                static_cast<float>(hand.axes[i]),
                0.0);
        }

        if (poseOverride)
        {
            _cachedDriverPose = *poseOverride;
        }
        else
        {
            FillDriverPoseFromHand(hand, _cachedDriverPose);
        }

        vr::VRServerDriverHost()->TrackedDevicePoseUpdated(_trackedDeviceIndex, _cachedDriverPose, sizeof(vr::DriverPose_t));
    }

    void CreateInputComponents(vr::PropertyContainerHandle_t container)
    {
        char path[80]{};
        for (int i = 0; i < vrchotas::kButtonCount; ++i)
        {
            snprintf(path, sizeof(path), "/input/vrchotas/btn%02d/click", i);
            const auto error = vr::VRDriverInput()->CreateBooleanComponent(container, path, &_buttonHandles[static_cast<size_t>(i)]);
            if (error != vr::VRInputError_None)
            {
                DriverLogF("[vrchotas] CreateBooleanComponent failed for %s path=%s error=%d", _serialNumber, path, static_cast<int>(error));
            }
        }

        for (int i = 0; i < vrchotas::kAxisCount; ++i)
        {
            snprintf(path, sizeof(path), "/input/vrchotas/axis%02d/x", i);
            const auto error = vr::VRDriverInput()->CreateScalarComponent(
                container,
                path,
                &_axisHandles[static_cast<size_t>(i)],
                vr::VRScalarType_Absolute,
                vr::VRScalarUnits_NormalizedTwoSided);
            if (error != vr::VRInputError_None)
            {
                DriverLogF("[vrchotas] CreateScalarComponent failed for %s path=%s error=%d", _serialNumber, path, static_cast<int>(error));
            }
        }

        DriverLogF("[vrchotas] Input components created for %s.", _serialNumber);
    }

private:
    void ResetCachedPose()
    {
        _cachedDriverPose = {};
        _cachedDriverPose.poseTimeOffset = 0;
        _cachedDriverPose.qWorldFromDriverRotation.w = 1;
        _cachedDriverPose.qDriverFromHeadRotation.w = 1;
        _cachedDriverPose.qRotation.w = 1;
        _cachedDriverPose.result = vr::TrackingResult_Running_OK;
        _cachedDriverPose.poseIsValid = true;
        _cachedDriverPose.deviceIsConnected = true;
    }

    vr::ETrackedControllerRole _role;
    const char* _serialNumber;
    vr::TrackedDeviceIndex_t _trackedDeviceIndex{ vr::k_unTrackedDeviceIndexInvalid };
    vr::PropertyContainerHandle_t _propertyContainer{ vr::k_ulInvalidPropertyContainer };
    std::array<vr::VRInputComponentHandle_t, vrchotas::kButtonCount> _buttonHandles{};
    std::array<vr::VRInputComponentHandle_t, vrchotas::kAxisCount> _axisHandles{};
    vr::DriverPose_t _cachedDriverPose{};
    int32_t _controllerHandSelectionPriority{ 0 };
    bool _loggedFirstStateUpdate{ false };
    bool _loggedFirstActiveInput{ false };
};

class HotasServerDriver final : public vr::IServerTrackedDeviceProvider
{
public:
    vr::EVRInitError Init(vr::IVRDriverContext* pDriverContext) override
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

    void Cleanup() override
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

    const char* const* GetInterfaceVersions() override
    {
        return kCompatibleInterfaceVersions;
    }

    void RunFrame() override
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

            const int32_t handSelectionPriority = ShouldUseMirroredRealControllerPose(snapshot)
                ? kMirroredHandSelectionPriority
                : kMappedHandSelectionPriority;
            _left->SetHandSelectionPriority(handSelectionPriority, PoseSourceToString(snapshot.pose_source));
            _right->SetHandSelectionPriority(handSelectionPriority, PoseSourceToString(snapshot.pose_source));

            vr::DriverPose_t mirroredLeftPose{};
            vr::DriverPose_t mirroredRightPose{};
            const vr::DriverPose_t* leftPoseOverride = nullptr;
            const vr::DriverPose_t* rightPoseOverride = nullptr;
            vr::TrackedDeviceIndex_t realLeftDeviceIndex = vr::k_unTrackedDeviceIndexInvalid;
            vr::TrackedDeviceIndex_t realRightDeviceIndex = vr::k_unTrackedDeviceIndexInvalid;

            std::array<vr::TrackedDevicePose_t, vr::k_unMaxTrackedDeviceCount> trackedPoses{};
            const bool shouldSamplePoses = (GetTickCount64() - _lastPoseSampleTickMs) >= kPoseSampleIntervalMs;

            if (ShouldUseMirroredRealControllerPose(snapshot) || shouldSamplePoses)
            {
                vr::VRServerDriverHost()->GetRawTrackedDevicePoses(0.0f, trackedPoses.data(), static_cast<uint32_t>(trackedPoses.size()));
            }

            if (ShouldUseMirroredRealControllerPose(snapshot))
            {
                const auto leftFound = TryFindRealControllerPose(
                    vr::TrackedControllerRole_LeftHand,
                    trackedPoses.data(),
                    static_cast<uint32_t>(trackedPoses.size()),
                    mirroredLeftPose,
                    &realLeftDeviceIndex);
                const auto rightFound = TryFindRealControllerPose(
                    vr::TrackedControllerRole_RightHand,
                    trackedPoses.data(),
                    static_cast<uint32_t>(trackedPoses.size()),
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

            if (shouldSamplePoses)
            {
                vr::DriverPose_t sampledRealLeftPose{};
                vr::DriverPose_t sampledRealRightPose{};
                vr::TrackedDeviceIndex_t sampledLeftIndex = vr::k_unTrackedDeviceIndexInvalid;
                vr::TrackedDeviceIndex_t sampledRightIndex = vr::k_unTrackedDeviceIndexInvalid;
                const bool sampledLeftFound = TryFindRealControllerPose(
                    vr::TrackedControllerRole_LeftHand,
                    trackedPoses.data(),
                    static_cast<uint32_t>(trackedPoses.size()),
                    sampledRealLeftPose,
                    &sampledLeftIndex);
                const bool sampledRightFound = TryFindRealControllerPose(
                    vr::TrackedControllerRole_RightHand,
                    trackedPoses.data(),
                    static_cast<uint32_t>(trackedPoses.size()),
                    sampledRealRightPose,
                    &sampledRightIndex);

                const auto virtualLeftPose = _left->GetPose();
                const auto virtualRightPose = _right->GetPose();

                DriverLogF(
                    "[vrchotas] Pose sample (mode=%s): RealLeft(found=%s,index=%u,pos=%.3f,%.3f,%.3f,quat=%.4f,%.4f,%.4f,%.4f) | VirtualLeft(pos=%.3f,%.3f,%.3f,quat=%.4f,%.4f,%.4f,%.4f)",
                    PoseSourceToString(snapshot.pose_source),
                    sampledLeftFound ? "true" : "false",
                    sampledLeftFound ? sampledLeftIndex : vr::k_unTrackedDeviceIndexInvalid,
                    sampledLeftFound ? sampledRealLeftPose.vecPosition[0] : 0.0,
                    sampledLeftFound ? sampledRealLeftPose.vecPosition[1] : 0.0,
                    sampledLeftFound ? sampledRealLeftPose.vecPosition[2] : 0.0,
                    sampledLeftFound ? sampledRealLeftPose.qRotation.w : 1.0,
                    sampledLeftFound ? sampledRealLeftPose.qRotation.x : 0.0,
                    sampledLeftFound ? sampledRealLeftPose.qRotation.y : 0.0,
                    sampledLeftFound ? sampledRealLeftPose.qRotation.z : 0.0,
                    virtualLeftPose.vecPosition[0],
                    virtualLeftPose.vecPosition[1],
                    virtualLeftPose.vecPosition[2],
                    virtualLeftPose.qRotation.w,
                    virtualLeftPose.qRotation.x,
                    virtualLeftPose.qRotation.y,
                    virtualLeftPose.qRotation.z);

                DriverLogF(
                    "[vrchotas] Pose sample (mode=%s): RealRight(found=%s,index=%u,pos=%.3f,%.3f,%.3f,quat=%.4f,%.4f,%.4f,%.4f) | VirtualRight(pos=%.3f,%.3f,%.3f,quat=%.4f,%.4f,%.4f,%.4f)",
                    PoseSourceToString(snapshot.pose_source),
                    sampledRightFound ? "true" : "false",
                    sampledRightFound ? sampledRightIndex : vr::k_unTrackedDeviceIndexInvalid,
                    sampledRightFound ? sampledRealRightPose.vecPosition[0] : 0.0,
                    sampledRightFound ? sampledRealRightPose.vecPosition[1] : 0.0,
                    sampledRightFound ? sampledRealRightPose.vecPosition[2] : 0.0,
                    sampledRightFound ? sampledRealRightPose.qRotation.w : 1.0,
                    sampledRightFound ? sampledRealRightPose.qRotation.x : 0.0,
                    sampledRightFound ? sampledRealRightPose.qRotation.y : 0.0,
                    sampledRightFound ? sampledRealRightPose.qRotation.z : 0.0,
                    virtualRightPose.vecPosition[0],
                    virtualRightPose.vecPosition[1],
                    virtualRightPose.vecPosition[2],
                    virtualRightPose.qRotation.w,
                    virtualRightPose.qRotation.x,
                    virtualRightPose.qRotation.y,
                    virtualRightPose.qRotation.z);

                _lastPoseSampleTickMs = GetTickCount64();
            }

            const vrchotas::ControllerHandState neutralHand{};
            const auto& leftInput = ShouldUseMirroredRealControllerPose(snapshot) ? neutralHand : snapshot.left;
            const auto& rightInput = ShouldUseMirroredRealControllerPose(snapshot) ? neutralHand : snapshot.right;

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

    bool ShouldBlockStandbyMode() override
    {
        DriverLog("[vrchotas] ShouldBlockStandbyMode called.");
        return false;
    }

    void EnterStandby() override
    {
        DriverLog("[vrchotas] Server driver entering standby.");
    }

    void LeaveStandby() override
    {
        DriverLog("[vrchotas] Server driver leaving standby.");
    }

private:
    HANDLE _mapping{ nullptr };
    HANDLE _mutex{ nullptr };
    vrchotas::VirtualControllerState* _view{ nullptr };
    std::unique_ptr<HotasControllerDevice> _left;
    std::unique_ptr<HotasControllerDevice> _right;
    bool _loggedFirstRunFrame{ false };
    bool _loggedMissingRuntimeResources{ false };
    bool _loggedMutexWaitFailure{ false };
    bool _loggedButtonAxisMirrorLimitation{ false };
    bool _lastLeftRealControllerFound{ false };
    bool _lastRightRealControllerFound{ false };
    vrchotas::VirtualPoseSource _lastLoggedPoseSource{ vrchotas::VirtualPoseSource::Mapped };
    vr::TrackedDeviceIndex_t _lastLeftRealControllerIndex{ vr::k_unTrackedDeviceIndexInvalid };
    vr::TrackedDeviceIndex_t _lastRightRealControllerIndex{ vr::k_unTrackedDeviceIndexInvalid };
    ULONGLONG _lastPoseSampleTickMs{ 0 };
    unsigned long _consecutiveMutexWaitFailures{ 0 };
};

class HotasWatchdogDriver final : public vr::IVRWatchdogProvider
{
public:
    vr::EVRInitError Init(vr::IVRDriverContext* pDriverContext) override
    {
        VR_INIT_WATCHDOG_DRIVER_CONTEXT(pDriverContext);
        DriverLog("[vrchotas] Watchdog driver initialized.");
        return vr::VRInitError_None;
    }

    void Cleanup() override
    {
        DriverLog("[vrchotas] Watchdog driver cleanup called.");
        VR_CLEANUP_WATCHDOG_DRIVER_CONTEXT();
    }
};

static HotasServerDriver g_serverDriver;
static HotasWatchdogDriver g_watchdogDriver;

extern "C" __declspec(dllexport) void* HmdDriverFactory(const char* pInterfaceName, int* pReturnCode)
{
    DriverLogF("[vrchotas] HmdDriverFactory requested interface: %s", pInterfaceName ? pInterfaceName : "<null>");

    if (0 == strcmp(vr::IServerTrackedDeviceProvider_Version, pInterfaceName)
        || 0 == strcmp(kServerTrackedDeviceProviderVersion004, pInterfaceName))
    {
        if (pReturnCode)
        {
            *pReturnCode = vr::VRInitError_None;
        }

        DriverLog("[vrchotas] Returning server driver interface.");
        return &g_serverDriver;
    }

    if (0 == strcmp(vr::IVRWatchdogProvider_Version, pInterfaceName))
    {
        if (pReturnCode)
        {
            *pReturnCode = vr::VRInitError_None;
        }

        DriverLog("[vrchotas] Returning watchdog driver interface.");
        return &g_watchdogDriver;
    }

    if (pReturnCode)
    {
        *pReturnCode = vr::VRInitError_Init_InterfaceNotFound;
    }

    DriverLogF("[vrchotas] Unsupported interface requested: %s", pInterfaceName ? pInterfaceName : "<null>");

    return nullptr;
}
