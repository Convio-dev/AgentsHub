@echo off
setlocal
echo ========================================================
echo  Building Agents Hub and Installer Package
echo ========================================================

echo [1/2] Publishing standalone single-file binary...
dotnet publish -c Release
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Dotnet publish failed.
    exit /b %ERRORLEVEL%
)

echo [2/2] Compiling Inno Setup installer...
set "ISCC_EXE="
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" set "ISCC_EXE=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if not defined ISCC_EXE if exist "C:\Program Files\Inno Setup 6\ISCC.exe" set "ISCC_EXE=C:\Program Files\Inno Setup 6\ISCC.exe"

if not defined ISCC_EXE (
    echo [ERROR] Inno Setup 6 compiler ISCC.exe was not found.
    echo Please ensure Inno Setup 6 is installed.
    exit /b 1
)

"%ISCC_EXE%" AgentsHub.iss
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Inno Setup compilation failed.
    exit /b %ERRORLEVEL%
)

echo.
echo ========================================================
echo  SUCCESS: Setup installer created in dist\ folder!
echo ========================================================
