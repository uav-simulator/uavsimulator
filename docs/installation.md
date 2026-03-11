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

## Связанные документы
- [Использование](usage.md)
- [CI/CD](ci.md)
- [Roadmap](roadmap.md)
