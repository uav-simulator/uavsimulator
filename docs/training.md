# Обучение моделей

Краткий обзор контура обучения. Полная техническая спецификация — в
[главе 6 диссертации «Программная обвязка обучения»](master-thesis/08-training-python.md).

## Контур

Обучение запускается из Python и использует Unity-симулятор как
HTTP-окружение. Никакая часть тренировочного кода не импортирует Unity-сборку
напрямую — связь идёт через `python/sim_client/http_client.py` и
runtime API, описанный в [главе 4 диссертации](master-thesis/06-api-spec.md).

```
python/training/  →  SimClient  →  Unity HTTP API (:8000)  →  SimulationManager
```

## Что фиксируется как контракт

- **Наблюдения**: словарь `{image: HxWx3 uint8 JPEG-decoded, ultrasonic: float[5]}`
  для primary-агента, плюс массив `telemetry` для каждого активного агента.
- **Действия**: `ControlCommand{throttle: float, steer: float}` (непрерывные)
  либо одно из дискретных значений `DirStop / Forward / Back / Left / Right`,
  передаваемое через wrapper.
- **Метрики**: `route.distance_m`, `route.completed`, `frame_id`, `agent_count` —
  возвращаются Unity в поле `info` каждого `step()`.

Полная схема DTO зафиксирована в `Assets/Scripts/Contracts/SimulatorContracts.cs`
и в разделе 4.3 диссертации.

## Где живут эксперименты

- Тренировочные скрипты — `python/training/train_*.py`.
- Окружения — `python/training/{ab_corridor_vision_env, multi_agent_vision_env, meta_multi_agent_vec_env}.py`.
- Wrappers (дискретизация, аугментация, задержки) — `python/training/`.
- Артефакты — `python/training/artifacts/<run-name>/` (PPO checkpoint, ONNX, evidence-папка).
- Конфиги сценариев для Unity — `configs/scenarios/`.

## KPI и приёмка

Скрипт `python/training/evaluate_ab_policy.py` запускает 20 эпизодов на
канонической трассе и фиксирует success rate. Целевой порог и trace
по revisions описаны в [главе 7 «Sim-to-real»](master-thesis/09-sim2real-real-car.md).

## Запуск

```bash
# Поднять Unity-сцену с runtime API
rusim server up --scenario configs/scenarios/cardboard_corridor_v9.yaml

# В отдельном терминале запустить тренировку
python python/training/train_cardboard_corridor_v9.py --rev 42

# Экспортировать обученную политику в ONNX
python python/training/export_onnx.py --rev 42

# Активировать модель в backend
rusim model upload python/training/artifacts/cardboard-corridor-ppo-v9-rev42/policy.onnx
rusim model activate cardboard-corridor-ppo-v9-rev42
```

## См. также

- [Глава 6 диссертации — Программная обвязка обучения](master-thesis/08-training-python.md)
- [Глава 7 диссертации — Sim-to-real эксперименты](master-thesis/09-sim2real-real-car.md)
- [Model lifecycle](model-lifecycle.md) — как обученная модель попадает в backend и активируется.
- [CLI](cli.md) — команды `rusim server`, `rusim model`, `rusim scenario`.
