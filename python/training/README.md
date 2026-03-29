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
