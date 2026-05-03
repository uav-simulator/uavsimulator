# Defense Artifacts

Артефакты для защиты магистерской: установочные плагины, демо-видео, скриншоты.

## Plugins

### `vehicle.arcade.green.v1.rusim-plugin.zip`

**Назначение:** live-demo установки нового плагина во время защиты.

**Размер:** ~2 KB. Содержит `manifest.json`, `descriptor.json`, `device-contract.json`, `README.md`.

**Использование (procedure для демо):**

```bash
# 1. Показать текущий список — только built-in плагины (9 штук).
rusim plugin list

# 2. Установить новый плагин из архива.
rusim plugin install docs/report/master-thesis/defense-artifacts/plugins/vehicle.arcade.green.v1.rusim-plugin.zip

# 3. Показать список снова — `vehicle.arcade.green.v1` появляется с тегом [user].
rusim plugin list

# 4. (после демо) Удалить, чтобы вернуться в чистое состояние.
rusim plugin remove vehicle.arcade.green.v1
```

**Verified end-to-end на dev-машине 2026-05-03:** install, list, remove работают корректно. Плагин корректно регистрируется в `~/.rusim/plugin-registry.json` (или project-local `.rusim/plugins/` при наличии) и появляется с `[user]` source-тегом.

**Ограничение:** плагин зарегистрирован в реестре, но фактический spawn машины в сценарии требует регистрации в `BuiltinPluginFactory.cs` (соответствующий C# branch). Без этого попытка спавна `vehicle.arcade.green.v1` через scenario YAML не отрисует машинку. Для defense-демо это допустимо: демонстрируется install workflow + появление в `plugin list`, не визуальный спавн. Если для защиты нужен визуальный спавн — добавить ID + prefab branch в `BuiltinPluginFactory.cs` (см. `ArcadeBlueVehicleId` как образец) — это ~10 минут работы в Unity-проекте.
