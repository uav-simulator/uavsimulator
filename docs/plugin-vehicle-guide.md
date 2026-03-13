## Назначение
Эта страница описывает практический процесс создания новой машинки и её подключения к симулятору без правок core API.

## Что считается plugin-машинкой
Новая машинка в проекте состоит из трёх частей:
- runtime-компонент Unity, наследующий `VehicleBase`;
- prefab с физикой и визуалом;
- descriptor assets:
  - `VehiclePluginDescriptor`;
  - `DeviceContractDescriptorAsset`.

## Шаг 1. Реализовать runtime-компонент
Создай компонент-наследник `VehicleBase`, например:

```csharp
public sealed class MyCarVehicle : VehicleBase
{
    public override void ApplyControl(ControlCommand command) { }
    public override VehicleState ReadState() => base.ReadState();
    public override bool TryReadCameraFrame(out CameraFrame frame)
    {
        frame = null;
        return false;
    }
}
```

Минимум, который должен уметь runtime-компонент:
- принимать `ControlCommand`;
- возвращать `VehicleState`;
- по возможности отдавать `CameraFrame`;
- корректно отрабатывать `ResetVehicle`.

## Шаг 2. Собрать prefab
Prefab должен содержать:
- корневой `GameObject`;
- collider;
- `Rigidbody`, если машинка использует физику;
- твой runtime-компонент (`MyCarVehicle`);
- optional camera sensor, visual model, дополнительные сенсоры.

Критично:
- `SimulationManager` ищет `VehicleBase` внутри prefab;
- если в prefab нет `VehicleBase`, плагин не будет создан.

## Шаг 3. Создать контракт устройства
Создай `DeviceContractDescriptorAsset` и опиши в нём:
- `deviceId`;
- `deviceType`;
- sensors;
- actuators;
- `observationSchemaJson`;
- `actionSchemaJson`.

Пример назначения:
- камера: `sensor.camera.rgb`
- скорость: `sensor.speedometer`
- дальномер: `sensor.range`
- дифференциальные моторы: `drive.left_pwm_norm`, `drive.right_pwm_norm`

## Шаг 4. Создать descriptor машинки
Создай `VehiclePluginDescriptor` и заполни:
- `id`
- `displayName`
- `description`
- `prefab`
- `deviceContract`

Размещать descriptor нужно по пути:

```text
Assets/Resources/UavSimulator/Plugins/Vehicles
```

Это важно, потому что runtime подхватывает машинки через `Resources`.

## Шаг 5. Подключить машинку к каталогу
Есть два режима:

### Вариант A. Auto-discovery
Если descriptor asset лежит в `Assets/Resources/UavSimulator/Plugins/Vehicles`, он будет найден автоматически.

### Вариант B. Curated registry
Если нужно зафиксировать каталог явно, добавь asset в:

```text
Assets/Resources/UavSimulator/PluginRegistry.asset
```

Текущая загрузка объединяет:
- entries из `PluginRegistry.asset`;
- entries из `Resources/UavSimulator/Plugins`.

Поэтому новая машинка может быть подключена без правки core-кода.

## Шаг 6. Проверить подключение
Проверка через runtime:

```bash
./rusim server start --mode background --port 8000
curl -s http://127.0.0.1:8000/contract
```

Проверка через web UI:
- открыть popup выбора машинки;
- убедиться, что новая машинка появилась в списке;
- выбрать её и выполнить reset/connect.

## Шаг 7. Если нужна sim-to-real интеграция
Если новая машинка должна работать не только в Unity, но и с реальным стендом:
- сохраняй тот же `deviceId` и sensor/action schema;
- делай отдельный hardware adapter вне Unity core;
- не меняй `SimulationConfig`, `ControlCommand`, `StepResult` ради одной модели.

Правильный путь:
- симулятор и реальная машинка делят один контракт;
- transport/runtime adapter отличается, а не API.

## Для встроенных профилей проекта
Для текущих built-in машинок есть utility:

```text
UavSimulator/Plugins/Sync Builtin Plugin Catalog
```

Он генерирует и обновляет:
- `PluginRegistry.asset`
- `VehiclePluginDescriptor`
- `TrackPluginDescriptor`
- `DeviceContractDescriptorAsset`

CLI-вызов:

```bash
"/Applications/Unity/Hub/Editor/6000.1.8f1/Unity.app/Contents/MacOS/Unity" \
  -projectPath "src/UnityProject/uav-simulator" \
  -batchmode -quit \
  -executeMethod UavSimulator.EditorTools.PluginCatalogSeeder.SyncBuiltinPluginCatalog
```

## Что не надо делать
- не добавляй специальные ветки `if vehicleId == ...` в core-код без необходимости;
- не храни descriptor assets вне `Resources`, если машинка должна работать в build/runtime;
- не подменяй общий контракт локальными полями только для одной модели;
- не завязывай plugin registration на ручную правку сцены.
