# Training Artifacts (A->B)

Скрипт `build_ab_policy_artifact.py` собирает baseline-артефакт для контура `train -> install -> run`:
- `ab_corridor_policy_v1.onnx`
- `metadata.json`
- `metrics.json`

Артефакт собирается в backend-compatible формате для текущего `Microsoft.ML.OnnxRuntime`.

## Запуск

```bash
cd python/training
python3 -m pip install onnx numpy
python3 build_ab_policy_artifact.py
```

Артефакты сохраняются в:

`python/training/artifacts/ab_corridor_policy_v1/`

## Загрузка в backend

```bash
curl -X POST "http://localhost:5058/api/models/upload" \
  -F "file=@python/training/artifacts/ab_corridor_policy_v1/ab_corridor_policy_v1.onnx" \
  -F "name=ab-corridor-policy-v1" \
  -F "version=1.0.0" \
  -F "source=python-rl-api" \
  -F "metadata=$(cat python/training/artifacts/ab_corridor_policy_v1/metadata.json)" \
  -F "metrics=$(cat python/training/artifacts/ab_corridor_policy_v1/metrics.json)"
```

## Установка через rusim

```bash
./rusim model install python/training/artifacts/ab_corridor_policy_v1/ab_corridor_policy_v1.onnx
./rusim model active
```

`rusim` автоматически подхватит соседние `metadata.json` и `metrics.json`, если они лежат рядом с `.onnx`.

## KPI-оценка в unity-sim

Скрипт `evaluate_ab_policy.py` прогоняет серию эпизодов напрямую через `unity-sim` API (`/reset` + `/step`) и сохраняет:
- JSON-сводку по эпизодам;
- SVG с траекториями.

Запуск:

```bash
/opt/homebrew/bin/python3.13 -m venv /tmp/uavsim-eval-venv313
/tmp/uavsim-eval-venv313/bin/pip install requests PyYAML numpy onnxruntime
/tmp/uavsim-eval-venv313/bin/python python/training/evaluate_ab_policy.py \
  --episodes 20 \
  --max-steps 220 \
  --output-json docs/report/prediploma-practice/evidence/ab-corridor-kpi-2026-03-29.json \
  --output-svg docs/report/prediploma-practice/evidence/ab-corridor-kpi-2026-03-29.svg
```

Ограничения текущего evaluator:
- `goal_reached` и `timeout` считаются строго;
- `out_of_bounds` считается внешне по геометрии коридора;
- `collision` пока не входит в KPI-сводку, потому что runtime-контракт его явно не отдает.
