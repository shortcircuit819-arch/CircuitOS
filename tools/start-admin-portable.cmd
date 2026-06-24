@echo off
setlocal
title CircuitOS Control Core
set "DATA_PATH=%~dp0..\..\data"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0CircuitAdmin.ps1" -DataPath "%DATA_PATH%"
endlocal
