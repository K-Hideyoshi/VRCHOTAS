# VRCHOTAS Troubleshooting Guide

This guide provides detailed troubleshooting steps for common issues when deploying and using VRCHOTAS.

## Quick Diagnostics

Before diving into specific issues, gather diagnostic information:

```powershell
# Check if SteamVR is installed
$steamPath = (Get-ItemProperty 'HKCU:\Software\Valve\Steam').SteamPath
"Steam Path: $steamPath"
"SteamVR installed: $(Test-Path "$steamPath\steamapps\common\SteamVR")"

# Check vrpathreg.exe availability
$vrpathreg = "$env:STEAMVR_PATH\bin\win64\vrpathreg.exe"
"vrpathreg found: $(Test-Path $vrpathreg)"

# List registered drivers
if (Test-Path $vrpathreg) {
	& $vrpathreg showdrivers
}

# Check VRCHOTAS deployment status
"VRCHOTAS driver deployed: $(Test-Path "$env:LOCALAPPDATA\openvr\drivers\vrchotas")"
"VRCHOTAS driver files:"
Get-ChildItem "$env:LOCALAPPDATA\openvr\drivers\vrchotas" -Recurse -ErrorAction SilentlyContinue

# Check SteamVR settings
$vrSettings = "$steamPath\config\steamvr.vrsettings"
if (Test-Path $vrSettings) {
	"activateMultipleDrivers setting:"
	(Get-Content $vrSettings | ConvertFrom-Json).steamvr.activateMultipleDrivers
}

# Check VRCHOTAS logs
"Latest VRCHOTAS logs:"
Get-ChildItem "$env:APPDATA\VRCHOTAS\logs" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 5
```

## Common Issues and Solutions

### Issue: Driver Not Detected by SteamVR

**Symptoms:**
- Virtual controllers do not appear in SteamVR
- `vrpathreg.exe showdrivers` does not list VRCHOTAS driver
- SteamVR crashes or freezes when loading drivers

**Diagnostic Steps:**

1. Verify VRCHOTAS deployment:
   ```powershell
   Test-Path "$env:LOCALAPPDATA\openvr\drivers\vrchotas\driver.vrdrivermanifest"
   Test-Path "$env:LOCALAPPDATA\openvr\drivers\vrchotas\bin\win64\driver_vrchotas.dll"
   Test-Path "$env:LOCALAPPDATA\openvr\drivers\vrchotas\resources\input\vrchotas_virtual_profile.json"
   ```

2. Check registration:
   ```powershell
   & "$env:STEAMVR_PATH\bin\win64\vrpathreg.exe" showdrivers | Select-String -Pattern "vrchotas"
   ```

3. Verify `steamvr.vrsettings`:
   ```powershell
   $vrSettings = (Get-ItemProperty 'HKCU:\Software\Valve\Steam').SteamPath + "\config\steamvr.vrsettings"
   (Get-Content $vrSettings | ConvertFrom-Json).steamvr.activateMultipleDrivers
   ```

**Solutions:**

1. **Files not deployed**: Run VRCHOTAS.exe at least once with SteamVR installed
   - Check application logs: `%APPDATA%\VRCHOTAS\logs\`
   - Look for errors mentioning "deployment" or "vrpathreg"

2. **Driver not registered**: Manually register the driver
   ```powershell
   & "$env:STEAMVR_PATH\bin\win64\vrpathreg.exe" adddriver "$env:LOCALAPPDATA\openvr\drivers\vrchotas"
   ```

3. **Multiple drivers not enabled**: Ensure `activateMultipleDrivers` is set to `true`
   ```powershell
   # Quick fix: Run VRCHOTAS again to auto-configure
   # Or manually edit the file and restart SteamVR
   ```

4. **Corrupted cached driver list**: Clear SteamVR device cache
   ```powershell
   $steamPath = (Get-ItemProperty 'HKCU:\Software\Valve\Steam').SteamPath
   Remove-Item "$steamPath\config\lighthouse" -Recurse -Force -ErrorAction SilentlyContinue
   Remove-Item "$steamPath\config\deviceconfig" -Recurse -Force -ErrorAction SilentlyContinue
   ```
   Then close and restart SteamVR.

### Issue: Configuration File Modification Fails

**Symptoms:**
- Application log shows "Failed to modify steamvr.vrsettings"
- `activateMultipleDrivers` is not set after running VRCHOTAS

**Common Reasons:**

1. **File Not Found**
   - Steam installation path not detected
   - SteamVR is installed in a non-standard location
   - Solution: Set `STEAMVR_PATH` environment variable explicitly

2. **Permission Denied**
   - VRCHOTAS lacks write permissions to `steamvr.vrsettings`
   - SteamVR is running and has the file locked
   - Solution: Close SteamVR before running VRCHOTAS, or run VRCHOTAS as administrator

3. **Invalid JSON**
   - `steamvr.vrsettings` is corrupted or has invalid JSON syntax
   - Solution: Restore from backup or delete and let SteamVR regenerate
   ```powershell
   # Backup and check for corruption
   $vrSettings = (Get-ItemProperty 'HKCU:\Software\Valve\Steam').SteamPath + "\config\steamvr.vrsettings"
   Copy-Item $vrSettings "$vrSettings.backup"
   try { Get-Content $vrSettings | ConvertFrom-Json } catch { "JSON is invalid" }
   ```

**Manual Configuration:**

If automatic configuration fails, manually edit `steamvr.vrsettings`:

```powershell
$steamPath = (Get-ItemProperty 'HKCU:\Software\Valve\Steam').SteamPath
$vrSettings = "$steamPath\config\steamvr.vrsettings"

# Read current settings
$config = Get-Content $vrSettings | ConvertFrom-Json

# Ensure steamvr section exists
if (-not $config.steamvr) {
	$config | Add-Member -Type NoteProperty -Name "steamvr" -Value @{}
}

# Set activateMultipleDrivers
$config.steamvr.activateMultipleDrivers = $true

# Write back with proper formatting
$config | ConvertTo-Json -Depth 10 | Set-Content $vrSettings

Write-Host "Configuration updated. Close and restart SteamVR."
```

### Issue: Configuration Applied But Driver Still Not Detected

**Symptoms:**
- `activateMultipleDrivers` is now set to `true`
- SteamVR was configured successfully
- But virtual controllers still do not appear in SteamVR

**Solutions:**

1. **SteamVR needs a restart**: Close SteamVR completely and restart it
   ```powershell
   # Ensure SteamVR is fully closed
   Get-Process vrserver -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
   Start-Sleep -Seconds 2
   ```

2. **Clear SteamVR device cache**: SteamVR caches driver information
   ```powershell
   $steamPath = (Get-ItemProperty 'HKCU:\Software\Valve\Steam').SteamPath
   Remove-Item "$steamPath\config\lighthouse" -Recurse -Force -ErrorAction SilentlyContinue
   Remove-Item "$steamPath\config\deviceconfig" -Recurse -Force -ErrorAction SilentlyContinue
   Write-Host "Cache cleared. Close and restart SteamVR."
   ```

3. **Check driver files are still in place**: Verify deployment wasn't affected
   ```powershell
   Test-Path "$env:LOCALAPPDATA\openvr\drivers\vrchotas\bin\win64\driver_vrchotas.dll"
   ```

4. **Re-register the driver**: If the registration was lost
   ```powershell
   & "$env:STEAMVR_PATH\bin\win64\vrpathreg.exe" adddriver "$env:LOCALAPPDATA\openvr\drivers\vrchotas"
   ```

### Issue: Input Not Responding

**Symptoms:**
- Virtual controllers appear in SteamVR but buttons/axes don't respond
- Controller state not updating in VRCHOTAS UI

**Diagnostic Steps:**

1. Verify VRCHOTAS is running: Check application window and device monitor

2. Verify device detection:
   - Open VRCHOTAS and check the device monitor for your HOTAS/joystick
   - Confirm the device shows axis/button input when you interact with it

3. Check shared memory communication:
   - Application logs should show heartbeat ticks
   - Look for "driver_heartbeat_tick_ms" in logs

4. Verify mappings:
   - Confirm mappings are created and enabled
   - Check that source devices and controls are correctly configured
   - Verify target mappings point to the intended controller axes/buttons

**Solutions:**

1. **Device not detected**:
   - Connect/reconnect your HOTAS device
   - Check Device Manager for the device
   - Verify it's a DirectInput device (not Raw Input only)
   - Try a different USB port

2. **Heartbeat missing**:
   - Restart VRCHOTAS
   - Check Windows Event Viewer for application crashes
   - Verify shared memory permissions
   - Run VRCHOTAS as administrator

3. **Mappings not working**:
   - Verify mapping is enabled (toggle button in UI)

### Issue: VR Overlay Not Visible

**Symptoms:**
- Master Switch or configuration toasts do not appear in the headset.
- The persistent Master ON marker does not appear.
- The desktop app still works and the SteamVR driver may still function.

**Important distinction:**

The VR Overlay is handled by `VRCHOTAS.OverlayHelper.exe`. It is separate from the SteamVR driver under `%LOCALAPPDATA%\openvr\drivers\vrchotas`, so driver deployment success does not prove the overlay is healthy.

**Diagnostic Steps:**

1. Verify the overlay helper payload exists in the app output directory:
   ```powershell
   Test-Path ".\VRCHOTAS.OverlayHelper.exe"
   Test-Path ".\openvr_api.dll"
   Test-Path ".\SharpDX.Direct3D11.dll"
   Test-Path ".\SharpDX.DXGI.dll"
   ```

2. Run the overlay runtime check from the repository root:
   ```powershell
   .\scripts\check-overlay-runtime.ps1 -Configuration Debug
   ```

3. Open Preferences -> VR Overlay and click **Show test toast**.

4. Check the latest helper log:
   ```powershell
   Get-ChildItem "$env:APPDATA\VRCHOTAS\logs\*overlay-helper*.log" |
       Sort-Object LastWriteTime -Descending |
       Select-Object -First 1 |
       Get-Content
   ```

5. Look for these status markers:
   - `WaitingForSteamVR`: SteamVR is not running or has not finished starting.
   - `OpenVrReady`: OpenVR initialized successfully.
   - `D3DReady`: D3D11 texture submission is active.
   - `FallbackRaw`: D3D11 failed and the helper switched to raw texture upload.
   - `SteamVrQuit`: SteamVR closed; the helper will reset and retry.
   - `LastError`: the helper saw an OpenVR or renderer error.

**Solutions:**

1. **SteamVR is not ready**: Start SteamVR, wait until `vrserver` and `vrcompositor` are running, then trigger a test toast.

2. **Helper files are missing**: Rebuild the app or release package. The main app output must include `VRCHOTAS.OverlayHelper.exe`, `openvr_api.dll`, and the SharpDX D3D DLLs.

3. **D3D11 path fails**: In Preferences -> VR Overlay, switch rendering mode to `RawCompatibility`, apply settings, and click **Show test toast** again.

4. **Overlay disappears after SteamVR restart**: Trigger another toast or toggle Master Switch. The helper now handles `VREvent_Quit` and should reinitialize automatically after SteamVR returns.

5. **Dashboard conflicts**: The helper hides VRCHOTAS overlays while the SteamVR Dashboard is visible. Close Dashboard before testing toast visibility.
   - Test the raw input: verify device monitor shows input
   - Review mapping configuration for typos or wrong selections
   - Check application logs for mapping processing errors

### Issue: SteamVR Crashes When Loading Driver

**Symptoms:**
- SteamVR crashes immediately after starting
- Event Viewer shows exceptions from `driver_vrchotas.dll`

**Diagnostic Steps:**

1. Check crash logs:
   ```powershell
   $steamPath = (Get-ItemProperty 'HKCU:\Software\Valve\Steam').SteamPath
   Get-ChildItem "$steamPath\logs" -Recurse | Where-Object Name -Like "*crash*" | Sort-Object LastWriteTime -Descending | Select-Object -First 3
   ```

2. Verify driver DLL integrity:
   ```powershell
   $dllPath = "$env:LOCALAPPDATA\openvr\drivers\vrchotas\bin\win64\driver_vrchotas.dll"
   [System.Reflection.AssemblyName]::GetAssemblyName($dllPath) | Select-Object FullName
   ```

**Solutions:**

1. **Missing dependencies**: Reinstall VRCHOTAS to re-deploy driver
   ```powershell
   Remove-Item "$env:LOCALAPPDATA\openvr\drivers\vrchotas" -Recurse -Force
   # Run VRCHOTAS again to redeploy
   ```

2. **Shared memory initialization failure**: Clear stale shared memory objects
   ```powershell
   # Restart computer (nuclear option) or:
   # Run: `Remove-Item $env:LOCALAPPDATA\openvr\drivers\vrchotas -Recurse -Force`
   # Then restart VRCHOTAS
   ```

3. **OpenVR version mismatch**: Update or downgrade SteamVR
   - Check OpenVR version used to compile driver
   - Match it with installed SteamVR version

### Issue: "activateMultipleDrivers" Setting Reverts or Disappears

**Symptoms:**
- Setting changes after modifying `steamvr.vrsettings`
- SteamVR overwrites changes on startup

**Explanation:**

SteamVR periodically regenerates sections of `steamvr.vrsettings`. This is normal behavior.

**Solutions:**

1. **Ensure VRCHOTAS runs after SteamVR updates**:
   - After updating SteamVR, run VRCHOTAS once to re-apply the setting
   - The automatic configuration detects and updates the setting each time VRCHOTAS starts

2. **Verify persistence**:
   ```powershell
   # Close SteamVR completely
   Get-Process vrserver -ErrorAction SilentlyContinue | Stop-Process -Force

   # Run VRCHOTAS to reapply setting
   Start-Process "path\to\VRCHOTAS.exe"

   # Verify setting persisted after SteamVR restart
   Start-Sleep -Seconds 5
   $setting = (Get-Content "$steamPath\config\steamvr.vrsettings" | ConvertFrom-Json).steamvr.activateMultipleDrivers
   Write-Host "Setting value: $setting"
   ```

## Advanced Troubleshooting

### Check Application Logs

VRCHOTAS logs detailed information to help diagnose issues:

```powershell
# List available logs
Get-ChildItem "$env:APPDATA\VRCHOTAS\logs" | Sort-Object LastWriteTime -Descending

# Read latest log
Get-Content (Get-ChildItem "$env:APPDATA\VRCHOTAS\logs" | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
```

Look for these key patterns:
- `[INFO] ... SteamVR driver deployed` - Deployment successful
- `[WARNING] ... Skipping driver deployment` - Deployment skipped (check why)
- `[ERROR] ... Failed to modify steamvr.vrsettings` - Configuration failed
- `[DEBUG] ... steamvr.vrsettings file not found` - Settings file not found

### Reset Everything

If all else fails, perform a clean reset:

```powershell
# 1. Close SteamVR and VRCHOTAS
Get-Process vrserver, VRCHOTAS -ErrorAction SilentlyContinue | Stop-Process -Force

# 2. Remove VRCHOTAS driver
& "$env:STEAMVR_PATH\bin\win64\vrpathreg.exe" removedriver "$env:LOCALAPPDATA\openvr\drivers\vrchotas"

# 3. Delete deployed driver files
Remove-Item "$env:LOCALAPPDATA\openvr\drivers\vrchotas" -Recurse -Force -ErrorAction SilentlyContinue

# 4. Clear SteamVR cache
$steamPath = (Get-ItemProperty 'HKCU:\Software\Valve\Steam').SteamPath
Remove-Item "$steamPath\config\lighthouse" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "$steamPath\config\deviceconfig" -Recurse -Force -ErrorAction SilentlyContinue

# 5. (Optional) Reset SteamVR settings
# Copy from backup or delete to let SteamVR regenerate
# Remove-Item "$steamPath\config\steamvr.vrsettings" -Force

# 6. Start fresh
Start-Process "path\to\VRCHOTAS.exe"
```

## Performance and Optimization

### High CPU Usage

If VRCHOTAS or SteamVR shows high CPU usage:

1. **Check device polling rate**:
   - VRCHOTAS polls devices continuously
   - Reduce the polling frequency in preferences if available

2. **Disable unnecessary mappings**:
   - Disable mappings you're not using
   - Remove old or unused configurations

3. **Check for driver conflicts**:
   - Disable other VR drivers temporarily
   - Test with only VRCHOTAS driver enabled

### Input Latency

If controller input feels sluggish or delayed:

1. **Check USB connection**:
   - Use powered USB hubs
   - Try different USB ports
   - Ensure cable quality

2. **Review frame timing**:
   - Check application logs for frame timing info
   - Verify driver heartbeat frequency
   - Ensure no OS background tasks interfere

3. **Reduce device count**:
   - Remove unused physical devices
   - Some systems have limits on simultaneous device polling

## Getting Help

If issues persist after troubleshooting:

1. **Collect diagnostic information**:
   - Output of the diagnostic script above
   - Latest log file from `%APPDATA%\VRCHOTAS\logs\`
   - Screenshot of the error message
   - Description of exact steps to reproduce

2. **Check existing issues**:
   - Visit [VRCHOTAS GitHub Issues](https://github.com/K-Hideyoshi/VRCHOTAS/issues)
   - Search for keywords related to your issue

3. **Report a new issue**:
   - Include all diagnostic information
   - Describe your hardware setup
   - Include step-by-step reproduction steps
