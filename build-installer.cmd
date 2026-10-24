@echo off
setlocal
title Agents Hub Multi-Target Release Builder

set "ARGS=%*"
if "%~1"=="" set "ARGS=win-x64 linux-x64 linux-arm64 osx-arm64 osx-x64"
if /i "%~1"=="win" set "ARGS=win-x64"
if /i "%~1"=="windows" set "ARGS=win-x64"
if /i "%~1"=="linux" set "ARGS=linux-x64 linux-arm64"
if /i "%~1"=="osx" set "ARGS=osx-arm64 osx-x64"
if /i "%~1"=="mac" set "ARGS=osx-arm64 osx-x64"
if /i "%~1"=="all" set "ARGS=win-x64 linux-x64 linux-arm64 osx-arm64 osx-x64"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\BuildTargets.ps1" -Targets "%ARGS%"
exit /b %ERRORLEVEL%
