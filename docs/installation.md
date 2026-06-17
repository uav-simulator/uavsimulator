# Установка


## Что нужно заранее

### Для готового runtime на Windows
- Windows 10/11 x64
- Python `3.11+`
- PowerShell

Unity Editor, .NET и Node.js для этого пути не обязательны: runtime скачивается через `rusim upgrade` из GitHub Release.

### Unity
- Unity `6000.1.8f1`
- проект: `src/UnityProject/uav-simulator`

### Backend и frontend
- .NET SDK 8
- Node.js 20+
- `npm`

### Python
- Python `3.11+`
- зависимости устанавливаются через `pip install -e "python[test,dev]"` (минимум для тестов и линтеров) или `pip install -e "python[test,dev,training]"` для полного RL-стека (torch + stable-baselines3, ~1 ГБ).

## Базовый путь на Windows

### 1. Установить CLI
```powershell
git clone https://github.com/uav-simulator/uavsimulator.git
cd uavsimulator
py -3.11 -m pip install .\python
rusim --help
```

### 2. Скачать и зарегистрировать runtime
```powershell
rusim upgrade --repo uav-simulator/uavsimulator --tag latest --platform windows
rusim runtime list
```

### 3. Запустить симулятор
```powershell
rusim server up --build latest --mode windowed --port 8000 --scenario configs/scenarios/demo.yaml
rusim doctor --base-url http://127.0.0.1:8000
```

### 4. Проверить управление
```powershell
rusim scenario reset configs/scenarios/demo.yaml --base-url http://127.0.0.1:8000
rusim step --base-url http://127.0.0.1:8000 --throttle 0.2 --steer 0.1 --brake 0.0
rusim server down
```

Для этого сценария в релизе должен быть опубликован Windows runtime asset `uav-simulator-windows-vX.Y.Z.zip` и актуальный `rusim-release-manifest.json`.

## Базовый путь запуска из репозитория для разработки

### 1. Установить зависимости
```bash
python -m pip install -e "python[test,dev]"
dotnet build src/ks0223-web-mac/backend/backend.csproj
cd src/ks0223-web-mac/frontend && npm ci && npm run build
cd ../../..
python -c "from sim_client.http_client import SimClient; print(SimClient)"
```

CLI также можно запускать без установки:

```bash
./rusim --help
```

### 2. Поднять Unity runtime

Вариант через CLI:

```bash
rusim server up --mode background --port 8000 --scenario configs/scenarios/demo.yaml
rusim doctor --base-url http://127.0.0.1:8000
```

Вариант через Unity Editor:
1. Открыть `src/UnityProject/uav-simulator`.
2. Открыть `Assets/Scenes/PresentationTrack.unity`, `Assets/Scenes/RoadSystemTrack.unity` или `Assets/Scenes/TrackScence.unity`.
3. Нажать `Play`.

### 3. Поднять операторский backend и frontend
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

## Standalone runtime build
Проект поддерживает локальную сборку standalone runtime:

```bash
rusim runtime build --project-path src/UnityProject/uav-simulator
rusim runtime list
rusim server up --build latest --mode background --port 8011
```

Локальный registry runtime build-ов:

```text
.rusim/runtime-builds.json
```

Runtime state и logs:

```text
.rusim/runtime/
```

Если нужен отдельный корень для runtime state:

```bash
export RUSIM_HOME=/custom/path/to/rusim-home
```

## Web UI: host и port
В Web UI хранятся отдельные настройки подключения для:
- `unity-sim`
- `real-robot`

Практически это позволяет держать разные адреса, например:
- `127.0.0.1:8000` для Unity runtime;
- `192.168.1.121:5051` для физического стенда.

## Что поддерживается для релизов сейчас
- `Release Runtime` собирает Windows runtime, публикует `uav-simulator-windows-vX.Y.Z.zip`, `.sha256` и обновляет `rusim-release-manifest.json`;
- `Release Manifest` можно запустить повторно, если assets в релизе менялись вручную;
- `Release Rusim Package` публикует Python package artifacts по тегу.

Это release-поток для разработчика и дистрибуции, а не end-user installer.

## Связанные страницы
- [Использование](usage.md)
- [CLI `rusim`](cli.md)
- [Архитектура](architecture.md)
