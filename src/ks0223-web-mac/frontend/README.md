# KS0223 Web Frontend

Frontend SPA для `ks0223-web-mac` (React + TypeScript + Vite + MUI).

## Запуск

```bash
cd src/ks0223-web-mac/frontend
npm install
npm run dev
```

По умолчанию открывается на `http://localhost:5173`.

## Build

```bash
npm run build
```

## Runtime/API модель (v2)

- Все runtime-вызовы идут в backend с обязательными `clientId` и `runtimeMode`.
- `clientId` генерируется на вкладку (`sessionStorage`) и используется для session-aware изоляции.
- После старта SignalR frontend вызывает `BindClient(clientId)` на `/hub/telemetry`.
- Статус/телеметрия приходят адресно только для этой вкладки/клиента.

## Поведение UI

- Host/port поля работают как пользовательский draft и не перетираются из `/api/status`.
- Кэш host/port хранится в `localStorage` отдельно по runtime mode:
  - `real-robot`
  - `unity-sim`
- В Unity-режиме поддержан zero-agent флоу:
  - начальное состояние может быть без машинок (`spectator`);
  - первая машинка добавляется вручную в popup;
  - выбор `Camera agent` автоматически синхронизирует `Control agent`.
- Разделение Unity API:
  - `/api/unity/runtime-selection` — world-level (track/vehicle/agents/reset);
  - `/api/unity/client-selection` — client-level (control/camera agent без reset).
- Вкладка `Model Control`:
  - загрузка ONNX в backend registry;
  - активация версии модели;
  - запуск/остановка autopilot loop;
  - просмотр состояния (`manual/autopilot`, шаги, последняя команда, last error).
