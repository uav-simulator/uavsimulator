# Технологии

## Назначение
Эта страница фиксирует фактический стек проекта и роль каждой технологии в общей системе.

Источник правды по Unity-пакетам:
- `src/UnityProject/uav-simulator/Packages/manifest.json`

## Ядро симулятора и runtime
### Unity
- Unity Editor `6000.1.8f1`
- язык: `C#`
- роль:
  - сцена и физика;
  - vehicle/track plugins;
  - камера и телеметрия;
  - внешний HTTP runtime API.

### Unity packages
- URP `17.1.0`
- `com.unity.inputsystem` `1.14.0`
- `com.unity.ai.navigation` `2.0.8`

ML-Agents пока рассматривается как отдельный следующий слой и не является обязательной текущей product-core зависимостью.

## Operator stack
### Backend
- `ASP.NET Core`
- `.NET 8`
- `SignalR`
- роль:
  - unified runtime orchestration;
  - адаптер к реальному роботу;
  - адаптер к Unity runtime;
  - health / telemetry / camera / logs.

### Frontend
- `React`
- `TypeScript`
- `Vite`
- `MUI`
- роль:
  - единый операторский Web UI для `unity-sim` и `real-robot`.

## Research layer
### Python
- `Python 3.11+`
- `requests`
- `PyYAML`
- роль:
  - SDK;
  - CLI;
  - notebooks;
  - bridge tooling;
  - research automation.

### Jupyter
- роль:
  - интерактивные smoke-сценарии;
  - визуализация телеметрии;
  - подготовка training/inference экспериментов.

### ROS2
- дистрибутив: `Humble`
- роль:
  - interoperability-слой;
  - публикация typed topics;
  - интеграция с `RViz`, `rqt` и внешними robotics-инструментами.

## Transport и API
### HTTP JSON API
- роль:
  - текущий канонический transport для Unity runtime;
  - база для Python SDK и CLI.

### TCP / UDP
- роль:
  - транспорт реального стенда;
  - прием камеры и телеметрии в physical runtime path.

### SignalR
- роль:
  - realtime-обновления в operator UI.

## DevOps и документация
### GitHub
- `GitHub Actions`
- `GitHub Pages`
- роль:
  - CI/CD;
  - публикация wiki/documentation layer.

### MkDocs Material
- роль:
  - генерация markdown-first документации для GitHub Pages.

### Docker
- роль:
  - контейнерный запуск web-controller;
  - изоляция ROS2 desktop toolchain на macOS.

## Принцип выбора технологий
Используемый стек должен оставаться:

1. достаточно простым для воспроизводимого запуска;
2. достаточно модульным для plugin-first архитектуры;
3. достаточно стабильным для sim-to-real сценариев.
