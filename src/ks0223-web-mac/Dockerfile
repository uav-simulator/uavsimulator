# syntax=docker/dockerfile:1.7
#
# Build context = repo root so that COPY can pull both
#   src/ks0223-web-mac/{backend,frontend}/  (the Web UI)
#   python/sim_client/                       (rusim CLI used by /api/scenarios/load)
# and so the scenarios directory can be mounted into /app/configs/scenarios at run time.
#
# Build via the project Makefile: `make -C src/ks0223-web-mac docker-build`.

FROM node:22-alpine AS frontend-build
WORKDIR /src/frontend
COPY src/ks0223-web-mac/frontend/package*.json ./
RUN npm ci
COPY src/ks0223-web-mac/frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS backend-build
WORKDIR /src
COPY src/ks0223-web-mac/backend/backend.csproj backend/
RUN dotnet restore backend/backend.csproj
COPY src/ks0223-web-mac/backend/ backend/
COPY --from=frontend-build /src/frontend/dist/ backend/wwwroot/
RUN dotnet restore backend/backend.csproj && dotnet publish backend/backend.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl python3 python3-pip python3-venv \
    && rm -rf /var/lib/apt/lists/*

COPY --from=backend-build /app/publish/ ./

# Install the project's Python rusim CLI into a venv so /api/scenarios/load
# can shell out to `rusim scenario print-reset <yaml>` before posting to Unity.
COPY python/sim_client /app/sim_client/sim_client
COPY python/pyproject.toml /app/sim_client/pyproject.toml
COPY python/README.md /app/sim_client/README.md
RUN python3 -m venv /opt/rusim-venv \
    && /opt/rusim-venv/bin/pip install --upgrade pip \
    && /opt/rusim-venv/bin/pip install pyyaml requests \
    && /opt/rusim-venv/bin/pip install -e /app/sim_client --no-deps \
    && ln -s /opt/rusim-venv/bin/rusim /usr/local/bin/rusim

RUN mkdir -p /app/logs /app/configs/scenarios

ENV ASPNETCORE_URLS=http://0.0.0.0:5058
ENV SessionLogs__Directory=/app/logs
ENV RUSIM_PATH=/usr/local/bin/rusim
# Unity Editor runs on the host; container reaches it through Docker's host
# bridge alias. Override via `-e RUSIM_BASE_URL=...` for non-default hosts.
ENV RUSIM_BASE_URL=http://host.docker.internal:8000

EXPOSE 5058
EXPOSE 5051/udp

HEALTHCHECK --interval=10s --timeout=3s --start-period=15s --retries=6 \
  CMD curl -fsS "http://localhost:5058/api/health?clientId=healthcheck&runtimeMode=real-robot" >/dev/null || exit 1

CMD ["dotnet", "backend.dll"]
