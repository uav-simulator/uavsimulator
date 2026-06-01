#!/usr/bin/env bash
set -euo pipefail

PI_HOST="${1:-192.168.1.121}"
PI_USER="${2:-pi}"
PI_PASS="${PI_PASS:-123}"
REMOTE_MAIN="/home/pi/RaspberryPi-Car/MainControl.py"

echo "[1/4] Patch deterministic timed pulse support in $REMOTE_MAIN on $PI_USER@$PI_HOST"
SSHPASS="$PI_PASS" sshpass -e ssh -o PubkeyAuthentication=no -o PreferredAuthentications=password -o StrictHostKeyChecking=no -o ConnectTimeout=5 "$PI_USER@$PI_HOST" \
  "REMOTE_MAIN='$REMOTE_MAIN' python3 -" <<'PY'
from pathlib import Path
from datetime import datetime
import os

path = Path(os.environ["REMOTE_MAIN"])
text = path.read_text()
marker = "# KS0223 deterministic timed pulse patch 2026-05-29"

if marker in text:
    print("already patched")
    raise SystemExit(0)

if "# KS0223 safety patch 2026-05-29" not in text:
    raise SystemExit("safety patch marker not found; apply safety patch first")

backup = path.with_name(path.name + ".pre_timed_pulse_patch_" + datetime.now().strftime("%Y%m%d_%H%M%S"))
backup.write_text(text)
print("backup:", backup)

old = """def applyMotorCommand(command, duration_s=None):
    global lastMotorCommand
    global lastMotorCommandAt
    global motorStopAt
    motorAction(command)
    now = time.time()
    if command == 'DirStop':
        lastMotorCommand = 'DirStop'
        lastMotorCommandAt = now
        motorStopAt = None
        return

    lastMotorCommand = command
    lastMotorCommandAt = now
    motorStopAt = now + (duration_s if duration_s is not None else MOTOR_WATCHDOG_S)
"""
new = f"""{marker}
def applyMotorCommand(command, duration_s=None):
    global lastMotorCommand
    global lastMotorCommandAt
    global motorStopAt
    motorAction(command)
    now = time.time()
    if command == 'DirStop':
        lastMotorCommand = 'DirStop'
        lastMotorCommandAt = now
        motorStopAt = None
        return

    lastMotorCommand = command
    lastMotorCommandAt = now

    if duration_s is not None:
        duration_s = max(0.0, min(duration_s, TIMED_COMMAND_MAX_S))
        motorStopAt = now + duration_s
        time.sleep(duration_s)
        safeStop('timed-pulse')
        return

    motorStopAt = now + MOTOR_WATCHDOG_S
"""
if old not in text:
    raise SystemExit("applyMotorCommand block not found or already changed")

path.write_text(text.replace(old, new, 1))
print("patched:", path)
PY

echo "[2/4] Syntax check"
SSHPASS="$PI_PASS" sshpass -e ssh -o PubkeyAuthentication=no -o PreferredAuthentications=password -o StrictHostKeyChecking=no -o ConnectTimeout=5 "$PI_USER@$PI_HOST" \
  "python3 -m py_compile '$REMOTE_MAIN'"

echo "[3/4] Restart MainControl service"
SSHPASS="$PI_PASS" sshpass -e ssh -o PubkeyAuthentication=no -o PreferredAuthentications=password -o StrictHostKeyChecking=no -o ConnectTimeout=5 "$PI_USER@$PI_HOST" \
  "printf '%s\n' '$PI_PASS' | sudo -S systemctl restart ks0223-maincontrol.service && systemctl is-active ks0223-maincontrol.service"

echo "[4/4] Verify marker and listener"
SSHPASS="$PI_PASS" sshpass -e ssh -o PubkeyAuthentication=no -o PreferredAuthentications=password -o StrictHostKeyChecking=no -o ConnectTimeout=5 "$PI_USER@$PI_HOST" \
  "grep -n 'KS0223 deterministic timed pulse patch' '$REMOTE_MAIN'; ss -lntp | grep 5051 || true; journalctl -u ks0223-maincontrol.service -n 25 --no-pager"

echo "Done. Timed pulse examples: DirForward#180, DirRight#120"
