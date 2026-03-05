# Strikeball Positioning Server

Серверное приложение для системы позиционирования игроков в страйкбол с использованием UWB технологии.

## 🚀 Быстрый старт

### Требования
- .NET 8.0 SDK
- SQLite (для разработки) или PostgreSQL (для продакшена)

### Установка зависимостей

```bash
cd Server
dotnet restore
```

### Запуск сервера

```bash
dotnet run
```

Сервер запустится на `http://localhost:5000`

### Swagger UI

Документация API доступна по адресу: `http://localhost:5000`

### WebSocket Hub

WebSocket подключение для real-time обновлений: `ws://localhost:5000/hubs/positioning`

## 📡 API Endpoints

### Телеметрия
- `POST /api/telemetry/measurement` — Прием пакета измерений от маяка

### Позиции
- `GET /api/positions` — Список всех текущих позиций
- `GET /api/positions/{beaconId}` — Позиция конкретного маяка
- `GET /api/positions/history/{beaconId}` — История позиций

### Якоря
- `GET /api/anchors` — Список всех якорей
- `POST /api/anchors` — Добавить якорь
- `PUT /api/anchors/{id}` — Обновить якорь
- `DELETE /api/anchors/{id}` — Удалить якорь

### Маяки
- `GET /api/beacons` — Список всех маяков
- `POST /api/beacons` — Зарегистрировать маяк
- `PUT /api/beacons/{id}` — Обновить маяк
- `DELETE /api/beacons/{id}` — Удалить маяк

## 🗄️ База данных

### Миграции (Entity Framework Core)

Создание миграции:
```bash
dotnet ef migrations add InitialCreate
```

Применение миграции:
```bash
dotnet ef database update
```

## 🧪 Тестирование

Пример запроса для отправки измерения:

```bash
curl -X POST http://localhost:5000/api/telemetry/measurement \
  -H "Content-Type: application/json" \
  -d '{
    "beaconId": 1,
    "distances": [
      {"anchorId": 1, "distance": 5.2},
      {"anchorId": 2, "distance": 7.3},
      {"anchorId": 3, "distance": 4.8},
      {"anchorId": 4, "distance": 6.1}
    ],
    "timestamp": 1709048400000,
    "batteryLevel": 85
  }'
```

## 📚 Дополнительная документация

Полная документация проекта находится в `Docs/PROJECT_DOCUMENTATION.md`

## 🐧 Развертывание на Linux

См. инструкции в `Docs/PROJECT_DOCUMENTATION.md`, раздел "Развертывание"
