@echo off
setlocal
title "Agents Hub - Build and Run"

set "SCRIPT_DIR=%~dp0"
cd /d "%SCRIPT_DIR%"

echo ========================================================
echo  Agents Hub - Build and Launch Pipeline
echo ========================================================
echo.

:: ---------------------------------------------------------
:: 0. Close existing running instances to free file locks
:: ---------------------------------------------------------
taskkill /F /IM AgentsHub.exe >nul 2>&1
taskkill /F /IM AgentsEngine.exe >nul 2>&1

:: ---------------------------------------------------------
:: 1. Check the Antigravity CLI
:: ---------------------------------------------------------
:: The Antigravity engine drives `agy`. Without it that engine cannot start, and profile
:: switching has nothing to switch. DeepSeek mode works regardless, so a missing CLI warns
:: instead of stopping the run.
echo [*] Step 1/4: Checking the Antigravity CLI (agy)...

set "AGY_CMD="

call :FindAgy
if not "%AGY_CMD%"=="" goto :AgyReady

echo [!] agy was not found, so the Antigravity engine has no CLI to run.
echo [*] Installing the Antigravity CLI...
powershell -NoProfile -ExecutionPolicy Bypass -Command "irm https://antigravity.google/cli/install.ps1 | iex"
echo [*] Installer finished with code %ERRORLEVEL%.

:: A fresh install writes a new PATH entry, and this window still holds the old one. Probe the
:: known locations directly before believing it failed.
call :FindAgy
if not "%AGY_CMD%"=="" goto :AgyReady

set "AGY_DIR=%USERPROFILE%\.antigravity\bin"
if exist "%AGY_DIR%\agy.exe" (
    set "PATH=%AGY_DIR%;%PATH%"
    set "AGY_CMD=%AGY_DIR%\agy.exe"
    goto :AgyReady
)

if exist "%APPDATA%\npm\agy.cmd" (
    set "PATH=%APPDATA%\npm;%PATH%"
    set "AGY_CMD=%APPDATA%\npm\agy.cmd"
    goto :AgyReady
)

echo [WARN] agy is still missing. Antigravity chats will not start until it is installed.
echo        Install it, then close and reopen this window so PATH is picked up:
echo          irm https://antigravity.google/cli/install.ps1 ^| iex
echo.
goto :AgyDone

:AgyReady
echo [OK] Found %AGY_CMD%.
echo.

:AgyDone

:: ---------------------------------------------------------
:: 2. Check and Build Bun Sidecar Engine
:: ---------------------------------------------------------
echo [*] Step 2/4: Checking Bun runtime and building Sidecar Engine...

set "BUN_CMD="
if exist "%USERPROFILE%\.bun\bin\bun.exe" (
    set "BUN_CMD=%USERPROFILE%\.bun\bin\bun.exe"
) else (
    where bun.exe >nul 2>&1
    if not errorlevel 1 (
        set "BUN_CMD=bun.exe"
    )
)

if "%BUN_CMD%"=="" (
    echo [!] Bun not found in PATH or %USERPROFILE%\.bun\bin
    echo [*] Attempting automated Bun installation...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "irm bun.sh/install.ps1 | iex"
    if exist "%USERPROFILE%\.bun\bin\bun.exe" (
        set "BUN_CMD=%USERPROFILE%\.bun\bin\bun.exe"
        echo [OK] Bun installed successfully.
    ) else (
        echo [ERROR] Failed to locate or install Bun. Please install manually from https://bun.sh
        pause
        exit /b 1
    )
)

echo [*] Compiling Sidecar Engine (win-x64)...
"%BUN_CMD%" run "%SCRIPT_DIR%build.ts" --target=win-x64
if errorlevel 1 (
    echo [ERROR] Failed to compile Sidecar Engine!
    pause
    exit /b 1
)

:: Sync binary to Debug build output directory if present
if exist "bin\Debug\net10.0-windows" (
    if not exist "bin\Debug\net10.0-windows\bin\win-x64" mkdir "bin\Debug\net10.0-windows\bin\win-x64" >nul 2>&1
    copy /y "bin\win-x64\AgentsEngine.exe" "bin\Debug\net10.0-windows\bin\win-x64\AgentsEngine.exe" >nul 2>&1
)

echo [OK] Sidecar Engine built successfully.
echo.

:: ---------------------------------------------------------
:: 3. Check and Build React / Vite Frontend
:: ---------------------------------------------------------
echo [*] Step 3/4: Checking dependencies and building React UI...
if not exist "ui\node_modules" (
    echo [*] ui\node_modules not found. Running npm install in ui...
    call npm.cmd --prefix "%SCRIPT_DIR%ui" install
)

echo [*] Building React UI...
call npm.cmd --prefix "%SCRIPT_DIR%ui" run build
if errorlevel 1 (
    echo.
    echo [ERROR] React / Vite build failed. The message above names the file and line.
    echo         If it points at a type error that no longer exists in the source, the installed
    echo         dependencies are stale: delete ui\node_modules and ui\package-lock.json,
    echo         then run this script again.
    echo.
    pause
    exit /b 1
)

:: Sync ui/dist to Debug build output directory if present
if exist "bin\Debug\net10.0-windows" (
    if not exist "bin\Debug\net10.0-windows\ui\dist" mkdir "bin\Debug\net10.0-windows\ui\dist" >nul 2>&1
    xcopy /e /y /i /q "ui\dist\*" "bin\Debug\net10.0-windows\ui\dist" >nul 2>&1
)

echo [OK] React frontend built successfully.
echo.

:: ---------------------------------------------------------
:: 4. Launch Agents Hub via .NET 10 (Windows GUI)
:: ---------------------------------------------------------
echo [*] Step 4/4: Starting Agents Hub via dotnet run (-f net10.0-windows)...
echo ========================================================
echo.

dotnet.exe run -f net10.0-windows %*
set "APP_ERR=%ERRORLEVEL%"

if %APP_ERR% neq 0 (
    echo.
    echo [!] Agents Hub exited with code %APP_ERR%.
)

exit /b %APP_ERR%

:: ---------------------------------------------------------
:: Subroutines
:: ---------------------------------------------------------
:FindAgy
:: Two names: the installer may add either, and PATH lookups are case-insensitive but not
:: extension-inventing, so both are probed explicitly.
where agy.exe >nul 2>&1
if not errorlevel 1 (
    set "AGY_CMD=agy.exe"
    goto :eof
)
where agy >nul 2>&1
if not errorlevel 1 set "AGY_CMD=agy"
goto :eof
