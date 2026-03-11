@echo off
:: ==============================================================================
:: SuspendedNTime Package Uninstaller
:: This script force closes the backend and removes the UWP package completely.
:: Make sure to run this script as Administrator.
:: ==============================================================================
title SuspendedNTime Uninstaller
color 0C

:: 1. Admin check removed.

echo ===================================================
echo [INFO] FORCE CLOSING SuspendedNTime PROCESSES...
echo ===================================================
:: Suppress error output if the process is not already running
taskkill /F /IM "Suspended.Backend.exe" /T >nul 2>&1
if %errorlevel% equ 0 (
    echo [SUCCESS] Backend process forcefully terminated.
) else (
    echo [INFO] Backend process was not running.
)

:: Wait 1 second to let Windows free up memory completely
timeout /t 1 /nobreak >nul

echo.
echo ===================================================
echo [INFO] UNINSTALLING THE UWP PACKAGE...
echo ===================================================
:: Tell PowerShell to find any package containing "SuspendedNTime" and remove it for current user
PowerShell -NoProfile -ExecutionPolicy Bypass -Command "Get-AppxPackage -Name *SuspendedNTime* | Remove-AppxPackage"

if %errorlevel% equ 0 (
    echo [SUCCESS] Old SuspendedNTime package has been completely uninstalled!
    echo You can now run Install_Package.bat to install the new version.
) else (
    echo [ERROR] Uninstallation failed. See PowerShell output above.
)
echo.

pause
