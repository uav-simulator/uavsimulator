# API

**Что это**  
Текущее описание HTTP JSON API Unity runtime и его роли в продукте.

**Для кого**  
Для разработчика, автора CLI, backend-интегратора и Python tooling.

**Статус**  
Каноническая reference-страница по runtime API.

**Проверено по**  
`Assets/Scripts/Api/`, `Assets/Scripts/Core/SimulationManager.cs`, `python/sim_client/http_client.py`, `python/sim_client/cli.py`

## Назначение API
HTTP JSON API используется как базовый интерфейс между Unity runtime и внешними клиентами:
- `rusim`
- operator backend
- Python tooling
- bridge-процессами

## Актуальные endpoint-ы
- `GET /health`
- `GET /contract`
- `POST /reset`
- `POST /step`

## `GET /health`
Назначение:
- проверить доступность runtime;
- получить активный трек, машинку и диагностическую сводку.

Типичный ответ:

```json
{
  "status": "ok",
  "pluginRegistrySource": "RegistryAsset",
  "availableVehicles": 6,
  "availableTracks": 3,
  "activeAgentId": "ego",
  "activeVehicleId": "vehicle.arcade.blue.v1",
  "activeTrackId": "track.roadsystem_realistic.v2",
  "activeVehicleCount": 1
}
```

## `GET /contract`
Назначение:
- вернуть каталог доступных треков и машинок;
- показать их сенсоры, актуаторы и схемы.

Используется командами:
- `rusim contract`
- `rusim list ...`
- `rusim inspect ...`

## `POST /reset`
Назначение:
- выбрать track и vehicle;
- применить сценарную конфигурацию;
- создать или пересоздать активную среду.

Используется:
- `rusim reset`
- `rusim scenario reset`
- backend connect/reset path для Unity runtime

## `POST /step`
Назначение:
- передать один шаг управления;
- получить новое состояние, telemetry и camera frame.

Типовые поля управления:
- `throttle`
- `steer`
- `brake`
- `targetAgentId`
- `targetVehicleId`
- `extensions[]`

## Camera frame
Текущий HTTP fallback возвращает кадр в `StepResult.frame` как `jpeg + base64`.

Это используется для:
- CLI smoke и diagnostics;
- backend camera layer;
- Python tooling;
- bridge-слоёв.

## Multi-agent
Для multi-agent сценариев runtime поддерживает:
- адресацию по `targetAgentId`;
- список активных агентов в health/step ответах;
- адресный camera flow через product backend.

## Связанные страницы
- [Unified Runtime Contract](unified-runtime-contract.md)
- [Simulator Scenario Config Contract](simulator-scenario-config-contract.md)
- [CLI `rusim`](cli.md)
