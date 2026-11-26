@echo off
setlocal
set "PYTHONUTF8=1"
set "PYTHONIOENCODING=utf-8"
set "TRACKING=%~dp0Tracking"
set "PYTHONW=%TRACKING%\.venv\Scripts\pythonw.exe"
if not exist "%PYTHONW%" (
    echo Run Setup FruitCatch.bat first.
    pause
    exit /b 1
)
if not exist "%TRACKING%\fruitcatch_launcher.py" (
    echo FruitCatch launcher is missing. Check the distribution files.
    pause
    exit /b 1
)
start "" "%PYTHONW%" -X utf8 "%TRACKING%\fruitcatch_launcher.py"
