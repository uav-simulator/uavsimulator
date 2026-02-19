SHELL := /bin/bash
.DEFAULT_GOAL := help

PROJECT_ROOT := $(abspath .)
UNITY_VERSION ?= 6000.1.8f1
UNITY_PROJECT ?= $(PROJECT_ROOT)/src/UnityProject/uav-simulator
UNITY_BIN ?= /Applications/Unity/Hub/Editor/$(UNITY_VERSION)/Unity.app/Contents/MacOS/Unity

PYTHON ?= $(PROJECT_ROOT)/.venv/bin/python
PIP ?= $(PROJECT_ROOT)/.venv/bin/pip

UAVSIM_API_HOST ?= 127.0.0.1
UAVSIM_API_PORT ?= 8000
BASE_URL ?= http://127.0.0.1:$(UAVSIM_API_PORT)

UAVSIM_ROS_NAMESPACE ?= /uavsim/ks0223
UAVSIM_ROS_RATE_HZ ?= 15
UAVSIM_ROS_RESET_ON_START ?= 0
UAVSIM_VEHICLE_ID ?= vehicle.ks0223.v1
UAVSIM_TRACK_ID ?= track.basic_arena.v1
UAVSIM_CAMERA_TOPIC ?= $(UAVSIM_ROS_NAMESPACE)/camera/front/image_raw
UAVSIM_CMD_TOPIC ?= /cmd_vel
UAVSIM_CMD_LINEAR ?= 0.5
UAVSIM_CMD_ANGULAR ?= 0.0

ROS2_CONTAINER ?= uavsim-ros2-desktop
ROS2_IMAGE ?= tiryoh/ros2-desktop-vnc:humble
ROS2_HTTP_PORT ?= 6080
ROS2_VNC_PORT ?= 5901

ROS_BRIDGE_RESET_FLAG := $(if $(filter 1 true TRUE yes YES,$(UAVSIM_ROS_RESET_ON_START)),--reset-on-start,)

.PHONY: help quickstart venv sim sim-public sim-health sim-step sim-reset \
	demo-up demo-control demo-reset demo-status demo-down demo-restart ros-demo-reset \
	ros-mock ros-bridge ros-demo ros-up ros-down ros-shell ros-bridge-container \
	ros-ui-container ros-control-ui-container ros-topics ros-install-image-plugins \
	ros-install-control-ui ros-cmd-vel ros-stop clean-pyc

help:
	@echo "Main (daily):"
	@echo "  make sim-public  - start Unity (API accessible for Docker bridge)"
	@echo "  make demo-up     - start ROS desktop + bridge + RViz/rqt windows"
	@echo "  make demo-reset  - reset simulator to baseline robot/track"
	@echo "  make demo-status - quick health check (API + ROS topics + bridge log)"
	@echo "  make demo-control - demo-up + ROS steering UI (cmd_vel)"
	@echo "  make demo-down   - stop ROS desktop container"
	@echo "  make demo-restart - full ROS restart (down -> up)"
	@echo ""
	@echo "Setup:"
	@echo "  make venv        - create .venv and install python/requirements.txt"
	@echo "  make quickstart  - print minimal run order"
	@echo ""
	@echo "Advanced:"
	@echo "  make sim, sim-health, sim-step, sim-reset"
	@echo "  make ros-up, ros-down, ros-shell, ros-ui-container, ros-bridge-container, ros-topics"
	@echo "  make ros-install-image-plugins (for compressed image transport)"
	@echo "  make ros-install-control-ui (rqt_robot_steering)"
	@echo "  make ros-control-ui-container (open steering UI in ROS desktop)"
	@echo "  make ros-cmd-vel UAVSIM_CMD_LINEAR=0.4 UAVSIM_CMD_ANGULAR=0.2"
	@echo "  make ros-stop"
	@echo "  make ros-bridge, ros-mock, ros-demo"
	@echo "  make clean-pyc"

quickstart:
	@echo "1) make sim-public  (then press Play in Unity)"
	@echo "2) make demo-up"
	@echo "3) make demo-reset"
	@echo "4) Open http://127.0.0.1:$(ROS2_HTTP_PORT)"

venv:
	python3 -m venv .venv
	$(PIP) install -r python/requirements.txt

sim:
	@if [ ! -x "$(UNITY_BIN)" ]; then echo "Unity binary not found: $(UNITY_BIN)"; exit 1; fi
	UAVSIM_API_HOST=$(UAVSIM_API_HOST) UAVSIM_API_PORT=$(UAVSIM_API_PORT) "$(UNITY_BIN)" -projectPath "$(UNITY_PROJECT)"

sim-public:
	$(MAKE) sim UAVSIM_API_HOST=+

sim-health:
	curl -m 4 -sS "$(BASE_URL)/health"; echo

sim-step:
	curl -m 8 -sS -X POST "$(BASE_URL)/step" \
		-H 'Content-Type: application/json' \
		-d '{"throttle":0.0,"steer":0.0,"brake":0.0,"extensions":[],"timestamp":0,"timeBase":"sim_ms"}' | head -c 300; echo

sim-reset:
	curl -m 10 -sS -X POST "$(BASE_URL)/reset" \
		-H 'Content-Type: application/json' \
		-d '{"seed":1,"timeScale":1.0,"selectedTrackId":"$(UAVSIM_TRACK_ID)","selectedVehicleId":"$(UAVSIM_VEHICLE_ID)","trackParams":[],"vehicleParams":[],"flags":[]}' | head -c 500; echo

demo-up: ros-up ros-bridge-container ros-ui-container demo-reset
	@echo "ROS UI ready: http://127.0.0.1:$(ROS2_HTTP_PORT)"
	@echo "Camera topic default: $(UAVSIM_CAMERA_TOPIC)"

demo-control: demo-up ros-install-control-ui ros-control-ui-container
	@echo "Control UI ready: Robot Steering on topic $(UAVSIM_CMD_TOPIC)"

demo-reset: sim-reset
	@echo "Simulator reset done: vehicle=$(UAVSIM_VEHICLE_ID), track=$(UAVSIM_TRACK_ID)"

ros-demo-reset: demo-reset

demo-status:
	@echo "[sim] health:" && (curl -m 4 -sS "$(BASE_URL)/health" || true) && echo
	@echo "[sim] contract:" && (curl -m 4 -sS "$(BASE_URL)/contract" | head -c 140 || true) && echo
	@echo "[ros] container:" && (docker ps --filter "name=$(ROS2_CONTAINER)" --format 'table {{.Names}}\t{{.Status}}\t{{.Ports}}' || true)
	@echo "[ros] bridge process:" && (docker exec "$(ROS2_CONTAINER)" bash -lc 'ps -ef | grep "python3 /workspace/python/bridges/ros2_bridge.py" | grep -v grep || true' || true)
	@echo "[ros] topics (uavsim):" && (docker exec "$(ROS2_CONTAINER)" bash -lc 'su - ubuntu -c "source /opt/ros/humble/setup.bash; ros2 daemon stop >/dev/null 2>&1 || true; ros2 daemon start >/dev/null 2>&1 || true; ros2 topic list | grep \"^$(UAVSIM_ROS_NAMESPACE)\" | sort || true"' || true)
	@echo "[ros] bridge log tail:" && (docker exec "$(ROS2_CONTAINER)" bash -lc 'tail -n 8 /home/ubuntu/uavsim_bridge.log 2>/dev/null || tail -n 8 /tmp/uavsim_bridge.log 2>/dev/null || true' || true)

demo-down: ros-down

demo-restart:
	$(MAKE) demo-down
	$(MAKE) demo-up

ros-mock:
	$(PYTHON) python/bridges/ros2_bridge.py \
		--base-url "$(BASE_URL)" \
		--namespace "$(UAVSIM_ROS_NAMESPACE)" \
		--rate-hz "$(UAVSIM_ROS_RATE_HZ)" \
		$(ROS_BRIDGE_RESET_FLAG) \
		--mock-ros2 \
		--mock-steps 20

ros-bridge:
	$(PYTHON) python/bridges/ros2_bridge.py \
		--base-url "$(BASE_URL)" \
		--namespace "$(UAVSIM_ROS_NAMESPACE)" \
		--rate-hz "$(UAVSIM_ROS_RATE_HZ)" \
		$(ROS_BRIDGE_RESET_FLAG)

ros-demo:
	python/bridges/run_ros2_demo.sh

ros-up:
	docker rm -f "$(ROS2_CONTAINER)" >/dev/null 2>&1 || true
	docker run -d --name "$(ROS2_CONTAINER)" \
		--security-opt seccomp=unconfined \
		-p "$(ROS2_HTTP_PORT):80" \
		-p "$(ROS2_VNC_PORT):5900" \
		--shm-size=1g \
		-e RESOLUTION=1728x1117 \
		-v "$(PROJECT_ROOT):/workspace" \
		-w /workspace \
		"$(ROS2_IMAGE)"
	@echo "noVNC URL: http://127.0.0.1:$(ROS2_HTTP_PORT)"
	@echo "VNC URL:   vnc://127.0.0.1:$(ROS2_VNC_PORT)"

ros-down:
	docker rm -f "$(ROS2_CONTAINER)" >/dev/null 2>&1 || true

ros-shell:
	docker exec -it "$(ROS2_CONTAINER)" bash

ros-bridge-container:
	docker exec "$(ROS2_CONTAINER)" bash -lc 'source /opt/ros/humble/setup.bash && python3 -m pip install -q requests pillow >/dev/null 2>&1 || true && pkill -f "^python3 /workspace/python/bridges/ros2_bridge.py" || true && su - ubuntu -c "source /opt/ros/humble/setup.bash; ros2 daemon stop >/dev/null 2>&1 || true; ros2 daemon start >/dev/null 2>&1 || true; nohup python3 /workspace/python/bridges/ros2_bridge.py --base-url http://host.docker.internal:$(UAVSIM_API_PORT) --namespace \"$(UAVSIM_ROS_NAMESPACE)\" --rate-hz \"$(UAVSIM_ROS_RATE_HZ)\" $(ROS_BRIDGE_RESET_FLAG) >/home/ubuntu/uavsim_bridge.log 2>&1 < /dev/null &"'

ros-ui-container:
	docker exec "$(ROS2_CONTAINER)" bash -lc 'mkdir -p /dev/shm/runtime-ubuntu /dev/shm/roslog && chmod 700 /dev/shm/runtime-ubuntu && chown -R ubuntu:ubuntu /dev/shm/runtime-ubuntu /dev/shm/roslog && pkill -9 -u ubuntu -f "rviz2 -d /workspace/ros2/rviz/uavsim_demo.rviz" || true && pkill -9 -u ubuntu -f "rqt_image_view" || true && pkill -9 -u ubuntu -f "rqt_publisher" || true && pkill -9 -u ubuntu -f "rqt_robot_steering" || true && su - ubuntu -c "source /opt/ros/humble/setup.bash; ros2 daemon stop >/dev/null 2>&1 || true; ros2 daemon start >/dev/null 2>&1 || true; export DISPLAY=:1; export XDG_RUNTIME_DIR=/dev/shm/runtime-ubuntu; export ROS_LOG_DIR=/dev/shm/roslog; rviz2 -d /workspace/ros2/rviz/uavsim_demo.rviz >/home/ubuntu/rviz2.log 2>&1 & rqt --standalone rqt_image_view --args $(UAVSIM_CAMERA_TOPIC) >/home/ubuntu/rqt_image.log 2>&1 & rqt --standalone rqt_publisher >/home/ubuntu/rqt_pub.log 2>&1 &"'

ros-control-ui-container:
	docker exec "$(ROS2_CONTAINER)" bash -lc 'pkill -9 -u ubuntu -f "rqt --standalone rqt_robot_steering" || true && su - ubuntu -c "source /opt/ros/humble/setup.bash; export DISPLAY=:1; export XDG_RUNTIME_DIR=/dev/shm/runtime-ubuntu; export ROS_LOG_DIR=/dev/shm/roslog; nohup rqt --standalone rqt_robot_steering --force-discover >/home/ubuntu/rqt_steering.log 2>&1 < /dev/null &"'

ros-topics:
	docker exec "$(ROS2_CONTAINER)" bash -lc 'source /opt/ros/humble/setup.bash && ros2 topic list | sort'

ros-install-image-plugins:
	docker exec "$(ROS2_CONTAINER)" bash -lc 'apt-get update -y && apt-get install -y ros-humble-image-transport-plugins'

ros-install-control-ui:
	docker exec "$(ROS2_CONTAINER)" bash -lc 'apt-get update -y && apt-get install -y ros-humble-rqt-robot-steering'

ros-cmd-vel:
	docker exec "$(ROS2_CONTAINER)" bash -lc 'su - ubuntu -c "source /opt/ros/humble/setup.bash; ros2 topic pub --once $(UAVSIM_CMD_TOPIC) geometry_msgs/msg/Twist \"{linear: {x: $(UAVSIM_CMD_LINEAR), y: 0.0, z: 0.0}, angular: {x: 0.0, y: 0.0, z: $(UAVSIM_CMD_ANGULAR)}}\""'

ros-stop:
	$(MAKE) ros-cmd-vel UAVSIM_CMD_LINEAR=0.0 UAVSIM_CMD_ANGULAR=0.0

clean-pyc:
	find python -type f -name '*.pyc' -delete
	find python -type d -name '__pycache__' -empty -delete
