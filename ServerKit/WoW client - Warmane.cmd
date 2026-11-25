@echo off
title WoW client - Warmane
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0wow-client-realm.ps1" -Target Warmane
echo.
pause
