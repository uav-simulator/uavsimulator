# Документация `uav-simulator`

`uav-simulator` — платформа для сценариев `sim-to-real`: симуляция в Unity, операторский контур, продуктовый CLI, обучение модели и запуск автопилота.

## С чего начать

### Если нужно быстро поднять продукт
1. Прочитать [О продукте](about-simulator.md).
2. Пройти [Установку](installation.md).
3. Открыть [Использование](usage.md) и [CLI `rusim`](cli.md).

### Если нужно понять устройство системы
1. Прочитать [Архитектуру](architecture.md).
2. Открыть [API](api.md).
3. При необходимости перейти к [Model Lifecycle](model-lifecycle.md).

### Если нужно работать с моделью
1. Открыть [CLI `rusim`](cli.md).
2. Прочитать [API](api.md).
3. Использовать сценарий `train -> install -> activate -> run` через backend и `unity-sim`.

## Основные страницы
- [О продукте](about-simulator.md)
- [Установка](installation.md)
- [Использование](usage.md)
- [CLI `rusim`](cli.md)
- [Архитектура](architecture.md)
- [API](api.md)
- [Model Lifecycle](model-lifecycle.md)
- [Обучение моделей](training.md)
- [Глоссарий](glossary.md)

## Текущий продуктовый контур
1. Unity runtime поднимает сцену, плагины треков и машинок, а также HTTP JSON API.
2. `rusim` управляет установкой, build/runtime lifecycle, сценариями, плагинами и моделями.
3. Backend связывает `web-ui` с `unity-sim` и `real-robot`, а также держит model registry и autopilot loop.
4. Python tooling использует runtime API для обучения, сборки артефакта и KPI-оценки.

## Что смотреть дальше
- Для ручного запуска и управления: [Использование](usage.md)
- Для продуктовых команд: [CLI `rusim`](cli.md)
- Для интеграции с runtime: [API](api.md)
- Для работы с моделью: [Model Lifecycle](model-lifecycle.md)
