@echo off
rem ============================================================================================
rem Dreamport ServerKit - Manage WoW accounts.cmd
rem Double-click it: it opens the WoW account menu on your server ("sudo wow-account") over SSH.
rem Needs the SSH key %USERPROFILE%\.ssh\wow_droplet (README step 1) and setup-accounts.sh run once
rem on the server (README "Automatic accounts"). Nothing is stored on this PC.
rem ============================================================================================
title Manage WoW accounts
set "WOW_SERVER=145.241.232.229"
set "WOW_KEY=%USERPROFILE%\.ssh\wow_droplet"

if not exist "%WOW_KEY%" (
    echo The SSH key "%WOW_KEY%" was not found.
    echo See README.md, step 1 "SSH key".
    echo.
    pause
    exit /b 1
)
where ssh >nul 2>nul
if errorlevel 1 (
    echo The Windows ssh program was not found.
    echo Windows Settings - System - Optional features - add "OpenSSH Client", then try again.
    echo.
    pause
    exit /b 1
)

echo Connecting to your WoW server %WOW_SERVER% ...
echo.
ssh -t -o StrictHostKeyChecking=accept-new -i "%WOW_KEY%" ubuntu@%WOW_SERVER% "sudo wow-account"
if errorlevel 255 (
    echo.
    echo Could not connect to %WOW_SERVER%. Is the server running, and is this its address?
)
echo.
pause
