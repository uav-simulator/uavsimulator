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
  - в Unity нажать `Play`
  - `make demo-up`
  - `make demo-status`
- Точечные операции:
  - `make demo-reset`
  - `make demo-down`
  - `make demo-restart`
- Advanced:
  - `make ros-up`, `make ros-bridge-container`, `make ros-ui-container`, `make ros-topics`
  - `make ros-install-image-plugins` (если нужен `compressed` transport в `rqt_image_view`)
  - `make ros-bridge`, `make ros-mock`
  - `make sim-health`, `make sim-step`, `make sim-reset`

## Runtime поведение
- `RuntimeSceneBootstrap` гарантирует наличие:
  - `SimulationManager`
  - `HttpJsonApiHost`
  - `Ros2BridgeProcessHost` (опционально, по `UAVSIM_ENABLE_ROS2_BRIDGE=1`)
- Если plugin assets отсутствуют, включается fallback:
  - `track.basic_arena.v1`
  - `vehicle.ks0223.v1`
- В презентационной сцене автодрайв отключён по умолчанию.

## Next steps
- Добавить формальный build pipeline для standalone player.
- Добавить отдельный make target для batch smoke в CI.
