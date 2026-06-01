# Release Packaging Guide

This document describes how to build and package the portable VRCHOTAS release.

## Overview

The repository ships only a portable release flow.

The release now contains three deliverables that work together:

- `VRCHOTAS.exe` - the main desktop application
- `VRCHOTAS.OverlayHelper.exe` - the separate helper process that hosts OpenVR overlay initialization and rendering
- `DriverPayload\...` - the native SteamVR driver payload

## Version Management

- Edit `version.json` in the repository root.
- The `version` value in that file is the single version source for `VRCHOTAS`, `VirtualDriver`, and `scripts\publish-release.ps1`.
- Normal .NET builds, release packaging, and the native driver DLL version resource all read from the same `version.json` file.

## Package Portable Release

Example:

```powershell
$env:VRCHOTAS_SIGN_PFX_PATH = 'D:\certs\vrchotas-release.pfx'
$env:VRCHOTAS_SIGN_PFX_PASSWORD = 'your-password'

.\scripts\publish-release.ps1 `
  -RequireSigning
```

Outputs:

- `artifacts\release\portable\VRCHOTAS\`
- `artifacts\release\VRCHOTAS-<version>-portable.zip`

The portable output is expected to contain at least:

- `VRCHOTAS.exe`
- `VRCHOTAS.OverlayHelper.exe`
- `openvr_api.dll`
- `DriverPayload\driver.vrdrivermanifest`
- `DriverPayload\bin\win64\driver_vrchotas.dll`

## OpenVR SDK Resolution

- By default, the script checks the latest OpenVR release on GitHub before each package build.
- If the cached SDK version matches the latest GitHub release and the cache is valid, the script reuses the cached SDK directly.
- If a newer OpenVR release is found, the script deletes the old cached SDK, downloads the new archive, refreshes the cache, and then builds with the new version.
- The temporary download and extract scratch files are deleted after each run, but the validated SDK cache is preserved for future packaging runs.
- `-OpenVrSdkPath` is optional and only acts as an explicit local override.
- The script does not auto-read `OPENVR_SDK_PATH` from the environment for packaging defaults.
- Packaging requires outbound network access to GitHub unless you provide `-OpenVrSdkPath`.

## Signing

- If you omit signing inputs, the script still produces artifacts but warns that the release is unsigned.
- Use a valid Authenticode certificate through `VRCHOTAS_SIGN_PFX_PATH` and `VRCHOTAS_SIGN_PFX_PASSWORD` for official releases.

## Driver Payload in the Portable Package

- `scripts\publish-release.ps1` builds `VRCHOTAS.OverlayHelper` before publishing the main app.
- The main app publish output includes `VRCHOTAS.OverlayHelper.exe` beside `VRCHOTAS.exe`.
- `scripts\publish-release.ps1` builds `VirtualDriver` in `Release` mode.
- The script copies these build outputs into `DriverPayload\` inside the portable folder:
  - `driver.vrdrivermanifest`
  - `bin\win64\driver_vrchotas.dll`
  - `resources\input\...`
- On first app start, `SteamVrDriverDeploymentService` reads that bundled `DriverPayload` directory and deploys it into the local OpenVR driver directory.

## Resetting a Test Deployment

Use this script to remove the registered SteamVR driver and delete the deployed local driver files before testing deployment again:

```powershell
.\scripts\remove-deployed-driver.ps1
```

If you want to keep the deployed files but remove only the SteamVR registration:

```powershell
.\scripts\remove-deployed-driver.ps1 -SkipFileDeletion
```

Note:

- In some environments, `vrpathreg.exe removedriver` removes the deployed `vrchotas` directory but still leaves a stale path entry in `%LOCALAPPDATA%\openvr\openvrpaths.vrpath`.
- If `vrpathreg.exe show` still lists `%LOCALAPPDATA%\openvr\drivers\vrchotas` after cleanup, the test environment is not fully reset.
- For deployment testing, close SteamVR first and manually remove the stale `vrchotas` entry from `openvrpaths.vrpath` before re-testing automatic deployment.

## OpenVR SDK Usage

- The OpenVR SDK is used only when compiling the C++ driver in `VirtualDriver\CMakeLists.txt`.
- During packaging, `scripts\publish-release.ps1` checks GitHub for the latest SDK version and reuses or refreshes the local SDK cache automatically unless `-OpenVrSdkPath` is provided.
- The cached SDK is kept between release runs, while only temporary download and extract files are removed after each run.
- The portable app does not download or discover OpenVR SDK at runtime.
- At runtime, only SteamVR itself is detected, mainly so VRCHOTAS can find and run `vrpathreg.exe`.
