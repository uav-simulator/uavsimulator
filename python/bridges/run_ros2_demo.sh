#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/../.." && pwd)"

BASE_URL="${UAVSIM_BASE_URL:-http://127.0.0.1:8000}"
NAMESPACE="${UAVSIM_ROS_NAMESPACE:-/uavsim/ks0223}"
RATE_HZ="${UAVSIM_ROS_RATE_HZ:-15}"
RVIZ_CONFIG="${UAVSIM_RVIZ_CONFIG:-$REPO_ROOT/ros2/rviz/uavsim_demo.rviz}"

if ! command -v rviz2 >/dev/null 2>&1; then
  echo "[uavsim] rviz2 not found in PATH." >&2
  exit 1
fi

echo "[uavsim] starting bridge: base=$BASE_URL ns=$NAMESPACE rate_hz=$RATE_HZ"
python3 "$SCRIPT_DIR/ros2_bridge.py" \
  --base-url "$BASE_URL" \
  --namespace "$NAMESPACE" \
  --rate-hz "$RATE_HZ" \
  --reset-on-start &
BRIDGE_PID=$!

cleanup() {
  if kill -0 "$BRIDGE_PID" >/dev/null 2>&1; then
    kill "$BRIDGE_PID" >/dev/null 2>&1 || true
    wait "$BRIDGE_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT INT TERM

echo "[uavsim] bridge pid=$BRIDGE_PID"
echo "[uavsim] opening rviz config: $RVIZ_CONFIG"
rviz2 -d "$RVIZ_CONFIG"
