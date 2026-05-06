# CLI `rusim`

`rusim` — основной CLI платформы для установки, запуска и диагностики runtime, а также для работы со сценариями, плагинами и моделями.

Если команда не найдена в `zsh`, сначала выполнить:

```bash
./rusim install --write-shell-config
source ~/.zshrc
```

Или использовать локальный wrapper из корня репозитория:

```bash
./rusim --help
```

## Основные группы команд

- `install`
- `version`
- `upgrade`
- `doctor`
- `contract`
- `list`
- `inspect`
- `reset`
- `step`
- `scenario`
- `model`
- `plugin`
- `runtime`
- `server`

## 1. Установка и bootstrap

```bash
rusim install --write-shell-config
rusim version
rusim upgrade --repo NMGorovenko/uav-simulator --tag latest --check-only
```

Назначение:
- установить CLI;
- проверить версию и локальный runtime registry;
- скачать и зарегистрировать runtime build из GitHub Release.

## 2. Runtime lifecycle

```bash
rusim runtime build --project-path src/UnityProject/uav-simulator
rusim runtime list
rusim runtime favorite set latest
rusim server up --build latest --mode background --port 8000
rusim server status
rusim server down
```

Назначение:
- собрать standalone runtime;
- выбрать рабочий build;
- поднять и остановить runtime.

## 3. Диагностика и discovery

```bash
rusim doctor --base-url http://127.0.0.1:8000
rusim contract --base-url http://127.0.0.1:8000
rusim list tracks --base-url http://127.0.0.1:8000
rusim list vehicles --base-url http://127.0.0.1:8000
rusim inspect vehicle vehicle.prometeo.sport.v1 --base-url http://127.0.0.1:8000
```

Назначение:
- проверить доступность runtime;
- получить contract;
- увидеть доступные треки и роботов;
- инспектировать конкретный plugin descriptor.

## 4. Управление симуляцией

```bash
rusim reset --base-url http://127.0.0.1:8000 --track-id track.roadsystem_arena.v1 --vehicle-id vehicle.prometeo.sport.v1
rusim step --base-url http://127.0.0.1:8000 --throttle 0.3 --steer 0.1
rusim scenario list
rusim scenario list --json
rusim scenario validate configs/scenarios/ab-corridor-v1.yaml
rusim scenario print-reset configs/scenarios/ab-corridor-v1.yaml
rusim scenario reset configs/scenarios/ab-corridor-v1.yaml --base-url http://127.0.0.1:8000
```

Назначение:
- переключать активный track и vehicle;
- отправлять один шаг управления;
- валидировать и применять YAML-сценарии;
- получать список доступных сценариев из `configs/scenarios/` (паритет с Web UI scenario picker и backend `/api/scenarios`).

## 5. Управление моделями

```bash
rusim model install python/training/artifacts/ab_corridor_policy_v1/ab_corridor_policy_v1.onnx --activate
rusim model list
rusim model active
rusim model activate model-20260329-xxxx
```

Назначение:
- загрузить ONNX-модель в backend registry;
- автоматически подхватить `metadata.json` и `metrics.json` из той же директории;
- активировать выбранную модель перед запуском автопилота.

Поддерживаемые аргументы `model install`:
- `--backend-url`
- `--name`
- `--version`
- `--source`
- `--metadata`
- `--metrics`
- `--activate`

## 6. Управление плагинами

```bash
rusim plugin install ./my-plugin.rusim-plugin.zip
rusim plugin list
rusim plugin list --json
rusim plugin remove vehicle.custom.racer.v1
rusim plugin new vehicle.my_brand.racer.v1 --type vehicle --display-name "My Racer"
```

Назначение:
- установить пользовательский plugin из `.rusim-plugin.zip` архива;
- увидеть built-in и user plugins;
- удалить пользовательский plugin из каталога runtime;
- создать новый plugin-проект из шаблона.

## Типовой продуктовый сценарий

```bash
# 1. Поднять runtime
rusim server up --build latest --mode background --port 8000

# 2. Применить сценарий
rusim scenario reset configs/scenarios/ab-corridor-v1.yaml --base-url http://127.0.0.1:8000

# 3. Установить модель
rusim model install python/training/artifacts/ab_corridor_policy_v1/ab_corridor_policy_v1.onnx --activate

# 4. Проверить runtime
rusim doctor --base-url http://127.0.0.1:8000
```

Дальше запуск автопилота выполняется через `web-ui` или backend API.

## Связанные страницы
- [Использование](usage.md)
- [API](api.md)
- [Архитектура](architecture.md)
