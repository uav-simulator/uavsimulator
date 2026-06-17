# Autonomous Vehicle Training Simulator (Unity)

[![CI](https://github.com/uav-simulator/uavsimulator/actions/workflows/ci.yml/badge.svg?branch=develop)](https://github.com/uav-simulator/uavsimulator/actions/workflows/ci.yml)
[![Docs](https://github.com/uav-simulator/uavsimulator/actions/workflows/pages.yml/badge.svg?branch=develop)](https://uav-simulator.github.io/uavsimulator/)
[![Python 3.10+](https://img.shields.io/badge/python-3.10%2B-blue.svg)](python/pyproject.toml)
[![Unity 6000.1](https://img.shields.io/badge/unity-6000.1.8f1-black.svg)](src/UnityProject/uav-simulator/)
[![.NET 8](https://img.shields.io/badge/.net-8.0-512bd4.svg)](src/ks0223-web-mac/backend/)

`uav-simulator` - Unity-платформа для проверки автономного управления наземными роботами. Основной пользовательский вход - CLI `rusim`: он устанавливает runtime из GitHub Release, запускает симулятор, применяет сценарии и отправляет команды управления.

## Быстрый старт на Windows

Требования:
- Windows 10/11 x64.
- Python 3.11+.
- PowerShell.

Установка CLI из исходников:

```powershell
git clone https://github.com/uav-simulator/uavsimulator.git
cd uavsimulator
py -3.11 -m pip install .\python
rusim --help
```

Установка готового Unity runtime из релиза:

```powershell
rusim upgrade --repo uav-simulator/uavsimulator --tag latest --platform windows
rusim runtime list
rusim server up --build latest --mode windowed --port 8000 --scenario configs/scenarios/demo.yaml
rusim doctor --base-url http://127.0.0.1:8000
```

Проверка управления:

```powershell
rusim scenario reset configs/scenarios/demo.yaml --base-url http://127.0.0.1:8000
rusim step --base-url http://127.0.0.1:8000 --throttle 0.2 --steer 0.1 --brake 0.0
rusim server down
```

Для этого сценария в GitHub Release должен быть опубликован Windows runtime asset вида `uav-simulator-windows-vX.Y.Z.zip` и `rusim-release-manifest.json`.

## Быстрый старт для разработки

Unity версия проекта: `6000.1.8f1`.

```bash
python -m pip install -e "python[test,dev]"
./rusim --help
./rusim server up --mode background --port 8000 --scenario configs/scenarios/demo.yaml
./rusim doctor --base-url http://127.0.0.1:8000
```

Если runtime нужно собрать локально:

```bash
./rusim runtime build --project-path src/UnityProject/uav-simulator
./rusim runtime list
./rusim server up --build latest --mode background --port 8000
```

Операторский backend и frontend:

```bash
dotnet run --project src/ks0223-web-mac/backend/backend.csproj
cd src/ks0223-web-mac/frontend
npm ci
npm run dev
```

## Что входит в проект

- Unity runtime с HTTP JSON API: `/health`, `/contract`, `/reset`, `/step`.
- Плагинная система треков и роботов.
- Готовые треки: `track.roadsystem_arena.v1`, `track.basic_arena.v1`, `track.roadsystem_realistic.v2`, `track.cardboard_corridor.v1`.
- Готовые роботы: `vehicle.prometeo.sport.v1`, `vehicle.arcade.*`, `vehicle.drone.simple.v1`.
- Python SDK и CLI `rusim`.
- Web UI и backend для операторского управления.
- Инструменты обучения и установки ONNX-моделей.
- Опциональный ROS2 bridge для research-сценариев.

## Основные команды `rusim`

```bash
rusim version
rusim upgrade --repo uav-simulator/uavsimulator --tag latest --platform windows
rusim runtime list
rusim server up --build latest --mode background --port 8000
rusim doctor --base-url http://127.0.0.1:8000
rusim list tracks --base-url http://127.0.0.1:8000
rusim list vehicles --base-url http://127.0.0.1:8000
rusim scenario list
rusim scenario reset configs/scenarios/demo.yaml --base-url http://127.0.0.1:8000
rusim step --base-url http://127.0.0.1:8000 --throttle 0.2 --steer 0.0 --brake 0.0
rusim server down
```

## Документация

- [Индекс документации](docs/README.md)
- [О продукте](docs/about-simulator.md)
- [Установка](docs/installation.md)
- [Использование](docs/usage.md)
- [CLI `rusim`](docs/cli.md)
- [Архитектура](docs/architecture.md)
- [API](docs/api.md)
- [Model Lifecycle](docs/model-lifecycle.md)
- [Обучение моделей](docs/training.md)
- [Глоссарий](docs/glossary.md)

## Разработка

```bash
python -m pip install -e "python[test,dev]"
.venv/bin/python -m ruff check python
pytest python/tests
dotnet test src/ks0223-web-mac/backend.Tests/backend.Tests.csproj
cd src/ks0223-web-mac/frontend && npm ci && npm run lint && npm run build
```

`Makefile` используется только для developer/ROS2 automation. Для пользовательского запуска и проверки нужен `rusim`.

## Лицензии ассетов

Проект использует Unity и сторонние визуальные ассеты через локальный Unity project:
- Road System package как UPM-пакет `com.barmetler.roadsystem`.
- `PROMETEO - Car Controller`.
- `ARCADE - FREE Racing Car`.

Публичный runtime должен поставляться через GitHub Release вместе с manifest и checksum.
