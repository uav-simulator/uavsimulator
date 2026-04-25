# Спринт 3 — журнал изменений преддипломной практики

> **Период:** 19.04.2026 — 02.05.2026
> **Практика:** преддипломная (производственная), 09.04.04 «Программная инженерия»
> **Тема:** Разработка расширяемой программной платформы симуляции в Unity для проведения экспериментальных исследований в задачах обучения и sim-to-real для робототехнической платформы KS0223
> **Проект:** `uav-simulator`
> **Студент:** Горовенко Никита Максимович, группа КИ24-04-3М

---

## Цель этапа

Достигнуть positive successRate > 0 для обученной модели на процедурных maze-трассах, подготовить safety-обвязку для реального робота и провести первые sim-to-real прогоны.

## Планируемые задачи

| # | Задача | Приоритет | Статус |
|---|--------|-----------|--------|
| 1 | Curriculum learning для maze (staged difficulty A→D) | Высокий | В работе |
| 2 | Модель `cardboard-maze-ppo-v7` с transfer от v6 | Высокий | В работе |
| 3 | Domain randomization: вариация освещения, текстур, шума камеры | Средний | |
| 4 | Safety wrapper: ограничение скорости, аварийный стоп по ультразвуку | Высокий | |
| 5 | Калибровка физики sim ↔ real (замеры KS0223) | Высокий | |
| 6 | Скрипт deploy ONNX модели на реального робота | Высокий | |
| 7 | Сборка физического стенда из картонных стенок | Средний | |
| 8 | Первые sim-to-real прогоны на L-коридоре | Высокий | |
| 9 | Sim-to-real gap analysis | Средний | |

---

## 1. Curriculum learning для maze

Спринт 2 закрыл sim-to-real на фиксированном L-коридоре (`cardboard-corridor-ppo-v6`, 100% SR на 20 эпизодах), но обучение на процедурных maze-трассах не сошлось ни в одной из четырёх попыток (v2–v5). Анализ отказов:

| Версия | Подход | Исход | Причина |
|---|---|---|---|
| v2 | Рандомизация каждый эпизод, 40k | reward −119 → −42, до 0 не дотянул | Unity нестабилен при частой регенерации; слишком много вариаций сразу |
| v3 | Фиксированный seed=42, 8 клеток | reward застрял на −290 | Путь 4 м против 1.5 м на L; случайная политика никогда не доходит до финиша за 400 шагов |
| v4 | Transfer от v6, lr=3e-4 | reward скатился −48 → −162 | Catastrophic forgetting: сложные maze «размывают» выученное |
| v5 | Transfer от v6, lr=3e-5, clip=0.1 | reward деградировал медленнее, но до −305 | Тот же forgetting, просто медленнее |

Общий вывод — агент не получает положительного сигнала на старте, поэтому обучение с нуля застревает, а transfer от v6 деградирует под напором сложных примеров.

### 1.1. Решение — staged curriculum

Реализован `MazeCurriculumCallback` (`python/training/maze_curriculum.py`), который на ходу меняет диапазоны параметров maze в зависимости от числа шагов обучения. Геометрия стартует тривиально близкой к L-коридору (где v6 даёт 100% SR), постепенно усложняясь.

| Стадия | Шаги | length_cells | turns (L/R) | corridor_width_m |
|---|---|---|---|---|
| A — easy | 0–40k | 3–4 | 0–1 / 0–1 | 0.58–0.62 |
| B — medium | 40k–100k | 4–6 | 0–2 / 0–2 | 0.55–0.65 |
| C — hard | 100k–170k | 5–8 | 1–3 / 1–3 | 0.52–0.70 |
| D — full | 170k+ | 5–12 | 1–4 / 1–4 | 0.50–0.80 |

Внутри env добавлен метод `set_maze_param_ranges()`, callback дёргает его через `SubprocVecEnv.env_method()`, работает как на одиночном env, так и на векторизованном.

### 1.2. Гиперпараметры v7

Решение по lr/clip взято из анализа v4/v5:

| Параметр | v6 (corridor) | v7 (maze, transfer) | Обоснование |
|---|---|---|---|
| learning_rate | 3e-4 | **1e-4** | Мягче, чтобы не разрушить v6 policy на stage-A |
| clip_range | 0.2 | **0.15** | Узкий PPO-клип — та же причина |
| ent_coef | 0.02 | **0.01** | Меньше случайности, использовать знание v6 |
| timesteps | 250k | 150k | Бюджет ночного прогона |
| max_ep_steps | 400 | 400 | Как в спринте 2 |
| maze_regen_every | — | 5 | Один maze живёт 5 эпизодов (стабильность Unity) |
| aruco_goal | ✓ | ✓ | Параллельный сигнал цели (+20 за детекцию) |
| device | mps/cpu | mps | Ускорение обучения |

Старт: transfer из `cardboard_cnn_ppo_250000_steps.zip` (финальный чекпоинт v6). `num_timesteps` модели сброшен в 0, чтобы curriculum стартовал со стадии A, а не сразу переходил в D.

## 2. Следующие шаги после v7

Порядок зависит от результата v7 eval:

- Если SR на stage-A ≥ 70% и просадка на B/C/D контролируемая → переход к domain randomization (освещение, текстуры, шум) для sim-to-real.
- Если модель деградирует или застревает на одной из стадий → разбор на tensorboard, подстройка schedule (длиннее stage-A или дополнительные промежуточные стадии), повторный прогон с тем же бюджетом.

## 3. Sim-to-real pipeline (параллельная ветка)

Независимо от результата v7, sim-to-real развёртывание опирается на уже готовую модель v6 (100% SR на фиксированной L-трассе). Для реального робота нужны:

1. Safety wrapper в backend autopilot: `maxSpeed` клип, E-stop по ультразвуку < 15 см, ramp-up throttle.
2. Калибровка `maxYawRate` и `maxSpeed` на реальном KS0223 (замеры с секундомером) vs симулятор (2.2 м/с, 160°/с).
3. Deploy pipeline: ONNX из `artifacts/cardboard-corridor-ppo-v6/1.0.0/` → активация через `rusim model activate` → autopilot запуск через WebUI.
4. Первые прогоны на физической L-трассе из картона (воспроизведение геометрии из `configs/scenarios/cardboard-corridor-v1.yaml`).
5. Логирование sim-vs-real метрик (реальные steps-to-goal, lateral, contact events) и сравнение с симуляторным eval.

## Результаты (День 1, 18.04.2026)

### Что сделано

- **Реализован `MazeCurriculumCallback`** ([python/training/maze_curriculum.py](../../../python/training/maze_curriculum.py)) — staged ranges на 4 стадии (A→D), применяется через `env_method()` на любом SubprocVecEnv/DummyVecEnv.
- **Setter в env** ([python/training/ab_corridor_vision_env.py](../../../python/training/ab_corridor_vision_env.py)) — метод `set_maze_param_ranges()` позволяет callback менять диапазоны на ходу.
- **Поддержка transfer с override hyperparameters** в `train_cardboard_corridor.py`: при `--resume` можно задавать новые lr/clip/ent_coef поверх загруженной модели, `num_timesteps=0` сбрасывается для корректной работы curriculum со стадии A.
- **Три попытки обучения v7** за ночь (~3.5ч компьютерного времени):
  1. Att1 (lr=1e-4, stage-A 3-4 клетки, 0-1 поворотов): catastrophic forgetting за 20 мин.
  2. Att2 (lr=5e-5, stage-A 3 клетки, ровно 1R): слишком короткие эпизоды (3-4 шага), PPO нестабилен.
  3. Att3 (lr=1e-4, stage-A 5 клеток, 1R, 3м путь): сначала catastrophic KL-всплеск (0.96), потом стабилизация и обучение с reward −28 до +12 за 29k шагов. Дошёл до 36k шагов (stage-B начался на 25k).
- **7 чекпоинтов v7** сохранены (5k/10k/15k/20k/25k/30k/35k).
- **Детерминированный eval-сценарий** `configs/scenarios/cardboard-maze-stageA-eval.yaml` для проверки на фиксированной maze-геометрии.

### Результаты eval (20 эпизодов, seed_offset=3000 для corridor, 10 эпизодов seed_offset=7000 для maze)

| Модель | L-corridor SR | L-corridor reward | Stage-A maze SR | Stage-A maze reward | Stage-A progress |
|---|---|---|---|---|---|
| v6 baseline (sprint 2) | 100% (20/20) | 116.74 | 0% | +18.54 | 67% |
| **v7@35k** (этот спринт) | **100% (20/20)** | 103.44 | 0% | +12.08 | 64% |
| v7@30k (промежуточный) | — | — | 0% | −4.32 | 67% |
| v7@25k (конец stage-A) | — | — | 0% | −42.47 | 62% |

### Проверка

- v7@35k **держит 100% SR на L-коридоре** — sim2real кейс спринта 2 не пострадал.
- v7 НЕ достиг positive SR>0 на maze за ночь (35k шагов недостаточно, на основании динамики нужно 80-100k+).
- v6 zero-shot на maze stage-A даёт reward +18 — уже лучше чем v7@35k на том же maze, что показывает что curriculum пока не перебил baseline, и это нужно учитывать в следующих прогонах.

### Ограничения

- **FPS обучения 2.8-3.0 на MPS** с одним Unity рантаймом. Для целевого бюджета 200k+ шагов нужен multi-runtime запуск (`rusim server up --count 3`, `--num-envs 3`).
- **Catastrophic forgetting при transfer от v6 на maze** — основная проблема curriculum с transfer. Гипотеза: Unity-maze использует свой spawn, не тот же что у corridor, → v6 видит off-distribution кадр на первом шаге. Лечится инъекцией `spawn.position`/`spawn.yaw` из Python maze-генератора в trackParams при reset.
- **ONNX для v7 не экспортирован** — есть только SB3 zip. Экспорт — ~30 строк поверх существующей функции `export_to_onnx` в `train_cardboard_corridor.py`.

### Следующие шаги

1. **[training]** Инъектировать spawn из Python в Unity через trackParams — устранит гэп который провоцирует forgetting.
2. **[training]** Запустить multi-runtime обучение (3× Unity) для 150-200k шагов за ночь. При этом transfer вероятно стабилизируется сам если исправить spawn.
3. **[training]** Экспортировать v7@35k в ONNX для deploy.
4. **[sim2real]** Safety wrapper в backend autopilot (`src/ks0223-web-mac/backend/Services/AutopilotService.cs`, 840 строк, ноль safety-логики): клип maxSpeed, E-stop по ультразвуку, ramp-up throttle.
5. **[sim2real]** Калибровка реального KS0223: замерить maxSpeed и maxYawRate vs sim (2.2 м/с, 160°/с).
6. **[sim2real]** Собрать картонный стенд по геометрии `cardboard-corridor-v1.yaml` (ширина 0.60м, высота 0.25м, L-форма с правым поворотом, ArUco на финише).
7. **[sim2real]** Первые прогоны v6 или v7 на физической L-трассе, логирование sim-vs-real метрик.

## Результаты (День 2, 25.04.2026)

### Что сделано

- **Запущено обучение `cardboard-maze-ppo-v8`** с нуля (без transfer от v6) на 3× Unity runtime (`:8000-:8002`), 150k шагов, ~70 минут wall-clock (~36 fps).
- **Curriculum пройден полностью:** A-easy (0–25k) → B-medium (25k–60k) → C-hard (60k–100k) → D-full (100k–150k). Все 10 промежуточных чекпоинтов сохранены.
- **Best-checkpoint выбор:** override 150k → **120k**. Внутри одной curriculum-стадии (D-full) `ep_rew_mean` упал с −1 (на 120k) до −22 (на 150k), что указывает на overfitting на сложные образцы / шумную ленту в финале. 120k скопирован поверх `cardboard-maze-ppo-v8_sb3.zip`.
- **Robustness sweep (10 сценариев × 10 эпизодов)** запущен на single-runtime (свернули с трёх до одного для детерминизма). Артефакты: `docs/report/prediploma-practice/evidence/v8_robustness/eval_*.json` + `log_*.txt`.
- **L-corridor regression eval (20 эпизодов, seed_offset=3000)** на v8@120k: SR 100%, reward 108.45, progress 77.0%. Артефакт: `docs/report/prediploma-practice/evidence/v8_robustness/eval_corridor_regression.json`.

### Robustness sweep — v8@120k (10 эпизодов на сценарий)

| Сценарий | SR | avgReward | avgProgress | avgSteps |
|---|---|---|---|---|
| short_L_3c | 0% | -11.63 | 17.3% | 19 |
| medium_L_4c | 0% | +3.58 | 46.4% | 34 |
| long_L_7c | 0% | +27.79 | 72.7% | 80 |
| long_L_9c | 0% | +40.19 | 79.5% | 111 |
| left_turn | 0% | +13.42 | 58.9% | 49 |
| straight_5c | 0% | +13.55 | 59.0% | 48 |
| zigzag_6c_RL | 0% | -17.20 | 67.5% | 76 |
| zigzag_7c_RR | 0% | +35.53 | 73.4% | 74 |
| narrow_05m | 0% | +3.05 | 51.2% | 39 |
| wide_07m | 0% | +14.10 | 65.2% | 63 |
| **SR ≥ 50%** | **0/10** | — | — | — |

### Verdict против sprint-3 критериев

- [ ] ≥ 3/10 сценариев с SR ≥ 50% — **FAIL** (0/10, как и v7).
- [x] L-corridor SR ≥ 80% — **PASS** (100% / 20 из 20, reward 108.45 vs 116.74 у v6).

### Анализ

Curriculum дал **значительное улучшение reward/progress** по сравнению с v7 (zigzag_6c_RL: −350.98 → −17.20; long_L_7c: −2.46 → +27.79; long_L_9c: −13.05 → +40.19), но **не закрыл финальный goal-step**: на длинных и зигзаг-сценариях агент проходит 60–80% дистанции, тратит много шагов и не достигает финиша до timeout. Гипотеза: per-step shaping слишком слабый относительно step penalty, поэтому интегральный reward выходит положительным даже без `goal_reached`. Полный side-by-side: [v8_vs_v7_vs_v6.md](./evidence/v8_vs_v7_vs_v6.md).

### Следующие шаги

1. **[training]** v9 transfer-from-v8@120k с усиленным goal-shaping: увеличить per-step distance-to-goal коэффициент и/или добавить bonus за приближение в последние 20% дистанции, без изменения curriculum. Бюджет 100k шагов (transfer быстрее сходится).
2. **[training]** Повторить тот же 10-сценарный sweep на v9 — это даст чистый A/B v8 vs v9 на одной reward-функции.
3. **[sim2real]** Параллельно — продолжать sim2real-подготовку на v6 (подтверждённый 100% SR на L-коридоре) и не блокировать deploy ожиданием v9.
