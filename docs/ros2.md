## Purpose
Зафиксировать минимальный опциональный bridge между симулятором и ROS2 без изменения core DTO.

## Assumptions
- Unity остаётся источником физики/состояния и публикует данные через текущий HTTP API.
- ROS2 слой подключается отдельно и может быть полностью выключен.

## Decisions
- Bridge реализован как внешний Python-процесс: `python/bridges/ros2_bridge.py`.
- Unity-side host: `Ros2BridgeProcessHost` (опционально, запуск по env `UAVSIM_ENABLE_ROS2_BRIDGE=1`).
- Топики v1:
  - publish `std_msgs/String`: `/uavsim/ks0223/state_json`
  - publish `std_msgs/String`: `/uavsim/ks0223/telemetry_json`
  - publish `sensor_msgs/CompressedImage`: `/uavsim/ks0223/camera/front/image_raw/compressed`
  - subscribe `std_msgs/Float32MultiArray`: `/uavsim/ks0223/cmd_drive` (`[left_pwm, right_pwm, brake?]`)
  - subscribe `std_msgs/String`: `/uavsim/ks0223/cmd_drive_json`
- Для локальной проверки без ROS2 доступен `--mock-ros2` режим (печатает publish payloads в stdout).

## Next steps
- Добавить отдельные typed ROS2 сообщения вместо JSON-строк для state/telemetry.
- Добавить watchdog-канал и аварийный stop в bridge.
