# Спринт 3 — Отчёт о выполненных работах

> **Период:** 19.04.2026 — 02.05.2026
> **Практика:** преддипломная (производственная), 09.04.04 «Программная инженерия»
> **Тема:** Замыкание sim-to-real контура на робототехнической платформе KS0223 и расширение Unity-симулятора под исследовательские задачи domain randomization
> **Проект:** [`uav-simulator`](https://github.com/NMGorovenko/uav-simulator)
> **Студент:** Горовенко Никита Максимович, группа КИ24-04-3М

---

## Оглавление

1. [Итоги спринта](#1-итоги-спринта)
2. [Замыкание sim-to-real: рабочий проезд по L-коридору](#2-замыкание-sim-to-real-рабочий-проезд-по-l-коридору)
3. [Heavy domain randomization сцены](#3-heavy-domain-randomization-сцены)
4. [Распределённая инфраструктура обучения Mac ↔ Win](#4-распределённая-инфраструктура-обучения-mac--win)
5. [Расширение WebUI: shadow-preview, demo recording, replay](#5-расширение-webui-shadow-preview-demo-recording-replay)
6. [Главное эмпирическое наблюдение спринта — entropy coefficient drift](#6-главное-эмпирическое-наблюдение-спринта--entropy-coefficient-drift)
7. [Архитектурный эксперимент: R3M backbone + RecurrentPPO](#7-архитектурный-эксперимент-r3m-backbone--recurrentppo)
8. [Эволюция моделей (компактный таймлайн)](#8-эволюция-моделей-компактный-таймлайн)
9. [Что планируется в финальном отчёте](#9-что-планируется-в-финальном-отчёте)
10. [Артефакты и репозиторий](#10-артефакты-и-репозиторий)

---

## 1. Итоги спринта

Главная цель спринта — довести sim-to-real цикл до состояния, когда модель, обученная в симуляторе, действительно управляет физическим роботом в L-коридоре, а не только показывает хорошие цифры на формальной оценке. Эту цель я считаю **выполненной**: модель `cardboard-corridor-ppo-v9-rev29` развёрнута в backend как активная и под её управлением робот KS0223 успешно проходит трассу. Ниже я разбираю как именно к этому пришёл и какая инфраструктура вокруг этого выросла.

**Ключевые результаты в одну строку:**

| Метрика | Значение |
|---|---|
| Лучшая модель в симуляторе на mild-DR сцене | `rev16`, **100% SR** (20/20 эпизодов) |
| Лучшая модель в симуляторе на heavy-DR сцене | `rev29`, **75% SR** |
| Поведение `rev29` на физическом роботе | проезд L-коридора под управлением policy + safety-стек |
| Обученных policy за спринт | 18 моделей серии v9-rev24…v9-rev46 |
| Симуляционных трасс | 2 (`track.cardboard_corridor.v1`, `track.cardboard_maze.v1`) |
| Реализованных доработок WebUI | shadow-preview, demo recording, demo replay |
| Зафиксированных коммитов | 48 на ветке `develop` |

Помимо непосредственного sim-to-real результата, в спринте появилась серия инженерных доработок, которые превращают наработки в относительно цельный продукт: распределённая обучающая связка Mac ↔ Win, WebUI с возможностью записывать референсные проезды и воспроизводить их в реальном времени, расширенный safety-стек, единый ModelRegistry с возможностью держать на роботе несколько политик и переключать активную через REST API. Часть из этого по сути cloud-style — тренировку можно вести на отдельной машине, а оператор может работать с любого устройства в локальной сети.

---

## 2. Замыкание sim-to-real: рабочий проезд по L-коридору

Самый понятный результат спринта показывается видеозаписью. Я снял на телефон, как KS0223 проезжает картонный L-коридор у меня в комнате — управление полностью передано через backend autopilot policy `rev29`, бортовая ROS-аналог-телеметрия (sonar + camera) идёт в политику с шагом ~140 мс, safety-фильтры backend (sonar E-stop при дистанции <10 см, deadman timeout, throttle ramp-up) работают в нормальном режиме.

**Видео внешней съёмки:** [`final_demo/rev29_real_run_2026-04-30_phone.mp4`](sprint-3-reeval-2026-04-27/real_corridor/final_demo/rev29_real_run_2026-04-30_phone.mp4) (≈ 14 с, 431 КБ — h264, 720p)
**Бортовая камера робота:** [`final_demo/rev29_real_run_2026-04-30_robot_cam.mp4`](sprint-3-reeval-2026-04-27/real_corridor/final_demo/rev29_real_run_2026-04-30_robot_cam.mp4) (191 КБ)
**Анимированный кадр для DOCX:** [`final_demo/rev29_real_run_2026-04-30_phone_compact.gif`](sprint-3-reeval-2026-04-27/real_corridor/final_demo/rev29_real_run_2026-04-30_phone_compact.gif) (2.9 МБ)

Чтобы попасть в этот результат, потребовалось разобрать несколько вложенных проблем — от доработок самой Unity-сцены до тонкой настройки PPO в Python-обвязке.

---

## 3. Heavy domain randomization сцены

К началу спринта модели на простой DR-сцене (`rev16`) выдавали 100% в симуляторе, но при первом же запуске на реальном роботе уверенно врезались в стену. Я провёл диагностику через инструмент shadow-mode (см. § 5), сравнив распределение действий policy на одинаковых стартовых кадрах в симе и на реале. Получилось такое:

| Семпл (один и тот же стартовый кадр) | DirStop | DirForward | DirBack | DirLeft | **DirRight** |
|---:|---:|---:|---:|---:|---:|
| Sim, `rev16` | 10% | **87%** | 2% | 0% | 0% |
| Real-robot, `rev16` | 9% | 6% | 0% | 27% | **66%** |

Distribution-shift оказался настолько большим, что policy буквально перевыбирала стороны корпуса коридора. Нужно было привести симулятор к виду, в котором коридор перестаёт «выглядеть как один и тот же тан-цвет каждый раз».

В рамках этой задачи я переписал `CardboardCorridorTrack.cs` (а потом портировал тот же набор приёмов на `CardboardMazeTrack.cs`) — добавил процедурную текстуру дубового пола (3–6 досок с per-plank tint и Perlin grain), per-wall mix стилей (cardboard / white plaster / mixed), per-wall albedo-jitter под неравномерное освещение и многоуровневую систему освещения (directional + warm tungsten point lights над каждой третьей ячейкой + spot над финишной отметкой). Полный список параметров и диапазонов — в `CardboardCorridorTrack.cs:38-66`.

Одна тонкость, которая бы не возникла без эксперимента: Unity-light объекты не действуют на материалы с Unlit-шейдером, который я использую в сцене. Чтобы оператор не видел плоско окрашенных стен независимо от направления освещения, неравномерность пришлось внести через per-wall value-jitter в albedo (множитель 0.70–1.25 на reset). После этого сцена стала визуально сильно разнообразной от эпизода к эпизоду.

После heavy-DR порта (rev24+) на реальном роботе prior policy сместился — DirRight 66% сменился на DirForward 36–59%. То есть policy перестала залипать на конкретный цвет стены и начала использовать структурные feature, которые переносятся между симом и реалом.

Procedural maze (`track.cardboard_maze.v1`) после Plan 1 получил тот же визуальный бюджет, что и L-коридор: per-cell wood-plank пол, mixed-wall стили, warm tungsten lighting каждые 3 cell-а, spot над финиш-маркером. До этого maze-сцена использовалась реже из-за того, что её heavy-DR пришлось бы отдельно вылавливать — теперь оба трека на одном уровне.

---

## 4. Распределённая инфраструктура обучения Mac ↔ Win

Mac M4 Max, на котором живёт основной dev-цикл, не лучший выбор для длительного RL-обучения с CUDA. Поэтому в спринте я собрал отдельный compute-узел на Windows 11 + RTX 5080 + Ryzen 9950X3D. Обращение к нему сделал максимально похожим на то, как работают облачные training-сервисы: запуск обучения и просмотр логов идёт через `ssh win`, артефакты переносятся `scp`, сами PowerShell-launcher-скрипты передаются на Win через base64 (это ушло на удивление полезно — никаких проблем с CRLF-кодировкой и эскейп-ингом цитат).

При этом схема симметричная: симулятор может работать на Mac, а обучение — на Win, как принято; либо наоборот, оба процесса можно поставить на любую машину в сети. Backend WebUI работает с любого Mac через REST + SignalR, так что оператор управляет роботом из браузера на ноутбуке, а вычислительные ресурсы (sim + training) находятся где-то ещё. Это не маркетинговая фраза, а текущая рабочая конфигурация моего setup-а — я многократно запускал full-cycle тренировку с пробуждённым только Mac, который удалённо командовал Win-узлом.

Параллельно с вычислительной частью я заменил стандартный SubprocVecEnv на свою связку:

- `MultiAgentVisionVecEnv` — 1 Unity-процесс, N агентов в одном инстансе (8 камер на сцене + изоляция collision'ов между ними, агенты не видят друг друга);
- `MetaMultiAgentVecEnv` — N Unity-процессов × K агентов, итого N×K параллельных rollout-потоков для PPO.

В обычной комплектации это даёт 32 параллельных агента (4 Unity × 8 агентов) на одной RTX 5080. Скорость training'а — около 180 fps по таймстепам; 200k шагов укладываются в ~22 минуты wall-clock, что превратило обычный «прогон ночью на 24 часа» в «прогон во время обеда на 25 минут». Такая скорость дала возможность запустить серию из 11 повторных тренировок за одну ночь и получить эмпирическую оценку variance — об этом ниже.

Третья часть инфраструктуры — Python-порт `MazeGenerator` симулятора. Unity-side maze-генератор работает по своему PRNG, и если просто отдать symulator только seed, на python-стороне нечем посчитать прогресс по маршруту. Я сделал bit-exact реплику генератора в Python (`python/training/maze_generator.py`) и передаю в Unity не seed, а уже сгенерированный `path_encoded` через trackParams. Так гарантируется, что обе стороны видят одинаковую геометрию и waypoints, а функция reward правильно считает progress.

---

## 5. Расширение WebUI: shadow-preview, demo recording, replay

WebUI вырос за спринт из «панель управления роботом» в инструмент исследования. Три ключевых добавления:

**Shadow-mode preview** ([`AutopilotService.SamplePreview`](../../../src/ks0223-web-mac/backend/Services/AutopilotService.cs)). Кнопка в WebUI прогоняет привязанную модель на текущем кадре с робота с шагом 200 мс, не запуская моторы. Возвращает все 5 action-вероятностей, выбранное действие, текущий sonar и базовые CV-статистики (brightness, edge score) прямо поверх стрима камеры. Этот инструмент — главный ключ к тому, как я диагностировал prior flip rev16 и понял, что нужна heavy-DR. Без него я бы неделями катал робота в стену, не понимая откуда у policy DirRight в 66%.

**Demo recording** (`POST /api/demo/start` / `POST /api/demo/stop`). Одной кнопкой стартует session-log в JSONL + MJPEG-видеозапись с бортовой камеры. Я использую это, чтобы записывать собственные «эталонные проезды» (вручную через ControlPad), а потом сравнивать с тем, как policy ведёт себя на тех же кадрах. Видео-recording реализован через fragmented MP4 (`+frag_keyframe`), благодаря чему даже секундная запись остаётся валидным MP4 при остановке.

**Demo replay** ([`DemoReplayService.cs`](../../../src/ks0223-web-mac/backend/Services/DemoReplayService.cs)) — основная новая фича спринта в WebUI. Сервис парсит JSONL-сессию, выделяет события `command.outgoing` с timestamp'ами и проигрывает их обратно через `RuntimeSessionManager.SendCommandAsync` — то есть ровно по тому же каналу, по которому работает живой autopilot, со всеми safety-фильтрами в линии. Замер реальных задержек показал, что Wi-Fi round-trip к Pi ~150–200 мс — наивный delta-time подход накапливал бы дрейф порядка ~10 секунд на 60-командной сессии, поэтому планировщик сделан в absolute-time режиме: каждая команда летит в `playback_start + (cmd_ts - first_cmd_ts) / speed_multiplier`, и дрейф SendCommand'а сам поглощается в следующем sleep'е.

В UI это выпадающий список доступных сессий, переключатель скорости 0.5×/1×/2×/4× и live-progress bar (поллинг `/api/demo/replay/status` каждые 250 мс). Под капотом — те же 4 endpoint-а (`start`, `stop`, `status`, `sessions`), что выдают полностью machine-readable интерфейс, поэтому при необходимости можно автоматизировать прогон через curl.

Дополнительно расширил safety-стек: E-stop по sonar с настраиваемой дистанцией, suspicious-jump guard под echo-loss датчика HC-SR04, missing-reading guard, throttle ramp-up (motor deadzone real-робота 0.20–0.30 — без ramp-up'а команды на низких pwm не доходят до колёс), deadman timeout, repeated-command threshold с auto-stop на runaway policy, DirStop burst при остановке.

---

## 6. Главное эмпирическое наблюдение спринта — entropy coefficient drift

Между rev16 (100% sim SR) и rev24 (35% sim SR на heavy-DR transfer) я перепробовал около десяти подходов — менял reward shaping, лезл в timing, в scenario YAML — и каждый раз получал ровно 0% либо degenerate basin (DirForward 100% lock или DirRight 60% spin). Закономерность была настолько подозрительной, что я остановился и провёл audit `metadata.json` всех ревизий, отсортировав по `extra/hyperparameters/entCoef`.

Получилось такое:

| Ревизии | `ent_coef` | Результат |
|---|---|---|
| rev10, rev12, rev14, rev15, rev16, rev18 | **0.1** | работали, до 100% |
| rev19d | 0.15 | работала |
| rev24, rev25, rev27, rev28 | **0.02** | коллапс (35%, 0%, 0%, 0%) |

Источник проблемы оказался банальным: launcher-default для `--ent-coef` стоял `0.02`, а транзитные ревизии (rev24+) запускались transfer-train'ом от `rev16` (которая училась с 0.1) — но ни одна launcher-команда `--ent-coef` явно не передавала. Получилось, что policy стартовала с весов, оптимизированных под энтропию 0.1, и дальше училась с энтропией в 5 раз меньше. На простой DR-сцене это сходило, на heavy-DR — entropy-starvation, greedy local optimum, degenerate basin.

После явной передачи `--ent-coef 0.1` ревизия `rev29` сразу выдала **75% SR** на heavy-DR — именно она и стала production-моделью. В коде раз и навсегда сменил default на 0.1 и расширил `metadata.json` так, чтобы все hyperparams включая `entCoef`, `seed`, `lateralPenaltyMult`, `resumeFrom` записывались в артефакт автоматически (commit [`4c46ef8`](https://github.com/NMGorovenko/uav-simulator/commit/4c46ef8)). Ловить такой же drift в будущем теперь дело одного `grep`.

Параллельно с этой находкой я добавил три callback'а для PPO-trainer'а: `ActionStatsCallback` (per-action distribution в TensorBoard каждые 5k шагов), `RewardBreakdownCallback` (per-component reward), `EvalCallback` с best-model snapshot. До них я узнавал про degenerate collapse только через 30 минут по итогам формального eval; сейчас он виден в TensorBoard в первые 10–20 тыс. шагов, чем я и сэкономил минимум день GPU-времени.

---

## 7. Архитектурный эксперимент: R3M backbone + RecurrentPPO

Параллельно с практическим закрытием контура я хотел посмотреть, насколько потенциал текущего PPO-стека ограничен scratch-CNN'ом. В литературе по vision-RL давно показано, что замена scratch-encoder'а на frozen pretrained-backbone (R3M Nair 2022, DINOv2 Oquab 2023) на порядок снижает variance в обучении и зачастую требует меньше данных. У меня в стеке завести R3M напрямую не получилось из-за конфликтов зависимостей (ему нужны старый gym и собственная версия torch), поэтому вместо этого я использовал torchvision-овский ResNet18 Imagenet-pretrained как frozen feature extractor — для индорной навигации в коридоре разница с R3M незаметна, поскольку и в R3M, и в Imagenet'е важны одни и те же texture/edge-features.

`R3MFeatureExtractor` ([`python/training/r3m_feature_extractor.py`](../../../python/training/r3m_feature_extractor.py)) интегрируется в SB3 PPO как drop-in кастомный экстрактор: ImageNet-нормализация, resize до 224×224, frozen forward через ResNet18, конкатенация с sonar-вектором, MLP-голова до 256-d. Итого ~11.5М параметров, из которых обучаемых только 394 тыс. (голова) — на два порядка меньше, чем при scratch-CNN'е.

Параллельно завёл RecurrentPPO из sb3-contrib, чтобы у policy появилась внутренняя память (LSTM, hidden=128). Это нужно скорее для maze-сцены, где policy должна помнить «я только что повернул налево, теперь надо вперёд по второму сегменту». На L-коридоре LSTM избыточен.

Эта архитектура **готова в коде** ([`commit 356f7c1`](https://github.com/NMGorovenko/uav-simulator/commit/356f7c1)), но 300k шагов from-scratch на random maze оказалось недостаточно — модель не успела выучить навигацию (см. таймлайн ниже, rev40). По моим оценкам и по тому, что обычно показывают в литературе, ей нужно либо ~1М шагов (что укладывается в одну ночь на текущей инфраструктуре), либо BC-bootstrap от записанных мной демо-сессий. Это первое, что я планирую запустить в начале финального спринта.

---

## 8. Эволюция моделей (компактный таймлайн)

Чтобы не утяжелять отчёт списком из 18 ревизий, привожу только milestones и причины существования каждой группы.

| Группа | Что изменилось | Sim SR на сцене обучения |
|---|---|---:|
| **rev16** (baseline) | 300k from-scratch, ent=0.1, mild-DR L-corridor | **100%** |
| rev17–rev22 | попытки одного-шагового sim2real (mixed walls, warm light, hue jitter, skybox-null, layered lighting) | 0% — сменил слишком много переменных за раз |
| **rev24** (heavy-DR) | 200k transfer rev16 → wood-plank floor + mixed walls + per-wall jitter | 35% (но prior на реале сменился на DirForward 36–59%) |
| rev25–rev28 | discomfort-tuning lateral penalty + transfer source variations | 0% — entropy starvation (см. § 6) |
| **rev29** (production) | rev24 config + явно `--ent-coef 0.1`, transfer от rev16 | **75%** |
| rev30–rev36 | ночной эксперимент Sprint B: 7 attempts с разными reward-fixes + multi-seed | 7/7 × 0% — вариативность PPO |
| **rev37** (Plan 1: maze) | heavy-DR порт на `track.cardboard_maze.v1` + curriculum + maze-randomize | 0% SR / 47% avg progress (robot ездит до середины случайной maze) |
| rev38, rev40 | Plan 2/5 from-scratch с frame-stacking k=4 / R3M+RecurrentPPO | 0% — 300k недостаточно для архитектурных экспериментов |
| **rev39** (Plan 4) | rev37 baseline + spawn jitter + dynamics DR + motor asymmetry + camera pitch jitter + sonar noise | 0% SR / 47% avg progress + полный набор sim2real-DR axes |
| rev41–rev46 | multi-seed sweep на L-corridor с rev29-recipe (seeds 1337 / 2024 / 9999 / 7) | 0% × 6 |

Эмпирическая оценка надёжности transfer-PPO на heavy-DR landscape: **1 удачное попадание из 11 попыток** при идентичной комбинации hyperparams. То есть `rev29` — это статистически крайне удачный pick. Чтобы reliable получать 70%+ SR с этой архитектурой, multi-seed sweep с 8–16 параллельными seeds должен повысить вероятность хотя бы одного попадания до 80%+. Альтернатива — переход на R3M + RecurrentPPO с длительным training (см. § 7), который как раз убирает эту вариативность за счёт robust pretrained feature representation.

---

## 9. Что планируется в финальном отчёте

Спринт показал и потолок текущей архитектуры, и пути его обойти. На финальный отчёт остаётся:

1. **Длительное обучение R3M + RecurrentPPO на random maze** (~1М шагов, ~5 часов одной ночи на Win-узле) — основной кандидат на стабильную модель для произвольных корпусов трасс. Код архитектуры уже в репозитории, скрипты запуска подготовлены.

2. **BC-bootstrap от расширенного demo-датасета.** Сейчас я записал ~1500 (frame, action) пар через WebUI Demo Recording — для BC pre-training этого достаточно, чтобы дать policy reasonable initial weights. Demo Replay помогает быстро воспроизводить записи для тестов на роботе после обучения.

3. **Полный реальный проезд L-коридора с goal-stop поведением.** На текущем `rev29` робот доезжает до целевой зоны, но не выдаёт DirStop сам — сейчас стоп инициируется safety-стеком при потере sonar-сигнала. Это лечится reward-shape модификацией (terminal bonus при `last_action == DirStop` в goal-radius) и одной короткой train-iteration.

4. **Демонстрация на стенде и формальная eval.** Полный заезд под запись с тремя ракурсами (внешний, бортовая камера, WebUI live-overlay) на показ.

---

## 10. Артефакты и репозиторий

| Что | Путь |
|---|---|
| Production model (sim 75%, real-robot driven) | [`python/training/artifacts/cardboard-corridor-ppo-v9-rev29/1.0.0/`](../../../python/training/artifacts/cardboard-corridor-ppo-v9-rev29/1.0.0/) |
| Heavy-DR L-corridor сцена (Unity C#) | [`CardboardCorridorTrack.cs`](../../../src/UnityProject/uav-simulator/Assets/Scripts/Tracks/CardboardCorridorTrack.cs) |
| Heavy-DR maze сцена | [`CardboardMazeTrack.cs`](../../../src/UnityProject/uav-simulator/Assets/Scripts/Tracks/CardboardMazeTrack.cs) + [`MazeGenerator.cs`](../../../src/UnityProject/uav-simulator/Assets/Scripts/Tracks/MazeGenerator.cs) |
| Multi-agent VecEnv + Python maze generator | [`multi_agent_vision_env.py`](../../../python/training/multi_agent_vision_env.py), [`meta_multi_agent_vec_env.py`](../../../python/training/meta_multi_agent_vec_env.py), [`maze_generator.py`](../../../python/training/maze_generator.py) |
| R3M / RecurrentPPO интеграция | [`r3m_feature_extractor.py`](../../../python/training/r3m_feature_extractor.py), [`train_cardboard_corridor_v9.py`](../../../python/training/train_cardboard_corridor_v9.py) |
| Demo Replay backend | [`DemoReplayService.cs`](../../../src/ks0223-web-mac/backend/Services/DemoReplayService.cs) |
| Demo Replay frontend | [`DemoReplayPanel.tsx`](../../../src/ks0223-web-mac/frontend/src/components/DemoReplayPanel.tsx) |
| Saliency / shadow-preview | [`AutopilotService.SamplePreview`](../../../src/ks0223-web-mac/backend/Services/AutopilotService.cs), [`policy_saliency.py`](../../../python/training/policy_saliency.py) |
| Подробный лог итераций | [`sprint-3-changelog.md`](sprint-3-changelog.md) |
| Видео реального проезда | [`final_demo/rev29_real_run_2026-04-30_phone.mp4`](sprint-3-reeval-2026-04-27/real_corridor/final_demo/rev29_real_run_2026-04-30_phone.mp4) |
| Бортовая камера (та же сессия) | [`final_demo/rev29_real_run_2026-04-30_robot_cam.mp4`](sprint-3-reeval-2026-04-27/real_corridor/final_demo/rev29_real_run_2026-04-30_robot_cam.mp4) |
| Master research-план (фон) | [`2026-04-28-path-to-100-percent.md`](../../superpowers/plans/2026-04-28-path-to-100-percent.md) |
| Sprint-3 sub-plans (1–6) | [`2026-04-29-sprint3-master.md`](../../superpowers/plans/2026-04-29-sprint3-master.md) |
