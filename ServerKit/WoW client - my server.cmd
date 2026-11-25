@echo off
title WoW client - my server
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0wow-client-realm.ps1" -Target Server
echo.
pause
