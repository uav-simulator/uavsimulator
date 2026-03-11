# Использование

## Основные режимы работы
Платформа должна поддерживать два основных режима:

1. `unity-sim`
2. `real-robot`

Для оператора оба режима должны выглядеть одинаково.

## Базовый operator flow
```mermaid
sequenceDiagram
    participant User as "Оператор"
    participant UI as "Web UI"
    participant Backend as "Operator backend"
    participant Runtime as "Unity / Physical runtime"

    User->>UI: выбирает runtime и host
    UI->>Backend: POST /api/connection/connect
    Backend->>Runtime: connect
    Runtime-->>Backend: status / contract / telemetry
    Backend-->>UI: unified status
    User->>UI: отправляет команды
    UI->>Backend: POST /api/command
    Backend->>Runtime: runtime-specific command
    Runtime-->>Backend: telemetry / camera / health
    Backend-->>UI: unified updates
```

## Web UI
Текущий Web UI используется как единый операторский интерфейс:

- подключение и отключение;
- отображение статуса;
- ручное управление;
- просмотр камеры;
- телеметрия и сенсоры;
- логирование.

## HTTP API
Канонический операторский API описан в:

- [Unified Runtime Contract v1](unified-runtime-contract-v1.md)
- [API](api.md)

Ключевые точки:

- `POST /api/connection/connect`
- `POST /api/connection/disconnect`
- `GET /api/status`
- `GET /api/health`
- `POST /api/command`
- `GET /api/camera/*`
- `GET /api/sensors/*`

## CLI
Полноценный CLI еще не зафиксирован в коде как завершенный продуктовый слой.

Но в целевом `v1` CLI должен покрывать:

- запуск симулятора;
- запуск в server/headless режиме;
- загрузку scenario config;
- запуск operator stack;
- preflight/doctor проверки.

## Python SDK и notebooks
Python tooling используется для:

- smoke-проверок;
- интеграционных тестов;
- Jupyter-экспериментов;
- будущего training flow.

Это research-layer, но он должен опираться на стабильные product-core интерфейсы.

## ROS2
ROS2 используется как отдельный interoperability-слой.

Он нужен для:

- публикации typed topics;
- интеграции со стандартными robotics-инструментами;
- внешних экспериментальных сценариев.

ROS2 не должен быть обязательной зависимостью базового operator flow.

## Автопилот
Автопилот рассматривается как внешний модуль, который подключается к платформе через отдельный интеграционный контракт.

Канонический документ:
- [Autopilot Integration Contract v1](autopilot-integration-contract-v1.md)

## Сценарии симуляции
Симуляция должна подниматься не через ручную возню по сцене, а через сценарный/config entrypoint.

Канонический документ:
- [Simulator Scenario Config Contract v1](simulator-scenario-config-contract-v1.md)
