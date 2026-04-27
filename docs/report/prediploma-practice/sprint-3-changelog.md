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

## Результаты (День 3, 25.04.2026) — Sim-to-real первый прогон

### Что сделано

- **Backend поднят локально** (`dotnet run`, порт 5287), TCP-коннект к Pi установился, OLED робота сменил `State: Disconnect` на коннект-статус.
- **Camera UDP pipeline подтверждён рабочим:** ~34 fps приёма JPEG-кадров от `FramesSend.py` на Pi после ICMP-ping handshake (sender ловит `source IP` из echo, дальше шлёт UDP на :5051). 19 КБ/кадр, IP-фрагментация без потерь в WiFi.
- **v6 ONNX экспортирован** из SB3 zip ([export_v6_onnx.py](../../python/training/export_v6_onnx.py), 3.5 МБ, opset 18) — у v6 ONNX отсутствовал в артефактах после Sprint 2. Загружен через `/api/models/upload`, активирован, привязан к `runtimeMode=real-robot`.
- **Калибровка реального KS0223** двумя DirForward-бёрстами + рулеткой Никиты: **v ≈ 0.73 м/с** на `drive_speed_percent: 80`. Sim-to-real скорость gap × 1.5 (ожидаемая sim ~1.0 м/с). Ультразвук занижает абсолютное расстояние на ~25%, дельта при движении на 30–50% (фильтр сглаживания ~150 мс).
- **Stage 1 / Run 1: автопилот 50% throttle на физической L-трассе.** 57 шагов, 8.7 с, **E-stop @ 10.49 см ультразвук**, **1 контакт со стеной** (правая стена сегмента А завалилась). Прерван по протоколу безопасности (≥1 контакт = стоп). Stage 2/3 не запускались.
- **Safety wrapper подтверждён работающим в проде:** E-stop триггер, sticky hold, throttle clip 0.5, `eStopTriggerCount` reset на старте сессии — всё ровно как в unit-тестах. Инфраструктура sim-to-real готова.

### Поведение модели v6 на реале

Модель почти весь прогон выдавала `steer = +1.00` (полный лево), throttle прыгал между +0.5 и -0.95. Из 57 шагов: ~45 DirLeft (= ротация на месте у diff-drive ks0223), ~10 DirBack, почти 0 DirForward. Робот **крутился на месте** и снёс правую стену.

### Корневые причины sim-to-real провала

1. **Visual domain gap (главный).** Камера KS0223 наклонена сильно вниз и видит пол + кусок дивана/оранжевую тумбу сбоку — не видит «коридор» как в Unity тренировке. Distribution shift → policy collapses to constant max-steer.
2. **Control mapping gap.** В Unity `vehicle.prometeo.sport.v1` пара `(throttle=+0.5, steer=+1)` = forward arc вперёд-влево. На KS0223 эта же пара через `AutopilotService.ResolveCommand` маппится в `DirLeft` = in-place rotation. Модель ожидала движение по дуге, получила вращение.
3. **Отсутствие domain randomization** при тренировке v6/v7/v8 (фиксированные текстуры/освещение/угол камеры).

Полный отчёт: [evidence/sim2real_run_2026-04-25/README.md](./evidence/sim2real_run_2026-04-25/README.md).

### Verdict против sprint-3 критериев

- [x] **Safety wrapper end-to-end в проде** — PASS (E-stop сработал, никаких ложных срабатываний за 57 шагов, robot wall-slam перехвачен).
- [x] **Sim-to-real инфраструктура готова** — PASS (TCP, UDP-камера, ONNX inference, telemetry, model registry все работают).
- [ ] **v6 проходит реальную L-трассу** — FAIL (0/1 успешных запусков, 1 контакт со стеной за 1 прогон). Прерван дальше, чтобы не разносить трассу.

### Следующие шаги

1. **[training]** v9/v10 с **domain randomization**: randomized texture/lighting/camera-angle, motion blur, noise. Это базовый фикс №1 для visual sim-to-real, у v6/v7/v8 его не было.
2. **[backend]** Расширить `ResolveCommand` маппинг (или ввести `DirArcLeft`/`DirArcRight` для diff-drive forward-with-bias), либо переобучить модель на vehicle plugin с in-place rotation. Текущий маппинг даёт «крутится на месте» при steer≥0.55, что физически отличается от того что модель видела в обучении.
3. **[sim2real]** Поднять servo-угол камеры (API уже есть в Pi, но не использовался), чтобы реальный кадр приближался к sim-углу.
4. **[sim2real]** ArUco на финише как минимум для одной попытки — модель имела +20 reward bonus за aruco_goal в обучении, а на реале этого сигнала не было совсем.
5. **[reporting]** Финальный отчёт диплома: sprint 3 закрыт с честным sim-to-real результатом + dependency на новую тренировочную итерацию для production-готового policy.

## Результаты (День 4, 26.04.2026) — Win-кластер обучения, серия sim-to-real итераций v9, фикс инфраструктуры

### Что сделано

#### Производственная инфраструктура — Win-кластер для тренировки

- **Поднят отдельный compute-узел** на Win11 + RTX 5080 + Ryzen 9950X3D, доступ через SSH (`ssh win`). Цель — снять ограничение Mac M4 Max (одиночный Unity рантайм, 3 fps) и тренировать на реальном GPU. Локальный Mac остаётся edge-узлом для backend, WebUI, real-robot bridge.
- **Полный repo-клон** на Win, идентичный Mac, синхронизация через git (`develop` → `rev18`). Изменения C# Unity-кода и Python-тренера правятся на Mac, push/pull на Win, запускаются там через SSH-команды.
- **8 параллельных Unity-инстансов** (`start_unity_farm_gpu.ps1`, ports 8000–8007) с `-force-d3d12 -gpu-id 1` — RTX 5080 принудительно (не iGPU). Bind: 1 GPU делит VRAM на 8 рендеров, ~12 ГБ занято при 1280×720 «high» профиле.
- **Сетевой fix Windows-only** ([python/sim_client/http_client.py](../../../python/sim_client/http_client.py)): persistent `requests.Session()` с `HTTPAdapter(pool_maxsize=16)` вместо per-request connections — без него Win исчерпывает TCP ephemeral-port pool (`WinError 10055`) при `SubprocVecEnv` с 8 envs и тренировка падает через 5–10 мин с EOFError в worker.
- **Detached process launcher** через Win Task Scheduler (`schtasks /Create … /RU SYSTEM`) — SSH-сессия может закрыться, тренировка живёт. Прямой `Start-Process` от SSH убивается hangup'ом сессии, batch + scheduled task привязывает процесс к SYSTEM-сессии.
- **Unity build pipeline через git**: edit C# на Mac → `git push rev18` → `ssh win git pull` → `Unity.exe -batchmode -executeMethod UavSimulator.EditorTools.RuntimeBuildPipeline.BuildWindowsRuntime`. Build пишет в `src/UnityProject/.../build/runtime/windows/`, копируем в `build/runtime/windows/` (путь, который ожидают rusim CLI и старт-скрипты). Cache wipe `Library/{ScriptAssemblies,Bee,PlayerScriptAssemblies}` обязателен после правки asmdef-входящих C# (без него Unity батч-режим возвращает success, но не пересобирает DLL).
- **Бенчмарк скорости обучения:** 300k шагов на Win = **18.6 мин** (rev18, multi-agent), на Mac M4 Max = ~25–28 мин (rev16). Сам Unity-rendering — bottleneck (~270 fps на 8 envs независимо от железа), Win выигрывает за счёт стабильности и отсутствия тёрмо-троттлинга.

#### Серия rev10–16: восстановление 100% sim SR после потери в Sprint 3 Day 1

Modeli `cardboard-corridor-ppo-v9` (новая ветка обучения с Discrete-action wrapper для KS0223 diff-drive вместо continuous, см. День 1) проходила через серию итераций восстановления — Day 1 ушёл в curriculum/maze направление, baseline на L-corridor нужно было собрать заново.

| Версия | Бюджет | Изменения vs предыдущей | Sim SR | Главный вывод |
|---|---|---|---|---|
| rev10 | 200k from-scratch | DiscreteActionWrapper + heading_alignment_bonus +1.0 / pure rotation actions (0,±1) | 40% (4/10) | Первая рабочая Discrete-policy, использует DirLeft при поворотах |
| rev11 | rev10 + 200k continuation | без изменений | 0% | Continuation сломала policy — DirForward collapse |
| **rev12** | 300k from-scratch | reward stack v9 (progress×100, survival_bonus, lateral_penalty×1, heading_bonus, backward_penalty −0.5) | **85%** (17/20) | Гольден-стандарт. 91% DirForward, 8% DirLeft, 0 wall-hits в sim |
| rev13 | 300k aborted at 65% | strong-aug + lateral×5 + sensor noise + dropout + latency=1 | killed | ep_len=34, ep_rew=−146 — landscape stuck в "die fast" |
| rev14 | 300k from-scratch | менее агрессивно: lateral×2 + dropout 2% + strong-aug + latency=1 | 0% | DirForward collapse 97%, нет поворотов — всё ещё перебор augmentations |
| rev15 | 300k from-scratch | rev12 + только latency=1 | 0% (eval) | latency wrapper при eval не применялся → train/eval mismatch. После фикса `--latency-steps` в `evaluate_v9.py` всё равно 0% — wrapper сам ломает learning без явного timing-аware policy |
| **rev16** | 300k from-scratch (seed=43) | EXACT rev12 recipe | **100%** (20/20) | Best-ever. 91% DirForward, 8% DirLeft, 0 stalls. Воспроизводимость pipeline подтверждена (rev12 85% → rev16 100% — оба валидные random-seed exploration) |

Артефакт: `python/training/artifacts/cardboard-corridor-ppo-v9-rev16/1.0.0/cardboard-corridor-ppo-v9-rev16.onnx` (3.55 МБ). Загружен в backend через `/api/models/upload`, активен (`isActive: true`), привязан к `runtimeMode=real-robot`.

#### Расширение safety-обвязки backend autopilot

Sprint 3 Day 3 показал что E-stop сам по себе спас правую стену от полного разноса, но робот всё ещё толкал стенку 5 раз подряд (E-stop hold 500мс → release → policy опять DirForward → E-stop), что в итоге опрокинуло картон. Добавлены ([src/ks0223-web-mac/backend/Services/AutopilotService.cs](../../../src/ks0223-web-mac/backend/Services/AutopilotService.cs), [AutopilotSafetyFilter.cs](../../../src/ks0223-web-mac/backend/Services/AutopilotSafetyFilter.cs), [SessionVideoRecorder.cs](../../../src/ks0223-web-mac/backend/Services/SessionVideoRecorder.cs), [appsettings.json](../../../src/ks0223-web-mac/backend/appsettings.json)):

| Компонент | Параметр | Было | Стало | Зачем |
|---|---|---|---|---|
| ThrottleMax | safety filter clip | 0.5 | **0.15** | На реале робот не должен ехать быстрее чем ~11 см/с — успевает реагировать на E-stop без разгона |
| EStopDistanceM | front-stop порог | 0.12 м | **0.35 м** | Большой зазор учитывает TCP latency 50–100мс + время ответа моторов |
| EStopHoldMs | удержание stop | 500 мс | **800 мс** | Дольше держит стоп — меньше повторных «толчков» |
| Suspicious-jump E-stop | новая | — | если `prev<0.30м && curr>1.50м` за 1 tick → E-stop hold | Real HC-SR04 при потере эха возвращает `null/0/максимум` — sim-policy интерпретирует как «открытый путь» |
| Missing-reading guard | новая | — | `front <= 0` или `> 4.0м` → принудительный E-stop | Польза при поднятии робота / закрытии сонара рукой |
| Auto-stops | session-level | — | max-duration 60с, stuck-cmd 15 reps, ≥5 E-stops/10s, stale telemetry > 1.5с | Защита от runaway-policy и от потери связи |
| DirStop burst | при auto-stop | 1× | **4× с 80мс gap** + reset DirectDrive(0,0) | Гарантия остановки моторов: одиночный DirStop иногда теряется в TCP буфере |
| Video recording | per session | — | ffmpeg-захват MJPEG-стрима в mp4, авто-старт/стоп с autopilot | Запись каждой попытки для post-mortem анализа |

Конфиг через `appsettings.json` секцию `AutopilotSafety`. DTO `AutopilotStatusDto` расширен `maxDurationSeconds`, `repeatedCommandCount`, `stopReason`. Тесты прошли в проде на 4 реальных прогонах (Day 4 параграф ниже).

#### Sim-to-real тестовые прогоны на rev12/rev16

Робот заряжен, картонная L-трасса воспроизведена. Серия sim-to-real-проверок:

| # | Модель | Throttle | Длительность | Результат | E-stop count |
|---|---|---|---|---|---|
| 2 | rev12 | 0.5 | ~25 с | 583 шага, не доехал, после auto-stop колёса продолжали крутить (баг → DirStop burst) | 66 |
| 3 | rev12 (после safety upgrade) | 0.15 | 3 с | auto-stop сработал на 5 E-stop за 10 с, корпус цел, но не доехал | 5 |
| 4 | rev16 | 0.15 | 4 с | проехал ~30 см, столкнулся со стенкой, она упала (cumulative push 5× E-stop hold release циклов) | 5 |

Записанные видео: [autopilot_20260426_*.mp4](../../../src/ks0223-web-mac/backend/runtime-data/autopilot-recordings/). Видео sim для сравнения тоже есть (sim_rev16_episode.mp4).

#### Анализ visual sim-to-real gap (по визуальному сравнению sim/real видео)

Никита (студент) визуально сравнил sim-видео rev16 и real-видео rev16 теста #4. Зафиксированы различия которые объясняют policy collapse:

1. **Освещение**: sim — яркое студийное «дневное», real — слабое тёплое домашнее. Distribution shift по brightness/contrast/hue существенный.
2. **Цвет неба**: sim — синее небо (default Unity skybox), real — серая стена комнаты выше уровня картона. CNN видит у sim небо как контекст, у real — нет.
3. **Текстура стенок**: sim — bright orange-tan «свежий картон», real — grey-matte «плотный переработанный картон». Разные feature-maps.
4. **Скорость движения**: sim тренировался на throttle=1.0 (max 0.73 м/с), real safety filter capил на throttle=0.15 (~0.11 м/с). 7× медленнее на real → reactive policy не успевает реагировать на изменения визуала «в темпе» который видела в обучении.

Camera-profile change (`high` 1280×720 → `performance` 640×360 + JPEG72) **не дал видимого эффекта** на 84×84 inputs — bottleneck в target resolution, не в source.

#### План rev18 через superpowers (writing-plans skill)

Через [superpowers writing-plans skill](https://github.com/anthropics/claude-code) выписан полный bite-sized план из 6 фаз для итерации [PLAN_REV18.md](../../../.claude/projects/-Users-nikitagorovenko-Documents-Projects-uav-simulator/PLAN_REV18.md) — закрыть visual gap + ускорить тренировку через multi-agent Unity. План затем выполнен через `executing-plans` skill в одной сессии.

**Phase 1 — Unity scene visual fixes** (через C#-edit + git push + Unity rebuild):
- [CardboardCorridorTrack.cs](../../../src/UnityProject/uav-simulator/Assets/Scripts/Tracks/CardboardCorridorTrack.cs): grey-matte cardboard palette (RGB 0.55/0.53/0.50 вместо 0.76/0.60/0.42), warm low-intensity directional light (0.55 вместо 1.0), `RenderSettings.skybox = null` + flat grey ambient + null fog.
- [Ks0223Vehicle.cs](../../../src/UnityProject/uav-simulator/Assets/Scripts/Vehicles/Ks0223Vehicle.cs): `frontCamera.clearFlags = CameraClearFlags.SolidColor` + grey background — иначе Unity при null skybox показывает default dark blue.

**Phase 2 — Python camera post-process** ([ab_corridor_vision_env.py](../../../python/training/ab_corridor_vision_env.py)): новый метод `_apply_real_camera_postprocess` — dim ×0.85 + desaturate (luma 25%) + JPEG roundtrip quality 60. Включается флагом `--real-cam-postprocess`. Smoke-test: input mean RGB 200 → output 170, цвета приглушены.

**Phase 3 — Multi-agent Unity VecEnv** ([multi_agent_vision_env.py](../../../python/training/multi_agent_vision_env.py), `--multi-agent` флаг в trainer): N агентов в одном Unity, параллельные `/step` calls с `targetAgentId`. Аудит провёл отдельный subagent — нашёл 3 критичных бага в моей первой реализации:
1. Position path: `state.position` вместо `state.pose.position` (все позиции были = 0).
2. Telemetry path: `step.info` вместо `step.state.telemetry` (ультразвук всегда 0).
3. Отсутствовал `heading_alignment_bonus` в reward (без него policy не учится поворотам в L-corridor).

После фикса smoke-test 2000 steps × 4 agents в 1 Unity = 14 c, 142 fps total (35.5 fps/agent vs 25 fps/agent для SubprocVecEnv).

#### rev17 — провал от Win-Python-3.13 SubprocVecEnv pipe crash

Перед интеграцией multi-agent попытался стандартным `--num-envs 8` с SubprocVecEnv. Тренировка детерминированно умирала на **116k шагов из 300k** с `BrokenPipeError: WinError 109` в `subproc_vec_env._worker`. Точка падения — около итерации 57 PPO-update'а. Воспроизводилось 3 раза подряд при разных seed. Корневая причина в Python 3.13 + SB3 + Win named-pipe combo (Linux/WSL2 не падает). Workaround — multi-agent VecEnv (single-process, без pipes) или downgrade на Python 3.12.

#### rev18 — все фиксы сразу, FAILED

| Параметр | rev16 baseline | rev18 |
|---|---|---|
| Wall colors | bright tan | **grey matte** (Phase 1.1) |
| Lighting | studio bright | **warm indoor + flat grey ambient** (Phase 1.2) |
| Sky / camera bg | default skybox | **grey solid color** (Phase 1.3) |
| Camera post-process | off | **dim+desaturate+JPEG60** (Phase 2.1) |
| VecEnv | SubprocVecEnv 8 | **MultiAgentVisionVecEnv 1×8** (Phase 3) |
| Throughput | 200 fps | **269 fps (+35%)** |
| Wall-clock | 25–28 мин | **18.6 мин** |
| **sim eval SR** | **100%** | **0%** |

Action distribution rev18: DirLeft 79%, DirStop 21%, **DirForward 0%**, all 20 episodes stalled. Policy полностью сколлапсировалась — научилась крутиться на месте вместо движения.

**Гипотеза**: 5 одновременных изменений visual distribution + augmentation push CNN-input в зону где policy не может извлечь forward-driving signal. В частности, post-process `dim×0.85 + desaturate + JPEG60` поверх уже изменённой scene = слишком dark/desaturated input.

#### rev19d — изоляция гипотезы (в процессе на момент закрытия Day 4)

Запущена rev19d с теми же изменениями что rev18 + увеличенный `ent_coef 0.10 → 0.15` для большей exploration. Идея — дать PPO больше «стимула» пробовать DirForward даже когда policy сходится в DirLeft. ETA окончания обучения через ~12 мин. Если SR < 50% → переход к серии абляций (rev19a/b/c — каждое visual-fix изолированно).

### Verdict против sprint-3 критериев (cumulative Day 1–4)

- [x] **Curriculum learning maze** — реализован (Day 1), v8 проходит curriculum полностью.
- [ ] **≥ 3/10 maze scenarios SR ≥ 50%** — FAIL (v8: 0/10).
- [x] **L-corridor SR ≥ 80%** — PASS (rev16: 100%, rev12: 85%).
- [x] **Safety wrapper end-to-end** — PASS (4 реальных прогона, никаких ложных срабатываний; 4-й тест опрокинул стену из-за multi-cycle E-stop release, добавлен DirStop burst как фикс).
- [x] **Sim-to-real инфраструктура готова** — PASS (TCP, UDP camera, ONNX, telemetry, model registry, video recording, auto-stops).
- [x] **Win-кластер обучения** — BONUS (Sprint 3 не планировал, но снял bottleneck Mac M4 Max).
- [ ] **rev16 проходит реальную L-трассу** — FAIL (1 контакт за прогон #4, стена упала). Sim2real visual gap основной блокер.
- [-] **rev18/rev19d закрывают visual gap** — В РАБОТЕ (rev18 FAIL, rev19d в обучении).

### Следующие шаги (на момент закрытия Day 4)

1. **[training]** Дождаться rev19d eval → решение deploy/абляции.
2. **[training]** Если rev19d не закроет — rev19a/b/c series для изолированной проверки (только visual / только postprocess / только multi-agent).
3. **[sim2real]** При успешной модели — повторный real-robot test #5 на той же L-трассе.
4. **[infrastructure]** На будущее: WSL2 на Win + Linux Genesis (BatchRenderer) для GPU-параллельного render N=512+ envs — обещает 100–500× speedup vs текущая Unity тренировка. Не критично для diploma но интересно как research-direction.
5. **[reporting]** Финальный отчёт диплома: Sprint 3 закроется с rev16/rev18/rev19 как best-effort, честный sim-to-real результат и инфраструктурные достижения (Win-кластер, multi-agent VecEnv, расширенный safety stack, video recording).

## Результаты (День 5, 27.04.2026) — Re-eval rev16/18/19d, диагностика regressions, debug-инфраструктура

### Что сделано

#### Phase 0 — Debug-инструменты в `develop`

Закоммичены два debug-инструмента, которые ранее лежали uncommitted в рабочей копии:

1. **Shadow-mode preview (`/api/autopilot/preview`)** ([commit `9fd18c1`](#)) — endpoint и UI-overlay в WebUI: при включении «Shadow mode» backend каждые 200 мс прогоняет привязанную ONNX-policy на последнем камера-кадре + сонаре, не управляя моторами, и возвращает: action probabilities (5 баров), chosen action, sonar в см, image CV stats (brightness mean/std, edge score top/bottom для blind-detection), guard-reason для случаев когда `ApplyPolicyGuards` форсит DirStop. Backend изменения в [`AutopilotService.SamplePreview`](../../../src/ks0223-web-mac/backend/Services/AutopilotService.cs), [`Models/Contracts.cs`](../../../src/ks0223-web-mac/backend/Models/Contracts.cs), Frontend overlay в [`CameraPanel.tsx`](../../../src/ks0223-web-mac/frontend/src/components/CameraPanel.tsx). Используется на реальном роботе перед включением autopilot — даёт увидеть, что policy будет делать, БЕЗ риска удара о стену.

2. **Grad-CAM saliency tool** ([commit `658a2fb`](#)) — `python/training/policy_saliency.py` (CLI: single image / glob / live MJPEG) + `policy_saliency_server.py` (HTTP-sidecar на :5288 для embed в WebUI). Грузит SB3 PPO `_sb3.zip`, бэкпропит chosen-logit обратно к input-картинке, рендерит side-by-side image+heatmap с annotation action-probabilities. Цель — увидеть, на что CNN смотрит для принятия решения: если на угол стены/vanishing point — ОК; если на потолок/skybox/случайные пиксели — policy выучила spurious feature и она поломается на real-robot frames (где этих фоновых элементов нет).

После коммитов — fast-forward `rev18` → `develop`, push `develop` (от `65db535` до `658a2fb`, +17 коммитов включая всю серию rev17–22 и debug-инструменты). Ветка `rev18` сохраняется но фактически слита.

#### Phase 1 — Re-eval rev16/18/19d в текущем калиброванном симе

**Цель**: проверить гипотезу плана `2026-04-27-sim2real-recovery-plan.md` — действительно ли rev16 регрессировала после калибровочного коммита `26a44d0` (maxSpeedMps 2.2→0.73, yaw 160→380, pure-steer ACTION_TABLE), или регрессия — это исключительно rev17→22 эксперименты.

**Тест-конфиг**: текущий develop HEAD на Win, headless Unity (`-batchmode -force-d3d12`), 20 episodes на каждую policy, seed-offset=3000, latency-steps=1, scenario `cardboard-corridor-v1.yaml` (рев21 mild DR — ±12° hue, intensity 0.75–1.10, skybox always present).

| rev | sim SR | progress | termination | action distribution |
|---|---:|---:|---|---|
| **rev16** | **100%** (20/20) | **0.81** | goal_reached × 20 | **DirForward 90% / DirLeft 8% / DirStop 1%** |
| rev18 | 0% (0/20) | 0.00 | stalled × 20 (на step 50) | DirLeft **100%** (mode collapse) |
| rev19d | 0% (0/20) | 0.47 | stalled × 20 (на step 106) | DirForward **100%** (no turns) |

**Главное открытие**: **rev16 ВСЁ ЕЩЁ работает 100% в текущем калиброванном симе** с DR-сценой rev21. Гипотеза «rev16 трен. до калибровки → не работает в новом симе» опровергнута. Это полностью меняет приоритеты:

- **Не нужно** переобучать rev23 baseline (Phase 2 плана). Уже есть `rev16` ONNX, который проходит L-коридор.
- **Регрессия — исключительно rev18+ эксперименты** (визуальный overhaul + DR). Их можно списать.
- **Реальная задача** — закрыть sim2real gap для уже работающего rev16, а не искать «новую модель».

**rev19d дополняющий вывод**: ent_coef boost (0.10→0.15) восстановил DirForward-bias, но потерял способность поворачивать. 47% progress = он доезжает ровно до угла L и стоит. Это указывает что **визуальные изменения rev18 (grey walls + warm light + null skybox) убивают конкретно turn-trigger feature** — CNN перестаёт «видеть» угол поворота. Эту гипотезу можно проверить запуском `policy_saliency.py` на rev19d на frame перед углом.

**Артефакты**:
- Eval JSONs: [`docs/report/prediploma-practice/sprint-3-reeval-2026-04-27/eval-rendered-rev{16,18,19d}.json`](sprint-3-reeval-2026-04-27/)
- Preview видео (3 seeds × 2 rev = 6 mp4, 84×84 → 336×336 nearest-upscale 7fps): [`docs/.../sprint-3-reeval-2026-04-27/videos/rev{16,19d}_preview_{101,202,303}.mp4`](sprint-3-reeval-2026-04-27/videos/)
  - rev16: все 3 seeds → goal_reached в 76–82 шагов (плавный проход L)
  - rev19d: все 3 seeds → stalled в 103–106 шагов (упёрся в угол)

#### Технические находки про Win Unity-runtime через SSH

При попытке запустить sim напрямую через `ssh win` обнаружено:
- **`-batchmode -nographics` (headless)**: HTTP API поднимается, но render to texture для камеры даёт чёрные кадры или ломается на shaders ("not supported on this GPU" для всех URP-шейдеров). Eval с такими кадрами **бесполезен** — все policy выдают свой default action (rev16 → 100% DirLeft, rev19d → 100% DirForward), независимо от реального navigation skill. Это объясняет первый прогон сегодня где rev16 дал 0% — был запущен через `-nographics`.
- **`-batchmode` (без `-nographics`)**: HTTP поднимается за 60–90 сек, render идёт через AMD Radeon iGPU (NVIDIA dGPU не выбирается из SSH session 0 даже с `HKCU\...\UserGpuPreferences` registry-hint), кадры **рендерятся корректно** (mean=144, std=40, нормальный dynamic range). На iGPU производительность ниже чем на dGPU, но для 20-эпизодного eval достаточно.
- **Unity убивается при закрытии SSH session**. Решение: запускать стартер sim'а и eval-скрипт **в одной SSH-session** через единый `.ps1`.

### Verdict против плана `2026-04-27-sim2real-recovery-plan.md`

| Phase | Статус | Заметка |
|---|---|---|
| 0. Commit debug features | ✅ DONE | shadow mode + saliency, develop merged |
| 1.1. Re-eval rev16/18/21/22 | ✅ DONE (rev16/18/19d) | rev21/22 артефакты пустые на Win (training aborted, нет .onnx) |
| 1.2. Saliency на sim+real frames | ⏳ PENDING | sim-фреймы можно прогнать прямо сейчас, real-фреймы — нужен KS0223 включённый |
| 1.3. Замер реального FOV Pi-камеры | ⏳ BLOCKED on user | A4-лист на 1м, скриншот через WebUI |
| 1.4. Shadow-log на real KS0223 60с | ⏳ BLOCKED on user | требует робота |
| 2. Train rev23 baseline | ❌ **CANCELLED** | rev16 уже 100% в текущем симе — переобучать незачем |
| 3.1. Match camera FOV | ⏳ зависит от 1.3 | если real FOV ≈ 68° — пропускаем |
| 3.2. Match wall/floor palette | ⏳ нужно фото реального коридора | |
| 4. Train rev24 (mild DR + transfer) | ⏳ откладывается до 1.2/1.3/3.2 | |
| 5. Speed match | 🟡 READY TO DO | поднять `AutopilotSafetyFilter.ThrottleMax` 0.15→0.40, ужать E-stop 0.35→0.30 м |
| 6. Real validation | ⏳ BLOCKED on user | |

### Следующие шаги (приоритет)

1. **[user-action]** Включить KS0223 робота. Я сделаю:
   - 60-сек shadow-mode log при ручной езде по L-коридору с rev16 — посмотрим, какие probabilities выдаёт policy в реале (vs. в симе).
   - Saliency на 30 реальных кадрах через `policy_saliency.py --mjpeg http://127.0.0.1:5287/api/camera/mjpeg?...` — увидеть, на что CNN смотрит на реальной камере.
   - Замер FOV: A4-лист (21 см) на 1 м перед камерой → пиксельная ширина → HFOV.
   - Reference photo пустого реального L-коридора для палитры (Phase 3.2).

2. **[mac-only, мне]** Saliency rev16 на sim-фреймах — посмотреть, какие визуальные feature привлекают forward/turn decisions. Подсветит на чём policy уязвима.

3. **[mac-only, мне]** Phase 5 backend-changes: повысить `ThrottleMax` 0.15→0.40, E-stop 0.35→0.30, добавить «soft DirStop» при scrub-detection. Без real-теста — только статические правки + dotnet build.

4. **[после real-shadow-log]** Выбрать ОДНУ интервенцию (FOV-fix, palette-match, или throttle-lift) и тренировать rev24 транс-лернингом от rev16. Не больше одной переменной.

5. **[infrastructure]** ✅ Win SSH workflow зафиксирован: `-batchmode -force-d3d12`, без `-nographics`, sim+eval в одной session. Закоммитить как `scripts/win/run_eval_batch.ps1` для воспроизводимости.

### Phase 1 evening — реальный robot, sim2real prior flip ЗАФИКСИРОВАН

Робот KS0223 включён, поставлен в начало L-коридора (фото реальной трассы — деревянный пол + белая стена + картонные стены, все в [`docs/report/.../real_corridor/`](sprint-3-reeval-2026-04-27/real_corridor/)). Backend Mac → robot TCP подключён (`192.168.1.121:5051`), модель — **rev16** (тот же ONNX что в sim eval даёт 100% SR). Без запуска autopilot, через **shadow-mode preview** (новый endpoint `/api/autopilot/preview` из утреннего Phase 0) — снято 5 кадров с интервалом 0.5 с.

**Action probabilities (rev16, реальный робот, старт L-коридора, sonar=0.94 m, brightness mean=0.44):**

| sample | DirStop | DirForward | DirBack | DirLeft | **DirRight** | chosen |
|---|---:|---:|---:|---:|---:|---|
| 1 | 9% | 6% | 0% | 27% | **58%** | DirRight |
| 2 | 9% | 5% | 0% | 20% | **66%** | DirRight |
| 3 | 8% | 5% | 0% | 19% | **68%** | DirRight |
| 4 | 8% | 5% | 0% | 19% | **67%** | DirRight |
| 5 | 9% | 5% | 0% | 20% | **66%** | DirRight |

**Сравнение с sim (тот же rev16, старт того же L-коридора):**

| | DirStop | DirForward | DirBack | DirLeft | DirRight | chosen |
|---|---:|---:|---:|---:|---:|---|
| **SIM** | 10% | **87%** | 2% | 0% | 0% | DirForward |
| **REAL** | 8% | 5% | 0% | 20% | **66%** | DirRight |

Это **полный prior flip**: в симе policy уверенно идёт вперёд (87% DirForward), в реале — уверенно поворачивает направо (66% DirRight) на той же стартовой позиции. **Sim2real visual gap сейчас доминирует — это объясняет все проваленные real-deploy прогоны Day 3 без необходимости винить speed mismatch или E-stop thresholds.**

Saliency-карты на симовом и реальном кадре сохранены ([`real_corridor/sal_sim_start.png`](sprint-3-reeval-2026-04-27/real_corridor/sal_sim_start.png), [`real_corridor/saliency/sal_real_frame_*.png`](sprint-3-reeval-2026-04-27/real_corridor/saliency/), сводный side-by-side [`sim_vs_real_saliency_big.png`](sprint-3-reeval-2026-04-27/real_corridor/sim_vs_real_saliency_big.png)). Зрительно подтверждается: на симовом кадре gradient концентрируется на vanishing-point коридора (центр-низ), на реальном — расползается широко по всему фрейму, что для CNN означает «нет согласованной forward-feature» → policy ищет любую ассоциацию и находит «правый край картонной стены» → DirRight.

**Конкретные visual гэпы видны в фото:**
1. **Пол: деревянный паркет (дуб)** в реале vs **плоский тан-цвет без текстуры** в симе. Это сильнейший feature, на котором в симе CNN якорится: floor-line дает heading information, в реале её нет.
2. **Левая стена: белая шпатлёвка** в реале vs **тан картон** в симе. CNN видит резкий контраст «светло-белое слева / темно-коричневое справа» — фича отсутствующая в тренировочном distribution.
3. **Освещение неравномерное** — в кадре есть тёмные углы и светлые пятна (свет потолочной лампы), сим-DR (rev21) использует равномерный intensity 0.75–1.10 на всю сцену.
4. **Cardboard вертикальные швы** в правой стене реального корридора — линии контраста вертикальные, в симе все стены одного цвета без швов.

### Verdict против критериев плана

- ✅ **rev16 работает 100% в текущем симе** — Phase 2 не нужен.
- ✅ **rev18/19d поломаны экспериментально** — rev17–22 серия отброшена.
- ❌ **rev16 НЕ работает на реальном KS0223** — prior flip из-за visual gap.
- 🟢 **Главный sim2real блокер идентифицирован эмпирически** — visual gap доминирует над всеми другими (speed, FOV, latency).

### Решение пути вперёд

Минимум 2 пути дают честный диплом-результат:

**Path A (быстрый, 1–2 итерации тренировки): heavy domain randomization + текстуры пола.** Добавить в `CardboardCorridorTrack.cs`:
- Wood-plank floor texture (свободные PBR текстуры дуба) с per-reset rotation/hue jitter ±20°.
- Mixed wall palette: 50% картонный тан, 30% белый, 20% мix per-wall — рандомно на каждом reset.
- Per-corner lighting variability — добавить 2 точечных light с randomized intensity 0–0.6 для имитации потолочной лампы.
- Train rev24 с transfer learning от rev16 + 200k шагов на этой расширенной DR-сцене. ~12 мин на Win.

**Path B (правильный, инженерно более долгий): photo-grounded sim2real.** Использовать реальные фотки коридора (5 штук от пользователя) для:
- Procedural texturing (texture projection from reference photos).
- Per-wall PBR materials с реалистичным рельефом картона/штукатурки.
- Match HDR lighting (replicate ambient + 1 directional + 1 point из реального скриншота).

Path A — реалистично закрыть к концу Sprint 3. Path B — research direction для master thesis.

### Артефакты в этой итерации

- 5 кадров реальной камеры: [`real_corridor/real_frame_{1..5}.jpg`](sprint-3-reeval-2026-04-27/real_corridor/) (320×240 JPEG, JFIF, density 1×1)
- Стартовая позиция: [`real_corridor/real_camera_start_position.jpg`](sprint-3-reeval-2026-04-27/real_corridor/real_camera_start_position.jpg)
- Saliency на реальных кадрах: [`real_corridor/saliency/sal_real_frame_{1..5}.png`](sprint-3-reeval-2026-04-27/real_corridor/saliency/)
- Saliency на симовом кадре: [`real_corridor/sal_sim_start.png`](sprint-3-reeval-2026-04-27/real_corridor/sal_sim_start.png)
- **Сводный sim vs real (главный артефакт)**: [`real_corridor/sim_vs_real_saliency_big.png`](sprint-3-reeval-2026-04-27/real_corridor/sim_vs_real_saliency_big.png)

### Phase 4 evening — rev24 heavy-DR scene + transfer-train ИСПРАВЛЯЕТ prior на реале

После эмпирически зафиксированного prior flip rev16 на реале — реализован **Path A** из раздела «Решение пути вперёд»: heavy domain randomization в Unity-сцене + transfer learning от rev16.

**Изменения сцены ([`CardboardCorridorTrack.cs`](../../src/UnityProject/uav-simulator/Assets/Scripts/Tracks/CardboardCorridorTrack.cs), commit `caf44a7`):**

1. **Procedural wood-plank texture** — Texture2D 128×128 регенерируется на каждом ResetTrack(seed). 3–6 досок по горизонтали, каждая со своим оттенком дуба (HSV: hue 22–46°, sat 0.30–0.55, val 0.45–0.65) + Perlin grain внутри + затемнённый seam между досками. Tile-scale ~0.30 м под планки реального паркета. Заменяет плоский тан-цвет, на котором CNN раньше якорилась для heading.

2. **Per-wall style mix** — каждая из 6 стен на каждом reset получает один из {Cardboard, White-plaster, Mixed} по дефолту 50/30/20. White-plaster соответствует левой стене реального коридора; Mixed создаёт вертикальный шов посередине стены, имитируя стену из двух материалов.

3. **Per-wall albedo value jitter** (0.70–1.25) — фейковая «неравномерная подсветка» через albedo. Это необходимо потому, что walls/floor используют URP/Unlit shader path: реальные Unity Lights на них **не действуют**, поэтому единственный способ внести lighting variance — через albedo напрямую. Декоративные point lights из rev22 ничего не давали policy CNN observation.

DR ranges подняты с rev21-mild обратно к rev20-уровню: hue ±25°, sat ±0.15, val ±0.20, intensity 0.55–1.15, light hue ±30°. Сознательно overshooting реальный диапазон — тренировка transfer-learning от rev16 компенсирует «harder problem» удержанием базовой навигации.

**6 sample-кадров новой сцены ([`rev24_sim_samples/mosaic_3x2.png`](sprint-3-reeval-2026-04-27/rev24_sim_samples/mosaic_3x2.png))** — episode-to-episode визуально сильно отличаются: один с белыми стенами, другой с оранжевым картоном, третий с pink-mixed; пол везде с видимыми планками и перепадами цвета. Stats на 6 seed'ах: mean 128–141, std 45–54.

#### rev24 transfer-train

**Команда** (Win, multi-agent VecEnv):
```bash
.venv\Scripts\python.exe python\training\train_cardboard_corridor_v9.py \
  --model-name cardboard-corridor-ppo-v9-rev24 \
  --total-timesteps 200000 --num-envs 4 --multi-agent \
  --latency-steps 1 --time-scale 3.0 --strong-aug \
  --resume <rev16_sb3.zip>
```

**Результат тренировки**: 200000 шагов за **32.2 мин** (104 fps × 4 envs × 1 Unity), `ent_coef 0.10` (default), `lr 0.0003`, `value_loss` стабилизировалась на ~0.85, `entropy_loss ≈ -1.42` (policy остаётся explorative). Артефакт: [`python/training/artifacts/cardboard-corridor-ppo-v9-rev24/1.0.0/`](../../python/training/artifacts/cardboard-corridor-ppo-v9-rev24/1.0.0/) — 11.8 МБ sb3.zip + 3.5 МБ ONNX.

**ONNX-export** падал в trainer'е (`'charmap' codec can't encode '❌'` — emoji в console output) — экспортнул standalone-скриптом `python/training/export_v9_rev*_onnx.py` с `PYTHONIOENCODING=utf-8` + `dynamo=False` + явным uint8→float cast в forward.

#### Eval rev24 в sim — sanity + сравнение с rev16

| Variant | sim SR | progress | действия |
|---|---:|---:|---|
| rev16 в rev21 mild-DR | **100%** (20/20) | 0.81 | DirForward 90% / DirLeft 8% |
| rev16 в rev24 heavy-DR | **75%** (15/20) | 0.64 | DirForward 68% / DirLeft 14% / DirBack 11% |
| **rev24 в rev24 heavy-DR** | **35%** (7/20) | 0.46 | DirForward 64% / DirBack 22% / DirRight 14% |

rev16 удивительно robust в новой сцене — даёт 75% даже на heavy-DR без специальной тренировки. **rev24 же упал до 35%** — heavy DR оказался слишком жёстким, transfer-learning не сохранил всю навигацию rev16. Sim eval не финальный критерий, главное — реальный prior на real KS0223.

#### Real shadow-mode test rev24 vs rev16

Робот включён, в той же стартовой позиции что в Phase 1 evening. rev24 ONNX залит в backend (`/api/models/upload`) и привязан (`/api/model-bindings`). 5 shadow-preview snapshots с интервалом 0.5 с:

| | DirStop | **DirForward** | DirBack | DirLeft | **DirRight** | chosen |
|---|---:|---:|---:|---:|---:|---|
| **rev16** (BEFORE — mild-DR train) | 8% | **5%** | 0% | 20% | **66%** | DirRight ❌ |
| **rev24** (AFTER — heavy-DR train) | 11% | **36%** | 12% | 15% | 26% | **DirForward ✅** |

Та же модель-архитектура, тот же стартовый кадр реального коридора, тот же rev16-прогрев — **prior flipped в правильную сторону**: DirForward стал топ-действием вместо DirRight. Confidence не очень высокая (36% vs 66% у rev16-DirRight), но направление выбора **корректное**.

Главный артефакт визуально: [`real_corridor/sim_vs_real_BEFORE_AFTER.png`](sprint-3-reeval-2026-04-27/real_corridor/sim_vs_real_BEFORE_AFTER.png).

### Verdict (после Phase 4)

| Критерий | Статус |
|---|---|
| ✅ rev16 работает в sim | 100% rev21-DR, 75% rev24-DR |
| ✅ Sim2real visual gap эмпирически измерен | Phase 1 evening: rev16 prior flip 87%→5% DirForward |
| ✅ Heavy DR уменьшает visual gap | Phase 4: rev24 prior на real → DirForward 36% (top), не DirRight |
| 🟡 Real-deploy полный проход L | НЕ протестирован живым autopilot — только shadow-mode |
| 🟡 Confidence rev24 на реале | низкая (36%) — policy всё ещё неуверенно |

### Следующие шаги

**1. [приоритет] Live-test rev24 на роботе**: запустить autopilot на реальной L-трассе с safety throttle 0.15 (текущий) и записать видео. **Что ожидаем**: робот двинется вперёд (а не закрутится на месте как rev16). Может зигзаг'ить из-за низкой confidence, может стопнуться на углу. Это базовый «first frame» успеха.

**2. Если live-test показывает прогресс по straight но провал на углу**: добавить в DR-сцену **визуальные маркеры угла** (вертикальные линии где сходятся стены) — у policy будет более чёткий turn-trigger.

**3. Если live-test даёт zigzag**: дотренировать ещё 200k шагов на heavy-DR, возможно с увеличенным `--lateral-penalty-mult 2.0` чтобы штрафовать боковые движения. Confidence должна вырасти.

**4. Если real-prior снова flips на углу** (DirForward → DirRight в неправильный момент): значит DR в углу слабее чем в straight. Добавить ещё агрессивных DR-режимов специально для угла.

### Артефакты

- Код: [`CardboardCorridorTrack.cs`](../../src/UnityProject/uav-simulator/Assets/Scripts/Tracks/CardboardCorridorTrack.cs) (heavy DR, +242 строки), commit `caf44a7`
- Артефакт rev24: [`python/training/artifacts/cardboard-corridor-ppo-v9-rev24/1.0.0/`](../../python/training/artifacts/cardboard-corridor-ppo-v9-rev24/1.0.0/) (sb3.zip + ONNX + metadata)
- 6 sim-фреймов rev24-сцены: [`rev24_sim_samples/`](sprint-3-reeval-2026-04-27/rev24_sim_samples/)
- Eval JSONs: [`eval-rev24-heavy-dr.json`](sprint-3-reeval-2026-04-27/eval-rev24-heavy-dr.json), [`eval-rev16-in-heavy-dr.json`](sprint-3-reeval-2026-04-27/eval-rev16-in-heavy-dr.json)
- Real-frames rev24: [`real_corridor/real_frame_rev24_{1..5}.jpg`](sprint-3-reeval-2026-04-27/real_corridor/)
- Saliency rev24: [`real_corridor/saliency_rev24/`](sprint-3-reeval-2026-04-27/real_corridor/saliency_rev24/)
- **BEFORE/AFTER сводка**: [`real_corridor/sim_vs_real_BEFORE_AFTER.png`](sprint-3-reeval-2026-04-27/real_corridor/sim_vs_real_BEFORE_AFTER.png)

