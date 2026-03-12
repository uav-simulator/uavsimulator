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

## Полный список команд верхнего уровня

```bash
rusim --help
```

Доступные команды:
- `install`
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

## 2. Диагностика runtime

### Проверка health и contract

```bash
rusim doctor --base-url http://127.0.0.1:8000
```

Назначение:
- проверить доступность runtime;
- получить краткую сводку по `health` и `contract`.

### Получение полного contract

```bash
rusim contract --base-url http://127.0.0.1:8000
```

## 3. Discovery команд для tracks/scenes и vehicles

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

## 4. Inspect команд

### Inspect track

```bash
rusim inspect track track.basic_arena.v1 --base-url http://127.0.0.1:8000
```

### Inspect scene

```bash
rusim inspect scene track.basic_arena.v1 --base-url http://127.0.0.1:8000
```

### Inspect vehicle

```bash
rusim inspect vehicle vehicle.ks0223.v1 --base-url http://127.0.0.1:8000
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

## 5. Прямой выбор track и vehicle

```bash
rusim reset --base-url http://127.0.0.1:8000 --track-id track.basic_arena.v1 --vehicle-id vehicle.ks0223.v1
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

## 6. Standalone runtime build

```bash
rusim runtime build --project-path src/UnityProject/uav-simulator --output build/runtime/macos/uav-simulator.app
```

Назначение:
- собрать standalone macOS runtime без необходимости вручную открывать Unity Editor для пользователя.

Ограничение:
- если проект уже открыт в Unity Editor, batch build может быть заблокирован стандартным Unity project lock.

## 7. Управление runtime process

### Запуск через Unity project path

```bash
rusim server start --mode windowed
rusim server start --mode headless --port 8011
```

### Запуск через standalone runtime

```bash
rusim server start --runtime-app build/runtime/macos/uav-simulator.app --mode windowed --port 8011
rusim server start --runtime-app build/runtime/macos/uav-simulator.app --mode headless --port 8011
```

### Статус и остановка

```bash
rusim server status --port 8011
rusim server stop
```

## 8. Scenario-команды

### Проверка scenario-файла

```bash
rusim scenario validate configs/scenarios/ks0223-demo.yaml
```

### Печать reset payload

```bash
rusim scenario print-reset configs/scenarios/ks0223-demo.yaml
```

### Применение scenario

```bash
rusim scenario reset configs/scenarios/ks0223-demo.yaml --base-url http://127.0.0.1:8000
```

## 9. Одиночный step

```bash
rusim step --base-url http://127.0.0.1:8000 --throttle 0.2 --steer 0.1 --brake 0.0
```

Назначение:
- отправить один control step в runtime;
- получить краткий ответ по state/reward/frame.

## 10. Рекомендуемый smoke-test

Если Unity runtime уже запущен:

```bash
rusim doctor --base-url http://127.0.0.1:8000
rusim list tracks --base-url http://127.0.0.1:8000
rusim list vehicles --base-url http://127.0.0.1:8000
rusim inspect vehicle vehicle.ks0223.v1 --base-url http://127.0.0.1:8000
rusim reset --base-url http://127.0.0.1:8000 --track-id track.basic_arena.v1 --vehicle-id vehicle.ks0223.v1
```
