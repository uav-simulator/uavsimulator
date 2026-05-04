IMAGE ?= ks0223-web-mac:latest
CONTAINER ?= ks0223-web-mac
PORT ?= 5058
UDP_PORT ?= 5051
LOGS_DIR ?= $(CURDIR)/logs

# Repo root — used to mount configs/scenarios into the container so the
# WebUI scenario picker sees the same YAMLs the host edits.
REPO_ROOT ?= $(CURDIR)/../..
SCENARIOS_DIR ?= $(REPO_ROOT)/configs/scenarios

.PHONY: docker-build docker-rebuild docker-run docker-stop docker-update docker-logs docker-health docker-shell docker-pull-base docker-wait

docker-pull-base:
	docker pull node:22-alpine
	docker pull mcr.microsoft.com/dotnet/sdk:8.0
	docker pull mcr.microsoft.com/dotnet/aspnet:8.0

# Build context = repo root because the Dockerfile copies python/sim_client
# (rusim CLI source). The Dockerfile lives at src/ks0223-web-mac/Dockerfile.
docker-build:
	docker build -t $(IMAGE) -f $(CURDIR)/Dockerfile $(REPO_ROOT)

docker-rebuild:
	docker build --pull --no-cache -t $(IMAGE) -f $(CURDIR)/Dockerfile $(REPO_ROOT)

docker-stop:
	-docker rm -f $(CONTAINER)

docker-run: docker-stop
	mkdir -p $(LOGS_DIR)
	docker run -d --name $(CONTAINER) \
		-p $(PORT):5058 -p $(UDP_PORT):5051/udp \
		-v $(LOGS_DIR):/app/logs \
		-v $(SCENARIOS_DIR):/app/configs/scenarios:ro \
		--add-host host.docker.internal:host-gateway \
		$(IMAGE)

docker-wait:
	@echo "Waiting for healthcheck on http://localhost:$(PORT)/api/health?clientId=healthcheck&runtimeMode=real-robot ..."
	@for i in $$(seq 1 30); do \
		if curl -fsS "http://localhost:$(PORT)/api/health?clientId=healthcheck&runtimeMode=real-robot" >/dev/null 2>&1; then \
			echo "Container is ready"; \
			exit 0; \
		fi; \
		sleep 1; \
	done; \
	echo "Container did not become ready in time"; \
	exit 1

docker-update: docker-build docker-run docker-wait

docker-logs:
	docker logs -f $(CONTAINER)

docker-health:
	curl -fsS "http://localhost:$(PORT)/api/health?clientId=healthcheck&runtimeMode=real-robot" | jq .

docker-shell:
	docker exec -it $(CONTAINER) /bin/sh
