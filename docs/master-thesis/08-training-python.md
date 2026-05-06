# 6 Программная обвязка обучения

Главы 4 и 5 описывают платформу как набор runtime-контуров — Unity, backend, Web UI и плагинная подложка. Настоящая глава смотрит на ту же платформу со стороны исследователя машинного обучения: она разбирает Python-обвязку, которая превращает Unity-симулятор в источник наблюдений для Stable-Baselines3, оркестрирует параллельный запуск, выполняет PPO, сохраняет артефакты и возвращает обученную ONNX-модель в backend через тот же контракт, что и операторский интерфейс. Изложение опирается на исходники из `python/sim_client/` и `python/training/`; все цитируемые имена существуют в репозитории на момент написания работы и проверяются поиском.

Важная оговорка касается границ ответственности раздела. Глава описывает программную обвязку обучения, а не результаты sim-to-real-переноса — последние выделены в главу 7 настоящей работы. Когда речь заходит о наблюдаемых проблемах сходимости при тяжёлой доменной рандомизации, материал ограничивается описанием реализации и фиксацией факта: подбор архитектуры и гиперпараметров находится в исследовательской фазе, и часть обсуждаемых решений принята именно ради повышения воспроизводимости при отрицательном результате обучения, а не для оптимистичной презентации успехов.

## 6.1 Связь Python-контура с симулятором

### 6.1.1 SimClient как единственная точка получения наблюдений

Все взаимодействия Python-кода с Unity-runtime проходят через один класс — `SimClient` (`python/sim_client/http_client.py`). Это сознательное архитектурное решение: для всей тренировочной обвязки Unity существует исключительно как HTTP-сервер на `http://127.0.0.1:8000`, отвечающий по контракту `runtime API`, описанному в главе 6 диссертации. Никакая часть тренировочного кода не импортирует Unity-сборку напрямую, не разделяет с ней процессное пространство и не использует общую память — связь идёт только через сетевые запросы.

```python
class SimClient:
    def __init__(self, base_url: str, timeout_s: float = 10.0):
        self.base_url = base_url
        self.timeout_s = timeout_s
        self.session = requests.Session()
        adapter = HTTPAdapter(pool_connections=4, pool_maxsize=16, max_retries=0)
        self.session.mount("http://", adapter)
        self.session.mount("https://", adapter)

    def health(self) -> Dict[str, Any]: ...
    def get_contract(self) -> Dict[str, Any]: ...
    def reset(self, config: Dict[str, Any]) -> Dict[str, Any]: ...
    def step(self, command: Dict[str, Any]) -> Dict[str, Any]: ...
```

Использование `requests.Session` с явно сконфигурированным `HTTPAdapter` решает практическую проблему, выявленную при ранних запусках на Windows. В Python без сессии каждый HTTP-запрос порождает новое TCP-соединение; при тренировке со скоростью порядка сотни шагов в секунду на четыре параллельных Unity-инстанции пул эфемерных портов исчерпывается за несколько минут (TIME_WAIT накапливается быстрее, чем освобождается), и `SubprocVecEnv`-воркеры начинают падать с `WinError 10055` или `EOFError`. Постоянная сессия и keep-alive снимают эту проблему: одно соединение переиспользуется на всё время эпизода. Параметр `pool_maxsize=16` подобран эмпирически — это потолок одновременных вызовов от одного Python-процесса к одному Unity-серверу, наблюдаемый при N агентах в `MultiAgentVisionVecEnv`.

Класс намеренно содержит минимальный набор методов и не вводит уровней абстракции поверх HTTP. Все семантические интерпретации — поля наблюдения, поля команды, форматы JPEG-кадров, идентификаторы агентов — остаются на стороне environment-классов из `python/training/`. `SimClient` отвечает только за то, что строка превратилась в HTTP-запрос, дождалась ответа и вернула распарсенный JSON. Такое разделение позволяет переиспользовать клиент в трёх существенно разных контекстах: тренировочном цикле, KPI-оценке `evaluate_ab_policy.py` и в инструментах командной строки `rusim doctor`/`rusim contract`. Все три контекста используют один и тот же класс, не пересекаясь по бизнес-логике.

Помимо тренировочных endpoint-ов клиент содержит методы для работы с реестром моделей backend: `list_models`, `get_active_model`, `activate_model`, `set_model_binding`, `upload_model`. Эти методы не используются во время обучения — они нужны на этапе доставки артефакта, но размещены в том же классе сознательно, потому что ровно тот же `SimClient` инструменты `rusim model install` и `rusim model activate` используют для загрузки готовой ONNX-модели в backend. Единая точка контакта с двумя серверами — Unity-runtime и backend — упрощает обработку ошибок и таймаутов.

### 6.1.2 Семантика reset и step

Контракт между Python-контуром и Unity сводится к двум методам: `reset` и `step`. Семантика этих методов на стороне Unity описана в главе 5 («SimulationManager: ядро жизненного цикла»); здесь рассматривается, как тренировочная обвязка с ними работает.

`reset(config)` принимает структуру `SimulationConfig` в JSON-форме: идентификаторы трассы и транспортного средства, параметры трассы, параметры агента, флаги и seed. Метод синхронен — Python-вызов блокируется, пока Unity не завершит уничтожение прошлой сцены, инстанцирование новой, применение параметров и первый snapshot. Возвращается полный `StepResult`, который содержит начальное наблюдение для policy и пустой `info` (на стороне Unity это вырожденный шаг с командой `(0, 0)`). Тренировочный код использует возвращаемое значение `reset` непосредственно — никакой дополнительной фазы прогрева нет.

`step(command)` принимает `ControlCommand` — пару (`throttle`, `steer`) для непрерывных моделей, либо одно из дискретных значений `DirStop/Forward/Back/Left/Right`, переданное через wrapper. Возвращаемый `StepResult` содержит поле `state.camera.frame` (base64-кодированный JPEG камеры primary-агента), массив `telemetry`, поля `state.pose` (позиция и ориентация Rigidbody) и `info` со служебными ключами (`frame_id`, `agent_count`, `route.completed`, `route.distance_m`).

```python
def reset(self, *, seed=None, options=None):
    step = self.client.reset(self._reset_config)
    obs, _ = self._build_observation(step)
    self._steps = 0
    self._last_progress = 0.0
    return obs, {}

def step(self, action):
    cmd = {"throttle": float(action[0]), "steer": float(action[1])}
    step = self.client.step(cmd)
    obs, info = self._build_observation(step)
    reward, terminated, truncated = self._compute_reward(step, action)
    self._steps += 1
    return obs, reward, terminated, truncated, info
```

Принципиальное свойство этой пары — синхронность. Тренировочный цикл PPO работает в стандартном Gymnasium-режиме: «получили obs, посчитали действие, отправили action, дождались следующего obs». Никаких асинхронных колбэков, очередей телеметрии или потоковой подписки на стороне Python нет. Все хитрости параллелизма вынесены в `MultiAgentVisionVecEnv` и `MetaMultiAgentVecEnv`, которые работают над тем же синхронным контрактом, просто отправляя в одну итерацию пакеты команд для нескольких агентов.

Пакетирование на стороне backend требует от Python-кода соблюдать одно правило: между `step` для разных агентов одного Unity-инстанса не должна вмешиваться команда `step` без `targetAgentId`. Когда такое случается, Unity воспринимает команду как обращение к primary-агенту, а primary-агент мог быть закрыт прошлой эпизодической терминацией. На уровне `MultiAgentVisionVecEnv` каждый шаг отдельного агента сопровождается явным `targetAgentId`; primary-команды не используются.

### 6.1.3 Загрузка сценария и параметризация запуска

Источником `SimulationConfig` для всех тренировочных запусков служит файл сценария в формате YAML. Сценарии размещены в `configs/scenarios/`; типичный пример — `cardboard-corridor-v1.yaml`, описывающий узкий коридор шириной 0.6 м, KS0223-агента и waypoint-маршрут «A → B». Загрузку и валидацию сценариев выполняет модуль `python/sim_client/scenario.py`.

```python
def load_scenario_file(path: str | Path) -> Dict[str, Any]:
    file_path = Path(path)
    suffix = file_path.suffix.lower()
    raw = file_path.read_text(encoding="utf-8")
    if suffix == ".json":
        payload = json.loads(raw)
    elif suffix in {".yaml", ".yml"}:
        if yaml is None:
            raise RuntimeError("PyYAML is required to load YAML scenarios.")
        payload = yaml.safe_load(raw)
    else:
        raise ValueError(f"Unsupported scenario file extension: {suffix}")
    if not isinstance(payload, dict):
        raise ValueError("Scenario document must be a JSON/YAML object.")
    return payload
```

Функция `validate_scenario` дополнительно проверяет согласованность полей: наличие `runtime.runtimeMode`, ключа `world.trackId`, типов поля `vehicle.vehicleId`, корректности списка `route.waypoints`, согласованности счётчика `agents.count` с массивом `agents.vehicles`. Валидация происходит на стороне Python до отправки конфигурации в Unity — это позволяет получить осмысленную диагностику с указанием конкретного отсутствующего поля, а не безличное «schema validation failed» от Unity-runtime.

Преобразование загруженного сценария в `SimulationConfig`-payload выполняет `scenario_to_reset_config`. Значения по умолчанию для `time_scale`, `max_steps`, `oob_margin_m` берутся из CLI-аргументов тренировочного скрипта; параметры маршрута и геометрии трассы — из секций `route.params` и `route.waypoints`. Сценарий тем самым служит «паспортом эксперимента»: пара (commit, scenario file) однозначно описывает, в какой среде шло обучение. Этот паспорт сохраняется в `metadata.json` рядом с моделью под ключом `scenario`.

Помимо сценариев тренировочный пайплайн принимает дополнительные параметры через CLI. Их три категории. Первая — параметры запуска Unity (адрес, порт, число параллельных инстанций); они влияют на физическое расположение HTTP-серверов, но не на содержимое симуляции. Вторая — параметры обучения (число шагов, гиперпараметры PPO, опции wrapper-стека); они меняют только Python-сторону. Третья — параметры доменной рандомизации (`spawn-jitter-m`, `ultrasonic-noise-sigma`, `lateral-penalty-mult`); они частично попадают в Unity через `trackParams` и частично остаются в env-обёртках. Разделение полностью описано в `parse_args()` главного тренировочного скрипта; в файле `metadata.json` зафиксированы все три категории, что обеспечивает воспроизводимость.

## 6.2 Обёртки сред: единый VecEnv-интерфейс

### 6.2.1 Одиночная среда (ABCorridorVisionEnv)

### 6.2.2 Multi-agent в одной Unity-инстанции (MultiAgentVisionVecEnv)

### 6.2.3 Meta-multi-agent: N Unity на M агентов (MetaMultiAgentVecEnv)

### 6.2.4 Genesis-вариант для GPU-параллельной симуляции

### 6.2.5 Wrappers: DiscreteActionWrapper, image augmentation, latency

## 6.3 Тренировочный пайплайн на Stable-Baselines3

### 6.3.1 PPO как основной алгоритм

### 6.3.2 train_cardboard_corridor_v9.py: общий поток

### 6.3.3 Гиперпараметры и их обоснование

### 6.3.4 Курикулум и мониторинг через EvalCallback

## 6.4 Domain randomization и подготовка к sim-to-real

### 6.4.1 Heavy-DR: текстуры, освещение, спавны

### 6.4.2 Frame stacking и историческая память

### 6.4.3 Real-camera post-processing

## 6.5 KPI-оценка и evidence

### 6.5.1 evaluate_ab_policy.py: 20-эпизодный success rate

### 6.5.2 Структура evidence-папки и воспроизводимость

### 6.5.3 Сравнение revisions (rev16 → rev30+)

## 6.6 Экспорт ONNX-артефакта и связь с backend

### 6.6.1 torch.onnx.export: тонкие места

### 6.6.2 metadata.json и metrics.json: формат

### 6.6.3 rusim model install / activate / bind

## 6.7 Тестовое покрытие

### 6.7.1 Wrapper-тесты (DiscreteAction, image augmentation, latency, anti-spin reward)

### 6.7.2 Multi-runtime launch test

### 6.7.3 Resume curriculum test
