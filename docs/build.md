## Purpose
Описать фактический процесс запуска проекта и разделения контуров Simulator/ROS.

## Assumptions
- Основной режим работы: Unity Editor (`Play`).
- `rusim` является каноническим продуктовым CLI.
- `Makefile` используется только для developer/ROS automation.

## Decisions
- Версия Unity: `6000.1.8f1`.
- Основной product lifecycle:
  - `rusim server up`
  - `rusim server status`
  - `rusim server down`
- Рекомендованный старт симулятора:
  - `make sim-public`
- Для внешних клиентов (например, ROS в Docker) использовать публичный host:
  - `make sim-public` (внутри задаёт `UAVSIM_API_HOST=+`).
- Отдельный старт ROS desktop:
  - `make ros-up`
  - `make ros-down`
- Основной пользовательский слой запуска вынесен в `rusim`.
- `make demo-*` сохранены для ROS2/demo orchestration и preflight.

## Быстрые команды
- Подготовка Python окружения:
  - `make venv`
- Product CLI:
  - `rusim server up --mode background --port 8000 --scenario configs/scenarios/demo.yaml`
  - `rusim doctor --base-url http://127.0.0.1:8000`
  - `rusim step --base-url http://127.0.0.1:8000 --throttle 0.2 --steer 0.1`
  - `rusim server down`
- Ежедневный флоу:
  - `make sim-public`
  - в Unity открыть `Assets/Scenes/PresentationTrack.unity` или `Assets/Scenes/RoadSystemTrack.unity`
  - если нужно пересоздать RoadSystem-сцену: `UavSimulator/Scene/Build RoadSystem Track Scene`
  - нажать `Play`
  - `make demo-up`
  - (опционально, для ручного ROS2 управления) `make demo-control`
  - `make demo-status`
  - перед показом: `make demo-proof`
- Точечные операции:
  - `make demo-reset`
  - `make demo-proof`
  - `make demo-down`
  - `make demo-restart`
- Advanced:
  - `make ros-up`, `make ros-bridge-container`, `make ros-ui-container`, `make ros-topics`
  - `make ros-install-image-plugins` (если нужен `compressed` transport в `rqt_image_view`)
  - `make ros-bridge`, `make ros-mock`
  - legacy compatibility aliases в `Makefile` оставлены только для внутренних скриптов

Примечание:
- `make demo-reset` и `make demo-proof` теперь используют единый сценарий `configs/scenarios/demo.yaml`.
- При необходимости можно подменить demo-сценарий через `DEMO_SCENARIO=...`, не меняя `Makefile`.
- `make demo-status` теперь выводит preflight по `ros-humble-image-transport-plugins`.
- `make demo-proof` завершится ошибкой, если не выполняется любой из шагов проверки (`health/reset/step-frame/camera-one-shot/odom-hz`).
- Порог `odom hz` можно настроить через `UAVSIM_ODOM_HZ_MIN` (по умолчанию `10`).

## Runtime поведение
- `RuntimeSceneBootstrap` гарантирует наличие:
  - `SimulationManager`
  - `HttpJsonApiHost`
  - `Ros2BridgeProcessHost` (опционально, по `UAVSIM_ENABLE_ROS2_BRIDGE=1`)
- Если plugin assets отсутствуют, включается fallback:
  - `track.basic_arena.v1`
  - `track.roadsystem_arena.v1`
  - `vehicle.prometeo.sport.v1` (PROMETEO visual)
  - `vehicle.arcade.blue.v1`
  - `vehicle.arcade.red.v1`
  - `vehicle.arcade.gray.v1`
  - `vehicle.arcade.purple.v1`
  - `vehicle.drone.simple.v1`
- В презентационной сцене автодрайв отключён по умолчанию.
- При старте сцены транспорт не создаётся автоматически: нужен явный `reset`.

## Быстрое переключение машины
- Через demo-сценарий:
  - `rusim scenario reset configs/scenarios/demo.yaml --base-url http://127.0.0.1:8000`
- Через Unity simulator web UI:
  - выбрать `track`;
  - выбрать primary vehicle;
  - при необходимости добавить вторую машинку;
  - выбрать `camera mode` и `control agent`.

## Next steps
- Добавить формальный build pipeline для standalone player.
- Добавить notebook-smoke в CI (без Unity — graceful skip, с Unity — полный прогон).
