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
  - CLI-паритет: `rusim scenario list [--json]` показывает те же файлы, что и backend, без необходимости поднимать веб-сервер.
- **Plugin workflow smoke test**:
  - `scripts/validate_plugin_workflow.sh` (+ Makefile-цели `plugin-smoke`, `plugin-smoke-track`) — end-to-end проверка цикла `new → install → list → remove` на CLI-реестре без необходимости в Unity Editor или backend'е.
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
- **CI/CD discipline (quality polish iteration)**:
  - `python-tests` job: реальный `pytest` + `ruff check` + покрытие (артефакт `coverage.xml`) — заменяет фиктивный `python-lint` stub, гонявший только `import` smoke.
  - `rusim-smoke` job: проверяет `rusim --help`, `rusim scenario list` и валидирует все `configs/scenarios/*.yaml`.
  - `demo-proof-ci` теперь зовёт настоящий `make plugin-smoke-track` (раньше всегда возвращал 0 со строкой «skip, Unity API not reachable»).
  - Path-filter `docs/**`, `mkdocs.yml`, `*.md` для `push/pull_request` — экономит CI-минуты на чисто документационных коммитах.
  - `release-manifest.yml`: добавлен `on: release: [published]` триггер, давно ссылавшийся `${{ github.event.release.tag_name }}` теперь не мёртвый код.
  - Frontend lint в CI как non-blocking (`continue-on-error`) с baseline 2 ошибки + 10 warnings; драка с baseline — отдельная итерация.
- **Python project hygiene**:
  - `python/pyproject.toml`: optional-extras `test` / `training` / `dev`; конфиг `[tool.ruff]`, `[tool.pytest.ini_options]`, `[tool.mypy]`.
  - `python/tests/conftest.py` — заменяет `sys.path.insert` boilerplate в каждом тестовом файле.
  - `.pre-commit-config.yaml` — ruff + ruff-format + prettier + базовые pre-commit-hooks; eslint/format-check заведены закомментированными до устранения baseline.

### Changed

- `BuiltinPluginFactory` дополнен фабриками для `track.city_polygon.v1` и арсадных машинок Arcade Free Racing Car (Blue/Red/Gray/Purple).
- `RuntimeMaterialCompatibility` теперь автоматически конвертирует Built-in pipeline материалы на URP/Lit в момент загрузки сторонних ассетов (POLYGON City Pack).
- `CityPolygonTrack` переключён на дефолтный режим load DemoScene аддитивно через `EditorSceneManager.LoadSceneAsyncInPlayMode`. Procedural-grid режим сохранён как fallback.
- **Master-thesis numbering (quality polish)**: H1 заголовки глав 06-api-spec и 07-plugin-development приведены в соответствие с ToC из 11-conclusion (`6→4`, `7→5` и все subsection refs внутри). Cross-refs в 08-training-python и 03-related-work-and-analogs синхронизированы.
- **MkDocs nav**: добавлен раздел «Магистерская диссертация» (14 глав, ранее доступных только по прямому URL); `superpowers/**` исключён из деплоя.
- **Docs canonical examples**: `cli.md` подтянут к каноническому `track.cardboard_corridor.v1` (был `roadsystem_arena.v1`); `usage.md` — удалена стейл-нота с датой 2026-04-11.
- **Python codebase**: ruff `--fix` применён ко всему `python/` (240 авто-правок) — модернизация типов `Dict[K,V] → dict[K,V]`, `Optional[X] → X | None`, сортировка `import`-ов, удаление неиспользуемых импортов и `f`-префиксов без placeholder'ов. Поведение не меняется.

### Fixed

- POLYGON DemoScene magenta materials под URP — конвертируются на URP/Lit at runtime.
- Auto-detect tile spacing в `CityPolygonTrack` (вместо hardcoded 12 м), чтобы город собирался корректно вне зависимости от scale-параметров POLYGON префабов.
- Дублирующийся скаффолд раздела 7.7 в `docs/master-thesis/07-plugin-development.md` (оставшийся от ранней разметки) удалён.
- Текст 7.6.3 диссертации скорректирован: ранее ошибочно утверждалось, что Unity-сторона runtime читает `~/.rusim/plugin-registry.json`. На самом деле `PluginRegistry.Load()` читает только Resources + `BuiltinPluginFactory`. Раздел 7.6.4 описывает known limitation runtime-side-loading и что именно нужно сделать в `v0.3.0`.
- Frontend Vite build больше не выдаёт «chunks larger than 500 kB» — введено разделение на `react`, `mui`, `signalr` и основной чанк через `manualChunks`.
- `configs/scenarios/ab-corridor-multi-v1.yaml`: дублирующийся блок `runtime:` в конце файла затирал валидный верхний; добавлены недостающие `runtimeMode`/`headless`. Теперь `rusim scenario validate` зелёный для всех 12 сценариев.
- `docs/training.md` — был 14-строчный stub со ссылкой на несуществующий `docs/experiments.md`; переписан в полноценный обзор контура обучения.
- Битые master-thesis ссылки в `docs/sample-city-autonomy.md` (`master-thesis/06-python-training.md` → `08-training-python.md`).
- 6 точечных правок исходников (B904 raise-without-from в `sim_client/cli.py`, `evaluate_ab_policy.py`, `evaluate_v9.py`; F841 unused-variable в `inspect_policy.py`, `multi_agent_vision_env.py`, `sensor_only_baseline.py`).

### Removed

- **10 копипастных `export_v9_rev{24,25,26,29,30,37,38,39,41,42}_onnx.py`** — все различались только двумя путями (либо argparse-обёрткой для `--frame-stack` в случае rev38 и встроенной сигнатурной верификацией в rev24). Заменены одним `python/training/export_onnx.py --rev <rev>` с опциональными `--frame-stack k`, `--sanity-forward`, `--verify-signature`, `--artifacts-root`, `--model-family`. Покрыт 7 unit-тестами (`tests/training/test_export_onnx.py`). Ссылка на семейство в `docs/master-thesis/04-architecture.md` обновлена.

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
