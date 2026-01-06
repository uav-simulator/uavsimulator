## Purpose
Зафиксировать фактический стек проекта и ключевые зависимости.

## Assumptions
- Источник правды по пакетам: `src/UnityProject/uav-simulator/Packages/manifest.json`.

## Decisions
- Unity Editor: `6000.1.8f1` (см. `ProjectSettings/ProjectVersion.txt`).
- Render pipeline: URP `17.1.0`.
- Input: `com.unity.inputsystem` `1.14.0`.
- Navigation: `com.unity.ai.navigation` `2.0.8`.
- ML-Agents: запланирован, но не указан в `manifest.json` на текущий момент.
- Языки/инструменты: C# (Unity), Python (обучение/эксперименты), Jupyter (ноутбуки).

## Next steps
- Зафиксировать версию ML-Agents при подключении и обновить этот документ.
