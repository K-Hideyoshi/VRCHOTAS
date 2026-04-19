@echo off
setlocal EnableExtensions

cd /d "%~dp0"

set "BUILD_CONFIG=Release"

if /i "%~1"=="" goto :validate_config
if /i "%~1"=="Debug" (
    set "BUILD_CONFIG=Debug"
) else if /i "%~1"=="Release" (
    set "BUILD_CONFIG=Release"
) else (
    echo [ERROR] Invalid configuration: %~1
    echo Usage: deploy_driver.bat [Debug^|Release]
    exit /b 1
)

:validate_config
set "SOURCE_DLL=build\%BUILD_CONFIG%\driver_vrchotas.dll"
set "SOURCE_MANIFEST=resources\driver.vrchotas.vrdrivermanifest"
set "SOURCE_PROFILE=build\resources\input\vrchotas_virtual_profile.json"
set "TARGET_ROOT=%LOCALAPPDATA%\openvr\drivers\vrchotas"
set "TARGET_BIN=%TARGET_ROOT%\bin\win64"
set "TARGET_INPUT=%TARGET_ROOT%\resources\input"
set "VRPATHREG="

if not exist "%SOURCE_DLL%" (
    echo [ERROR] Missing build output: %SOURCE_DLL%
    echo Build VirtualDriver in %BUILD_CONFIG% mode before deploying.
    exit /b 1
)

if not exist "%SOURCE_MANIFEST%" (
    echo [ERROR] Missing manifest: %SOURCE_MANIFEST%
    exit /b 1
)

if not exist "%SOURCE_PROFILE%" (
    echo [ERROR] Missing input profile: %SOURCE_PROFILE%
    echo Rebuild VirtualDriver so the post-build step copies resources\input into the build folder.
    exit /b 1
)

if not exist "%TARGET_BIN%" mkdir "%TARGET_BIN%"
if not exist "%TARGET_INPUT%" mkdir "%TARGET_INPUT%"

copy /Y "%SOURCE_DLL%" "%TARGET_BIN%\driver_vrchotas.dll" >nul
copy /Y "%SOURCE_MANIFEST%" "%TARGET_ROOT%\driver.vrdrivermanifest" >nul
copy /Y "%SOURCE_PROFILE%" "%TARGET_INPUT%\vrchotas_virtual_profile.json" >nul

call :ResolveVrPathReg

if exist "%VRPATHREG%" (
    "%VRPATHREG%" adddriver "%TARGET_ROOT%"
) else (
    echo [WARN] SteamVR vrpathreg.exe was not found automatically. The driver files were copied, but the folder was not registered explicitly.
)

echo.
echo Deployment completed.
echo Build configuration: %BUILD_CONFIG%
echo Driver root: %TARGET_ROOT%
exit /b 0

:ResolveVrPathReg
if defined STEAMVR_PATH (
    if exist "%STEAMVR_PATH%\bin\win64\vrpathreg.exe" (
        set "VRPATHREG=%STEAMVR_PATH%\bin\win64\vrpathreg.exe"
        exit /b 0
    )
)

call :TrySteamRoot "HKCU\Software\Valve\Steam"
if defined VRPATHREG exit /b 0

call :TrySteamRoot "HKLM\SOFTWARE\WOW6432Node\Valve\Steam"
if defined VRPATHREG exit /b 0

call :TrySteamRoot "HKLM\SOFTWARE\Valve\Steam"
if defined VRPATHREG exit /b 0

for %%I in ("%ProgramFiles(x86)%\Steam\steamapps\common\SteamVR\bin\win64\vrpathreg.exe" "%ProgramFiles%\Steam\steamapps\common\SteamVR\bin\win64\vrpathreg.exe") do (
    if not defined VRPATHREG if exist "%%~I" set "VRPATHREG=%%~fI"
)

exit /b 0

:TrySteamRoot
for /f "delims=" %%I in ('powershell -NoProfile -Command "try { (Get-ItemProperty -Path ''Registry::%~1'' -Name SteamPath -ErrorAction Stop).SteamPath } catch { }"') do (
    if not defined VRPATHREG if exist "%%I\steamapps\common\SteamVR\bin\win64\vrpathreg.exe" set "VRPATHREG=%%I\steamapps\common\SteamVR\bin\win64\vrpathreg.exe"
)

exit /b 0
