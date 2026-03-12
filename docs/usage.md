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
Минимальный продуктовый CLI-слой уже добавлен.

Полная справка по командам вынесена в отдельный документ:
- [CLI `rusim`](cli.md)

Если `rusim` не найден в `zsh`, сначала выполнить:

```bash
./rusim install --write-shell-config
source ~/.zshrc
```

или запускать из корня репозитория:

```bash
./rusim --help
```

CLI ведёт себя дружелюбно:
- `rusim` без аргументов показывает корневую справку;
- `rusim help` и `rusim help runtime` работают как ожидается;
- пустые группы команд (`rusim runtime`, `rusim server`, `rusim inspect`, `rusim scenario`, `rusim list`) показывают help по разделу вместо argparse error.

Поддерживаемые команды:

- `version`
- `install`
- `doctor`
- `contract`
- `list tracks`
- `list scenes`
- `list vehicles`
- `inspect track`
- `inspect scene`
- `inspect vehicle`
- `reset`
- `scenario validate`
- `scenario print-reset`
- `scenario reset`
- `step`

Примеры:

```bash
rusim version
rusim install --write-shell-config
rusim doctor --base-url http://127.0.0.1:8000
rusim contract --base-url http://127.0.0.1:8000
rusim list tracks --base-url http://127.0.0.1:8000
rusim list vehicles --base-url http://127.0.0.1:8000
rusim inspect vehicle vehicle.ks0223.v1 --base-url http://127.0.0.1:8000
rusim reset --base-url http://127.0.0.1:8000 --track-id track.basic_arena.v1 --vehicle-id vehicle.ks0223.v1
rusim scenario validate configs/scenarios/ks0223-demo.yaml
rusim scenario reset configs/scenarios/ks0223-demo.yaml --base-url http://127.0.0.1:8000
rusim step --base-url http://127.0.0.1:8000 --throttle 0.2 --steer 0.1
```

Важно:
- `scene` в CLI является alias для track plugin;
- к отдельной машинке в Unity не подключаются через отдельный порт;
- подключение идёт к общему runtime, а выбор активной машинки/сцены делается через `rusim reset`.

Ограничение текущего среза:
- CLI пока не управляет полным release/distribution lifecycle;
- для `headless` камера не гарантируется, потому что Unity запускается с `-nographics`.

Частично это уже закрыто:
- добавлен `rusim server start/status/stop` для запуска отдельного Unity runtime instance;
- но launcher всё ещё ограничен стандартным Unity project lock.

Примеры:

```bash
rusim server start --mode windowed
rusim server start --mode background --port 8011
rusim server start --mode headless --port 8011
rusim server status --port 8011
rusim server stop
```

Практическое ограничение:
- если проект уже открыт в другом Unity Editor instance, headless/windowed launcher второго instance не сможет занять тот же project path.

## Standalone runtime
Для продуктового сценария без Unity Editor используется standalone runtime build:

```bash
rusim runtime build --project-path src/UnityProject/uav-simulator
rusim runtime list
rusim runtime favorite set latest
rusim runtime run --build favorite --mode background --port 8011
rusim runtime remove latest
```

Именно этот путь должен стать основным для конечного пользователя.

Рекомендуемая интерпретация режимов:
- `windowed` — ручная визуальная работа;
- `background` — есть камера и рендер, но не нужен обычный UI;
- `headless` — максимально лёгкий режим без графики, useful для CI, server-side rollout и batch training.

Практически подтверждено:
- standalone runtime в режиме `background` успешно отвечает на `/health` и `/contract`;
- после `reset` endpoint `/step` возвращает `frame.dataBase64`, то есть camera flow в этом режиме работает.

Registry build-артефактов хранится в:
- `.rusim/runtime-builds.json`

Runtime state и logs:
- `.rusim/runtime/`

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
