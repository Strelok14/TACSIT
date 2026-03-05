# 📂 СТРУКТУРА ПРОЕКТА - ПОЛНОЕ ОПИСАНИЕ

**Последнее обновление:** 5 марта 2026  
**Версия:** 1.0 MVP

---

## 🏗️ ОБЩАЯ АРХИТЕКТУРА

```
StrikeballServer/                          # Корневая папка проекта
│
├── 📄 ФАЙЛЫ ДОКУМЕНТАЦИИ И КОНФИГУРАЦИИ
│   ├── README.md                          # Быстрый старт (кратко)
│   ├── STATUS.md                          # Статус проекта и что сделано
│   ├── FINAL_CHECKLIST.md                 # ← ПРОЧИТАЙТЕ ЭТО ПЕРВЫМ
│   ├── READY_FOR_TESTING.md               # Что готово, сводка
│   ├── TESTING_GUIDE.md                   # Полная инструкция тестирования
│   ├── DEPLOYMENT_CHECKLIST.md            # Чек-лист для deployment
│   ├── .gitignore                         # Git конфиг (игнорировать bin/, obj/, и т.д.)
│   ├── Dockerfile                         # Docker образ для контейнеризации
│   ├── docker-compose.yml                 # Docker Compose конфиг (PostgreSQL + Server)
│   ├── strikeball-server.service          # systemd сервис для Linux автозапуска
│   └── deploy.sh                          # Bash скрипт автоматического развертывания
│
├── 🖥️ ОСНОВНОЙ СЕРВЕР (.NET 8)
│   └── Server/
│       ├── Program.cs                     # ⭐ Точка входа, конфигурация DI
│       ├── StrikeballServer.csproj        # Конфиг проекта (зависимости)
│       ├── appsettings.json               # Конфигурация (подробнее ниже)
│       ├── appsettings.Development.json   # Overrides для Development
│       ├── appsettings.Production.json    # Overrides для Production
│       │
│       ├── 🎮 КОНТРОЛЛЕРЫ (REST API)
│       ├── Controllers/
│       │   ├── TelemetryController.cs      # POST /api/telemetry/measurement
│       │   ├── PositionsController.cs      # GET /api/positions (все и по ID)
│       │   ├── AnchorsController.cs        # GET/POST/PUT/DELETE /api/anchors
│       │   └── BeaconsController.cs        # GET/POST/PUT/DELETE /api/beacons
│       │
│       ├── 🔧 СЕРВИСЫ (Бизнес-логика)
│       ├── Services/
│       │   ├── IPositioningService.cs      # Интерфейс позиционирования
│       │   ├── PositioningService.cs       # Трилатерация 3D, LS метод
│       │   ├── IFilteringService.cs        # Интерфейс фильтрации
│       │   ├── FilteringService.cs         # EMA фильтр, Kalman-like
│       │   ├── ITelemetryService.cs        # Интерфейс телеметрии
│       │   └── TelemetryService.cs         # Логирование метрик
│       │
│       ├── 📊 МОДЕЛИ (Структуры данных)
│       ├── Models/
│       │   ├── Anchor.cs                   # Якорь (базовая станция)
│       │   ├── Beacon.cs                   # Маяк (на игроке)
│       │   ├── Measurement.cs              # Измерение расстояния
│       │   ├── Position.cs                 # Вычисленная позиция
│       │   └── Dtos.cs                     # DTO для API (обмена)
│       │
│       ├── 🗄️ БАЗА ДАННЫХ
│       ├── Data/
│       │   └── ApplicationDbContext.cs     # EF Core контекст, конфиг БД
│       │
│       ├── 📡 REAL-TIME (WebSocket)
│       ├── Hubs/
│       │   └── PositioningHub.cs           # SignalR hub для position updates
│       │
│       └── 📁 СКОМПИЛИРОВАННЫЕ ФАЙЛЫ
│           ├── bin/                        # Скомпилированные dll
│           └── obj/                        # Объектные файлы
│
├── 🧪 ТЕСТЫ И СИМУЛЯТОР
│   └── Tests/
│       ├── Tests.csproj                    # Проект тестов/симулятора
│       ├── BeaconSimulator.cs              # Симулятор маяков
│       ├── Program.cs                      # Точка входа для симулятора
│       └── bin/obj/                        # Скомпилированные файлы
│
├── 📚 ДОКУМЕНТАЦИЯ
│   └── Docs/
│       ├── API.md                          # ⭐ Полная API документация (383 строки)
│       │   # Все 10 endpoints с примерами JSON для запросов/ответов
│       │
│       └── PROJECT_DOCUMENTATION.md        # ⭐ Архитектура и детали (374 строки)
│           # Описание системы, алгоритмы, технический стек
│
└── 🚀 СКРИПТЫ ЗАПУСКА
    ├── start-server.ps1                    # PowerShell скрипт запуска сервера
    └── start-simulator.ps1                 # PowerShell скрипт запуска симулятора
```

---

## 📄 ОПИСАНИЕ КЛЮЧЕВЫХ ФАЙЛОВ

### 🎯 Program.cs (Точка входа)
**Путь:** `Server/Program.cs`

**Что делает:**
- Настраивает DI контейнер (зависимости)
- Регистрирует сервисы
- Конфигурирует базу данных (SQLite или PostgreSQL)
- Настраивает CORS, SignalR, Swagger
- Добавляет middleware для логирования и обработки ошибок
- Инициализирует БД при старте

**Ключевые строки:**
```csharp
// Выбор БД в зависимости от окружения
if (builder.Environment.IsProduction())
    options.UseNpgsql(postgresConnection);  // PostgreSQL
else
    options.UseSqlite(connectionString);    // SQLite
```

---

### 🔧 ApplicationDbContext.cs
**Путь:** `Server/Data/ApplicationDbContext.cs`

**Что делает:**
- Entity Framework Core контекст для БД
- Определяет Entity модели (Anchors, Beacons, Measurements, Positions)
- Конфигурирует таблицы, индексы, отношения
- Содержит seed данные (4 якоря, 2 маяка)

**Таблицы:**
```sql
Anchors         # Якоря с координатами (x, y, z)
Beacons         # Маяки с информацией о батарее
Measurements    # Измерения расстояний
Positions       # Вычисленные позиции маяков
```

---

### 🎮 Контроллеры

#### TelemetryController.cs
**Путь:** `Server/Controllers/TelemetryController.cs`

**Endpoint:** `POST /api/telemetry/measurement`

**Что делает:**
1. Принимает пакет с измерениями от маяка
2. Сохраняет измерения в БД
3. Вызывает PositioningService для вычисления позиции
4. Отправляет update через SignalR WebSocket

**Входные данные:**
```json
{
  "beaconId": 1,
  "distances": [
    {"anchorId": 1, "distance": 15.2, "rssi": -45}
  ],
  "timestamp": 1709048400000,
  "batteryLevel": 85
}
```

---

#### PositionsController.cs
**Путь:** `Server/Controllers/PositionsController.cs`

**Endpoints:**
```
GET /api/positions              # Все позиции (последние)
GET /api/positions/{id}         # Позиция конкретного маяка
GET /api/positions/history/{id} # История позиций маяка
```

**Что делает:**
- Возвращает текущие/исторические позиции из БД

---

#### AnchorsController.cs, BeaconsController.cs
**Пути:** `Server/Controllers/Anchors|Beacons`Controller.cs`

**Endpoints:**
```
GET    /api/anchors             # Список якорей
GET    /api/anchors/{id}        # Якорь по ID
POST   /api/anchors             # Создать якорь
PUT    /api/anchors/{id}        # Обновить якорь
DELETE /api/anchors/{id}        # Удалить якорь

GET    /api/beacons             # Список маяков
GET    /api/beacons/{id}        # Маяк по ID
POST   /api/beacons             # Создать маяк
PUT    /api/beacons/{id}        # Обновить маяк
DELETE /api/beacons/{id}        # Удалить маяк
```

---

### 🔧 Сервисы

#### PositioningService.cs
**Путь:** `Server/Services/PositioningService.cs`

**Ключевой метод:**
```csharp
Task<Position?> CalculatePositionAsync(int beaconId, List<Measurement> measurements)
```

**Алгоритм:**
1. Получает координаты якорей из БД
2. Применяет калибровку (смещения)
3. Решает систему уравнений методом наименьших квадратов
4. Вычисляет confidence (уверенность в позиции)
5. Пропускает через FilteringService

**Формула (3D Trilateration):**
```
Решение уравнения: (A^T * A)^-1 * A^T * b
где:
A = матрица коэффициентов
b = вектор расстояний
```

---

#### FilteringService.cs
**Путь:** `Server/Services/FilteringService.cs`

**Что делает:**
- Применяет EMA фильтр (экспоненциальное сглаживание)
- Сглаживает дрожание позиций
- Упрощенный Kalman фильтр

**Параметр:** `SmoothingFactor = 0.3` (чем выше - тем больше сглаживание)

---

### 📡 PositioningHub.cs
**Путь:** `Server/Hubs/PositioningHub.cs`

**Что делает:**
- SignalR Hub для WebSocket подключений
- Обрабатывает подключение/отключение клиентов
- Отправляет broadcast обновления позиций (`PositionUpdate`)

**WebSocket путь:** `ws://localhost:5000/hubs/positioning`

---

### 📊 Модели

#### Anchor.cs
```csharp
public class Anchor
{
    public int Id { get; set; }
    public string Name { get; set; }
    public double X, Y, Z { get; set; }      // Координаты в метрах
    public string MacAddress { get; set; }   // UWB модулей
    public double CalibrationOffset { get; set; }  // Коррекция
    public AnchorStatus Status { get; set; }
}
```

---

#### Beacon.cs
```csharp
public class Beacon
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string MacAddress { get; set; }
    public int BatteryLevel { get; set; }     // 0-100%
    public DateTime LastSeen { get; set; }
    public BeaconStatus Status { get; set; }   // Active/LowBattery/Offline
}
```

---

#### Position.cs
```csharp
public class Position
{
    public long Id { get; set; }
    public int BeaconId { get; set; }
    public double X, Y, Z { get; set; }        // Вычисленные координаты
    public double Confidence { get; set; }     // 0.0 - 1.0
    public string Method { get; set; }         // "TWR", "TDoA"
    public DateTime Timestamp { get; set; }
    public int AnchorsUsed { get; set; }
    public double? EstimatedError { get; set; }
}
```

---

### 🧪 BeaconSimulator.cs
**Путь:** `Tests/BeaconSimulator.cs`

**Основной класс:**
```csharp
public class BeaconSimulator
{
    public async Task SimulateStraightPath(
        int beaconId,
        double startX, double startY, double startZ,
        double endX, double endY, double endZ,
        int steps = 20,
        int delayMs = 200
    )
}
```

**Что делает:**
- Симулирует маяк, движущийся по прямой траектории
- Вычисляет расстояния до якорей в каждой точке
- Добавляет реалистичный шум (±0.2м)
- Отправляет POST запросы на `/api/telemetry/measurement`
- Показывает вычисленные позиции

---

### ⚙️ Конфигурационные файлы

#### appsettings.json (Development)
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=strikeball.db"
  },
  "PositioningSettings": {
    "MinimumAnchorsRequired": 3,
    "KalmanFilterEnabled": true,
    "ConfidenceThreshold": 0.7
  }
}
```

#### appsettings.Production.json
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=strikeballdb;..."
  },
  "Kestrel": {
    "Endpoints": {
      "Http": { "Url": "http://0.0.0.0:5000" }
    }
  }
}
```

---

## 📐 СХЕМА ПОТОКА ДАННЫХ

```
МАЯК (UWB модуль)
    ↓
    отправляет расстояния до якорей
    ↓
СЕРВЕР: POST /api/telemetry/measurement
    ↓
TELEMETRY CONTROLLER
    ├─ сохраняет Measurement в БД
    ├─ обновляет статус Beacon
    └─ вызывает PositioningService
    ↓
POSITIONING SERVICE
    ├─ получает координаты якорей
    ├─ применяет калибровку
    ├─ трилатерает 3D (LS метод)
    ├─ вычисляет confidence
    └─ создает Position объект
    ↓
FILTERING SERVICE
    ├─ применяет EMA фильтр
    └─ сглаживает шум
    ↓
RESPONSE (JSON) к клиенту
├─ position: {x, y, z, confidence, ...}
└─ success: true

BROADCAST через SignalR
├─ отправляет PositionUpdate
└─ все подключенные WebSocket клиенты получают обновление
```

---

## 🔍 БЫСТРКА СПРАВКА

| Что нужно | Где искать |
|----------|-----------|
| Запустить сервер | `start-server.ps1` или `Server/Program.cs` |
| Запустить тесты | `Tests/BeaconSimulator.cs` |
| API документация | `Docs/API.md` или http://localhost:5000 (Swagger) |
| Архитектура | `Docs/PROJECT_DOCUMENTATION.md` |
| Как деплоить | `DEPLOYMENT_CHECKLIST.md` или `deploy.sh` |
| Docker | `Dockerfile`, `docker-compose.yml` |
| Linux сервис | `strikeball-server.service` |
| Конфигурация | `Server/appsettings*.json` |
| Модели БД | `Server/Models/*.cs` |
| Бизнес логика | `Server/Services/*.cs` |
| REST endpoints | `Server/Controllers/*.cs` |
| WebSocket | `Server/Hubs/PositioningHub.cs` |
| БД контекст | `Server/Data/ApplicationDbContext.cs` |

---

## 🔐 Структура БД (SQLite/PostgreSQL)

```sql
-- Таблица якорей
CREATE TABLE Anchors (
    Id INTEGER PRIMARY KEY,
    Name VARCHAR NOT NULL,
    X DOUBLE NOT NULL,
    Y DOUBLE NOT NULL,
    Z DOUBLE NOT NULL,
    MacAddress VARCHAR(17),
    CalibrationOffset DOUBLE DEFAULT 0.0,
    Status INTEGER DEFAULT 0,
    CreatedAt TIMESTAMP,
    UpdatedAt TIMESTAMP
);

-- Таблица маяков
CREATE TABLE Beacons (
    Id INTEGER PRIMARY KEY,
    Name VARCHAR NOT NULL,
    MacAddress VARCHAR(17),
    BatteryLevel INTEGER DEFAULT 100,
    Status INTEGER DEFAULT 0,
    LastSeen TIMESTAMP,
    CreatedAt TIMESTAMP
);

-- Таблица измерений
CREATE TABLE Measurements (
    Id INTEGER PRIMARY KEY,
    BeaconId INTEGER NOT NULL,
    AnchorId INTEGER NOT NULL,
    Distance DOUBLE NOT NULL,
    Rssi INTEGER,
    Quality DOUBLE,
    Timestamp TIMESTAMP NOT NULL,
    FOREIGN KEY (BeaconId) REFERENCES Beacons(Id) ON DELETE CASCADE,
    FOREIGN KEY (AnchorId) REFERENCES Anchors(Id) ON DELETE CASCADE
);

-- Таблица позиций
CREATE TABLE Positions (
    Id INTEGER PRIMARY KEY,
    BeaconId INTEGER NOT NULL,
    X DOUBLE NOT NULL,
    Y DOUBLE NOT NULL,
    Z DOUBLE NOT NULL,
    Confidence DOUBLE DEFAULT 1.0,
    Method VARCHAR(20) DEFAULT 'TWR',
    AnchorsUsed INTEGER,
    EstimatedError DOUBLE,
    Timestamp TIMESTAMP NOT NULL,
    FOREIGN KEY (BeaconId) REFERENCES Beacons(Id) ON DELETE CASCADE
);

-- Индексы для производительности
CREATE INDEX idx_positions_beacon_timestamp ON Positions(BeaconId, Timestamp);
CREATE INDEX idx_measurements_beacon_timestamp ON Measurements(BeaconId, Timestamp);
```

---

## 🎯 ИТОГО

Проект полностью организован и готов к:
- ✅ Локальной разработке и тестированию
- ✅ Развертыванию на Linux сервер
- ✅ Интеграции с вашим клиентом

Все файлы, все скрипты, все документация на месте! 🎉

---

**Версия:** 1.0 MVP  
**Дата:** 5 марта 2026  
**Статус:** ✅ ГОТОВО К ТЕСТИРОВАНИЮ
