@echo off
setlocal
title Agents Hub Release Publisher
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\PublishReleaseGui.ps1"
