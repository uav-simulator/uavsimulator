## Purpose
Описать фактический процесс запуска проекта и разделения контуров Simulator/ROS.

## Assumptions
- Основной режим работы: Unity Editor (`Play`).
- `Makefile` используется как основной runbook интерфейс.

## Decisions
- Версия Unity: `6000.1.8f1`.
- Рекомендованный старт симулятора:
  - `make sim-public`
- Для внешних клиентов (например, ROS в Docker) использовать публичный host:
  - `make sim-public` (внутри задаёт `UAVSIM_API_HOST=+`).
- Отдельный старт ROS desktop:
  - `make ros-up`
  - `make ros-down`
- Основной пользовательский слой запуска вынесен в `make demo-*` команды.

## Быстрые команды
- Подготовка Python окружения:
  - `make venv`
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
  - `make sim-health`, `make sim-step`, `make sim-reset`

Примечание:
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
  - `vehicle.ks0223.v1` (PROMETEO visual)
  - `vehicle.ks0223.arcade.blue.v1`
  - `vehicle.ks0223.arcade.red.v1`
  - `vehicle.ks0223.arcade.gray.v1`
  - `vehicle.ks0223.arcade.purple.v1`
  - `vehicle.drone.simple.v1`
- В презентационной сцене автодрайв отключён по умолчанию.
- При старте сцены транспорт не создаётся автоматически: нужен явный `reset`.

## Быстрое переключение машины
- Через reset API/Makefile:
  - `make demo-reset UAVSIM_VEHICLE_ID=vehicle.ks0223.v1`
  - `make demo-reset UAVSIM_VEHICLE_ID=vehicle.ks0223.arcade.blue.v1`
  - `make demo-reset UAVSIM_VEHICLE_ID=vehicle.ks0223.arcade.red.v1`
  - `make demo-reset UAVSIM_VEHICLE_ID=vehicle.drone.simple.v1`
  - `make demo-reset UAVSIM_TRACK_ID=track.roadsystem_arena.v1`

## Next steps
- Добавить формальный build pipeline для standalone player.
- Добавить notebook-smoke в CI (без Unity — graceful skip, с Unity — полный прогон).
