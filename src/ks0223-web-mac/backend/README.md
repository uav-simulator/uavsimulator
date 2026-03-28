# KS0223 Web Backend

ASP.NET Core backend для управления real robot и Unity runtime в session-aware режиме.

## Запуск

```bash
cd src/ks0223-web-mac/backend
dotnet run
```

По умолчанию: `http://localhost:5058`.

## Build

```bash
dotnet build
```

## API v2 (breaking)

Для runtime endpoint-ов обязательны `clientId` и `runtimeMode`.

- `runtimeMode`:
  - `real-robot`
  - `unity-sim`

Примеры:

```bash
# status
curl "http://localhost:5058/api/status?clientId=tab-a&runtimeMode=real-robot"

# connect real robot
curl -X POST "http://localhost:5058/api/connection/connect" \
  -H "content-type: application/json" \
  -d '{"clientId":"tab-a","runtimeMode":"real-robot","host":"192.168.1.121","port":5051}'

# connect unity
curl -X POST "http://localhost:5058/api/connection/connect" \
  -H "content-type: application/json" \
  -d '{"clientId":"tab-a","runtimeMode":"unity-sim","host":"127.0.0.1","port":8000}'

# command
curl -X POST "http://localhost:5058/api/command" \
  -H "content-type: application/json" \
  -d '{"clientId":"tab-a","runtimeMode":"unity-sim","command":"DirForward","agentId":"car-a"}'

# model upload
curl -X POST "http://localhost:5058/api/models/upload" \
  -F "file=@ab_corridor_policy_v1.onnx" \
  -F "name=ab-corridor-policy-v1" \
  -F "version=1.0.0" \
  -F "source=python-rl-api"

# activate model
curl -X POST "http://localhost:5058/api/models/activate" \
  -H "content-type: application/json" \
  -d '{"modelId":"model-20260328-xxxx"}'

# start autopilot
curl -X POST "http://localhost:5058/api/autopilot/start" \
  -H "content-type: application/json" \
  -d '{"clientId":"tab-a","runtimeMode":"unity-sim","agentId":"car-a","loopIntervalMs":140}'

# unity world-level selection (track/vehicle/agents reset)
curl -X POST "http://localhost:5058/api/unity/runtime-selection" \
  -H "content-type: application/json" \
  -d '{"clientId":"tab-a","runtimeMode":"unity-sim","trackId":"track.basic_arena.v1","vehicleId":"vehicle.arcade.blue.v1","cameraMode":"spectator","agents":[{"agentId":"car-a","vehicleId":"vehicle.arcade.blue.v1","isPrimary":true}],"applyImmediately":true}'

# unity client-level selection (без reset мира)
curl -X POST "http://localhost:5058/api/unity/client-selection" \
  -H "content-type: application/json" \
  -d '{"clientId":"tab-a","runtimeMode":"unity-sim","controlAgentId":"car-a","cameraAgentId":"car-a"}'
```

## SignalR

- Hub: `/hub/telemetry`
- Клиент обязан вызвать `BindClient(clientId)` после подключения.
- `real-robot`: push идёт в group `client:{clientId}`.
- `unity-sim`: push идёт в group shared world endpoint-а (`unity-world:<host>:<port>`).

## Unity shared world model

- Ключ world: `(targetHost,targetPort)`.
- `connect` к уже активному world делает attach клиента без reset.
- `disconnect` отцепляет только этого клиента; world останавливается, когда attached clients = 0.
- `runtime-selection` меняет world config (track/vehicle/agents).
- `client-selection` меняет только выбор agent для этой вкладки (control/camera), без reset.

## Model lifecycle API (v1)

- `POST /api/models/upload` — multipart upload (`file`, optional `name/version/source/metadata/metrics`).
- `GET /api/models` — список моделей.
- `GET /api/models/active` — активная модель.
- `POST /api/models/activate` — сделать модель активной.
- `POST /api/autopilot/start` — запустить inference loop.
- `POST /api/autopilot/stop` — остановить loop.
- `GET /api/autopilot/status` — текущий статус автопилота.

Manual safety:
- Любая ручная команда через `/api/command` автоматически останавливает автопилот для данного `clientId/runtimeMode`.

## Real robot camera bootstrap

- На KS0223 `FramesSend.py` стартует отправку UDP-кадров только после первого ICMP echo на `wlan0`.
- Backend теперь автоматически делает bootstrap ping при `POST /api/connection/connect` в `real-robot`.
- Поэтому после reboot Pi обычно достаточно просто переподключиться в WebUI (ручной `ping` не нужен).
