# 3 Реализация ключевых компонентов

## 3.1 Подходы к реализации

Глава 2 фиксирует архитектуру платформы — границы контуров, транспортные контракты, расположение модулей в репозитории. Настоящая глава спускается на уровень кода: разбираются конкретные классы, методы и инварианты, обеспечивающие работу четырёх контуров. Цель раздела — показать, как архитектурные решения превращаются в исполняемый код, и какие компромиссы при этом приняты. Изложение опирается на актуальные исходники из `src/UnityProject/uav-simulator/Assets/Scripts/`, `src/ks0223-web-mac/backend/Services/` и `src/ks0223-web-mac/frontend/src/`; все цитируемые имена классов и методов существуют в дереве на момент написания работы и могут быть проверены поиском.

### 3.1.1 Принципы декомпозиции по ответственности

При реализации платформы выдержан общий принцип: один класс — одна ответственность, и эта ответственность формулируется в терминах внешнего наблюдателя, а не внутреннего устройства. `SimulationManager` отвечает за жизненный цикл активной симуляции: что в сцене сейчас существует, как оно реагирует на `reset` и `step`. `PluginRegistry` отвечает за каталог доступных плагинов и порядок их слияния из разных источников. `BuiltinPluginFactory` отвечает за создание встроенных плагинов и runtime-instance конкретного транспортного средства или трассы. `Ks0223Vehicle` отвечает за физическую и сенсорную модель робота. `RuntimeSessionManager` на стороне backend отвечает за пользовательскую сессию и маршрутизацию команд в одну из двух подложек. `AutopilotService` отвечает за фон inference loop и взаимодействие с `AutopilotSafetyFilter`. Каждый из перечисленных классов остаётся в своих границах: `SimulationManager` не знает о существовании HTTP-сервера, `PluginRegistry` не знает о Rigidbody, `AutopilotService` не знает о JSON-сериализации сцены. Такое разделение позволяет независимо тестировать и эволюционировать модули.

Критерий проверки декомпозиции — на код-ревью: если изменение одной функциональности затрагивает более двух классов, либо распределение ответственностей нарушено, либо вводится новая ответственность, для которой нет дома. Так появилась потребность вынести `AutopilotSafetyFilter` из `AutopilotService` отдельно — фильтр безопасности оказался самодостаточной сущностью с собственным состоянием (счётчик E-stop, фаза hold, ramp-up); его держание внутри inference loop делало бы тесты безопасности неотличимыми от тестов inference, что неудобно.

### 3.1.2 Использование DI и singleton-сервисов на стороне backend

Backend-контур построен на стандартной для ASP.NET Core схеме внедрения зависимостей через `IServiceCollection` и регистрации сервисов в `Program.cs`. Доменные сервисы — `RuntimeSessionManager`, `AutopilotService`, `AutopilotSafetyFilter`, `ModelRegistryService`, `SessionLogger`, `SessionVideoRecorder`, `DemoReplayService`, `TelemetryParser` — регистрируются как singleton, поскольку их состояние обслуживает один процесс backend и должно сохраняться между HTTP-запросами. Контроллеры (`Ks0223Controller`, `AutopilotController`, `ModelsController`, `LogsController`, `DemoReplayController`) регистрируются по умолчанию как scoped и получают зависимости через конструктор. SignalR-хаб `TelemetryHub` получает `RuntimeSessionManager` через `IHubContext` инверсию, чтобы сервис не зависел от хаба напрямую и публиковал телеметрию вне зависимости от наличия активных подключений.

Singleton-семантика влечёт два инварианта, выдерживаемых в коде явно. Первый — потокобезопасность: сервисы, обслуживающие параллельные HTTP-запросы и фоновые таймеры, защищают изменяемое состояние через `lock` (`AutopilotService.gate`, `DemoReplayService.stateLock`), `SemaphoreSlim` (`SessionLogger.writeLock`, `RuntimeSessionManager.lifecycleLock`) или используют `ConcurrentDictionary` (`RuntimeSessionManager.realSessions`, `RuntimeSessionManager.unityWorlds`). Второй — управляемая инициализация. Сервисы, требующие настройки в момент старта приложения, реализуют `IHostedService` (так сделано для `RuntimeSessionManager`), что позволяет хосту корректно вызвать `StartAsync` и `StopAsync` в нужные фазы жизненного цикла, в частности — при остановке backend закрыть активные сессии и выгрузить ONNX-сессии модели.

Конфигурация сервисов вынесена в options-классы (`PiConnectionOptions`, `CameraOptions`, `SensorBridgeOptions`, `LoggingOptions`, `AutopilotSafetyOptions`), а они привязаны к секциям `appsettings.json` через `IOptions<T>`. Эта схема позволяет хранить настройки в текстовом виде, переопределять их переменными окружения (через стандартный механизм ASP.NET Core) и подменять в тестах без модификации кода.

### 3.1.3 Lifecycle-управление через MonoBehaviour и ScriptableObject в Unity

Unity-контур опирается на две существенно различные базы — `MonoBehaviour` для объектов сцены и `ScriptableObject` для дескрипторов и ассетов. `MonoBehaviour` живёт в иерархии сцены, имеет `Awake`/`Start`/`Update`/`FixedUpdate` колбэки и завязан на `GameObject`. На этом уровне реализованы `SimulationManager`, `Ks0223Vehicle`, `BasicArenaTrack`, `CityPolygonTrack`, `TrafficLight`, `TrafficLightController`, `TrafficLightPolygonAdapter`, `TrafficLightAwareController`. `ScriptableObject` живёт как ассет на диске, не имеет позиции в сцене и используется для описаний, не зависящих от runtime-состояния. На этом уровне реализованы `VehiclePluginDescriptor`, `TrackPluginDescriptor`, `PluginRegistryAsset`, `DeviceContractDescriptorAsset`. Различие принципиальное: дескриптор плагина — это данные о возможностях, а не сама возможность, поэтому он не нуждается в физическом теле в сцене и может быть свободно загружен из `Resources` в момент запуска.

Жизненный цикл `MonoBehaviour`-объектов в Unity нелинеен: `Awake` вызывается при инстанцировании префаба, `Start` — перед первым кадром, `OnEnable`/`OnDisable` — при изменении активности, `OnDestroy` — при уничтожении. Это обстоятельство учитывается в `Ks0223Vehicle.Awake`, где компонент идемпотентно создаёт `Rigidbody` и сенсорную камеру; в `BasicArenaTrack.Awake` и `CityPolygonTrack.Awake`, где трасса собирается через `BuildIfNeeded` с ленивой инициализацией; в `TrafficLightPolygonAdapter.OnEnable` и `OnDisable`, где подписка на `TrafficLight.StateChanged` устанавливается симметрично с отпиской при выключении компонента. Каждое из этих правил — следствие нелинейного жизненного цикла; нарушение порождает либо двойную инициализацию (сценарий `Awake` после `Reset`), либо утечку обработчика события (подписка без отписки приводит к фантомным вызовам при перезагрузке сцены).

## 3.2 SimulationManager: ядро жизненного цикла

### 3.2.1 Состояния и переходы

### 3.2.2 ResetWithConfig: применение сценария

### 3.2.3 Step: цикл управления и наблюдения

### 3.2.4 Управление множеством агентов

## 3.3 PluginRegistry и BuiltinPluginFactory

### 3.3.1 Two-tier реестр и merge-логика

### 3.3.2 BuiltinPluginFactory как декларативный каталог

### 3.3.3 Procedural-fallback для отсутствующих ассетов

### 3.3.4 RuntimeMaterialCompatibility: конверсия Built-in в URP

## 3.4 Реализация Vehicle: Ks0223Vehicle

### 3.4.1 Физический контур: Rigidbody и интегрирование скоростей

### 3.4.2 Сенсоры: камера, ультразвук, line tracker

### 3.4.3 Применение ControlCommand: маппинг на физические каналы

### 3.4.4 Презентационные visual-режимы

## 3.5 Реализация Track: BasicArenaTrack и CityPolygonTrack

### 3.5.1 Procedural arena как минимальный track

### 3.5.2 CityPolygonTrack: загрузка стороннего ассета через AssetDatabase

### 3.5.3 Светофоры: TrafficLight FSM, Controller, Adapter, Aware controller

## 3.6 Backend services

### 3.6.1 RuntimeSessionManager: маршрутизация в две подложки

### 3.6.2 UnityKs0223RuntimeProvider: HTTP-клиент к Unity

### 3.6.3 AutopilotService: inference loop

### 3.6.4 AutopilotSafetyFilter: deadman, sonar E-stop, dropout

### 3.6.5 ModelRegistryService: lifecycle ONNX-артефакта

### 3.6.6 SessionLogger и SessionVideoRecorder

### 3.6.7 DemoReplayService: воспроизведение записанных сессий

## 3.7 Frontend: операторский пульт

### 3.7.1 Архитектура tabs и SignalR

### 3.7.2 ControlPad: keyboard и виртуальный джойстик

### 3.7.3 Camera panel и MJPEG streaming

### 3.7.4 Scenario picker: выбор сценария из YAML

## 3.8 Качество реализации

### 3.8.1 Тестовое покрытие

### 3.8.2 Линтинг и компиляция

### 3.8.3 Производительность и узкие места
