# Плагины

**Что это**  
Обзор текущей plugin-архитектуры симулятора и актуального каталога машинок и треков.

**Для кого**  
Для разработчика, который добавляет новый track или vehicle plugin.

**Статус**  
Каноническая reference-страница по plugin catalog.

**Проверено по**  
`Assets/Resources/UavSimulator/PluginRegistry.asset`, `Assets/Resources/UavSimulator/Plugins/`, `Assets/Scripts/Plugins/`

## Как устроен каталог
- Основной реестр: `Assets/Resources/UavSimulator/PluginRegistry.asset`
- Дополнительные descriptor assets автоматически подхватываются из:
  - `Assets/Resources/UavSimulator/Plugins/Vehicles`
  - `Assets/Resources/UavSimulator/Plugins/Tracks`
- Если assets недоступны, runtime может использовать fallback из `BuiltinPluginFactory`.

## Vehicle plugin
Содержит:
- `id`
- `displayName`
- `description`
- prefab с `VehicleBase`
- `DeviceContractDescriptorAsset`

## Track plugin
Содержит:
- `trackId`
- `displayName`
- `description`
- optional prefab
- JSON schema для `trackParams`

## Актуальный каталог машинок
- `vehicle.prometeo.sport.v1`
- `vehicle.arcade.blue.v1`
- `vehicle.arcade.red.v1`
- `vehicle.arcade.gray.v1`
- `vehicle.arcade.purple.v1`
- `vehicle.drone.simple.v1`

Legacy IDs `vehicle.ks0223.*` больше не считаются каноническими для симуляторного каталога.

## Актуальный каталог треков
- `track.basic_arena.v1`
- `track.roadsystem_arena.v1`
- `track.roadsystem_realistic.v2`

## Синхронизация built-in каталога
Editor utility:

```text
UavSimulator/Plugins/Sync Builtin Plugin Catalog
```

Batchmode вызов:

```bash
"/Applications/Unity/Hub/Editor/6000.1.8f1/Unity.app/Contents/MacOS/Unity" \
  -projectPath "src/UnityProject/uav-simulator" \
  -batchmode -quit \
  -executeMethod UavSimulator.EditorTools.PluginCatalogSeeder.SyncBuiltinPluginCatalog
```

## Проверка каталога
- `GET /contract`
- `rusim list tracks`
- `rusim list vehicles`
- `rusim inspect vehicle ...`

## Связанные страницы
- [Машинки](vehicles.md)
- [Как добавить новую машинку](plugin-vehicle-guide.md)
