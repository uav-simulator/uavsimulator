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
6. Для демо-управления через ROS2 (`/cmd_vel`) открой steering UI:
   - `make demo-control`
7. Быстрая проверка:
   - `make demo-status`

Примечание:
- По умолчанию demo-машина **не едет сама** при старте (`autoDrive = false`).

## Упрощённый Make Workflow
- `make sim-public`: запуск Unity с API для Docker/внешних клиентов.
- `make demo-up`: ROS desktop + bridge + `rviz/rqt` + reset baseline.
- `make demo-reset`: ручной reset baseline робота/трека.
- `make demo-status`: быстрый статус API/топиков/bridge.
- `make demo-control`: `demo-up` + запуск `rqt_robot_steering` для ручного управления через ROS2.
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
