#!/usr/bin/env bash
set -euo pipefail

PI_HOST="${1:-192.168.1.121}"
PI_USER="${2:-pi}"
BACKUP_DIR="${3:-}"
PI_SUDO_PASS="${PI_SUDO_PASS:-123}"

if [[ -z "$BACKUP_DIR" ]]; then
  echo "Usage: $0 <pi_host> <pi_user> <backup_dir_on_pi>"
  echo "Example: $0 192.168.1.121 pi /home/pi/RaspberryPi-Car/runtime_fix_backup_20260306_120000"
  exit 1
fi

ssh "$PI_USER@$PI_HOST" "PI_SUDO_PASS='$PI_SUDO_PASS' BACKUP_DIR='$BACKUP_DIR' bash -s" <<'REMOTE'
set -euo pipefail

sudo_cmd() {
  printf '%s\n' "$PI_SUDO_PASS" | sudo -S "$@"
}

sudo_cmd systemctl disable --now ks0223-maincontrol.service ks0223-framesend.service || true
sudo_cmd rm -f /etc/systemd/system/ks0223-maincontrol.service /etc/systemd/system/ks0223-framesend.service
sudo_cmd systemctl daemon-reload

if [[ -f "$BACKUP_DIR/rc.local.bak" ]]; then
  sudo_cmd cp -f "$BACKUP_DIR/rc.local.bak" /etc/rc.local
  sudo_cmd chmod +x /etc/rc.local
fi

if [[ -f "$BACKUP_DIR/dhcpcd.conf.bak" ]]; then
  sudo_cmd cp -f "$BACKUP_DIR/dhcpcd.conf.bak" /etc/dhcpcd.conf
fi

sudo_cmd rm -f /etc/network/interfaces.d/eth0 || true
if [[ -f "$BACKUP_DIR/interfaces.d.eth0.bak" ]]; then
  sudo_cmd cp -f "$BACKUP_DIR/interfaces.d.eth0.bak" /etc/network/interfaces.d/eth0
fi
if [[ -f "$BACKUP_DIR/interfaces.d.eth0.ks0223-disabled.bak" ]]; then
  sudo_cmd cp -f "$BACKUP_DIR/interfaces.d.eth0.ks0223-disabled.bak" /etc/network/interfaces.d/eth0.ks0223-disabled
fi
if [[ ! -f "$BACKUP_DIR/interfaces.d.eth0.ks0223-disabled.bak" ]]; then
  sudo_cmd rm -f /etc/network/interfaces.d/eth0.ks0223-disabled || true
fi

if [[ -f "$BACKUP_DIR/MainControl.py.bak" ]]; then
  cp -f "$BACKUP_DIR/MainControl.py.bak" /home/pi/RaspberryPi-Car/MainControl.py
fi

if [[ -f "$BACKUP_DIR/FramesSend.py.bak" ]]; then
  cp -f "$BACKUP_DIR/FramesSend.py.bak" /home/pi/RaspberryPi-Car/FramesSend.py
fi

python3 -m py_compile /home/pi/RaspberryPi-Car/MainControl.py /home/pi/RaspberryPi-Car/FramesSend.py || true

if [[ -f "$BACKUP_DIR/networking.is-enabled" ]]; then
  case "$(cat "$BACKUP_DIR/networking.is-enabled")" in
    enabled)
      sudo_cmd systemctl enable networking.service || true
      ;;
    disabled)
      sudo_cmd systemctl disable networking.service || true
      ;;
  esac
fi
if [[ -f "$BACKUP_DIR/networking.is-active" ]]; then
  case "$(cat "$BACKUP_DIR/networking.is-active")" in
    active)
      sudo_cmd systemctl start networking.service || true
      ;;
    inactive|failed)
      sudo_cmd systemctl stop networking.service || true
      ;;
  esac
fi

sudo_cmd systemctl enable --now create_ap.service rc-local.service || true
sudo_cmd systemctl restart wpa_supplicant.service dhcpcd.service || true
REMOTE

echo "Rollback complete. Validate services/network manually."
