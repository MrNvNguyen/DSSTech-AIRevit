@echo off
REM Install DSS.QSEngine add-in for Revit (default: 2025).
REM Close Revit before running.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\install-revit.ps1" -Year 2025
if errorlevel 1 (
  echo.
  echo FAILED — close Revit then run again. Or: scripts\build-revit.ps1 -Year 2025 first.
  pause
  exit /b 1
)
echo.
echo OK — restart Revit, open tab DSS.QSEngine.
pause
