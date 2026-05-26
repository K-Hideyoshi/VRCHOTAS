param(
	[switch]$SkipFileDeletion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$driverRoot = Join-Path $env:LOCALAPPDATA "openvr\drivers\vrchotas"

function Get-SteamPath {
	$registryCandidates = @(
		@{ Hive = "CurrentUser"; View = "Registry64"; Path = "Software\Valve\Steam" },
		@{ Hive = "CurrentUser"; View = "Registry32"; Path = "Software\Valve\Steam" },
		@{ Hive = "LocalMachine"; View = "Registry64"; Path = "SOFTWARE\WOW6432Node\Valve\Steam" },
		@{ Hive = "LocalMachine"; View = "Registry32"; Path = "SOFTWARE\WOW6432Node\Valve\Steam" },
		@{ Hive = "LocalMachine"; View = "Registry64"; Path = "SOFTWARE\Valve\Steam" },
		@{ Hive = "LocalMachine"; View = "Registry32"; Path = "SOFTWARE\Valve\Steam" }
	)

	foreach ($registryCandidate in $registryCandidates) {
		try {
			$registryHive = [Microsoft.Win32.RegistryHive]::$($registryCandidate.Hive)
			$registryView = [Microsoft.Win32.RegistryView]::$($registryCandidate.View)
			$baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey($registryHive, $registryView)
			try {
				$subKey = $baseKey.OpenSubKey($registryCandidate.Path)
				try {
					if ($null -ne $subKey) {
						$steamPath = $subKey.GetValue("SteamPath")
						if (-not [string]::IsNullOrWhiteSpace($steamPath)) {
							return $steamPath
						}
					}
				}
				finally {
					if ($null -ne $subKey) {
						$subKey.Dispose()
					}
				}
			}
			finally {
				$baseKey.Dispose()
			}
		}
		catch {
		}
	}

	foreach ($basePath in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
		if ([string]::IsNullOrWhiteSpace($basePath)) {
			continue
		}

		$candidate = Join-Path $basePath "Steam"
		if (Test-Path $candidate) {
			return $candidate
		}
	}

	return $null
}

function Get-VrPathRegPath {
	if (-not [string]::IsNullOrWhiteSpace($env:STEAMVR_PATH)) {
		$candidate = Join-Path $env:STEAMVR_PATH "bin\win64\vrpathreg.exe"
		if (Test-Path $candidate) {
			return $candidate
		}
	}

	$steamPath = Get-SteamPath
	if (-not [string]::IsNullOrWhiteSpace($steamPath)) {
		$candidate = Join-Path $steamPath "steamapps\common\SteamVR\bin\win64\vrpathreg.exe"
		if (Test-Path $candidate) {
			return $candidate
		}
	}

	foreach ($basePath in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
		if ([string]::IsNullOrWhiteSpace($basePath)) {
			continue
		}

		$candidate = Join-Path $basePath "Steam\steamapps\common\SteamVR\bin\win64\vrpathreg.exe"
		if (Test-Path $candidate) {
			return $candidate
		}
	}

	return $null
}

function Remove-ActivateMultipleDriversSetting {
	$steamPath = Get-SteamPath
	if ([string]::IsNullOrWhiteSpace($steamPath)) {
		Write-Warning "Steam installation path was not found automatically. steamvr.vrsettings cleanup was skipped."
		return
	}

	$steamVrSettingsPath = Join-Path $steamPath "config\steamvr.vrsettings"
	if (-not (Test-Path $steamVrSettingsPath)) {
		Write-Host "SteamVR settings file was not found at: $steamVrSettingsPath"
		return
	}

	$config = Get-Content -Path $steamVrSettingsPath -Raw | ConvertFrom-Json
	$steamVrConfig = $null
	$steamVrProperty = $config.PSObject.Properties["steamvr"]
	if ($null -ne $steamVrProperty) {
		$steamVrConfig = $steamVrProperty.Value
	}
	if ($null -eq $steamVrConfig) {
		Write-Host "No steamvr section was found in: $steamVrSettingsPath"
		return
	}

	if ($steamVrConfig.PSObject.Properties.Remove("activateMultipleDrivers")) {
		Write-Host "Removing activateMultipleDrivers from: $steamVrSettingsPath"
		$config | ConvertTo-Json -Depth 100 | Set-Content -Path $steamVrSettingsPath
		return
	}

	Write-Host "activateMultipleDrivers was not present in: $steamVrSettingsPath"
}

$vrPathRegPath = Get-VrPathRegPath
if (-not [string]::IsNullOrWhiteSpace($vrPathRegPath)) {
	Write-Host "Removing SteamVR driver registration: $driverRoot"
	& $vrPathRegPath removedriver $driverRoot
	if ($LASTEXITCODE -ne 0) {
		throw "vrpathreg.exe removedriver failed with exit code ${LASTEXITCODE}."
	}
}
else {
	Write-Warning "SteamVR vrpathreg.exe was not found automatically. Registration removal was skipped."
}

if (-not $SkipFileDeletion) {
	if (Test-Path $driverRoot) {
		Write-Host "Deleting deployed driver files: $driverRoot"
		Remove-Item -Path $driverRoot -Recurse -Force
	}
	else {
		Write-Host "No deployed driver files were found at: $driverRoot"
	}
}
else {
	Write-Host "Skipping deployed driver file deletion."
}

Remove-ActivateMultipleDriversSetting

Write-Host "Driver cleanup completed."
