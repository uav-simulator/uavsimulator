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

## Next steps
- Добавить helpers для типизированных telemetry DTO в Python.
- Добавить e2e notebook smoke (auto-run cells subset).
