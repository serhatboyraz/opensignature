#!/bin/sh
# Ensures shared compose volumes are writable, then runs the app as appuser when started as root.
set -eu

mkdir -p /data/storage /data/certs

if [ "$(id -u)" = "0" ]; then
  chown -R appuser:appuser /data/storage 2>/dev/null || true
  if command -v runuser >/dev/null 2>&1; then
    exec runuser -u appuser -- "$@"
  fi
fi

exec "$@"
