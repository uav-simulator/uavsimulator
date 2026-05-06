# 3 Реализация ключевых компонентов

## 3.1 Подходы к реализации

### 3.1.1 Принципы декомпозиции по ответственности

### 3.1.2 Использование DI и singleton-сервисов на стороне backend

### 3.1.3 Lifecycle-управление через MonoBehaviour и ScriptableObject в Unity

## 3.2 SimulationManager: ядро жизненного цикла

### 3.2.1 Состояния и переходы

### 3.2.2 ResetWithConfig: применение сценария

### 3.2.3 Step: цикл управления и наблюдения

### 3.2.4 Управление множеством агентов

## 3.3 PluginRegistry и BuiltinPluginFactory

### 3.3.1 Two-tier реестр и merge-логика

### 3.3.2 BuiltinPluginFactory как декларативный каталог

### 3.3.3 Procedural-fallback для отсутствующих ассетов

### 3.3.4 RuntimeMaterialCompatibility: конверсия Built-in в URP

## 3.4 Реализация Vehicle: Ks0223Vehicle

### 3.4.1 Физический контур: Rigidbody и интегрирование скоростей

### 3.4.2 Сенсоры: камера, ультразвук, line tracker

### 3.4.3 Применение ControlCommand: маппинг на физические каналы

### 3.4.4 Презентационные visual-режимы

## 3.5 Реализация Track: BasicArenaTrack и CityPolygonTrack

### 3.5.1 Procedural arena как минимальный track

### 3.5.2 CityPolygonTrack: загрузка стороннего ассета через AssetDatabase

### 3.5.3 Светофоры: TrafficLight FSM, Controller, Adapter, Aware controller

## 3.6 Backend services

### 3.6.1 RuntimeSessionManager: маршрутизация в две подложки

### 3.6.2 UnityKs0223RuntimeProvider: HTTP-клиент к Unity

### 3.6.3 AutopilotService: inference loop

### 3.6.4 AutopilotSafetyFilter: deadman, sonar E-stop, dropout

### 3.6.5 ModelRegistryService: lifecycle ONNX-артефакта

### 3.6.6 SessionLogger и SessionVideoRecorder

### 3.6.7 DemoReplayService: воспроизведение записанных сессий

## 3.7 Frontend: операторский пульт

### 3.7.1 Архитектура tabs и SignalR

### 3.7.2 ControlPad: keyboard и виртуальный джойстик

### 3.7.3 Camera panel и MJPEG streaming

### 3.7.4 Scenario picker: выбор сценария из YAML

## 3.8 Качество реализации

### 3.8.1 Тестовое покрытие

### 3.8.2 Линтинг и компиляция

### 3.8.3 Производительность и узкие места
