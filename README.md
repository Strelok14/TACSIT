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

Сервер запускается с обязательным HTTPS (для внешних подключений).

Перед запуском задайте минимум:

```bash
export TACID_JWT_SIGNING_KEY="change_me_to_very_long_random_key_32+chars"
export TACID_MASTER_KEY_B64="<base64_of_32_bytes>"
export TACID_ADMIN_LOGIN="admin"
export TACID_ADMIN_PASSWORD="strong_password"
export REDIS_CONNECTION_STRING="localhost:6379,abortConnect=false"
```

### Swagger UI

Документация API доступна по адресу: `https://localhost:5001` (или через reverse proxy).

### WebSocket Hub

WebSocket подключение для real-time обновлений: `wss://<host>/hubs/positioning`

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

Краткая инструкция для ручного теста с ПК и телефона: `Docs/QUICK_MANUAL_TEST.md`

One-page версия для боевого прогона: `Docs/QUICK_MANUAL_TEST_ONEPAGE.md`

## 🐧 Развертывание на Linux

См. инструкции в `Docs/PROJECT_DOCUMENTATION.md`, раздел "Развертывание".

Для TLS через Nginx используйте:

```bash
sudo bash scripts/setup_tls_nginx.sh your-domain.example
```
