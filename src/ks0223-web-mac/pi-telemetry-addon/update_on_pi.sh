#!/usr/bin/env bash
set -euo pipefail

PI_HOST="${1:-192.168.1.121}"
PI_USER="${2:-pi}"
REMOTE_DIR="/home/pi/RaspberryPi-Car/ks0223_sensor_bridge"
SERVICE_NAME="ks0223-sensor-bridge.service"
BACKUP_DIR="${REMOTE_DIR}/backup_$(date +%Y%m%d_%H%M%S)"

ROOT_DIR="$(cd "$(dirname "$0")" && pwd)"

echo "[1/5] Backup current files on Pi"
ssh "$PI_USER@$PI_HOST" "mkdir -p '$BACKUP_DIR' && cp -f '$REMOTE_DIR/ks0223_sensor_bridge.py' '$BACKUP_DIR/' 2>/dev/null || true"

echo "[2/5] Upload updated files"
scp "$ROOT_DIR/ks0223_sensor_bridge.py" "$PI_USER@$PI_HOST:$REMOTE_DIR/"
scp "$ROOT_DIR/$SERVICE_NAME" "$PI_USER@$PI_HOST:/tmp/$SERVICE_NAME"

echo "[3/5] Refresh service file"
ssh "$PI_USER@$PI_HOST" "chmod +x '$REMOTE_DIR/ks0223_sensor_bridge.py' && sudo mv '/tmp/$SERVICE_NAME' '/etc/systemd/system/$SERVICE_NAME'"

echo "[4/5] Restart"
ssh "$PI_USER@$PI_HOST" "sudo systemctl daemon-reload && sudo systemctl restart '$SERVICE_NAME'"

echo "[5/5] Verify"
ssh "$PI_USER@$PI_HOST" "systemctl --no-pager --full status '$SERVICE_NAME' | sed -n '1,25p'"
ssh "$PI_USER@$PI_HOST" "curl -fsS 'http://127.0.0.1:8765/api/telemetry' | head -c 400; echo"

echo "Update complete. Backup: $BACKUP_DIR"
