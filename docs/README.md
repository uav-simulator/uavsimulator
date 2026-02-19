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

## Разделы
- Обзор проекта: `docs/overview.md`
- Архитектура и sequence-диаграммы: `docs/architecture.md`
- Стек: `docs/stack.md`
- Сборка/запуск (`Makefile`, Unity, ROS): `docs/build.md`
- API: `docs/api.md`
- ROS2 bridge: `docs/ros2.md`
- Плагины: `docs/plugins.md`
- Транспорт и роботы: `docs/vehicles.md`, `docs/robots/ks0223.md`
- Интеграция ML и обучение: `docs/mlagents.md`, `docs/training.md`, `docs/experiments.md`
- Прогресс/задачи: `docs/tasks.md`
- Отчёты: `docs/reports/`
- Магистерская: `docs/master-thesis/README.md`

## Задачи (архив)
- Каталог задач: `docs/tasks/`
- Кратко по выполненному:
  - `task-01`…`task-03`: гигиена репозитория, аудит сцены, базовая структура симулятора.
  - `task-04`…`task-08`: контракт управления, физическая модель, сенсоры, reset-логика, reward/termination.
  - `task-09`…`task-11`: структура экспериментов и каркас training pipeline.
  - `task-12`: контракт и профиль реального робота Keyestudio KS0223.
