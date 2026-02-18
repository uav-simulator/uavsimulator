## Purpose
Зафиксировать минимальный опциональный bridge между симулятором и ROS2 без изменения core DTO.

## Assumptions
- Unity остаётся источником физики/состояния и публикует данные через текущий HTTP API.
- ROS2 слой подключается отдельно и может быть полностью выключен.

## Decisions
- Bridge реализован как внешний Python-процесс: `python/bridges/ros2_bridge.py`.
- Unity-side host: `Ros2BridgeProcessHost` (опционально, запуск по env `UAVSIM_ENABLE_ROS2_BRIDGE=1`).
- Топики v2 (typed + backward compatible JSON):
  - publish `nav_msgs/Odometry`: `/uavsim/ks0223/odom`
  - publish `std_msgs/Float32`: `/uavsim/ks0223/speedometer/mps`
  - publish `sensor_msgs/BatteryState`: `/uavsim/ks0223/battery_state`
  - publish `sensor_msgs/Range`: `/uavsim/ks0223/ultrasonic/front`
  - publish `std_msgs/Float32MultiArray`: `/uavsim/ks0223/line_tracker/front_norm`
  - publish `std_msgs/Float32MultiArray`: `/uavsim/ks0223/powertrain/estimate`
  - publish `sensor_msgs/Image`: `/uavsim/ks0223/camera/front/image_raw`
  - publish `sensor_msgs/CompressedImage`: `/uavsim/ks0223/camera/front/image_raw/compressed`
  - publish `std_msgs/String`: `/uavsim/ks0223/state_json`
  - publish `std_msgs/String`: `/uavsim/ks0223/telemetry_json`
  - subscribe `geometry_msgs/Twist`: `/uavsim/ks0223/cmd_vel` (и `/cmd_vel` для `rqt_robot_steering`)
  - subscribe `std_msgs/Float32MultiArray`: `/uavsim/ks0223/cmd_drive` (`[left_pwm, right_pwm, brake?]`)
  - subscribe `std_msgs/String`: `/uavsim/ks0223/cmd_drive_json`
- Для локальной проверки без ROS2 доступен `--mock-ros2` режим (печатает publish payloads в stdout).

## Demo one-command launch
- Подними Unity в `Play`.
- Выполни:
  - `python/bridges/run_ros2_demo.sh`
- Скрипт поднимает bridge и открывает `rviz2` с готовым конфигом `ros2/rviz/uavsim_demo.rviz`.

## Optional control UI
- `rqt_robot_steering`:
  - `ros2 run rqt_robot_steering rqt_robot_steering --ros-args -r /cmd_vel:=/uavsim/ks0223/cmd_vel`

## Next steps
- Добавить watchdog-канал и аварийный stop в bridge.
- При необходимости выделить custom msg package под line/powertrain/diagnostics.
