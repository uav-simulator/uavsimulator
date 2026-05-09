# Autonomous Vehicle Training Simulator (Unity)

[![CI](https://github.com/NMGorovenko/uav-simulator/actions/workflows/ci.yml/badge.svg?branch=develop)](https://github.com/NMGorovenko/uav-simulator/actions/workflows/ci.yml)
[![Docs](https://github.com/NMGorovenko/uav-simulator/actions/workflows/pages.yml/badge.svg?branch=develop)](https://nmgorovenko.github.io/uav-simulator/)
[![Python 3.10+](https://img.shields.io/badge/python-3.10%2B-blue.svg)](python/pyproject.toml)
[![Unity 6000.1](https://img.shields.io/badge/unity-6000.1.8f1-black.svg)](src/UnityProject/uav-simulator/)
[![.NET 8](https://img.shields.io/badge/.net-8.0-512bd4.svg)](src/ks0223-web-mac/backend/)

Расширяемый Unity-симулятор для обучения и проверки моделей управления наземными роботами (текущий baseline: `KS0223`).

## Текущее состояние
- Плагинная архитектура для треков и роботов.
- Актуальный каталог треков: `track.roadsystem_arena.v1`, `track.basic_arena.v1`, `track.roadsystem_realistic.v2`.
- Добавлен реалистичный RoadSystem plugin track:
  - `track.roadsystem_realistic.v2` (бордюры, старт/финиш, освещение, расширенное окружение).
- Ассетные визуалы машин (через plugin `vehicleId`):
  - `vehicle.prometeo.sport.v1`
  - `vehicle.arcade.blue.v1`
  - `vehicle.arcade.red.v1`
  - `vehicle.arcade.gray.v1`
  - `vehicle.arcade.purple.v1`
- Дрон-плагин:
  - `vehicle.drone.simple.v1`
- HTTP JSON API (`/health`, `/contract`, `/reset`, `/step`).
- CARLA-подобный high-level Python слой (`Client/World/Map/BlueprintLibrary`) поверх текущего API.
- Презентационная сцена `PresentationTrack` с разметкой и sensor HUD.
- Python SDK + Jupyter презентационный notebook.
- Опциональный ROS2 bridge (typed topics + compat JSON topics).

## Быстрый старт
Unity версия проекта: `6000.1.8f1`.

1. Создай Python окружение:
   - `make venv`
   - `python -m pip install ./python`
2. Запусти симулятор (публичный API для ROS/Docker):
   - `make sim-public`
3. В Unity открой сцену:
   - `Assets/Scenes/PresentationTrack.unity`
   - или `Assets/Scenes/RoadSystemTrack.unity` (spline-трек на базе `Road System`)
   - при необходимости пересобери RoadSystem-сцену через меню: `UavSimulator/Scene/Build RoadSystem Track Scene`
4. Нажми `Play`.
5. Подними ROS UI и bridge одной командой:
   - `make demo-up`
6. Для демо-управления через ROS2 (`/cmd_vel`) открой steering UI:
   - `make demo-control`
7. Быстрая проверка:
   - `make demo-status`
8. Строгая pre-demo проверка (fail-fast):
   - `make demo-proof`

CLI пример CARLA-style:
- `python python/examples/carla_like_quickstart.py --base-url http://127.0.0.1:8000`

Примечание:
- По умолчанию при старте сцены транспорт **не спавнится автоматически**. Экземпляр появляется после `reset` (API/ROS/demo-reset).
- Порог `odom hz` в `demo-proof` регулируется через `UAVSIM_ODOM_HZ_MIN` (по умолчанию `10`).
- Переключение визуала машины (через plugin id):
  - базовый demo теперь берётся только из `configs/scenarios/demo.yaml`;
  - `make demo-reset`
  - `make demo-reset DEMO_SCENARIO=configs/scenarios/demo.yaml`
  - multi-agent demo:
    - `rusim server up --mode background --port 8000 --scenario configs/scenarios/demo-multi-agent.yaml`
    - `rusim step --base-url http://127.0.0.1:8000 --agent-id npc-red --throttle 0.3 --steer 0.0 --brake 0.0`
  - для ручного override вне demo-сценария:
    - `rusim reset --base-url http://127.0.0.1:8000 --vehicle-id vehicle.arcade.red.v1`
    - `rusim reset --base-url http://127.0.0.1:8000 --vehicle-id vehicle.prometeo.sport.v1`
    - `rusim reset --base-url http://127.0.0.1:8000 --vehicle-id vehicle.drone.simple.v1`
    - `rusim reset --base-url http://127.0.0.1:8000 --track-id track.roadsystem_arena.v1`

## CLI и Make
- `rusim` является каноническим продуктовым CLI:
  - `rusim server up/down/status`
  - `rusim runtime build/list/inspect/remove`
  - `rusim scenario validate/reset`
  - `rusim step`
- `Makefile` оставлен для developer/ROS2 automation:
  - запуск Unity Editor в режиме `sim-public`;
  - ROS2 desktop container;
  - demo/preflight smoke;
  - bridge/UI automation для показа.

## Упрощённый Make Workflow
- `make sim-public`: запуск Unity Editor с API для Docker/ROS bridge.
- `make demo-up`: ROS desktop + bridge + `rviz/rqt` + reset baseline.
- `make demo-status`: быстрый статус API/топиков/bridge + preflight image transport plugins.
- `make demo-proof`: строгая проверка перед презентацией (`health/reset/step-frame/camera-one-shot/odom-hz`).
- `make demo-control`: `demo-up` + запуск `rqt_robot_steering` для ручного управления через ROS2.
- `make demo-down`: остановка ROS desktop контейнера.
- `make ros-install-image-plugins`: опционально для `compressed` image transport в `rqt_image_view`.

## Asset Store / Package Manager
- Дороги: Road System package подключён как UPM пакет `com.barmetler.roadsystem` (папка `src/UnityProject/uav-simulator/Packages/com.barmetler.roadsystem`).
- Машины:
  - `PROMETEO - Car Controller` импортирован в `Assets/PROMETEO - Car Controller`.
  - `ARCADE - FREE Racing Car` импортирован в `Assets/ARCADE - FREE Racing Car`.
- Симулятор использует эти ассеты как **визуальные плагины** поверх KS0223 физики/сенсоров.

## Документация
- Индекс: `docs/README.md`
- О продукте: `docs/about-simulator.md`
- Установка: `docs/installation.md`
- Использование: `docs/usage.md`
- CLI: `docs/cli.md`
- Архитектура + диаграммы: `docs/architecture.md`
- Сборка/запуск: `docs/build.md`
- CI/CD: `docs/ci.md`
- Плагины: `docs/plugins.md`
- API: `docs/api.md`
- Контракты: `docs/contracts.md`
- Транспорт/роботы: `docs/vehicles.md`, `docs/robots/ks0223.md`, `docs/robots/simple-drone.md`
- Python SDK: `python/README.md`

## Материалы магистерской
- Индекс: `docs/master-thesis/README.md`
- Введение: `docs/master-thesis/02-introduction.md`
- Архитектура: `docs/master-thesis/04-architecture.md`
- Реализация: `docs/master-thesis/05-implementation.md`
- API: `docs/master-thesis/06-api-spec.md`
- Плагины и расширение: `docs/master-thesis/07-plugin-development.md`
- Обучение и Python: `docs/master-thesis/08-training-python.md`
- Sim2Real и реальная машинка: `docs/master-thesis/09-sim2real-real-car.md`
- Тестирование/сборка/релиз: `docs/master-thesis/10-testing-build-release.md`

## Вклад
- Правила работы: `CONTRIBUTING.md`
- Прогресс и решения: `docs/tasks.md`
