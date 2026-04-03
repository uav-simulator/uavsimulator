# Unified Runtime Contract


## Назначение
Контракт фиксирует:
- что frontend получает от backend;
- какие runtime-режимы поддерживаются;
- какие пользовательские действия считаются каноническими;
- где заканчивается product-core и начинается research-layer.

## Поддерживаемые runtime-режимы
- `unity-sim`
- `real-robot`

Для frontend оба режима проходят через один операторский контур.

## Главный принцип
Frontend не знает transport-детали конкретного runtime.

Frontend работает только с нормализованным backend API, а backend уже переводит:
- команды управления;
- camera flow;
- telemetry;
- health;
- logging

в механику конкретного runtime provider-а.

## Product API backend
Канонические точки операторского backend:
- `POST /api/connection/connect`
- `POST /api/connection/disconnect`
- `GET /api/status`
- `GET /api/health`
- `POST /api/command`
- `GET /api/camera/*`
- `GET /api/sensors/*`

## Что backend должен уметь
- подключать и отключать выбранный runtime;
- выполнять best-effort STOP на disconnect и ошибках;
- отдавать единый status/health;
- предоставлять камеру и телеметрию без runtime-specific терминологии на уровне UI;
- логировать операторскую сессию единообразно.

## Unity runtime side
Для `unity-sim` backend работает поверх Unity HTTP JSON API:
- `GET /health`
- `GET /contract`
- `POST /reset`
- `POST /step`

Это отдельный runtime interface, а не интерфейс frontend.

## Multi-agent
Текущий контракт допускает:
- выбор нескольких агентов на одном runtime;
- адресную команду конкретному `agentId`;
- адресный camera flow для конкретного агента;
- разные browser tabs для разных агентов одного runtime.

## Что не входит в этот контракт
- ROS2 message layer;
- Jupyter/notebook flow;
- внутренние детали Unity scene graph;
- конкретный transport физического стенда.

## Связанные страницы
- [Product Definition](product-definition.md)
- [API](api.md)
- [Simulator Scenario Config Contract](simulator-scenario-config-contract.md)
