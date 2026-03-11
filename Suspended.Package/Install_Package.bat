@echo off
:: ==============================================================================
:: SuspendedNTime Package Installer
:: This script circumvents execution policies and installs the certificate/package
:: Make sure to run this script as Administrator.
:: ==============================================================================
title SuspendedNTime Installer
color 0A

:: 1. Admin checks removed, UWP apps register per-user natively!

:: 2. Check and Enable Developer Mode gracefully (Bypass Microsoft's frustrating UI)
echo ===================================================
echo [INFO] Checking Developer Mode status...
reg query "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" /v AllowDevelopmentWithoutDevLicense | findstr "0x1" >nul
if %errorlevel% neq 0 (
    echo [WARNING] Developer Mode is NOT enabled. This is required for installation.
    :askDev
    set "devChoice="
    set /p devChoice="Voulez-vous activer le Mode Developpeur automatiquement ? (Y/N) : "
    if /i "!devChoice!"=="" (
        set devChoice=%devChoice%
    )
    if /i "%devChoice%"=="Y" (
        reg add "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" /t REG_DWORD /v AllowDevelopmentWithoutDevLicense /d 1 /f >nul
        echo [SUCCESS] Mode Developpeur active !
    ) else if /i "%devChoice%"=="N" (
        echo [INFO] Vous avez refuse d'activer le Mode Developpeur. L'installation native va probablement echouer.
    ) else (
        echo Reponse invalide. Veuillez taper Y ou N. 
        echo Une simple touche 'Entree' ne passe pas !
        echo.
        goto askDev
    )
) else (
    echo [INFO] Developer Mode is already enabled.
)
echo ===================================================
echo.

:: 3. Search for the MSIXBUNDLE and install it directly
echo ===================================================
echo [INFO] Searching for compiled MSIXBUNDLE...

set "BUNDLE_PATH="
for /r "%~dp0" %%i in (*.msixbundle) do (
    set "BUNDLE_PATH=%%i"
    goto :foundBundle
)
:foundBundle

if not "%BUNDLE_PATH%"=="" (
    echo [INFO] Found package: %BUNDLE_PATH%
    echo [EXEC] Force installing package for current user...
    PowerShell -NoProfile -ExecutionPolicy Bypass -Command "Add-AppxPackage -Path '%BUNDLE_PATH%'"
    
    if %errorlevel% equ 0 (
        echo.
        echo [SUCCESS] Application installed successfully!
    ) else (
        echo.
        echo [ERROR] Installation failed. See the red PowerShell text above.
    )
) else (
    echo [ERROR] Cannot find any .msixbundle file. 
    echo Please compile the Package project in Visual Studio first.
)

pause
