# KS0223 Web (Mac)

Локальное Web-приложение для управления машинкой Keyestudio KS0223 с Mac через существующий TCP-сервер на Raspberry Pi (`192.168.1.121:5051`) и отдельный add-on сенсоров (`:8765`, опционально).

Подробная карта сенсоров и ограничений: [`KS0223_SENSORS_AND_CAMERA.md`](./KS0223_SENSORS_AND_CAMERA.md).

## Что реализовано

- Backend: ASP.NET Core (.NET 8) + SignalR (`src/ks0223-web-mac/backend`).
- Frontend: React + TypeScript + Vite + MUI (`src/ks0223-web-mac/frontend`).
- Локальные логи сессий в JSONL: `src/ks0223-web-mac/logs/session_YYYYMMDD_HHMMSS[_tag].jsonl`.
- Health endpoint: `GET /api/health`.
- Camera ingest без правок Pi:
  - UDP listener JPEG (`/api/camera/snapshot`, `/api/camera/mjpeg`);
  - HTTP probe типовых camera URL на выбранном IP.
- Fail-safe STOP:
  - при отключении последнего UI-клиента SignalR;
  - при ручном Disconnect;
  - при остановке backend;
  - при потере TCP-цикла чтения (best effort).

## Read-only анализ Pi (выполнен 2026-03-05)

SSH доступ использовался только для чтения (`pi@192.168.1.121`). Изменений на Pi не выполнялось.

### Проверенные файлы

- `/home/pi/RaspberryPi-Car/MainControl.py`
- `/home/pi/RaspberryPi-Car/socket_test.py`
- `/home/pi/RaspberryPi-Car/FramesSend.py`
- `/home/pi/RaspberryPi-Car/FramesSend_test.py`

### Что запущено на Pi

- `python3 MainControl.py` слушает `192.168.1.121:5051/tcp`.
- По `ss -lntp` также открыты: `22`, `80`, `5900`.

### Проверка портов с Mac (короткий timeout)

- Открыты: `22`, `80`, `5900`, `5051`.
- Закрыты: `5000`, `8000`, `8080`.

### Результат по протоколу 5051

`MainControl.py` использует:

- `socket.AF_INET`, `SOCK_STREAM` (TCP)
- `bind((host, 5051))`, `listen(5)`, `accept()`
- `client.recv(1024)` + `bytes.decode(data)`
- `motorAction(data)` / `setCameraAction(data)`
- `send()`/`sendall()` ответов клиенту в коде отсутствуют

Это означает:

- Протокол: plain UTF-8 text command.
- Фрейминг: длина/разделители не используются, фактически «одна команда = один send».
- Телеметрия/ответы по текущему TCP-каналу не возвращаются.

## Таблица команд

| Назначение | Команда |
|---|---|
| Вперёд | `DirForward` |
| Назад | `DirBack` |
| Влево | `DirLeft` |
| Вправо | `DirRight` |
| Стоп | `DirStop` |
| Камера вверх | `CamUp` |
| Камера вниз | `CamDown` |
| Камера влево | `CamLeft` |
| Камера вправо | `CamRight` |
| Стоп камеры | `CamStop` |

Пример отправки на TCP 5051: строка `DirForward` в UTF-8 без завершающего `\n`.

## Про видео/сенсоры

`FramesSend.py` отправляет JPEG кадры по UDP на порт `5051`, но как отдельный процесс-отправитель (не сервер) и сейчас не запущен.

В текущей конфигурации без правок Pi:

- канал управления есть;
- backend готов принимать кадры камеры по UDP, если на Pi активен `FramesSend*`/эквивалентный отправитель;
- backend проверяет типовые HTTP camera URL (`/?action=stream`, `/stream.mjpg`, `/video_feed` и т.д.);
- входящей телеметрии сенсоров из `MainControl.py` по сети нет;
- поэтому для «всех сенсоров» используется отдельный `pi-telemetry-addon` (см. ниже).

## Pi Telemetry Add-on (отдельная папка)

Добавлена отдельная папка:

- `src/ks0223-web-mac/pi-telemetry-addon`

Содержимое:

- `ks0223_sensor_bridge.py` - HTTP bridge сенсоров на Pi.
- `ks0223-sensor-bridge.service` - unit для systemd.
- `deploy_to_pi.sh` - первичная установка.
- `update_on_pi.sh` - обновление с backup.
- `README.md` - подробная инструкция заливки/обновления/отката.

После установки add-on backend автоматически опрашивает:

- `http://<targetHost>:8765/api/telemetry`

## Запуск

## 1) Backend

```bash
cd src/ks0223-web-mac/backend
dotnet run
```

Backend слушает: `http://localhost:5058`

Полезные endpoint-ы:

- `GET /api/status`
- `GET /api/health`
- `POST /api/connection/connect`
  - body: `{ "host": "192.168.1.121", "port": 5051, "runtimeMode": "real-robot" }`
  - для Unity: `{ "host": "127.0.0.1", "port": 8000, "runtimeMode": "unity-sim" }`
- `POST /api/connection/disconnect`
- `POST /api/command` body: `{ "command": "DirStop" }`
- `POST /api/logs/start` body: `{ "tag": "test" }`
- `POST /api/logs/stop`
- `GET /api/logs/files`
- `POST /api/logs/open-folder`
- `GET /api/camera/status`
- `GET /api/camera/snapshot`
- `GET /api/camera/mjpeg`
- `GET /api/sensors/status`
- `GET /api/sensors/latest`
- `POST /api/sensors/config?driveSpeedPercent=80&cameraSpeedPercent=70`
- `POST /api/sensors/ultrasonic/position?angleDeg=90&disableAutoScan=true`
- `POST /api/sensors/ultrasonic/auto-scan?enabled=true|false`
- `POST /api/led/pattern?pattern=smile|forward|back|left|right|stop|heart`
- `POST /api/led/custom?frameHex=<32 hex chars>`
- `POST /api/led/clear`
- SignalR hub: `/hub/telemetry`

## Docker (backend+frontend в одном контейнере)

```bash
cd src/ks0223-web-mac
make docker-update
```

Что делает:

- собирает image `ks0223-web-mac:latest`;
- перезапускает контейнер `ks0223-web-mac`;
- монтирует логи: `src/ks0223-web-mac/logs -> /app/logs`;
- ждёт готовности по `GET /api/health`.

Полезные команды:

- `make docker-logs`
- `make docker-health`
- `make docker-stop`
- `make docker-rebuild` (полная пересборка без кэша)

## 2) Frontend

```bash
cd src/ks0223-web-mac/frontend
npm install
npm run dev
```

Открыть: `http://localhost:5173`

## Runtime modes

Operator UI поддерживает два режима работы без отдельного frontend:

- `Real robot`
  - target host: Raspberry Pi;
  - backend подключается к `MainControl.py` по TCP `5051`;
  - камера и сенсоры идут через реальные Pi-каналы.
- `Unity simulator`
  - target host: Unity runtime с `HttpJsonApiHost` (по умолчанию `127.0.0.1:8000`);
  - backend работает как live-адаптер поверх Unity HTTP API (`/health`, `/contract`, `/reset`, `/step`);
  - камера, телеметрия и управление отдаются в тех же UI-панелях.
  - если backend запущен в Docker, loopback-host автоматически нормализуется для доступа к Unity на macOS host.

Последний host кэшируется в браузере отдельно для каждого режима.
После изменений в Unity `HttpJsonApiHost`/`HttpJsonSimulatorApiServer` перезапустите Play Mode, чтобы Editor поднял API с новой конфигурацией.

## Функции UI

- Вкладка «Пульт и телеметрия» (единый экран):
  - выбор runtime mode: `Real robot` / `Unity simulator`;
  - поле выбора target host (кэш последнего значения по каждому режиму в localStorage браузера);
  - Connect/Disconnect к выбранному runtime через backend;
  - индикаторы runtime/UI/latency/last error;
  - большая кнопка STOP;
  - `Space = STOP`;
  - `WASD` и стрелки;
  - управление камерой (пан/тилт): кнопки + `I/J/K/L`, `X = CamStop`;
  - удержание направления: повтор команд ~12.5 Hz.
  - управление стало плавнее: `Stop` отправляется на отпускании, без stop-пульсации между командами;
  - слайдер «Скорость машинки (командная)»;
  - слайдер «Скорость камеры (командная)»;
  - управление поворотом ultrasonic-сервопривода (угол + автоскан);
  - карточка камеры/health;
  - на камере есть настраиваемый Overlay (текст поверх видео) с сохранением настроек в localStorage;
  - в Overlay можно включать/выключать рамку и поля: time, TCP, latency, HC-SR04, scan L/C/R, tracking, IR, CPU temp, source, позиция камеры, скорость машинки/камеры, позиция ultrasonic-серво;
  - в камере есть full screen режим и mini-control (справа снизу), который включается/выключается в настройках Overlay.
  - live-данные сенсоров и входящие TCP сообщения на той же странице.
- Вкладка «Сенсоры KS0223»:
  - отдельный блок «Сенсоры KS0223 и доступность данных»;
  - таблица доступности данных по модулям;
  - сводка и сырые значения из add-on.
- Вкладка «LED панель»:
  - пресеты для TM1604 (smile/forward/back/left/right/stop/heart);
  - пиксельный редактор 8x16 для рисования собственного паттерна;
  - отправка custom frame (16 байт hex);
  - очистка матрицы;
  - отображение текущего состояния LED из телеметрии.
- Страница «Логи»:
  - старт/стоп JSONL логирования;
  - тег сессии;
  - список последних файлов;
  - кнопка открытия папки логов.

## Проверка fail-safe

1. Подключиться в UI (Connect).
2. Нажать/удерживать движение, затем закрыть вкладку UI: backend должен отправить `DirStop` (best effort).
3. Нажать Disconnect: backend отправляет `DirStop` перед разрывом.
4. Остановить backend (`Ctrl+C`): отправляется `DirStop` при остановке приложения.

## Деплой/обновление add-on на Pi

Используйте инструкцию из:

- `src/ks0223-web-mac/pi-telemetry-addon/README.md`

## Фикс после reboot (сеть + автозапуск)

Если после перезагрузки Pi пропадает `5051`, поднимаются лишние IP и отваливается камера, примените runtime fix:

- `src/ks0223-web-mac/pi-runtime-fix/README.md`
- `src/ks0223-web-mac/pi-runtime-fix/NETWORK_REBOOT_PATCH.md`

Патч также фиксирует конфликт `dhcpcd` + legacy static `eth0` (`/etc/network/interfaces.d/eth0`), из-за которого после reboot могло подниматься два IP на `eth0` и теряться доступ к управлению.

Быстрый запуск:

```bash
cd src/ks0223-web-mac/pi-runtime-fix
./apply_runtime_fix.sh 192.168.1.121 pi
```
