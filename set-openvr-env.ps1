<#
设置 OPENVR_SDK_PATH 环境变量的脚本。

用法：在 PowerShell 中运行：
	.\set-openvr-env.ps1

该脚本会在当前会话中设置环境变量。如果需要持久化到当前用户（会影响后续新的会话），脚本默认已启用持久化。
#>

$envPath = 'D:\Programming\Workspace\VRCHOTAS\artifacts\release\openvr-sdk-cache\sdk'
$env:OPENVR_SDK_PATH = $envPath
Write-Host "OPENVR_SDK_PATH 已为当前会话设置: $env:OPENVR_SDK_PATH"

# 将环境变量持久化到当前用户（需要重新打开终端或重启应用以使其生效）
[Environment]::SetEnvironmentVariable('OPENVR_SDK_PATH', $envPath, 'User')
Write-Host "OPENVR_SDK_PATH 已持久化到用户环境: $envPath"
