@echo off
REM Chay release tu PowerShell NGOAI Cursor (tranh Co-authored-by Cursor trong git)
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\release-full.ps1" %*
pause
