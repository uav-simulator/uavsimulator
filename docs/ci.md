## Purpose
Зафиксировать требования к CI для проекта (проверки качества и воспроизводимости).

## Assumptions
- CI должен быть минимальным и не требовать Unity Editor в ранней стадии, если это не подтверждено инфраструктурой.

## Decisions
- Минимальный набор проверок:
  - проверка форматирования/стиля для `.cs` и `.md` (на уровне репозитория);
  - проверка целостности структуры (наличие ключевых документов, задач, индексов);
  - (после появления тестов) запуск Unity Test Framework в batchmode.
  - smoke-проверка `make demo-proof-ci` с graceful skip, если Unity не в Play / ROS не запущен.

## Next steps
- CI платформа: GitHub Actions.
- Unity тесты: через GameCI `unity-test-runner`, требует `UNITY_LICENSE` secret (job пропускается, если secret не задан).
- Python: минимальная проверка импорта клиента из `python/`.
- Добавить notebook-smoke в CI (без Unity — graceful skip, с Unity — полный прогон).
