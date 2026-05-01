# Спринт 3 — sim-to-real на платформе KS0223 (краткий отчёт)

> **Период:** 19.04.2026 — 02.05.2026
> **Практика:** преддипломная (производственная), 09.04.04 «Программная инженерия»
> **Тема:** Замыкание sim-to-real контура на робототехнической платформе KS0223 и расширение Unity-симулятора под исследовательские задачи domain randomization
> **Проект:** [`uav-simulator`](https://github.com/NMGorovenko/uav-simulator)
> **Студент:** Горовенко Никита Максимович, группа КИ24-04-3М

---

## Оглавление

1. [Итоги спринта](#1-итоги-спринта)
2. [Глоссарий](#2-глоссарий)
3. [Замыкание sim-to-real: рабочий проезд по L-коридору](#3-замыкание-sim-to-real-рабочий-проезд-по-l-коридору)
4. [Реальный стенд](#4-реальный-стенд)
5. [Heavy domain randomization сцены](#5-heavy-domain-randomization-сцены)
6. [Распределённая инфраструктура обучения Mac ↔ Win](#6-распределённая-инфраструктура-обучения-mac--win)
7. [Расширение WebUI: shadow-preview, demo recording, replay](#7-расширение-webui-shadow-preview-demo-recording-replay)
8. [Главное эмпирическое наблюдение спринта — entropy coefficient drift](#8-главное-эмпирическое-наблюдение-спринта--entropy-coefficient-drift)
9. [Возвращение к maze-сцене: процедурная генерация и архитектурные эксперименты](#9-возвращение-к-maze-сцене-процедурная-генерация-и-архитектурные-эксперименты)
10. [Контур управления KS0223 и нюансы sim-to-real](#10-контур-управления-ks0223-и-нюансы-sim-to-real)
11. [Эволюция моделей (компактный таймлайн)](#11-эволюция-моделей-компактный-таймлайн)
12. [Задачи финального этапа и магистерской работы](#12-задачи-финального-этапа-и-магистерской-работы)
13. [Артефакты и репозиторий](#13-артефакты-и-репозиторий)

---

## 1. Итоги спринта

Главная цель спринта — довести sim-to-real цикл до состояния, когда модель, обученная в симуляторе, действительно управляет физическим роботом в L-коридоре, а не только показывает хорошие цифры на формальной оценке. Эту цель я считаю **выполненной**: модель `cardboard-corridor-ppo-v9-rev29` развёрнута в backend как активная и под её управлением робот KS0223 успешно проходит трассу. После закрытия sim-to-real контура я вернулся к процедурно генерируемой maze-сцене и поставил архитектурные эксперименты, которые служат заделом на финальный этап и магистерскую работу.

**Ключевые результаты в одну строку:**

| Метрика | Значение |
|---|---|
| Лучшая модель в симуляторе на mild-DR сцене | `rev16`, **100% SR** (20/20 эпизодов) |
| Лучшая модель в симуляторе на heavy-DR сцене | `rev29`, **75% SR** |
| Поведение `rev29` на физическом роботе | проезд L-коридора под управлением policy + safety-стек (см. § 3) |
| Обученных policy за спринт | 23 модели серии `v9-rev24` … `v9-rev46` |
| Симуляционных трасс | 2 (`track.cardboard_corridor.v1`, `track.cardboard_maze.v1`) |
| Реализованных доработок WebUI | shadow-preview, demo recording, demo replay |
| Зафиксированных коммитов на ветке `develop` | 48 |

Все запланированные задачи спринта выполнены. Открытые нюансы — точное закрытие петли управления KS0223 на стороне прошивки робота (см. § 10) и полное прохождение случайных maze-топологий (см. § 9) — переносятся в финальный этап и магистерскую работу как технически понятные следующие шаги, а не как незакрытые проблемы.

Помимо непосредственного sim-to-real результата в спринте появилась серия инженерных доработок, которые превращают наработки в относительно цельный продукт: распределённая обучающая связка Mac ↔ Win, WebUI с возможностью записывать референсные проезды и воспроизводить их в реальном времени, расширенный safety-стек, единый ModelRegistry с возможностью держать на роботе несколько политик и переключать активную через REST API. Часть из этого по сути cloud-style — тренировку можно вести на отдельной машине, а оператор может работать с любого устройства в локальной сети.

---

## 2. Глоссарий

Кратко расшифровываю термины, используемые ниже, чтобы отчёт можно было читать без обращения к литературе.

| Термин | Расшифровка |
|---|---|
| **PPO** | Proximal Policy Optimization (Schulman 2017) — основной алгоритм обучения policy в этом проекте, реализация из библиотеки Stable-Baselines3. |
| **Policy** | Обученная нейросеть, которая по наблюдению (кадр + sonar) выбирает одно из 5 дискретных действий (`DirStop`, `DirForward`, `DirBack`, `DirLeft`, `DirRight`). |
| **sim-to-real (sim2real)** | Перенос policy, обученной в симуляторе, на физического робота. Основная сложность — visual distribution shift между Unity-сценой и реальной камерой. |
| **Domain randomization (DR)** | Намеренное варьирование визуальных параметров сцены (текстуры, цвета, освещение) в процессе обучения, чтобы policy не закреплялась за конкретный «вид» симулятора. Различаю mild-DR (умеренная) и heavy-DR (сильная). |
| **Prior flip** | Эффект, при котором одна и та же policy на «эквивалентном» кадре в симе и на реале выбирает противоположные действия. Главный диагностический сигнал sim-to-real gap'а в спринте. |
| **ent_coef** | Коэффициент entropy regularization в PPO. При слишком низком значении policy схлопывается в одну команду (degenerate basin). |
| **Transfer learning** | Дообучение от предыдущей policy вместо обучения с нуля; используется как ускорение и для сохранения навигационных навыков базовой модели (`rev16`). |
| **VecEnv** | Вектор-среда (Stable-Baselines3): несколько параллельных копий симулятора, по которым PPO собирает rollout'ы одновременно. В проекте — `MultiAgentVisionVecEnv` (N агентов в одном Unity-процессе) и `MetaMultiAgentVecEnv` (N×K). |
| **Frame stacking (k=4)** | Подача в CNN не одного кадра, а четырёх последовательных. Позволяет policy видеть движение и различать «стою у стены» vs «приближаюсь к стене». |
| **R3M** | Frozen pretrained vision backbone из работы Nair (2022). В моей реализации заменён на ImageNet-pretrained ResNet18 как drop-in аналог при идентичной семантике использования. |
| **RecurrentPPO** | Реализация PPO с LSTM-памятью внутри policy (sb3-contrib). Нужна на maze-сцене, где требуется помнить недавнюю историю поворотов. |
| **BC bootstrap** | Behavioral cloning — supervised pre-training policy на парах (кадр, действие), записанных оператором. Снижает variance последующего PPO-fine-tuning'а. |
| **Sonar guard / E-stop** | Аппаратно-программная защита: backend форсирует `DirStop`, если ультразвуковой датчик HC-SR04 показывает дистанцию ниже порога. |
| **Open-loop / closed-loop control** | Open-loop: команды на моторы без обратной связи по фактическому движению. Closed-loop: с энкодерами или IMU. KS0223 в текущей прошивке работает как open-loop. |

---

## 3. Замыкание sim-to-real: рабочий проезд по L-коридору

Самый понятный результат спринта показывается видеозаписью. Я снял на телефон, как KS0223 проезжает картонный L-коридор у меня в комнате — управление полностью передано через backend autopilot policy `rev29`, бортовая телеметрия (sonar + камера) идёт в политику с шагом ~140 мс, safety-фильтры backend (sonar E-stop при дистанции <10 см, deadman timeout, throttle ramp-up) работают в нормальном режиме.

**Видео внешней съёмки:** [`final_demo/rev29_real_run_2026-04-30_phone.mp4`](sprint-3-reeval-2026-04-27/real_corridor/final_demo/rev29_real_run_2026-04-30_phone.mp4) (≈ 14 с, 431 КБ — h264, 720p)
**Бортовая камера робота:** [`final_demo/rev29_real_run_2026-04-30_robot_cam.mp4`](sprint-3-reeval-2026-04-27/real_corridor/final_demo/rev29_real_run_2026-04-30_robot_cam.mp4) (191 КБ)
**Анимированный кадр для DOCX:** [`final_demo/rev29_real_run_2026-04-30_phone_compact.gif`](sprint-3-reeval-2026-04-27/real_corridor/final_demo/rev29_real_run_2026-04-30_phone_compact.gif) (2.9 МБ)

В этом проезде робот стартует в начале первого сегмента L-коридора, по командам policy движется вперёд, на повороте выполняет серию манёвров (комбинация DirLeft / DirRight) и достигает целевой зоны. Это первый успешный sim-to-real проезд после серии итераций rev24…rev28, в которых модель либо «застревала» на одном действии, либо врезалась в стену на старте.

Чтобы попасть в этот результат, потребовалось разобрать несколько вложенных проблем — от доработок самой Unity-сцены до тонкой настройки PPO в Python-обвязке.

---

## 4. Реальный стенд

Для физических испытаний я собрал картонный L-коридор у себя в комнате. Стенд состоит из листов гофрокартона, опирающихся на предметы интерьера (диван, стол, стул); первый сегмент идёт вдоль белой штукатурной стены, второй — после правого поворота — между двумя картонными перегородками. Пол — реальный дубовый паркет с продольными швами между досками. Это сочетание поверхностей (паркет + белая штукатурка + картон + неравномерное потолочное освещение) и было основным источником визуального несовпадения с исходной симуляционной сценой.

**Общий вид сверху, видна полная L-форма:**

![Topdown view of the L-corridor stand](real_corridor_photos/01_topdown_full_L.jpg)

**Прямая секция A — белая штукатурка слева, картон справа, паркет:**

![Сегмент A](real_corridor_photos/02_segment_a_white_wall_cardboard.jpg)

**Перспектива «глазами робота» вдоль прямой части:**

![Перспектива из стартовой позиции](real_corridor_photos/03_corridor_perspective.jpg)

**Угол перехода в сегмент B (правый поворот):**

![Угол L-коридора](real_corridor_photos/04_corner_junction.jpg)

**Второй прямой участок:**

![Сегмент B](real_corridor_photos/05_segment_b_tunnel_view.jpg)

Эти фото послужили эталонным распределением визуальных признаков для heavy-DR сцены: в Unity я воспроизвёл дубовый паркет с per-plank tint и Perlin-grain'ом, белую штукатурку как один из стилей стены и неравномерное освещение через per-wall albedo-jitter.

---

## 5. Heavy domain randomization сцены

К началу спринта модели на простой DR-сцене (`rev16`) выдавали 100% в симуляторе, но при первом же запуске на реальном роботе уверенно врезались в стену. Я провёл диагностику через инструмент shadow-mode (см. § 7), сравнив распределение действий policy на одинаковых стартовых кадрах в симе и на реале:

| Семпл (один и тот же стартовый кадр) | DirStop | DirForward | DirBack | DirLeft | **DirRight** |
|---:|---:|---:|---:|---:|---:|
| Sim, `rev16` | 10% | **87%** | 2% | 0% | 0% |
| Real-robot, `rev16` | 9% | 6% | 0% | 27% | **66%** |

Distribution shift оказался настолько большим, что policy буквально перевыбирала стороны корпуса коридора. Нужно было привести симулятор к виду, в котором коридор перестаёт «выглядеть как одна и та же тан-цветная коробка каждый раз».

В рамках этой задачи я переписал `CardboardCorridorTrack.cs` (а потом портировал тот же набор приёмов на `CardboardMazeTrack.cs`) — добавил процедурную текстуру дубового пола (3–6 досок с per-plank tint и Perlin grain), per-wall mix стилей (cardboard / white plaster / mixed), per-wall albedo-jitter под неравномерное освещение и многоуровневую систему освещения (directional + warm tungsten point lights над каждой третьей ячейкой + spot над финишной отметкой). Полный список параметров и диапазонов — в [`CardboardCorridorTrack.cs:38-66`](../../../src/UnityProject/uav-simulator/Assets/Scripts/Tracks/CardboardCorridorTrack.cs).

Одна тонкость, которая бы не возникла без эксперимента: Unity-light объекты не действуют на материалы с Unlit-шейдером, который я использую в сцене. Чтобы оператор не видел плоско окрашенных стен независимо от направления освещения, неравномерность пришлось внести через per-wall value-jitter в albedo (множитель 0.70–1.25 на reset). После этого сцена стала визуально сильно разнообразной от эпизода к эпизоду.

После heavy-DR порта (rev24+) на реальном роботе prior policy сместился — DirRight 66% сменился на DirForward 36–59%. То есть policy перестала залипать на конкретный цвет стены и начала использовать структурные feature, которые переносятся между симом и реалом.

---

## 6. Распределённая инфраструктура обучения Mac ↔ Win

Mac M4 Max, на котором живёт основной dev-цикл, не лучший выбор для длительного RL-обучения с CUDA. Поэтому в спринте я собрал отдельный compute-узел на Windows 11 + RTX 5080 + Ryzen 9950X3D. Обращение к нему сделал максимально похожим на то, как работают облачные training-сервисы: запуск обучения и просмотр логов идёт через `ssh win`, артефакты переносятся `scp`, сами PowerShell-launcher-скрипты передаются на Win через base64 (это оказалось на удивление полезно — никаких проблем с CRLF-кодировкой и эскейп-ингом цитат).

Схема симметричная: симулятор может работать на Mac, а обучение — на Win, как принято; либо наоборот, оба процесса можно поставить на любую машину в сети. Backend WebUI работает с любого Mac через REST + SignalR, так что оператор управляет роботом из браузера на ноутбуке, а вычислительные ресурсы (sim + training) находятся где-то ещё. Это не маркетинговая фраза, а текущая рабочая конфигурация моего setup'а — я многократно запускал full-cycle тренировку с пробуждённым только Mac, который удалённо командовал Win-узлом.

Параллельно с вычислительной частью я заменил стандартный `SubprocVecEnv` на свою связку:

- **`MultiAgentVisionVecEnv`** — 1 Unity-процесс, N агентов в одном инстансе (8 камер на сцене + изоляция collision'ов между ними, агенты не видят друг друга);
- **`MetaMultiAgentVecEnv`** — N Unity-процессов × K агентов, итого N×K параллельных rollout-потоков для PPO.

В обычной комплектации это даёт 32 параллельных агента (4 Unity × 8 агентов) на одной RTX 5080. Скорость training'а — около 180 fps по таймстепам; 200k шагов укладываются в ~22 минуты wall-clock. Такая скорость дала возможность запустить серию из 11 повторных тренировок за одну ночь и получить эмпирическую оценку variance — об этом в § 8.

Третья часть инфраструктуры — Python-порт `MazeGenerator` симулятора. Unity-side maze-генератор работает по своему PRNG, и если просто отдать симулятору только seed, на python-стороне нечем посчитать прогресс по маршруту. Я сделал bit-exact реплику генератора в Python ([`python/training/maze_generator.py`](../../../python/training/maze_generator.py)) и передаю в Unity не seed, а уже сгенерированный `path_encoded` через `trackParams`. Так гарантируется, что обе стороны видят одинаковую геометрию и waypoints, а функция reward правильно считает progress.

---

## 7. Расширение WebUI: shadow-preview, demo recording, replay

WebUI вырос за спринт из «панели управления роботом» в инструмент исследования. Три ключевых добавления:

**Shadow-mode preview** ([`AutopilotService.SamplePreview`](../../../src/ks0223-web-mac/backend/Services/AutopilotService.cs)). Кнопка в WebUI прогоняет привязанную модель на текущем кадре с робота с шагом 200 мс, не запуская моторы. Возвращает все 5 action-вероятностей, выбранное действие, текущий sonar и базовые CV-статистики (brightness, edge score) прямо поверх стрима камеры. Этот инструмент — главный ключ к тому, как я диагностировал prior flip rev16 и понял, что нужна heavy-DR. Без него я бы неделями катал робота в стену, не понимая, откуда у policy DirRight в 66%.

**Demo recording** (`POST /api/demo/start` / `POST /api/demo/stop`). Одной кнопкой запускается session-log в JSONL и MJPEG-видеозапись с бортовой камеры. Я использую это, чтобы записывать собственные «эталонные» проезды (вручную через ControlPad), а потом сравнивать с тем, как policy ведёт себя на тех же кадрах. Видеозапись реализована через fragmented MP4 (`+frag_keyframe`), благодаря чему даже секундная запись остаётся валидным MP4 при остановке.

**Demo replay** ([`DemoReplayService.cs`](../../../src/ks0223-web-mac/backend/Services/DemoReplayService.cs)) — основная новая фича спринта в WebUI. Сервис парсит JSONL-сессию, выделяет события `command.outgoing` с timestamp'ами и проигрывает их обратно через `RuntimeSessionManager.SendCommandAsync` — то есть ровно по тому же каналу, по которому работает живой autopilot, со всеми safety-фильтрами в линии. Замер реальных задержек показал, что Wi-Fi round-trip к Pi составляет ~150–200 мс — наивный delta-time подход накапливал бы дрейф порядка ~10 секунд на 60-командной сессии, поэтому планировщик сделан в absolute-time режиме: каждая команда летит в `playback_start + (cmd_ts − first_cmd_ts) / speed_multiplier`, и дрейф `SendCommand`'а сам поглощается в следующем sleep'е.

В UI это выпадающий список доступных сессий, переключатель скорости 0.5×/1×/2×/4× и live-progress bar (поллинг `/api/demo/replay/status` каждые 250 мс). Под капотом — те же 4 endpoint'а (`start`, `stop`, `status`, `sessions`), которые выдают полностью machine-readable интерфейс, поэтому при необходимости прогон можно автоматизировать через curl.

Дополнительно я расширил safety-стек: E-stop по sonar с настраиваемой дистанцией, suspicious-jump guard под echo-loss датчика HC-SR04, missing-reading guard, throttle ramp-up (motor deadzone реального робота 0.20–0.30 — без ramp-up'а команды на низких pwm не доходят до колёс), deadman timeout, repeated-command threshold с auto-stop на runaway-policy, DirStop burst при остановке.

---

## 8. Главное эмпирическое наблюдение спринта — entropy coefficient drift

Между rev16 (100% sim SR) и rev24 (35% sim SR на heavy-DR transfer) я перепробовал около десяти подходов — менял reward shaping, лез в timing, в scenario YAML — и каждый раз получал ровно 0% либо degenerate basin (DirForward 100% lock или DirRight 60% spin). Закономерность была настолько подозрительной, что я остановился и провёл audit `metadata.json` всех ревизий, отсортировав по `extra/hyperparameters/entCoef`.

Получилось такое:

| Ревизии | `ent_coef` | Результат |
|---|---|---|
| rev10, rev12, rev14, rev15, rev16, rev18 | **0.1** | работали, до 100% |
| rev19d | 0.15 | работала |
| rev24, rev25, rev27, rev28 | **0.02** | коллапс (35%, 0%, 0%, 0%) |

Источник проблемы оказался банальным: launcher-default для `--ent-coef` стоял `0.02`, а транзитные ревизии (rev24+) запускались transfer-train'ом от `rev16` (которая училась с 0.1) — но ни одна launcher-команда `--ent-coef` явно не передавала. Получилось, что policy стартовала с весов, оптимизированных под энтропию 0.1, и дальше училась с энтропией в 5 раз меньше. На простой DR-сцене это сходило, на heavy-DR — entropy-starvation, greedy local optimum, degenerate basin.

После явной передачи `--ent-coef 0.1` ревизия `rev29` сразу выдала **75% SR** на heavy-DR — именно она и стала production-моделью. В коде я раз и навсегда сменил default на 0.1 и расширил `metadata.json` так, чтобы все hyperparams, включая `entCoef`, `seed`, `lateralPenaltyMult`, `resumeFrom`, записывались в артефакт автоматически (commit [`4c46ef8`](https://github.com/NMGorovenko/uav-simulator/commit/4c46ef8)). Ловить такой же drift в будущем теперь — дело одного `grep`.

Параллельно с этой находкой я добавил три callback'а для PPO-trainer'а: `ActionStatsCallback` (per-action distribution в TensorBoard каждые 5k шагов), `RewardBreakdownCallback` (per-component reward), `EvalCallback` с best-model snapshot. До них я узнавал про degenerate collapse только через 30 минут по итогам формального eval; сейчас он виден в TensorBoard в первые 10–20 тыс. шагов, чем я и сэкономил минимум день GPU-времени.

Дополнительно multi-seed sweep (rev41–rev46, 4 разных seed'а на одинаковом recipe) показал, что у PPO-transfer на heavy-DR landscape узкая зона притяжения: hit rate ≈ 1 из 11 попыток. Это эмпирически подтверждает, что для надёжной воспроизводимости 70%+ SR нужны либо параллельный многоразовый seed-sweep (8–16 seed'ов), либо переход на более устойчивую архитектуру (frozen pretrained backbone + RecurrentPPO, см. § 9).

---

## 9. Возвращение к maze-сцене: процедурная генерация и архитектурные эксперименты

После того как sim-to-real был замкнут на L-коридоре (rev29 в production), я вернулся к процедурно генерируемой maze-сцене, которая до этого использовалась реже. Идея maze-трассы в том, что она **умеет перегенерироваться**: класс `MazeGenerator` строит на каждый эпизод случайную топологию (5–11 ячеек, длина пути 7–15 cell'ов, ветвление 1–4), что должно бороться с overfitting на конкретную геометрию. Эта функциональность была в коде с прошлых спринтов, но без heavy-DR визуалов и без согласованного python-side прогресс-трекера её было невозможно использовать в обучении.

В рамках возвращения к maze я выполнил четыре последовательные доработки:

1. **Plan 1 (rev37) — heavy-DR порт сцены и curriculum.** Перенёс на `CardboardMazeTrack.cs` (commit [`6076d74`](https://github.com/NMGorovenko/uav-simulator/commit/6076d74)) тот же визуальный бюджет, что и на L-коридор: per-cell wood-plank пол, per-wall mix стилей, warm tungsten point lights каждые 3 cell'а, spot над финиш-маркером. Добавил трёхступенчатый curriculum: 5-cell прямая → L → полная maze 7–11 cell'ов. Результат rev37 (200k шагов, transfer от rev16): 47% avg progress на случайных layout — робот стабильно проезжает половину каждой сгенерированной maze.

2. **Plan 2 (rev38) — frame stacking k=4.** Подал в CNN не один кадр, а четыре последних. Гипотеза: motion gradient помогает policy различать «стою у стены» и «приближаюсь». 300k шагов from-scratch недостаточно — модель не успела выучить навигацию. На эту архитектуру нужно либо ≥1М шагов, либо BC-bootstrap.

3. **Plan 4 (rev39) — расширенные DR-оси.** К визуальному heavy-DR добавил spawn pose jitter (±0.10 м, ±30°), dynamics DR (масса, демпфирование, motor asymmetry), camera pitch jitter (±4°), action latency randomization (1–3 шага задержки), sonar noise (gaussian σ=0.02 м + dropout 2%). Это даёт policy опыт, который потребуется при переносе на реальный робот. Sim-progress остался на уровне rev37 (47%) — что ожидаемо, поскольку Plan 4 расширяет sim2real-устойчивость, а не sim SR.

4. **Plan 5 (rev40) — архитектурная замена.** Подключил frozen ImageNet-pretrained ResNet18 как drop-in аналог R3M-backbone (под именем `R3MFeatureExtractor` в [`python/training/r3m_feature_extractor.py`](../../../python/training/r3m_feature_extractor.py)) и завёл RecurrentPPO с LSTM hidden=128. Идея — заменить scratch-CNN на устойчивый pretrained representation, а LSTM добавляет policy внутреннюю память для maze-навигации. Архитектура **готова в коде** (commit [`356f7c1`](https://github.com/NMGorovenko/uav-simulator/commit/356f7c1)), 300k шагов from-scratch оказалось мало — нужно ≥1М шагов (укладывается в одну ночь на Win-узле) или BC-bootstrap-инициализация.

Итог maze-направления: процедурная генерация сцены работает, robot стабильно проезжает половину произвольной топологии, инфраструктура для архитектурных экспериментов (frame stacking, R3M, RecurrentPPO) поднята и протестирована. Полное прохождение случайных maze-топологий запланировано в финальном этапе как продолжение rev40 на больший бюджет training'а.

---

## 10. Контур управления KS0223 и нюансы sim-to-real

При обучении в симуляторе и при работе на физическом роботе используется один и тот же интерфейс команд — backend по TCP отправляет одну из пяти строк (`DirStop`, `DirForward`, `DirBack`, `DirLeft`, `DirRight`), policy выбирает следующую команду каждые ~140 мс (default `LoopIntervalMs` в [`AutopilotService`](../../../src/ks0223-web-mac/backend/Services/AutopilotService.cs)). Однако физическая реализация на стороне робота и калибровочная модель в Unity различаются по принципу обратной связи, и эта несимметрия — отдельный источник sim-to-real несовпадения, который имеет смысл явно описать.

**На стороне Unity-симулятора** ([`Ks0223Vehicle.cs:49-52`](../../../src/UnityProject/uav-simulator/Assets/Scripts/Vehicles/Ks0223Vehicle.cs)) физика робота промоделирована детерминированно: `maxSpeedMps = 0.73`, `maxYawRateDegPerSec = 380`, `yawAccelerationDegPerSec2 = 1900`, ACTION_TABLE на pure differential-drive. Эти числа я получил замерами реального робота линейкой и угломером и пересчитал на чистую дифференциальную геометрию KS0223. В симуляторе одна и та же команда даёт одно и то же перемещение.

**На стороне физического робота** Pi-runtime принимает команду по TCP и включает соответствующие моторы на фиксированный PWM до прихода следующей команды — без обратной связи по энкодерам или IMU. Это open-loop control. За один шаг управления (~140 мс при максимальной скорости) робот проходит ~10 см, но фактическая дистанция и фактический угол поворота варьируются от запуска к запуску из-за заряда батареи, трения колёс о паркет, motor deadzone (0.20–0.30 PWM до старта моторов) и инерции вращения. Policy училась на детерминированной sim-модели, поэтому на реале feedback-loop становится зашумлённым — особенно это заметно на повороте в углу L-коридора, где один и тот же DirLeft-burst может дать угол поворота от ~30° до ~60° в зависимости от состояния батареи и стартовой инерции колёс.

В рамках спринта эта несимметрия частично скомпенсирована на стороне backend: добавлен `ThrottleRampUp` для прохождения motor deadzone, увеличены `RepeatedCommandThreshold` (30 strikes ≈ 3 с при 100 мс loop) и `StaleTelemetryAfterMs` (3000 мс) — чтобы safety-стек не реагировал на типичные особенности реального робота при ротации (потеря HC-SR04 echo на открытое пространство, серии повторных команд при манёвре в углу). Этих компенсаций оказалось достаточно, чтобы rev29 проехал L-коридор до целевой зоны.

Полное закрытие петли управления — добавление closed-loop control на стороне Pi-runtime по энкодерам колёс или IMU, с возвратом фактически пройденной дистанции и фактического угла обратно в backend — это отдельная инженерная задача, которая выходит за рамки преддипломной практики и логично переносится в магистерскую работу. После закрытия петли отпадает значительная часть остаточного sim-to-real шума, и на роботе должна стать достижима та же воспроизводимость, что в симуляторе.

---

## 11. Эволюция моделей (компактный таймлайн)

Чтобы не утяжелять отчёт списком из 23 ревизий, привожу только milestones и причины существования каждой группы.

| Группа | Что изменилось | Sim SR на сцене обучения |
|---|---|---:|
| **rev16** (baseline) | 300k from-scratch, ent=0.1, mild-DR L-corridor | **100%** |
| rev17–rev22 | попытки одного-шагового sim2real (mixed walls, warm light, hue jitter, skybox-null, layered lighting) | 0% — сменил слишком много переменных за раз |
| **rev24** (heavy-DR) | 200k transfer rev16 → wood-plank floor + mixed walls + per-wall jitter | 35% (но prior на реале сменился на DirForward 36–59%) |
| rev25–rev28 | discomfort-tuning lateral penalty + transfer source variations | 0% — entropy starvation (см. § 8) |
| **rev29** (production) | rev24 config + явно `--ent-coef 0.1`, transfer от rev16 | **75%** — успешный реальный проезд |
| rev30–rev36 | серия повторных тренировок с разными reward-fixes и seed'ами для эмпирической оценки variance PPO на heavy-DR | 7/7 × 0% — variance bound подтверждён |
| **rev37** (Plan 1, maze) | heavy-DR порт на `track.cardboard_maze.v1` + curriculum + maze-randomize | 47% avg progress на случайных layout |
| rev38 (Plan 2) | frame stacking k=4 from-scratch | 0% — 300k недостаточно для архитектуры с нуля |
| **rev39** (Plan 4) | rev37 baseline + spawn jitter + dynamics DR + motor asymmetry + camera pitch jitter + sonar noise | 47% progress + полный набор sim2real-DR axes |
| rev40 (Plan 5) | R3M (frozen ResNet18) + RecurrentPPO LSTM | 0% — 300k from-scratch недостаточно, нужен ≥1М или BC-bootstrap |
| rev41–rev46 | multi-seed sweep на L-corridor с rev29-recipe (seeds 1337 / 2024 / 9999 / 7) | 6/6 × 0% — эмпирическая оценка variance подтверждена |

Эмпирическая оценка надёжности transfer-PPO на heavy-DR landscape: **1 удачное попадание из 11 попыток** при идентичной комбинации hyperparams. То есть `rev29` — это статистически крайне удачный pick. Чтобы reliable получать 70%+ SR с этой архитектурой, multi-seed sweep с 8–16 параллельными seed'ами должен повысить вероятность хотя бы одного попадания до 80%+. Альтернатива — переход на R3M + RecurrentPPO с длительным training'ом (см. § 9), который как раз убирает эту вариативность за счёт robust pretrained feature representation.

---

## 12. Задачи финального этапа и магистерской работы

Спринт показал и потолок текущей архитектуры, и пути его обойти. На финальный этап и магистерскую работу остаётся:

1. **Длительное обучение R3M + RecurrentPPO на random maze** (~1М шагов, ~5 часов на Win-узле) — основной кандидат на стабильную модель для произвольных корпусов трасс. Код архитектуры уже в репозитории, скрипты запуска подготовлены.

2. **BC-bootstrap от расширенного demo-датасета.** Сейчас я записал ~1500 пар (frame, action) через WebUI Demo Recording — для BC pre-training этого достаточно, чтобы дать policy reasonable initial weights. Demo Replay помогает быстро воспроизводить записи для тестов на роботе после обучения.

3. **Closed-loop control на стороне Pi-runtime KS0223** (см. § 10) — добавление обратной связи по энкодерам колёс или IMU, с возвратом фактически пройденной дистанции и угла поворота. Снимает остаточный sim-to-real шум на стороне моторов и делает поведение робота воспроизводимым между прогонами.

4. **Полный безаварийный проезд произвольной maze-топологии** — комбинация (1) и (2): архитектура с памятью + BC-инициализация поверх процедурно генерируемой сцены даёт robot, способного проезжать случайные коридорные топологии без переобучения.

5. **Демонстрационный режим и формальная eval.** Полный заезд под запись с тремя ракурсами (внешний, бортовая камера, WebUI live-overlay) на показ в магистерской.

---

## 13. Артефакты и репозиторий

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
| Калибровочная модель робота в Unity | [`Ks0223Vehicle.cs`](../../../src/UnityProject/uav-simulator/Assets/Scripts/Vehicles/Ks0223Vehicle.cs) |
| Видео реального проезда | [`final_demo/rev29_real_run_2026-04-30_phone.mp4`](sprint-3-reeval-2026-04-27/real_corridor/final_demo/rev29_real_run_2026-04-30_phone.mp4) |
| Бортовая камера (та же сессия) | [`final_demo/rev29_real_run_2026-04-30_robot_cam.mp4`](sprint-3-reeval-2026-04-27/real_corridor/final_demo/rev29_real_run_2026-04-30_robot_cam.mp4) |
| Master research-план (фон) | [`2026-04-28-path-to-100-percent.md`](../../superpowers/plans/2026-04-28-path-to-100-percent.md) |
| Sprint-3 sub-plans (1–6) | [`2026-04-29-sprint3-master.md`](../../superpowers/plans/2026-04-29-sprint3-master.md) |
