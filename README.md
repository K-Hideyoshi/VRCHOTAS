# VRCHOTAS

[![Platform](https://img.shields.io/badge/platform-Windows-blue)](#prerequisites)
[![Runtime](https://img.shields.io/badge/.NET-10-purple)](#tech-stack)
[![UI](https://img.shields.io/badge/UI-WPF-512BD4)](#tech-stack)

VRCHOTAS is a Windows desktop mapper for HOTAS/joystick devices. It reads physical controller input, applies configurable mapping curves, and writes a virtual controller state to shared memory for a VR driver to consume.

## Table of Contents

- [Architecture](#architecture)
- [Features](#features)
- [Tech Stack](#tech-stack)
- [Project Structure](#project-structure)
- [VirtualDriver](#virtualdriver)
  - [Principle](#principle)
  - [Data Contract Notes](#data-contract-notes)
  - [Build](#build)
  - [Deploy](#deploy)
  - [Runtime Verification Checklist](#runtime-verification-checklist)
- [Prerequisites](#prerequisites)
- [Build and Run (ControllerApp)](#build-and-run-controllerapp)
- [Usage](#usage)
- [Configuration](#configuration)
- [Troubleshooting](#troubleshooting)
- [Roadmap](#roadmap)
- [Contributing](#contributing)
- [Security](#security)
- [License](#license)

## Architecture

The solution follows a hybrid two-process design:

1. **ControllerApp (this repository, `VRCHOTAS` WPF app)**
   - Polls DirectInput joystick/HOTAS devices
   - Applies mapping rules (deadzone, curve, saturation, invert)
   - Writes mapped state into a shared memory block

2. **VirtualDriver (OpenVR driver, C++ module)**
   - Reads the same shared memory block each frame
   - Converts mapped data to OpenVR input/pose updates
   - Exposes virtual left/right controller behavior to SteamVR

## Features

- Device discovery and polling via DirectInput
- Real-time axis and button monitoring
- Mapping editor with automatic source detection
- Button-to-axis style mapping (press = synthetic axis input)
- Axis shaping controls (deadzone, curve, saturation, invert)
- Configuration save/load
- Shared-memory IPC output for downstream consumers

## Tech Stack

- .NET 10 (`net10.0-windows`)
- WPF + MVVM (`CommunityToolkit.Mvvm`)
- `SharpDX.DirectInput`
- `Newtonsoft.Json`

## Project Structure

```text
VRCHOTAS/
  Interop/      # Shared memory contracts and writer channel
  Logging/      # Logging abstractions and implementations
  Models/       # Mapping and runtime state models
  Services/     # Joystick polling, mapping engine, configuration service
  ViewModels/   # WPF view models
  *.xaml        # WPF windows and views
```

## VirtualDriver

### Principle

The **ControllerApp is the writer** and VirtualDriver is the reader.

Writer-side contracts used by this repository:

- Shared memory name: `Local\\VRCHOTAS.VirtualController.State`
- Mutex name: `Local\\VRCHOTAS.VirtualController.State.Mutex`
- Structs: `VirtualControllerState` -> `Left`/`Right` `ControllerHandState`
- Layout attributes: `StructLayout(LayoutKind.Sequential, Pack = 1)`
- Fixed array lengths:
  - `Buttons`: 32 (`bool`)
  - `Axes`: 16 (`double`)
  - `Position`: 3 (`double`)
  - `Quaternion`: 4 (`double`) (default identity `[1,0,0,0]`)
  - `LinearVelocity`: 3 (`double`)
  - `AngularVelocity`: 3 (`double`)

VirtualDriver should mirror this exact binary contract in C++ (packing, field order, array sizes, and element types), then read the state in `RunFrame()`:

1. Open the named file mapping and named mutex.
2. In each frame, lock mutex, copy one full state snapshot, unlock mutex.
3. Convert snapshot into OpenVR input updates:
   - button components from `Buttons[]`
   - scalar/vector components from `Axes[]`, `Position[]`, `LinearVelocity[]`, `AngularVelocity[]`
   - orientation from `Quaternion[]`

Because the writer uses a short mutex wait (5 ms), the reader should keep lock duration minimal (copy-then-process pattern).

### Data Contract Notes

To avoid ABI mismatches between C# and C++:

- Use **1-byte bool** for button arrays in C++ (`uint8_t` is recommended).
- Use `double` for all numeric arrays (`Axes`, pose, velocities).
- Use explicit packing (`#pragma pack(push, 1)` / `#pragma pack(pop)`).
- Keep exact field order and fixed lengths.
- Assume little-endian Windows runtime.

Recommended C++ struct sketch:

```cpp
#pragma pack(push, 1)
struct ControllerHandState
{
    uint8_t buttons[32];
    double axes[16];
    double position[3];
    double quaternion[4];
    double linearVelocity[3];
    double angularVelocity[3];
};

struct VirtualControllerState
{
    ControllerHandState left;
    ControllerHandState right;
};
#pragma pack(pop)
```

### Build

The C++ VirtualDriver source is not part of this .NET solution, so this repository does not provide a direct build target for it.

Use the same ABI contract documented above when building your driver project.

Recommended build checklist:

1. Install prerequisites:
   - Visual Studio with C++ Desktop workload
   - OpenVR SDK
   - CMake (if your driver project is CMake-based)
2. Build and run this ControllerApp first (to validate writer side):

```powershell
dotnet restore .\VRCHOTAS\VRCHOTAS.csproj
dotnet build .\VRCHOTAS\VRCHOTAS.csproj -c Release
```

3. Configure x64 build for VirtualDriver (example):

```powershell
cmake -S . -B .\build -A x64
cmake --build .\build --config Release
```

4. Ensure output contains a valid OpenVR driver structure (for example `bin\\win64\\driver_*.dll` and `driver.vrdrivermanifest`).
5. Verify your C++ side struct definitions match `VirtualControllerState` exactly before runtime testing.

### Deploy

1. Deploy VirtualDriver to SteamVR, either by copying files or registering a driver folder.

Option A (copy):

```text
%LOCALAPPDATA%\openvr\drivers\<your_driver_name>
```

Option B (register folder):

```powershell
& "$env:ProgramFiles(x86)\Steam\steamapps\common\SteamVR\bin\win64\vrpathreg.exe" adddriver "<absolute-driver-folder>"
```

2. Confirm required files exist in deployed location:
   - `driver.vrdrivermanifest`
   - `bin\win64\<driver dll>`
   - resource/input profile files (if used)
3. Start `VRCHOTAS` first so shared memory writer is active.
4. Start/restart SteamVR so VirtualDriver can attach and read.
5. Validate behavior:
   - Input changes in VRCHOTAS monitor should be reflected in VR runtime.
   - If not, check struct packing/field alignment and shared memory names first.

### Runtime Verification Checklist

1. In VRCHOTAS, confirm joystick data updates in the monitor panel.
2. Create at least one mapping and verify output changes in preview.
3. Launch SteamVR and confirm the driver is loaded (no manifest/load errors).
4. Confirm virtual controller button/axis/pose values react to HOTAS input.
5. If values are incorrect, check in this order:
   - shared memory name and mutex name
   - struct packing and bool size
   - field order and array lengths
   - coordinate and quaternion interpretation in driver code

## Prerequisites

- Windows 10/11
- .NET 10 SDK
- A DirectInput-compatible joystick/HOTAS device
- SteamVR (for end-to-end VirtualDriver validation)

## Build and Run (ControllerApp)

```powershell
dotnet restore
dotnet build
dotnet run --project .\VRCHOTAS\VRCHOTAS.csproj
```

## Usage

1. Launch the app and verify devices are discovered.
2. Add or edit a mapping.
3. Use auto-detect to capture source input.
4. Select target and adjust mapping parameters.
5. Save configuration and validate preview output.

## Configuration

- Configurations are stored as JSON files.
- The app supports save, save-as, load, and default configuration selection.

## Troubleshooting

- **No device input shown**
  - Reconnect the joystick/HOTAS and refresh devices.
  - Check whether another app exclusively owns the device.
- **Driver does not react**
  - Validate shared memory names and struct layout compatibility.
  - Confirm the driver reads the latest mapped values each frame.
- **SteamVR driver load failure**
  - Re-check manifest path and driver folder structure.
  - Inspect SteamVR/OpenVR logs.
- **Values are scrambled or offset in VirtualDriver**
  - Verify C++ struct packing is `1` and bool array uses 1-byte elements.
  - Verify struct field order exactly matches `VirtualControllerState`.

## Roadmap

- Expand virtual input profile coverage
- Improve reconnect and fault-handling behavior
- Add configuration schema versioning and migration
- Add automated tests for mapping validation

## Contributing

1. Fork the repository.
2. Create a focused feature branch.
3. Keep changes small and reviewable.
4. Open a pull request with rationale and test notes.

## Security

Do not include secrets, tokens, or private credentials in code, commits, or configuration files.

## License

GNU GENERAL PUBLIC LICENSE Version 3