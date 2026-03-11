# Документация

## Purpose
Единая точка входа в техническую документацию: как запустить симулятор, как подключить ROS2, как устроена архитектура и где смотреть прогресс разработки.

## Assumptions
- Репозиторий используется как инженерная база разработки и как демонстрационный проект.
- Документы отражают фактическое состояние кода на текущем коммите.

## Decisions
- Основные runbook-команды сведены в `Makefile`.
- Архитектурный документ содержит не только текст, но и диаграммы компонентов/последовательностей.

## Next steps
- Добавить отдельный ops-runbook для CI smoke и nightly regression.

## Канонические точки входа
- Продуктовая рамка: [product-definition-v1.md](<repo>/docs/product-definition-v1.md)
- Unified runtime contract: [unified-runtime-contract-v1.md](<repo>/docs/unified-runtime-contract-v1.md)
- Definition of Done MVP: [definition-of-done-mvp.md](<repo>/docs/definition-of-done-mvp.md)
- Master-document по магистерской и продукту: [2026-03-11-sim-to-real-platform-master-document.md](<repo>/docs/research/2026-03-11-sim-to-real-platform-master-document.md)
- Архитектура: [architecture.md](<repo>/docs/architecture.md)
- API: [api.md](<repo>/docs/api.md)
- Прогресс и история решений: [tasks.md](<repo>/docs/tasks.md)

## Разделы
- Обзор проекта: [overview.md](<repo>/docs/overview.md)
- Архитектура и sequence-диаграммы: [architecture.md](<repo>/docs/architecture.md)
- Стек: [stack.md](<repo>/docs/stack.md)
- Сборка/запуск (`Makefile`, Unity, ROS): [build.md](<repo>/docs/build.md)
- API: [api.md](<repo>/docs/api.md)
- ROS2 bridge: [ros2.md](<repo>/docs/ros2.md)
- Плагины: [plugins.md](<repo>/docs/plugins.md)
- Транспорт и роботы: [vehicles.md](<repo>/docs/vehicles.md), [ks0223.md](<repo>/docs/robots/ks0223.md)
- Drone plugin: [simple-drone.md](<repo>/docs/robots/simple-drone.md)
- Интеграция ML и обучение: [mlagents.md](<repo>/docs/mlagents.md), [training.md](<repo>/docs/training.md), [experiments.md](<repo>/docs/experiments.md)
- Demo-ноутбуки: `output/jupyter-notebook/uavsim-quickstart-demo.ipynb`, `output/jupyter-notebook/ks0223-presentation-demo.ipynb`, `output/jupyter-notebook/ks0223-ros2-training-demo.ipynb`
- Магистерская: [master-thesis README](<repo>/docs/master-thesis/README.md)
- План уборки документации: [documentation-cleanup-plan.md](<repo>/docs/documentation-cleanup-plan.md)
- Отчёты и учебные материалы: `docs/reports/`, `docs/report/`

## Задачи (архив)
- Каталог задач: `docs/tasks/`
- Кратко по выполненному:
  - `task-01`…`task-03`: гигиена репозитория, аудит сцены, базовая структура симулятора.
  - `task-04`…`task-08`: контракт управления, физическая модель, сенсоры, reset-логика, reward/termination.
  - `task-09`…`task-11`: структура экспериментов и каркас training pipeline.
  - `task-12`: контракт и профиль реального робота Keyestudio KS0223.
  - `task-13`: pre-demo proof команды и preflight проверки ROS image transport.
  - `task-14`: CI smoke для `demo-proof` и параметр порога `odom hz`.
