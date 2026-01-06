# Autonomous Vehicle Training Simulator (Unity)

Симулятор для обучения нейросетевых моделей управления беспилотным транспортом в Unity.

## MVP
- 1 трасса + 1 машина
- API для управления и получения состояния
- обучение/эксперименты запускаются из Python

## Быстрый старт (Editor)
Assumptions:
- Unity версия проекта: `6000.1.8f1` (см. `src/UnityProject/uav-simulator/ProjectSettings/ProjectVersion.txt`).

Steps:
1) Откройте Unity проект: `src/UnityProject/uav-simulator`.
2) Откройте сцену: `Assets/Scenes/TrackScence.unity`.
3) Нажмите Play.

## Документация
- Индекс: `docs/README.md`
- Архитектура: `docs/architecture.md`
- Стек: `docs/stack.md`
- Сборка: `docs/build.md`
- Обучение (концептуально): `docs/training.md`
- Плагины: `docs/plugins.md`
- API (план): `docs/api.md`
- CI (план): `docs/ci.md`
- Ассеты и лицензии: `docs/assets.md`
- Магистерская (черновик структуры): `docs/master-thesis/README.md`
- Python SDK: `python/README.md`

## Вклад
- Правила работы: `CONTRIBUTING.md`.
- Прогресс и решения фиксируются в `docs/tasks.md`.
