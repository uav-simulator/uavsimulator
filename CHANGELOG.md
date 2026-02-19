# Changelog

## Purpose
Фиксировать изменения по версиям (что добавлено/изменено/исправлено) без привязки к экспериментальным результатам.

## Assumptions
- Версионирование будет уточнено после стабилизации MVP.

## Decisions
- Формат секций: Added / Changed / Fixed / Removed.

## Next steps
- Начать заполнять с первого релевантного релиза MVP.

## [Unreleased]
### Added
- Root `Makefile` with separated operator flows for simulator and ROS2 tooling.
- Extended architecture documentation with component and sequence diagrams.
- Simplified `make demo-*` workflow (`demo-up`, `demo-reset`, `demo-status`, `demo-down`, `demo-restart`) for one-pass ROS demo operations.
- Added ROS2 control helpers in `Makefile`: `ros-install-control-ui`, `ros-control-ui-container`, `ros-cmd-vel`, `ros-stop`.
### Changed
- `HttpJsonApiHost` now supports `UAVSIM_API_HOST` and `UAVSIM_API_PORT` environment overrides.
- Main run/readme documentation updated to match actual runtime flow and ROS2 integration.
- `ros-ui-container` now opens `rqt_image_view` with default camera topic and restarts ROS UI windows more predictably.
- ROS container flows now restart ROS2 daemon for `ubuntu` user to avoid stale discovery state in long sessions.
- Camera publishers in ROS2 bridge now use sensor-data QoS (`BEST_EFFORT`) for RViz/rqt compatibility.
### Fixed
- Presentation auto-drive is disabled by default (`autoDrive = false`) to prevent unexpected robot motion on Play start.
- ROS2 bridge now auto-recovers from `Active vehicle is not initialized` by retrying reset in runtime loop.
- ROS demo now defaults to raw image topic to avoid missing `compressed_sub` plugin errors in `rqt_image_view`.
- Fixed broken RGB decode path in `ros2_bridge.py` that prevented `sensor_msgs/Image` publishing.
- Fixed RViz camera display config (`Topic` key) so RViz actually subscribes to `/uavsim/ks0223/camera/front/image_raw`.
### Removed
