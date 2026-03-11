## Purpose
Зафиксировать требования к CI для проекта (проверки качества и воспроизводимости).

## Assumptions
- CI должен быть минимальным и не требовать Unity Editor в ранней стадии, если это не подтверждено инфраструктурой.

## Decisions
- Минимальный набор проверок:
  - сборка backend (`.NET`);
  - сборка frontend (`Vite/TypeScript`);
  - Python import-check для SDK/bridge;
  - запуск Unity Test Framework в batchmode, если задан `UNITY_LICENSE`;
  - smoke-проверка `make demo-proof-ci` с graceful skip, если Unity не в Play / ROS не запущен;
  - публикация стартовой документационной страницы через GitHub Pages отдельным workflow.

## Next steps
- CI платформа: GitHub Actions.
- Pages: публикация `docs/site` через GitHub Pages.
- Unity тесты: через GameCI `unity-test-runner`, требует `UNITY_LICENSE` secret (job пропускается, если secret не задан).
- Python: минимальная проверка импорта клиента из `python/`.
- Добавить notebook-smoke в CI (без Unity — graceful skip, с Unity — полный прогон).
- Следующий шаг: добавить docs validation и smoke-check для операторского backend API.
