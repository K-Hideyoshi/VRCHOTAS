#include <cmath>
#include <cstring>
#include <string>
#include <openvr_driver.h>
#include "driver_constants.h"
#include "driver_openvr_helpers.h"

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
        && trackingSystemName == vrchotas::driver::kTrackingSystemName)
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
    std::uint32_t trackedPoseCount,
    vr::DriverPose_t& pose,
    vr::TrackedDeviceIndex_t* foundDeviceIndex)
{
    if (!trackedPoses || trackedPoseCount == 0)
    {
        return false;
    }

    for (std::uint32_t deviceIndex = 0; deviceIndex < trackedPoseCount; ++deviceIndex)
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
