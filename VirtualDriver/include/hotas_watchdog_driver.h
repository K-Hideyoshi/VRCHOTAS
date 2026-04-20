#pragma once

#include <openvr_driver.h>

class HotasWatchdogDriver final : public vr::IVRWatchdogProvider
{
public:
    vr::EVRInitError Init(vr::IVRDriverContext* pDriverContext) override;
    void Cleanup() override;
};
