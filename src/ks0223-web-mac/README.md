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

`FramesSend.py` отправляет JPEG кадры по UDP на порт `5051`, но как отдельный процесс-отправитель (не сервер).

В текущей конфигурации без правок Pi:

- канал управления есть;
- backend принимает кадры камеры по UDP (`:5051/udp`), если на Pi активен `FramesSend*`/эквивалентный отправитель;
- при connect в `real-robot` backend автоматически отправляет ICMP bootstrap ping к Pi, чтобы поднять `FramesSend.py` после reboot (он ждёт первый echo-пакет на `wlan0`);
- backend проверяет типовые HTTP camera URL (`/?action=stream`, `/stream.mjpg`, `/video_feed` и т.д.);
- входящей телеметрии сенсоров из `MainControl.py` по сети нет;
- поэтому для «всех сенсоров» используется отдельный `pi-telemetry-addon` (см. ниже).

Примечание: если в `camera/status` нет `hasFrame=true` и `httpDiscoveredStreams` пустой, проверьте, что на Pi реально запущен поток кадров (`FramesSend*`) и что backend слушает `5051/udp`.

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

Полезные endpoint-ы (API v2, breaking):

- `GET /api/status?clientId=<id>&runtimeMode=<mode>`
- `GET /api/health?clientId=<id>&runtimeMode=<mode>`
- `POST /api/connection/connect`
  - body:
    - real: `{ "clientId":"tab-a", "runtimeMode":"real-robot", "host":"192.168.1.121", "port":5051 }`
    - unity: `{ "clientId":"tab-a", "runtimeMode":"unity-sim", "host":"127.0.0.1", "port":8000 }`
- `POST /api/connection/disconnect`
  - body: `{ "clientId":"tab-a", "runtimeMode":"unity-sim" }`
- `POST /api/command`
  - body: `{ "clientId":"tab-a", "runtimeMode":"unity-sim", "command":"DirStop", "agentId":"car-a" }`
- `POST /api/logs/start` body: `{ "tag": "test" }`
- `POST /api/logs/stop`
- `GET /api/logs/files`
- `POST /api/logs/open-folder`
- `GET /api/camera/status?clientId=<id>&runtimeMode=<mode>`
- `GET /api/camera/snapshot?clientId=<id>&runtimeMode=<mode>&agentId=<optional>`
- `GET /api/camera/mjpeg?clientId=<id>&runtimeMode=<mode>&agentId=<optional>`
- `GET /api/sensors/status?clientId=<id>&runtimeMode=<mode>`
- `GET /api/sensors/latest?clientId=<id>&runtimeMode=<mode>`
- `POST /api/sensors/config` (`clientId`/`runtimeMode` в query или body)
- `POST /api/sensors/ultrasonic/position` (`clientId`/`runtimeMode` в query или body)
- `POST /api/sensors/ultrasonic/auto-scan` (`clientId`/`runtimeMode` в query или body)
- `POST /api/led/pattern` (`clientId`/`runtimeMode` + `pattern`)
- `POST /api/led/custom` (`clientId`/`runtimeMode` + `frameHex`)
- `POST /api/led/clear` (`clientId`/`runtimeMode`)
- `GET /api/unity/runtime-catalog?clientId=<id>&runtimeMode=unity-sim&host=<optional>&port=<optional>`
- `POST /api/unity/runtime-selection`
  - body: `{ "clientId":"tab-a","runtimeMode":"unity-sim","trackId":"...","vehicleId":"...","cameraMode":"spectator","agents":[],"applyImmediately":true }`
- `POST /api/unity/client-selection`
  - body: `{ "clientId":"tab-a","runtimeMode":"unity-sim","controlAgentId":"car-a","cameraAgentId":"car-a" }`
- `POST /api/models/upload` (`multipart/form-data`, поле `file=.onnx`)
- `GET /api/models`
- `GET /api/models/active`
- `POST /api/models/activate` body: `{ "modelId":"..." }`
- `POST /api/autopilot/start` body: `{ "clientId":"tab-a","runtimeMode":"unity-sim","agentId":"car-a" }`
- `POST /api/autopilot/stop` body: `{ "clientId":"tab-a","runtimeMode":"unity-sim" }`
- `GET /api/autopilot/status`
- SignalR hub: `/hub/telemetry`

Важно: в SignalR после подключения клиент вызывает `BindClient(clientId)`. Для `real-robot` события идут в `client:{clientId}`, для `unity-sim` — в group shared world endpoint-а.

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
  - камера и сенсоры идут через реальные Pi-каналы;
  - для нескольких вкладок используется единый live-stream камеры (без конфликтов UDP bind).
  - для одного endpoint активным держится последний подключившийся клиент (takeover), так как штатный `MainControl.py` фактически обрабатывает один активный TCP control-client.
- `Unity simulator`
  - target host: Unity runtime с `HttpJsonApiHost` (по умолчанию `127.0.0.1:8000`);
  - backend работает как live-адаптер поверх Unity HTTP API (`/health`, `/contract`, `/reset`, `/step`);
  - один endpoint `(host:port)` = один shared world session;
  - вкладки attach-ятся к этому world без reset сцены и выбирают свой `control agent`/`camera agent`;
  - по умолчанию симуляция может быть с пустым списком агентов (`agents=[]`, `agents.allow_empty=true`) и камерой `spectator`;
  - первая машинка добавляется явно через popup в UI;
  - при выборе `Camera agent` UI автоматически синхронизирует `Control agent`;
  - камера, телеметрия и управление отдаются в тех же UI-панелях.
  - если backend запущен в Docker, loopback-host автоматически нормализуется для доступа к Unity на macOS host.

Последний host кэшируется в браузере отдельно для каждого режима.
После изменений в Unity `HttpJsonApiHost`/`HttpJsonSimulatorApiServer` перезапустите Play Mode, чтобы Editor поднял API с новой конфигурацией.

### Multi-tab / multi-runtime

Поддерживается одновременная работа нескольких вкладок:

- Tab A: `clientId=a1`, `runtimeMode=unity-sim`, endpoint `127.0.0.1:8000`;
- Tab B: `clientId=b1`, `runtimeMode=unity-sim`, endpoint `cloud-host:8000`;
- Tab C: `clientId=c1`, `runtimeMode=real-robot`, endpoint `192.168.1.121:5051`.

Сессии и realtime-каналы изолированы, автоматического глобального переключения режима больше нет.

## Migration from global runtime model

С версии API v2 удалена глобальная модель `currentMode`.

Breaking changes:

- Для runtime endpoint-ов обязательны `clientId` и `runtimeMode`.
- Legacy-запросы без этих полей не поддерживаются.
- SignalR push больше не `Clients.All`: `real-robot` идёт по `client:{clientId}`, `unity-sim` по group shared world endpoint-а.
- Real robot fail-safe STOP выполняется в рамках соответствующей real-session (disconnect UI/TCP/shutdown).

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
  - автоскан ultrasonic по умолчанию выключен и включается явно тумблером в UI;
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
