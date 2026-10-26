@echo off
setlocal enabledelayedexpansion
title Agents Hub - Sidecar Engine Cross-Compiler

set "SCRIPT_DIR=%~dp0"
cd /d "%SCRIPT_DIR%"

:: Check if Bun is in PATH
where bun >nul 2>&1
if %ERRORLEVEL% equ 0 (
    set "BUN_CMD=bun"
    goto :RunBuild
)

:: Check if Bun is in standard userprofile directory
if exist "%USERPROFILE%\.bun\bin\bun.exe" (
    set "BUN_CMD=%USERPROFILE%\.bun\bin\bun.exe"
    goto :RunBuild
)

echo [!] Bun runtime not found on system.
echo [*] Downloading and installing Bun automatically...
powershell -NoProfile -ExecutionPolicy Bypass -Command "irm bun.sh/install.ps1 | iex"

if exist "%USERPROFILE%\.bun\bin\bun.exe" (
    set "BUN_CMD=%USERPROFILE%\.bun\bin\bun.exe"
    echo [✔] Bun installed successfully.
    goto :RunBuild
)

echo [ERROR] Failed to locate or install Bun. Please install manually from https://bun.sh
exit /b 1

:RunBuild
echo [*] Running Sidecar Cross-Compilation using %BUN_CMD%...
"%BUN_CMD%" run build.ts %*
exit /b %ERRORLEVEL%
