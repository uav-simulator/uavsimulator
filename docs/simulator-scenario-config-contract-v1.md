# Simulator Scenario Config Contract v1

## Назначение
Этот документ фиксирует, как симуляция должна конфигурироваться как продуктовый runtime, а не как набор ручных операций в Unity Editor.

Контракт нужен для следующих сценариев:

- server/headless запуск;
- reproducible simulation setup;
- запуск из CLI;
- обучение и тесты через сценарные файлы;
- удаленное подключение к уже поднятому simulator runtime.

## Статус
Контракт считается **целевым для v1**, но не полностью реализованным в коде.

## Главный принцип
Симуляция должна запускаться через **одну точку входа конфигурации**.

Пользователь или внешний инструмент не должны вручную собирать runtime из разрозненных действий внутри сцены.

## Формы представления
В `v1` допускаются две эквивалентные формы:

1. JSON payload для API.
2. YAML/JSON scenario file для CLI/server mode.

Логическая структура при этом должна быть одинаковой.

## Канонические секции сценария
### 1. Metadata
- `scenarioId`
- `displayName`
- `description`
- `version`

### 2. Runtime
- `runtimeMode`
- `headless`
- `timeScale`
- `fixedDeltaTime`
- `seed`

### 3. World
- `trackId`
- `environmentProfile`
- `weatherProfile` (optional)
- `lightingProfile` (optional)

### 4. Vehicle
- `vehicleId`
- `spawnPointId` или `spawnPose`
- `vehicleProfile`

### 5. Sensors
- `camera.enabled`
- `camera.profile`
- `telemetry.profile`
- `lineTracker.enabled`
- `ultrasonic.enabled`

### 6. Route / task
- `route.waypoints`
- `route.loop`
- `route.reachDistanceM`
- `task.mode`

### 7. Agent visibility / isolation
- `agents.count`
- `agents.isolated`
- `agents.seeEachOther`
- `agents.collisionsEnabled`
- `agents.vehicles[]`
  - `agentId`
  - `vehicleId`
  - `primary`
  - `spawnPose`
  - `params`

### 8. Logging / outputs
- `logging.enabled`
- `logging.tag`
- `artifacts.outputDir`

## Пример JSON
```json
{
  "scenarioId": "ks0223-demo-track",
  "displayName": "KS0223 Demo Track",
  "runtime": {
    "runtimeMode": "unity-sim",
    "headless": false,
    "timeScale": 1.0,
    "seed": 42
  },
  "world": {
    "trackId": "track.demo.production"
  },
  "vehicle": {
    "vehicleId": "vehicle.prometeo.sport.v1",
    "spawnPointId": "start.main"
  },
  "sensors": {
    "camera": { "enabled": true, "profile": "balanced" },
    "telemetry": { "profile": "default" },
    "lineTracker": { "enabled": true },
    "ultrasonic": { "enabled": true }
  },
  "route": {
    "waypoints": [[0, 0, 0], [4, 0, 8], [12, 0, 16]],
    "loop": false,
    "reachDistanceM": 0.75
  },
  "agents": {
    "count": 1,
    "isolated": true,
    "seeEachOther": false
  },
  "logging": {
    "enabled": true,
    "tag": "demo"
  }
}
```

## Реализованный multi-agent срез
В текущем коде уже поддерживается практический subset этого контракта:

- `agents.count > 1`
  - быстрый способ заспавнить несколько одинаковых машинок с автосмещением по стартовой позиции;
- `agents.vehicles[]`
  - явная конфигурация нескольких машинок;
  - каждая машинка получает собственный `agentId`;
  - можно задать свой `vehicleId`;
  - можно задать `spawnPose.position` и `spawnPose.yawDeg`;
- `agents.seeEachOther`
  - управляет видимостью других машинок в vehicle camera;
- `agents.collisionsEnabled`
  - включает или выключает физические столкновения между машинками;
- `agents.isolated`
  - shorthand для режима без взаимной видимости и без столкновений.

Адресное управление выполняется через `ControlCommand.targetAgentId`.

## Пример multi-agent YAML
```yaml
scenarioId: demo-multi-agent
runtime:
  runtimeMode: unity-sim
  headless: false
  timeScale: 1.0
  seed: 42

world:
  trackId: track.roadsystem_realistic.v2

vehicle:
  vehicleId: vehicle.arcade.blue.v1

agents:
  isolated: false
  seeEachOther: true
  collisionsEnabled: false
  vehicles:
    - agentId: ego
      vehicleId: vehicle.arcade.blue.v1
      primary: true
      spawnPose:
        position: [-11.0, 0.2, -13.5]
        yawDeg: 3
    - agentId: npc-red
      vehicleId: vehicle.arcade.red.v1
      spawnPose:
        position: [-11.0, 0.2, -10.8]
        yawDeg: 3
```

## Связь с текущим API
Текущий `SimulationConfig` и `POST /reset` считаются базой для этой модели, но сценарный контракт шире:

- он должен работать не только для reset;
- он должен быть пригоден для CLI и headless server mode;
- он должен описывать не только машину, но и всю среду запуска.

## Планируемый CLI UX
```bash
rusim scenario validate configs/scenarios/demo.yaml
rusim server start --scenario configs/scenarios/demo.yaml
rusim run --scenario configs/scenarios/demo.yaml
```

## Требования к реализации
1. Один и тот же сценарий должен подниматься локально и в headless-режиме.
2. Сценарий должен быть пригоден для воспроизводимого training/test flow.
3. Сценарий должен уметь выбирать track plugin и vehicle plugin.
4. Параллельные прогоны должны быть изолируемыми.

## Что не входит в v1
- полноценный distributed job scheduler;
- многопользовательская orchestration-система;
- сложная декларативная DSL.

## Связанные документы
- [Autopilot Integration Contract v1](autopilot-integration-contract-v1.md)
- [Unified Runtime Contract v1](unified-runtime-contract-v1.md)
