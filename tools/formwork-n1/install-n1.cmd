@echo off
setlocal
cd /d "%~dp0\..\.."
echo.
echo === Formwork N1 Measure — build + install (Revit 2025) ===
echo Repo: %CD%
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-n1-measure.ps1" -Year 2025 -DevLink
set ERR=%ERRORLEVEL%
echo.
if %ERR% NEQ 0 (
  echo FAILED exit=%ERR%
  echo Tip: close Revit completely, then run this .cmd again.
  pause
  exit /b %ERR%
)
echo OK — CLOSE Revit if open, then open Revit 2025 + TEST FORMWORK:
echo   Add-Ins → External Tools → Formwork N1 Measure
pause
