# Defense Demo Video — Storyboard

> **Цель:** 2:30–3:00 минут видео-ролик, показывающий ключевые свойства расширяемой Unity-платформы для KS0223. Используется как embedded-иллюстрация в защитной презентации (или как backup на USB).

**Целевой формат:** 1080p MP4, H.264, 30fps, ~10–20 Mbps, музыка license-free на -20 dB (опционально). Подписи (subtitles) на нижней трети, контрастный шрифт без засечек (Inter / Helvetica).

## Сцены

| # | Время | Длит. | Источник | Подпись (на экране) |
|---|---|---|---|---|
| 1 | 0:00 → 0:10 | 10 s | Static title card (Keynote/PowerPoint → PNG → import) | «Расширяемая Unity-платформа симуляции для робототехнической платформы KS0223» / Имя, факультет, год |
| 2 | 0:10 → 0:35 | 25 s | Screen-record: терминал + WebUI рядом. `rusim plugin list` → `rusim plugin install vehicle.arcade.green.v1.rusim-plugin.zip` → `rusim plugin list` показывает новый `[user]` плагин | «Плагинная архитектура: новый плагин ставится одной CLI-командой, без пересборки runtime» |
| 3 | 0:35 → 1:05 | 30 s | Unity Editor + Showcase camera: 16 машин swarm на arena, медленный orbit | «Параллельная тренировка: 16 агентов на одной Unity-инстанции (`MultiAgentVisionVecEnv`)» |
| 4 | 1:05 → 1:35 | 30 s | Unity Editor + Showcase camera: city demo, машина едет, останавливается на красные светофоры, едет на зелёные | «Track-плагин с готовым city-asset + traffic-light awareness» |
| 5 | 1:35 → 2:05 | 30 s | Архив: real KS0223 на cardboard corridor с rev29 моделью (sprint-3 evidence — `docs/report/prediploma-practice/sprint-3-reeval-2026-04-27/real_corridor/`) | «Sim-to-real: тренировка в Unity → проезд на реальном роботе KS0223» |
| 6 | 2:05 → 2:25 | 20 s | WebUI: оператор выбирает запись из dropdown → нажимает Play → DemoReplayPanel показывает progress bar | «WebUI Demo Replay: повторное проигрывание hand-driven траектории на реальном роботе одним кликом» |
| 7 | 2:25 → 2:45 | 20 s | Static outro card | Архитектурная диаграмма: Unity ↔ Python training ↔ Backend ↔ WebUI ↔ KS0223. Ссылка GitHub: github.com/NMGorovenko/uav-simulator |

**Итого:** 2:45 (есть резерв до 3:00 для transitions).

## Транзишены и аудио

- Все cut'ы — crossfade 0.5 s.
- Background music: ambient track из YouTube Audio Library, громкость −20 dB.
- Voice-over: **не используется** в текущей версии — subtitles достаточно. Если время позволит, можно добавить voice-over русский, читать subtitles + 1–2 поясняющих фразы на сцену.

## Источники клипов

| Клип | Где взять / создать |
|---|---|
| Сцена 2 (plugin install) | Plan 4 Task 2: записать через QuickTime Cmd+Shift+5 → `Screen Recording <date>.mov` → Сохранить в `docs/report/master-thesis/defense-artifacts/clips/plugin-install-demo.mov` |
| Сцена 3 (multi-agent swarm) | Plan 4 Task 4: запустить `configs/scenarios/demo-swarm.yaml` через `rusim server up`, открыть Unity Editor, активировать Cinemachine orbit, использовать `SceneCameraRecorder` → MP4 |
| Сцена 4 (city demo) | Plan 3 Task 6: после готовой city-asset интеграции записать через `SceneCameraRecorder` |
| Сцена 5 (real KS0223) | **Уже существует** — лежит в `docs/report/prediploma-practice/sprint-3-reeval-2026-04-27/real_corridor/` (видео с rev29 на коридоре). Обрезать до 30 секунд. |
| Сцена 6 (WebUI replay) | Записать после Plan 5 verification: открыть WebUI, выбрать demo-сессию из dropdown, hit Play, screen-record |

## Слайды

- **Title card (сцена 1):** Keynote slide → Export as PNG 1920×1080 → import as still-image на 10 s.
- **Outro card (сцена 7):** Аналогично. Архитектурная диаграмма — отдельный артефакт (`docs/master-thesis/04-architecture.md` глава если будет завершена; иначе нарисовать в Excalidraw / Figma → PNG).

## Монтаж (рекомендация)

**iMovie** (бесплатно на macOS) — простейший вариант:
- Импорт всех клипов → drag на timeline по порядку
- Trim каждого клипа до целевой длительности
- Add titles → text overlay для subtitles
- Add background music → reduce volume to −20 dB
- Export: File → Share → File → 1080p, ProRes 422 (для качества) или H.264 (для размера)

**Альтернатива — DaVinci Resolve** (бесплатно): больше контроля, но steeper learning curve. Не нужен если iMovie хватает.

**ffmpeg-only** (terminal): можно собрать через concat filter:
```bash
# storyboard.txt:
# file 'title.png'   duration 10
# file 'scene2.mp4'
# file 'scene3.mp4'
# ...
ffmpeg -f concat -safe 0 -i storyboard.txt -c:v libx264 -pix_fmt yuv420p -framerate 30 defense-demo.mp4
```
Подходит, если все клипы уже одного разрешения и нужен fast batch render без UI.

## Final export checklist

- [ ] Все 7 сцен в правильном порядке.
- [ ] Длительность 2:30–3:00.
- [ ] 1080p (1920×1080), 30 fps, H.264.
- [ ] Subtitles читаются на 1080p preview без zoom.
- [ ] (Optional) ambient music без копирайт-claim'а.
- [ ] Export сохранён в `docs/report/master-thesis/defense-artifacts/defense-demo-video.mp4`.
- [ ] Backup на USB-flash.
- [ ] Загружен на YouTube unlisted (на случай отказа интернета во время защиты — резервный link в презентацию).

## Backup-вариант

Если один из клипов 3/4/6 не получится записать вовремя (например, Unity-сцена не доделана):
- Сцена 3 (swarm) → заменить на screenshot 16-агентского спавна + закадровый комментарий subtitle'ом.
- Сцена 4 (city) → заменить на screenshot Asset Store города из marketing-страницы + subtitle «track-плагин в разработке, демо записано на cardboard corridor».
- Сцена 6 (WebUI replay) → заменить на screenshot DemoReplayPanel + subtitle.

В худшем случае: 4 минимально-обязательные сцены — title (1) + plugin install (2) + real KS0223 (5) + outro (7) = ~1:25 минут, всё ещё убедительный ролик.
