## Purpose
Описать фактический способ запуска проекта и минимальные требования к сборке.

## Assumptions
- Запуск и сборка выполняются через Unity Editor; автоматизированная сборка пока не зафиксирована.

## Decisions
- Версия Unity: `6000.1.8f1`.
- Точка входа для MVP: сцена `Assets/Scenes/TrackScence.unity` (если не будет заменена).
- Runtime bootstrap создаёт `SimulationManager` и `HttpJsonApiHost`, если их нет на сцене.
- Если plugin assets отсутствуют, загружается встроенный fallback:
  - трек `track.basic_arena.v1`;
  - робот `vehicle.ks0223.v1`.
- ROS2 bridge опционален:
  - Unity host: `Ros2BridgeProcessHost`;
  - автостарт bridge через env `UAVSIM_ENABLE_ROS2_BRIDGE=1`;
  - внешний скрипт: `python/bridges/ros2_bridge.py`.

## Next steps
- Добавить документированный процесс сборки билда под целевые платформы после стабилизации сцены и конфигов.
