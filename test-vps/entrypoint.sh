#!/bin/bash
set -e

# Generate host keys if not present
ssh-keygen -A 2>/dev/null || true

# Ensure cron is running
service cron start

# Ensure standard directories exist
mkdir -p /var/backups/vpsmanager /var/www/my_app /var/www/uploads /tmp

echo "=================================================="
echo " VPS Test Container is Ready!"
echo " SSH Port: 22 (Host mapped to 2222)"
echo " Available Users:"
echo "   - root   (password: rootpass)"
echo "   - deploy (password: deploypass, passwordless sudo)"
echo " Cron daemon: Active"
echo "=================================================="

# Run SSH daemon in foreground
exec /usr/sbin/sshd -D -e
