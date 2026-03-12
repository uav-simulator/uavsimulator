# Release Manifest v1

## Назначение
`Release Manifest v1` — машинно-читаемое описание опубликованного runtime release.

Он нужен для:
- внешнего source of truth по доступным release-артефактам;
- проверки версии и download URL;
- будущего `rusim upgrade`.

## Формат

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
      "assets": [
        {
          "name": "uav-simulator-macos-v0.1.0.zip",
          "kind": "runtime",
          "platform": "macos",
          "contentType": "application/zip",
          "sizeBytes": 123456789,
          "browserDownloadUrl": "https://github.com/.../download/...",
          "sha256": "0123abcd...",
          "sha256Source": "release-asset"
        }
      ]
    }
  ]
}
```

## Поля верхнего уровня
- `schemaVersion`
  - версия схемы manifest;
  - для текущего формата всегда `1`.

- `channel`
  - release channel;
  - сейчас ожидается `stable`.

- `generatedAt`
  - UTC timestamp генерации manifest.

- `repo`
  - репозиторий-источник release.

- `latestVersion`
  - последняя доступная продуктовая версия.

- `latestTag`
  - git/release tag, например `v0.1.0`.

- `releases`
  - массив release entries;
  - в первом практическом срезе допустим один latest release.

## Поля release entry
- `version`
- `tag`
- `publishedAt`
- `releaseUrl`
- `manifestAssetName`
- `assets`

## Поля asset entry
- `name`
  - имя asset-файла.

- `kind`
  - тип asset:
  - `runtime`
  - `manifest`
  - `checksum`
  - `unknown`

- `platform`
  - `macos`
  - `linux`
  - `windows`
  - `unknown`

- `contentType`
  - MIME-type asset.

- `sizeBytes`
  - размер файла.

- `browserDownloadUrl`
  - прямой URL скачивания.

- `sha256`
  - SHA-256 checksum runtime asset-а;
  - может быть `null`, если checksum пока не опубликован.

- `sha256Source`
  - откуда взят checksum:
  - `inline`
  - `release-asset`
  - `null`

## Ограничения текущей версии
- Manifest пока не описывает delta-updates.
- Manifest пока не различает installer и raw runtime отдельно.
- Manifest пока не описывает совместимость по Unity version или host OS version.
- Manifest пока не описывает release notes структурированно.

## Генерация
Локальная генерация:

```bash
python scripts/generate_release_manifest.py local \
  --version 0.1.0 \
  --tag v0.1.0 \
  --asset build/release/uav-simulator-macos-v0.1.0.zip \
  --release-url https://github.com/NMGorovenko/uav-simulator/releases/tag/v0.1.0 \
  --output dist/v0.1.0/rusim-release-manifest.json
```

GitHub Release генерация:

```bash
python scripts/generate_release_manifest.py github-release \
  --repo NMGorovenko/uav-simulator \
  --tag v0.1.0 \
  --output dist/v0.1.0/rusim-release-manifest.json
```

## Будущее использование
Будущий `rusim upgrade` должен:
1. получить latest release manifest;
2. сравнить локальную установленную версию и `latestVersion`;
3. выбрать asset по `platform`;
4. скачать runtime;
5. проверить `sha256`;
6. распаковать и зарегистрировать build локально.
