# Pi Telemetry Add-on (KS0223)

Отдельная папка с минимальным add-on для Raspberry Pi, который публикует сенсоры по HTTP JSON без изменения вашего web backend.

## Что добавляет

- Endpoint `GET /api/telemetry` на Pi (`:8765`)
- Endpoint `GET /healthz`
- Управляющие endpoint-ы:
  - `POST /api/config`
  - `POST /api/ultrasonic/position`
  - `POST /api/ultrasonic/auto-scan`
  - `POST /api/led/pattern`
  - `POST /api/led/custom`
  - `POST /api/led/clear`
- Данные:
  - HC-SR04 (`ultrasonic.distance_cm`)
  - HC-SR04 scan (left/center/right) c поворотом серво (`ultrasonic.scan.*`)
  - текущая позиция серво (`ultrasonic.scan_servo_angle_deg`, `ultrasonic.manual_angle_deg`)
  - line tracking (`tracking.left/center/right`)
  - IR receiver (`ir.last_code_*`, `ir.signal_level`)
  - системные метрики (`system.cpu_temp_c`, `system.uptime_sec`)
  - config (`config.auto_scan_enabled`, `config.drive_speed_percent`, `config.camera_speed_percent`)
  - LED состояние (`led.mode`, `led.pattern`, `led.frame_hex`)

## Структура

- `ks0223_sensor_bridge.py` - основной сервис сенсоров
- `ks0223-sensor-bridge.service` - unit для systemd
- `deploy_to_pi.sh` - первичная установка
- `update_on_pi.sh` - обновление с backup

## Первичная установка (с Mac)

```bash
cd src/ks0223-web-mac/pi-telemetry-addon
chmod +x deploy_to_pi.sh update_on_pi.sh ks0223_sensor_bridge.py
./deploy_to_pi.sh 192.168.1.121 pi
```

Примечание: скрипт попросит пароль `pi` и `sudo` на Pi.

## Обновление

```bash
cd src/ks0223-web-mac/pi-telemetry-addon
./update_on_pi.sh 192.168.1.121 pi
```

Скрипт делает backup старого `ks0223_sensor_bridge.py` в папку `backup_YYYYMMDD_HHMMSS`.

## Проверка на Pi

```bash
systemctl status ks0223-sensor-bridge.service
curl -fsS http://127.0.0.1:8765/healthz
curl -fsS http://127.0.0.1:8765/api/telemetry
curl -fsS -X POST http://127.0.0.1:8765/api/ultrasonic/position -H 'Content-Type: application/json' -d '{"angle_deg":120}'
curl -fsS -X POST http://127.0.0.1:8765/api/led/pattern -H 'Content-Type: application/json' -d '{"pattern":"heart"}'
```

## Откат

1. Остановить сервис:
```bash
sudo systemctl stop ks0223-sensor-bridge.service
```
2. Вернуть backup-файл:
```bash
cp /home/pi/RaspberryPi-Car/ks0223_sensor_bridge/backup_YYYYMMDD_HHMMSS/ks0223_sensor_bridge.py /home/pi/RaspberryPi-Car/ks0223_sensor_bridge/ks0223_sensor_bridge.py
```
3. Перезапустить сервис:
```bash
sudo systemctl restart ks0223-sensor-bridge.service
```

Если нужно полностью убрать add-on:
```bash
sudo systemctl disable --now ks0223-sensor-bridge.service
sudo rm -f /etc/systemd/system/ks0223-sensor-bridge.service
sudo systemctl daemon-reload
```

## Связь с web backend

Backend в `src/ks0223-web-mac/backend` опрашивает `http://<targetHost>:8765/api/telemetry` и пробрасывает в UI на страницу `Пульт и сенсоры`.
