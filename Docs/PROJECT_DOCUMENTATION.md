# 📚 Документация проекта Strikeball Positioning System

**Дата создания:** 27 февраля 2026  
**Версия:** 1.0 MVP  
**Статус:** В разработке

---

## 📋 Описание проекта

**Система позиционирования игроков в страйкбол** в реальном времени с использованием UWB (Ultra-Wideband) технологии для определения координат игроков на полигоне.

### Цель проекта
- Отслеживание координат игроков в real-time с точностью до 0.5–1 метр
- Передача данных на КПК игроков для мониторинга игровой ситуации
- Логирование всех боев для последующего разбора полетов

---

## 🏗️ Архитектура системы

```
ПОЛИГОН СТРАЙКБОЛА
│
├─ ЯКОРЯ (Anchors) — 4+ стационарных базовых станций с известными координатами (x, y, z)
│  └─ Расположены по периметру полигона
│
├─ МАЯКИ (Beacons) — 4+ устройств на игроках
│  └─ UWB модуль DWM3000 + STM32F103C8T6 контроллер
│     ├─ Измеряет расстояние до якорей методом TWR (Two-Way Ranging)
│     ├─ Формирует пакет: [beacon_id, distances_to_anchors[], timestamp]
│     └─ Отправляет по LTE → Сервер
│
├─ СЕРВЕР (C# ASP.NET Core на Debian Linux)
│  ├─ Получает пакеты измерений от маяков
│  ├─ Вычисляет 3D координаты методом трилатерации
│  ├─ Фильтрует шум (Kalman фильтр)
│  ├─ Хранит все данные в PostgreSQL
│  └─ Отправляет координаты → Android КПК (real-time WebSocket)
│
└─ ANDROID КПК — приложение на телефоне игрока
   ├─ Отображает карту полигона
   ├─ Показывает позицию своего маяка
   ├─ Показывает позиции других игроков (опционально)
   └─ Логирует события
```

---

## 🛠️ Технический стек

### Сервер
- **Язык:** C# (.NET 8)
- **Фреймворк:** ASP.NET Core
- **БД:** PostgreSQL (для продакшена) / SQLite (для разработки)
- **ORM:** Entity Framework Core
- **Real-time:** SignalR (WebSocket)
- **ОС:** Debian Linux

### Аппаратная часть
- **UWB модуль:** DWM3000 (Qorvo/Decawave)
- **Контроллер:** STM32F103C8T6 (ARM Cortex-M3, 72 MHz)
- **Питание:** PowerBank (несколько часов работы)
- **Связь:** LTE (сотовая сеть)

### Android КПК
- **Язык:** Kotlin
- **Минимальная версия:** Android 8.0 (API 26+)
- **Карта:** Canvas / Google Maps API
- **Связь:** WebSocket клиент

---

## 📡 Протокол обмена данных

### 1. Пакет от маяка → Сервер (Бинарный формат)

```
Структура пакета:
[beacon_id (1 byte)]
[anchor_count (1 byte)]
[anchor_1_id (1 byte), distance_1 (4 bytes float)]
[anchor_2_id (1 byte), distance_2 (4 bytes float)]
[anchor_3_id (1 byte), distance_3 (4 bytes float)]
[anchor_4_id (1 byte), distance_4 (4 bytes float)]
[timestamp (8 bytes long)]

Пример:
beacon_id=1, anchors=[1->5.2m, 2->7.3m, 3->4.8m, 4->6.1m], timestamp=1709048400000
```

**API endpoint:** `POST /api/telemetry/measurement`

### 2. Пакет от сервера → КПК (JSON)

```json
{
  "beacon_id": 1,
  "x": 10.5,
  "y": 20.3,
  "z": 1.5,
  "confidence": 0.95,
  "timestamp": 1709048400000,
  "all_beacons": [
    {"id": 1, "x": 10.5, "y": 20.3, "z": 1.5, "name": "Player1"},
    {"id": 2, "x": 15.2, "y": 18.1, "z": 1.5, "name": "Player2"}
  ]
}
```

**WebSocket Hub:** `ws://server_ip:5000/hubs/positioning`

---

## 🗄️ Схема базы данных

### Таблица: `Anchors`
| Поле | Тип | Описание |
|------|-----|----------|
| Id | int (PK) | Уникальный ID якоря |
| Name | string | Название якоря (Anchor_1, Anchor_2...) |
| X | double | Координата X (метры) |
| Y | double | Координата Y (метры) |
| Z | double | Координата Z (метры, высота) |
| Latitude | double | Широта WGS84 (если известна) |
| Longitude | double | Долгота WGS84 (если известна) |
| MacAddress | string | MAC адрес UWB модуля |
| CalibrationOffset | double | Калибровочное смещение (метры) |
| Status | enum | Active / Inactive / Error |
| CreatedAt | DateTime | Дата добавления |

### Таблица: `Beacons`
| Поле | Тип | Описание |
|------|-----|----------|
| Id | int (PK) | Уникальный ID маяка |
| Name | string | Имя игрока |
| MacAddress | string | MAC адрес UWB модуля |
| BatteryLevel | int | Уровень батареи (0-100%) |
| LastSeen | DateTime | Последний пакет |
| Status | enum | Active / Offline / LowBattery |

### Таблица: `Measurements`
| Поле | Тип | Описание |
|------|-----|----------|
| Id | long (PK) | Уникальный ID измерения |
| BeaconId | int (FK) | ID маяка |
| AnchorId | int (FK) | ID якоря |
| Distance | double | Расстояние (метры) |
| Rssi | int | Мощность сигнала (опционально) |
| Timestamp | DateTime | Время измерения |

### Таблица: `Positions`
| Поле | Тип | Описание |
|------|-----|----------|
| Id | long (PK) | Уникальный ID позиции |
| BeaconId | int (FK) | ID маяка |
| X | double | Вычисленная координата X |
| Y | double | Вычисленная координата Y |
| Z | double | Вычисленная координата Z |
| Confidence | double | Уверенность (0.0–1.0) |
| Method | string | Алгоритм (TWR, TDoA) |
| Timestamp | DateTime | Время вычисления |

---

## 🧮 Алгоритмы позиционирования

### TWR (Two-Way Ranging) — Используется в MVP
- **Принцип:** Маяк отправляет сигнал, якорь отвечает, измеряется время прохождения
- **Формула:** `distance = (time_of_flight * speed_of_light) / 2`
- **Точность:** ±10–30 см (зависит от окружения)
- **Преимущества:** Не требует синхронизации времени между устройствами
- **Недостатки:** Требуется двусторонний обмен (больше энергопотребления)

### TDoA (Time Difference of Arrival) — Планируется в будущем
- **Принцип:** Измеряется разница времени прихода сигнала к разным якорям
- **Требования:** Синхронизация времени между всеми якорями
- **Преимущества:** Меньше энергопотребления, масштабируемость
- **Недостатки:** Сложнее в реализации

### Трилатерация (3D)
**Входные данные:** Расстояния `d1, d2, d3, d4` до якорей с известными координатами `(x1, y1, z1), (x2, y2, z2), (x3, y3, z3), (x4, y4, z4)`

**Система уравнений:**
```
(x - x1)² + (y - y1)² + (z - z1)² = d1²
(x - x2)² + (y - y2)² + (z - z2)² = d2²
(x - x3)² + (y - y3)² + (z - z3)² = d3²
(x - x4)² + (y - y4)² + (z - z4)² = d4²
```

**Решение:** Метод наименьших квадратов (Least Squares)

**Реализация:** `PositioningService.cs` → метод `CalculatePosition3D()`

### Фильтрация Kalman
- **Состояние:** `[x, y, z, vx, vy, vz]` (координаты + скорость)
- **Обновление:** При каждом новом измерении
- **Цель:** Сглаживание шума UWB, устранение выбросов
- **Реализация:** `FilteringService.cs`

---

## 🔌 API Endpoints

### Телеметрия
- `POST /api/telemetry/measurement` — Прием пакета измерений от маяка

### Позиции
- `GET /api/positions` — Список всех актуальных позиций
- `GET /api/positions/{beaconId}` — Позиция конкретного маяка
- `GET /api/positions/history/{beaconId}?from=<timestamp>&to=<timestamp>` — История позиций

### Якоря
- `GET /api/anchors` — Список всех якорей
- `POST /api/anchors` — Добавить новый якорь
- `PUT /api/anchors/{id}` — Обновить координаты якоря
- `DELETE /api/anchors/{id}` — Удалить якорь

> Примечание: В API `Anchor` теперь поддерживает дополнительные поля `latitude` и `longitude` (WGS84). Сервер сохраняет как локальные координаты `x,y,z` (метры) для триангуляции, так и опциональные `latitude/longitude` для отображения на глобальной карте.

### Маяки
- `GET /api/beacons` — Список всех маяков
- `POST /api/beacons` — Зарегистрировать новый маяк
- `PUT /api/beacons/{id}` — Обновить информацию о маяке

### Администрирование
- `POST /api/admin/calibrate` — Запуск калибровки якорей
- `GET /api/admin/logs` — Получение логов сервера
- `POST /api/admin/clear-logs` — Очистка старых логов

---

## 🚀 Развертывание

### Требования к серверу
- **ОС:** Debian 11+ / Ubuntu 20.04+
- **CPU:** 2+ ядра
- **RAM:** 2+ GB
- **Диск:** 20+ GB (для логов)
- **.NET:** SDK 8.0+
- **БД:** PostgreSQL 14+

### Установка на Debian

```bash
# Обновление системы
sudo apt update && sudo apt upgrade -y

# Установка .NET 8
wget https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt update
sudo apt install -y dotnet-sdk-8.0

# Установка PostgreSQL
sudo apt install -y postgresql postgresql-contrib
sudo systemctl enable postgresql
sudo systemctl start postgresql

# Создание БД
sudo -u postgres psql
CREATE DATABASE strikeballdb;
CREATE USER strikeballuser WITH PASSWORD 'your_password';
GRANT ALL PRIVILEGES ON DATABASE strikeballdb TO strikeballuser;
\q

# Публикация приложения
cd StrikeballServer/Server
dotnet publish -c Release -o /opt/strikeball

# Создание systemd сервиса
sudo nano /etc/systemd/system/strikeball.service
```

**Содержимое `/etc/systemd/system/strikeball.service`:**
```ini
[Unit]
Description=Strikeball Positioning Server
After=network.target postgresql.service

[Service]
Type=notify
WorkingDirectory=/opt/strikeball
ExecStart=/usr/bin/dotnet /opt/strikeball/StrikeballServer.dll
Restart=always
RestartSec=10
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
```

```bash
# Запуск сервиса
sudo systemctl daemon-reload
sudo systemctl enable strikeball
sudo systemctl start strikeball
sudo systemctl status strikeball
```

---

## 🧪 Тестирование

### Локальное тестирование
1. Запуск сервера: `dotnet run --project Server`
2. Симулятор маяка: `Tests/BeaconSimulator.cs`
3. Проверка API: Swagger UI → `http://localhost:5000/swagger`

### Географические координаты (WGS84)

Если вы хотите, чтобы клиент отображал якоря по широте/долготе, добавьте `latitude` и `longitude` в объект `Anchor`. Сервер приоритетно использует `x,y,z` для вычислений, но клиент может использовать `latitude/longitude` для визуализации на карте.

Пример POST с lat/lon:

```bash
curl -X POST http://SERVER_IP:5000/api/anchors -H "Content-Type: application/json" -d '{
  "name":"Anchor-Geo-1",
  "x":0.0,
  "y":0.0,
  "z":2.0,
  "latitude":56.260429,
  "longitude":44.009171
}'
```

Пример PUT для обновления существующего якоря (id=5):

```bash
curl -X PUT http://SERVER_IP:5000/api/anchors/5 -H "Content-Type: application/json" -d '{
  "id":5,
  "name":"Anchor-1",
  "x":0.0,
  "y":0.0,
  "z":0.0,
  "latitude":56.260429,
  "longitude":44.009171
}'
```

Если у вас есть только WGS84 (lat/lon) и нужно конвертировать в локальные метры для триангуляции, используйте простую приближенную формулу (подходит для небольших площадей):

- Выберите опорную точку (lat0, lon0) — она станет `(0,0)` в локальной системе.
- Вычислите смещение в метрах:

  - Δnorth_m = (lat - lat0) * meters_per_degree_lat
  - Δeast_m  = (lon - lon0) * meters_per_degree_lon_at_lat0

  Где примерно: `meters_per_degree_lat ≈ 111132` m, а `meters_per_degree_lon ≈ 111320 * cos(lat0)` m.

Пример кода JS для конверсии:

```javascript
const origin = { lat: 56.260429, lon: 44.009171 };
const metersPerDegLat = 111132.0;
const metersPerDegLon = 111320.0 * Math.cos(origin.lat * Math.PI / 180);

function latLonToXY(lat, lon) {
  const dy = (lat - origin.lat) * metersPerDegLat;
  const dx = (lon - origin.lon) * metersPerDegLon;
  return { x: dx, y: dy };
}

function xyToLatLon(x, y) {
  const lat = origin.lat + y / metersPerDegLat;
  const lon = origin.lon + x / metersPerDegLon;
  return { lat, lon };
}
```

Рекомендация: храните в БД оба представления (`x,y,z` и `latitude,longitude`) — это даёт точность для вычислений и удобство для картографического отображения.

### Тестирование на полигоне
1. Расставить 4 якоря в известных точках
2. Зафиксировать координаты якорей в БД
3. Включить маяк, записать данные
4. Проверить точность позиционирования
5. Логировать все измерения для анализа

---

## 📊 Метрики качества

| Метрика | Целевое значение | Текущее |
|---------|------------------|---------|
| Точность позиционирования | ±0.5–1 м | TBD |
| Частота обновления | 5–10 Гц | TBD |
| Задержка (latency) | <100 мс | TBD |
| Процент потерянных пакетов | <5% | TBD |
| Максимальная дальность UWB | >50 м | TBD |

---

## 🔄 История изменений

### v1.0 MVP (27.02.2026)
- ✅ Создана структура проекта
- ✅ Документация проекта
- 🔄 Реализация базовых моделей
- 🔄 Настройка Entity Framework Core
- 🔄 API endpoints для телеметрии
- 🔄 Сервис трилатерации TWR
- 🔄 SignalR Hub для real-time

---

## 📝 TODO / Планы на будущее

### MVP (Фаза 1)
- [ ] Реализовать базовый TWR позиционирование
- [ ] Настроить WebSocket real-time
- [ ] Создать Android клиент (минимальный)
- [ ] Протестировать на 4 якорях + 4 маяках

### Фаза 2
- [ ] Реализовать TDoA для масштабирования
- [ ] Буферизация при потере LTE
- [ ] Оффлайн режим с синхронизацией
- [ ] Увеличить количество якорей до 6–8

### Фаза 3
- [ ] Интеграция с камерой (видеозапись)
- [ ] Система компьютерного зрения (AI)
- [ ] HUD на шлеме игрока
- [ ] Веб-интерфейс для штаба

---

## 🤝 Контакты и поддержка

**Разработчик:** GitHub Copilot + Ваша команда  
**Дата начала:** 27 февраля 2026  
**Репозиторий:** TBD

---

**Примечание:** Этот документ будет обновляться по мере разработки проекта.
