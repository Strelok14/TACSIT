# 🎉 Strikeball Positioning Server MVP - ГОТОВ!

## ✅ Что сделано

### 1️⃣ Структура проекта
```
StrikeballServer/
├── Server/                          # Основной сервер
│   ├── Controllers/                 # API контроллеры
│   │   ├── TelemetryController.cs   # Прием измерений от маяков
│   │   ├── PositionsController.cs   # Получение позиций
│   │   ├── AnchorsController.cs     # Управление якорями
│   │   └── BeaconsController.cs     # Управление маяками
│   ├── Services/                    # Бизнес-логика
│   │   ├── PositioningService.cs    # Трилатерация 3D (TWR)
│   │   ├── FilteringService.cs      # Фильтр сглаживания (EMA)
│   │   └── TelemetryService.cs      # Метрики
│   ├── Models/                      # Модели данных
│   │   ├── Anchor.cs                # Якорь (базовая станция)
│   │   ├── Beacon.cs                # Маяк (на игроке)
│   │   ├── Measurement.cs           # Сырое измерение
│   │   ├── Position.cs              # Вычисленная позиция
│   │   └── Dtos.cs                  # DTO для API
│   ├── Data/                        # База данных
│   │   └── ApplicationDbContext.cs  # EF Core контекст
│   ├── Hubs/                        # Real-time WebSocket
│   │   └── PositioningHub.cs        # SignalR Hub
│   ├── Program.cs                   # Точка входа
│   └── appsettings.json             # Конфигурация
├── Tests/                           # Тесты и симуляторы
│   └── BeaconSimulator.cs           # Симулятор маяков
├── Docs/                            # Документация
│   ├── PROJECT_DOCUMENTATION.md     # Полная документация
│   └── API.md                       # API референс
├── README.md                        # Быстрый старт
├── start-server.ps1                 # Скрипт запуска сервера
└── start-simulator.ps1              # Скрипт запуска симулятора
```

### 2️⃣ База данных (SQLite)
- ✅ 4 таблицы: Anchors, Beacons, Measurements, Positions
- ✅ Индексы для быстрых запросов
- ✅ Foreign Keys с каскадным удалением
- ✅ Seed данные: 4 якоря в квадрате 50×50м, 2 тестовых маяка

### 3️⃣ API Endpoints
- ✅ `POST /api/telemetry/measurement` — Прием измерений
- ✅ `GET /api/positions` — Список всех позиций
- ✅ `GET /api/positions/{id}` — Позиция конкретного маяка
- ✅ `GET /api/positions/history/{id}` — История позиций
- ✅ `GET/POST/PUT/DELETE /api/anchors` — Управление якорями
- ✅ `GET/POST/PUT/DELETE /api/beacons` — Управление маяками

### 4️⃣ Алгоритм позиционирования
- ✅ **TWR (Two-Way Ranging)** — измерение расстояний
- ✅ **Трилатерация 3D** — метод наименьших квадратов (Least Squares)
- ✅ **Фильтр EMA** — экспоненциальное сглаживание (упрощенный Kalman)
- ✅ **Вычисление Confidence** — оценка точности (0.0–1.0)

### 5️⃣ Real-time обновления
- ✅ **SignalR Hub** — WebSocket для push-уведомлений
- ✅ Broadcast всем клиентам при новой позиции
- ✅ Подписка на конкретный маяк
- ✅ Статус сервера

### 6️⃣ Тестирование
- ✅ Симулятор маяков (BeaconSimulator.cs)
- ✅ Симуляция движения по траектории
- ✅ Симуляция статичного маяка с шумом

### 7️⃣ Документация
- ✅ Полная документация проекта
- ✅ API референс с примерами
- ✅ Инструкции по запуску и развертыванию

---

## 🚀 Быстрый запуск

### Запуск сервера
```powershell
cd StrikeballServer
.\start-server.ps1
```

Или вручную:
```powershell
cd Server
dotnet run --project StrikeballServer.csproj
```

**Сервер будет доступен на:**
- Swagger UI: http://localhost:5000
- WebSocket: ws://localhost:5000/hubs/positioning

### Запуск симулятора маяков
```powershell
cd StrikeballServer
.\start-simulator.ps1
```

---

## 📊 Что тестировать

### 1. Проверка Swagger UI
Открой http://localhost:5000 и попробуй:
- `GET /api/anchors` — должно вернуть 4 якоря
- `GET /api/beacons` — должно вернуть 2 маяка
- `GET /api/positions` — должно быть пусто (пока нет измерений)

### 2. Отправка тестового измерения
```bash
curl -X POST http://localhost:5000/api/telemetry/measurement \
  -H "Content-Type: application/json" \
  -d '{
    "beaconId": 1,
    "distances": [
      {"anchorId": 1, "distance": 15.2},
      {"anchorId": 2, "distance": 38.5},
      {"anchorId": 3, "distance": 42.1},
      {"anchorId": 4, "distance": 20.3}
    ],
    "timestamp": 1709048400000,
    "batteryLevel": 85
  }'
```

### 3. Проверка позиции
```bash
curl http://localhost:5000/api/positions/1
```

Должно вернуть вычисленную позицию маяка 1.

### 4. Запуск симулятора
Симулятор автоматически отправит 2 серии измерений:
- Маяк 1: движется по диагонали (10,10) → (40,40)
- Маяк 2: статичная позиция (25,25) с шумом

---

## 📈 Следующие шаги

### MVP завершен! Теперь можно:

1. **Протестировать на реальном оборудовании**
   - Расставить 4 якоря на полигоне
   - Подключить UWB модули
   - Отправлять реальные измерения

2. **Создать Android приложение**
   - WebSocket клиент для подключения к серверу
   - Отображение карты полигона
   - Визуализация позиций игроков

3. **Улучшить алгоритм позиционирования**
   - Реализовать полный Kalman фильтр
   - Добавить TDoA для масштабирования
   - Оптимизировать точность

4. **Добавить функции**
   - Буферизация при потере LTE
   - Оффлайн режим
   - Веб-интерфейс для штаба
   - Экспорт истории боев

---

## 🎯 Текущие характеристики

| Параметр | Значение |
|----------|----------|
| Минимум якорей | 3 (рекомендуется 4+) |
| Метод позиционирования | TWR + Трилатерация 3D |
| Фильтрация | EMA (экспоненциальное сглаживание) |
| Real-time | SignalR WebSocket |
| БД | SQLite (dev) / PostgreSQL (prod) |
| Платформа | .NET 8, ASP.NET Core |
| ОС | Linux Debian / Windows |

---

## 📝 Известные ограничения

1. Упрощенный фильтр (EMA вместо полного Kalman)
2. Нет обработки многолучевого распространения (multipath)
3. Нет аутентификации и авторизации
4. Нет буферизации при потере сети
5. Нет калибровки якорей на сервере

Эти функции можно добавить в следующих версиях.

---

## 🐛 Устранение неполадок

### Сервер не запускается
```powershell
# Проверь версию .NET
dotnet --version  # Должно быть 8.0+

# Пересобери проект
cd Server
dotnet clean
dotnet restore
dotnet build
```

### Ошибки компиляции
```powershell
# Удали кэш и пересобери
Remove-Item -Recurse -Force bin, obj
dotnet restore
dotnet build
```

### БД не создается
```powershell
# Проверь права доступа к папке
# БД создается автоматически в Server/strikeball.db
```

---

## 🏆 Готово к тестированию!

Сервер полностью готов к работе. Можно начинать тестирование на реальном оборудовании!

**Дата завершения:** 27 февраля 2026  
**Версия:** 1.0 MVP
