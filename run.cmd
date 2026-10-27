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
:: 1. Check and Build Bun Sidecar Engine
:: ---------------------------------------------------------
echo [*] Step 1/3: Checking Bun runtime and building Sidecar Engine...

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
:: 2. Check and Build React / Vite Frontend
:: ---------------------------------------------------------
echo [*] Step 2/3: Checking dependencies and building React UI...
if not exist "ui\node_modules" (
    echo [*] ui\node_modules not found. Running npm install in ui...
    call npm.cmd --prefix "%SCRIPT_DIR%ui" install
)

echo [*] Building React UI...
call npm.cmd --prefix "%SCRIPT_DIR%ui" run build
if errorlevel 1 (
    echo [ERROR] React / Vite build failed!
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
:: 3. Launch Agents Hub via .NET 10 (Windows GUI)
:: ---------------------------------------------------------
echo [*] Step 3/3: Starting Agents Hub via dotnet run (-f net10.0-windows)...
echo ========================================================
echo.

dotnet.exe run -f net10.0-windows %*
set "APP_ERR=%ERRORLEVEL%"

if %APP_ERR% neq 0 (
    echo.
    echo [!] Agents Hub exited with code %APP_ERR%.
)

exit /b %APP_ERR%
