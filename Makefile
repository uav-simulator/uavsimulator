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
UAVSIM_ODOM_HZ_MIN ?= 10
UAVSIM_ROS_RESET_ON_START ?= 0
UAVSIM_VEHICLE_ID ?= vehicle.ks0223.arcade.blue.v1
UAVSIM_TRACK_ID ?= track.roadsystem_realistic.v2
UAVSIM_CAMERA_TOPIC ?= $(UAVSIM_ROS_NAMESPACE)/camera/front/image_raw
UAVSIM_CMD_TOPIC ?= /cmd_vel
UAVSIM_CMD_LINEAR ?= 0.5
UAVSIM_CMD_ANGULAR ?= 0.0

ROS2_CONTAINER ?= uavsim-ros2-desktop
ROS2_IMAGE ?= tiryoh/ros2-desktop-vnc:humble
ROS2_HTTP_PORT ?= 6080
ROS2_VNC_PORT ?= 5901

ROS_BRIDGE_RESET_FLAG := $(if $(filter 1 true TRUE yes YES,$(UAVSIM_ROS_RESET_ON_START)),--reset-on-start,)

.PHONY: help quickstart venv sim sim-public sim-health sim-step sim-reset sim-doctor sim-contract sim-install-cli sim-version sim-upgrade-check sim-upgrade sim-runtime-upgrade-check sim-runtime-upgrade sim-runtime-build sim-runtime-list sim-runtime-inspect sim-runtime-run sim-runtime-remove sim-runtime-favorite-show sim-runtime-favorite-set sim-server-start sim-server-start-runtime sim-server-status sim-server-stop sim-scenario-validate sim-scenario-print sim-scenario-reset \
	demo-up demo-control demo-reset demo-status demo-proof demo-proof-ci demo-down demo-restart ros-demo-reset \
	ros-mock ros-bridge ros-demo ros-up ros-down ros-shell ros-bridge-container \
	ros-ui-container ros-control-ui-container ros-topics ros-install-image-plugins \
	ros-install-control-ui ros-cmd-vel ros-stop clean-pyc

SCENARIO ?= $(PROJECT_ROOT)/configs/scenarios/demo.yaml
DEMO_SCENARIO ?= $(PROJECT_ROOT)/configs/scenarios/demo.yaml
RUNTIME_APP ?=
RUSIM_RELEASE_REPO ?= NMGorovenko/uav-simulator
RUSIM_RELEASE_TAG ?= latest
RUSIM_MANIFEST_URL ?=

help:
	@echo "Developer setup:"
	@echo "  make venv         - create local Python env"
	@echo "  make sim-public   - open Unity Editor with API accessible for Docker/ROS"
	@echo ""
	@echo "ROS2 / demo automation (developer-only):"
	@echo "  make demo-up      - ROS desktop + bridge + baseline reset"
	@echo "  make demo-control - demo-up + steering UI"
	@echo "  make demo-status  - health/topics/log preflight"
	@echo "  make demo-proof   - strict ROS/demo smoke"
	@echo "  make demo-proof-ci"
	@echo "  make demo-down"
	@echo "  make demo-restart"
	@echo "  make ros-up, ros-down, ros-shell, ros-topics"
	@echo ""
	@echo "Product CLI:"
	@echo "  ./rusim --help"
	@echo "  ./rusim server up --mode background --port 8000 --scenario configs/scenarios/demo.yaml"
	@echo "  ./rusim server down"
	@echo "  ./rusim runtime build"
	@echo "  ./rusim scenario validate configs/scenarios/demo.yaml"
	@echo "  ./rusim scenario reset configs/scenarios/demo.yaml --base-url $(BASE_URL)"
	@echo "  ./rusim step --base-url $(BASE_URL) --throttle 0.2 --steer 0.1"
	@echo ""
	@echo "Compatibility aliases (deprecated, kept for internal scripts):"
	@echo "  make sim-doctor, sim-contract, sim-reset, sim-runtime-*, sim-server-*, sim-scenario-*"
	@echo ""
	@echo "Other:"
	@echo "  make quickstart"
	@echo "  make sim         - open Unity Editor on loopback host"
	@echo "  make ros-ui-container, ros-bridge-container"
	@echo "  make ros-install-image-plugins (for compressed image transport)"
	@echo "  make ros-install-control-ui (rqt_robot_steering)"
	@echo "  make ros-control-ui-container"
	@echo "  make ros-cmd-vel UAVSIM_CMD_LINEAR=0.4 UAVSIM_CMD_ANGULAR=0.2"
	@echo "  make ros-stop, ros-bridge, ros-mock, ros-demo"
	@echo "  make clean-pyc"

quickstart:
	@echo "Product workflow:"
	@echo "1) ./rusim server up --mode background --port 8000 --scenario configs/scenarios/demo.yaml"
	@echo "2) ./rusim doctor --base-url $(BASE_URL)"
	@echo "3) ./rusim step --base-url $(BASE_URL) --throttle 0.2 --steer 0.1"
	@echo ""
	@echo "Editor + ROS demo workflow:"
	@echo "1) make sim-public  (then press Play in Unity)"
	@echo "2) make demo-up"
	@echo "3) make demo-status"
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

sim-doctor:
	PYTHONPATH=python $(PYTHON) -m sim_client.cli doctor --base-url "$(BASE_URL)"

sim-contract:
	PYTHONPATH=python $(PYTHON) -m sim_client.cli contract --base-url "$(BASE_URL)"

sim-install-cli:
	@"$(PROJECT_ROOT)/rusim" install --write-shell-config

sim-version:
	@"$(PROJECT_ROOT)/rusim" version

sim-upgrade-check:
	@"$(PROJECT_ROOT)/rusim" upgrade --repo "$(RUSIM_RELEASE_REPO)" --tag "$(RUSIM_RELEASE_TAG)" $(if $(RUSIM_MANIFEST_URL),--manifest-url "$(RUSIM_MANIFEST_URL)",) --check-only

sim-upgrade:
	@"$(PROJECT_ROOT)/rusim" upgrade --repo "$(RUSIM_RELEASE_REPO)" --tag "$(RUSIM_RELEASE_TAG)" $(if $(RUSIM_MANIFEST_URL),--manifest-url "$(RUSIM_MANIFEST_URL)",)

sim-runtime-upgrade-check:
	@"$(PROJECT_ROOT)/rusim" runtime upgrade --repo "$(RUSIM_RELEASE_REPO)" --tag "$(RUSIM_RELEASE_TAG)" $(if $(RUSIM_MANIFEST_URL),--manifest-url "$(RUSIM_MANIFEST_URL)",) --check-only

sim-runtime-upgrade:
	@"$(PROJECT_ROOT)/rusim" runtime upgrade --repo "$(RUSIM_RELEASE_REPO)" --tag "$(RUSIM_RELEASE_TAG)" $(if $(RUSIM_MANIFEST_URL),--manifest-url "$(RUSIM_MANIFEST_URL)",)

sim-runtime-build:
	PYTHONPATH=python $(PYTHON) -m sim_client.cli runtime build --project-path "$(UNITY_PROJECT)" $(if $(RUNTIME_APP),--output "$(RUNTIME_APP)",)

sim-runtime-list:
	@"$(PROJECT_ROOT)/rusim" runtime list

sim-runtime-inspect:
	@"$(PROJECT_ROOT)/rusim" runtime inspect "$(BUILD)"

sim-runtime-run:
	@"$(PROJECT_ROOT)/rusim" runtime run --build "$(BUILD)" --mode "$(MODE)" --port "$(UAVSIM_API_PORT)"

sim-runtime-remove:
	@"$(PROJECT_ROOT)/rusim" runtime remove "$(BUILD)"

sim-runtime-favorite-show:
	@"$(PROJECT_ROOT)/rusim" runtime favorite show

sim-runtime-favorite-set:
	@"$(PROJECT_ROOT)/rusim" runtime favorite set "$(BUILD)"

sim-server-start:
	PYTHONPATH=python $(PYTHON) -m sim_client.cli server start --project-path "$(UNITY_PROJECT)" --mode "$(MODE)" --port "$(UAVSIM_API_PORT)"

sim-server-start-runtime:
	PYTHONPATH=python $(PYTHON) -m sim_client.cli server start --runtime-app "$(RUNTIME_APP)" --mode "$(MODE)" --port "$(UAVSIM_API_PORT)"

sim-server-status:
	PYTHONPATH=python $(PYTHON) -m sim_client.cli server status --port "$(UAVSIM_API_PORT)"

sim-server-stop:
	PYTHONPATH=python $(PYTHON) -m sim_client.cli server stop

sim-scenario-validate:
	PYTHONPATH=python $(PYTHON) -m sim_client.cli scenario validate "$(SCENARIO)"

sim-scenario-print:
	PYTHONPATH=python $(PYTHON) -m sim_client.cli scenario print-reset "$(SCENARIO)"

sim-scenario-reset:
	PYTHONPATH=python $(PYTHON) -m sim_client.cli scenario reset "$(SCENARIO)" --base-url "$(BASE_URL)"

demo-up: ros-up ros-bridge-container ros-ui-container demo-reset
	@echo "ROS UI ready: http://127.0.0.1:$(ROS2_HTTP_PORT)"
	@echo "Camera topic default: $(UAVSIM_CAMERA_TOPIC)"

demo-control: demo-up ros-install-control-ui ros-control-ui-container
	@echo "Control UI ready: Robot Steering on topic $(UAVSIM_CMD_TOPIC)"

demo-reset:
	PYTHONPATH=python $(PYTHON) -m sim_client.cli scenario reset "$(DEMO_SCENARIO)" --base-url "$(BASE_URL)"
	@echo "Simulator reset done: scenario=$(DEMO_SCENARIO)"

ros-demo-reset: demo-reset

demo-status:
	@echo "[sim] health:" && (curl -m 4 -sS "$(BASE_URL)/health" || true) && echo
	@echo "[sim] contract:" && (curl -m 4 -sS "$(BASE_URL)/contract" | head -c 140 || true) && echo
	@echo "[ros] container:" && (docker ps --filter "name=$(ROS2_CONTAINER)" --format 'table {{.Names}}\t{{.Status}}\t{{.Ports}}' || true)
	@echo "[ros] bridge process:" && (docker exec "$(ROS2_CONTAINER)" bash -lc 'ps -ef | grep "python3 /workspace/python/bridges/ros2_bridge.py" | grep -v grep || true' || true)
	@echo "[ros] topics (uavsim):" && (docker exec "$(ROS2_CONTAINER)" bash -lc 'su - ubuntu -c "source /opt/ros/humble/setup.bash; ros2 daemon stop >/dev/null 2>&1 || true; ros2 daemon start >/dev/null 2>&1 || true; ros2 topic list | grep \"^$(UAVSIM_ROS_NAMESPACE)\" | sort || true"' || true)
	@echo "[ros] image transport plugins:" && (docker exec "$(ROS2_CONTAINER)" bash -lc 'dpkg -s ros-humble-image-transport-plugins >/dev/null 2>&1 && echo "installed (compressed topic supported)" || echo "not installed (optional; run: make ros-install-image-plugins for .../compressed)"' || true)
	@echo "[ros] bridge log tail:" && (docker exec "$(ROS2_CONTAINER)" bash -lc 'tail -n 8 /home/ubuntu/uavsim_bridge.log 2>/dev/null || tail -n 8 /tmp/uavsim_bridge.log 2>/dev/null || true' || true)

demo-proof:
	@set -euo pipefail; \
	echo "[1/6] simulator health"; \
	health_json="$$(curl -m 4 -fsS "$(BASE_URL)/health")"; \
	echo "$$health_json"; \
	echo "[2/6] simulator reset"; \
	reset_payload="$$(PYTHONPATH=python $(PYTHON) -m sim_client.cli scenario print-reset "$(DEMO_SCENARIO)")"; \
	reset_json="$$(curl -m 10 -fsS -X POST "$(BASE_URL)/reset" -H 'Content-Type: application/json' -d "$$reset_payload")"; \
	echo "$$reset_json" | head -c 220; \
	echo; \
	echo "[3/6] simulator step + frame payload check"; \
	step_json="$$(curl -m 10 -fsS -X POST "$(BASE_URL)/step" -H 'Content-Type: application/json' -d '{"throttle":0.0,"steer":0.0,"brake":0.0,"extensions":[],"timestamp":0,"timeBase":"sim_ms"}')"; \
	STEP_JSON="$$step_json" python3 -c 'import json, os; payload = json.loads(os.environ.get("STEP_JSON", "{}")); state = payload.get("state") if isinstance(payload, dict) else None; frame = payload.get("frame") if isinstance(payload, dict) else None; frame_data = frame.get("dataBase64") if isinstance(frame, dict) else None; assert isinstance(state, dict), "StepResult.state is missing."; assert isinstance(frame, dict), "StepResult.frame is missing."; assert isinstance(frame_data, str) and len(frame_data) >= 32, "StepResult.frame.dataBase64 is empty."; print("step ok: frame_base64_len={} speed={}".format(len(frame_data), state.get("speed", 0.0)))'; \
	echo "[4/6] check ROS container"; \
	if ! docker ps --filter "name=$(ROS2_CONTAINER)" --format '{{.Names}}' | grep -qx "$(ROS2_CONTAINER)"; then \
		echo "ROS container '$(ROS2_CONTAINER)' is not running. Start with: make demo-up" >&2; \
		exit 1; \
	fi; \
	echo "[5/6] ROS camera one-shot on $(UAVSIM_CAMERA_TOPIC)"; \
	docker exec -u ubuntu -e UAVSIM_CAMERA_TOPIC="$(UAVSIM_CAMERA_TOPIC)" "$(ROS2_CONTAINER)" bash -lc 'source /opt/ros/humble/setup.bash; ros2 daemon stop >/dev/null 2>&1 || true; ros2 daemon start >/dev/null 2>&1 || true; i=0; while [ $$i -lt 24 ]; do ros2 topic info "$$UAVSIM_CAMERA_TOPIC" >/dev/null 2>&1 && break; i=$$((i+1)); sleep 0.5; done; timeout 12s ros2 topic echo --once --qos-reliability best_effort --field header "$$UAVSIM_CAMERA_TOPIC" > /home/ubuntu/uavsim_camera_once.log 2>&1'; \
	docker exec "$(ROS2_CONTAINER)" bash -lc 'tail -n 2 /home/ubuntu/uavsim_camera_once.log'; \
	echo "[6/6] ROS odom hz check (min $(UAVSIM_ODOM_HZ_MIN) Hz)"; \
	docker exec -u ubuntu -e UAVSIM_HZ_TOPIC="$(UAVSIM_ROS_NAMESPACE)/odom" -e UAVSIM_ODOM_HZ_MIN="$(UAVSIM_ODOM_HZ_MIN)" "$(ROS2_CONTAINER)" bash -lc 'source /opt/ros/humble/setup.bash; timeout 12s ros2 topic hz "$$UAVSIM_HZ_TOPIC" > /home/ubuntu/uavsim_odom_hz.log 2>&1 || true; grep "average rate" /home/ubuntu/uavsim_odom_hz.log | tail -n 1 >/home/ubuntu/uavsim_odom_hz_last.log; test -s /home/ubuntu/uavsim_odom_hz_last.log'; avg_line="$$(docker exec "$(ROS2_CONTAINER)" bash -lc 'tail -n 1 /home/ubuntu/uavsim_odom_hz_last.log')"; if [ -z "$$avg_line" ]; then echo "No average rate in odom hz output." >&2; exit 1; fi; UAVSIM_ODOM_HZ_LINE="$$avg_line" UAVSIM_ODOM_HZ_MIN="$(UAVSIM_ODOM_HZ_MIN)" python3 -c 'import os, re, sys; line = os.environ.get("UAVSIM_ODOM_HZ_LINE", ""); min_hz = float(os.environ.get("UAVSIM_ODOM_HZ_MIN", "0")); m = re.search(r"average rate:\s*([0-9.]+)", line); assert m, f"Cannot parse average rate from: {line}"; avg = float(m.group(1)); assert avg >= min_hz, f"odom hz too low: {avg:.2f} < {min_hz:.2f}"; print(f"odom hz ok: {avg:.2f} >= {min_hz:.2f}")'; \
	docker exec "$(ROS2_CONTAINER)" bash -lc 'cat /home/ubuntu/uavsim_odom_hz_last.log'; \
	echo "demo-proof passed"

demo-proof-ci:
	@set -euo pipefail; \
	if ! curl -m 3 -fsS "$(BASE_URL)/health" >/dev/null 2>&1; then \
		echo "demo-proof (CI): skip, Unity API not reachable (Play Mode not running)"; \
		exit 0; \
	fi; \
	if ! command -v docker >/dev/null 2>&1; then \
		echo "demo-proof (CI): skip, docker not available"; \
		exit 0; \
	fi; \
	if ! docker ps --filter "name=$(ROS2_CONTAINER)" --format '{{.Names}}' | grep -qx "$(ROS2_CONTAINER)"; then \
		echo "demo-proof (CI): skip, ROS container not running"; \
		exit 0; \
	fi; \
	$(MAKE) demo-proof

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
