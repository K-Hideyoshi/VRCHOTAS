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
  - [Build](#build)
  - [Deploy](#deploy)
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

VirtualDriver is expected to use the same memory layout as `VirtualControllerState` (defined in the Interop layer). The runtime loop is typically:

1. Open named shared memory and synchronization primitive (mutex).
2. On each `RunFrame()`:
   - Acquire mutex
   - Read latest `VirtualControllerState`
   - Release mutex
3. Push values to OpenVR input components:
   - Axis/button states
   - Hand pose (position + quaternion)
   - Linear/angular velocity

### Build

> The C++ VirtualDriver project is not contained in this solution. The following is the recommended generic build flow.

1. Install prerequisites:
   - Visual Studio with C++ Desktop workload
   - OpenVR SDK
   - CMake (if the driver uses CMake)
2. Configure build (example):

```powershell
cmake -S . -B .\build -A x64
cmake --build .\build --config Release
```

3. Ensure build output contains a valid OpenVR driver folder structure (for example `bin\win64\driver_*.dll` + `driver.vrdrivermanifest`).

### Deploy

1. Copy driver output to your SteamVR drivers directory, for example:

```text
%LOCALAPPDATA%\openvr\drivers\<your_driver_name>
```

2. Confirm required files exist:
   - `driver.vrdrivermanifest`
   - `bin\win64\<driver dll>`
   - resource/input profile files (if used)
3. Start VRCHOTAS and verify it is writing shared memory.
4. Restart SteamVR and inspect logs if the driver does not load.

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