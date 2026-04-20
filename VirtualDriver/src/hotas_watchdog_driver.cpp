#include <openvr_driver.h>
#include "driver_logging.h"
#include "hotas_watchdog_driver.h"

vr::EVRInitError HotasWatchdogDriver::Init(vr::IVRDriverContext* pDriverContext)
{
    VR_INIT_WATCHDOG_DRIVER_CONTEXT(pDriverContext);
    DriverLog("[vrchotas] Watchdog driver initialized.");
    return vr::VRInitError_None;
}

void HotasWatchdogDriver::Cleanup()
{
    DriverLog("[vrchotas] Watchdog driver cleanup called.");
    VR_CLEANUP_WATCHDOG_DRIVER_CONTEXT();
}
