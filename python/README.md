# Python SDK

## Purpose
Минимальный клиент и инструменты для управления симулятором из Python.

## Assumptions
- Unity API поднят (`/health`, `/contract`, `/reset`, `/step`).
- Рекомендуемое окружение: `.venv` в корне проекта.

## Decisions
- Python слой остается тонким: клиент + examples + notebook.
- ROS2 bridge хранится в `python/bridges/` как отдельный модуль.

## Setup
- `make venv`
- `python -m pip install ./python`

## CLI
- `rusim version`
- `rusim upgrade --repo NMGorovenko/uav-simulator --tag latest --check-only`
- `rusim runtime upgrade --repo NMGorovenko/uav-simulator --tag latest --check-only`
- `rusim doctor --base-url http://127.0.0.1:8000`
- `rusim contract --base-url http://127.0.0.1:8000`
- `rusim runtime list`
- `rusim runtime inspect latest`
- `rusim runtime favorite show`
- `rusim runtime run --build latest --mode background --port 8011`
- `rusim runtime remove latest`
- `rusim scenario validate configs/scenarios/ks0223-demo.yaml`
- `rusim scenario reset configs/scenarios/ks0223-demo.yaml --base-url http://127.0.0.1:8000`
- `rusim scenario validate configs/scenarios/ab-corridor-v1.yaml`
- `rusim scenario reset configs/scenarios/ab-corridor-v1.yaml --base-url http://127.0.0.1:8000`
- `rusim step --base-url http://127.0.0.1:8000 --throttle 0.2 --steer 0.1`

CLI нужен как минимальный продуктовый entrypoint для:
- preflight/doctor проверки;
- scenario-driven reset;
- smoke управления без ручного `curl`.

Рекомендация по режимам запуска:
- `background` — основной безоконный режим, если нужен camera flow;
- `headless` — для CI и batch-сценариев без видео.

Registry build-артефактов и runtime state:
- `RUSIM_HOME/runtime-builds.json`
- `RUSIM_HOME/runtime/`

## Быстрый старт
- `python examples/random_agent.py --base-url http://127.0.0.1:8000`
- `python examples/ks0223_random_pwm.py --base-url http://127.0.0.1:8000`
- `python examples/carla_like_quickstart.py --base-url http://127.0.0.1:8000`

## CARLA-подобный Python API (минимум)
- Классы: `Client`, `World`, `Map`, `BlueprintLibrary`, `Waypoint`.
- Аналоги:
  - `load_world(map_id)` -> `reset` с `selectedTrackId`.
  - `spawn_actor(vehicle_id, map_id=...)` -> `reset` с `selectedVehicleId/selectedTrackId`.
  - `world.tick(command)` -> `step`.
- Waypoints для обучения передаются через `trackParams`:
  - `route.waypoints`: строка `"x,y,z;x,y,z;..."`;
  - `route.reach_distance_m`: порог достижения;
  - `route.loop`: зациклить маршрут (`true/false`).
- Прогресс маршрута возвращается в `StepResult.info`:
  - `route.current_index`, `route.remaining_waypoints`, `route.completed`.

## Presentation Notebook
- Файл: `output/jupyter-notebook/ks0223-presentation-demo.ipynb`
- Что делает:
  - API sanity check
  - camera preview
  - scripted drive + telemetry plots
  - interactive control widgets

## Quickstart Notebook
- Файл: `output/jupyter-notebook/uavsim-quickstart-demo.ipynb`
- Что делает:
  - пошаговый smoke (`health/contract/reset`)
  - короткий rollout с `step`
  - графики скорости и управляющих сигналов
  - несколько кадров фронтальной камеры

## ROS2 Training Notebook
- Файл: `output/jupyter-notebook/ks0223-ros2-training-demo.ipynb`
- Что делает:
  - сбор сенсоров из ROS2 топиков (`line_tracker`, `speedometer`)
  - мини-обучение линейной steering-модели
  - rollout обученной политики через ROS2 `/cmd_vel`

## ROS2 bridge
- Док: `python/bridges/README.md`
- Native запуск: `make ros-bridge`
- Mock запуск без ROS: `make ros-mock`
- Docker demo flow: `make demo-up` + `make demo-status`

## Training artifacts (A->B)
- Док: `python/training/README.md`
- Генерация baseline ONNX:
  - `python3 -m pip install onnx numpy`
  - `python3 python/training/build_ab_policy_artifact.py`
- Выход:
  - `python/training/artifacts/ab_corridor_policy_v1/ab_corridor_policy_v1.onnx`
  - `metadata.json`
  - `metrics.json`

## Next steps
- Добавить helpers для типизированных telemetry DTO в Python.
- Добавить e2e notebook smoke (auto-run cells subset).
