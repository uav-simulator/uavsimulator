# Документация продукта

**Что это**  
Главная точка входа в публичную документацию `uav-simulator`.

**Для кого**  
Для пользователя, разработчика и исследователя, которым нужно быстро понять продукт и работать с актуальным состоянием проекта.

**Статус**  
Канонический индекс документации GitHub Pages.

**Проверено по**  
`mkdocs.yml`, `README.md`, `configs/scenarios/demo.yaml`, `configs/scenarios/demo-multi-agent.yaml`, `python/sim_client/cli.py`

## С чего начать

### Быстрый старт
1. Прочитать [О продукте](about-simulator.md).
2. Пройти [Установку](installation.md).
3. Использовать [CLI](cli.md) и [Использование](usage.md).

### Ежедневная работа
1. Проверить [Использование](usage.md).
2. Открыть [CLI](cli.md).
3. При необходимости свериться с [API](api.md) и [Архитектурой](architecture.md).

### Разработка и расширение
1. Открыть [Архитектуру](architecture.md).
2. Прочитать [Плагины](plugins.md) и [Как добавить новую машинку](plugin-vehicle-guide.md).
3. При необходимости перейти к [Контрактам](contracts.md) и [CI/CD](ci.md).

## Канонические product-страницы
- [О продукте](about-simulator.md)
- [Установка](installation.md)
- [Использование](usage.md)
- [CLI `rusim`](cli.md)
- [Архитектура](architecture.md)
- [API](api.md)
- [Плагины](plugins.md)
- [Сборка и релизы](build.md)
- [Статус проекта](roadmap.md)

## Контракты
- [Обзор контрактов](contracts.md)
- [Product Definition](product-definition.md)
- [Unified Runtime Contract](unified-runtime-contract.md)
- [Autopilot Integration Contract](autopilot-integration-contract.md)
- [Simulator Scenario Config Contract](simulator-scenario-config-contract.md)

## Вторичный слой
- [Research и магистерская](research-index.md)
- [Инженерный журнал](engineering-log.md)

## Что не является основным входом
- `docs/research/`, `docs/master-thesis/`, `docs/report/` и `docs/tasks/` сохраняются в репозитории, но не заменяют продуктовые guide-страницы.
- Черновики, cleanup-планы и исторические заметки не используются как источник истины для запуска и эксплуатации.
