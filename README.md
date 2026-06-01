# VRCHOTAS

![VRCHOTAS banner](assets/banner.jpeg)

[![Platform](https://img.shields.io/badge/platform-Windows-blue)](#requirements)
	[![.NET](https://img.shields.io/badge/.NET-10-purple)](#requirements)
[![C++](https://img.shields.io/badge/C%2B%2B-20-00599C)](#requirements)
[![UI](https://img.shields.io/badge/UI-WPF-512BD4)](#implementation-overview)
[![License](https://img.shields.io/badge/license-GPLv3-green)](#license)

VRCHOTAS transforms joysticks, HOTAS systems, and steering wheels into SteamVR controllers. It is a simple yet powerful emulator that bridges DirectInput hardware into VR with fully customizable mappings.

The project originally started to help flight-sim players use their joystick / HOTAS hardware to control aircraft and fighter jets in VRChat flight worlds. It is also designed as a more general solution that can theoretically be used to bring any DirectInput game controller into any SteamVR game or application.

The system is composed of two required parts:

- **.NET App**: discovers DirectInput devices, manages mappings/configurations/hotkeys, and publishes virtual controller state
- **C++ Driver**: loads as an OpenVR / SteamVR driver and exposes SteamVR-visible virtual controllers

## Table of Contents

- [Runtime Requirements](#runtime-requirements)
- [Implementation Overview](#implementation-overview)
- [Build Guide](#build-guide)
  - [Build the C++ Driver](#build-the-c-driver)
  - [Build the .NET App](#build-the-net-app)
  - [Release Packaging Guide](#release-packaging-guide)
- [Deployment and Startup](#deployment-and-startup)
- [How to Undo Deployment](#how-to-undo-deployment)
- [Configuration and Data Locations](#configuration-and-data-locations)
- [Troubleshooting](#troubleshooting)
- [License](#license)

## Runtime Requirements

### Windows

- Windows 10/11
- SteamVR
- A DirectInput HOTAS / joystick device

## Implementation Overview

VRCHOTAS uses a shared-memory architecture built from two required parts:

1. **.NET App**
	  - Location: `VRCHOTAS/`
   - Enumerates and polls physical DirectInput HOTAS / joystick devices.
   - Polls device axes and buttons continuously.
   - Applies mapping logic for controller axes, buttons, and other supported targets.
   - Lets users create, edit, reorder, enable/disable, save, and load mappings.
   - Applies VR controller mapping logic.
   - Publishes virtual controller state into shared memory.
   - Provides the desktop UI, configuration management, hotkeys, and logging.

2. **C++ Driver**
   - Location: `VirtualDriver/`
   - Loads inside SteamVR as an OpenVR driver.
   - Registers left and right virtual controllers with SteamVR.
   - Declares the virtual controller input profile.
   - Reads the shared virtual controller state.
   - Exposes left and right virtual controllers to SteamVR.
   - Updates SteamVR-visible inputs and controller state.
   - Updates virtual controller pose from mapped pose data.

In short:

- **.NET App handles device input, mapping, UI, and state publishing**
- **C++ Driver handles SteamVR-facing controller injection**

## Build Guide

### Build the C++ Driver

#### Requirements

- Visual Studio 2022 / 2026 with C++ Desktop workload
- CMake 3.20+
- OpenVR SDK
- `OPENVR_SDK_PATH` configured to the OpenVR SDK root

Configure and build from the repository root:

```powershell
cmake -S .\VirtualDriver -B .\VirtualDriver\build -A x64 -DOPENVR_SDK_PATH=D:/Programming/Workspace/openvr-2.15.6
cmake --build .\VirtualDriver\build --config Release
```

Or set the environment variable first and then build an existing configured tree:

```powershell
$env:OPENVR_SDK_PATH = 'D:\Programming\Workspace\openvr-2.15.6'
cmake --build .\VirtualDriver\build --config Release
```

Expected output:

- `VirtualDriver\build\Release\driver_vrchotas.dll`
- `VirtualDriver\build\resources\input\vrchotas_virtual_profile.json`
- `VirtualDriver\build\resources\input\vrcompositor_bindings_touch.json`

### Build the .NET App

#### Requirements

- .NET 10 SDK
- Visual Studio 2026 or another environment capable of building `net10.0-windows`

```powershell
dotnet restore .\VRCHOTAS\VRCHOTAS.csproj
dotnet build .\VRCHOTAS\VRCHOTAS.csproj -c Release
```

Notes:

- Building `VRCHOTAS.csproj` automatically builds `VRCHOTAS.OverlayHelper` first.
- The build then copies the helper outputs into the main app output directory.
- After a successful build, `VRCHOTAS\bin\<Configuration>\net10.0-windows\` should contain at least:
  - `VRCHOTAS.exe`
  - `VRCHOTAS.OverlayHelper.exe`
  - `openvr_api.dll`

Run:

```powershell
dotnet run --project .\VRCHOTAS\VRCHOTAS.csproj
```

### Release Packaging Guide

Release packaging instructions were moved to [docs/release-packaging.md](docs/release-packaging.md).

## Deployment and Startup

The portable package uses this startup behavior:

- On app startup, VRCHOTAS tries to detect SteamVR.
- When VR overlay features are used, VRCHOTAS starts `VRCHOTAS.OverlayHelper.exe` from the same output directory.
- When SteamVR is found, VRCHOTAS copies the bundled C++ driver payload into `%LOCALAPPDATA%\openvr\drivers\vrchotas`.
- VRCHOTAS then runs `vrpathreg.exe adddriver` automatically.
- VRCHOTAS automatically modifies `<Steam>\config\steamvr.vrsettings` to set `"steamvr.activateMultipleDrivers": true`.
- No manual deployment step is required for the normal portable release.

For local development, `VirtualDriver\deploy_driver.bat Release` is still available if you want to deploy the driver manually outside the packaged app flow.

Recommended startup order:

1. Extract VRCHOTAS.
2. Close SteamVR and Steam completely.
3. Start VRCHOTAS ...
4. Confirm devices and mappings are configured in the UI.
5. Start or restart SteamVR.
6. Confirm driver sync rate and driver heartbeat in the VRCHOTAS UI.
7. Turn master switch ON, Verify the virtual controllers in SteamVR...

## How to Undo Deployment

1. Close SteamVR.
2. Remove the deployed driver registration:

   ```powershell
   & "$env:STEAMVR_PATH\bin\win64\vrpathreg.exe" removedriver "$env:LOCALAPPDATA\openvr\drivers\vrchotas"
   ```

   Or use the repository cleanup script:

   ```powershell
   .\scripts\remove-deployed-driver.ps1
   ```

   Note: if `vrpathreg.exe show` still lists `%LOCALAPPDATA%\openvr\drivers\vrchotas` after cleanup, manually remove the stale entry from `%LOCALAPPDATA%\openvr\openvrpaths.vrpath` before re-testing deployment.

3. Delete the deployed driver folder if you no longer need the local copy:

   ```powershell
   Remove-Item "$env:LOCALAPPDATA\openvr\drivers\vrchotas" -Recurse -Force
   ```

4. Start SteamVR again.

## Configuration and Data Locations

- App data root: `%APPDATA%\VRCHOTAS\`
- Configurations: `%APPDATA%\VRCHOTAS\configs\`
- Logs: `%APPDATA%\VRCHOTAS\logs\`
- Preferences: `%APPDATA%\VRCHOTAS\preferences.json`

## Troubleshooting

### The C++ Driver build fails with an OpenVR SDK error

- Set `OPENVR_SDK_PATH` correctly.
- Confirm the SDK path contains:
  - `headers\openvr_driver.h`
  - `lib\win64\openvr_api.lib`

### SteamVR does not show the virtual controllers

- Confirm `deploy_driver.bat` completed successfully.
- Confirm the driver directory was registered:

  ```powershell
  & "$env:STEAMVR_PATH\bin\win64\vrpathreg.exe" showdrivers
  ```

- Confirm these files exist under the deployed driver root:
	- `%LOCALAPPDATA%\openvr\drivers\vrchotas\driver.vrdrivermanifest`
  - `%LOCALAPPDATA%\openvr\drivers\vrchotas\bin\win64\driver_vrchotas.dll`
  - `%LOCALAPPDATA%\openvr\drivers\vrchotas\resources\input\vrchotas_virtual_profile.json`
- Restart SteamVR after deployment.
- Ensure `activateMultipleDrivers` is set to `true` in `<Steam>\config\steamvr.vrsettings` (VRCHOTAS configures this automatically on first run).

### SteamVR loads the driver, but input does not react as expected

- Confirm the .NET app is running.
- Confirm the device monitor is updating.
- Confirm mappings are enabled.
- Confirm source mappings point to the intended device and control.
- Confirm shared memory names and structure layout match on both sides.

### Position or velocity mapping does not behave as expected

- Confirm whether the mapping targets are pose position or velocity targets.
- Use `Recenter Hand` if you need to reset the current hand reference point.
- Review the application logs for mapping and pose state output.

For more detailed troubleshooting guidance and diagnostic tools, see [docs/troubleshooting.md](docs/troubleshooting.md), which includes quick diagnostic scripts, step-by-step solutions for common issues, advanced troubleshooting techniques, and performance optimization tips.

## License

This repository is licensed under **GPLv3**.
