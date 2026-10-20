@echo off
chcp 65001 >nul 2>&1
echo.
echo   ================================================================
echo     Codex for Revit - Installer
echo   ================================================================
echo.
echo   Installing...
echo.
powershell -ExecutionPolicy Bypass -File "%~dp0scripts\install.ps1" -LocalZip "%~dp0" -Force
echo.
echo   Press any key to close.
pause >nul
