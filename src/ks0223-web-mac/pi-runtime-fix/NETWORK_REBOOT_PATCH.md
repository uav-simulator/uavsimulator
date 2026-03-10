# NETWORK_REBOOT_PATCH (KS0223)

## Проблема

После reboot на Pi периодически ломался доступ к управлению (`5051/tcp`), хотя камера могла продолжать работать.

Диагностика показала конфликт:

- `dhcpcd` управляет интерфейсами `wlan0/eth0`;
- одновременно `networking.service` поднимает legacy static профиль `eth0` из `/etc/network/interfaces.d/eth0`;
- на `eth0` появляются два IPv4 (static + DHCP), что приводит к нестабильным маршрутам и недоступности управления.

## Что делает патч

Патч применяется скриптом `apply_runtime_fix.sh` и:

1. делает backup сетевых файлов/состояния в `runtime_fix_backup_YYYYMMDD_HHMMSS`;
2. отключает `create_ap.service`;
3. отключает legacy static профиль `/etc/network/interfaces.d/eth0` (перенос в `eth0.ks0223-disabled`);
4. отключает `networking.service` (оставляем единый менеджер сети: `dhcpcd`);
5. сохраняет приоритеты маршрутов в `/etc/dhcpcd.conf` (`wlan0 metric 100`, `eth0 metric 400`);
6. обновляет и перезапускает `ks0223-maincontrol.service` и `ks0223-framesend.service`.

## Проверка после reboot

На Pi:

```bash
ip -br addr
ip route
systemctl is-active networking.service
ls -la /etc/network/interfaces.d/eth0*
systemctl is-active ks0223-maincontrol.service
systemctl is-active ks0223-framesend.service
sudo ss -lntp | grep 5051
```

Ожидаемо:

- `networking.service` -> `inactive`/`disabled`;
- `eth0` static профиль отключен (`eth0.ks0223-disabled`);
- `5051/tcp` доступен;
- управление из web UI стабильно после перезапуска Pi.

## Откат

Использовать `rollback_runtime_fix.sh` с путём к backup:

```bash
./rollback_runtime_fix.sh 192.168.1.121 pi /home/pi/RaspberryPi-Car/runtime_fix_backup_YYYYMMDD_HHMMSS
```

Откат восстанавливает:

- `/etc/network/interfaces.d/eth0` (если был),
- состояние `networking.service` (enabled/active),
- `rc.local`, `dhcpcd.conf`, исходники и сервисы.
