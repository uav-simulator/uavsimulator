# Верификация инкремента (2026-03-29)

## Что проверено
1. Сборка backend:
   - Команда: `dotnet build src/ks0223-web-mac/backend/backend.csproj`
   - Результат: успешно.
2. Сборка frontend:
   - Команда: `npm run build` в `src/ks0223-web-mac/frontend`
   - Результат: успешно.
3. Валидация сценария `A->B`:
   - Команда: `./rusim scenario validate configs/scenarios/ab-corridor-v1.yaml`
   - Результат: `valid: true`.
4. Базовый API smoke (backend локально):
   - `GET /api/models` -> `[]`.
   - `GET /api/models/active` -> `{"error":"Active model is not selected"}`.
   - `GET /api/autopilot/status` -> `mode=manual`, `isRunning=false`.
   - `POST /api/autopilot/start` (без активной модели) -> `{"error":"Active model is not selected"}`.
5. Product e2e smoke `train -> install -> activate -> run` на `unity-sim`:
   - `./rusim server up --mode background --port 8000 --scenario configs/scenarios/ab-corridor-v1.yaml --wait-seconds 120` -> Unity runtime поднят, `/health = ok`.
   - `POST /api/connection/connect` для `clientId=smoke-e2e`, `runtimeMode=unity-sim` -> `tcpConnected=true`.
   - `POST /api/unity/runtime-selection` + `POST /api/unity/client-selection` -> выбран `track.basic_arena.v1`, `vehicle.prometeo.sport.v1`, `agentId=ego`.
   - `GET /api/sensors/latest` -> backend получает unified telemetry от симулятора.
   - `./rusim model install python/training/artifacts/ab_corridor_policy_v1/ab_corridor_policy_v1.onnx --activate` -> артефакт загружен и активирован.
   - `POST /api/autopilot/start` -> запуск без ошибки.
   - Через ~3 секунды `GET /api/autopilot/status?clientId=smoke-e2e&runtimeMode=unity-sim` -> `stepsTotal=22`, `commandsSent=22`, `lastError=null`, `mode=autopilot`.
   - `POST /api/autopilot/stop` -> `mode=manual`, автопилот остановлен корректно.

## Что исправлено перед e2e
- В `configs/scenarios/ab-corridor-v1.yaml` был устаревший `vehicleId=vehicle.ks0223.v1`.
- Для runnable сценария заменен на канонический `vehicle.prometeo.sport.v1`, иначе `rusim server up` падал с `Unknown vehicle id`.

## Скриншоты
- UI smoke: ![Model Control E2E](./evidence/model-control-e2e-2026-03-29.png)

## Артефакты smoke
- `docs/report/prediploma-practice/evidence/smoke_models.json`
- `docs/report/prediploma-practice/evidence/smoke_active.json`
- `docs/report/prediploma-practice/evidence/smoke_autopilot_status.json`
- `docs/report/prediploma-practice/evidence/smoke_autopilot_start.json`
- `docs/report/prediploma-practice/evidence/smoke_e2e_connect.json`
- `docs/report/prediploma-practice/evidence/smoke_e2e_runtime_selection.json`
- `docs/report/prediploma-practice/evidence/smoke_e2e_sensors_latest.json`
- `docs/report/prediploma-practice/evidence/smoke_e2e_model_active.json`
- `docs/report/prediploma-practice/evidence/smoke_e2e_autopilot_start.json`
- `docs/report/prediploma-practice/evidence/smoke_e2e_autopilot_running.json`
- `docs/report/prediploma-practice/evidence/smoke_e2e_autopilot_stopped.json`

## Ограничения текущего шага
- Реальные прогоны на машинке не проводились (нужен отдельный спринт/выезд).
- KPI по достижению точки `B` в серии эпизодов еще не собраны: в этом шаге подтвержден только рабочий контур `install -> activate -> start/stop` на реальном `unity-sim` runtime.

## Что исправлено дополнительно
- Для генерации отчетов `.docx` добавлены:
  - удаление неиспользуемых embedded media,
  - оптимизация крупных встроенных изображений,
  - проверка лимитов размера (`target <= 25 MB`, `hard <= 50 MB`).
- Фактический размер файлов после пересборки:
  - `НМГоровенко_ПреддипломнаяПрактика_ПромежуточныйОтчет2.docx`: ~4.94 MB.
  - `НМГоровенко_ПреддипломнаяПрактика_ФинальныйОтчет.docx`: ~4.94 MB.
