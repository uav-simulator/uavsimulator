# Сборка и локальный запуск

**Что это**  
Практическая страница про локальную сборку, запуск и developer flow вокруг Unity runtime, CLI и demo automation.

**Для кого**  
Для разработчика и пользователя, который работает с репозиторием локально.

**Статус**  
Актуальный guide по локальному запуску.

**Проверено по**  
`Makefile`, `python/sim_client/cli.py`, `configs/scenarios/demo.yaml`, `src/UnityProject/uav-simulator/Assets/Scenes/`

## Канонический product lifecycle
- `rusim server up`
- `rusim server status`
- `rusim server down`

## Быстрые команды
Поднять runtime:

```bash
rusim server up --mode background --port 8000 --scenario configs/scenarios/demo.yaml
rusim doctor --base-url http://127.0.0.1:8000
rusim server down
```

Локально собрать standalone runtime:

```bash
rusim runtime build --project-path src/UnityProject/uav-simulator
rusim runtime list
```

## Сцены Unity
В проекте есть три основные сцены:
- `Assets/Scenes/TrackScence.unity`
- `Assets/Scenes/PresentationTrack.unity`
- `Assets/Scenes/RoadSystemTrack.unity`

Для ручной работы в Editor чаще используются:
- `PresentationTrack`
- `RoadSystemTrack`

## Demo automation и Makefile
`Makefile` нужен для:
- developer automation;
- ROS2/demo orchestration;
- smoke/preflight сценариев;
- локальной работы с Unity Editor.

Практические команды:

```bash
make sim-public
make demo-up
make demo-status
make demo-proof
make demo-down
```

`make demo-*` используют канонический demo-сценарий:

```text
configs/scenarios/demo.yaml
```

## Runtime behaviour
- `RuntimeSceneBootstrap` поднимает `SimulationManager` и `HttpJsonApiHost`.
- `GET /health` и `GET /contract` используются как базовая проверка готовности.
- runtime не спавнит машинку автоматически при старте сцены: нужен явный `reset` или запуск сценария.

## Текущий каталог built-in и asset-based сущностей
Треки:
- `track.basic_arena.v1`
- `track.roadsystem_arena.v1`
- `track.roadsystem_realistic.v2`

Машинки:
- `vehicle.prometeo.sport.v1`
- `vehicle.arcade.blue.v1`
- `vehicle.arcade.red.v1`
- `vehicle.arcade.gray.v1`
- `vehicle.arcade.purple.v1`
- `vehicle.drone.simple.v1`

## Связанные страницы
- [Установка](installation.md)
- [Использование](usage.md)
- [Плагины](plugins.md)
