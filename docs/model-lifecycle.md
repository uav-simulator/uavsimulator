# Model Lifecycle

Эта страница описывает базовый путь работы с моделью управления в `uav-simulator`.

## Цепочка работы

1. Обучить или собрать модель во внешнем Python-контуре.
2. Получить артефакт:
   - `.onnx`
   - `metadata.json`
   - `metrics.json`
3. Установить модель через `rusim` или backend API.
4. Выбрать нужную `name/version` и привязать её к целевой паре `clientId + runtimeMode + agentId?`.
5. Запустить автопилот.
6. Проверить результат в `unity-sim`.
7. Подготовить перенос на `real-robot`.

## Формат артефакта

Минимальный набор:
- ONNX-модель;
- metadata c описанием наблюдений и действий;
- metrics c базовой сводкой по обучению или baseline.

## Установка через CLI

```bash
rusim model install python/training/artifacts/cardboard-corridor-policy/v1.0.0/cardboard-corridor-policy.onnx
rusim model catalog
rusim model binding --client-id demo --runtime-mode unity-sim --agent-id ego
rusim model bind --client-id demo --runtime-mode unity-sim --agent-id ego --model-id model-...
```

CLI автоматически подхватывает соседние `metadata.json` и `metrics.json`, если они лежат рядом с `.onnx`.  
`name`, `version` и compatibility hints берутся из sidecar metadata или задаются явно.

## Backend API

Основные endpoint-ы:
- `POST /api/models/upload`
- `GET /api/models`
- `GET /api/model-catalog`
- `POST /api/models/activate`
- `GET /api/models/active`
- `GET /api/model-bindings/current`
- `POST /api/model-bindings`
- `POST /api/autopilot/start`
- `POST /api/autopilot/stop`
- `GET /api/autopilot/status`

## Что делает backend

- хранит registry моделей;
- группирует модели по `name -> versions`;
- хранит binding выбранной версии к конкретной цели запуска;
- валидирует ONNX-артефакт при загрузке;
- держит legacy active model как fallback;
- запускает inference loop автопилота;
- умеет выполнять inference как для flat `obs`, так и для multi-input vision ONNX (`image + ultrasonic`);
- возвращает статус и ошибки автопилота.

## Базовый сценарий

```mermaid
flowchart LR
    Train["Train / Build"] --> Artifact["ONNX + metadata + metrics"]
    Artifact --> Install["Install"]
    Install --> Bind["Bind name/version to target"]
    Bind --> Start["Start autopilot"]
    Start --> Eval["Check status and KPI"]
```

## Текущее состояние

Для `unity-sim` путь `train -> install -> bind -> run` уже собран на уровне интеграции, включая vision-policy для `cardboard-corridor-policy`.  
Следующий этап — улучшение самой модели и перенос контура на реальную платформу.
