# Simulator Scenario Config Contract


## Назначение
Сценарий задаёт:
- runtime mode;
- трек;
- робота;
- route и spawn;
- multi-agent конфигурацию;
- logging.

Это позволяет поднимать симуляцию как повторяемый runtime, а не как набор ручных действий в Unity Editor.

## Текущая структура
Типовой сценарий содержит:
- `scenarioId`
- `displayName`
- `description`
- `version`
- `runtime`
- `world`
- `vehicle`
- `route`
- `agents`
- `logging`

Дополнительно могут присутствовать секции:
- `sensors`

## Актуальный product baseline
Базовый demo entrypoint:

```text
configs/scenarios/demo.yaml
```

Он использует:
- `track.roadsystem_realistic.v2`
- `vehicle.arcade.blue.v1`

Базовый multi-agent пример:

```text
configs/scenarios/demo-multi-agent.yaml
```

Он поднимает:
- `ego` на `vehicle.arcade.blue.v1`
- `npc-red` на `vehicle.arcade.red.v1`

## Практическое использование
Проверка сценария:

```bash
rusim scenario validate configs/scenarios/demo.yaml
```

Применение сценария:

```bash
rusim scenario reset configs/scenarios/demo.yaml --base-url http://127.0.0.1:8000
```

Запуск runtime со сценарием:

```bash
rusim server up --mode background --port 8000 --scenario configs/scenarios/demo.yaml
```

## Ограничения
- сценарий описывает продуктовый runtime, а не внутреннюю структуру Unity сцены;
- конкретные значения plugin ID должны совпадать с активным plugin catalog;
- сценарий используется и для single-agent, и для multi-agent flow.

## Связанные страницы
- [Использование](usage.md)
- [CLI `rusim`](cli.md)
- [API](api.md)
