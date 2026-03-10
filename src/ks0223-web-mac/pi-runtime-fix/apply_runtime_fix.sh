#!/usr/bin/env bash
set -euo pipefail

PI_HOST="${1:-192.168.1.121}"
PI_USER="${2:-pi}"
PI_SUDO_PASS="${PI_SUDO_PASS:-123}"
ROOT_DIR="$(cd "$(dirname "$0")" && pwd)"
TS="$(date +%Y%m%d_%H%M%S)"
REMOTE_BACKUP_DIR="/home/pi/RaspberryPi-Car/runtime_fix_backup_${TS}"

echo "[1/5] Upload service files"
scp "$ROOT_DIR/ks0223-maincontrol.service" "$PI_USER@$PI_HOST:/tmp/ks0223-maincontrol.service"
scp "$ROOT_DIR/ks0223-framesend.service" "$PI_USER@$PI_HOST:/tmp/ks0223-framesend.service"

echo "[2/5] Apply runtime/network/source fixes on Pi"
ssh "$PI_USER@$PI_HOST" "PI_SUDO_PASS='$PI_SUDO_PASS' REMOTE_BACKUP_DIR='$REMOTE_BACKUP_DIR' bash -s" <<'REMOTE'
set -euo pipefail

sudo_cmd() {
  printf '%s\n' "$PI_SUDO_PASS" | sudo -S "$@"
}

mkdir -p "$REMOTE_BACKUP_DIR"
sudo_cmd chown -R pi:pi "$REMOTE_BACKUP_DIR"

cp -f /etc/rc.local "$REMOTE_BACKUP_DIR/rc.local.bak"
cp -f /etc/dhcpcd.conf "$REMOTE_BACKUP_DIR/dhcpcd.conf.bak"
cp -f /home/pi/RaspberryPi-Car/MainControl.py "$REMOTE_BACKUP_DIR/MainControl.py.bak"
cp -f /home/pi/RaspberryPi-Car/FramesSend.py "$REMOTE_BACKUP_DIR/FramesSend.py.bak"
if [[ -f /etc/network/interfaces.d/eth0 ]]; then
  sudo_cmd cp -f /etc/network/interfaces.d/eth0 "$REMOTE_BACKUP_DIR/interfaces.d.eth0.bak"
fi
if [[ -f /etc/network/interfaces.d/eth0.ks0223-disabled ]]; then
  sudo_cmd cp -f /etc/network/interfaces.d/eth0.ks0223-disabled "$REMOTE_BACKUP_DIR/interfaces.d.eth0.ks0223-disabled.bak"
fi
(systemctl is-enabled networking 2>/dev/null || true) > "$REMOTE_BACKUP_DIR/networking.is-enabled"
(systemctl is-active networking 2>/dev/null || true) > "$REMOTE_BACKUP_DIR/networking.is-active"

# Disable AP mode to avoid extra 10.0.0.1 IP and routing conflicts.
sudo_cmd systemctl disable --now create_ap.service || true

# Replace rc.local with minimal safe variant (no direct python launches).
cat > /tmp/rc.local.ksfix <<'RCLOCAL'
#!/bin/sh -e
_IP=$(hostname -I) || true
if [ "$_IP" ]; then
  printf "My IP address is %s\n" "$_IP"
fi
exit 0
RCLOCAL
sudo_cmd mv /tmp/rc.local.ksfix /etc/rc.local
sudo_cmd chmod +x /etc/rc.local

# Prefer wlan0 route over eth0 for binding decisions.
if ! grep -q 'KS0223 runtime fix: prefer Wi-Fi route' /etc/dhcpcd.conf; then
  cat <<'DHCPCD' | sudo_cmd tee -a /etc/dhcpcd.conf >/dev/null

# KS0223 runtime fix: prefer Wi-Fi route for MainControl/FramesSend binding
interface wlan0
metric 100

interface eth0
metric 400
DHCPCD
fi

# Prevent dual-address conflict on eth0:
# keep only dhcpcd as network manager and disable static ifupdown eth0 profile.
if [[ -f /etc/network/interfaces.d/eth0 ]]; then
  sudo_cmd mv /etc/network/interfaces.d/eth0 /etc/network/interfaces.d/eth0.ks0223-disabled
fi
sudo_cmd systemctl disable --now networking.service || true

# Patch MainControl/FramesSend to prefer wlan0 explicitly.
python3 - <<'PY'
from pathlib import Path

main_path = Path('/home/pi/RaspberryPi-Car/MainControl.py')
main_text = main_path.read_text()
old = """def getLocalIp():
    '''Get the local ip'''
    try:
        s=socket.socket(socket.AF_INET,socket.SOCK_DGRAM)
        s.connect(('8.8.8.8',80))
        ip=s.getsockname()[0]
        time.sleep(0.1)
    finally:
        s.close()
    return ip
"""
new = """def getLocalIp():
    '''Get local ip, prefer wlan0 to keep stable Wi-Fi control IP'''
    try:
        import subprocess
        out = subprocess.check_output("ip -4 -o addr show wlan0 | awk '{print $4}' | cut -d/ -f1", shell=True, text=True).strip()
        if out:
            return out
    except Exception:
        pass

    try:
        s=socket.socket(socket.AF_INET,socket.SOCK_DGRAM)
        s.connect(('8.8.8.8',80))
        ip=s.getsockname()[0]
        time.sleep(0.1)
    finally:
        s.close()
    return ip
"""
if old in main_text:
    main_text = main_text.replace(old, new, 1)
main_path.write_text(main_text)

frames_path = Path('/home/pi/RaspberryPi-Car/FramesSend.py')
frames_text = frames_path.read_text()
needle = "server=socket.socket(socket.AF_INET,socket.SOCK_DGRAM)  # create a UDP \nserver.setsockopt(socket.SOL_SOCKET,socket.SO_BROADCAST,1) #enable broadcast\nserver.connect((HOST,PORT))"
repl = "server=socket.socket(socket.AF_INET,socket.SOCK_DGRAM)  # create a UDP \nserver.setsockopt(socket.SOL_SOCKET,socket.SO_BROADCAST,1) #enable broadcast\ntry:\n    server.setsockopt(socket.SOL_SOCKET, 25, b'wlan0\\0')  # SO_BINDTODEVICE\nexcept Exception:\n    pass\nserver.connect((HOST,PORT))"
if needle in frames_text:
    frames_text = frames_text.replace(needle, repl, 1)
frames_path.write_text(frames_text)
PY

python3 -m py_compile /home/pi/RaspberryPi-Car/MainControl.py /home/pi/RaspberryPi-Car/FramesSend.py

# Install services.
sudo_cmd mv /tmp/ks0223-maincontrol.service /etc/systemd/system/ks0223-maincontrol.service
sudo_cmd mv /tmp/ks0223-framesend.service /etc/systemd/system/ks0223-framesend.service
sudo_cmd systemctl daemon-reload

# Stop legacy direct processes and start managed services.
sudo_cmd pkill -f 'python3 MainControl.py' || true
sudo_cmd pkill -f 'python3 FramesSend.py' || true
sudo_cmd systemctl enable --now ks0223-maincontrol.service ks0223-framesend.service

# Restart only robot services; avoid full network restart here
# because it can temporarily drop SSH and force IPv4LL fallback.
sudo_cmd systemctl restart ks0223-maincontrol.service ks0223-framesend.service
REMOTE

echo "[3/5] Verify"
ssh "$PI_USER@$PI_HOST" "ip -br addr"
ssh "$PI_USER@$PI_HOST" "ip route"
ssh "$PI_USER@$PI_HOST" "systemctl --no-pager --full status ks0223-maincontrol.service | sed -n '1,24p'"
ssh "$PI_USER@$PI_HOST" "systemctl --no-pager --full status ks0223-framesend.service | sed -n '1,24p'"
ssh "$PI_USER@$PI_HOST" "systemctl is-active create_ap.service || true"
ssh "$PI_USER@$PI_HOST" "systemctl is-active networking.service || true"
ssh "$PI_USER@$PI_HOST" "ls -la /etc/network/interfaces.d/eth0* 2>/dev/null || true"
ssh "$PI_USER@$PI_HOST" "echo '$PI_SUDO_PASS' | sudo -S ss -lntp | grep 5051 || true"
ssh "$PI_USER@$PI_HOST" "echo '$PI_SUDO_PASS' | sudo -S ss -uanp | grep python3 | grep 5051 || true"

echo "[4/5] Reboot test reminder"
echo "Run: sudo reboot"
echo "After reboot validate 192.168.1.121:5051 and camera frames."

echo "[5/5] Done"
echo "Backup on Pi: $REMOTE_BACKUP_DIR"
