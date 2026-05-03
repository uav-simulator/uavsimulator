# 7. Разработка плагинов

## 7.1. Концепция плагинной архитектуры
(TBD)

## 7.2. Типы плагинов и точки расширения
(TBD)

## 7.3. Plugin SDK: API и базовые классы
### 7.3.1. PluginDescriptorBase
### 7.3.2. VehiclePluginDescriptor + VehicleBase
### 7.3.3. TrackPluginDescriptor + TrackBase
### 7.3.4. DeviceContractDescriptor
### 7.3.5. ContractVersion и совместимость
### 7.3.6. PluginRegistryAsset

## 7.4. Worked example: vehicle plugin (vehicle.arcade.green.v1)
### 7.4.1. Создание Unity-проекта плагина
### 7.4.2. Подключение SDK как Unity Package
### 7.4.3. Создание префаба и наследника VehicleBase
### 7.4.4. Создание VehiclePluginDescriptor + DeviceContract
### 7.4.5. Validate + Export
### 7.4.6. Установка в runtime через rusim CLI

## 7.5. Worked example: track plugin (track.city_demo.v1)
### 7.5.1. Префаб трассы и наследник TrackBase
### 7.5.2. ParametersSchemaJson — параметризация трассы
### 7.5.3. Validate + Export + Install

## 7.6. Распространение плагинов: формат архива и CLI
### 7.6.1. Структура .rusim-plugin.zip
### 7.6.2. CLI: rusim plugin install/list/remove/new
### 7.6.3. Регистрация в PluginRegistryAsset

## 7.7. Версионирование и совместимость
### 7.7.1. ContractVersion: semver для контрактов
### 7.7.2. compatibleRuntime в manifest.json
### 7.7.3. Стратегия breaking changes

## 7.8. Заключение
