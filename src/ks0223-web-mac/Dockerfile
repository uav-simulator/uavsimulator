# syntax=docker/dockerfile:1.7

FROM node:22-alpine AS frontend-build
WORKDIR /src/frontend
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS backend-build
WORKDIR /src
COPY backend/backend.csproj backend/
RUN dotnet restore backend/backend.csproj
COPY backend/ backend/
COPY --from=frontend-build /src/frontend/dist/ backend/wwwroot/
RUN dotnet publish backend/backend.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=backend-build /app/publish/ ./
RUN mkdir -p /app/logs

ENV ASPNETCORE_URLS=http://0.0.0.0:5058
ENV SessionLogs__Directory=/app/logs

EXPOSE 5058
EXPOSE 5051/udp

HEALTHCHECK --interval=10s --timeout=3s --start-period=15s --retries=6 \
  CMD curl -fsS http://localhost:5058/api/health >/dev/null || exit 1

CMD ["dotnet", "backend.dll"]
