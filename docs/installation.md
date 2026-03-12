# Установка

## Цель страницы
Эта страница описывает **текущий практический путь запуска** и **целевое состояние установки** для MVP.

Сейчас проект запускается как инженерная система разработки. В `v1` он должен прийти к более простому onboarding-потоку.

## Текущее состояние
На данный момент в проекте реально поддерживаются следующие контуры запуска:

1. Unity runtime.
2. Web controller для physical runtime / unity runtime.
3. Python tooling.
4. Dockerized запуск web-контроллера.

## Предварительные требования
### Unity
- Unity `6000.1.8f1`
- проект: `src/UnityProject/uav-simulator`

### Backend / frontend
- .NET SDK 8
- Node.js 20+
- npm
- Docker Desktop для контейнерного запуска web-controller

### Python tooling
- Python `3.11+`
- зависимости из `python/requirements.txt`

## Установка CLI `rusim` в macOS zsh
Сейчас канонический bootstrap-путь для CLI:

```bash
./rusim install --write-shell-config
source ~/.zshrc
rusim --help
```

Проверка release-канала и upgrade:

```bash
rusim upgrade --repo NMGorovenko/uav-simulator --tag latest --check-only
```

## Публикация runtime релиза (без cloud-build)
На текущем этапе runtime **не собирается в GitHub Actions**.
Релиз публикуется из локально собранного `.app` и затем дополняется manifest.

Шаг 1. Собрать runtime локально:

```bash
rusim runtime build --project-path src/UnityProject/uav-simulator
```

Шаг 2. Упаковать `.app` в zip и посчитать checksum:
- `uav-simulator-macos-vX.Y.Z.zip`
- `uav-simulator-macos-vX.Y.Z.zip.sha256`

Шаг 3. Создать GitHub Release `vX.Y.Z` и загрузить эти 2 asset-а.

Шаг 4. Запустить workflow `Release Manifest` вручную с параметром `tag=vX.Y.Z`, чтобы прикрепить:
- `rusim-release-manifest.json`

Практические варианты:

### Вариант 1. Запуск из репозитория

```bash
chmod +x ./rusim
./rusim --help
```

### Вариант 2. Установка в пользовательский PATH
Рекомендуемый способ для macOS:

```bash
./rusim install --write-shell-config
```

Команда:
- ставит symlink `~/.local/bin/rusim`;
- при флаге `--write-shell-config` добавляет `~/.local/bin` в `~/.zshrc`, если записи там ещё нет.

Альтернативный alias через `Makefile`:

```bash
make sim-install-cli
```

Если `PATH` нужно прописать вручную:

```bash
echo 'export PATH="$HOME/.local/bin:$PATH"' >> ~/.zshrc
source ~/.zshrc
```

После этого команда должна быть доступна из обычного `zsh`:

```bash
rusim --help
rusim help
```

## Быстрый локальный старт
### 1. Симулятор
Открыть Unity-проект:

```bash
open src/UnityProject/uav-simulator
```

Далее:
- открыть сцену `Assets/Scenes/TrackScence.unity`;
- запустить `Play Mode`.

### 2. Web controller
Backend:

```bash
dotnet run --project src/ks0223-web-mac/backend/backend.csproj
```

Frontend:

```bash
cd src/ks0223-web-mac/frontend
npm ci
npm run dev
```

### 3. Dockerized web-controller
Использовать команды из `Makefile` и каталога `src/ks0223-web-mac`.

Если нужен контейнерный запуск:

```bash
make docker-update
make docker-up
```

## Web controller: host и port
В операторском Web UI теперь настраиваются оба параметра подключения:
- `host`
- `port`

Значения кешируются отдельно для:
- `real-robot`
- `unity-sim`

Практический смысл:
- для реальной машинки можно держать `192.168.1.121:5051`;
- для Unity runtime можно держать `127.0.0.1:8000`, `127.0.0.1:18083` или другой порт под конкретный runtime instance.

## Целевое состояние установки для MVP
Для продуктового `v1` установка должна выглядеть проще, чем сейчас.

Требования:

1. Один понятный bootstrap-путь.
2. Один CLI entrypoint.
3. Возможность запустить симулятор в `windowed / background / headless` режимах.
4. Возможность подключаться к симуляции с другой машины.

Рекомендуемая интерпретация:
- `windowed` — визуальная ручная работа;
- `background` — рекомендуемый серверный режим, если нужна камера;
- `headless` — без графики, если видео не нужно.

## Целевой CLI-поток
Текущий целевой UX уже частично реализован:

```bash
rusim version
rusim install
rusim doctor
rusim runtime build
rusim runtime run --build latest --mode background
rusim web open
```

Полностью не реализован пока только пользовательский поток `rusim web open`.

## Проверка окружения
Минимальный практический smoke-check:

```bash
./rusim --help
dotnet build src/ks0223-web-mac/backend/backend.csproj
cd src/ks0223-web-mac/frontend && npm run build
python -c "from sim_client.http_client import SimClient; print(SimClient)"
```

## Сценарный CLI-поток
Минимальный продуктовый CLI-слой уже поддерживает:

```bash
rusim doctor --base-url http://127.0.0.1:8000
rusim list tracks --base-url http://127.0.0.1:8000
rusim list vehicles --base-url http://127.0.0.1:8000
rusim reset --base-url http://127.0.0.1:8000 --track-id track.basic_arena.v1 --vehicle-id vehicle.ks0223.v1
rusim scenario validate configs/scenarios/ks0223-demo.yaml
rusim scenario reset configs/scenarios/ks0223-demo.yaml --base-url http://127.0.0.1:8000
```

Через `Makefile` это же доступно короче:

```bash
make sim-scenario-validate
make sim-scenario-print
make sim-scenario-reset
```

## Launcher runtime
CLI также поддерживает первый launcher Unity runtime:

```bash
rusim server start --mode windowed
rusim server start --mode background --port 8011
rusim server start --mode headless --port 8011
rusim server status --port 8011
rusim server stop
```

Ограничение Unity:
- новый Unity instance не сможет открыть тот же проект, если он уже открыт в другом Editor instance;
- в этом случае `rusim` возвращает явную диагностическую ошибку, а не молчаливый таймаут.

## Standalone runtime build
Теперь есть и build pipeline для standalone runtime:

```bash
rusim runtime build --project-path src/UnityProject/uav-simulator
```

После сборки build регистрируется в `rusim` registry, и его можно запускать без ручного указания пути:

```bash
rusim runtime list
rusim runtime favorite set latest
rusim runtime run --build favorite --mode background --port 8011
rusim runtime remove latest
```

Служебные данные `rusim` по умолчанию сохраняются в:

```text
.rusim/
```

При необходимости можно переопределить корень через переменную окружения:

```bash
export RUSIM_HOME=/custom/path/to/rusim-home
```

Практическое ограничение текущей проверки:
- если Unity project уже открыт в другом Editor instance, batch build через CLI будет заблокирован project lock;
- в этом случае сначала нужно закрыть текущий Unity Editor.

## Связанные документы
- [Использование](usage.md)
- [CI/CD](ci.md)
- [Roadmap](roadmap.md)
