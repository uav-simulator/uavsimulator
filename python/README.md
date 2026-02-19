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

## Быстрый старт
- `python examples/random_agent.py --base-url http://127.0.0.1:8000`
- `python examples/ks0223_random_pwm.py --base-url http://127.0.0.1:8000`

## Presentation Notebook
- Файл: `output/jupyter-notebook/ks0223-presentation-demo.ipynb`
- Что делает:
  - API sanity check
  - camera preview
  - scripted drive + telemetry plots
  - interactive control widgets

## ROS2 bridge
- Док: `python/bridges/README.md`
- Native запуск: `make ros-bridge`
- Mock запуск без ROS: `make ros-mock`
- Docker demo flow: `make demo-up` + `make demo-status`

## Next steps
- Добавить helpers для типизированных telemetry DTO в Python.
- Добавить e2e notebook smoke (auto-run cells subset).
