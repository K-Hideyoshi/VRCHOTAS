#pragma once

#include <cstdint>

namespace vrchotas::driver
{
inline constexpr unsigned long long kPoseSampleIntervalMs = 5000;
inline constexpr std::int32_t kMappedHandSelectionPriority = 1000;
inline constexpr std::int32_t kMirroredHandSelectionPriority = -1000;
inline constexpr const char kControllerType[] = "vrchotas_virtual";
inline constexpr const char kInputProfilePath[] = "vrchotas/input/vrchotas_virtual_profile.json";
inline constexpr const char kTrackingSystemName[] = "vrchotas";
inline constexpr const char kManufacturerName[] = "VRCHOTAS";
inline constexpr const char kServerTrackedDeviceProviderVersion004[] = "IServerTrackedDeviceProvider_004";
}
