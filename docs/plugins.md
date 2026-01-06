## Purpose
Зафиксировать правила расширения симулятора (плагины/модули) без “разрастания” ядра.

## Assumptions
- Плагины должны быть отключаемыми и не требовать ручной правки сцен для включения/выключения.

## Decisions
- Плагины выделяются в отдельные папки и подключаются через конфигурацию.
- Настройки плагинов хранятся в конфиг-ассетах (ScriptableObject), чтобы минимизировать кодовые изменения.

## Next steps
- Формат регистрации (runtime): `Resources` (чтобы работало в build без AssetDatabase).
  - Опционально: `PluginRegistry` asset в `Assets/Resources/UavSimulator/PluginRegistry.asset`.
  - Альтернатива: отдельные descriptors в `Assets/Resources/UavSimulator/Plugins/` и загрузка через `Resources.LoadAll`.
- После появления ядра определить минимальные lifecycle hook’и плагина.
