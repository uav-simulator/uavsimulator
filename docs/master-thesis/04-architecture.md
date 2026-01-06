## Purpose
Архитектура симулятора и границы модулей (что production, что research).

## Assumptions
- Архитектура должна поддерживать расширение ТС/сенсоров/экспериментов без переписывания ядра.

## Decisions
- Описать:
  - модули (Environment, Vehicle, API, Experiments, Plugins);
  - границы директорий (Core/Vehicles vs AI);
  - контракт взаимодействия между модулями.

## Next steps
- Синхронизировать этот документ с `docs/architecture.md` после появления кода.
