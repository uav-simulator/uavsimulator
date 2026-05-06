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

## Multi-agent bridge

`ros2_bridge_multi.py` запускает один ROS2 node, который держит **по namespace на каждый агент** (`{prefix}/{agent_id}/...`). Single-agent bridge (`ros2_bridge.py`) остаётся без изменений и продолжает использоваться в `make demo-*`.

### Topics (per agent)

Под каждый агент публикуется тот же набор, что у single-agent версии, но в namespace `{prefix}/{agent_id}` (по умолчанию `/uavsim/{agent_id}`):

- `nav_msgs/Odometry`: `{ns}/odom`
- `std_msgs/Float32`: `{ns}/speedometer/mps`
- `sensor_msgs/BatteryState`: `{ns}/battery_state`
- `sensor_msgs/Range`: `{ns}/ultrasonic/front`
- `std_msgs/Float32MultiArray`: `{ns}/line_tracker/front_norm`, `{ns}/powertrain/estimate`
- `sensor_msgs/Image` + `sensor_msgs/CompressedImage`: `{ns}/camera/front/image_raw[/compressed]`
- `std_msgs/String`: `{ns}/state_json`, `{ns}/telemetry_json`

Subscribe:
- `geometry_msgs/Twist`: `{ns}/cmd_vel`
- `std_msgs/Float32MultiArray`: `{ns}/cmd_drive`
- `std_msgs/String`: `{ns}/cmd_drive_json`

Глобального `/cmd_vel` в multi-agent режиме **нет** — управление всегда явное per-agent, иначе невозможно различить, кому идёт команда.

### Step routing

На каждый tick таймера bridge делает один Unity `/step` на агент с `targetAgentId` равным id этого агента. Это совпадает с тем, как `python/training/multi_agent_*_env.py` гоняют N машин через одну Unity-инстанцию.

### CLI

```bash
python python/bridges/ros2_bridge_multi.py \
  --base-url http://127.0.0.1:8000 \
  --agents ego,npc-01,npc-02,npc-03 \
  --rate-hz 30 \
  --reset-on-start \
  --topic-prefix /uavsim
```

`--agents` принимает:
- список через запятую/пробелы (`ego,npc-01,npc-02`);
- `auto` или пустую строку — попытаться вытащить список из ответа Unity `/step` (`agents[].agentId`); если Unity не отдаёт `agents`, fallback — `["ego"]`.

Для smoke-теста без ROS2 окружения:

```bash
python python/bridges/ros2_bridge_multi.py --mock-ros2 --agents ego,npc01 --mock-steps 5
```

### Тесты

```bash
cd python && pytest tests/bridges/test_ros2_bridge_multi.py -v
```

Тесты мокают `rclpy`/`SimClient`, проверяют:
- парсинг `--agents` (список / `auto` / sanitization);
- авто-detect агентов из `/step`-ответа (с fallback-ами);
- что bridge создаёт ровно один namespace на агент;
- что `step_all()` делает один `/step` per agent с правильным `targetAgentId`;
- что `cmd_vel` per agent корректно конвертится в left/right PWM.
