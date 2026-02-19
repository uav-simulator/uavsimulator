# Autonomous Vehicle Training Simulator (Unity)

Расширяемый Unity-симулятор для обучения и проверки моделей управления наземными роботами (текущий baseline: `KS0223`).

## Текущее состояние
- Плагинная архитектура для треков и роботов.
- Runtime fallback-плагины (`track.basic_arena.v1`, `vehicle.ks0223.v1`).
- HTTP JSON API (`/health`, `/contract`, `/reset`, `/step`).
- Презентационная сцена `PresentationTrack` с разметкой и sensor HUD.
- Python SDK + Jupyter презентационный notebook.
- Опциональный ROS2 bridge (typed topics + compat JSON topics).

## Быстрый старт
Assumptions:
- Unity версия проекта: `6000.1.8f1`.

1. Создай Python окружение:
   - `make venv`
2. Запусти симулятор (публичный API для ROS/Docker):
   - `make sim-public`
3. В Unity открой сцену:
   - `Assets/Scenes/PresentationTrack.unity`
4. Нажми `Play`.
5. Подними ROS UI и bridge одной командой:
   - `make demo-up`
6. Быстрая проверка:
   - `make demo-status`

Примечание:
- По умолчанию demo-машина **не едет сама** при старте (`autoDrive = false`).

## Упрощённый Make Workflow
- `make sim-public`: запуск Unity с API для Docker/внешних клиентов.
- `make demo-up`: ROS desktop + bridge + `rviz/rqt` + reset baseline.
- `make demo-reset`: ручной reset baseline робота/трека.
- `make demo-status`: быстрый статус API/топиков/bridge.
- `make demo-down`: остановка ROS desktop контейнера.
- `make ros-install-image-plugins`: опционально для `compressed` image transport в `rqt_image_view`.

## Документация
- Индекс: `docs/README.md`
- Обзор: `docs/overview.md`
- Архитектура + диаграммы: `docs/architecture.md`
- Сборка/запуск: `docs/build.md`
- ROS2: `docs/ros2.md`
- Плагины: `docs/plugins.md`
- API: `docs/api.md`
- Транспорт/роботы: `docs/vehicles.md`, `docs/robots/ks0223.md`
- Python SDK: `python/README.md`

## Вклад
- Правила работы: `CONTRIBUTING.md`
- Прогресс и решения: `docs/tasks.md`
