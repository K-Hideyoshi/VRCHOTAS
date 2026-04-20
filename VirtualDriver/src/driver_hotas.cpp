#include <cstring>
#include <openvr_driver.h>
#include "driver_constants.h"
#include "driver_logging.h"
#include "hotas_server_driver.h"
#include "hotas_watchdog_driver.h"

static HotasServerDriver g_serverDriver;
static HotasWatchdogDriver g_watchdogDriver;

extern "C" __declspec(dllexport) void* HmdDriverFactory(const char* pInterfaceName, int* pReturnCode)
{
    DriverLogF("[vrchotas] HmdDriverFactory requested interface: %s", pInterfaceName ? pInterfaceName : "<null>");

    if (0 == strcmp(vr::IServerTrackedDeviceProvider_Version, pInterfaceName)
        || 0 == strcmp(vrchotas::driver::kServerTrackedDeviceProviderVersion004, pInterfaceName))
    {
        if (pReturnCode)
        {
            *pReturnCode = vr::VRInitError_None;
        }

        DriverLog("[vrchotas] Returning server driver interface.");
        return &g_serverDriver;
    }

    if (0 == strcmp(vr::IVRWatchdogProvider_Version, pInterfaceName))
    {
        if (pReturnCode)
        {
            *pReturnCode = vr::VRInitError_None;
        }

        DriverLog("[vrchotas] Returning watchdog driver interface.");
        return &g_watchdogDriver;
    }

    if (pReturnCode)
    {
        *pReturnCode = vr::VRInitError_Init_InterfaceNotFound;
    }

    DriverLogF("[vrchotas] Unsupported interface requested: %s", pInterfaceName ? pInterfaceName : "<null>");

    return nullptr;
}
