## Purpose
Определить целевой API симулятора для MVP: управление ТС и получение наблюдений/состояния.

## Assumptions
- API используется как стабильный контракт между симулятором и внешними системами (обучение, эксперименты, тесты).
- Текущие реализации могут меняться, но контракт должен быть минимальным и явно версионируемым.

## Decisions
- MVP API включает:
  - Reset/Start эпизода (детерминированный сброс).
  - Step: применение действий и получение наблюдений.
  - Доступ к численному состоянию (скорость, базовые метрики).
  - События/флаги завершения эпизода (коллизии/сход/лимиты).
- API проектируется без привязки к конкретной сцене/объектам; источники данных подтверждаются аудитом сцены.

### Decision Record: Время и формат кадров камеры
Assumptions:
- Внешний клиент (Python) должен синхронизировать команды/наблюдения и уметь хранить датасеты.

Decisions:
- `timeBase`: по умолчанию `unix_ms` (миллисекунды Unix epoch) для межпроцессной совместимости; `sim_ms` допускается как вторичный формат при необходимости.
- `CameraFrame`: для MVP HTTP fallback возвращается в `StepResult.frame` в `jpeg+base64`; поле `dataRef` зарезервировано под будущий внешний stream.

Rationale:
- `unix_ms` проще согласовать между Unity и Python и сопоставлять с логами/артефактами.
- `StepResult.frame` позволяет быстро стартовать с одним HTTP каналом без отдельного транспорта.

### Decision Record: Расширяемый action/state контракт (KS0223 baseline)
Assumptions:
- Симулятор должен оставаться расширяемым (разные типы роботов и разные приводы).
- Первый реальный робот: Keyestudio KS0223 (Raspberry Pi car), где удобно использовать дифференциальное управление моторами.

Decisions:
- Базовый `ControlCommand` остаётся общим для всех роботов:
  - `throttle` в диапазоне `[-1..1]`;
  - `steer` в диапазоне `[-1..1]`;
  - `brake` в диапазоне `[0..1]`.
- Добавлено поле `ControlCommand.extensions[]` (массив `key/value`) для robot-specific каналов.
- Добавлено поле `VehicleState.telemetry[]` (массив `key/value`) для robot-specific телеметрии.
- Для KS0223 (CARLA/ROS2-friendly naming) используются ключи:
  - actions: `drive.left_pwm_norm`, `drive.right_pwm_norm`, `control.throttle_norm`, `control.steer_norm`, `control.brake_norm`.
  - telemetry: `sensor.speedometer.mps`, `sensor.ultrasonic.front.m`,
    `sensor.line_tracker.s1_norm`..`sensor.line_tracker.s5_norm`,
    `power.battery.voltage_v`, `power.battery.current_a`, `power.motor.estimated_w`.
- Правило приоритета для Vehicle plugin:
  - если заданы `drive.left_pwm_norm` и `drive.right_pwm_norm`, используется дифференциальный канал;
  - иначе применяется базовый канал `throttle/steer/brake`.

Rationale:
- Базовый API не ломается и остаётся единым для всех плагинов.
- Реальный робот может использовать свои каналы без разрастания core DTO.
- Это упрощает sim2real и поддержку новых платформ через адаптеры.

## Next steps
- Зафиксировать первую стабильную версию key-space для `extensions/telemetry` (v1).
- Добавить в Python SDK helper-обёртки для KS0223 (`left/right pwm`, telemetry parsing).
- Добавить бинарный транспорт кадров (`dataRef`) для снижения нагрузки от base64.

## HTTP JSON (fallback) — текущий скелет
Assumptions:
- Это временный транспорт “точно компилируется/работает локально” до gRPC.

Decisions:
- Эндпоинты:
  - `GET /health`
  - `GET /contract`
  - `POST /reset` (body: `SimulationConfig`)
  - `POST /step` (body: `ControlCommand`)
- Реализация: TCP listener + минимальный HTTP парсер, выполнение Unity-операций через main-thread dispatcher.

Next steps:
- Определить схему `dataRef` для кадров (отдельный endpoint/stream).
