#!/usr/bin/env bash
set -euo pipefail

PI_HOST="${1:-192.168.1.121}"
PI_USER="${2:-pi}"
REMOTE_DIR="/home/pi/RaspberryPi-Car/ks0223_sensor_bridge"
SERVICE_NAME="ks0223-sensor-bridge.service"

ROOT_DIR="$(cd "$(dirname "$0")" && pwd)"

echo "[1/4] Upload files to $PI_USER@$PI_HOST"
ssh "$PI_USER@$PI_HOST" "mkdir -p '$REMOTE_DIR'"
scp "$ROOT_DIR/ks0223_sensor_bridge.py" "$PI_USER@$PI_HOST:$REMOTE_DIR/"
scp "$ROOT_DIR/$SERVICE_NAME" "$PI_USER@$PI_HOST:/tmp/$SERVICE_NAME"

echo "[2/4] Install systemd service"
ssh "$PI_USER@$PI_HOST" "chmod +x '$REMOTE_DIR/ks0223_sensor_bridge.py' && sudo mv '/tmp/$SERVICE_NAME' '/etc/systemd/system/$SERVICE_NAME'"

echo "[3/4] Enable and restart service"
ssh "$PI_USER@$PI_HOST" "sudo systemctl daemon-reload && sudo systemctl enable --now '$SERVICE_NAME' && sudo systemctl restart '$SERVICE_NAME'"

echo "[4/4] Verify"
ssh "$PI_USER@$PI_HOST" "systemctl --no-pager --full status '$SERVICE_NAME' | sed -n '1,25p'"
ssh "$PI_USER@$PI_HOST" "curl -fsS 'http://127.0.0.1:8765/healthz'"

echo "Done."
