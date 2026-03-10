# KS0223: сенсоры, камера и доступность по сети

Дата ревизии: 2026-03-05.

Этот файл фиксирует, какие данные доступны в текущей архитектуре: штатный `MainControl.py` (управление) + отдельный `pi-telemetry-addon` (сенсоры).

## Что уже работает в приложении

- Управление движением по TCP `host:5051`:
  - `DirForward`, `DirBack`, `DirLeft`, `DirRight`, `DirStop`.
- Управление камерой (пан/тилт) по TCP:
  - `CamUp`, `CamDown`, `CamLeft`, `CamRight`, `CamStop`.
- Получение видеокадров:
  - UDP ingest на Mac (`5051/udp`) в backend;
  - endpoint-ы: `GET /api/camera/status`, `GET /api/camera/snapshot`, `GET /api/camera/mjpeg`.
- Healthcheck:
  - `GET /api/health`.
- Телеметрия сенсоров через отдельный endpoint на Pi:
  - `GET http://<pi-ip>:8765/api/telemetry` (скрипт `pi-telemetry-addon/ks0223_sensor_bridge.py`);
  - backend проксирует состояние в UI и SignalR (`sensorStatus`, `sensorTelemetry`).

## Какие сенсоры/модули найдены в коде Pi

- Камера (OpenCV):
  - `Ai_recognition/Ai1_Camera_driver.py`, `Ai2_Color_identification.py`, `Ai3_color_follow.py`.
- Видеопередача (UDP JPEG):
  - `FramesSend.py`, `FramesSend_test.py`.
- Ультразвук:
  - `basic_project/bp3_ultrasonic.py`, `bp11_follow_car.py`, `bp12_avoid_car.py`.
- Датчики линии (tracking):
  - `basic_project/bp2_tracking.py`, `bp10_tracking_car.py`.
- IR receiver / пульт:
  - `basic_project/bp8_ir_remote.py`, `bp9_ir_car.py`.
- OLED:
  - `OledModule/OLED.py` (используется в `MainControl.py`).
- LED matrix / buzzer / сервоприводы:
  - `basic_project/bp5_LED8X16_TM1604.py`, `bp1_1buzzer.py`, `bp1_2buzzer_pwm.py`, `bp4_2servo.py`.

## Ограничения штатного MainControl.py

Штатный `MainControl.py` принимает команды по TCP и управляет приводами/серво, но не публикует значения сенсоров по сети (нет `send()` телеметрии, нет отдельного telemetry endpoint).

Итог:

- Команды движения/камеры работают.
- Видео можно получить (если активен отправитель `FramesSend.py`).
- Для «всех сенсоров» нужен отдельный канал (реализован add-on ниже).

## Реализация add-on для сенсоров (отдельная папка)

Папка:

- `src/ks0223-web-mac/pi-telemetry-addon`

Содержимое:

- `ks0223_sensor_bridge.py` - read-only HTTP JSON bridge (`:8765`);
- `ks0223-sensor-bridge.service` - systemd unit;
- `deploy_to_pi.sh` - первичная установка;
- `update_on_pi.sh` - обновление;
- `README.md` - пошаговая инструкция загрузки/обновления/отката.

Данные add-on:

- `ultrasonic.distance_cm`, `ultrasonic.scan.left_cm/center_cm/right_cm`;
- `tracking.left/center/right`;
- `ir.last_code_hex/dec`, `ir.signal_level`;
- `system.cpu_temp_c`, `system.uptime_sec`.

## Риски и откат

Риски:

- Одновременная работа с GPIO в нескольких процессах (штатный `MainControl.py` + add-on).
- Нагрузка от частого опроса HC-SR04 и сканирующего серво.

Откат:

- `sudo systemctl disable --now ks0223-sensor-bridge.service`
- удалить unit и перезагрузить systemd
- удалить папку `/home/pi/RaspberryPi-Car/ks0223_sensor_bridge` (или восстановить backup скрипта)
