# Release Packaging Guide

This document describes how to build and package the portable VRCHOTAS release.

## Overview

The repository ships only a portable release flow.

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

- `scripts\publish-release.ps1` builds `VirtualDriver` in `Release` mode.
- The script copies these build outputs into `DriverPayload\` inside the portable folder:
  - `driver.vrdrivermanifest`
  - `bin\win64\driver_vrchotas.dll`
  - `resources\input\...`
- On first app start, `SteamVrDriverDeploymentService` reads that bundled `DriverPayload` directory and performs the following automatic steps:
  1. Deploys the bundled driver files into the local OpenVR driver directory (`%LOCALAPPDATA%\openvr\drivers\vrchotas`)
  2. Registers the driver with SteamVR using `vrpathreg.exe adddriver`
  3. Automatically modifies `<Steam>\config\steamvr.vrsettings` to enable multiple driver support by setting `"activateMultipleDrivers": true` in the `steamvr` configuration section

### Automatic SteamVR Configuration

During deployment, VRCHOTAS automatically updates the SteamVR settings file to ensure multiple drivers can run simultaneously:

- The service locates `steamvr.vrsettings` by checking the `STEAMVR_PATH` environment variable first, then falling back to Steam registry paths
- If found, it reads the JSON file, ensures the `steamvr` section exists, and sets `"activateMultipleDrivers": true`
- The modified JSON file is written back with proper formatting and indentation
- If this step fails (missing file, permissions, or JSON parse errors), a warning is logged but deployment continues
- Manual configuration of `steamvr.vrsettings` is still possible if the automatic step does not complete successfully
## Resetting a Test Deployment

Use this script to remove the registered SteamVR driver and delete the deployed local driver files before testing deployment again:

```powershell
.\scripts\remove-deployed-driver.ps1
```

If you want to keep the deployed files but remove only the SteamVR registration:

```powershell
.\scripts\remove-deployed-driver.ps1 -SkipFileDeletion
```

### Resetting SteamVR Settings After Undeployment

If you need to revert the `activateMultipleDrivers` change after removing VRCHOTAS:

- The setting is not automatically reverted when the driver is removed
- To disable multiple driver support, manually edit `<Steam>\config\steamvr.vrsettings` and set `"activateMultipleDrivers": false` (or remove the property entirely if other applications don't require it)
- Alternatively, delete the entire `steamvr.vrsettings` file and let SteamVR regenerate it with default settings on next startup

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
