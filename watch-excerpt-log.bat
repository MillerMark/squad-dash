@echo off
REM Helper script to view excerpt diagnostic logs in real-time

set LOGFILE=%TEMP%\squaddash-excerpt-debug.log

echo ========================================
echo Excerpt Diagnostic Log Viewer
echo ========================================
echo.
echo Log file: %LOGFILE%
echo.

if not exist "%LOGFILE%" (
    echo [!] Log file does not exist yet.
    echo [i] It will be created when you create an excerpt attachment in SquadDash.
    echo.
    pause
    exit /b
)

echo [*] Showing last 50 lines and watching for new entries...
echo [*] Press Ctrl+C to exit
echo.
echo ========================================
echo.

powershell -Command "Get-Content '%LOGFILE%' -Tail 50 -Wait"
