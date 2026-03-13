# CLI `rusim`

## Назначение
`rusim` — канонический CLI платформы для:
- bootstrap и установки;
- проверки runtime;
- выбора сцен и машинок;
- запуска Unity runtime;
- scenario-driven reset flow.

Если команда не найдена в `zsh`, сначала выполнить:

```bash
./rusim install --write-shell-config
source ~/.zshrc
```

или использовать локальный wrapper из корня репозитория:

```bash
./rusim --help
```

Поведение справки:
- `rusim` без аргументов печатает корневую справку;
- `rusim help` делает то же самое;
- `rusim runtime`, `rusim server`, `rusim inspect`, `rusim scenario`, `rusim list` печатают справку по разделу, а не завершаются ошибкой;
- `rusim help runtime` и `rusim help server` также поддерживаются.

## Полный список команд верхнего уровня

```bash
rusim --help
rusim help
```

Доступные команды:
- `version`
- `install`
- `upgrade`
- `doctor`
- `contract`
- `list`
- `inspect`
- `reset`
- `runtime`
- `server`
- `scenario`
- `step`

## 1. Установка CLI

```bash
rusim install --help
```

Назначение:
- установить symlink `rusim` в пользовательский `PATH`;
- при необходимости дописать `PATH` в `~/.zshrc`.

Пример:

```bash
rusim install --write-shell-config
source ~/.zshrc
```

Поддерживаемые аргументы:
- `--bin-dir`
- `--rc-file`
- `--write-shell-config`

## 2. Версия и metadata

```bash
rusim version
```

Назначение:
- показать версию CLI;
- показать git sha;
- показать `rusim home`;
- показать `latest` и `favorite` build, если они уже есть.

## 3. Upgrade runtime из GitHub Release

```bash
rusim upgrade --repo NMGorovenko/uav-simulator --tag latest --check-only
rusim upgrade --repo NMGorovenko/uav-simulator --tag v0.1.0
rusim runtime upgrade --repo NMGorovenko/uav-simulator --tag latest --check-only
```

Назначение:
- скачать `rusim-release-manifest.json` из GitHub Release;
- выбрать runtime asset по текущей платформе;
- скачать runtime archive;
- проверить `sha256` (если checksum есть в manifest);
- распаковать и зарегистрировать build в локальном runtime registry.

Поддерживаемые аргументы:
- `--repo`
- `--tag` (`latest` или конкретный tag)
- `--manifest-url` (ручная ссылка на manifest)
- `--platform`
- `--channel`
- `--check-only`
- `--force`
- `--no-set-favorite`
- `--github-token` (по умолчанию берётся из `GITHUB_TOKEN`)

Примечание для private репозитория:
- задайте `GITHUB_TOKEN` (или `--github-token`), иначе скачивание release assets может вернуть `404`.

`runtime upgrade`:
- `rusim runtime upgrade ...` — алиас к тому же upgrade flow;
- удобно использовать рядом с `rusim runtime list/run/favorite`.
- при `--tag latest` runtime-upgrade выбирает самый новый Release, где есть `rusim-release-manifest.json`.

## 4. Диагностика runtime

### Проверка health и contract

```bash
rusim doctor --base-url http://127.0.0.1:8000
```

Назначение:
- проверить доступность runtime;
- получить краткую сводку по `health` и `contract`.

Ключевые поля в выводе `doctor`:
- `pluginRegistrySource`: откуда загружены плагины (`RegistryAsset`, `ResourcesDescriptorsFolder` или fallback из `BuiltinPluginFactory`);
- `activeTrackId` и `activeVehicleId`: какая сцена/машинка активны после последнего `reset`;
- `healthAvailableVehicles` и `healthAvailableTracks`: количество плагинов по данным `/health`.

### Получение полного contract

```bash
rusim contract --base-url http://127.0.0.1:8000
```

## 5. Discovery команд для tracks/scenes и vehicles

### Список tracks

```bash
rusim list tracks --base-url http://127.0.0.1:8000
```

### Список scenes

```bash
rusim list scenes --base-url http://127.0.0.1:8000
```

`scene` в CLI является alias для `track plugin`.

### Список машинок

```bash
rusim list vehicles --base-url http://127.0.0.1:8000
```

## 6. Inspect команд

### Inspect track

```bash
rusim inspect track track.roadsystem_arena.v1 --base-url http://127.0.0.1:8000
```

### Inspect scene

```bash
rusim inspect scene track.roadsystem_arena.v1 --base-url http://127.0.0.1:8000
```

### Inspect vehicle

```bash
rusim inspect vehicle vehicle.ks0223.arcade.blue.v1 --base-url http://127.0.0.1:8000
```

Команда возвращает:
- идентификатор машинки;
- тип машинки;
- список сенсоров и актуаторов;
- observation/action schema;
- пример команды `reset`.

Важно:
- к отдельной машинке в Unity не подключаются через отдельный порт;
- подключение идёт к общему runtime;
- выбор активной машинки делается через `rusim reset`.

## 7. Прямой выбор track и vehicle

```bash
rusim reset --base-url http://127.0.0.1:8000 --track-id track.roadsystem_arena.v1 --vehicle-id vehicle.ks0223.arcade.blue.v1
```

Поддерживаемые аргументы:
- `--track-id`
- `--vehicle-id`
- `--seed`
- `--time-scale`

Назначение:
- переключить активную сцену/track;
- переключить активную машинку;
- отправить нормализованный `reset` payload в runtime.

## 8. Standalone runtime build

```bash
rusim runtime build --project-path src/UnityProject/uav-simulator
```

Назначение:
- собрать standalone macOS runtime без необходимости вручную открывать Unity Editor для пользователя.

По умолчанию build получает versioned name вида:

```text
uav-simulator-2026.03.12-153000+abc123.app
```

Можно задать свой label:

```bash
rusim runtime build --label demo
```

После сборки build автоматически попадает в registry.

### Список и inspect build-артефактов

```bash
rusim runtime list
rusim runtime inspect latest
```

### Favorite build

```bash
rusim runtime favorite show
rusim runtime favorite set latest
```

### Удаление build-артефакта

```bash
rusim runtime remove latest
```

По умолчанию команда:
- удаляет build из registry;
- удаляет `.app` bundle с диска;
- если этот build сейчас запущен через `rusim`, сначала останавливает его.

Если нужно оставить файлы на диске и убрать только запись из registry:

```bash
rusim runtime remove latest --keep-files
```

Ограничение:
- если проект уже открыт в Unity Editor, batch build может быть заблокирован стандартным Unity project lock.

## 9. Управление runtime process

### Запуск через Unity project path

```bash
rusim server start --mode windowed
rusim server start --mode background --port 8011
rusim server start --mode headless --port 8011
```

### Запуск через standalone runtime

```bash
rusim server start --runtime-app build/runtime/macos/uav-simulator.app --mode windowed --port 8011
rusim server start --runtime-app build/runtime/macos/uav-simulator.app --mode background --port 8011
rusim server start --runtime-app build/runtime/macos/uav-simulator.app --mode headless --port 8011
```

### Запуск по registry id

```bash
rusim runtime run --build latest --mode background --port 8011
rusim runtime run --build favorite --mode windowed --port 8011
```

### Значение режимов

- `windowed`
  - обычный запуск с видимым окном;
  - нужен для ручной отладки и визуальной работы в симуляции.

- `background`
  - запуск без полноценного пользовательского окна, но с сохранением graphics device;
  - нужен, когда требуется камера и рендер, но не нужен обычный UI рантайма.

- `headless`
  - запуск с `-batchmode -nographics`;
  - подходит для серверных, CI и training-сценариев, где видео не требуется;
  - в этом режиме камера может быть недоступна, потому что Unity идёт без graphics device.

Практический вывод по текущей реализации:
- `background` уже подтверждён на standalone runtime: после `reset` команда `step` возвращает camera frame;
- `headless` следует использовать только там, где видеопоток не нужен по определению.

### Статус и остановка

```bash
rusim server status --port 8011
rusim server stop
```

Runtime state и logs хранятся в:

```text
$RUSIM_HOME/runtime/
```

По умолчанию:

```text
.rusim/runtime/
```

## 10. Scenario-команды

### Проверка scenario-файла

```bash
rusim scenario validate configs/scenarios/demo.yaml
```

### Печать reset payload

```bash
rusim scenario print-reset configs/scenarios/demo.yaml
```

### Применение scenario

```bash
rusim scenario reset configs/scenarios/demo.yaml --base-url http://127.0.0.1:8000
```

Важно:
- `configs/scenarios/demo.yaml` является каноническим demo-сценарием для `make demo-reset` и `make demo-proof`.
- Для разовых экспериментов можно использовать любые другие scenario-файлы, но базовый runbook проекта опирается именно на `demo.yaml`.

## 11. Одиночный step

```bash
rusim step --base-url http://127.0.0.1:8000 --throttle 0.2 --steer 0.1 --brake 0.0
```

Назначение:
- отправить один control step в runtime;
- получить краткий ответ по state/reward/frame.

## 11. Рекомендуемый smoke-test

Если Unity runtime уже запущен:

```bash
rusim doctor --base-url http://127.0.0.1:8000
rusim list tracks --base-url http://127.0.0.1:8000
rusim list vehicles --base-url http://127.0.0.1:8000
rusim inspect vehicle vehicle.ks0223.arcade.blue.v1 --base-url http://127.0.0.1:8000
rusim reset --base-url http://127.0.0.1:8000 --track-id track.roadsystem_arena.v1 --vehicle-id vehicle.ks0223.arcade.blue.v1
```

Если проверяется build registry:

```bash
rusim version
rusim runtime list
rusim runtime inspect latest
rusim runtime favorite set latest
rusim runtime favorite show
rusim runtime run --build latest --mode background --port 8011
```
