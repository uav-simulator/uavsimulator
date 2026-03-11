# Unified Runtime Contract v1

## Назначение
Этот документ фиксирует **единый контракт платформы** для `operator-core`.

Он отвечает на вопросы:
- какие интерфейсы считаются каноническими;
- как взаимодействуют frontend, backend, physical runtime и Unity runtime;
- какие данные и команды считаются обязательными для `v1`;
- где проходит граница между product-core и research-layer.

Документ не заменяет низкоуровневую реализацию transport-слоев, а задает **предметный и API-контракт**, относительно которого должны развиваться все runtime-адаптеры.

---

## 1. Область действия

### Входит в контракт v1
- operator frontend API;
- unified backend behavior;
- runtime provider contract;
- camera / telemetry / health / logs contract;
- simulator control contract для `unity-sim`;
- базовый key-space телеметрии и управляющих extension-ключей.

### Не входит в контракт v1
- полный контракт research-layer;
- все будущие transport-варианты (`gRPC`, high-rate binary streaming и т.п.);
- все возможные аппаратные особенности конкретного physical runtime.

---

## 2. Главный архитектурный принцип

Frontend не знает, с каким runtime он работает.

Frontend взаимодействует только с **нормализованным backend API**, а различия между:
- `physical runtime`;
- `unity-sim runtime`

спрятаны внутри backend runtime providers.

Это означает:
- один frontend;
- один operator backend;
- несколько runtime-адаптеров;
- одна предметная модель состояния и управления.

---

## 3. Уровни контрактов

### 3.1. Operator API Contract
Это контракт между:
- Web UI;
- локальным operator backend.

Он определяет:
- подключение и отключение;
- команды управления;
- получение статуса и health;
- получение камеры и сенсоров;
- доступ к логированию.

### 3.2. Runtime Provider Contract
Это внутренний контракт между:
- `RuntimeControlService`;
- конкретным runtime provider.

Он определяет:
- lifecycle подключения;
- отправку управляющих команд;
- получение нормализованных данных состояния;
- предоставление камеры и телеметрии.

### 3.3. Simulator Control Contract
Это контракт, который нужен только для `unity-sim` и описывает управление не только машинкой, но и самой средой:
- reset;
- выбор vehicle plugin;
- выбор track plugin;
- запуск сценария;
- step-loop;
- конфигурацию environment.

### 3.4. Research Integration Contract
Это контракт для:
- Python SDK;
- Jupyter notebooks;
- ROS2 bridge;
- будущих autonomous modules.

Он должен опираться на стабильные product-core точки интеграции и не ломать operator-core.

---

## 4. Runtime modes v1

В `v1` поддерживаются два runtime-режима:

1. `real-robot`
   - physical runtime;
   - реальный экспериментальный стенд;
   - reference case: Keyestudio KS0223.

2. `unity-sim`
   - virtual runtime;
   - цифровой двойник платформы в Unity;
   - используется для отладки, тестов, обучения и sim-to-real валидации.

Требование:
- оба режима должны выглядеть для frontend как один и тот же операторский контур.

---

## 5. Operator API v1

### 5.1. Подключение и состояние

#### `POST /api/connection/connect`
Назначение:
- подключить backend к выбранному runtime.

Body:
```json
{
  "host": "127.0.0.1",
  "port": 8000,
  "runtimeMode": "unity-sim"
}
```

или

```json
{
  "host": "192.168.1.121",
  "port": 5051,
  "runtimeMode": "real-robot"
}
```

#### `POST /api/connection/disconnect`
Назначение:
- остановить операторскую сессию и выполнить best-effort STOP.

#### `GET /api/status`
Назначение:
- получить текущее состояние operator runtime.

Канонические поля `StatusDto`:
- `desiredConnection`
- `tcpConnected`
- `uiConnectedClients`
- `targetHost`
- `targetPort`
- `latencyMs`
- `lastError`
- `lastTcpMessageAt`
- `isLogging`
- `currentLogFile`
- `hasParsedTelemetry`
- `runtimeMode`
- `runtimeLabel`

### 5.2. Health

#### `GET /api/health`
Назначение:
- получить агрегированное состояние operator-core.

Канонические блоки:
- `status`
- `timestamp`
- `version`
- `control`
- `camera`
- `sensors`

Требование:
- health должен быть единой точкой проверки готовности operator stack вне зависимости от runtime.

### 5.3. Управляющие команды

#### `POST /api/command`
Назначение:
- отправить операторскую команду высокого уровня.

Body:
```json
{
  "command": "DirForward"
}
```

Поддерживаемые базовые команды `v1`:
- `DirForward`
- `DirBack`
- `DirLeft`
- `DirRight`
- `DirStop`
- `CamUp`
- `CamDown`
- `CamLeft`
- `CamRight`
- `CamStop`

Требование:
- команды трактуются как **operator intents**, а не как низкоуровневый transport payload.
- provider сам переводит их в TCP, step-control или другой runtime-specific механизм.

### 5.4. Камера

#### `GET /api/camera/status`
Назначение:
- вернуть состояние camera layer.

Канонические поля `CameraStatusDto`:
- `udpListenerEnabled`
- `udpListenPort`
- `hasFrame`
- `lastFrameAt`
- `source`
- `framesReceived`
- `bytesReceived`
- `httpProbeCandidates`
- `httpDiscoveredStreams`

#### `GET /api/camera/snapshot`
Назначение:
- вернуть последний JPEG-кадр.

#### `GET /api/camera/mjpeg`
Назначение:
- вернуть операторский MJPEG stream.

Требование:
- frontend всегда получает камеру через один и тот же backend layer.
- backend сам скрывает, пришел ли кадр из physical runtime или из Unity.

### 5.5. Сенсоры и периферия

#### `GET /api/sensors/status`
Назначение:
- вернуть состояние sensor bridge / telemetry layer.

#### `GET /api/sensors/latest`
Назначение:
- вернуть последнюю нормализованную телеметрию.

#### `POST /api/sensors/config`
Назначение:
- обновить runtime-конфигурацию sensor/drive layer.

Поддерживаемые поля v1:
- `autoScanEnabled`
- `sampleIntervalMs`
- `scanIntervalSec`
- `scanSettleMs`
- `driveSpeedPercent`
- `cameraSpeedPercent`
- `ultrasonicServoPin`

#### `POST /api/sensors/ultrasonic/position`
Назначение:
- установить угол ultrasonic servo.

#### `POST /api/sensors/ultrasonic/auto-scan`
Назначение:
- включить или выключить autoscan.

### 5.6. LED control

#### `POST /api/led/pattern`
#### `POST /api/led/custom`
#### `POST /api/led/clear`

Статус в v1:
- допустимо как `optional capability` physical runtime;
- не является обязательным parity-требованием для Unity.

### 5.7. Логи

#### `POST /api/logs/start`
#### `POST /api/logs/stop`
#### `GET /api/logs/files`
#### `POST /api/logs/open-folder`

Требование:
- формат логов единый, независимо от runtime.

---

## 6. Runtime Provider Contract v1

Это внутренний инженерный контракт backend.

Минимальный semantic lifecycle provider:
1. `Connect`
2. `Disconnect`
3. `SendCommand`
4. `GetStatus`
5. `GetCameraStatus`
6. `GetSensorStatus`
7. `GetLatestSensorTelemetry`
8. `TryGetLatestFrame`
9. `Broadcast / notify status changes`

Требования:
- provider не должен отдавать наружу transport-specific детали;
- provider обязан возвращать данные в нормализованных backend DTO;
- fail-safe STOP реализуется на operator backend уровне и через provider.

---

## 7. Physical runtime contract

### 7.1. Общий смысл
Physical runtime может использовать любой низкоуровневый transport, но для `v1` reference implementation использует:
- TCP text commands для движения;
- camera ingest через доступный physical stream;
- sensor bridge как отдельный telemetry source.

### 7.2. Reference case: KS0223
Подтвержденный control transport:
- TCP `5051`
- plain UTF-8 text command
- фактически одна команда = один send
- сервер не возвращает телеметрию по тому же TCP каналу

### 7.3. Physical runtime requirements v1
- должен поддерживать базовые operator commands;
- должен предоставлять camera data через backend camera layer;
- должен предоставлять sensor telemetry либо напрямую, либо через sensor bridge;
- должен поддерживать STOP на disconnect/ошибке по best effort.

---

## 8. Unity runtime contract

### 8.1. Базовый Unity API
Обязательные операции:
- `GET /health`
- `GET /contract`
- `POST /reset`
- `POST /step`

### 8.2. Базовые DTO Unity API
Ключевые сущности:
- `SimulationConfig`
- `ControlCommand`
- `StepResult`
- `VehicleState`
- `CameraFrame`
- `SimulatorContractDescriptor`

### 8.3. Обязательные поля `ControlCommand`
- `throttle`
- `steer`
- `brake`
- `timestamp`
- `timeBase`
- `extensions[]`

### 8.4. Обязательные поля `VehicleState`
- `pose`
- `linearVelocity`
- `angularVelocity`
- `speed`
- `timestamp`
- `timeBase`
- `telemetry[]`

### 8.5. Обязательные поля `CameraFrame`
- `frameId`
- `width`
- `height`
- `format`
- `encoding`
- `timestamp`
- `timeBase`
- `bytesLength`
- `dataRef`
- `dataBase64`

### 8.6. Unity provider responsibilities
`UnityKs0223RuntimeProvider` обязан:
- поднимать и поддерживать operator session поверх step-loop;
- конвертировать operator commands в `ControlCommand`;
- извлекать из `StepResult` камеру, телеметрию и состояние;
- нормализовать это в backend DTO для frontend.

---

## 9. Simulator Control Contract v1

Этот контракт описывает управление **самой симуляцией**, а не только машинкой.

### Обязательные возможности v1
- выбрать `selectedVehicleId`;
- выбрать `selectedTrackId`;
- передать `trackParams`;
- передать `vehicleParams`;
- выполнить `reset`;
- выполнять `step` в live-loop;
- управлять временем симуляции и конфигурацией запуска.

### Целевые возможности следующего шага
- headless/server mode;
- сценарные конфигурационные файлы;
- запуск нескольких изолированных прогонов;
- унифицированный CLI entrypoint.

---

## 10. Unified telemetry key-space v1

Ниже перечислены ключи, которые уже являются базой для нормализованной телеметрии платформы.

### Drive / control
- `drive.left_pwm_norm`
- `drive.right_pwm_norm`
- `control.throttle_norm`
- `control.steer_norm`
- `control.brake_norm`

### Motion / speed
- `sensor.speedometer.mps`

### Range sensing
- `sensor.ultrasonic.front.m`

### Line tracking
- `sensor.line_tracker.s1_norm`
- `sensor.line_tracker.s2_norm`
- `sensor.line_tracker.s3_norm`
- `sensor.line_tracker.s4_norm`
- `sensor.line_tracker.s5_norm`

### Power
- `power.battery.voltage_v`
- `power.battery.current_a`
- `power.motor.estimated_w`

### Camera and servo state
- `camera.pan_norm`
- `camera.tilt_norm`
- `camera.pan_deg`
- `camera.tilt_deg`
- `ultrasonic.servo.angle_deg`

Требование:
- новые runtime не должны придумывать произвольный ключевой хаос;
- расширение key-space допустимо, но должно быть документировано и versioned.

---

## 11. Operator behavior invariants

Эти правила обязательны для любого runtime v1.

1. `Connect` должен приводить к наблюдаемому статусу готовности.
2. `Disconnect` должен приводить к best-effort STOP.
3. Потеря UI-клиента должна приводить к best-effort STOP.
4. Камера должна быть доступна через одни и те же endpoints.
5. Health должен быть агрегированным и runtime-agnostic.
6. Логи должны писаться в едином формате.
7. Runtime-specific особенности не должны размазываться по frontend.

---

## 12. Отношение к transport-слоям

Контракт `v1` не запрещает использовать разные transport-подходы.

Допустимые транспортные каналы в системе:
- TCP/UDP для physical runtime;
- HTTP JSON step API для Unity;
- SignalR для UI realtime;
- ROS2 как research integration channel;
- в будущем `gRPC` или отдельный binary stream.

Требование:
- transport не является источником правды;
- источником правды является unified semantic contract.

---

## 13. Что должно считаться нарушением контракта

Нарушением контракта считается, если:
- frontend начинает знать transport-specific детали runtime;
- один runtime возвращает принципиально другую семантику status/health/camera/telemetry;
- новые runtime-ключи добавляются без документации;
- симулятор управляется вручную через отдельные ad-hoc точки, минуя канонический layer;
- research-layer начинает подменять собой operator-core.

---

## 14. Следующие обязательные документы

После фиксации этого контракта следующими каноническими артефактами должны стать:
1. `Autopilot Integration Contract v1`
2. `Simulator Scenario Config Contract v1`
3. `Operator Runbook v1`
4. `Docs publishing / Pages structure`

---

## 15. Критерий принятия контракта

Контракт `Unified Runtime Contract v1` считается реально принятым, если:
- на него ссылаются `Product Definition`, `Definition of Done` и `docs/README`;
- новые изменения backend и runtime providers сверяются относительно него;
- дальнейшие интеграции Python/ROS2/autopilot не обходят его, а расширяют через формализованные точки.
