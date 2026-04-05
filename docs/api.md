# API

В проекте есть два уровня API:
- Unity runtime API;
- operator backend API.

## 1. Unity runtime API

Unity runtime поднимает HTTP JSON API, которое используется `rusim`, backend и Python tooling.

### Актуальные endpoint-ы
- `GET /health`
- `GET /contract`
- `POST /reset`
- `POST /step`

### `GET /health`
Назначение:
- проверить доступность runtime;
- получить активный трек и активного робота;
- увидеть диагностическую сводку по текущему runtime.

Типичный ответ:

```json
{
  "status": "ok",
  "pluginRegistrySource": "RegistryAsset",
  "availableVehicles": 6,
  "availableTracks": 3,
  "activeAgentId": "ego",
  "activeVehicleId": "vehicle.prometeo.sport.v1",
  "activeTrackId": "track.roadsystem_realistic.v2",
  "activeVehicleCount": 1
}
```

### `GET /contract`
Возвращает:
- каталог роботов;
- каталог треков;
- сенсоры, актуаторы и схемы устройства.

Используется командами:
- `rusim contract`
- `rusim list ...`
- `rusim inspect ...`

### `POST /reset`
Используется для:
- выбора track и vehicle;
- применения сценария;
- переинициализации активной среды.

Основные клиенты:
- `rusim reset`
- `rusim scenario reset`
- backend connect/reset path для `unity-sim`

### `POST /step`
Используется для:
- передачи одного шага управления;
- получения нового состояния, телеметрии и camera frame.

Типовые поля управления:
- `throttle`
- `steer`
- `brake`
- `targetAgentId`
- `targetVehicleId`

## 2. Operator backend API

Backend предоставляет единый операторский слой поверх `unity-sim` и `real-robot`.

### Подключение и runtime control
Основные направления:
- подключение к runtime;
- чтение телеметрии и камеры;
- ручное управление;
- запуск и остановка автопилота.

### Model lifecycle API

Актуальные endpoint-ы:
- `POST /api/models/upload`
- `GET /api/models`
- `POST /api/models/activate`
- `GET /api/models/active`

Назначение:
- загрузка ONNX-артефакта;
- хранение модели в registry;
- выбор активной версии;
- выдача метаданных и статуса активной модели.

### Autopilot API

Актуальные endpoint-ы:
- `POST /api/autopilot/start`
- `POST /api/autopilot/stop`
- `GET /api/autopilot/status`

Назначение:
- запуск inference loop на активной модели;
- остановка автопилота и возврат в ручной режим;
- диагностика шагов, ошибок и текущего состояния.

## Runtime roles в продукте

- Unity runtime API — базовый интерфейс симулятора.
- Backend API — операторский и продуктовый слой.
- `rusim` использует оба контура: напрямую runtime API и backend API для моделей.

## Связанные страницы
- [Архитектура](architecture.md)
- [CLI `rusim`](cli.md)
- [Model Lifecycle](model-lifecycle.md)
