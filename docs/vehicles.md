## Purpose
Зафиксировать требования к ТС для MVP и правила добавления новых моделей транспорта.

## Assumptions
- Физическая модель и сенсоры будут согласованы отдельно; этот документ фиксирует требования к составу.

## Decisions
- Минимальный состав ТС (MVP):
  - геометрия и коллайдеры;
  - физическая модель движения;
  - сенсоры: камера + численное состояние + plugin telemetry;
  - интерфейс управления: базовый (`throttle/steer/brake`) + расширения через `ControlCommand.extensions`.
- Ассеты:
  - runtime prefab машинки размещается в обычных Unity asset folders;
  - descriptor asset машинки хранится в `Assets/Resources/UavSimulator/Plugins/Vehicles`;
  - контракт хранится в `Assets/Resources/UavSimulator/Contracts`;
  - каталог подхватывается через `PluginRegistry.asset` + auto-discovery из `Resources`.
- Профиль первого реального робота: Keyestudio KS0223.
  - Добавляется как `VehiclePluginDescriptor`, а не как исключение в ядре.
  - Для KS0223 целевой канал управления: `drive.left_pwm_norm`/`drive.right_pwm_norm` (дифференциальный привод).
  - Базовый канал `throttle/steer/brake` остаётся fallback для сима и универсальных агентов.
  - Сенсоры/термины выравниваются с CARLA/ROS2-подходом:
    - `sensor.camera.rgb`, `sensor.speedometer`, `sensor.range`, `sensor.line_tracker`, `sensor.powertrain`.
- Дополнительно добавлен базовый drone профиль:
  - `vehicle.drone.simple.v1` (MVP квадрокоптер для расширяемости архитектуры).

## Next steps
- Добавить отдельный runtime adapter для KS0223 (Raspberry Pi), который переводит контракт симулятора в GPIO/PWM команды робота.
- Зафиксировать минимальный профиль телеметрии KS0223 в `VehicleState.telemetry`.
- После этого провести первые тесты sim2real на малой скорости и с аварийным стопом.

## Практический поток добавления новой машинки
- Реализовать runtime-компонент, наследующий `VehicleBase`.
- Собрать prefab с физикой и нужными дочерними объектами.
- Создать `DeviceContractDescriptorAsset`.
- Создать `VehiclePluginDescriptor` и положить его в `Assets/Resources/UavSimulator/Plugins/Vehicles`.
- Проверить, что машинка появилась в `GET /contract` и в web UI.

Подробный пошаговый гайд:
- см. `docs/plugin-vehicle-guide.md`.
