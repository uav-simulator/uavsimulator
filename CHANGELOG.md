# Changelog

История изменений платформы `uav-simulator`. Формат секций: Added (новое) / Changed (изменено) / Fixed (исправлено) / Removed (удалено). Версии следуют семантическому версионированию `major.minor.patch`.

## [Unreleased]

Текущее состояние ветки `develop`. Изменения войдут в `v0.2.0`.

### Added

- **City Sample — учебный стенд для autonomous driving**:
  - `track.city_polygon.v1` — track-плагин на основе POLYGON City Pack (Unity Asset Store id 107224), загружает встроенную DemoScene аддитивно с runtime-конверсией материалов в URP.
  - `CityWaypointGraph` — ScriptableObject для графа путевых точек, с Editor-меню Export/Import/Validate JSON, генератором демо-скаффолда (9-узловой крест-перекрёсток) и Scene-view gizmo'ами.
  - `WaypointFollowerVehicle` — AI-контроллер для NPC-машин: PD-steering к целевому waypoint, cruise control с замедлением на перекрёстке, интеграция с `TrafficLightAwareController` для остановки на красный сигнал.
  - `NpcTrafficSpawner` — компонент трассы, спавнит N NPC-машин на случайных стартовых waypoint'ах с round-robin цветов arcade-машин.
  - `TrafficLight`, `TrafficLightController`, `TrafficLightTriggerZone`, `TrafficLightAwareController` — конечный автомат светофора с координацией NS/EW пар на перекрёстке и raycast-детектором у машин.
  - `TrafficLightPolygonAdapter` — адаптер для single-mesh POLYGON traffic-light prefab с переключением material slots на цикле.
  - Учебный гайд `docs/sample-city-autonomy.md` (~3000 слов) с тремя уроками для студентов и преподавателей.
- **Multi-agent ROS2 bridge**:
  - `python/bridges/ros2_bridge_multi.py` — расширение существующего bridge на N агентов, namespace `/uavsim/<agent_id>` per car.
  - 11 pytest-тестов с rclpy-моком (arg parsing, agent discovery, namespace construction, step routing, cmd_vel→PWM конверсия).
  - Документация в `python/bridges/README.md` с разделом про multi-agent режим.
- **Docker compose city-demo**:
  - `docker-compose.city-demo.yml` — backend + ros2-bridge сервисы.
  - Makefile-цели `city-demo-up`, `city-demo-down`, `city-demo-logs`, `city-demo-status`.
  - Backend Dockerfile дополнен Python venv + `rusim` CLI для `/api/scenarios/load`, bind-mount каталога сценариев в `/app/configs/scenarios`.
- **WebUI Scenario Picker**:
  - Новая вкладка «Сценарии» в Web UI: dropdown сценариев из `configs/scenarios/`, кнопка Load с отображением выбранных track/vehicle/agents.
  - Backend endpoints `GET /api/scenarios` и `POST /api/scenarios/load` (форвард в `rusim scenario reset`).
- **WebUI Demo Replay**:
  - `DemoReplayPanel` для воспроизведения сохранённых JSONL session-логов на реальном роботе.
  - Backend `DemoReplayService` с timestamp-точным воспроизведением `command.outgoing` событий и поддержкой speed multiplier.
- **Магистерская диссертация (auxiliary content)**:
  - 11 разделов общим объёмом более 50 000 слов в `docs/master-thesis/`.
  - Структурные элементы: реферат, введение, заключение, список источников по ГОСТ Р 7.0.100-2018, список сокращений.
  - Соответствие СТУ СФУ 7.5-07-2021.
- **Plugin SDK Editor**:
  - `Tools > UavSimulator > City Waypoints > Generate Demo Scaffold/Validate Selected/Export to JSON/Import from JSON` — менюшный набор для работы с waypoint-графом.
  - `UavSimulator.Editor` asmdef для editor-only кода.

### Changed

- `BuiltinPluginFactory` дополнен фабриками для `track.city_polygon.v1` и арсадных машинок Arcade Free Racing Car (Blue/Red/Gray/Purple).
- `RuntimeMaterialCompatibility` теперь автоматически конвертирует Built-in pipeline материалы на URP/Lit в момент загрузки сторонних ассетов (POLYGON City Pack).
- `CityPolygonTrack` переключён на дефолтный режим load DemoScene аддитивно через `EditorSceneManager.LoadSceneAsyncInPlayMode`. Procedural-grid режим сохранён как fallback.

### Fixed

- POLYGON DemoScene magenta materials под URP — конвертируются на URP/Lit at runtime.
- Auto-detect tile spacing в `CityPolygonTrack` (вместо hardcoded 12 м), чтобы город собирался корректно вне зависимости от scale-параметров POLYGON префабов.

## [v0.1.2] — 2026-04

### Added

- Релиз: `dist/v0.1.2/uav-simulator-macos-v0.1.2.zip`.
- Workflow `release-rusim.yml` для автоматической публикации `rusim` CLI.
- Workflow `release-manifest.yml` для манифестов плагинов.

## [v0.1.1] — 2026-04

### Added

- Релиз: `dist/v0.1.1/`.
- Стабилизация HTTP JSON API после спринта 2.

## [v0.1.0] — 2026-04

Первый релиз платформы по итогам спринта 1 преддипломной практики.

### Added

- Unity runtime: `RuntimeSceneBootstrap`, `SimulationManager`, `PluginRegistry`, `HttpJsonApiHost`, `HttpJsonSimulatorApiServer`, `SimulatorApiFacade`.
- Plugin SDK: `VehiclePluginDescriptor`, `TrackPluginDescriptor`, `PluginDescriptorBase`, `DeviceContractDescriptor`, `ContractVersion`, `VehicleBase`, `TrackBase`.
- Editor-инструменты: `Tools > UavSimulator > Validate Plugins`, `Export Plugin (.zip)`.
- CLI `rusim`: подкоманды `server`, `scenario`, `plugin`, `model`, `runtime`, `step`, `reset`, `doctor`, `version`, `contract`, `inspect`, `list`, `install`, `upgrade`.
- Backend на ASP.NET Core: `RuntimeSessionManager`, `ModelRegistryService`, `AutopilotService`, `AutopilotSafetyFilter`, `SessionLogger`, `SessionVideoRecorder`, `DemoReplayService`. Около 50 HTTP-маршрутов и SignalR-хаб для телеметрии.
- Web UI на React + TypeScript + Vite + MUI с вкладками управления, сенсоров, индикации, моделей, повтора демо и логов.
- Python training pipeline: `SimClient` (HTTP-клиент Unity API), `ABCorridorVisionEnv`, `MultiAgentVisionVecEnv`, `MetaMultiAgentVecEnv`, `CorridorGenesisVecEnv`, тренировочный скрипт на Stable-Baselines3 PPO.
- Тесты: Unity EditMode (5 файлов) для контрактов, конфига, recorder и FSM светофоров; backend xUnit (1 файл) для AutopilotSafetyFilter; Python pytest (6 файлов) для wrappers и launch-логики.
- ROS2 bridge для одиночного агента: одометрия, телеметрия, кадр камеры, `cmd_vel`.
- CI/CD: GitHub Actions `ci.yml` (lint+build), `pages.yml` (mkdocs deploy), `release-manifest.yml`, `release-rusim.yml`.
- Документация на mkdocs material: разделы по архитектуре, API, CLI, использованию, обучению, sim-to-real.
- Каталог встроенных плагинов: KS0223, Prometeo Sport, Arcade (Blue/Red/Gray/Purple), Simple Drone, Basic Arena, RoadSystem (Arena/Realistic), Cardboard Corridor, Cardboard Maze.

### Changed

- `HttpJsonApiHost` поддерживает переменные среды `UAVSIM_API_HOST` и `UAVSIM_API_PORT` для параллельных запусков на одной машине.
- ROS2-bridge переведён на sensor-data QoS (`BEST_EFFORT`) для совместимости с RViz и rqt-инструментами.

### Fixed

- Presentation auto-drive отключён по умолчанию (`autoDrive = false`), чтобы предотвратить непреднамеренное движение робота при нажатии Play.
- ROS2-bridge восстанавливается после ошибки `Active vehicle is not initialized` через повторный reset в runtime loop.

## Формат изменений

При добавлении новой записи использовать формат:

```markdown
## [Unreleased]

### Added
- Краткое описание нового функционала с одним-двумя предложениями объяснения.

### Changed
- Описание изменения существующего поведения.

### Fixed
- Описание исправления с упоминанием симптома и контекста, без подробной отладочной истории.

### Removed
- Описание удалённого функционала с указанием причины и альтернативы.
```

При выпуске релиза `[Unreleased]` переименовывается в новую версию с датой. Тэг проставляется через `git tag -a vX.Y.Z`.
