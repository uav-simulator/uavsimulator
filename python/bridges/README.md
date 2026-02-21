# ROS2 Bridge

`ros2_bridge.py` связывает Unity HTTP API и ROS2 топики без изменений в core контрактах симулятора.

## Topics
Publish:
- `nav_msgs/Odometry`: `/uavsim/ks0223/odom`
- `std_msgs/Float32`: `/uavsim/ks0223/speedometer/mps`
- `sensor_msgs/BatteryState`: `/uavsim/ks0223/battery_state`
- `sensor_msgs/Range`: `/uavsim/ks0223/ultrasonic/front`
- `std_msgs/Float32MultiArray`: `/uavsim/ks0223/line_tracker/front_norm`
- `std_msgs/Float32MultiArray`: `/uavsim/ks0223/powertrain/estimate`
- `sensor_msgs/Image`: `/uavsim/ks0223/camera/front/image_raw`
- `sensor_msgs/CompressedImage`: `/uavsim/ks0223/camera/front/image_raw/compressed`
- `std_msgs/String`: `/uavsim/ks0223/state_json` (compat)
- `std_msgs/String`: `/uavsim/ks0223/telemetry_json` (compat)

Subscribe:
- `geometry_msgs/Twist`: `/uavsim/ks0223/cmd_vel` and `/cmd_vel`
- `std_msgs/Float32MultiArray`: `/uavsim/ks0223/cmd_drive`
- `std_msgs/String`: `/uavsim/ks0223/cmd_drive_json`

## Run
Native:
```bash
make ros-bridge
```

Mock (без ROS):
```bash
make ros-mock
```

## Docker ROS2 desktop flow
```bash
make sim-public
make demo-up
make demo-status
make demo-proof
```

noVNC URL: `http://127.0.0.1:6080`

## UI tools
- `rviz2`: визуализация odom/camera
- `rqt_image_view`: поток камеры
- `rqt_publisher`/`rqt_robot_steering`: ручное управление через `cmd_vel`

## Notes
- Требуются ROS2 пакеты: `rclpy`, `geometry_msgs`, `nav_msgs`, `std_msgs`, `sensor_msgs`.
- Для `sensor_msgs/Image` bridge использует `Pillow`.
- Bridge публикует данные только когда Unity в `Play` и API `step` отвечает.
- По умолчанию демо использует `.../camera/front/image_raw` (raw transport).
- `make demo-status` показывает preflight по image transport plugins в ROS контейнере.
- Если нужен `.../compressed` в `rqt_image_view`, установи plugins:
  - `make ros-install-image-plugins`
- При ошибке `Active vehicle is not initialized` bridge пытается сделать auto-reset и продолжает цикл.
