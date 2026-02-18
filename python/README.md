# Python SDK (черновик)

## Purpose
Минимальный клиент для управления симулятором из Python через HTTP JSON fallback.

## Assumptions
- Unity сцена содержит `SimulationManager` и `HttpJsonApiHost`.
- Сервер слушает `http://127.0.0.1:<port>`.

## Decisions
- Транспорт: HTTP JSON (fallback) для ранней стадии; gRPC добавляется отдельно.

## Next steps
- Добавить потоковую доставку кадров камеры (`dataRef`/stream).

## Быстрый старт
1) Создать виртуальное окружение и установить зависимости:
   - `python3 -m venv .venv`
   - `source .venv/bin/activate`
   - `pip install -r requirements.txt`
2) Запустить примеры из папки `python/`:
   - `python examples/random_agent.py --base-url http://127.0.0.1:8000`
   - `python examples/ks0223_random_pwm.py --base-url http://127.0.0.1:8000`

## ROS2 bridge (optional)
- Скрипт bridge: `python/bridges/ros2_bridge.py`
- Запуск:
  - `python python/bridges/ros2_bridge.py --base-url http://127.0.0.1:8000 --namespace /uavsim/ks0223 --rate-hz 15 --reset-on-start`
- Требуется ROS2 Python среда (`rclpy`, `std_msgs`, `sensor_msgs`).
