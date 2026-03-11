# Roadmap

## Зачем эта страница
Эта страница нужна не для абстрактных мечтаний, а для понятного ответа на два вопроса:

1. Где мы находимся сейчас.
2. Что должно быть доведено до цельного MVP.

## Где мы сейчас
Текущий статус проекта:

- Unity runtime уже существует и внешне управляется через HTTP API.
- Реальный стенд уже подключен в unified operator flow.
- Один Web UI уже умеет работать и с `unity-sim`, и с `real-robot`.
- Backend уже выполняет роль unified orchestration layer.
- Базовые health/logging/camera/telemetry контуры уже собраны.

То есть проект уже **не на стадии идеи**, а на стадии приведения к цельному продукту.

## Текущая стадия
**Стадия:** сборка и канонизация ядра продукта.

Главная задача сейчас:
- не добавлять случайные фичи;
- а зафиксировать ядро, контракты, docs-layer и сценарии запуска.

## Карта этапов
### Этап 1. Ядро operator-core
Статус: `в основном выполнено`

Включает:
- unified backend;
- unified Web UI;
- real runtime;
- unity runtime;
- базовые health / telemetry / camera / logging.

### Этап 2. Канонизация контрактов и документации
Статус: `в работе`

Включает:
- product definition;
- unified runtime contract;
- autopilot integration contract;
- simulator scenario config contract;
- понятные страницы docs и GitHub Pages.

### Этап 3. Product usability
Статус: `частично сделано`

Нужно довести:
- installer/bootstrap;
- CLI;
- server/headless режим;
- сценарную конфигурацию;
- понятную quickstart-инструкцию.

### Этап 4. Sim-to-real workflow
Статус: `частично сделано`

Нужно довести:
- связанный сценарий `train/test in sim -> validate on real`;
- автопилотный интеграционный поток;
- smoke-flow сравнения `sim vs real`.

### Этап 5. Research packaging
Статус: `частично сделано`

Нужно довести:
- Jupyter notebooks;
- documented Python flow;
- ROS2 слой как отдельный, но устойчивый integration contour.

## Definition of Done для MVP
MVP считается собранным, когда одновременно выполнены условия:

1. Один UI работает с двумя runtime-режимами.
2. Один backend скрывает runtime-специфику.
3. Контракты зафиксированы и не живут «в голове».
4. Есть понятный сценарий запуска и проверки.
5. Документация опубликована и поддерживается как источник правды.

## Что делаем следующим
1. Доводим `Autopilot Integration Contract v1`.
2. Доводим `Simulator Scenario Config Contract v1`.
3. Поднимаем готовый docs-layer через GitHub Pages.
4. После этого идем в реализацию CLI/server-headless path.
