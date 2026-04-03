# Как добавить робота

Короткая внутренняя инструкция по добавлению нового robot plugin.

## Что нужно создать

1. Компонент Unity на базе `VehicleBase`.
2. Prefab робота.
3. `DeviceContractDescriptorAsset`.
4. `VehiclePluginDescriptor`.

## Компонент runtime

Минимальный шаблон:

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

Компонент должен:
- принимать `ControlCommand`;
- возвращать `VehicleState`;
- по возможности отдавать `CameraFrame`;
- корректно отрабатывать `ResetVehicle`.

## Prefab

Prefab должен содержать:
- корневой `GameObject`;
- collider;
- `Rigidbody`, если робот использует физику;
- твой runtime-компонент (`MyCarVehicle`);
- optional camera sensor, visual model, дополнительные сенсоры.

Критично:
- `SimulationManager` ищет `VehicleBase` внутри prefab;
- если в prefab нет `VehicleBase`, плагин не будет создан.

## Контракт устройства

В `DeviceContractDescriptorAsset` нужно описать:
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

## Descriptor

В `VehiclePluginDescriptor` нужно заполнить:
- `id`
- `displayName`
- `description`
- `prefab`
- `deviceContract`

Размещать descriptor нужно по пути:

```text
Assets/Resources/UavSimulator/Plugins/Vehicles
```

## Подключение к каталогу

Есть два варианта:

### Auto-discovery
Если descriptor asset лежит в `Assets/Resources/UavSimulator/Plugins/Vehicles`, он будет найден автоматически.

### Curated registry
Если нужен явный список, добавь asset в:

```text
Assets/Resources/UavSimulator/PluginRegistry.asset
```

Текущая загрузка объединяет:
- entries из `PluginRegistry.asset`;
- entries из `Resources/UavSimulator/Plugins`.

## Проверка

Проверка через runtime:

```bash
./rusim server up --mode background --port 8000
curl -s http://127.0.0.1:8000/contract
```

Если робот нужен и в sim-to-real контуре:
- сохраняй тот же `deviceId` и sensor/action schema;
- делай отдельный hardware adapter вне Unity core;
- симулятор и реальный робот должны делить один контракт управления и наблюдений.

## Встроенный каталог

Для built-in профилей есть utility:

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
