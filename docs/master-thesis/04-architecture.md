# 2 Архитектура платформы

## 2.1 Принципы и границы

(TBD)

## 2.2 Высокоуровневая декомпозиция

### 2.2.1 Четыре функциональных контура

### 2.2.2 Поток данных и управления

### 2.2.3 Деление на production и research контуры

## 2.3 Unity runtime

### 2.3.1 Точка входа: RuntimeSceneBootstrap

### 2.3.2 SimulationManager как ядро жизненного цикла

### 2.3.3 PluginRegistry и каталог сущностей

### 2.3.4 HTTP JSON API host

### 2.3.5 SimulatorApiFacade и поверхность контракта

## 2.4 Operator stack

### 2.4.1 Backend на ASP.NET Core

### 2.4.2 Слой runtime-провайдеров: unity-sim и real-robot

### 2.4.3 Web UI на React и SignalR

### 2.4.4 Сервисы model lifecycle и autopilot

## 2.5 CLI rusim

### 2.5.1 Структура командного дерева

### 2.5.2 Ответственность CLI и backend

### 2.5.3 Сценарии и плагины как точки расширения CLI

## 2.6 Python training и eval

### 2.6.1 Источник наблюдений через HTTP-клиент

### 2.6.2 Обёртки сред: одиночные, multi-agent и Genesis

### 2.6.3 Жизненный цикл артефакта модели

## 2.7 Контракты между модулями

### 2.7.1 ControlCommand, VehicleState, CameraFrame — транспортные DTO

### 2.7.2 Плагинные контракты

### 2.7.3 ContractVersion и совместимость

## 2.8 Архитектурные решения и компромиссы

### 2.8.1 HTTP JSON вместо gRPC

### 2.8.2 Descriptor-based плагины вместо DLL hot-reload

### 2.8.3 Единый Unity-процесс vs Meta vs Genesis

### 2.8.4 Backend как proxy: зачем не звать Unity напрямую из Web UI
