# API Документация - Strikeball Positioning Server

## Базовый URL
```
http://localhost:5000/api
```

---

## 📡 Телеметрия

### POST /api/telemetry/measurement
Прием пакета измерений от маяка

**Request Body:**
```json
{
  "beaconId": 1,
  "distances": [
    {
      "anchorId": 1,
      "distance": 5.2,
      "rssi": -45
    },
    {
      "anchorId": 2,
      "distance": 7.3,
      "rssi": -48
    },
    {
      "anchorId": 3,
      "distance": 4.8,
      "rssi": -43
    },
    {
      "anchorId": 4,
      "distance": 6.1,
      "rssi": -46
    }
  ],
  "timestamp": 1709048400000,
  "batteryLevel": 85
}
```

**Response (200 OK):**
```json
{
  "success": true,
  "position": {
    "beaconId": 1,
    "beaconName": "Player_1",
    "x": 15.23,
    "y": 18.67,
    "z": 1.5,
    "confidence": 0.95,
    "method": "TWR_EMA",
    "timestamp": "2026-02-27T12:30:00Z",
    "anchorsUsed": 4
  }
}
```

---

## 📍 Позиции

### GET /api/positions
Получить текущие позиции всех активных маяков

**Response (200 OK):**
```json
{
  "positions": [
    {
      "beaconId": 1,
      "beaconName": "Player_1",
      "x": 15.23,
      "y": 18.67,
      "z": 1.5,
      "confidence": 0.95,
      "method": "TWR_EMA",
      "timestamp": "2026-02-27T12:30:00Z",
      "anchorsUsed": 4
    },
    {
      "beaconId": 2,
      "beaconName": "Player_2",
      "x": 22.45,
      "y": 30.12,
      "z": 1.5,
      "confidence": 0.92,
      "method": "TWR_EMA",
      "timestamp": "2026-02-27T12:30:01Z",
      "anchorsUsed": 4
    }
  ],
  "timestamp": "2026-02-27T12:30:01Z",
  "totalBeacons": 2
}
```

### GET /api/positions/{beaconId}
Получить текущую позицию конкретного маяка

**Path Parameters:**
- `beaconId` (int) - ID маяка

**Response (200 OK):**
```json
{
  "beaconId": 1,
  "beaconName": "Player_1",
  "x": 15.23,
  "y": 18.67,
  "z": 1.5,
  "confidence": 0.95,
  "method": "TWR_EMA",
  "timestamp": "2026-02-27T12:30:00Z",
  "anchorsUsed": 4
}
```

### GET /api/positions/history/{beaconId}
Получить историю позиций маяка за период

**Path Parameters:**
- `beaconId` (int) - ID маяка

**Query Parameters:**
- `from` (DateTime, optional) - Начало периода (ISO 8601)
- `to` (DateTime, optional) - Конец периода (ISO 8601)
- `limit` (int, optional, default: 1000) - Максимальное количество записей

**Example:**
```
GET /api/positions/history/1?from=2026-02-27T10:00:00Z&to=2026-02-27T12:00:00Z&limit=500
```

**Response (200 OK):**
```json
[
  {
    "beaconId": 1,
    "beaconName": "Player_1",
    "x": 15.23,
    "y": 18.67,
    "z": 1.5,
    "confidence": 0.95,
    "method": "TWR_EMA",
    "timestamp": "2026-02-27T12:30:00Z",
    "anchorsUsed": 4
  },
  {
    "beaconId": 1,
    "beaconName": "Player_1",
    "x": 15.45,
    "y": 18.89,
    "z": 1.5,
    "confidence": 0.94,
    "method": "TWR_EMA",
    "timestamp": "2026-02-27T12:30:01Z",
    "anchorsUsed": 4
  }
]
```

---

## ⚓ Якоря (Anchors)

### GET /api/anchors
Получить список всех якорей

**Response (200 OK):**
```json
[
  {
    "id": 1,
    "name": "Anchor_1",
    "x": 0.0,
    "y": 0.0,
    "z": 2.0,
    "macAddress": "AA:BB:CC:DD:EE:01",
    "calibrationOffset": 0.0,
    "status": 1,
    "createdAt": "2026-02-27T10:00:00Z",
    "updatedAt": "2026-02-27T10:00:00Z"
  },
  {
    "id": 2,
    "name": "Anchor_2",
    "x": 50.0,
    "y": 0.0,
    "z": 2.0,
    "macAddress": "AA:BB:CC:DD:EE:02",
    "calibrationOffset": 0.0,
    "status": 1,
    "createdAt": "2026-02-27T10:00:00Z",
    "updatedAt": "2026-02-27T10:00:00Z"
  }
]
```

**Status Values:**
- `1` - Active
- `2` - Inactive
- `3` - Error
- `4` - Maintenance

### GET /api/anchors/{id}
Получить якорь по ID

### POST /api/anchors
Добавить новый якорь

**Request Body:**
```json
{
  "name": "Anchor_5",
  "x": 25.0,
  "y": 25.0,
  "z": 2.5,
  "macAddress": "AA:BB:CC:DD:EE:05",
  "calibrationOffset": 0.0,
  "status": 1
}
```

### PUT /api/anchors/{id}
Обновить координаты якоря

**Request Body:**
```json
{
  "id": 1,
  "name": "Anchor_1_Updated",
  "x": 1.0,
  "y": 1.0,
  "z": 2.1,
  "macAddress": "AA:BB:CC:DD:EE:01",
  "calibrationOffset": 0.05,
  "status": 1
}
```

### DELETE /api/anchors/{id}
Удалить якорь

---

## 📡 Маяки (Beacons)

### GET /api/beacons
Получить список всех маяков

**Response (200 OK):**
```json
[
  {
    "id": 1,
    "name": "Player_1",
    "macAddress": "FF:EE:DD:CC:BB:01",
    "batteryLevel": 85,
    "lastSeen": "2026-02-27T12:30:00Z",
    "status": 1,
    "createdAt": "2026-02-27T10:00:00Z"
  },
  {
    "id": 2,
    "name": "Player_2",
    "macAddress": "FF:EE:DD:CC:BB:02",
    "batteryLevel": 92,
    "lastSeen": "2026-02-27T12:30:01Z",
    "status": 1,
    "createdAt": "2026-02-27T10:00:00Z"
  }
]
```

**Status Values:**
- `1` - Active
- `2` - Offline
- `3` - LowBattery
- `4` - Error

### GET /api/beacons/{id}
Получить маяк по ID

### POST /api/beacons
Зарегистрировать новый маяк

**Request Body:**
```json
{
  "name": "Player_3",
  "macAddress": "FF:EE:DD:CC:BB:03",
  "batteryLevel": 100,
  "status": 1
}
```

### PUT /api/beacons/{id}
Обновить информацию о маяке

### DELETE /api/beacons/{id}
Удалить маяк

---

## 🔌 WebSocket (SignalR Hub)

### Подключение
```
ws://localhost:5000/hubs/positioning
```

### События от сервера

#### Connected
Подтверждение подключения
```json
{
  "connectionId": "abc123",
  "message": "Успешное подключение к серверу позиционирования",
  "timestamp": "2026-02-27T12:30:00Z"
}
```

#### PositionUpdate
Обновление позиции маяка (broadcast всем клиентам)
```json
{
  "beaconId": 1,
  "beaconName": "Player_1",
  "x": 15.23,
  "y": 18.67,
  "z": 1.5,
  "confidence": 0.95,
  "method": "TWR_EMA",
  "timestamp": "2026-02-27T12:30:00Z",
  "anchorsUsed": 4
}
```

### Методы клиента

#### SubscribeToBeacon
Подписаться на обновления конкретного маяка
```javascript
connection.invoke("SubscribeToBeacon", 1);
```

#### UnsubscribeFromBeacon
Отписаться от обновлений маяка
```javascript
connection.invoke("UnsubscribeFromBeacon", 1);
```

#### GetServerStatus
Получить статус сервера
```javascript
connection.invoke("GetServerStatus");
```

---

## ❌ Коды ошибок

- `200 OK` - Успешный запрос
- `201 Created` - Ресурс создан
- `204 No Content` - Успешное обновление/удаление
- `400 Bad Request` - Некорректные данные
- `404 Not Found` - Ресурс не найден
- `500 Internal Server Error` - Ошибка сервера

**Пример ответа с ошибкой:**
```json
{
  "error": "Маяк с ID 99 не найден"
}
```
