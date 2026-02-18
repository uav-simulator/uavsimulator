## Purpose
Зафиксировать правила расширения симулятора (плагины/модули) без “разрастания” ядра.

## Assumptions
- Плагины должны быть отключаемыми и не требовать ручной правки сцен для включения/выключения.

## Decisions
- Плагины выделяются в отдельные папки и подключаются через конфигурацию.
- Настройки плагинов хранятся в конфиг-ассетах (ScriptableObject), чтобы минимизировать кодовые изменения.
- При отсутствии plugin assets загружается встроенный runtime fallback (`BuiltinPluginFactory`) для локальной проверки API/сцены.
- Robot plugin включает:
  - Unity prefab (визуал + физика + `VehicleBase`);
  - `DeviceContractDescriptorAsset` (сенсоры/актуаторы/схемы);
  - runtime adapter (опционально) для реального железа.
- Для реального робота (KS0223) используется отдельный hardware adapter, который не меняет core API и может быть отключён.
- ROS2 поддержка оформлена как отдельный bridge-plugin слой:
  - Unity host (`Ros2BridgeProcessHost`) поднимает внешний process bridge;
  - bridge читает/пишет через текущий HTTP API и публикует/подписывается в ROS2 топики;
  - core контракты `SimulationConfig/ControlCommand/StepResult` не меняются.

## Next steps
- Формат регистрации (runtime): `Resources` (чтобы работало в build без AssetDatabase).
  - Опционально: `PluginRegistry` asset в `Assets/Resources/UavSimulator/PluginRegistry.asset`.
  - Альтернатива: отдельные descriptors в `Assets/Resources/UavSimulator/Plugins/` и загрузка через `Resources.LoadAll`.
- Добавить lifecycle hook’и для hardware adapter (`connect`, `apply`, `read`, `disconnect`) и использовать их только в plugin scope.
