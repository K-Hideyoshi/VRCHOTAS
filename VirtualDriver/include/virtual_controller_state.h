#pragma once

#include <cstdint>

namespace vrchotas
{
    inline constexpr int kButtonCount = 32;
    inline constexpr int kAxisCount = 16;
    inline constexpr int kVec3 = 3;
    inline constexpr int kQuat = 4;
    inline constexpr int kThumbstickClickButton = 0;
    inline constexpr int kPrimaryFaceButton = 1;
    inline constexpr int kSecondaryFaceButton = 2;
    inline constexpr int kSystemButton = 3;
    inline constexpr int kThumbstickTouchButton = 4;
    inline constexpr int kTriggerTouchButton = 5;
    inline constexpr int kTriggerClickButton = 6;
    inline constexpr int kGripTouchButton = 7;
    inline constexpr int kGripClickButton = 8;
    inline constexpr int kThumbstickXAxis = 0;
    inline constexpr int kThumbstickYAxis = 1;
    inline constexpr int kTriggerAxis = 2;
    inline constexpr int kGripAxis = 3;

    enum class VirtualPoseSource : std::uint8_t
    {
        Mapped = 0,
        MirrorRealControllers = 1
    };

#pragma pack(push, 1)
    struct ControllerHandState
    {
        bool buttons[kButtonCount];
        double axes[kAxisCount];
        // OpenVR driver space: +X right, +Y up, -Z forward (meters)
        double position[kVec3];
        // Quaternion w,x,y,z (matches vr::HmdQuaternion_t)
        double quaternion[kQuat];
        double linear_velocity[kVec3];
        // rad/s about driver X, Y, Z (component-wise angular velocity)
        double angular_velocity[kVec3];
    };

    struct VirtualControllerState
    {
        VirtualPoseSource pose_source;
        ControllerHandState left;
        ControllerHandState right;
        // Written by OpenVR driver each RunFrame while holding the shared mutex (GetTickCount64 ms).
        std::uint64_t driver_heartbeat_tick_ms;
    };
#pragma pack(pop)

    inline constexpr const wchar_t* kSharedMemoryName = L"Local\\VRCHOTAS.VirtualController.State";
    inline constexpr const wchar_t* kSharedMemoryMutexName = L"Local\\VRCHOTAS.VirtualController.State.Mutex";
}
