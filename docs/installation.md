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

## Целевое состояние установки для MVP
Для продуктового `v1` установка должна выглядеть проще, чем сейчас.

Требования:

1. Один понятный bootstrap-путь.
2. Один CLI entrypoint.
3. Возможность запустить симулятор в server/headless режиме.
4. Возможность подключаться к симуляции с другой машины.

## Целевой CLI-поток
Планируемый UX:

```bash
rusim install
rusim doctor
rusim server start --profile ks0223-demo
rusim web open
```

Это еще не реализовано полностью, но именно такой поток считается целевым.

## Проверка окружения
Минимальный практический smoke-check:

```bash
dotnet build src/ks0223-web-mac/backend/backend.csproj
cd src/ks0223-web-mac/frontend && npm run build
python -c "from sim_client.http_client import SimClient; print(SimClient)"
```

## Сценарный CLI-поток
Минимальный продуктовый CLI-слой уже поддерживает:

```bash
rusim doctor --base-url http://127.0.0.1:8000
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
rusim runtime build --project-path src/UnityProject/uav-simulator --output build/runtime/macos/uav-simulator.app
```

После сборки standalone app можно запускать без Unity Editor:

```bash
rusim server start --runtime-app build/runtime/macos/uav-simulator.app --mode windowed --port 8011
rusim server start --runtime-app build/runtime/macos/uav-simulator.app --mode headless --port 8011
make sim-server-start-runtime MODE=headless UAVSIM_API_PORT=8011
```

Практическое ограничение текущей проверки:
- если Unity project уже открыт в другом Editor instance, batch build через CLI будет заблокирован project lock;
- в этом случае сначала нужно закрыть текущий Unity Editor.

## Связанные документы
- [Использование](usage.md)
- [CI/CD](ci.md)
- [Roadmap](roadmap.md)
