## Purpose
Зафиксировать контракт и управление для базового drone-plugin `vehicle.drone.simple.v1`.

## Assumptions
- Плагин ориентирован на демонстрации и ранние эксперименты (не high-fidelity аэродинамика).
- Используется общий API симулятора (`/reset`, `/step`) без специальных transport-изменений.

## Device Id
- `vehicle.drone.simple.v1`

## Sensors
- `camera.front.rgb` (`sensor.camera.rgb`, `jpeg`, `120x160`)
- `altimeter` (`sensor.altimeter`, `m`)
- `airspeed` (`sensor.airspeed`, `m/s`)
- `powertrain.estimate` (`sensor.powertrain`)

## Actuators
- `drone.pitch_norm` (`[-1..1]`)
- `drone.yaw_norm` (`[-1..1]`)
- `drone.thrust_norm` (`[-1..1]`)

## Control Notes
- Базовые поля `throttle/steer/brake` также поддерживаются:
  - `throttle` -> pitch
  - `steer` -> yaw
  - `brake` -> thrust mapping
- Предпочтительный путь: использовать extensions `drone.*`.

## Telemetry Keys
- `sensor.altimeter.m`
- `sensor.airspeed.mps`
- `sensor.vertical_speed.mps`
- `power.battery.voltage_v`
- `power.motor.estimated_w`

