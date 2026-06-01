#!/usr/bin/env bash
set -euo pipefail

PI_HOST="${1:-192.168.1.121}"
PI_USER="${2:-pi}"
PI_PASS="${PI_PASS:-123}"
REMOTE_MAIN="/home/pi/RaspberryPi-Car/MainControl.py"

echo "[1/4] Patch drive trim support in $REMOTE_MAIN on $PI_USER@$PI_HOST"
SSHPASS="$PI_PASS" sshpass -e ssh -o StrictHostKeyChecking=no -o ConnectTimeout=5 "$PI_USER@$PI_HOST" \
  "REMOTE_MAIN='$REMOTE_MAIN' python3 -" <<'PY'
from pathlib import Path
from datetime import datetime
import os

path = Path(os.environ["REMOTE_MAIN"])
text = path.read_text()
marker = "# KS0223 drive trim patch 2026-05-29"

if marker in text:
    print("already patched")
    raise SystemExit(0)

if "# KS0223 safety patch 2026-05-29" not in text:
    raise SystemExit("safety patch marker not found; apply safety patch first")

backup = path.with_name(path.name + ".pre_trim_patch_" + datetime.now().strftime("%Y%m%d_%H%M%S"))
backup.write_text(text)
print("backup:", backup)

anchor = "pwm_R1.start(0)\npwm_L1.start(0)\npwm_R2.start(0)\npwm_L2.start(0)\n"
insert = anchor + f"""

{marker}
LEFT_DUTY = 80
RIGHT_DUTY = 80


def clampDuty(value):
    try:
        parsed = int(value)
    except Exception:
        parsed = 80
    return max(0, min(100, parsed))


def setDriveDuty(left, right):
    global LEFT_DUTY
    global RIGHT_DUTY
    LEFT_DUTY = clampDuty(left)
    RIGHT_DUTY = clampDuty(right)
    print('drive duty: L={{}} R={{}}'.format(LEFT_DUTY, RIGHT_DUTY))
"""
if anchor not in text:
    raise SystemExit("PWM start anchor not found")
text = text.replace(anchor, insert, 1)

text = text.replace("pwm_L1.ChangeDutyCycle(80)", "pwm_L1.ChangeDutyCycle(LEFT_DUTY)")
text = text.replace("pwm_L2.ChangeDutyCycle(80)", "pwm_L2.ChangeDutyCycle(LEFT_DUTY)")
text = text.replace("pwm_R1.ChangeDutyCycle(80)", "pwm_R1.ChangeDutyCycle(RIGHT_DUTY)")
text = text.replace("pwm_R2.ChangeDutyCycle(80)", "pwm_R2.ChangeDutyCycle(RIGHT_DUTY)")

old_parse = """        timed = parseTimedCommand(token)
        if timed is not None:
            parsed.extend(timed)
            continue

        if token in MOTOR_COMMANDS or token in CAMERA_COMMANDS:
"""
new_parse = """        if token.startswith('DriveSpeed#') or token.startswith('DriveTrim#'):
            parsed.append((token, None))
            continue

        timed = parseTimedCommand(token)
        if timed is not None:
            parsed.extend(timed)
            continue

        if token in MOTOR_COMMANDS or token in CAMERA_COMMANDS:
"""
if old_parse not in text:
    raise SystemExit("parseCommands insertion point not found")
text = text.replace(old_parse, new_parse, 1)

old_apply = """def applyMotorCommand(command, duration_s=None):
    global lastMotorCommand
"""
new_apply = """def applyDriveConfig(command):
    if command.startswith('DriveSpeed#'):
        value = command.split('#', 1)[1]
        setDriveDuty(value, value)
        return True
    if command.startswith('DriveTrim#'):
        raw = command.split('#', 1)[1]
        parts = raw.split(',')
        if len(parts) == 2:
            setDriveDuty(parts[0], parts[1])
            return True
    print('invalid drive config: ' + repr(command))
    safeStop('invalid-drive-config')
    return False


def applyMotorCommand(command, duration_s=None):
    global lastMotorCommand
"""
if old_apply not in text:
    raise SystemExit("applyMotorCommand insertion point not found")
text = text.replace(old_apply, new_apply, 1)

old_loop = """                    for command, duration_s in commands:
                        if command in MOTOR_COMMANDS:
                            applyMotorCommand(command, duration_s)
                            cameraActionState = 'CamStop'
                        elif command in CAMERA_COMMANDS:
                            cameraActionState = setCameraAction(command)
                        else:
                            safeStop('unknown-command')
                            cameraActionState = 'CamStop'
"""
new_loop = """                    for command, duration_s in commands:
                        if command.startswith('DriveSpeed#') or command.startswith('DriveTrim#'):
                            applyDriveConfig(command)
                            cameraActionState = 'CamStop'
                        elif command in MOTOR_COMMANDS:
                            applyMotorCommand(command, duration_s)
                            cameraActionState = 'CamStop'
                        elif command in CAMERA_COMMANDS:
                            cameraActionState = setCameraAction(command)
                        else:
                            safeStop('unknown-command')
                            cameraActionState = 'CamStop'
"""
if old_loop not in text:
    raise SystemExit("command dispatch block not found")
text = text.replace(old_loop, new_loop, 1)

path.write_text(text)
print("patched:", path)
PY

echo "[2/4] Syntax check"
SSHPASS="$PI_PASS" sshpass -e ssh -o StrictHostKeyChecking=no -o ConnectTimeout=5 "$PI_USER@$PI_HOST" \
  "python3 -m py_compile '$REMOTE_MAIN'"

echo "[3/4] Restart MainControl service"
SSHPASS="$PI_PASS" sshpass -e ssh -o StrictHostKeyChecking=no -o ConnectTimeout=5 "$PI_USER@$PI_HOST" \
  "printf '%s\n' '$PI_PASS' | sudo -S systemctl restart ks0223-maincontrol.service && systemctl is-active ks0223-maincontrol.service"

echo "[4/4] Verify marker"
SSHPASS="$PI_PASS" sshpass -e ssh -o StrictHostKeyChecking=no -o ConnectTimeout=5 "$PI_USER@$PI_HOST" \
  "grep -n 'KS0223 drive trim patch' '$REMOTE_MAIN'; ss -lntp | grep 5051 || true; journalctl -u ks0223-maincontrol.service -n 25 --no-pager"

echo "Done. Config examples: DriveSpeed#70, DriveTrim#72,80"
