#pragma once

#include <cstdint>
#include <string>
#include <openvr_driver.h>
#include "virtual_controller_state.h"

const char* RoleToString(vr::ETrackedControllerRole role);

bool HasMeaningfulInput(const vrchotas::ControllerHandState& hand);

bool ShouldUseMirroredRealControllerPose(const vrchotas::VirtualControllerState& state);

const char* PoseSourceToString(vrchotas::VirtualPoseSource poseSource);

bool TryGetTrackedDeviceStringProperty(vr::TrackedDeviceIndex_t deviceIndex, vr::ETrackedDeviceProperty property, std::string& value);

bool TryGetTrackedDeviceRole(vr::TrackedDeviceIndex_t deviceIndex, vr::ETrackedControllerRole& role);

bool IsVrchotasTrackedDevice(vr::TrackedDeviceIndex_t deviceIndex);

void NormalizeQuaternion(double& w, double& x, double& y, double& z);

void FillDriverPoseFromHand(const vrchotas::ControllerHandState& hand, vr::DriverPose_t& pose);

void FillDriverPoseFromTrackedPose(const vr::TrackedDevicePose_t& trackedPose, vr::DriverPose_t& pose);

bool TryFindRealControllerPose(
    vr::ETrackedControllerRole targetRole,
    const vr::TrackedDevicePose_t* trackedPoses,
    std::uint32_t trackedPoseCount,
    vr::DriverPose_t& pose,
    vr::TrackedDeviceIndex_t* foundDeviceIndex = nullptr);
