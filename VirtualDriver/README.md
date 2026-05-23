# VirtualDriver (OpenVR)

## Overview

`VirtualDriver` is the native OpenVR / SteamVR driver component of VRCHOTAS.
It reads the shared `VirtualControllerState` published by the .NET app and exposes a left/right pair of Oculus Touch-style virtual controllers to SteamVR.

## Current runtime behavior

- Registers exactly one left controller and one right controller with fixed serial numbers:
  - `vrchotas_left`
  - `vrchotas_right`
- Publishes Oculus Touch-style controller properties, render models, and input profile metadata.
- Reads shared memory every driver frame while holding the named mutex.
- Updates SteamVR button, touch, click, axis, and pose state from the shared snapshot.
- Writes `driver_heartbeat_tick_ms` into the shared structure every frame.
- Exposes or hides the virtual controllers based on the app heartbeat (`app_heartbeat_tick_ms`).
- Uses in-place disconnect/reconnect for handoff instead of repeatedly creating new tracked devices.

## Pose and handoff modes

The driver reacts to the app-provided `pose_source` field:

- `Mapped`
  - Uses mapped pose data from the app.
  - Keeps the virtual controllers connected while the app heartbeat is alive.
- `MirrorRealControllers`
  - Virtual button/axis values are forced to neutral.
  - The driver disconnects the virtual controllers so real SteamVR controllers can take over.

This means the current design supports seamless switching between mapped virtual controllers and real controllers without device accumulation.

## Shared-memory communication

Named objects:

- Shared memory: `Local\\VRCHOTAS.VirtualController.State`
- Mutex: `Local\\VRCHOTAS.VirtualController.State.Mutex`

Shared structure:

- Defined in `include/virtual_controller_state.h`
- Must stay byte-for-byte aligned with `VRCHOTAS/Interop/VirtualControllerState.cs`
- Includes both heartbeat fields:
  - `app_heartbeat_tick_ms`
  - `driver_heartbeat_tick_ms`

## Build requirements

- Visual Studio 2022 / 2026 with MSVC v143+
- CMake 3.20+
- OpenVR SDK
- `OPENVR_SDK_PATH` pointing to the OpenVR SDK root

## Build steps

From `VirtualDriver/`:

```powershell
cmake -S . -B build -A x64 -DOPENVR_SDK_PATH=D:/Programming/Workspace/openvr-2.15.6
cmake --build build --config Release
```

Expected output:

- `build\\Release\\driver_vrchotas.dll`
- `build\\resources\\input\\vrchotas_virtual_profile.json`
- `build\\resources\\input\\vrcompositor_bindings_touch.json`

## Deploy

Use the deployment helper from `VirtualDriver/`:

```powershell
.\\deploy_driver.bat Release
```

The script:

- checks for the built DLL
- checks for the source manifest: `resources\\driver.vrchotas.vrdrivermanifest`
- checks for the copied input profile in the build output
- copies the deployed manifest as `driver.vrdrivermanifest`
- copies files into `%LOCALAPPDATA%\\openvr\\drivers\\vrchotas`
- calls `vrpathreg.exe adddriver` when SteamVR registration tooling is found

## Key files

- `include/virtual_controller_state.h` - native shared-memory contract
- `src/hotas_server_driver.cpp` - driver lifecycle, shared-memory polling, heartbeat gating, and controller handoff
- `src/hotas_controller_device.cpp` - controller properties, input components, pose updates, and reconnect refresh
- `resources/input/vrchotas_virtual_profile.json` - SteamVR input profile
- `resources/input/vrcompositor_bindings_touch.json` - compositor bindings

## Notes

- If you change the shared structure, update the C# and C++ definitions together.
- If you change semantic buttons/axes, update the driver input profile and matching app-side layout constants together.
- If the CMake configure step fails with an OpenVR SDK error, verify `OPENVR_SDK_PATH` first.
