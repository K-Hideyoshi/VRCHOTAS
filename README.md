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
	  - Enumerates and acquires DirectInput devices.
   - Polls device axes and buttons continuously.
   - Applies mapping logic for controller axes, buttons, and other supported targets.
	  - Lets users create, edit, reorder, enable/disable, save, and load mappings.
   - Applies VR controller mapping logic.
   - Publishes virtual controller state into shared memory.
	  - Writes the resulting virtual controller state to shared memory.
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

Run:

```powershell
dotnet run --project .\VRCHOTAS\VRCHOTAS.csproj
```

## Deployment and Startup

### Deploy the driver

From `VirtualDriver/`:

```powershell
.\deploy_driver.bat Release
```

The script:

- verifies that the DLL, source manifest (`resources\driver.vrchotas.vrdrivermanifest`), and input profile exist
- copies the driver files to `%LOCALAPPDATA%\openvr\drivers\vrchotas`
- tries to locate `vrpathreg.exe`
- calls `adddriver` when SteamVR registration tooling is found

### Recommended startup order

1. Build `VirtualDriver`
2. Build `VRCHOTAS`
3. Deploy the driver
4. Start the .NET app
5. Confirm devices, mappings, driver sync rate, and driver heartbeat in the UI
6. Start or restart SteamVR
7. Verify the virtual controllers in SteamVR and confirm the driver is registered:

   ```powershell
   & "$env:STEAMVR_PATH\bin\win64\vrpathreg.exe" showdrivers
   ```

## How to Undo Deployment

1. Close SteamVR.
2. Remove the deployed driver registration:

   ```powershell
   & "$env:STEAMVR_PATH\bin\win64\vrpathreg.exe" removedriver "$env:LOCALAPPDATA\openvr\drivers\vrchotas"
   ```

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

## License

This repository is licensed under **GPLv3**.
