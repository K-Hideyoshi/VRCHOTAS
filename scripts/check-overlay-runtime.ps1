param(
	[string]$Configuration = "Debug",
	[string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
	$OutputDirectory = Join-Path $repoRoot ("VRCHOTAS\bin\{0}\net10.0-windows" -f $Configuration)
}

function Test-RequiredFile {
	param([string]$Path)

	[pscustomobject]@{
		Path = $Path
		Exists = Test-Path $Path
	}
}

$requiredFiles = @(
	"VRCHOTAS.exe",
	"VRCHOTAS.OverlayHelper.exe",
	"VRCHOTAS.OverlayHelper.dll",
	"VRCHOTAS.OverlayHelper.deps.json",
	"VRCHOTAS.OverlayHelper.runtimeconfig.json",
	"openvr_api.dll",
	"SharpDX.dll",
	"SharpDX.Direct3D11.dll",
	"SharpDX.DXGI.dll"
) | ForEach-Object { Test-RequiredFile -Path (Join-Path $OutputDirectory $_) }

$steamVrProcesses = "vrserver", "vrmonitor", "vrcompositor" | ForEach-Object {
	[pscustomobject]@{
		Process = $_
		Running = $null -ne (Get-Process -Name $_ -ErrorAction SilentlyContinue)
	}
}

$logRoot = Join-Path $env:APPDATA "VRCHOTAS\logs"
$latestOverlayLog = Get-ChildItem -Path $logRoot -Filter "*overlay-helper*.log" -ErrorAction SilentlyContinue |
	Sort-Object LastWriteTime -Descending |
	Select-Object -First 1

Write-Host "Overlay output directory: $OutputDirectory"
Write-Host ""
Write-Host "Required files:"
$requiredFiles | Format-Table -AutoSize

Write-Host ""
Write-Host "SteamVR processes:"
$steamVrProcesses | Format-Table -AutoSize

Write-Host ""
if ($null -eq $latestOverlayLog) {
	Write-Host "No overlay-helper log was found under '$logRoot'."
}
else {
	Write-Host "Latest overlay-helper log: $($latestOverlayLog.FullName)"
	$interestingLines = Select-String -Path $latestOverlayLog.FullName -Pattern "OpenVR|D3D|Fallback|Waiting for SteamVR|Overlay status|failed|error" -CaseSensitive:$false -ErrorAction SilentlyContinue |
		Select-Object -Last 20
	if ($interestingLines) {
		$interestingLines | ForEach-Object { $_.Line }
	}
	else {
		Write-Host "No overlay diagnostic lines were found in the latest helper log."
	}
}

$missingFiles = $requiredFiles | Where-Object { -not $_.Exists }
if ($missingFiles) {
	throw "Overlay runtime check failed because required files are missing."
}
