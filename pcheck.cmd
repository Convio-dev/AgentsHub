@echo off
setlocal
title Agents Hub - Cross-Platform Compatibility Audit (/pcheck)
set "BUN_EXE=%USERPROFILE%\.bun\bin\bun.exe"
if exist "%BUN_EXE%" (
    "%BUN_EXE%" run "%~dp0tools\pcheck.ts" %*
) else (
    bun run "%~dp0tools\pcheck.ts" %*
)
exit /b %ERRORLEVEL%
