#pragma once

#include <cstdint>

namespace vrchotas
{
    inline constexpr int kButtonCount = 32;
    inline constexpr int kAxisCount = 16;
    inline constexpr int kVec3 = 3;
    inline constexpr int kQuat = 4;

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
        ControllerHandState left;
        ControllerHandState right;
    };
#pragma pack(pop)

    inline constexpr const wchar_t* kSharedMemoryName = L"Local\\VRCHOTAS.VirtualController.State";
    inline constexpr const wchar_t* kSharedMemoryMutexName = L"Local\\VRCHOTAS.VirtualController.State.Mutex";
}
