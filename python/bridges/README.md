# ROS2 Bridge

`ros2_bridge.py` связывает HTTP API симулятора и ROS2 топики без изменения core DTO.

## Topics

- Publish `nav_msgs/Odometry`: `/uavsim/ks0223/odom`
- Publish `std_msgs/Float32`: `/uavsim/ks0223/speedometer/mps`
- Publish `sensor_msgs/BatteryState`: `/uavsim/ks0223/battery_state`
- Publish `sensor_msgs/Range`: `/uavsim/ks0223/ultrasonic/front`
- Publish `std_msgs/Float32MultiArray`: `/uavsim/ks0223/line_tracker/front_norm`
- Publish `std_msgs/Float32MultiArray`: `/uavsim/ks0223/powertrain/estimate`
- Publish `sensor_msgs/Image`: `/uavsim/ks0223/camera/front/image_raw`
- Publish `sensor_msgs/CompressedImage`: `/uavsim/ks0223/camera/front/image_raw/compressed`
- Publish `std_msgs/String`: `/uavsim/ks0223/state_json` (compat)
- Publish `std_msgs/String`: `/uavsim/ks0223/telemetry_json` (compat)
- Subscribe `geometry_msgs/Twist`: `/uavsim/ks0223/cmd_vel` and `/cmd_vel`
- Subscribe `std_msgs/Float32MultiArray`: `/uavsim/ks0223/cmd_drive` (`[left_pwm, right_pwm, brake?]`)
- Subscribe `std_msgs/String`: `/uavsim/ks0223/cmd_drive_json`

## Run

```bash
python python/bridges/ros2_bridge.py \
  --base-url http://127.0.0.1:8000 \
  --namespace /uavsim/ks0223 \
  --rate-hz 15 \
  --reset-on-start
```

## Demo (one command)

```bash
python/bridges/run_ros2_demo.sh
```

Script starts bridge and opens `rviz2` with `ros2/rviz/uavsim_demo.rviz`.

## rqt control window

```bash
ros2 run rqt_robot_steering rqt_robot_steering --ros-args -r /cmd_vel:=/uavsim/ks0223/cmd_vel
```

## Local Smoke (without ROS2)

```bash
python python/bridges/ros2_bridge.py \
  --base-url http://127.0.0.1:8000 \
  --mock-ros2 \
  --mock-steps 20
```

## Notes

- Нужна ROS2 Python среда (`rclpy`, `geometry_msgs`, `nav_msgs`, `std_msgs`, `sensor_msgs`).
- Для публикации `sensor_msgs/Image` нужен `Pillow`; без него продолжается публикация `CompressedImage`.
- Bridge является опциональным слоем и может быть выключен без влияния на Unity core.
