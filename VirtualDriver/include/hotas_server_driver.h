#pragma once

#include <Windows.h>
#include <memory>
#include <openvr_driver.h>
#include "hotas_controller_device.h"

class HotasServerDriver final : public vr::IServerTrackedDeviceProvider
{
public:
    vr::EVRInitError Init(vr::IVRDriverContext* pDriverContext) override;
    void Cleanup() override;
    const char* const* GetInterfaceVersions() override;
    void RunFrame() override;
    bool ShouldBlockStandbyMode() override;
    void EnterStandby() override;
    void LeaveStandby() override;

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
    unsigned long long _lastPoseSampleTickMs{ 0 };
    unsigned long _consecutiveMutexWaitFailures{ 0 };
};
