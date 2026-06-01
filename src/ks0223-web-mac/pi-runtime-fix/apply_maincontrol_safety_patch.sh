#!/usr/bin/env bash
set -euo pipefail

PI_HOST="${1:-192.168.1.121}"
PI_USER="${2:-pi}"
PI_PASS="${PI_PASS:-123}"
REMOTE_MAIN="/home/pi/RaspberryPi-Car/MainControl.py"

echo "[1/4] Patch $REMOTE_MAIN on $PI_USER@$PI_HOST"
SSHPASS="$PI_PASS" sshpass -e ssh -o StrictHostKeyChecking=no -o ConnectTimeout=5 "$PI_USER@$PI_HOST" \
  "REMOTE_MAIN='$REMOTE_MAIN' python3 -" <<'PY'
from pathlib import Path
from datetime import datetime

path = Path(__import__("os").environ["REMOTE_MAIN"])
text = path.read_text()
marker = "# KS0223 safety patch 2026-05-29"

if marker in text:
    print("already patched")
    raise SystemExit(0)

backup = path.with_name(path.name + ".pre_safety_patch_" + datetime.now().strftime("%Y%m%d_%H%M%S"))
backup.write_text(text)
print("backup:", backup)

old_stop = """def stop():
    pwm_L1.ChangeDutyCycle(0)
    pwm_L2.ChangeDutyCycle(0)
    pwm_R1.ChangeDutyCycle(0)
    pwm_R2.ChangeDutyCycle(0)
"""
new_stop = """def stop():
    pwm_L1.ChangeDutyCycle(0)
    pwm_L2.ChangeDutyCycle(0)
    pwm_R1.ChangeDutyCycle(0)
    pwm_R2.ChangeDutyCycle(0)
    GPIO.output(L_IN1,GPIO.LOW)
    GPIO.output(L_IN2,GPIO.LOW)
    GPIO.output(L_IN3,GPIO.LOW)
    GPIO.output(L_IN4,GPIO.LOW)
    GPIO.output(R_IN1,GPIO.LOW)
    GPIO.output(R_IN2,GPIO.LOW)
    GPIO.output(R_IN3,GPIO.LOW)
    GPIO.output(R_IN4,GPIO.LOW)
"""
if old_stop not in text:
    raise SystemExit("stop() block not found")
text = text.replace(old_stop, new_stop, 1)

old_helpers_anchor = """def setCameraAction(command):
    if command=='CamUp' or command=='CamDown' or command=='CamLeft' or command=='CamRight':
        return command
    else:
        return 'CamStop'



def main():
"""
new_helpers_anchor = """def setCameraAction(command):
    if command=='CamUp' or command=='CamDown' or command=='CamLeft' or command=='CamRight':
        return command
    else:
        return 'CamStop'


{marker}
MOTOR_COMMANDS = ('DirForward', 'DirBack', 'DirLeft', 'DirRight', 'DirStop')
CAMERA_COMMANDS = ('CamUp', 'CamDown', 'CamLeft', 'CamRight', 'CamStop')
ALL_COMMANDS = sorted(MOTOR_COMMANDS + CAMERA_COMMANDS, key=len, reverse=True)
MOTOR_WATCHDOG_S = 0.45
TIMED_COMMAND_MAX_S = 2.0
lastMotorCommand = 'DirStop'
lastMotorCommandAt = 0.0
motorStopAt = None


def safeStop(reason):
    global lastMotorCommand
    global lastMotorCommandAt
    global motorStopAt
    if lastMotorCommand != 'DirStop':
        print('safe stop: ' + reason)
    stop()
    lastMotorCommand = 'DirStop'
    lastMotorCommandAt = time.time()
    motorStopAt = None


def parseTimedCommand(token):
    for sep in ('#', ':'):
        if sep in token:
            command, duration_ms = token.split(sep, 1)
            if command in MOTOR_COMMANDS and duration_ms.isdigit():
                duration_s = min(int(duration_ms) / 1000.0, TIMED_COMMAND_MAX_S)
                return [(command, duration_s)]
    return None


def parseCommands(payload):
    payload = payload.strip()
    if not payload:
        return []

    parsed = []
    for raw in payload.replace(';', '\\n').replace('|', '\\n').splitlines():
        token = raw.strip()
        if not token:
            continue

        timed = parseTimedCommand(token)
        if timed is not None:
            parsed.extend(timed)
            continue

        if token in MOTOR_COMMANDS or token in CAMERA_COMMANDS:
            parsed.append((token, None))
            continue

        rest = token
        glued = []
        while rest:
            matched = None
            for command in ALL_COMMANDS:
                if rest.startswith(command):
                    glued.append((command, None))
                    rest = rest[len(command):]
                    matched = command
                    break
            if matched is None:
                print('unknown command payload: ' + repr(token))
                glued.append(('DirStop', None))
                break
        parsed.extend(glued)

    return parsed


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
    motorStopAt = now + (duration_s if duration_s is not None else MOTOR_WATCHDOG_S)


def checkMotorWatchdog():
    if lastMotorCommand == 'DirStop':
        return
    now = time.time()
    deadline = motorStopAt if motorStopAt is not None else (lastMotorCommandAt + MOTOR_WATCHDOG_S)
    if now >= deadline:
        safeStop('watchdog')


def main():
""".format(marker=marker)
if old_helpers_anchor not in text:
    raise SystemExit("setCameraAction/main anchor not found")
text = text.replace(old_helpers_anchor, new_helpers_anchor, 1)

old_recv = """                    data=client.recv(1024)
                    data=bytes.decode(data)
                    if(len(data)==0):
                        print('client is closed')
                        oled.writeArea4(' Disconnect')
                        break
                    motorAction(data)
                    cameraActionState=setCameraAction(data)
"""
new_recv = """                    checkMotorWatchdog()
                    data=client.recv(1024)
                    data=bytes.decode(data)
                    if(len(data)==0):
                        print('client is closed')
                        safeStop('client-closed')
                        oled.writeArea4(' Disconnect')
                        break
                    commands = parseCommands(data)
                    if not commands:
                        safeStop('empty-payload')
                        continue
                    for command, duration_s in commands:
                        if command in MOTOR_COMMANDS:
                            applyMotorCommand(command, duration_s)
                            cameraActionState = 'CamStop'
                        elif command in CAMERA_COMMANDS:
                            cameraActionState = setCameraAction(command)
                        else:
                            safeStop('unknown-command')
                            cameraActionState = 'CamStop'
"""
if old_recv not in text:
    raise SystemExit("recv block not found")
text = text.replace(old_recv, new_recv, 1)

path.write_text(text)
print("patched:", path)
PY

echo "[2/4] Syntax check"
SSHPASS="$PI_PASS" sshpass -e ssh -o StrictHostKeyChecking=no -o ConnectTimeout=5 "$PI_USER@$PI_HOST" \
  "python3 -m py_compile '$REMOTE_MAIN'"

echo "[3/4] Restart MainControl service"
SSHPASS="$PI_PASS" sshpass -e ssh -o StrictHostKeyChecking=no -o ConnectTimeout=5 "$PI_USER@$PI_HOST" \
  "printf '%s\n' '$PI_PASS' | sudo -S systemctl restart ks0223-maincontrol.service && systemctl is-active ks0223-maincontrol.service"

echo "[4/4] Verify listener and patch marker"
SSHPASS="$PI_PASS" sshpass -e ssh -o StrictHostKeyChecking=no -o ConnectTimeout=5 "$PI_USER@$PI_HOST" \
  "grep -n 'KS0223 safety patch' '$REMOTE_MAIN'; ss -lntp | grep 5051 || true; journalctl -u ks0223-maincontrol.service -n 25 --no-pager"

echo "Done. Safe command examples: DirStop, DirStopDirStop, DirForward#200"
