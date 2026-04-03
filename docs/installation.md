# Установка


## Что нужно заранее

### Unity
- Unity `6000.1.8f1`
- проект: `src/UnityProject/uav-simulator`

### Backend и frontend
- .NET SDK 8
- Node.js 20+
- `npm`

### Python
- Python `3.11+`
- зависимости из `python/requirements.txt`

## Базовый путь запуска из репозитория

### 1. Установить CLI
```bash
./rusim install --write-shell-config
source ~/.zshrc
rusim --help
```

CLI также можно запускать без установки:

```bash
./rusim --help
```

### 2. Проверить зависимости
```bash
dotnet build src/ks0223-web-mac/backend/backend.csproj
cd src/ks0223-web-mac/frontend && npm ci && npm run build
cd python && python3 -c "from sim_client.http_client import SimClient; print(SimClient)"
```

### 3. Поднять Unity runtime

Вариант через CLI:

```bash
rusim server up --mode background --port 8000 --scenario configs/scenarios/demo.yaml
rusim doctor --base-url http://127.0.0.1:8000
```

Вариант через Unity Editor:
1. Открыть `src/UnityProject/uav-simulator`.
2. Открыть `Assets/Scenes/PresentationTrack.unity`, `Assets/Scenes/RoadSystemTrack.unity` или `Assets/Scenes/TrackScence.unity`.
3. Нажать `Play`.

### 4. Поднять операторский backend и frontend
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
- runtime release собирается локально и публикуется вручную в GitHub Release;
- `Release Manifest` генерирует и прикладывает `rusim-release-manifest.json`;
- `Release Rusim Package` публикует Python package artifacts по тегу.

Это release-поток для разработчика и дистрибуции, а не end-user installer.

## Связанные страницы
- [Использование](usage.md)
- [CLI `rusim`](cli.md)
- [Сборка и локальный запуск](build.md)
- [Поставка и обновление](release-distribution-model.md)
