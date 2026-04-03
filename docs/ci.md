# CI/CD


## CI
Основной workflow:

```text
.github/workflows/ci.yml
```

Он выполняет:
- backend build (`.NET`);
- frontend build (`Vite`);
- Python import check;
- Unity tests через GameCI, если задан `UNITY_LICENSE`;
- `make demo-proof-ci` как graceful smoke job.

## GitHub Pages
Публикация документации живёт отдельно:

```text
.github/workflows/pages.yml
```

Workflow:
- собирает `mkdocs build --strict`;
- загружает `.mkdocs-site`;
- деплоит GitHub Pages.

## Release workflow
Runtime manifest:

```text
.github/workflows/release-manifest.yml
```

Что делает:
- принимает `tag`;
- генерирует `rusim-release-manifest.json`;
- прикладывает manifest в GitHub Release.

Rusim package:

```text
.github/workflows/release-rusim.yml
```

Что делает:
- реагирует на тег `v*` или ручной запуск;
- синхронизирует версию Python package с тегом;
- собирает `.whl` и `.tar.gz`;
- публикует их в GitHub Release.

## Ограничения текущего контура
- runtime build не собирается в GitHub Actions;
- release runtime публикуется из локально собранного `.app`;
- Unity test job зависит от наличия `UNITY_LICENSE`.

## Связанные страницы
- [Сборка и локальный запуск](build.md)
- [Поставка и обновление](release-distribution-model.md)
