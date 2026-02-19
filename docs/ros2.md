## Purpose
Зафиксировать как используется ROS2 в проекте, какие топики доступны и как запускать ROS-контур отдельно от Unity.

## Assumptions
- ROS2 слой опционален и не обязателен для core симулятора.
- Unity остаётся источником физики/состояния.

## Decisions
- Bridge реализован внешним Python процессом: `python/bridges/ros2_bridge.py`.
- Core DTO (`SimulationConfig`, `ControlCommand`, `StepResult`) не меняются для ROS.
- Поддерживаются typed topics + compat JSON topics.

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
- `geometry_msgs/Twist`: `/uavsim/ks0223/cmd_vel` и `/cmd_vel`
- `std_msgs/Float32MultiArray`: `/uavsim/ks0223/cmd_drive`
- `std_msgs/String`: `/uavsim/ks0223/cmd_drive_json`

## Раздельный запуск Simulator и ROS
1. Старт симулятора:
   - `make sim-public`
2. В Unity открыть `PresentationTrack` и нажать `Play`.
3. Единый запуск ROS demo-контура:
   - `make demo-up`
4. Проверка состояния:
   - `make demo-status`
5. При необходимости ручной reset:
   - `make demo-reset`

## Native запуск (без Docker)
- Bridge: `make ros-bridge`
- Demo script: `make ros-demo`

## UI инструменты
- RViz: визуализация odometry/camera и базовой геометрии.
- `rqt_image_view`: просмотр камеры (по умолчанию открывается с `.../camera/front/image_raw`).
- `rqt_publisher` или `rqt_robot_steering`: ручная отправка `cmd_vel`.

## Управление машинкой через ROS2
1. Поднять сим и ROS-контур:
   - `make sim-public` (в Unity нажать Play)
   - `make demo-up`
2. Открыть UI-руль:
   - `make ros-install-control-ui`
   - `make ros-control-ui-container`
3. В окне `Robot Steering` выбрать topic `/cmd_vel`.
4. Подавать линейную/угловую скорость из UI.
5. Экстренный стоп:
   - `make ros-stop`

CLI-вариант управления без UI:
- Вперед: `make ros-cmd-vel UAVSIM_CMD_LINEAR=0.45 UAVSIM_CMD_ANGULAR=0.0`
- Поворот: `make ros-cmd-vel UAVSIM_CMD_LINEAR=0.25 UAVSIM_CMD_ANGULAR=0.35`
- Стоп: `make ros-stop`

## Troubleshooting: `No image` в RViz/rqt
- Убедиться, что Unity реально в `Play` и API отвечает:
  - `make sim-health`
- Перезапустить весь ROS демо-контур:
  - `make demo-restart`
- Проверить, что поток реально идет:
  - `docker exec uavsim-ros2-desktop bash -lc 'su - ubuntu -c "source /opt/ros/humble/setup.bash; ros2 topic hz /uavsim/ks0223/camera/front/image_raw"'`
- Если в UI пусто после долгой сессии, перезапустить только UI:
  - `make ros-ui-container`

Примечание:
- Камера публикуется в `sensor_data` QoS (`BEST_EFFORT`) для совместимости с RViz/rqt.
- В RViz используется `Topic` (а не `Image Topic`) для `Image` display, иначе подписка на камеру не создается.

## Ограничения
- Bridge публикует данные только когда Unity в `Play` и `/step` отвечает.
- Для `sensor_msgs/Image` в bridge нужен `Pillow`.
- Для Docker ROS2 на macOS нужен доступный API host у Unity (`make sim-public`).
- Для просмотра `.../compressed` в `rqt_image_view` нужны image transport plugins:
  - `make ros-install-image-plugins`

## Next steps
- Добавить watchdog и аварийный stop в bridge.
- Вынести расширенную telemetry в custom ROS2 msg package.
