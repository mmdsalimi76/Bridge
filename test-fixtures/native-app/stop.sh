#!/bin/bash
if [ -f /tmp/native_app.pid ]; then
    kill $(cat /tmp/native_app.pid) 2>/dev/null || true
    rm -f /tmp/native_app.pid
fi
pkill -f "python3 main.py" 2>/dev/null || true
echo "Stopped native service."
