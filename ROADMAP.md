# Roadmap

Публичный план развития платформы `uav-simulator`. Документ обновляется по мере прогресса; статусы фиксируют состояние на момент последнего push в `develop`.

## Видение

`uav-simulator` развивается как расширяемая Unity-симуляционная платформа для исследований обучения и sim-to-real transfer на роботах класса Keyestudio KS0223 и схожих по уровню сложности устройствах. Ключевые свойства, поддерживаемые на каждом этапе развития, — воспроизводимость экспериментов, расширяемость через плагинную архитектуру и измеримость результатов.

## Релизы

| Тег | Дата | Состояние | Артефакт |
|---|---|---|---|
| `v0.1.0` | 2026-04 | released | `dist/v0.1.0/` |
| `v0.1.1` | 2026-04 | released | `dist/v0.1.1/` |
| `v0.1.2` | 2026-04 | released | `dist/v0.1.2/uav-simulator-macos-v0.1.2.zip` |
| `v0.2.0` | 2026-05 | planned | плагин-SDK 1.0, расширяемый каталог встроенных треков и роботов, City Sample |
| `v0.3.0` | 2026-Q3 | planned | стабилизированный sim-to-real pipeline + tutorial для учебного применения |

## Этап MVP (выполнено в v0.1.x)

- Unity-runtime с физическим контуром, рендером и сценой; точка входа `RuntimeSceneBootstrap`, ядро жизненного цикла `SimulationManager`.
- Контракт управления транспортным средством: `ControlCommand` (throttle, steer, brake), `VehicleState`, `CameraFrame`.
- HTTP JSON API для внешних клиентов (`/reset`, `/step`, `/state`, `/health`, `/contract`).
- Operator stack: backend на ASP.NET Core (.NET 8) + Web UI на React + TypeScript + Vite + MUI.
- CLI `rusim`: установка, запуск runtime, управление сценариями, плагинами и моделями.
- Plugin SDK: `VehiclePluginDescriptor`, `TrackPluginDescriptor`, `DeviceContractDescriptor`, `PluginRegistry` с two-tier merge-логикой.
- Каталог встроенных плагинов: KS0223, Prometeo, четыре варианта Arcade Free Racing Car, Simple Drone, Basic Arena, Cardboard Corridor, Cardboard Maze, RoadSystem (две модификации), POLYGON City.
- Python training pipeline: SimClient как HTTP-обёртка, `ABCorridorVisionEnv`, multi-agent варианты, GPU-параллельный `CorridorGenesisVecEnv`, тренировочный скрипт на Stable-Baselines3 PPO.
- ONNX-экспорт обученной policy и model lifecycle (install, activate, bind по runtime mode) через backend.
- ROS2-bridge для одиночного агента: одометрия, телеметрия, кадр камеры, `cmd_vel`.
- Документация: статический сайт на mkdocs material, разделы по архитектуре, API, CLI, использованию, обучению, sim-to-real.
- Релизный конвейер: GitHub Actions для CI, Pages-деплой, релиз `rusim` и манифестов плагинов.

## Этап v0.2.0 — стабилизация платформы и учебный стенд

В работе на момент 2026-05.

- City Sample как опорный учебный стенд: track-плагин на основе POLYGON City Pack, граф путевых точек (`CityWaypointGraph` ScriptableObject + JSON I/O), NPC-машины с waypoint-следующим контроллером, светофоры на перекрёстках с реакцией машин на сигнал. `make city-demo-up` для запуска одной командой через docker-compose.
- Multi-agent ROS2 bridge: namespace per agent, поддержка `auto`-discovery агентов, конвертация Twist в throttle/steer/brake.
- Магистерская диссертация: полный текстовый документ объёмом более 50 000 слов, 11 разделов, code-grounded, по требованиям СТУ СФУ 7.5-07-2021.
- Учебный гайд `docs/sample-city-autonomy.md`: три урока для студентов, постановка задач для практики и курсовых.
- WebUI Scenario Picker: выбор сценария из YAML напрямую в Web UI без обращения к CLI.
- Docker-инфраструктура: backend в контейнере с bind-mount каталога сценариев, передачей RUSIM_BASE_URL для доступа к Unity на хосте.

## Этап v0.3.0 — расширения и стабилизация sim-to-real

Планируется на 2026-Q3.

### Расширения плагинной архитектуры

- Sensor plugins: lidar, depth-камера, IMU как самостоятельные расширения (сейчас поддерживаются только camera и scalar).
- Reward plugins: API на стороне Python для подключения собственных функций награды без модификации тренировочного скрипта.
- Standalone build для сторонних плагинов: переход с Editor-only `AssetDatabase.LoadAssetAtPath` на Resources/Addressables, чтобы плагины работали в собранном бинарнике.
- Plugin marketplace: подписанные `.rusim-plugin.zip` архивы, реестр сторонних плагинов с проверкой совместимости.

### Стабилизация sim-to-real

- Multi-seed sweeps как стандартная практика для оценки variance heavy-DR конфигураций.
- Recurrent PPO как альтернативная архитектура для maze-навигации с памятью.
- BC bootstrap: предобучение policy на демо-записях оператора для снижения variance.
- R3M backbone как замена fresh CNN для визуального энкодера.
- Vision-based traffic light detection: CNN-классификатор, обученный на данных City Sample, как пример решения студенческой задачи.

### Дополнительные транспорты и интеграции

- gRPC как параллельный транспорт runtime API наряду с HTTP JSON для high-rate тренировок.
- Полная ROS2 navigation2 интеграция: TF tree, costmap, planner, recovery behaviors.
- Pedestrians, погода и время суток в City Sample.

### Документация и учебные материалы

- Plugin author runbook с примерами расширения каталога.
- Operator runbook: установка, первый запуск, типовые проблемы.
- Сборник студенческих задач на основе City Sample, согласованный с программой кафедры.

## Этап v1.0.0 — производственная зрелость

Долгосрочный горизонт, после защиты магистерской работы.

- Стабилизация публичных API без обратной совместимости в пределах major-версии.
- Перенос Unity-runtime на актуальные LTS-версии Unity по мере выхода.
- Полное тестовое покрытие: PlayMode, frontend (Vitest), end-to-end (rusim ↔ Unity ↔ backend через pytest).
- Стабильные релизы Unity-runtime для Linux, Windows, macOS.
- Публикация Plugin SDK в OpenUPM.
- Автоматизированный экспорт диссертации и документации в DOCX/PDF через pandoc + СТУ-шаблоны для университетских отчётов.

## Не входит в roadmap

Намеренно исключённые направления, которые регулярно поступают как пожелания, но требуют отдельного проекта:

- Полный физический симулятор автомобильного класса (CARLA-альтернатива).
- Симулятор воздушных транспортных средств (название проекта исторически отражает первоначальный замысел; фактический фокус смещён на наземные KS0223-подобные платформы).
- Интегрированная среда обучения с поддержкой нескольких ML-фреймворков (TensorFlow, JAX) — текущий контур ограничен PyTorch и ONNX.
- Облачное развёртывание для удалённых тренировок с GPU-кластером.

## Tracking

Прогресс по каждому пункту roadmap отражается в `CHANGELOG.md` (по релизам) и в коммитах ветки `develop` (ежедневный). Текущий статус и список задач хранятся в ``.
