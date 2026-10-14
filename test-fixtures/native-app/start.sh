#!/bin/bash
nohup python3 main.py > /tmp/native_app.log 2>&1 &
echo $! > /tmp/native_app.pid
sleep 1
echo "Started native service on port 8085 with PID $(cat /tmp/native_app.pid)"
