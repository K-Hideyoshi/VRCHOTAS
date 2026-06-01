param(
	[string]$Configuration = "Release",
	[string]$RuntimeIdentifier = "win-x64",
	[string]$OpenVrSdkPath,
	[string]$CertificatePath = $env:VRCHOTAS_SIGN_PFX_PATH,
	[string]$CertificatePassword = $env:VRCHOTAS_SIGN_PFX_PASSWORD,
	[string]$TimestampUrl = "http://timestamp.digicert.com",
	[string]$SignToolPath,
	[switch]$RequireSigning,
	[switch]$SkipPortable
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$versionFilePath = Join-Path $repoRoot "version.json"
$projectPath = Join-Path $repoRoot "VRCHOTAS\VRCHOTAS.csproj"
$overlayHelperProjectPath = Join-Path $repoRoot "VRCHOTAS.OverlayHelper\VRCHOTAS.OverlayHelper.csproj"
$virtualDriverRoot = Join-Path $repoRoot "VirtualDriver"
$virtualDriverBuildRoot = Join-Path $virtualDriverRoot "build"
$artifactsRoot = Join-Path $repoRoot "artifacts\release"

function Get-ReleaseVersion {
	param([string]$VersionFilePath)

	if (-not (Test-Path $VersionFilePath)) {
		throw "Version file '$VersionFilePath' was not found."
	}

	$versionDocument = Get-Content -Path $VersionFilePath -Raw | ConvertFrom-Json
	$version = $versionDocument.version
	if ([string]::IsNullOrWhiteSpace($version)) {
		throw "Version file '$VersionFilePath' does not contain a non-empty 'version' value."
	}

	if ($version -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z\.-]+)?$') {
		throw "Version '$version' in '$VersionFilePath' is not a valid semantic version."
	}

	return $version
}

$Version = Get-ReleaseVersion -VersionFilePath $versionFilePath
$publishRoot = Join-Path $artifactsRoot (Join-Path "publish" $RuntimeIdentifier)
$portableRoot = Join-Path $artifactsRoot "portable\VRCHOTAS"
$portableArchivePath = Join-Path $artifactsRoot ("VRCHOTAS-{0}-portable.zip" -f $Version)
$tempRoot = Join-Path $artifactsRoot "temp"
$sdkCacheRoot = Join-Path $artifactsRoot "openvr-sdk-cache"
$sdkCacheMetadataPath = Join-Path $sdkCacheRoot "metadata.json"
$sdkCacheSdkRoot = Join-Path $sdkCacheRoot "sdk"
$script:CleanupPaths = [System.Collections.Generic.List[string]]::new()

function Resolve-SignToolPath {
	param([string]$ConfiguredPath)

	if ($ConfiguredPath -and (Test-Path $ConfiguredPath)) {
		return (Resolve-Path $ConfiguredPath).Path
	}

	$kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
	if (-not (Test-Path $kitsRoot)) {
		return $null
	}

	$candidate = Get-ChildItem -Path $kitsRoot -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
		Where-Object { $_.FullName -like "*\x64\signtool.exe" } |
		Sort-Object FullName -Descending |
		Select-Object -First 1

	if ($null -ne $candidate) {
		return $candidate.FullName
	}

	return $null
}

function Assert-CommandExists {
	param([string]$CommandName)

	if (-not (Get-Command $CommandName -ErrorAction SilentlyContinue)) {
		throw "Required command '$CommandName' was not found in PATH."
	}
}

function Register-CleanupPath {
	param([string]$Path)

	if (-not [string]::IsNullOrWhiteSpace($Path)) {
		$script:CleanupPaths.Add($Path)
	}
}

function Resolve-OpenVrSdkRoot {
	param([string]$SearchRoot)

	if ([string]::IsNullOrWhiteSpace($SearchRoot) -or -not (Test-Path $SearchRoot)) {
		return $null
	}

	$directHeaderPath = Join-Path $SearchRoot "headers\openvr_driver.h"
	$directLibraryPath = Join-Path $SearchRoot "lib\win64\openvr_api.lib"
	if ((Test-Path $directHeaderPath) -and (Test-Path $directLibraryPath)) {
		return (Resolve-Path $SearchRoot).Path
	}

	$candidateHeader = Get-ChildItem -Path $SearchRoot -Filter openvr_driver.h -Recurse -File -ErrorAction SilentlyContinue |
		Select-Object -First 1
	if ($null -eq $candidateHeader) {
		return $null
	}

	$candidateRoot = Split-Path -Parent (Split-Path -Parent $candidateHeader.FullName)
	$candidateLibraryPath = Join-Path $candidateRoot "lib\win64\openvr_api.lib"
	if (Test-Path $candidateLibraryPath) {
		return $candidateRoot
	}

	return $null
}

function Get-LatestOpenVrSdkDownloadInfo {
	$headers = @{
		"Accept" = "application/vnd.github+json"
		"User-Agent" = "VRCHOTAS-Packager"
	}

	$release = Invoke-RestMethod -Uri "https://api.github.com/repos/ValveSoftware/openvr/releases/latest" -Headers $headers
	$asset = $release.assets |
		Where-Object { $_.name -match 'openvr.*\.(zip|7z)$' } |
		Sort-Object name -Descending |
		Select-Object -First 1

	if ($null -ne $asset) {
		return [pscustomobject]@{
			Version = if ([string]::IsNullOrWhiteSpace($release.tag_name)) { [string]$release.id } else { $release.tag_name }
			Name = $asset.name
			Url = $asset.browser_download_url
			Kind = "release asset"
		}
	}

	if (-not [string]::IsNullOrWhiteSpace($release.zipball_url)) {
		$name = if ([string]::IsNullOrWhiteSpace($release.tag_name)) { "openvr-latest-source.zip" } else { "openvr-$($release.tag_name)-source.zip" }
		return [pscustomobject]@{
			Version = if ([string]::IsNullOrWhiteSpace($release.tag_name)) { [string]$release.id } else { $release.tag_name }
			Name = $name
			Url = $release.zipball_url
			Kind = "release source zipball"
		}
	}

	throw "Could not find a downloadable OpenVR SDK archive in the latest GitHub release metadata."
}

function Get-OpenVrSdkPath {
	param([string]$ConfiguredPath)

	if (-not [string]::IsNullOrWhiteSpace($ConfiguredPath)) {
		$resolvedConfiguredPath = Resolve-OpenVrSdkRoot -SearchRoot $ConfiguredPath
		if ($null -eq $resolvedConfiguredPath) {
			throw "Configured OpenVR SDK path '$ConfiguredPath' does not contain headers\\openvr_driver.h and lib\\win64\\openvr_api.lib."
		}

		Write-Host "Using OpenVR SDK from configured path: $resolvedConfiguredPath"
		return $resolvedConfiguredPath
	}

	$downloadInfo = Get-LatestOpenVrSdkDownloadInfo
	$cachedVersion = $null
	$cachedSdkPath = Resolve-OpenVrSdkRoot -SearchRoot $sdkCacheSdkRoot

	if (Test-Path $sdkCacheMetadataPath) {
		try {
			$cachedMetadata = Get-Content -Path $sdkCacheMetadataPath -Raw | ConvertFrom-Json
			$cachedVersion = $cachedMetadata.Version
		}
		catch {
			$cachedVersion = $null
		}
	}

	if (($cachedVersion -eq $downloadInfo.Version) -and ($null -ne $cachedSdkPath)) {
		Write-Host "Using cached OpenVR SDK: $cachedVersion"
		return $cachedSdkPath
	}

	if (Test-Path $sdkCacheRoot) {
		if ($cachedVersion -and ($cachedVersion -ne $downloadInfo.Version)) {
			Write-Host "OpenVR SDK update detected: $cachedVersion -> $($downloadInfo.Version). Refreshing cache."
		}
		elseif ($null -eq $cachedSdkPath) {
			Write-Host "Existing OpenVR SDK cache is invalid. Re-downloading latest version."
		}

		Remove-Item -Path $sdkCacheRoot -Recurse -Force -ErrorAction SilentlyContinue
	}

	New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
	$downloadRoot = Join-Path $tempRoot ("openvr-sdk-" + [Guid]::NewGuid().ToString('N'))
	$archivePath = Join-Path $downloadRoot "openvr-sdk.zip"
	$extractRoot = Join-Path $downloadRoot "extract"

	New-Item -ItemType Directory -Force -Path $downloadRoot | Out-Null
	Register-CleanupPath -Path $downloadRoot

	Write-Host ("Downloading OpenVR SDK from GitHub ({0}): {1}" -f $downloadInfo.Kind, $downloadInfo.Url)
	Invoke-WebRequest -Uri $downloadInfo.Url -OutFile $archivePath -Headers @{ "User-Agent" = "VRCHOTAS-Packager" }

	New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
	Expand-Archive -Path $archivePath -DestinationPath $extractRoot -Force

	$resolvedDownloadedPath = Resolve-OpenVrSdkRoot -SearchRoot $extractRoot
	if ($null -eq $resolvedDownloadedPath) {
		throw "Downloaded OpenVR SDK archive did not contain the expected headers and win64 library layout."
	}

	New-Item -ItemType Directory -Force -Path $sdkCacheRoot | Out-Null
	New-Item -ItemType Directory -Force -Path $sdkCacheSdkRoot | Out-Null
	Copy-Item -Path (Join-Path $resolvedDownloadedPath '*') -Destination $sdkCacheSdkRoot -Recurse -Force
	[pscustomobject]@{
		Version = $downloadInfo.Version
		SourceName = $downloadInfo.Name
		SourceKind = $downloadInfo.Kind
		DownloadedAtUtc = [DateTime]::UtcNow.ToString("o")
	} | ConvertTo-Json | Set-Content -Path $sdkCacheMetadataPath -Encoding UTF8

	Write-Host "Using OpenVR SDK downloaded from GitHub and cached locally."
	return (Resolve-Path $sdkCacheSdkRoot).Path
}

function Invoke-External {
	param(
		[Parameter(Mandatory)] [string]$FilePath,
		[Parameter(Mandatory)] [string[]]$ArgumentList,
		[string]$WorkingDirectory = $repoRoot
	)

	Write-Host ("==> {0} {1}" -f $FilePath, ($ArgumentList -join ' '))
	Push-Location $WorkingDirectory
	try {
		& $FilePath @ArgumentList
		if ($LASTEXITCODE -ne 0) {
			throw "Command failed with exit code ${LASTEXITCODE}: $FilePath"
		}
	}
	finally {
		Pop-Location
	}
}

function Copy-DriverPayload {
	param([string]$DestinationRoot)

	$manifestSourcePath = Join-Path $virtualDriverRoot "resources\driver.vrchotas.vrdrivermanifest"
	$driverDllSourcePath = Join-Path $virtualDriverBuildRoot (Join-Path $Configuration "driver_vrchotas.dll")
	$inputSourceDirectory = Join-Path $virtualDriverBuildRoot "resources\input"

	foreach ($requiredPath in @($manifestSourcePath, $driverDllSourcePath, $inputSourceDirectory)) {
		if (-not (Test-Path $requiredPath)) {
			throw "Driver payload is incomplete. Missing '$requiredPath'. Build VirtualDriver Release before packaging."
		}
	}

	$payloadRoot = Join-Path $DestinationRoot "DriverPayload"
	$payloadBinDirectory = Join-Path $payloadRoot "bin\win64"
	$payloadInputDirectory = Join-Path $payloadRoot "resources\input"

	New-Item -ItemType Directory -Force -Path $payloadBinDirectory | Out-Null
	New-Item -ItemType Directory -Force -Path $payloadInputDirectory | Out-Null

	Copy-Item -Path $manifestSourcePath -Destination (Join-Path $payloadRoot "driver.vrdrivermanifest") -Force
	Copy-Item -Path $driverDllSourcePath -Destination (Join-Path $payloadBinDirectory "driver_vrchotas.dll") -Force
	Copy-Item -Path (Join-Path $inputSourceDirectory "*") -Destination $payloadInputDirectory -Recurse -Force
}

function Invoke-SignFile {
	param([string]$Path)

	if (-not (Test-Path $Path)) {
		return
	}

	if (-not $script:IsSigningEnabled) {
		if ($RequireSigning) {
			throw "Signing is required, but no usable signing certificate was provided."
		}

		return
	}

	$arguments = [System.Collections.Generic.List[string]]::new()
	$arguments.Add("sign")
	$arguments.Add("/fd")
	$arguments.Add("SHA256")
	$arguments.Add("/td")
	$arguments.Add("SHA256")
	$arguments.Add("/tr")
	$arguments.Add($TimestampUrl)
	$arguments.Add("/f")
	$arguments.Add($CertificatePath)

	if (-not [string]::IsNullOrWhiteSpace($CertificatePassword)) {
		$arguments.Add("/p")
		$arguments.Add($CertificatePassword)
	}

	$arguments.Add($Path)

	Invoke-External -FilePath $script:ResolvedSignToolPath -ArgumentList $arguments.ToArray()
}

Assert-CommandExists -CommandName "cmake"
Assert-CommandExists -CommandName "dotnet"

$ResolvedSignToolPath = Resolve-SignToolPath -ConfiguredPath $SignToolPath
$IsSigningEnabled = -not [string]::IsNullOrWhiteSpace($CertificatePath) -and (Test-Path $CertificatePath) -and -not [string]::IsNullOrWhiteSpace($ResolvedSignToolPath)
if ($RequireSigning -and -not $IsSigningEnabled) {
	throw "Signing was requested but signtool.exe or the PFX certificate was not available."
}

try {
	$resolvedOpenVrSdkPath = Get-OpenVrSdkPath -ConfiguredPath $OpenVrSdkPath

	New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
	Remove-Item -Path $publishRoot, $portableRoot -Recurse -Force -ErrorAction SilentlyContinue
	Remove-Item -Path $portableArchivePath -Force -ErrorAction SilentlyContinue

	Invoke-External -FilePath "cmake" -ArgumentList @(
		"-S", $virtualDriverRoot,
		"-B", $virtualDriverBuildRoot,
		"-A", "x64",
		"-DOPENVR_SDK_PATH=$resolvedOpenVrSdkPath"
	)

	Invoke-External -FilePath "cmake" -ArgumentList @(
		"--build", $virtualDriverBuildRoot,
		"--config", $Configuration
	)

	$versionParts = $Version.Split('.')
	while ($versionParts.Count -lt 4) {
		$versionParts += '0'
	}

	$assemblyVersion = ($versionParts[0..3] -join '.')
	$publishDirArgument = "/p:PublishDir=$publishRoot\"
	$overlayHelperOutputDirectory = Join-Path $repoRoot ("VRCHOTAS.OverlayHelper\bin\{0}\net10.0-windows" -f $Configuration)

	Invoke-External -FilePath "dotnet" -ArgumentList @(
		"build", $overlayHelperProjectPath,
		"-c", $Configuration
	)

	Invoke-External -FilePath "dotnet" -ArgumentList @(
		"publish", $projectPath,
		"-c", $Configuration,
		"-r", $RuntimeIdentifier,
		"--self-contained", "true",
		"/p:Version=$Version",
		"/p:AssemblyVersion=$assemblyVersion",
		"/p:FileVersion=$assemblyVersion",
		"/p:InformationalVersion=$Version",
		"/p:OverlayHelperOutputDirectory=$overlayHelperOutputDirectory",
		$publishDirArgument
	)

	if (-not $SkipPortable) {
		New-Item -ItemType Directory -Force -Path $portableRoot | Out-Null
		Copy-Item -Path (Join-Path $publishRoot "*") -Destination $portableRoot -Recurse -Force
		Copy-DriverPayload -DestinationRoot $portableRoot

		Get-ChildItem -Path $portableRoot -File -Recurse |
			Where-Object { $_.Extension -in '.exe', '.dll' } |
			ForEach-Object { Invoke-SignFile -Path $_.FullName }

		Compress-Archive -Path (Join-Path $portableRoot '*') -DestinationPath $portableArchivePath -CompressionLevel Optimal
	}

	if (-not $IsSigningEnabled) {
		Write-Warning "Portable release artifacts were created without Authenticode signing. Provide VRCHOTAS_SIGN_PFX_PATH and VRCHOTAS_SIGN_PFX_PASSWORD for official releases."
	}

	Write-Host "Release packaging completed."
	Write-Host "Publish output: $publishRoot"
	if (-not $SkipPortable) {
		Write-Host "Portable archive: $portableArchivePath"
	}
}
finally {
	foreach ($cleanupPath in $script:CleanupPaths) {
		if (Test-Path $cleanupPath) {
			Remove-Item -Path $cleanupPath -Recurse -Force -ErrorAction SilentlyContinue
		}
	}
}
