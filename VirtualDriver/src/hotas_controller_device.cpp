#include <cstdio>
#include <cstring>
#include <cmath>
#include <openvr_driver.h>
#include "driver_constants.h"
#include "driver_logging.h"
#include "driver_openvr_helpers.h"
#include "hotas_controller_device.h"

namespace
{
    constexpr double kSemanticAxisLogThreshold = 0.01;
    constexpr double kSemanticAxisChangeThreshold = 0.02;

    uint64_t BuildSupportedButtonsMask(vr::ETrackedControllerRole role)
    {
        uint64_t mask = 0;
        mask |= vr::ButtonMaskFromId(vr::k_EButton_Grip);
        mask |= vr::ButtonMaskFromId(vr::k_EButton_Axis0);
        mask |= vr::ButtonMaskFromId(vr::k_EButton_Axis1);
        mask |= vr::ButtonMaskFromId(vr::k_EButton_Axis2);

        if (role == vr::TrackedControllerRole_RightHand)
        {
            mask |= vr::ButtonMaskFromId(vr::k_EButton_A);
        }
        else
        {
            mask |= vr::ButtonMaskFromId(vr::k_EButton_ApplicationMenu);
            mask |= vr::ButtonMaskFromId(vr::k_EButton_Grip);
        }

        return mask;
    }

    bool ShouldLogAxisChange(double previousValue, double currentValue)
    {
        const bool crossedActiveBoundary =
            (std::abs(previousValue) <= kSemanticAxisLogThreshold) !=
            (std::abs(currentValue) <= kSemanticAxisLogThreshold);

        return crossedActiveBoundary || std::abs(previousValue - currentValue) >= kSemanticAxisChangeThreshold;
    }
}

HotasControllerDevice::HotasControllerDevice(vr::ETrackedControllerRole role)
    : _role(role),
      _serialNumber(role == vr::TrackedControllerRole_LeftHand ? "vrchotas_left" : "vrchotas_right")
{
    _buttonHandles.fill(vr::k_ulInvalidInputComponentHandle);
    _axisHandles.fill(vr::k_ulInvalidInputComponentHandle);
    _thumbstickAxisAliasHandles.fill(vr::k_ulInvalidInputComponentHandle);
    _touchAliasHandles.fill(vr::k_ulInvalidInputComponentHandle);
    ResetCachedPose();
}

vr::EVRInitError HotasControllerDevice::Activate(vr::TrackedDeviceIndex_t unObjectId)
{
    _trackedDeviceIndex = unObjectId;
    DriverLogF("[vrchotas] Activate started for %s with object id %u.", _serialNumber, unObjectId);
    auto container = vr::VRProperties()->TrackedDeviceToPropertyContainer(unObjectId);
    _propertyContainer = container;
    vr::VRProperties()->SetStringProperty(container, vr::Prop_SerialNumber_String, _serialNumber);
    vr::VRProperties()->SetStringProperty(container, vr::Prop_TrackingSystemName_String, vrchotas::driver::kTrackingSystemName);
    vr::VRProperties()->SetStringProperty(container, vr::Prop_ManufacturerName_String, vrchotas::driver::kManufacturerName);
    vr::VRProperties()->SetStringProperty(container, vr::Prop_ModelNumber_String, "VRCHOTAS Virtual Controller");
    vr::VRProperties()->SetInt32Property(container, vr::Prop_ControllerRoleHint_Int32, static_cast<std::int32_t>(_role));
    vr::VRProperties()->SetStringProperty(container, vr::Prop_ControllerType_String, vrchotas::driver::kControllerType);
    vr::VRProperties()->SetStringProperty(container, vr::Prop_InputProfilePath_String, vrchotas::driver::kInputProfilePath);
    vr::VRProperties()->SetBoolProperty(container, vr::Prop_HasControllerComponent_Bool, true);
    vr::VRProperties()->SetUint64Property(container, vr::Prop_SupportedButtons_Uint64, BuildSupportedButtonsMask(_role));
    vr::VRProperties()->SetInt32Property(container, vr::Prop_Axis0Type_Int32, vr::k_eControllerAxis_Joystick);
    vr::VRProperties()->SetInt32Property(container, vr::Prop_Axis1Type_Int32, vr::k_eControllerAxis_Trigger);
    vr::VRProperties()->SetInt32Property(container, vr::Prop_Axis2Type_Int32, vr::k_eControllerAxis_Trigger);
    vr::VRProperties()->SetInt32Property(container, vr::Prop_Axis3Type_Int32, vr::k_eControllerAxis_None);
    vr::VRProperties()->SetInt32Property(container, vr::Prop_Axis4Type_Int32, vr::k_eControllerAxis_None);
    DriverLogF(
        "[vrchotas] Controller capability properties for %s: supportedButtons=0x%llX axisTypes=[%d,%d,%d,%d,%d] role=%d controllerType=%s",
        _serialNumber,
        BuildSupportedButtonsMask(_role),
        vr::k_eControllerAxis_Joystick,
        vr::k_eControllerAxis_Trigger,
        vr::k_eControllerAxis_Trigger,
        vr::k_eControllerAxis_None,
        vr::k_eControllerAxis_None,
        static_cast<int>(_role),
        vrchotas::driver::kControllerType);
    SetHandSelectionPriority(vrchotas::driver::kMappedHandSelectionPriority, "activate");
    CreateInputComponents(container);
    ResetCachedPose();
    DriverLogF("[vrchotas] Activated tracked device %s (role=%s, profile=%s).", _serialNumber, RoleToString(_role), vrchotas::driver::kInputProfilePath);
    return vr::VRInitError_None;
}

void HotasControllerDevice::Deactivate()
{
    DriverLogF("[vrchotas] Deactivate called for %s.", _serialNumber);
    _trackedDeviceIndex = vr::k_unTrackedDeviceIndexInvalid;
    _propertyContainer = vr::k_ulInvalidPropertyContainer;
}

void HotasControllerDevice::EnterStandby()
{
    DriverLogF("[vrchotas] EnterStandby called for %s.", _serialNumber);
}

void* HotasControllerDevice::GetComponent(const char* pchComponentNameAndVersion)
{
    (void)pchComponentNameAndVersion;
    return nullptr;
}

void HotasControllerDevice::DebugRequest(const char* pchRequest, char* pchResponseBuffer, uint32_t unResponseBufferSize)
{
    DriverLogF("[vrchotas] DebugRequest for %s: %s", _serialNumber, pchRequest ? pchRequest : "<null>");
    if (pchResponseBuffer && unResponseBufferSize > 0)
    {
        pchResponseBuffer[0] = '\0';
    }
}

vr::DriverPose_t HotasControllerDevice::GetPose()
{
    return _cachedDriverPose;
}

void HotasControllerDevice::SetHandSelectionPriority(std::int32_t priority, const char* reason)
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

void HotasControllerDevice::UpdateState(const vrchotas::ControllerHandState& hand, const vr::DriverPose_t* poseOverride)
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

    for (size_t i = 0; i < _buttonHandles.size(); ++i)
    {
        if (!_hasLoggedSemanticInputs || _lastLoggedButtons[i] != hand.buttons[i])
        {
            DriverLogF(
                "[vrchotas] Semantic button update for %s: slot=%zu value=%s",
                _serialNumber,
                i,
                hand.buttons[i] ? "true" : "false");
            _lastLoggedButtons[i] = hand.buttons[i];
        }
    }

    for (size_t i = 0; i < _axisHandles.size(); ++i)
    {
        if (!_hasLoggedSemanticInputs || ShouldLogAxisChange(_lastLoggedAxes[i], hand.axes[i]))
        {
            DriverLogF(
                "[vrchotas] Semantic axis update for %s: slot=%zu value=%.3f",
                _serialNumber,
                i,
                hand.axes[i]);
            _lastLoggedAxes[i] = hand.axes[i];
        }
    }

    _hasLoggedSemanticInputs = true;

    for (int i = 0; i < vrchotas::kButtonCount; ++i)
    {
        if (i >= static_cast<int>(_buttonHandles.size()))
        {
            break;
        }

        vr::VRDriverInput()->UpdateBooleanComponent(_buttonHandles[static_cast<size_t>(i)], hand.buttons[i], 0.0);
    }

    if (_thumbstickClickAliasHandle != vr::k_ulInvalidInputComponentHandle)
    {
        vr::VRDriverInput()->UpdateBooleanComponent(_thumbstickClickAliasHandle, hand.buttons[vrchotas::kThumbstickClickButton], 0.0);
    }

    for (size_t i = 0; i < _touchAliasHandles.size(); ++i)
    {
        if (_touchAliasHandles[i] == vr::k_ulInvalidInputComponentHandle)
        {
            continue;
        }

        const auto buttonIndex = i == 0
            ? vrchotas::kThumbstickTouchButton
            : (i == 1 ? vrchotas::kTriggerTouchButton : vrchotas::kGripTouchButton);

        vr::VRDriverInput()->UpdateBooleanComponent(_touchAliasHandles[i], hand.buttons[buttonIndex], 0.0);
    }

    for (int i = 0; i < vrchotas::kAxisCount; ++i)
    {
        if (i >= static_cast<int>(_axisHandles.size()))
        {
            break;
        }

        vr::VRDriverInput()->UpdateScalarComponent(
            _axisHandles[static_cast<size_t>(i)],
            static_cast<float>(hand.axes[i]),
            0.0);
    }

    for (int i = 0; i < static_cast<int>(_thumbstickAxisAliasHandles.size()); ++i)
    {
        if (_thumbstickAxisAliasHandles[static_cast<size_t>(i)] == vr::k_ulInvalidInputComponentHandle)
        {
            continue;
        }

        vr::VRDriverInput()->UpdateScalarComponent(
            _thumbstickAxisAliasHandles[static_cast<size_t>(i)],
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

void HotasControllerDevice::CreateInputComponents(vr::PropertyContainerHandle_t container)
{
    struct BooleanPathBinding
    {
        size_t index;
        const char* path;
    };

    struct ScalarPathBinding
    {
        size_t index;
        const char* path;
        vr::EVRScalarUnits units;
    };

    const auto primaryFaceButtonPath = _role == vr::TrackedControllerRole_LeftHand ? "/input/x/click" : "/input/a/click";
    const auto secondaryFaceButtonPath = _role == vr::TrackedControllerRole_LeftHand ? "/input/y/click" : "/input/b/click";
    const auto systemButtonPath = "/input/system/click";

    const std::array<BooleanPathBinding, kSemanticButtonCount> buttonBindings{ {
        { vrchotas::kThumbstickClickButton, "/input/joystick/click" },
        { vrchotas::kPrimaryFaceButton, primaryFaceButtonPath },
        { vrchotas::kSecondaryFaceButton, secondaryFaceButtonPath },
        { vrchotas::kSystemButton, systemButtonPath },
        { vrchotas::kThumbstickTouchButton, "/input/joystick/touch" },
        { vrchotas::kTriggerTouchButton, "/input/trigger/touch" },
        { vrchotas::kTriggerClickButton, "/input/trigger/click" },
        { vrchotas::kGripTouchButton, "/input/grip/touch" },
        { vrchotas::kGripClickButton, "/input/grip/click" }
    } };

    const std::array<BooleanPathBinding, 1> thumbstickButtonAliasBindings{ {
        { vrchotas::kThumbstickClickButton, "/input/thumbstick/click" }
    } };

    const std::array<BooleanPathBinding, kTouchAliasButtonCount> touchAliasBindings{ {
        { vrchotas::kThumbstickTouchButton, "/input/thumbstick/touch" },
        { vrchotas::kTriggerTouchButton, "/input/trigger/touch" },
        { vrchotas::kGripTouchButton, "/input/grip/touch" }
    } };

    constexpr std::array<ScalarPathBinding, kSemanticAxisCount> axisBindings{ {
        { vrchotas::kThumbstickXAxis, "/input/joystick/x", vr::VRScalarUnits_NormalizedTwoSided },
        { vrchotas::kThumbstickYAxis, "/input/joystick/y", vr::VRScalarUnits_NormalizedTwoSided },
        { vrchotas::kTriggerAxis, "/input/trigger/value", vr::VRScalarUnits_NormalizedOneSided },
        { vrchotas::kGripAxis, "/input/grip/value", vr::VRScalarUnits_NormalizedOneSided }
    } };

    constexpr std::array<ScalarPathBinding, kThumbstickAliasAxisCount> thumbstickAxisAliasBindings{ {
        { vrchotas::kThumbstickXAxis, "/input/thumbstick/x", vr::VRScalarUnits_NormalizedTwoSided },
        { vrchotas::kThumbstickYAxis, "/input/thumbstick/y", vr::VRScalarUnits_NormalizedTwoSided }
    } };

    for (const auto& binding : buttonBindings)
    {
        const auto error = vr::VRDriverInput()->CreateBooleanComponent(container, binding.path, &_buttonHandles[binding.index]);
        if (error != vr::VRInputError_None)
        {
            DriverLogF("[vrchotas] CreateBooleanComponent failed for %s path=%s error=%d", _serialNumber, binding.path, static_cast<int>(error));
        }
        else
        {
            DriverLogF("[vrchotas] CreateBooleanComponent succeeded for %s path=%s handle=%llu", _serialNumber, binding.path, _buttonHandles[binding.index]);
        }
    }

    for (size_t i = 0; i < touchAliasBindings.size(); ++i)
    {
        const auto& binding = touchAliasBindings[i];
        const auto error = vr::VRDriverInput()->CreateBooleanComponent(container, binding.path, &_touchAliasHandles[i]);
        if (error != vr::VRInputError_None)
        {
            DriverLogF("[vrchotas] CreateBooleanComponent alias failed for %s path=%s error=%d", _serialNumber, binding.path, static_cast<int>(error));
        }
        else
        {
            DriverLogF("[vrchotas] CreateBooleanComponent alias succeeded for %s path=%s handle=%llu", _serialNumber, binding.path, _touchAliasHandles[i]);
        }
    }

    for (const auto& binding : thumbstickButtonAliasBindings)
    {
        const auto error = vr::VRDriverInput()->CreateBooleanComponent(container, binding.path, &_thumbstickClickAliasHandle);
        if (error != vr::VRInputError_None)
        {
            DriverLogF("[vrchotas] CreateBooleanComponent alias failed for %s path=%s error=%d", _serialNumber, binding.path, static_cast<int>(error));
        }
        else
        {
            DriverLogF("[vrchotas] CreateBooleanComponent alias succeeded for %s path=%s handle=%llu", _serialNumber, binding.path, _thumbstickClickAliasHandle);
        }
    }

    for (const auto& binding : axisBindings)
    {
        const auto error = vr::VRDriverInput()->CreateScalarComponent(
            container,
            binding.path,
            &_axisHandles[binding.index],
            vr::VRScalarType_Absolute,
            binding.units);
        if (error != vr::VRInputError_None)
        {
            DriverLogF("[vrchotas] CreateScalarComponent failed for %s path=%s error=%d", _serialNumber, binding.path, static_cast<int>(error));
        }
        else
        {
            DriverLogF("[vrchotas] CreateScalarComponent succeeded for %s path=%s handle=%llu", _serialNumber, binding.path, _axisHandles[binding.index]);
        }
    }

    for (const auto& binding : thumbstickAxisAliasBindings)
    {
        const auto error = vr::VRDriverInput()->CreateScalarComponent(
            container,
            binding.path,
            &_thumbstickAxisAliasHandles[binding.index],
            vr::VRScalarType_Absolute,
            binding.units);
        if (error != vr::VRInputError_None)
        {
            DriverLogF("[vrchotas] CreateScalarComponent alias failed for %s path=%s error=%d", _serialNumber, binding.path, static_cast<int>(error));
        }
        else
        {
            DriverLogF("[vrchotas] CreateScalarComponent alias succeeded for %s path=%s handle=%llu", _serialNumber, binding.path, _thumbstickAxisAliasHandles[binding.index]);
        }
    }

    DriverLogF("[vrchotas] Input components created for %s.", _serialNumber);
}

void HotasControllerDevice::ResetCachedPose()
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
