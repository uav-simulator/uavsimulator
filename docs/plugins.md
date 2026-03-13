## Назначение
Зафиксировать текущую plugin-архитектуру симулятора и правила её расширения без разрастания core-слоя.

## Текущая схема
- Реестр плагинов хранится в `Assets/Resources/UavSimulator/PluginRegistry.asset`.
- Дополнительные descriptor assets автоматически подхватываются из:
  - `Assets/Resources/UavSimulator/Plugins/Vehicles`
  - `Assets/Resources/UavSimulator/Plugins/Tracks`
- `PluginRegistry.Load()` объединяет:
  - curated registry asset;
  - auto-discovery из `Resources/UavSimulator/Plugins`.
- Если assets отсутствуют или каталог пустой, включается runtime fallback через `BuiltinPluginFactory`.

## Состав vehicle plugin
- `VehiclePluginDescriptor`
  - идентификатор машинки;
  - человекочитаемое имя;
  - ссылка на prefab с `VehicleBase`-совместимым runtime компонентом;
  - ссылка на `DeviceContractDescriptorAsset`.
- `DeviceContractDescriptorAsset`
  - сенсоры;
  - актуаторы;
  - observation/action schema.
- Runtime-реализация
  - prefab с `Rigidbody`, collider и компонентом-наследником `VehicleBase`;
  - либо fallback-строитель в `BuiltinPluginFactory` для встроенных профилей.

## Состав track plugin
- `TrackPluginDescriptor`
  - `trackId`, `displayName`, `description`;
  - optional prefab;
  - JSON schema для `trackParams`.

## Текущий asset-based каталог
### Машинки
- `vehicle.ks0223.v1`
- `vehicle.ks0223.arcade.blue.v1`
- `vehicle.ks0223.arcade.red.v1`
- `vehicle.ks0223.arcade.gray.v1`
- `vehicle.ks0223.arcade.purple.v1`
- `vehicle.drone.simple.v1`

### Треки
- `track.basic_arena.v1`
- `track.roadsystem_arena.v1`
- `track.roadsystem_realistic.v2`

## Где лежат assets
- Реестр: `Assets/Resources/UavSimulator/PluginRegistry.asset`
- Контракты: `Assets/Resources/UavSimulator/Contracts`
- Машинки: `Assets/Resources/UavSimulator/Plugins/Vehicles`
- Треки: `Assets/Resources/UavSimulator/Plugins/Tracks`

## Синхронизация built-in каталога
Для встроенных профилей добавлен editor utility:

```text
UavSimulator/Plugins/Sync Builtin Plugin Catalog
```

Он создаёт или обновляет:
- `PluginRegistry.asset`
- `VehiclePluginDescriptor`
- `TrackPluginDescriptor`
- `DeviceContractDescriptorAsset`

CLI-эквивалент:

```bash
"/Applications/Unity/Hub/Editor/6000.1.8f1/Unity.app/Contents/MacOS/Unity" \
  -projectPath "src/UnityProject/uav-simulator" \
  -batchmode -quit \
  -executeMethod UavSimulator.EditorTools.PluginCatalogSeeder.SyncBuiltinPluginCatalog
```

## Диагностика
- Проверить каталог можно через:
  - `GET /contract`
  - `rusim inspect vehicle`
  - popup выбора машинки в web UI
- Если descriptor asset создан, но машинка не появляется:
  - проверить, что asset лежит под `Assets/Resources/UavSimulator/Plugins/...`;
  - проверить уникальность `id`;
  - проверить, что prefab содержит runtime компонент, наследующий `VehicleBase`.
