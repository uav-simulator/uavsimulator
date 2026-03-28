# Контракт model lifecycle (v1)

## Назначение
Зафиксировать единый продуктовый поток работы с моделью: `train -> install -> activate -> autopilot run`.

## Артефакт модели
- `*.onnx` — исполняемая политика (в backend на ПК).
- `metadata.json` — схема наблюдений/действий и версия.
- `metrics.json` — контрольные показатели и служебные метки эксперимента.

## Backend API
- `POST /api/models/upload` — загрузка ONNX и метаданных.
- `GET /api/models` — список зарегистрированных моделей.
- `POST /api/models/activate` — выбор активной модели.
- `GET /api/models/active` — текущая активная модель.
- `POST /api/autopilot/start` — запуск inference-loop.
- `POST /api/autopilot/stop` — остановка loop и возврат в manual mode.
- `GET /api/autopilot/status` — диагностический статус цикла.

## Web UI
Раздел `Model Control` предоставляет:
- upload ONNX;
- activate модели;
- start/stop autopilot;
- диагностику последнего шага (`command`, `throttle`, `steer`, `lastError`).

## Safety
- Любая ручная команда из UI прерывает autopilot (`manual override`).
- `DirStop` отправляется при остановке autopilot.
- При отсутствии активной модели autopilot не запускается.
