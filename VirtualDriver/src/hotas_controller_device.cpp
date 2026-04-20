#include <cstdio>
#include <cstring>
#include <openvr_driver.h>
#include "driver_constants.h"
#include "driver_logging.h"
#include "driver_openvr_helpers.h"
#include "hotas_controller_device.h"

HotasControllerDevice::HotasControllerDevice(vr::ETrackedControllerRole role)
    : _role(role),
      _serialNumber(role == vr::TrackedControllerRole_LeftHand ? "vrchotas_left" : "vrchotas_right")
{
    _buttonHandles.fill(vr::k_ulInvalidInputComponentHandle);
    _axisHandles.fill(vr::k_ulInvalidInputComponentHandle);
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

void HotasControllerDevice::CreateInputComponents(vr::PropertyContainerHandle_t container)
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
