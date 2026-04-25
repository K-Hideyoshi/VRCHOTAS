#pragma once

#include <array>
#include <cstdint>
#include <openvr_driver.h>
#include "virtual_controller_state.h"

class HotasControllerDevice final : public vr::ITrackedDeviceServerDriver
{
public:
    explicit HotasControllerDevice(vr::ETrackedControllerRole role);

    vr::EVRInitError Activate(vr::TrackedDeviceIndex_t unObjectId) override;
    void Deactivate() override;
    void EnterStandby() override;
    void* GetComponent(const char* pchComponentNameAndVersion) override;
    void DebugRequest(const char* pchRequest, char* pchResponseBuffer, uint32_t unResponseBufferSize) override;
    vr::DriverPose_t GetPose() override;

    void SetHandSelectionPriority(std::int32_t priority, const char* reason);
    void UpdateState(const vrchotas::ControllerHandState& hand, const vr::DriverPose_t* poseOverride = nullptr);

private:
    static constexpr size_t kSemanticButtonCount = 9;
    static constexpr size_t kSemanticAxisCount = 4;
    static constexpr size_t kThumbstickAliasAxisCount = 2;
    static constexpr size_t kTouchAliasButtonCount = 3;
    void CreateInputComponents(vr::PropertyContainerHandle_t container);
    void ResetCachedPose();

    vr::ETrackedControllerRole _role;
    const char* _serialNumber;
    vr::TrackedDeviceIndex_t _trackedDeviceIndex{ vr::k_unTrackedDeviceIndexInvalid };
    vr::PropertyContainerHandle_t _propertyContainer{ vr::k_ulInvalidPropertyContainer };
    std::array<vr::VRInputComponentHandle_t, kSemanticButtonCount> _buttonHandles{};
    std::array<vr::VRInputComponentHandle_t, kSemanticAxisCount> _axisHandles{};
    vr::VRInputComponentHandle_t _thumbstickClickAliasHandle{ vr::k_ulInvalidInputComponentHandle };
    std::array<vr::VRInputComponentHandle_t, kThumbstickAliasAxisCount> _thumbstickAxisAliasHandles{};
    std::array<vr::VRInputComponentHandle_t, kTouchAliasButtonCount> _touchAliasHandles{};
    std::array<bool, kSemanticButtonCount> _lastLoggedButtons{};
    std::array<double, kSemanticAxisCount> _lastLoggedAxes{};
    vr::DriverPose_t _cachedDriverPose{};
    std::int32_t _controllerHandSelectionPriority{ 0 };
    bool _loggedFirstStateUpdate{ false };
    bool _loggedFirstActiveInput{ false };
    bool _hasLoggedSemanticInputs{ false };
};
