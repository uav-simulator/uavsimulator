# Pi Runtime Fix (устранение бага после reboot)

Подробности сетевого патча: `NETWORK_REBOOT_PATCH.md`.

Этот пакет делает запуск KS0223 стабильным после перезагрузки Pi:

- отключает `create_ap` (убирает лишний AP IP `10.0.0.1`);
- устраняет конфликт сетевых менеджеров на `eth0`:
  - отключает `networking.service`,
  - отключает legacy static профиль `/etc/network/interfaces.d/eth0` (переносит в `eth0.ks0223-disabled`);
- убирает запуск `MainControl.py`/`FramesSend.py` из `rc.local`;
- патчит исходники Pi:
  - `MainControl.py` начинает выбирать IP с приоритетом `wlan0`,
  - `FramesSend.py` отправляет UDP через `wlan0` (`SO_BINDTODEVICE`);
- добавляет приоритет маршрутов в `/etc/dhcpcd.conf` (`wlan0` metric 100, `eth0` metric 400);
- ставит systemd-сервисы:
  - `ks0223-maincontrol.service` (TCP `5051`),
  - `ks0223-framesend.service` (UDP камера).

## Почему это нужно

После reboot в исходной конфигурации:

- поднимаются лишние IP (`wlan0`: `10.0.0.1` + Wi-Fi IP, `eth0`: DHCP IP);
- одновременно работают `dhcpcd` и legacy `ifupdown` с static `eth0` (`/etc/network/interfaces.d/eth0`), что даёт двойной IP на `eth0` и нестабильный доступ;
- `MainControl.py` в `rc.local` запускался через `sudo` и падал (`Adafruit-PlatformDetect`), поэтому `5051` закрыт;
- `FramesSend.py` уходил в не тот интерфейс/адрес.

## Применение

```bash
cd src/ks0223-web-mac/pi-runtime-fix
chmod +x apply_runtime_fix.sh rollback_runtime_fix.sh
./apply_runtime_fix.sh 192.168.1.121 pi
```

Если sudo-пароль на Pi не `123`, передайте его через env:

```bash
PI_SUDO_PASS='your-password' ./apply_runtime_fix.sh 192.168.1.121 pi
```

Скрипт автоматически:

1. копирует service-файлы на Pi;
2. делает backup (`/home/pi/RaspberryPi-Car/runtime_fix_backup_YYYYMMDD_HHMMSS`);
3. отключает `create_ap`;
4. отключает конфликтный static `eth0` профиль (`/etc/network/interfaces.d/eth0`) и `networking.service`;
5. заменяет `rc.local` на минимальный безопасный вариант;
6. включает и запускает `ks0223-maincontrol.service` + `ks0223-framesend.service`;
7. проверяет сервисы, IP и порт `5051`.

Важно: скрипт **не** делает принудительный рестарт `dhcpcd/wpa_supplicant`, чтобы не рвать текущую SSH-сессию и не уводить интерфейсы во временный `169.254.x.x`. Закрепление сетевого состояния выполняется обычным reboot.

## Проверка после reboot

На Pi:

```bash
ip -br addr
systemctl status ks0223-maincontrol.service
systemctl status ks0223-framesend.service
systemctl status create_ap.service
systemctl status networking.service
ls -la /etc/network/interfaces.d/eth0*
sudo ss -lntp | grep 5051
```

Ожидаемо:

- `create_ap` не активен;
- `networking.service` не активен (или disabled);
- `/etc/network/interfaces.d/eth0` отсутствует, вместо него `eth0.ks0223-disabled` (если профиль был ранее);
- `ks0223-maincontrol.service` активен;
- `5051/tcp` слушается на `192.168.1.121`;
- в web UI появляется управление без ручного фикса после reboot.

## Откат

```bash
cd src/ks0223-web-mac/pi-runtime-fix
./rollback_runtime_fix.sh 192.168.1.121 pi /home/pi/RaspberryPi-Car/runtime_fix_backup_YYYYMMDD_HHMMSS
```

Если sudo-пароль не `123`:

```bash
PI_SUDO_PASS='your-password' ./rollback_runtime_fix.sh 192.168.1.121 pi /home/pi/RaspberryPi-Car/runtime_fix_backup_YYYYMMDD_HHMMSS
```

Откат:

- удаляет новые systemd unit-файлы;
- восстанавливает `rc.local` из backup;
- восстанавливает legacy `eth0` профиль и исходное состояние `networking.service` (enabled/active) из backup;
- включает обратно `create_ap` и `rc-local`.
