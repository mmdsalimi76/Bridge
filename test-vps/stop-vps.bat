@echo off
echo Stopping Local Test VPS Container...
cd /d "%~dp0"
docker compose down
echo Test VPS stopped.
pause
