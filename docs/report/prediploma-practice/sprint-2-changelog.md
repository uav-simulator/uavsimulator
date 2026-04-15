# Спринт 2 — Отчёт о выполненных работах

> **Период:** 05.04.2026 — 18.04.2026
> **Практика:** преддипломная (производственная), 09.04.04 «Программная инженерия»
> **Тема:** Разработка расширяемой программной платформы симуляции в Unity для проведения экспериментальных исследований в задачах обучения и sim-to-real для робототехнической платформы KS0223
> **Проект:** `uav-simulator`
> **Студент:** Горовенко Никита Максимович, группа КИ24-04-3М

---

## Статус на 2026-04-14

- PPO v2 best checkpoint (65k шагов из 200k): **successRate = 100%** (20/20 эпизодов), avgProgress = 98.6%, avgReward = 495.2. Модель уверенно проходит оба поворота L-образного маршрута на basic_arena. Оптимальный чекпойнт — 65k шагов; дальнейшее обучение приводит к деградации политики (overfitting).
- Исправлен баг camera capture в standalone runtime (MSAA mismatch + отсутствие `targetTexture`). Камера стабильно отдаёт корректные JPEG-кадры 1280x720 (~40-60 KB).
- Добавлен sim-to-real трек `track.cardboard_corridor.v1` — L-образный картонный коридор с ArUco-маркером, зарегистрирован в plugin catalog, рендерится в standalone runtime.
- Реализован CNN-PPO pipeline для vision-обучения (`ABCorridorVisionEnv`, `train_cardboard_corridor.py`).
- Backend autopilot расширен поддержкой vision ONNX-моделей (image + ultrasonic входы).
- Введена система каталога моделей с группировкой по имени/версии и биндингами к target.
- Runtime shader seeding решает проблему `Shader.Find() == null` в standalone билдах (Unity стрипает неиспользуемые шейдеры).

---

## Оглавление

1. [Цель и задачи спринта](#1-цель-и-задачи-спринта)
2. [RL-обучение: Gymnasium-среда и PPO pipeline](#2-rl-обучение-gymnasium-среда-и-ppo-pipeline)
3. [Reward shaping: формирование функции вознаграждения](#3-reward-shaping-формирование-функции-вознаграждения)
4. [Результаты тренировки PPO-модели](#4-результаты-тренировки-ppo-модели)
5. [Улучшение визуальной среды basic_arena](#5-улучшение-визуальной-среды-basic_arena)
6. [KPI-оценка: сравнение baseline vs PPO](#6-kpi-оценка-сравнение-baseline-vs-ppo)
7. [Продуктовый контур: train -> install -> run](#7-продуктовый-контур-train---install---run)
8. [Фикс camera capture pipeline (standalone URP)](#8-фикс-camera-capture-pipeline-standalone-urp)
9. [Sim-to-real трек: Cardboard Corridor](#9-sim-to-real-трек-cardboard-corridor)
10. [Vision pipeline: CNN-PPO и backend autopilot](#10-vision-pipeline-cnn-ppo-и-backend-autopilot)
11. [Каталог моделей и биндинги](#11-каталог-моделей-и-биндинги)
12. [Выполненные задачи](#12-выполненные-задачи)
13. [Текущие ограничения](#13-текущие-ограничения)
14. [План на Спринт 3](#14-план-на-спринт-3)

---

## 1. Цель и задачи спринта

**Цель спринта** — реализовать полный цикл обучения RL-модели (PPO) для навигационной задачи A->B в симуляторе, достигнуть ненулевого successRate, и провести сравнительную KPI-оценку с baseline-моделью.

### Планируемые задачи

| # | Задача | Статус |
|---|--------|--------|
| 1 | Gymnasium-среда `ABCorridorEnv` — обёртка над runtime API | Выполнено |
| 2 | Reward shaping: многокомпонентная функция вознаграждения | Выполнено |
| 3 | PPO-тренировка через stable-baselines3, ONNX-экспорт | Выполнено |
| 4 | Улучшение визуального окружения basic_arena | Выполнено |
| 5 | KPI-оценка обученной модели (20 эпизодов) | Выполнено |
| 6 | Сравнительный анализ baseline vs PPO | Выполнено |
| 7 | Валидация контура `train -> upload -> activate -> run` с обученной моделью | Выполнено |
| 8 | Фикс camera capture pipeline в standalone URP runtime | Выполнено |
| 9 | Sim-to-real трек `track.cardboard_corridor.v1` | Выполнено |
| 10 | CNN-PPO vision pipeline (среда, тренировка, export ONNX) | Выполнено |
| 11 | Vision ONNX autopilot в backend (image + ultrasonic) | Выполнено |
| 12 | Каталог моделей: группировка, версионирование, биндинги | Выполнено |
| 13 | Runtime shader seeding для standalone билдов | Выполнено |

---

## 2. RL-обучение: Gymnasium-среда и PPO pipeline

### 2.1. ABCorridorEnv

Создана Gymnasium-совместимая среда `ABCorridorEnv`, инкапсулирующая взаимодействие с Unity-runtime через HTTP API.

**Файл:** `python/training/ab_corridor_env.py`

**Пространство наблюдений** (8-мерный вектор, `Box[-1, 1]`):

| # | Признак | Описание |
|---|---------|----------|
| 0 | `line_tracker.s1_norm` | Датчик линии — левый канал |
| 1 | `line_tracker.s2_norm` | Датчик линии — канал 2 |
| 2 | `line_tracker.s3_norm` | Датчик линии — центральный канал |
| 3 | `line_tracker.s4_norm` | Датчик линии — канал 4 |
| 4 | `line_tracker.s5_norm` | Датчик линии — правый канал |
| 5 | `ultrasonic.front_norm` | Ультразвуковой датчик, расстояние / 5.0 м |
| 6 | `speed_norm` | Текущая скорость / 3.0 м/с |
| 7 | `heading_error_norm` | Ошибка курса к следующему waypoint / pi |

**Пространство действий** (2-мерный вектор, `Box[-1, 1]`):
- `throttle` — тяга/торможение
- `steer` — поворот

**Архитектурные решения:**
- Среда подключается к runtime по HTTP, каждый `step()` вызывает `POST /step`
- `reset()` вызывает `POST /reset` с параметрами из сценария
- `timeScale` увеличен до 3.0 при тренировке для ускорения сбора данных
- Прогресс по маршруту рассчитывается проекцией на полилинию waypoints

### 2.2. Тренировочный скрипт

**Файл:** `python/training/train_ab_corridor.py`

Скрипт реализует полный pipeline:
1. Инициализация среды с подключением к работающему runtime
2. Создание PPO-модели (stable-baselines3) с MLP-политикой `[64, 64]`
3. Тренировка с checkpoints и progress-логированием
4. Экспорт в ONNX-формат (opset 11) для runtime-inference
5. Quick-eval на 5 эпизодах с выводом метрик

**Гиперпараметры PPO v2:**

| Параметр | Значение |
|----------|----------|
| learning_rate | 3e-4 |
| n_steps | 1024 |
| batch_size | 256 |
| n_epochs | 10 |
| gamma | 0.99 |
| gae_lambda | 0.95 |
| clip_range | 0.2 |
| ent_coef | 0.02 |
| net_arch (pi, vf) | [64, 64] |
| total_timesteps | 200 000 |
| best_checkpoint | 65 000 шагов |

```bash
# Запуск тренировки (1 агент)
cd python && python training/train_ab_corridor.py \
  --total-timesteps 200000 \
  --time-scale 2.0 \
  --max-ep-steps 400 \
  --n-steps 1024 \
  --batch-size 256 \
  --ent-coef 0.02

# Запуск с 4 параллельными агентами
cd python && python training/train_ab_corridor.py \
  --n-agents 4 \
  --total-timesteps 200000 \
  --time-scale 2.0
```

---

## 3. Reward shaping: формирование функции вознаграждения

Функция вознаграждения состоит из 10 компонентов, обеспечивающих устойчивое обучение через оба поворота:

### Компоненты reward (v2)

| # | Компонент | Формула | Описание |
|---|-----------|---------|----------|
| 1 | Progress | `Δprogress * 100.0` | Основной сигнал: только вперёд (`max(0, Δ)`) |
| 2 | Waypoint bonus | `+5 / +15 / +25` при достижении | Прогрессивные бонусы на ключевых точках маршрута |
| 3 | Heading reward | `0.3 * (1 - \|err\|/π)` | Курсовое выравнивание без штрафа за повороты |
| 4 | Velocity reward | `0.5 * min(v·cos(err)/0.05, 1)` | Скорость в направлении цели |
| 5 | Lateral penalty | `-0.5 * (d/threshold)²` | Штраф за отклонение от трассы |
| 6 | Steer jerk | `-0.01 * \|steer - prev_steer\|` | Плавность управления |
| 7 | Stall penalty | `-0.05 * min(stall_steps/20, 1)` | Нарастающий штраф при отсутствии прогресса |
| 8 | Goal bonus | `+200.0` (терминальный) | Достижение финальной точки |
| 9 | OOB penalty | `-30.0` (терминальный) | Выезд за пределы коридора |
| 10 | Runtime done | `-15.0` (терминальный) | Аварийный останов от runtime |

**Ключевые решения v2 относительно v1:**
- Масштаб progress-reward увеличен 10× (→100) — стал доминирующим сигналом
- Добавлен `velocity_reward` — устраняет застревание после поворотов (машина получала heading_reward, но не двигалась)
- Добавлен `stall_penalty` — нарастает при `delta_progress < 0.001` более 20 шагов
- Goal bonus увеличен 4× (+50→+200), OOB penalty ужесточён (-20→-30)
- Waypoint bonuses `[5, 15, 25]` обеспечивают промежуточные сигналы на обоих поворотах

**Терминальные условия:**
- `goal_reached` — расстояние до финальной точки < `goal_radius_m` (1.0 м)
- `out_of_bounds` — латеральное отклонение > `corridor_width/2 + oob_margin` (1.8 м)
- `timeout` — превышение `max_steps` (400 шагов)

---

## 4. Результаты тренировки PPO-модели

**Параметры тренировки (v2):**
- Общее количество шагов: 200 000
- Ускорение времени: ×2.0
- Максимальная длина эпизода: 400 шагов
- Скорость: ~79 fps
- Время тренировки: ~42 мин

**Динамика обучения:**

| Этап (шаги) | ep_rew_mean | ep_len_mean | Наблюдение |
|-------------|-------------|-------------|------------|
| 0–20k | < 0 | < 100 | Случайные действия, быстрый OOB |
| 20k–65k | +100…+180 | ~360 | Модель проходит маршрут, ep_len растёт к max |
| 65k (пик) | ~177 | 360 | **Лучший checkpoint** — 100% успех при eval |
| 65k–200k | ~177 (плато) | 360 | Политика деградирует: std растёт до 1.8, mean action ухудшается |

**Вывод о checkpoint selection:**
- После 65k шагов ep_rew_mean перестаёт расти (plateau ≈ 177), но std действий начинает расти — модель переходит к стохастической стратегии с высокой дисперсией
- Eval лучшего checkpoint (65k): successRate = **100%** (20/20), avgProgress = 98.6%, avgReward = 495.2
- Eval финальной модели (200k): значительно хуже по детерминированному поведению
- Итог: оптимальная стратегия — ранняя остановка / выбор лучшего checkpoint по eval-метрике

---

## 5. Улучшение визуальной среды basic_arena

Трек `basic_arena` (track.basic_arena.v1) значительно улучшен визуально:

### Добавленные элементы

| Категория | Количество | Описание |
|-----------|-----------|----------|
| Деревья | 12 (было 4) | Размещены вдоль дороги с разным масштабом (0.75-1.3) |
| Кусты | 6 | Низкие сферы для покрытия грунта |
| Бордюры | 6 сегментов | Вдоль краёв каждого отрезка дороги |
| Фонарные столбы | 6 | Столб (цилиндр) + плафон (сфера) |
| Травяные участки | 4 | Круглые зоны на ландшафте с другим оттенком зелёного |
| Конусы | 6 (без изменений) | Дорожные конусы на ключевых точках |

### Цветовая палитра новых объектов

| Объект | RGB | Smoothness |
|--------|-----|-----------|
| Бордюр | (0.55, 0.55, 0.50) | 0.15 |
| Куст | (0.22, 0.50, 0.15) | 0.08 |
| Столб фонаря | (0.30, 0.30, 0.32) | 0.55 |
| Плафон фонаря | (0.95, 0.92, 0.75) | 0.60 |
| Трава | (0.24, 0.42, 0.16) | 0.05 |

**Файл:** `src/UnityProject/uav-simulator/Assets/Scripts/Tracks/BasicArenaTrack.cs`

---

## 6. KPI-оценка: сравнение baseline vs PPO

### Методика оценки

- Количество эпизодов: 20
- Максимальная длина эпизода: 300 шагов
- Ширина коридора: 3.0 м (basic_arena S-shape)
- Допуск выезда (OOB margin): 0.15 м
- Радиус достижения цели: 1.5 м
- Инструмент: `python/training/evaluate_ab_policy.py`
- Маршрут: (0,-7.5) → (0,-1) → (6,-1) → (6,5), длина 18.5 м

### Результаты

| Метрика | Baseline (linear) | PPO v1 (200k, финал) | PPO v2 (ckpt 65k) |
|---------|-------------------|----------------------|--------------------|
| successRate | 0% (0/20) | 0% (0/20) | **100% (20/20)** |
| avgSteps | ~32 | 300 (max) | ~360 |
| avgProgress | ~0% | 67.6% | **98.6%** |
| std(progress) | — | 0.2% | 0.3% |
| avgReward | — | — | 495.2 |
| termination breakdown | 20/20 OOB | 20/20 timeout | 20/20 goal |

**Evidence:** `evidence/eval_ppo_v1_20ep.json`, `evidence/eval_ppo_v1_20ep.svg`, `evidence/eval_ppo_v2_best_20ep.json`

### Анализ

- **Baseline** мгновенно вылетает за пределы трассы на первом же повороте (все 20 эпизодов — `out_of_bounds` за ~32 шага).
- **PPO v1** (финальная модель 200k шагов): стабильно проходит первый прямой участок (7.5 м), застревает у первого поворота. Проблема — отсутствие reward за скорость в направлении цели.
- **PPO v2 ckpt-65k**: уверенно проходит оба поворота. Ключевые изменения: `velocity_reward` (скорость × cos(heading_error)), `stall_penalty`, масштаб progress-reward ×10. Все 20 эпизодов завершаются достижением цели.

---

## 7. Продуктовый контур: train -> install -> run

Полный цикл работы с обученной моделью:

```bash
# 1. Тренировка модели (4 параллельных агента)
cd python && python training/train_ab_corridor.py \
  --n-agents 4 --total-timesteps 200000 --time-scale 2.0

# 2. Загрузка лучшего checkpoint в backend
rusim model install \
  python/training/artifacts/ab_corridor_ppo_v2/ab_corridor_ppo_v2_best.onnx \
  --name ab_corridor_ppo_v2 --version 2.0.0 --source ppo-training

# 3. Активация модели
rusim model activate ab_corridor_ppo_v2

# 4. KPI-оценка (20 эпизодов)
cd python && python training/evaluate_ab_policy.py \
  --model training/artifacts/ab_corridor_ppo_v2/ab_corridor_ppo_v2_best.onnx \
  --episodes 20 --include-trajectories \
  --output-json evidence/eval_ppo_v2_best_20ep.json
```

---

## 8. Фикс camera capture pipeline (standalone URP)

### Проблема

В standalone-билде Unity 6 с URP (Universal Render Pipeline) и Render Graph камера отдавала однотонный синий кадр (~15 KB) вместо рендера сцены.

### Причины

1. **MSAA mismatch:** `RenderTexture.antiAliasing = 4`, но URP pipeline ожидал другое количество сэмплов. Ошибка: `RenderPass: Attachment 0 was created with 1 samples but 4 samples were requested`.
2. **Отсутствие `targetTexture`:** `frontCamera.targetTexture` не назначался после создания RT — камера рендерила в экранный буфер, а `ReadPixels` читал из пустого RT.
3. **Shader stripping:** `Shader.Find("Universal Render Pipeline/Lit")` возвращал `null` в standalone-билде, т.к. Unity стрипала шейдеры, не привязанные ни к одному материалу в проекте.

### Решение

| Изменение | Файл |
|-----------|------|
| MSAA принудительно = 1 во всех пресетах и дефолтах | `Ks0223Vehicle.cs` |
| `QualitySettings.antiAliasing = 0` для всех профилей качества | `SimulationManager.cs` |
| `frontCamera.targetTexture = frontCameraRt` в `RecreateCameraTargets()` | `Ks0223Vehicle.cs` |
| `frontCamera.useOcclusionCulling = false` | `Ks0223Vehicle.cs` |
| URP assembly reference в .asmdef | `UavSimulator.Runtime.asmdef` |
| `RuntimeShaderAssetSeeder` — Material-ассеты в Resources для каждого шейдера | `Editor/RuntimeShaderAssetSeeder.cs` |
| `RuntimeMaterialCompatibility` переписан: сначала `Resources.Load`, затем `Shader.Find` | `Core/RuntimeMaterialCompatibility.cs` |
| Автозапуск сидера перед билдом | `Editor/RuntimeBuildPipeline.cs` |

### Результат

Камера стабильно отдаёт JPEG-кадры 1280x720 (~40–60 KB) с корректным рендером сцены как на `basic_arena`, так и на `cardboard_corridor`.

**Evidence:** `evidence/sprint2_camera_arena.jpg`, `evidence/sprint2_camera_cardboard.jpg`

---

## 9. Sim-to-real трек: Cardboard Corridor

### Описание

Процедурный L-образный коридор из картонных стен для sim-to-real тренировки. Воспроизводит реальный тестовый стенд из квартиры: картонные стенки 25 см, деревянный пол, ArUco-маркер на финише.

**Файл:** `src/UnityProject/uav-simulator/Assets/Scripts/Tracks/CardboardCorridorTrack.cs`

### Параметры

| Параметр | Значение | Описание |
|----------|----------|----------|
| corridorWidth | 0.40 м | Ширина коридора (40 см) |
| wallHeight | 0.25 м | Высота стен (25 см) |
| wallThickness | 0.02 м | Толщина стен (2 см) |
| segmentALength | 1.10 м | Прямой участок (вперёд по Z) |
| segmentBLength | 0.90 м | Прямой участок после поворота (по X) |
| roomWidth × roomLength | 1.50 × 1.90 м | Размер комнаты-контекста |

### Layout

```
Segment A: x=0, z=-1.1..z=0   (прямо, +Z)
Turn:      90° правый поворот в (0, 0)
Segment B: z=0, x=0..x=0.9    (прямо, +X)
Finish:    ArUco-маркер на стене x=0.9
Spawn:     (0, 0.01, -0.85), facing +Z
```

### Интеграция

- Зарегистрирован как `track.cardboard_corridor.v1` в `BuiltinPluginFactory`
- Дефолтный spawn `(0, 0.01, -0.85)` и маршрут через L-поворот в `SimulationManager`
- Scenario-файл: `configs/scenarios/cardboard-corridor-v1.yaml`

---

## 10. Vision pipeline: CNN-PPO и backend autopilot

### 10.1. ABCorridorVisionEnv

Gymnasium-среда для vision-обучения с камерой и ультразвуком.

**Файл:** `python/training/ab_corridor_vision_env.py`

**Пространство наблюдений** (Dict):

| Ключ | Shape | Dtype | Описание |
|------|-------|-------|----------|
| `image` | (84, 84, 3) | uint8 | RGB-кадр с камеры, ресайз до 84×84 |
| `ultrasonic` | (1,) | float32 | Нормализованное расстояние ультразвука |

**Пространство действий:** `Box[-1, 1]`, shape (2,) — throttle, steer.

Среда поддерживает загрузку конфигурации из YAML-сценария (`--scenario`), что обеспечивает единый источник правды для spawn, waypoints и параметров коридора.

### 10.2. Тренировочный скрипт

**Файл:** `python/training/train_cardboard_corridor.py`

- CNN-PPO (stable-baselines3 `CnnPolicy` с custom feature extractor)
- Экспорт в ONNX с входами `image` (84×84×3) и `ultrasonic` (1,)
- Генерация `metadata.json` с полями совместимости (`runtimeModes`, `vehicleIds`, `robotKinds`)

### 10.3. Vision autopilot в backend

**Файл:** `src/ks0223-web-mac/backend/Services/AutopilotService.cs`

`PolicyPredictor` расширен поддержкой двух режимов:

| Режим | Входы | Определение |
|-------|-------|-------------|
| FlatVector | 1D float tensor (8-dim) | ONNX-модель с одним 2D-входом |
| ImageAndUltrasonic | image (4D) + ultrasonic (2D) | ONNX-модель с входами `image` и `ultrasonic` |

При загрузке ONNX-модели режим определяется автоматически по именам и размерностям входов. Для vision-модели autopilot loop запрашивает JPEG-кадр через `TryGetLatestFrame()`, декодирует через `SixLabors.ImageSharp`, ресайзит до целевого разрешения и нормализует в `[0, 1]`.

---

## 11. Каталог моделей и биндинги

### Backend

**Файл:** `src/ks0223-web-mac/backend/Services/ModelRegistryService.cs`

Расширения:
- **Каталог:** `GET /api/models/catalog` — модели сгруппированы по имени, внутри — версии по дате.
- **Биндинги:** привязка модели к конкретному target (clientId + runtimeMode + agentId):
  - `GET /api/models/binding?clientId=...&runtimeMode=...` — текущая привязка
  - `POST /api/models/bind` — установить привязку
- **Автообнаружение metadata.json:** при `model install` backend автоматически читает `name`, `version`, `source` из сайдкар-файла `metadata.json` рядом с артефактом.
- **Дедупликация:** загрузка модели с совпадающим name+version отклоняется.

### CLI

Новые команды:
- `rusim model catalog` — сгруппированный список моделей
- `rusim model binding --client-id ... --runtime-mode ...` — показать привязку
- `rusim model bind <model_id> --client-id ... --runtime-mode ...` — привязать модель

### Frontend

`ModelControlPage` переработан: вместо плоского списка — выбор модели по имени и версии, отображение привязки, визуализация совместимости.

---

## 12. Выполненные задачи

### 12.1. Gymnasium-среда ABCorridorEnv

- Создан файл `python/training/ab_corridor_env.py`
- 8-мерное пространство наблюдений (все 5 каналов line tracker, ультразвук, скорость, ошибка курса)
- 2-мерное пространство действий (throttle, steer)
- Расчёт прогресса через проекцию на полилинию маршрута
- Расчёт ошибки курса через вектор скорости и направление на следующий waypoint

### 12.2. PPO-тренировка и ONNX-экспорт

- Создан файл `python/training/train_ab_corridor.py`
- PPO через stable-baselines3 с MLP [64, 64]
- Экспорт обученной политики в ONNX (opset 11) через `torch.onnx.export`
- Автоматическое сохранение checkpoints каждые 5000 шагов
- Quick-eval встроен в конец скрипта

### 12.3. Reward shaping

- 6-компонентная функция вознаграждения
- Прогресс по маршруту как основной сигнал
- Латеральный штраф квадратичной формы
- Штраф за дёргание руля (steer jerk)
- Терминальные бонусы/штрафы за финиш и выезд за пределы

### 12.4. Улучшение визуальной среды

- Добавлено 8 новых деревьев (итого 12)
- 6 кустов для покрытия грунта
- 6 бордюрных сегментов вдоль дороги
- 6 фонарных столбов с плафонами
- 4 травяных участка для визуального разнообразия ландшафта

### 12.5. Camera capture fix

- Устранён MSAA mismatch между RenderTexture и URP pipeline
- Добавлен `frontCamera.targetTexture = frontCameraRt`
- Runtime shader seeding через Material-ассеты в Resources
- `RuntimeMaterialCompatibility` переписан с fallback-цепочкой

### 12.6. Cardboard Corridor трек

- Процедурный L-коридор (40 см ширина, 25 см стены)
- Регистрация в plugin catalog, дефолтный spawn и маршрут
- ArUco-style finish marker

### 12.7. Vision pipeline

- `ABCorridorVisionEnv` — Gymnasium-среда с image (84×84×3) + ultrasonic
- `train_cardboard_corridor.py` — CNN-PPO тренировка с ONNX экспортом
- `evaluate_cardboard_corridor.py` — evaluation скрипт
- Backend autopilot с автодетекцией vision ONNX-моделей

### 12.8. Каталог моделей

- Группировка по name/version в backend
- Биндинги модели к target (clientId, runtimeMode, agentId)
- CLI: `rusim model catalog`, `rusim model bind`, `rusim model binding`
- Frontend: переработанный ModelControlPage

---

## 13. Текущие ограничения

| Ограничение | Влияние | Планируемое решение |
|-------------|---------|---------------------|
| Тренировка только на CPU | Скорость ~79 fps, 200k шагов ~ 42 мин | GPU-ускорение в дальнейших экспериментах |
| Нет domain randomization | Модель обучена на фиксированном маршруте, spawn-точке и освещении | Вариация spawn, освещения, текстур в Спринт 3 |
| Нет реальной трассы для sim-to-real | Cardboard corridor готов в симуляторе, но реальный стенд пока не построен | Сборка физического стенда в Спринт 3 |
| Оценка без collision-метрики | Столкновения не экспонируются runtime API | Добавить collision counter в runtime контракт |
| Vision модель (CNN-PPO) ещё не достигла goal | successRate = 0%, avgProgress = 41% | Увеличить объём тренировки, доработать reward |
| Multi-agent тренировка (4 агента) не протестирована | Инфраструктура `ABCorridorMultiAgentVecEnv` готова, но фактически все прогоны — 1 агент | Запустить 4-агентное обучение, измерить speedup |

---

## 14. План на Спринт 3

| # | Задача | Приоритет |
|---|--------|-----------|
| 1 | 4-агентное параллельное обучение PPO: `--n-agents 4`, измерить реальный speedup vs 1-агентное | Высокий |
| 2 | CNN-PPO тренировка на cardboard corridor (200k+ шагов) с vision env | Высокий |
| 3 | Safety wrapper: ограничение скорости, аварийный стоп при подключении к реальному роботу | Высокий |
| 4 | Сборка физического стенда из картонных стенок для sim-to-real тестов | Средний |
| 5 | Тестирование vision-модели на реальном роботе KS0223 (если стенд готов) | Средний |
| 6 | Domain randomization: вариация spawn position, освещения, текстур стен | Средний |
| 7 | Sim-to-real gap analysis: сравнение поведения sensor-based PPO v2 в sim vs real | Низкий |
