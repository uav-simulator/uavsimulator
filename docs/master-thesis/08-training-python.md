# 6 Программная обвязка обучения

## 6.1 Связь Python-контура с симулятором

### 6.1.1 SimClient как единственная точка получения наблюдений

### 6.1.2 Семантика reset и step

### 6.1.3 Загрузка сценария и параметризация запуска

## 6.2 Обёртки сред: единый VecEnv-интерфейс

### 6.2.1 Одиночная среда (ABCorridorVisionEnv)

### 6.2.2 Multi-agent в одной Unity-инстанции (MultiAgentVisionVecEnv)

### 6.2.3 Meta-multi-agent: N Unity на M агентов (MetaMultiAgentVecEnv)

### 6.2.4 Genesis-вариант для GPU-параллельной симуляции

### 6.2.5 Wrappers: DiscreteActionWrapper, image augmentation, latency

## 6.3 Тренировочный пайплайн на Stable-Baselines3

### 6.3.1 PPO как основной алгоритм

### 6.3.2 train_cardboard_corridor_v9.py: общий поток

### 6.3.3 Гиперпараметры и их обоснование

### 6.3.4 Курикулум и мониторинг через EvalCallback

## 6.4 Domain randomization и подготовка к sim-to-real

### 6.4.1 Heavy-DR: текстуры, освещение, спавны

### 6.4.2 Frame stacking и историческая память

### 6.4.3 Real-camera post-processing

## 6.5 KPI-оценка и evidence

### 6.5.1 evaluate_ab_policy.py: 20-эпизодный success rate

### 6.5.2 Структура evidence-папки и воспроизводимость

### 6.5.3 Сравнение revisions (rev16 → rev30+)

## 6.6 Экспорт ONNX-артефакта и связь с backend

### 6.6.1 torch.onnx.export: тонкие места

### 6.6.2 metadata.json и metrics.json: формат

### 6.6.3 rusim model install / activate / bind

## 6.7 Тестовое покрытие

### 6.7.1 Wrapper-тесты (DiscreteAction, image augmentation, latency, anti-spin reward)

### 6.7.2 Multi-runtime launch test

### 6.7.3 Resume curriculum test
