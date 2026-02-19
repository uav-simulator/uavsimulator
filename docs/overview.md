## Purpose
Кратко зафиксировать, что уже реализовано, какие сценарии поддерживаются и куда проект движется.

## Assumptions
- Текущий baseline: наземный дифференциальный робот `KS0223`.
- Основной runtime: Unity Editor в режиме `Play`.

## Decisions
- Core транспорта симулятора: HTTP JSON API.
- ROS2 интеграция: опциональный слой (bridge), не меняет core DTO.
- Расширение симулятора: через плагины треков/роботов.

## Что уже работает
- Сцены:
  - `Assets/Scenes/PresentationTrack.unity` (презентационная трасса).
  - `Assets/Scenes/TrackScence.unity` (базовый шаблон).
- Контракт и runtime:
  - `SimulationManager` + plugin registry + fallback assets.
  - API `GET /health`, `GET /contract`, `POST /reset`, `POST /step`.
- Сенсоры/телеметрия:
  - camera, speedometer, ultrasonic, line tracker, powertrain.
- Python:
  - SDK `python/sim_client/*`.
  - examples и notebook `output/jupyter-notebook/ks0223-presentation-demo.ipynb`.
- ROS2:
  - typed topics + `cmd_vel` control через `python/bridges/ros2_bridge.py`.
  - RViz конфиг `ros2/rviz/uavsim_demo.rviz`.

## Ограничения текущего этапа
- API работает только в `Play` режиме Unity.
- ROS2 desktop в Docker требует доступного API host (рекомендуется запуск симулятора через `make sim-public`).
- `rqt_plot` может требовать дополнительные Python зависимости в контейнере.

## Next steps
- Добавить watchdog и аварийный stop в ROS2 bridge.
- Добавить стабильный сценарий симуляции для regression smoke.
- Начать упаковку custom ROS2 msg для line/powertrain diagnostics.
