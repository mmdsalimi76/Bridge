@echo off
echo ========================================================
echo  Starting Local Test VPS Container
echo ========================================================
cd /d "%~dp0"
docker compose up -d --build
if %ERRORLEVEL% EQU 0 (
    echo.
    echo [SUCCESS] Test VPS is running!
    echo Host:     127.0.0.1
    echo SSH Port: 2222
    echo Username: root
    echo Password: rootpass
    echo --------------------------------------------------------
    echo You can now connect from VPS Manager settings!
) else (
    echo [ERROR] Failed to start test VPS. Ensure Docker Desktop is running.
)
pause
