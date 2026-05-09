# Вклад в проект

Минимальные правила работы с репозиторием, чтобы проект развивался предсказуемо.

## Repo layout

- `src/UnityProject/uav-simulator/` — Unity проект (C#, открывается через Unity Editor 6000.1.x).
- `src/ks0223-web-mac/{backend,frontend}/` — операторский стек: ASP.NET Core backend + React/Vite UI.
- `packages/com.uav-simulator.plugin-sdk/` — публичный Plugin SDK (UPM-пакет).
- `python/` — `sim_client` (HTTP-клиент + `rusim` CLI), `training/` (RL pipeline), `bridges/` (ROS2).
- `configs/scenarios/` — YAML-сценарии для `rusim scenario`.
- `docs/` — MkDocs Material источник, деплоится на GitHub Pages.

## Local setup

```bash
# Python
cd python && pip install -e ".[test,dev]"            # минимум для тестов и линтеров
cd python && pip install -e ".[test,dev,training]"   # полный стек, включая torch + SB3 (~700MB)

# Frontend
cd src/ks0223-web-mac/frontend && npm ci

# Pre-commit (одноразово)
pip install pre-commit && pre-commit install
```

## Quality gates (= что гоняет CI)

| Слой       | Команда                                              |
|------------|------------------------------------------------------|
| Python     | `cd python && ruff check . && pytest`                |
| Backend    | `dotnet build src/ks0223-web-mac/backend/backend.csproj` |
| Frontend   | `cd src/ks0223-web-mac/frontend && npm run build`    |
| CLI smoke  | `make plugin-smoke-track`                            |
| Scenarios  | `for f in configs/scenarios/*.yaml; do rusim scenario validate "$f"; done` |
| Docs       | `mkdocs build --strict`                              |

`pre-commit run --all-files` гоняет ruff + ruff-format + prettier + базовые
checks (см. `.pre-commit-config.yaml`).

## Commit hygiene

- Не коммитим: crash dumps, `.DS_Store`, `Library/`, `Temp/`, `Logs/`,
  локальные `appsettings.Development.json` с реальными IP, секреты.
- Идентификаторы — на английском; русский — только в комментариях/документации.
- Сообщения коммитов — императивная форма, без сторонних подписей.
- Доки и `mkdocs.yml` пушим на `develop` — Pages workflow деплоит автоматически.

## Branching

- Основная ветка разработки — `develop`. Релизные тэги (`vMAJOR.MINOR.PATCH`)
  ставятся на `develop` после прохождения CI и сборки релизных артефактов
  через `release-rusim.yml` / `release-manifest.yml`.
- Feature-ветки именуются `feat/<scope>` или `fix/<scope>` и сливаются в
  `develop` через PR (squash-merge при возможности).

## Documentation

- Каждая значимая фича обновляет `CHANGELOG.md` (раздел `[Unreleased]`)
  и, при необходимости, соответствующую страницу `docs/`.
- Главы магистерской диссертации (`docs/master-thesis/`) фиксируют только
  актуальное состояние кода — при breaking-change'ах обновлять синхронно
  с реализацией.
