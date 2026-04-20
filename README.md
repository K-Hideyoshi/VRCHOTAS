# VRCHOTAS

[![Platform](https://img.shields.io/badge/platform-Windows-blue)](#requirements)
[![.NET](https://img.shields.io/badge/.NET-10-purple)](#net-app)
[![C++](https://img.shields.io/badge/C%2B%2B-20-00599C)](#c-driver)
[![UI](https://img.shields.io/badge/UI-WPF-512BD4)](#net-app)
[![License](https://img.shields.io/badge/license-GPLv3-green)](#license)

VRCHOTAS is a Windows + SteamVR HOTAS / joystick mapping project composed of **two fully equal and cooperating implementations**:

- **C++ Driver**: an OpenVR / SteamVR driver that converts shared state into SteamVR-visible virtual controller input and pose data.
- **.NET App**: a Windows desktop mapper that discovers physical devices, reads DirectInput input, applies mapping logic, and continuously publishes shared state.

These two parts form one complete system: **without either part, the full VRCHOTAS workflow does not exist**.

## Table of Contents

- [Architecture](#architecture)
- [Repository Structure](#repository-structure)
- [Implementation Overview](#implementation-overview)
  - [C++ Driver](#c-driver)
  - [.NET App](#net-app)
- [Shared Contract and Runtime Modes](#shared-contract-and-runtime-modes)
- [Requirements](#requirements)
- [Build Guide](#build-guide)
  - [Build the C++ Driver](#build-the-c-driver)
  - [Build the .NET App](#build-the-net-app)
- [Deployment and Startup Order](#deployment-and-startup-order)
- [Usage Flow](#usage-flow)
- [Configuration and Data Locations](#configuration-and-data-locations)
- [Troubleshooting](#troubleshooting)
- [Development Notes](#development-notes)
- [License](#license)

## Architecture

VRCHOTAS uses a dual-implementation design:

1. **.NET App**
   - Uses `SharpDX.DirectInput` to enumerate and poll physical HOTAS / joystick devices.
   - Maps physical input to virtual controller buttons, axes, position, orientation, linear velocity, and angular velocity.
   - Writes the result into named shared memory with a fixed binary layout.
   - Provides the WPF UI, logging, configuration management, and mapping editor.

2. **C++ Driver**
   - Loads as a SteamVR OpenVR driver.
   - Reads a shared memory snapshot every frame.
   - Converts shared state into virtual left and right controller input and pose updates.
   - Can mirror real VR controller poses in a specific runtime mode while still participating in the overall VRCHOTAS pipeline.

In short:

- **.NET App handles input acquisition, mapping, and state publishing**
- **C++ Driver handles shared state consumption and SteamVR injection**

## Repository Structure

```text
VRCHOTAS/
├─ README.md
├─ VRCHOTAS.sln
├─ VRCHOTAS.slnx
├─ VRCHOTAS/                         # .NET App
│  ├─ Interop/                       # Shared memory contract and writer channel
│  ├─ Logging/                       # Logging system
│  ├─ Models/                        # Mapping configuration and runtime models
│  ├─ Services/                      # DirectInput, mapping, configuration, preferences, hotkeys
│  ├─ ViewModels/                    # WPF MVVM view models
│  ├─ Converters/
│  ├─ *.xaml / *.xaml.cs             # Main windows and views
│  └─ VRCHOTAS.csproj
└─ VirtualDriver/                    # C++ Driver
   ├─ CMakeLists.txt
   ├─ include/
   │  └─ virtual_controller_state.h  # Data structure aligned with .NET
   ├─ src/
   │  └─ driver_hotas.cpp            # Main OpenVR driver implementation
   ├─ resources/
   │  ├─ driver.vrchotas.vrdrivermanifest
   │  └─ input/
   │     └─ vrchotas_virtual_profile.json
   ├─ deploy_driver.bat              # Driver deployment script
   └─ README.md
```

## Implementation Overview

### C++ Driver

Location: `VirtualDriver/`

Responsibilities:

- Exposes virtual left and right controllers as an OpenVR driver.
- Reads `VirtualControllerState` from named shared memory.
- Pushes button, axis, and pose state into SteamVR.
- Exposes controller capabilities through the driver manifest and input profile.
- Produces driver-side logs for runtime and resource diagnostics.

Current implementation characteristics:

- Built with **CMake + MSVC**.
- Uses **C++20**.
- Depends on the **OpenVR SDK**.
- Reads shared state and updates devices in `RunFrame()`.
- Supports two pose source modes:
  - `Mapped`
  - `MirrorRealControllers`
- Includes `deploy_driver.bat` to copy runtime files and attempt `vrpathreg.exe adddriver` registration.

Key files:

- `VirtualDriver/CMakeLists.txt`
- `VirtualDriver/include/virtual_controller_state.h`
- `VirtualDriver/src/driver_hotas.cpp`
- `VirtualDriver/resources/driver.vrchotas.vrdrivermanifest`
- `VirtualDriver/resources/input/vrchotas_virtual_profile.json`
- `VirtualDriver/deploy_driver.bat`

### .NET App

Location: `VRCHOTAS/`

Responsibilities:

- Enumerates and acquires DirectInput game controllers.
- Reads axis and button state in real time.
- Provides mapping editing, input auto-detection, configuration save/load, and desktop UI features.
- Writes final virtual controller state into shared memory for the C++ Driver.

Current implementation characteristics:

- Targets **`.NET 10`** with `net10.0-windows`.
- Uses **WPF + MVVM** (main view model split across partial files such as `MainViewModel.FrameLoop.cs` and `MainViewModel.Configuration.cs`).
- Core dependencies:
  - `CommunityToolkit.Mvvm`
  - `SharpDX.DirectInput`
  - `Newtonsoft.Json`
- Runs a **background frame loop** (`PeriodicTimer`) that adapts between **~20 ms** and **~5 ms** per tick when mapping is enabled, shared memory is available, and a recent **driver heartbeat** is seen in shared memory; otherwise it stays on the slower interval.
- On each tick the app polls DirectInput, evaluates **global hotkeys** (keyboard via `GetAsyncKeyState` and joystick buttons from the same poll), applies mappings, queues UI refresh on the WPF dispatcher, and writes shared memory.
- Hotkeys are processed on that background loop so they keep working while the main window is **hidden in the system tray**.
- **Preferences** (`preferences.json`): default mapping configuration filename and hotkey bindings.
- **Mapping configurations** (`%APPDATA%\VRCHOTAS\configs\*.json`): list of mappings only. Per-row **Toggle** temporarily disables a mapping at runtime (not persisted); the grid status light is **gray** while toggled off.
- **Master mapping switch** is **runtime-only** (not saved in configuration JSON). When it is off, the app writes a default virtual state and sets pose mode to `MirrorRealControllers`.

Key files:

- `VRCHOTAS/VRCHOTAS.csproj`
- `VRCHOTAS/ViewModels/MainViewModel.cs` (and related partials)
- `VRCHOTAS/Services/JoystickService.cs`
- `VRCHOTAS/Services/MappingEngine.cs`
- `VRCHOTAS/Services/ConfigurationService.cs`
- `VRCHOTAS/Services/PreferencesService.cs`
- `VRCHOTAS/Services/HotkeyRuntime.cs`
- `VRCHOTAS/Models/PreferencesDocument.cs`
- `VRCHOTAS/Interop/VirtualControllerState.cs`
- `VRCHOTAS/Interop/SharedMemoryStateChannel.cs`

## Shared Contract and Runtime Modes

The C++ Driver and .NET App communicate through named shared memory plus a named mutex. Both sides must obey the exact same binary layout.

### Named Objects

- Shared memory name: `Local\VRCHOTAS.VirtualController.State`
- Mutex name: `Local\VRCHOTAS.VirtualController.State.Mutex`

### Data Structure

The current contract includes a top-level pose source field plus left and right hand state:

- `VirtualPoseSource PoseSource`
- `ControllerHandState Left`
- `ControllerHandState Right`

Where:

- `VirtualPoseSource.Mapped = 0`
- `VirtualPoseSource.MirrorRealControllers = 1`

Each `ControllerHandState` contains:

- `Buttons[32]`: button array using 1-byte boolean representation
- `Axes[16]`: scalar axis input values
- `Position[3]`: position in meters
- `Quaternion[4]`: quaternion ordered as `w, x, y, z`
- `LinearVelocity[3]`: linear velocity in m/s
- `AngularVelocity[3]`: angular velocity in rad/s

### Layout Requirements

Both sides use **Pack = 1 / `#pragma pack(push, 1)`**, and field order must match exactly.

C# side:

- `[StructLayout(LayoutKind.Sequential, Pack = 1)]`
- `Buttons` uses `UnmanagedType.I1`

C++ side:

- `#pragma pack(push, 1)` / `#pragma pack(pop)`
- `bool buttons[32]`
- `double` arrays matching the .NET definitions

### Runtime Modes

`PoseSource` determines how the driver interprets pose data:

- **Mapped**
  - The driver uses position, quaternion, and velocity directly from shared memory.
  - This is suitable when HOTAS mappings are intended to drive the virtual controller pose directly.

- **MirrorRealControllers**
  - The driver tries to read real left and right controller poses and mirror them onto the virtual controllers.
  - Shared-memory input is kept on a neutral input path for this mode.
  - This is suitable when HOTAS input should drive controls while spatial pose should come from real VR controllers.

## Requirements

For the complete VRCHOTAS workflow, prepare the following environment.

### General

- Windows 10 / 11
- SteamVR
- At least one DirectInput-compatible HOTAS, joystick, or game controller

### C++ Driver

- Visual Studio 2022 / 2026 with the C++ Desktop workload
- CMake 3.20+
- OpenVR SDK

### .NET App

- .NET 10 SDK
- Visual Studio 2026 or another environment capable of building `net10.0-windows`

## Build Guide

### Build the C++ Driver

You can run these commands from the repository root or from `VirtualDriver/`. The examples below use the repository root.

1. Prepare an OpenVR SDK path, for example: `D:/sdk/openvr`
2. Configure the project:

```powershell
cmake -S .\VirtualDriver -B .\VirtualDriver\build -G "Visual Studio 18 2026" -A x64 -DOPENVR_SDK_PATH=D:/sdk/openvr
```

If you do not want to specify a generator, you can also run CMake from a properly initialized Native Tools / Developer PowerShell session:

```powershell
cmake -S .\VirtualDriver -B .\VirtualDriver\build -A x64 -DOPENVR_SDK_PATH=D:/sdk/openvr
```

3. Build:

```powershell
cmake --build .\VirtualDriver\build --config Release
```

4. Expected output:

- `VirtualDriver\build\Release\driver_vrchotas.dll`
- `VirtualDriver\resources\driver.vrchotas.vrdrivermanifest`
- `VirtualDriver\build\resources\input\vrchotas_virtual_profile.json`

Note: the build includes a post-build copy step that places `resources/input` into the expected build-relative SteamVR layout.

### Build the .NET App

From the repository root:

```powershell
dotnet restore .\VRCHOTAS.sln
dotnet build .\VRCHOTAS.sln -c Release
```

Or directly by project:

```powershell
dotnet restore .\VRCHOTAS\VRCHOTAS.csproj
dotnet build .\VRCHOTAS\VRCHOTAS.csproj -c Release
```

Run:

```powershell
dotnet run --project .\VRCHOTAS\VRCHOTAS.csproj
```

## Deployment and Startup Order

For end-to-end validation, use the following sequence.

### 1. Build both parts

- Build `VirtualDriver`
- Build `VRCHOTAS`

The order is not a strict technical dependency, but both parts must be built successfully before validating the full pipeline.

### 2. Deploy the C++ Driver to SteamVR

From `VirtualDriver/`:

```powershell
.\deploy_driver.bat Release
```

For Debug:

```powershell
.\deploy_driver.bat Debug
```

The script will:

- verify that the DLL, manifest, and input profile exist
- copy files into `%LOCALAPPDATA%\openvr\drivers\vrchotas`
- try to locate `vrpathreg.exe`
- call `adddriver` if registration tooling is found

Target layout:

```text
%LOCALAPPDATA%\openvr\drivers\vrchotas\
├─ driver.vrdrivermanifest
├─ bin\win64\driver_vrchotas.dll
└─ resources\input\vrchotas_virtual_profile.json
```

### 3. Start the .NET App

Start `VRCHOTAS` first and confirm that:

- physical devices are discovered
- a mapping configuration is loaded
- shared memory writing is active

### 4. Start or restart SteamVR

Once SteamVR starts, it loads the registered VRCHOTAS driver. The C++ Driver then begins reading shared memory and driving the virtual controllers.

## Usage Flow

1. Start the `.NET App`.
2. Confirm that your DirectInput device appears under **Discovered Device** and in the device monitor list.
3. Create or edit mappings:
   - choose a source device
   - choose a source axis or button
   - choose the target hand (`Left` / `Right`)
   - choose a target type (button, axis, position, pose, or velocity)
   - adjust `Deadzone`, `Curve`, `Saturation`, and `Invert`
4. Save the configuration (**Configuration → Save / Save As**). Use **Set Default Configuration** so the chosen file becomes the startup default (stored in `preferences.json`).
5. Optional: **Configuration → Preference → Hotkeys** to bind previous/next configuration and the master mapping toggle (keyboard or joystick).
6. Use the **Mapping** button for the master switch (on/off is not written to disk). Use per-row **Toggle** to skip a mapping until toggled back (also not persisted).
7. Deploy and load the `C++ Driver`. Watch **Driver heartbeat** (OK when the driver is updating shared memory within the expected window) and **Refresh rate** on the main window.
8. In SteamVR, verify that the virtual controllers appear and react as expected.

Closing the main window sends the app to the **system tray**; the process keeps running so polling, hotkeys, and shared memory updates continue until you exit from the tray menu.

## Configuration and Data Locations

### Application data root

```text
%APPDATA%\VRCHOTAS\
```

### Preferences (`preferences.json`)

Path:

```text
%APPDATA%\VRCHOTAS\preferences.json
```

Contains:

- **`defaultConfigurationFileName`**: which file under `configs\` loads on startup (default: `default-config.json` when the file is first created).
- **`hotkeys`**: bindings for previous configuration, next configuration, and toggle master mapping (keyboard chord and/or joystick button per slot).

There is **no** separate `app-state.json`; older documentation referring to it does not apply to the current tree.

### Mapping configuration files (`configs`)

Directory:

```text
%APPDATA%\VRCHOTAS\configs\
```

Contains one JSON file per saved mapping set. Each file stores an `AppConfiguration` with a **`Mappings`** array only (no master switch, no per-row temporary toggle state).

If the configured default file is missing on disk, the app creates an empty configuration file with that name on startup.

### C++ Driver runtime resources

After deployment, the main runtime resources are:

- `driver.vrdrivermanifest`
- `driver_vrchotas.dll`
- `resources/input/vrchotas_virtual_profile.json`

### Logs

- The .NET App uses the repository logging system for application logs.
- The C++ Driver writes driver-side logs for runtime resources, pose mode, mutex wait behavior, and related diagnostics.

## Troubleshooting

### The .NET App does not detect devices

- Confirm that the device is DirectInput-compatible.
- Reconnect the device and refresh.
- Check whether another application has exclusive ownership.
- Review the application logs for device acquisition failures.

### SteamVR does not show the virtual controllers

- Confirm that `deploy_driver.bat` completed successfully.
- Confirm that the driver directory was registered through `vrpathreg.exe adddriver`.
- Confirm that `driver.vrdrivermanifest` and the DLL were copied to the correct location.
- Restart SteamVR and test again.

### The driver loads, but input does not react

- Confirm that the `.NET App` is running.
- Confirm that the `.NET App` detects devices and the monitor values are changing.
- Confirm that mappings are enabled.
- Check that the shared memory and mutex names match exactly.
- Check that `VirtualControllerState` layout and field order match exactly.

### Pose data is incorrect, scrambled, or offset

- Confirm that both sides use Pack=1.
- Confirm that the boolean array uses 1-byte representation.
- Confirm quaternion order is `w, x, y, z`.
- Confirm that position is in meters and angular velocity is in rad/s.
- If using `MirrorRealControllers`, confirm that real VR controllers are actively tracked.

### The C++ Driver build cannot find OpenVR headers or libraries

- Re-run CMake configuration with the correct `OPENVR_SDK_PATH`.
- Confirm that the path contains:
  - `headers\openvr_driver.h`
  - `lib\win64\openvr_api.lib`

### The deployment script reports missing files

Verify all of the following:

- `VirtualDriver\build\Release\driver_vrchotas.dll` or the Debug equivalent exists
- `VirtualDriver\resources\driver.vrchotas.vrdrivermanifest` exists
- `VirtualDriver\build\resources\input\vrchotas_virtual_profile.json` was produced by the post-build copy step

## Development Notes

- This repository contains both the **C++ Driver** and the **.NET App** as equal parts of one system. They should not be described as “external/internal” or “primary/secondary”.
- If you change the shared contract, update all of the following together:
  - `VRCHOTAS/Interop/VirtualControllerState.cs`
  - `VirtualDriver/include/virtual_controller_state.h`
  - all corresponding read/write logic
- If you change **preferences** shape or default configuration resolution, keep `PreferencesDocument`, `PreferencesService`, and the README “Configuration and Data Locations” section aligned.
- If you add new input semantics, also verify:
  - `.NET App` mapping editor and output logic
  - `C++ Driver` component registration, state update logic, and input profile

## License

GNU GENERAL PUBLIC LICENSE Version 3