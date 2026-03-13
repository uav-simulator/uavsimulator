## Purpose
Подробно зафиксировать текущую архитектуру, роли инструментов и рабочие потоки (включая sequence-диаграммы).

## Assumptions
- Базовый transport-контур симулятора: HTTP JSON API.
- ROS2 используется как внешний interoperability-слой.
- Проект должен оставаться расширяемым для новых роботов/треков через плагины.

## Decisions
- Core Unity runtime не зависит от ROS2 пакетов.
- Bridge и UI-инструменты ROS2 запускаются отдельно от Unity.
- Контракт сенсоров/актуаторов ориентирован на терминологию CARLA/ROS2.

## 1) Контекст системы
```mermaid
flowchart LR
    User["Engineer"] --> Unity["Unity Editor Play"]
    User --> Notebook["Jupyter Notebook"]
    Unity --> Api["HTTP JSON API"]
    Notebook --> Api
    Api --> SimCore["SimulationManager"]
    SimCore --> Plugins["PluginRegistry"]
    Plugins --> Track["Track Plugin"]
    Plugins --> Robot["Robot Plugin KS0223"]
    User --> RosBridge["ROS2 Bridge"]
    RosBridge --> Api
    RosBridge --> RosTopics["ROS2 Topics"]
    RosTopics --> Rviz["RViz2"]
    RosTopics --> Rqt["rqt tools"]
```

## 2) Компоненты Unity runtime
```mermaid
flowchart TB
    Bootstrap["RuntimeSceneBootstrap"] --> SimMgr["SimulationManager"]
    Bootstrap --> ApiHost["HttpJsonApiHost"]
    Bootstrap --> RosHost["Ros2BridgeProcessHost (optional)"]

    ApiHost --> ApiServer["HttpJsonSimulatorApiServer"]
    ApiServer --> Facade["SimulatorApiFacade"]
    Facade --> SimMgr

    SimMgr --> Registry["PluginRegistry.Load()"]
    Registry --> Builtin["BuiltinPluginFactory fallback"]
    SimMgr --> ActiveTrack["TrackBase instance"]
    SimMgr --> ActiveVehicle["VehicleBase instance"]

    ActiveVehicle --> Sensors["Camera + telemetry"]
    ActiveVehicle --> Actuators["PWM / throttle / steer / brake"]
```

## 3) Sequence: HTTP training/control loop
```mermaid
sequenceDiagram
    participant Client as "Python Client / Notebook"
    participant Api as "HttpJsonSimulatorApiServer"
    participant Facade as "SimulatorApiFacade"
    participant MainThread as "UnityMainThreadDispatcher"
    participant Sim as "SimulationManager"
    participant Vehicle as "VehicleBase"

    Client->>Api: POST /reset (SimulationConfig)
    Api->>MainThread: enqueue Reset
    MainThread->>Facade: Reset(config)
    Facade->>Sim: ResetSimulation(config)
    Sim-->>Facade: StepResult(state + frame)
    Facade-->>Api: StepResult
    Api-->>Client: JSON reset response

    loop each control step
        Client->>Api: POST /step (ControlCommand)
        Api->>MainThread: enqueue Step
        MainThread->>Facade: Step(command)
        Facade->>Sim: Step(command)
        Sim->>Vehicle: ApplyControl(command)
        Vehicle-->>Sim: ReadState + CameraFrame
        Sim-->>Facade: StepResult
        Facade-->>Api: StepResult
        Api-->>Client: JSON step response
    end
```

## 4) Sequence: ROS2 bridge loop
```mermaid
sequenceDiagram
    participant RosCmd as "rqt / ros2 topic pub"
    participant Bridge as "ros2_bridge.py"
    participant Api as "HTTP API /step"
    participant Sim as "SimulationManager"
    participant Topics as "ROS2 typed topics"
    participant UI as "RViz / rqt_image_view"

    RosCmd->>Bridge: cmd_vel / cmd_drive
    loop tick at rate_hz
        Bridge->>Api: POST /step (mapped PWM command)
        Api->>Sim: Step(command)
        Sim-->>Api: state + telemetry + frame
        Api-->>Bridge: StepResult
        Bridge->>Topics: publish odom / battery / range / image
        Topics-->>UI: visualize
    end
```

## 5) Sequence: plugin bootstrap
```mermaid
sequenceDiagram
    participant Boot as "RuntimeSceneBootstrap"
    participant Sim as "SimulationManager"
    participant Registry as "PluginRegistry"
    participant Builtin as "BuiltinPluginFactory"
    participant Scene as "Active Scene"

    Boot->>Sim: EnsureSimulationManager()
    Sim->>Registry: Load()
    alt registry assets found and valid
        Registry-->>Sim: PluginRegistrySnapshot
    else assets missing or empty
        Registry->>Builtin: CreateSnapshot()
        Builtin-->>Sim: fallback snapshot
    end
    Sim->>Scene: Instantiate track + vehicle
```

## 6) Инструменты и зачем они нужны
| Инструмент | Зачем нужен | Как используется |
|---|---|---|
| Unity Editor (`6000.1.8f1`) | Физика, сцена, визуализация, runtime API | `make sim` или `make sim-public`, затем `Play` |
| `SimulationManager` | Центр reset/step/контракта | Создает активные track/vehicle и собирает state/frame |
| Plugin Registry | Расширяемость под разные роботы/треки | Подгружает `Resources`, fallback через `BuiltinPluginFactory` |
| HTTP JSON API | Универсальный канал управления для Python/bridge | `/health`, `/contract`, `/reset`, `/step` |
| Python SDK (`sim_client`) | Скрипты обучения/сбора данных | Клиент к HTTP API, парсинг telemetry |
| Jupyter Notebook | Презентация и ручные эксперименты | Кадры, графики сенсоров, интерактивные команды |
| ROS2 bridge (`ros2_bridge.py`) | Совместимость с ROS2 экосистемой | Преобразует `step` в typed topics и обратно через cmd topics |
| RViz2 | 3D/2D визуализация ROS данных | Odom + camera topic visualization |
| rqt (`image_view`, `publisher`, `robot_steering`) | Оперативный UI для камеры/команд | Тест ручного управления и потоков сенсоров |
| Docker ROS2 Desktop | Изоляция ROS2 среды на macOS | `make ros-up` + `make ros-ui-container` |
| Makefile | Developer automation и ROS2/demo orchestration | Локальный инженерный workflow, smoke и bridge-контур |
| `rusim` CLI | Канонический product lifecycle и runtime tooling | `server up/down/status`, `scenario`, `runtime`, `step` |

## 7) Границы ответственности
- Unity Core: симуляция, физика, plugin runtime, HTTP API.
- Python Tools: эксперименты, датасеты, презентации.
- ROS2 Layer: внешний interoperability и UI-инструменты robotics-экосистемы.

## Next steps
- Добавить watchdog / emergency stop канал для bridge.
- Вынести часть telemetry в custom ROS2 msg package.
- Добавить автоматический e2e smoke (Unity Play + step loop + ROS topic check).
