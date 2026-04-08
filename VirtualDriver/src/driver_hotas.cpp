#include <Windows.h>
#include <array>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <memory>
#include <openvr_driver.h>
#include "virtual_controller_state.h"

namespace
{
    constexpr const char* kControllerType = "vrchotas_virtual";
    constexpr const char* kInputProfilePath = "vrchotas/input/vrchotas_virtual_profile.json";

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
}

class HotasControllerDevice final : public vr::ITrackedDeviceServerDriver
{
public:
    explicit HotasControllerDevice(vr::ETrackedControllerRole role)
        : _role(role)
    {
        _buttonHandles.fill(vr::k_ulInvalidInputComponentHandle);
        _axisHandles.fill(vr::k_ulInvalidInputComponentHandle);
        ResetCachedPose();
    }

    vr::EVRInitError Activate(vr::TrackedDeviceIndex_t unObjectId) override
    {
        _trackedDeviceIndex = unObjectId;
        auto container = vr::VRProperties()->TrackedDeviceToPropertyContainer(unObjectId);
        vr::VRProperties()->SetStringProperty(container, vr::Prop_ModelNumber_String, "VRCHOTAS Virtual Controller");
        vr::VRProperties()->SetInt32Property(container, vr::Prop_ControllerRoleHint_Int32, static_cast<int32_t>(_role));
        vr::VRProperties()->SetStringProperty(container, vr::Prop_ControllerType_String, kControllerType);
        vr::VRProperties()->SetStringProperty(container, vr::Prop_InputProfilePath_String, kInputProfilePath);
        CreateInputComponents(container);
        ResetCachedPose();
        return vr::VRInitError_None;
    }

    void Deactivate() override
    {
        _trackedDeviceIndex = vr::k_unTrackedDeviceIndexInvalid;
    }

    void EnterStandby() override {}
    void* GetComponent(const char* pchComponentNameAndVersion) override { return nullptr; }
    void PowerOff() override {}
    void DebugRequest(const char* pchRequest, char* pchResponseBuffer, uint32_t unResponseBufferSize) override {}
    vr::DriverPose_t GetPose() override { return _cachedDriverPose; }

    void UpdateState(const vrchotas::ControllerHandState& hand)
    {
        if (_trackedDeviceIndex == vr::k_unTrackedDeviceIndexInvalid)
        {
            return;
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

        FillDriverPoseFromHand(hand, _cachedDriverPose);
        vr::VRServerDriverHost()->TrackedDevicePoseUpdated(_trackedDeviceIndex, _cachedDriverPose, sizeof(vr::DriverPose_t));
    }

    void CreateInputComponents(vr::PropertyContainerHandle_t container)
    {
        char path[80]{};
        for (int i = 0; i < vrchotas::kButtonCount; ++i)
        {
            snprintf(path, sizeof(path), "/input/vrchotas/btn%02d/click", i);
            vr::VRDriverInput()->CreateBooleanComponent(container, path, &_buttonHandles[static_cast<size_t>(i)]);
        }

        for (int i = 0; i < vrchotas::kAxisCount; ++i)
        {
            snprintf(path, sizeof(path), "/input/vrchotas/axis%02d/x", i);
            vr::VRDriverInput()->CreateScalarComponent(
                container,
                path,
                &_axisHandles[static_cast<size_t>(i)],
                vr::VRScalarType_Absolute,
                vr::VRScalarUnits_NormalizedTwoSided);
        }
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
    vr::TrackedDeviceIndex_t _trackedDeviceIndex{ vr::k_unTrackedDeviceIndexInvalid };
    std::array<vr::VRInputComponentHandle_t, vrchotas::kButtonCount> _buttonHandles{};
    std::array<vr::VRInputComponentHandle_t, vrchotas::kAxisCount> _axisHandles{};
    vr::DriverPose_t _cachedDriverPose{};
};

class HotasServerDriver final : public vr::IServerTrackedDeviceProvider
{
public:
    vr::EVRInitError Init(vr::IVRDriverContext* pDriverContext) override
    {
        VR_INIT_SERVER_DRIVER_CONTEXT(pDriverContext);

        _mapping = CreateFileMappingW(
            INVALID_HANDLE_VALUE,
            nullptr,
            PAGE_READWRITE,
            0,
            sizeof(vrchotas::VirtualControllerState),
            vrchotas::kSharedMemoryName);
        _mutex = CreateMutexW(nullptr, FALSE, vrchotas::kSharedMemoryMutexName);
        _view = static_cast<vrchotas::VirtualControllerState*>(
            MapViewOfFile(_mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(vrchotas::VirtualControllerState)));

        _left = std::make_unique<HotasControllerDevice>(vr::TrackedControllerRole_LeftHand);
        _right = std::make_unique<HotasControllerDevice>(vr::TrackedControllerRole_RightHand);

        vr::VRServerDriverHost()->TrackedDeviceAdded("vrchotas_left", vr::TrackedDeviceClass_Controller, _left.get());
        vr::VRServerDriverHost()->TrackedDeviceAdded("vrchotas_right", vr::TrackedDeviceClass_Controller, _right.get());

        return vr::VRInitError_None;
    }

    void Cleanup() override
    {
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
    }

    const char* const* GetInterfaceVersions() override
    {
        return vr::k_InterfaceVersions;
    }

    void RunFrame() override
    {
        if (!_view || !_left || !_right || !_mutex)
        {
            return;
        }

        if (WaitForSingleObject(_mutex, 1) == WAIT_OBJECT_0)
        {
            const auto snapshot = *_view;
            ReleaseMutex(_mutex);

            _left->UpdateState(snapshot.left);
            _right->UpdateState(snapshot.right);
        }
    }

    bool ShouldBlockStandbyMode() override { return false; }
    void EnterStandby() override {}
    void LeaveStandby() override {}

private:
    HANDLE _mapping{ nullptr };
    HANDLE _mutex{ nullptr };
    vrchotas::VirtualControllerState* _view{ nullptr };
    std::unique_ptr<HotasControllerDevice> _left;
    std::unique_ptr<HotasControllerDevice> _right;
};

static HotasServerDriver g_serverDriver;

extern "C" __declspec(dllexport) void* HmdDriverFactory(const char* pInterfaceName, int* pReturnCode)
{
    if (0 == strcmp(vr::IServerTrackedDeviceProvider_Version, pInterfaceName))
    {
        return &g_serverDriver;
    }

    if (pReturnCode)
    {
        *pReturnCode = vr::VRInitError_Init_InterfaceNotFound;
    }

    return nullptr;
}
