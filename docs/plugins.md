# Плагины

Plugin architecture используется для расширения каталога машинок и треков без изменения внешнего product API.

## Как устроен каталог

Каталог строится из трех источников:
- `Assets/Resources/UavSimulator/PluginRegistry.asset`
- descriptor assets в `Assets/Resources/UavSimulator/Plugins/`
- fallback из `BuiltinPluginFactory`, если assets недоступны

Это позволяет:
- держать стабильные product IDs;
- использовать единый каталог в runtime, CLI и contract discovery;
- расширять каталог без ручной правки внешних интерфейсов.

## Vehicle plugin

Vehicle plugin содержит:
- `id`
- `displayName`
- `description`
- prefab с `VehicleBase`
- `DeviceContractDescriptorAsset`

## Track plugin

Track plugin содержит:
- `id`
- `displayName`
- `description`
- optional prefab
- JSON schema для параметров окружения

## Актуальный каталог машинок

- `vehicle.prometeo.sport.v1`
- `vehicle.arcade.blue.v1`
- `vehicle.arcade.red.v1`
- `vehicle.arcade.gray.v1`
- `vehicle.arcade.purple.v1`
- `vehicle.drone.simple.v1`

Legacy IDs `vehicle.ks0223.*` не считаются каноническими для симуляторного каталога.

## Актуальный каталог треков

- `track.basic_arena.v1`
- `track.roadsystem_arena.v1`
- `track.roadsystem_realistic.v2`

## Проверка каталога

Через runtime API:
- `GET /contract`
- `GET /health`

Через CLI:

```bash
rusim list tracks --base-url http://127.0.0.1:8000
rusim list vehicles --base-url http://127.0.0.1:8000
rusim inspect vehicle vehicle.prometeo.sport.v1 --base-url http://127.0.0.1:8000
```

## Установка пользовательских плагинов

```bash
rusim plugin install ./my-plugin.rusim-plugin.zip
rusim plugin list
rusim plugin remove vehicle.custom.racer.v1
```

Это относится к пользовательскому каталогу поверх built-in плагинов runtime.

## Editor utility

Для синхронизации built-in каталога:

```text
UavSimulator/Plugins/Sync Builtin Plugin Catalog
```

Batchmode-вызов:

```bash
"/Applications/Unity/Hub/Editor/6000.1.8f1/Unity.app/Contents/MacOS/Unity" \
  -projectPath "src/UnityProject/uav-simulator" \
  -batchmode -quit \
  -executeMethod UavSimulator.EditorTools.PluginCatalogSeeder.SyncBuiltinPluginCatalog
```

## Связанные страницы
- [Архитектура](architecture.md)
- [CLI `rusim`](cli.md)
- [Машинки](vehicles.md)
- [Как добавить новую машинку](plugin-vehicle-guide.md)
