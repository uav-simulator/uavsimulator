# Release Manifest

**Что это**  
Машинно-читаемый JSON-документ, который описывает опубликованный runtime release.

**Для кого**  
Для разработчика release-потока и для `rusim upgrade`.

**Статус**  
Текущая схема manifest, используемая в проекте.

**Проверено по**  
`scripts/generate_release_manifest.py`, `.github/workflows/release-manifest.yml`, `python/sim_client/cli.py`

## Назначение
Manifest нужен, чтобы:
- описать доступные release assets;
- дать `rusim upgrade` стабильную точку входа;
- отделить локальный runtime registry от публичной дистрибуции.

## Текущий формат
```json
{
  "schemaVersion": 1,
  "channel": "stable",
  "generatedAt": "2026-03-12T12:00:00Z",
  "repo": "NMGorovenko/uav-simulator",
  "latestVersion": "0.1.0",
  "latestTag": "v0.1.0",
  "releases": [
    {
      "version": "0.1.0",
      "tag": "v0.1.0",
      "publishedAt": "2026-03-12T12:00:00Z",
      "releaseUrl": "https://github.com/NMGorovenko/uav-simulator/releases/tag/v0.1.0",
      "manifestAssetName": "rusim-release-manifest.json",
      "assets": []
    }
  ]
}
```

## Ключевые поля
- `schemaVersion` — версия схемы manifest;
- `channel` — release channel;
- `repo` — репозиторий источника;
- `latestVersion` и `latestTag` — последняя доступная версия;
- `releases[]` — список release entries;
- `assets[]` — список runtime и связанных asset-ов.

## Генерация
Manifest создаётся скриптом:

```text
scripts/generate_release_manifest.py
```

Основной автоматический путь:
- workflow `Release Manifest`

## Использование в `rusim`
`rusim upgrade`:
1. получает manifest;
2. выбирает release по `tag`;
3. подбирает runtime asset по платформе;
4. скачивает архив;
5. проверяет checksum;
6. регистрирует build локально.

## Связанные страницы
- [Поставка и обновление](release-distribution-model.md)
- [CLI `rusim`](cli.md)
