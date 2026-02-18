# ROS2 Bridge

`ros2_bridge.py` связывает HTTP API симулятора и ROS2 топики без изменения core DTO.

## Topics

- Publish `std_msgs/String`: `/uavsim/ks0223/state_json`
- Publish `std_msgs/String`: `/uavsim/ks0223/telemetry_json`
- Publish `sensor_msgs/CompressedImage`: `/uavsim/ks0223/camera/front/image_raw/compressed`
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

## Local Smoke (without ROS2)

```bash
python python/bridges/ros2_bridge.py \
  --base-url http://127.0.0.1:8000 \
  --mock-ros2 \
  --mock-steps 20
```

## Notes

- Нужна ROS2 Python среда (`rclpy`, `std_msgs`, `sensor_msgs`).
- Bridge является опциональным слоем и может быть выключен без влияния на Unity core.
