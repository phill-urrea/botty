#!/bin/sh
set -e
# Ensure session and log dirs exist and are writable by botty (for volume mounts)
mkdir -p /app/.wwebjs_auth /app/logs
# Remove stale Chromium singleton lock (container restarts look like "another computer" to Chromium)
find /app/.wwebjs_auth -name 'SingletonLock' -delete 2>/dev/null || true
chown -R botty:botty /app/.wwebjs_auth /app/logs
exec gosu botty "$@"
