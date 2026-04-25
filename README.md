# VRCHOTAS

[![Platform](https://img.shields.io/badge/platform-Windows-blue)](#requirements)
[![.NET](https://img.shields.io/badge/.NET-10-purple)](#net-app)
[![C++](https://img.shields.io/badge/C%2B%2B-20-00599C)](#c-driver)
[![UI](https://img.shields.io/badge/UI-WPF-512BD4)](#net-app)
[![License](https://img.shields.io/badge/license-GPLv3-green)](#license)

VRCHOTAS is a Windows + SteamVR HOTAS / joystick mapping system composed of two equal parts:

- **.NET App**: discovers DirectInput devices, polls buttons and axes, applies mapping logic, and writes a shared virtual controller state.
- **C++ Driver**: loads as an OpenVR / SteamVR driver, reads that shared state, and exposes SteamVR-visible virtual controllers.

Both parts are required for the complete workflow.

## Table of Contents

- [Architecture](#architecture)
- [Repository Structure](#repository-structure)
- [Implementation Overview](#implementation-overview)
  - [C++ Driver](#c-driver)
  - [.NET App](#net-app)
- [Current Feature Summary](#current-feature-summary)
- [Shared Contract](#shared-contract)
- [Requirements](#requirements)
- [Build Guide](#build-guide)
  - [Build the C++ Driver](#build-the-c-driver)
  - [Build the .NET App](#build-the-net-app)
- [Deployment and Startup](#deployment-and-startup)
- [Usage Flow](#usage-flow)
- [Configuration and Data Locations](#configuration-and-data-locations)
- [Troubleshooting](#troubleshooting)
- [Development Notes](#development-notes)
- [License](#license)

## Architecture

VRCHOTAS uses a shared-memory architecture:

1. **.NET App**
   - Enumerates and polls physical DirectInput HOTAS / joystick devices.
   - Applies mapping logic for controller axes, buttons, pose position, pose orientation, linear velocity, and angular velocity.
   - Publishes a packed `VirtualControllerState` into named shared memory.
   - Provides the WPF desktop UI, configuration management, mapping editor, hotkeys, and logging.

2. **C++ Driver**
   - Loads inside SteamVR as an OpenVR driver.
   - Reads the same packed `VirtualControllerState` every frame.
   - Exposes left and right Oculus Touch-style virtual controllers to SteamVR.
   - Updates SteamVR button, touch, click, axis, and pose components from the shared state.

In short:

- **.NET App handles device input, mapping, UI, and state publishing**
- **C++ Driver handles SteamVR-facing controller injection**

## Repository Structure

```text
VRCHOTAS/                                   # Repository root
├─ README.md                                # Main project documentation for architecture, build, deployment, and usage
├─ VRCHOTAS.sln                             # Visual Studio solution file
├─ VRCHOTAS.slnx                            # Alternate solution metadata used by newer Visual Studio tooling
├─ VRCHOTAS/                                # .NET 10 WPF mapper app
│  ├─ Converters/                           # WPF value converters used by the main UI
│  ├─ Interop/                              # Shared-memory contract, layout constants, and IPC writer channel
│  ├─ Logging/                              # App-side logging infrastructure and log window models
│  ├─ Models/                               # Mapping, hotkey, preferences, and runtime data models
│  ├─ Services/                             # DirectInput polling, mapping engine, config, preferences, and hotkey services
│  ├─ ViewModels/                           # MVVM view models for main window, mapping editor, device monitor, and dialogs
│  ├─ MainWindow.xaml                       # Main dashboard UI for device monitor, mapping list, and status area
│  ├─ MainWindow.xaml.cs                    # Main window events, tray behavior, and dialog launching
│  ├─ MappingEditorWindow.xaml              # Mapping editor UI for source detection and target selection
│  ├─ MappingEditorWindow.xaml.cs           # Mapping editor dialog control and auto-detect timer wiring
│  ├─ HotkeysWindow.xaml                    # Hotkey settings dialog UI
│  ├─ HotkeysWindow.xaml.cs                 # Keyboard/joystick hotkey capture and persistence logic
│  └─ VRCHOTAS.csproj                       # .NET project file and package references
└─ VirtualDriver/                           # SteamVR OpenVR driver
   ├─ CMakeLists.txt                        # CMake build definition for the native driver
   ├─ README.md                             # Driver-specific notes and usage details
   ├─ deploy_driver.bat                     # Copies build output into the SteamVR driver folder and registers it
   ├─ include/                              # Native headers shared across driver components
   │  ├─ hotas_controller_device.h          # Virtual controller device class declaration
   │  └─ virtual_controller_state.h         # Native copy of the shared-memory contract and semantic slot constants
   ├─ resources/                            # SteamVR driver manifest and input profile resources
   │  ├─ driver.vrchotas.vrdrivermanifest   # SteamVR driver manifest file
   │  └─ input/                             # SteamVR input profile and compositor binding files
   │     ├─ vrchotas_virtual_profile.json   # Oculus Touch-style virtual controller profile
   │     └─ vrcompositor_bindings_touch.json# Dashboard pointer and system bindings for vrcompositor
   └─ src/                                  # Native driver source files
      ├─ driver_hotas.cpp                   # Driver entry points and factory exports
      ├─ hotas_controller_device.cpp        # Virtual controller creation, input component registration, and state updates
      ├─ hotas_server_driver.cpp            # OpenVR server driver orchestration and shared-memory polling
      └─ hotas_watchdog_driver.cpp          # Minimal watchdog driver implementation
```

## Implementation Overview

### C++ Driver

Location: `VirtualDriver/`

Responsibilities:

- Registers left and right virtual controllers with SteamVR.
- Declares an Oculus Touch-style input profile.
- Creates semantic button and axis components.
- Reads shared memory and updates SteamVR input state every frame.
- Updates virtual controller pose from mapped pose data.

Current implementation characteristics:

- Built with **CMake + MSVC**.
- Uses **C++20**.
- Requires the **OpenVR SDK**.
- Uses `OPENVR_SDK_PATH` during CMake configure/build.
- Publishes Oculus Touch-like capabilities, legacy bindings, and compositor bindings.
- Supports these semantic controller inputs:
  - joystick x/y
  - joystick click
  - joystick touch
  - trigger value
  - trigger touch
  - trigger click
  - grip value
  - grip touch
  - grip click
  - primary face button (`X`/`A`)
  - secondary face button (`Y`/`B`)
  - system button
- Includes alias paths for `joystick` / `thumbstick` naming expected by SteamVR bindings.
- Uses `vrcompositor_bindings_touch.json` for dashboard pointer and system toggle bindings.


### .NET App

Location: `VRCHOTAS/`

Responsibilities:

- Enumerates and acquires DirectInput devices.
- Polls device axes and buttons continuously.
- Lets users create, edit, reorder, enable/disable, save, and load mappings.
- Applies semantic VR controller mapping and pose mapping.
- Writes the resulting `VirtualControllerState` to shared memory.

Current implementation characteristics:

- Targets **`.NET 10`** with `net10.0-windows`.
- Uses **WPF + MVVM**.
- Core packages:
  - `CommunityToolkit.Mvvm`
  - `Newtonsoft.Json`
  - `SharpDX`
  - `SharpDX.DirectInput`
- Uses a background frame loop with adaptive polling:
  - normal: about **20 ms**
  - elevated: about **5 ms** when mapping is enabled and the driver heartbeat is active
- Tracks a driver heartbeat exposed through shared memory.
- Supports tray minimization while input polling, hotkeys, and state publishing continue.
- Supports keyboard and joystick hotkeys for:
  - previous configuration
  - next configuration
  - mapping master switch
- Displays joystick hotkeys using **device names** when available.
- Automatically selects the currently active mapping row when a source button edge or fast axis movement is detected.
- Supports moving mapping rows up/down while keeping the moved row selected.
- Highlights duplicate mapping groups in the list:
  - Source columns use red shades
  - Target columns use blue shades
  - Grouping is strict:
    - Source: `SourceDeviceId + Button/Axis`
    - Target: `TargetHand + TargetControl`
- The Mapping list Actions column includes:
  - Toggle
  - Move up
  - Move down
  - Edit
  - Delete


## Current Feature Summary

Compared to earlier states of the project, the current tree includes these notable changes and behaviors:

- SteamVR-facing input semantics were expanded beyond simple axes/buttons.
- Virtual controllers now expose Touch-style input paths and metadata instead of a minimal custom profile.
- `System Button` is supported in both the app-side mapping model and the driver-side semantic slots.
- Trigger and grip analog mappings derive click state from a configurable full-press threshold.
- Thumbstick / trigger / grip analog mappings derive touch state from analog activity.
- Mapping engine now resets transient button/axis state each frame before rebuilding output.
- Velocity mappings are integrated into position output each frame and include extra debug logging.
- Main window device monitor refresh has been tuned for more responsive updates.
- Hotkey display no longer truncates joystick devices to shortened IDs.
- Mapping rows can be reordered directly from the list.
- Duplicate mappings are visually grouped in the list by strict runtime keys, not by display text.

## Shared Contract

The .NET App and the C++ Driver communicate through named shared memory guarded by a named mutex.

### Named Objects

- Shared memory: `Local\VRCHOTAS.VirtualController.State`
- Mutex: `Local\VRCHOTAS.VirtualController.State.Mutex`

### Top-level structure

`VirtualControllerState` contains:

- `VirtualPoseSource PoseSource`
- `ControllerHandState Left`
- `ControllerHandState Right`
- `ulong / uint64 driver heartbeat`

`PoseSource` values:

- `Mapped = 0`
- `MirrorRealControllers = 1`

### ControllerHandState layout

Each hand contains:

- `Buttons[32]`
- `Axes[16]`
- `Position[3]`
- `Quaternion[4]` ordered as `w, x, y, z`
- `LinearVelocity[3]`
- `AngularVelocity[3]`

### Semantic input slots currently used

Buttons:

- `Buttons[0]`: thumbstick click
- `Buttons[1]`: primary face button (`X` on left, `A` on right)
- `Buttons[2]`: secondary face button (`Y` on left, `B` on right)
- `Buttons[3]`: system button
- `Buttons[4]`: thumbstick touch
- `Buttons[5]`: trigger touch
- `Buttons[6]`: trigger click
- `Buttons[7]`: grip touch
- `Buttons[8]`: grip click

Axes:

- `Axes[0]`: thumbstick X
- `Axes[1]`: thumbstick Y
- `Axes[2]`: trigger value
- `Axes[3]`: grip value

### Layout requirements

Both sides must stay aligned exactly:

- C# uses `[StructLayout(LayoutKind.Sequential, Pack = 1)]`
- C++ uses `#pragma pack(push, 1)`
- C# button array uses `UnmanagedType.I1`
- Field order and array lengths must match exactly

## Requirements

### General

- Windows 10 / 11
- SteamVR
- At least one DirectInput-compatible HOTAS, joystick, or controller

### C++ Driver

- Visual Studio 2022 / 2026 with C++ Desktop workload
- CMake 3.20+
- OpenVR SDK
- `OPENVR_SDK_PATH` configured to the OpenVR SDK root

### .NET App

- .NET 10 SDK
- Visual Studio 2026 or another environment capable of building `net10.0-windows`

## Build Guide

### Build the C++ Driver

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

- verifies that the DLL, manifest, and input profile exist
- copies the driver files to `%LOCALAPPDATA%\openvr\drivers\vrchotas`
- tries to locate `vrpathreg.exe`
- calls `adddriver` when SteamVR registration tooling is found

### Recommended startup order

1. Build `VirtualDriver`
2. Build `VRCHOTAS`
3. Deploy the driver
4. Start the .NET app
5. Confirm devices, mappings, refresh rate, and driver heartbeat in the UI
6. Start or restart SteamVR
7. Verify the virtual controllers in SteamVR

## Usage Flow

1. Start the .NET app.
2. Confirm your physical DirectInput device appears in the device monitor.
3. Create or edit a mapping.
4. Select the target hand: `Left` or `Right`.
5. Choose a target type:
   - `VR Axis`
   - `VR Button`
   - pose position X/Y/Z
   - pose orientation X/Y/Z
   - linear velocity X/Y/Z
   - angular velocity X/Y/Z
6. For `VR Axis`, choose one semantic target:
   - `Thumbstick X`
   - `Thumbstick Y`
   - `Trigger`
   - `Grip`
7. For `VR Button`, choose one semantic target:
   - `Thumbstick Click`
   - `Primary Face Button (A/X)`
   - `Secondary Face Button (B/Y)`
   - `System Button`
8. For axis sources, adjust:
   - `Deadzone`
   - `Curve`
   - `Saturation`
   - `Invert`
   - `Axis Range`
   - `Full Press Threshold` when applicable
9. Save the configuration.
10. Optionally configure hotkeys under `Configuration -> Preference -> Hotkeys`.
11. Use the Mapping master switch to enable or disable mapping output.
12. Use per-row actions to toggle, reorder, edit, or delete mappings.
13. Start SteamVR and validate the virtual controllers.

Notes:

- New mappings start source auto-detection automatically.
- Editing an existing mapping keeps its current source until re-detect is requested.
- Closing the main window hides the app to the system tray instead of exiting.
- When mapping is disabled, the app writes a default controller state and switches pose mode to `MirrorRealControllers`.

## Configuration and Data Locations

### App data root

```text
%APPDATA%\VRCHOTAS\
```

### Preferences

```text
%APPDATA%\VRCHOTAS\preferences.json
```

Contains:

- default configuration filename
- hotkey bindings

### Mapping configurations

```text
%APPDATA%\VRCHOTAS\configs\
```

Each JSON file stores an `AppConfiguration` containing a `Mappings` array.

Persisted mapping data includes the semantic mapping model such as:

- source device ID / name
- source axis or button
- target hand
- target kind
- target axis or target button
- axis range
- deadzone
- curve
- saturation
- invert
- full-press threshold

Runtime-only state is **not** persisted, including:

- master mapping switch state
- per-row temporary toggle state
- duplicate-row background colors
- source device connection indicator

## Troubleshooting

### The C++ Driver build fails with an OpenVR SDK error

- Set `OPENVR_SDK_PATH` correctly.
- Confirm the SDK path contains:
  - `headers\openvr_driver.h`
  - `lib\win64\openvr_api.lib`

### SteamVR does not show the virtual controllers

- Confirm `deploy_driver.bat` completed successfully.
- Confirm the driver directory was registered.
- Confirm these files exist under the deployed driver root:
  - `driver.vrdrivermanifest`
  - `bin\win64\driver_vrchotas.dll`
  - `resources\input\vrchotas_virtual_profile.json`
- Restart SteamVR after deployment.

### SteamVR loads the driver, but input does not react as expected

- Confirm the .NET app is running.
- Confirm the device monitor is updating.
- Confirm mappings are enabled.
- Confirm source mappings point to the intended device ID and control.
- Confirm trigger/grip mappings use unidirectional output where appropriate.
- Confirm shared memory names and structure layout match exactly on both sides.

### Touch / click / full-press behavior looks wrong

- Check the configured `Full Press Threshold` for trigger or grip mappings.
- Confirm the mapped axis returns close to zero when released.
- Review app logs from `MappingEngine` and driver logs from `hotas_controller_device.cpp` for semantic state changes.

### Position or velocity mapping does not behave as expected

- Confirm whether the mapping targets are pose position or velocity targets.
- Remember that velocity mappings are integrated into position over time by the app.
- Review the app debug logs for pose state output.

## Development Notes

- Treat the .NET app and C++ driver as equal parts of one system.
- If you change the shared contract, update all of the following together:
  - `VRCHOTAS/Interop/VirtualControllerState.cs`
  - `VirtualDriver/include/virtual_controller_state.h`
  - all matching read/write logic
- If you add or rename semantic inputs, update together:
  - `VRCHOTAS/Models/MappingConfig.cs`
  - `VRCHOTAS/Services/MappingEngine.cs`
  - `VRCHOTAS/Interop/VirtualControllerState.cs`
  - `VirtualDriver/include/virtual_controller_state.h`
  - `VirtualDriver/src/hotas_controller_device.cpp`
  - `VirtualDriver/resources/input/vrchotas_virtual_profile.json`
  - related SteamVR binding files
- Keep README statements aligned with the current runtime behavior, not older project assumptions.

## License

This repository is licensed under **GPLv3**.
