# Машинки


## Актуальные `vehicleId`
- `vehicle.prometeo.sport.v1`
- `vehicle.arcade.blue.v1`
- `vehicle.arcade.red.v1`
- `vehicle.arcade.gray.v1`
- `vehicle.arcade.purple.v1`
- `vehicle.drone.simple.v1`

## Общие требования к vehicle plugin
- runtime-компонент наследуется от `VehicleBase`;
- prefab содержит нужную физику и коллайдеры;
- descriptor лежит в `Assets/Resources/UavSimulator/Plugins/Vehicles`;
- контракт устройства лежит в `Assets/Resources/UavSimulator/Contracts`.

## Базовые каналы управления
Для наземных машинок продуктовый слой использует:
- `throttle`
- `steer`
- `brake`

Дополнительные каналы могут идти через `ControlCommand.extensions`, например:
- `drive.left_pwm_norm`
- `drive.right_pwm_norm`

## Базовые наблюдения
Текущий каталог машинок использует набор сенсоров и телеметрии вокруг:
- камеры;
- скорости;
- дальномера;
- line tracker;
- powertrain estimate.

## Как добавить новую машинку
Пошаговая инструкция вынесена отдельно:
- [Как добавить новую машинку](plugin-vehicle-guide.md)
