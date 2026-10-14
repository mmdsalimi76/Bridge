# Local Test VPS (Mock Environment)

This directory provides a fully functional Ubuntu Linux VPS simulation container running OpenSSH server, `cron`, Docker tools, and standard system utilities for testing VPS Manager locally.

---

## Quick Start

### 1. Start the Test VPS
Double-click `start-vps.bat` or run:
```bash
docker compose up -d --build
```

### 2. Stop the Test VPS
Double-click `stop-vps.bat` or run:
```bash
docker compose down
```

---

## Connection Details for VPS Manager

In **VPS Manager -> Settings**, add or select a profile with these parameters:

| Field | Value |
|---|---|
| **Host IP** | `127.0.0.1` |
| **SSH Port** | `2222` |
| **Username** | `root` (or `deploy`) |
| **Password** | `rootpass` (or `deploypass`) |
| **Remote Project Path** | `/var/www/my_app` |

---

## Features Tested by this Container

- **Dashboard**: Real-time CPU, RAM, and Disk telemetry parsing.
- **Terminal**: Interactive shell access via SSH to port 2222.
- **Backups & Database**: Native Linux `cron` daemon is active. Scheduled jobs and execution history (`/var/backups/vpsmanager/cron_history.log`) work out of the box.
- **Folder / Media Backups**: `/var/www/uploads` can be archived and downloaded to the local machine.
- **Hot Patching**: File uploads and synchronizations to `/var/www/my_app`.
