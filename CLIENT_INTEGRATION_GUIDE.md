📱 ИНТЕГРАЦИЯ КЛИЕНТСКОГО ПРИЛОЖЕНИЯ С СЕРВЕРОМ
═════════════════════════════════════════════════════════════════════

СОДЕРЖАНИЕ:
1. Общая архитектура обмена данными
2. REST API примеры
3. WebSocket примеры
4. Java/Kotlin для Android
5. JavaScript для Web
6. Python для тестирования

═════════════════════════════════════════════════════════════════════

1️⃣ ОБЩАЯ АРХИТЕКТУРА ОБМЕНА ДАННЫМИ

Маяк отправляет → Сервер вычисляет → Клиент получает

ЭТАП 1: Маяк отправляет измерения
   POST /api/telemetry/measurement
   {
     beaconId: 1,
     distances: [
       {anchorId: 1, distance: 15.2, rssi: -45},
       {anchorId: 2, distance: 38.5, rssi: -48},
       ...
     ],
     timestamp: 1709048400000,
     batteryLevel: 85
   }

ЭТАП 2: Сервер вычисляет позицию
   ↓ PositioningService (трилатерация 3D)
   ↓ FilteringService (EMA фильтр)
   ↓ Сохранение в БД

ЭТАП 3A: Клиент получает через REST
   GET /api/positions/1
   Response: {x: 23.45, y: 24.56, z: 0.15, confidence: 0.92}

ЭТАП 3B: Клиент получает через WebSocket
   ws://server:5000/hubs/positioning
   Событие: PositionUpdate {x, y, z, confidence, ...}

═════════════════════════════════════════════════════════════════════

2️⃣ REST API ПРИМЕРЫ

BASE_URL = "http://server:5000/api"

a) ОТПРАВИТЬ ИЗМЕРЕНИЕ ОТ МАЯКА
──────────────────────────────────

POST /telemetry/measurement

ЗАПРОС:
{
  "beaconId": 1,
  "distances": [
    {
      "anchorId": 1,
      "distance": 15.2,          // в метрах
      "rssi": -45               // опционально (RSSI сигнала)
    },
    {
      "anchorId": 2,
      "distance": 38.5,
      "rssi": -48
    },
    {
      "anchorId": 3,
      "distance": 42.1,
      "rssi": -52
    },
    {
      "anchorId": 4,
      "distance": 20.3,
      "rssi": -46
    }
  ],
  "timestamp": 1709048400000,    // Unix timestamp в миллисекундах
  "batteryLevel": 85             // 0-100, уровень батареи маяка
}

ОТВЕТ (200 OK):
{
  "success": true,
  "position": {
    "beaconId": 1,
    "beaconName": "Player_1",
    "x": 23.45,                  // в метрах
    "y": 24.56,
    "z": 0.15,
    "confidence": 0.92,          // 0.0-1.0, уверенность в позиции
    "method": "TWR",
    "timestamp": "2026-03-05T10:30:00Z",
    "anchorsUsed": 4
  }
}

b) ПОЛУЧИТЬ ТЕКУЩУЮ ПОЗИЦИЮ МАЯКА
──────────────────────────────────

GET /positions/{beaconId}

ПРИМЕР: GET /positions/1

ОТВЕТ:
{
  "beaconId": 1,
  "x": 23.45,
  "y": 24.56,
  "z": 0.15,
  "confidence": 0.92,
  "timestamp": "2026-03-05T10:30:00Z"
}

c) ПОЛУЧИТЬ ВСЕ ТЕКУЩИЕ ПОЗИЦИИ
────────────────────────────────

GET /positions

ОТВЕТ:
{
  "positions": [
    {
      "beaconId": 1,
      "beaconName": "Player_1",
      "x": 23.45,
      "y": 24.56,
      "z": 0.15,
      "confidence": 0.92,
      ...
    },
    {
      "beaconId": 2,
      "beaconName": "Player_2",
      "x": 35.12,
      "y": 36.78,
      "z": 0.12,
      "confidence": 0.88,
      ...
    }
  ],
  "totalBeacons": 2,
  "timestamp": "2026-03-05T10:30:10Z"
}

d) ПОЛУЧИТЬ ИСТОРИЮ ПОЗИЦИЙ
────────────────────────────

GET /positions/history/{beaconId}

ПРИМЕР: GET /positions/history/1

ОТВЕТ: Массив позиций за последнее время

e) ПОЛУЧИТЬ КООРДИНАТЫ ЯКОРЕЙ
─────────────────────────────

GET /anchors

ОТВЕТ:
[
  {
    "id": 1,
    "name": "Anchor_1",
    "x": 0.0,
    "y": 0.0,
    "z": 2.0,
    "status": "Active"
  },
  {
    "id": 2,
    "name": "Anchor_2",
    "x": 50.0,
    "y": 0.0,
    "z": 2.0,
    "status": "Active"
  },
  ...
]

═════════════════════════════════════════════════════════════════════

3️⃣ WEBSOCKET ПРИМЕРЫ (REAL-TIME)

Подключиться:
  ws://server:5000/hubs/positioning

События которые получает клиент:

a) Connected - сразу при подключении
─────────────────────────────────────
{
  "connectionId": "ABC123DEF456",
  "message": "Успешное подключение к серверу",
  "timestamp": "2026-03-05T10:30:00Z"
}

b) PositionUpdate - когда маяк обновляет позицию
──────────────────────────────────────────────────
{
  "beaconId": 1,
  "beaconName": "Player_1",
  "x": 23.45,
  "y": 24.56,
  "z": 0.15,
  "confidence": 0.92,
  "method": "TWR",
  "timestamp": "2026-03-05T10:30:00Z",
  "anchorsUsed": 4
}

═════════════════════════════════════════════════════════════════════

4️⃣ ANDROID (Java/Kotlin) ПРИМЕРЫ

a) REST API - OkHttp
────────────────────

// Отправить измерение
OkHttpClient client = new OkHttpClient();

String json = "{\n" +
    "  \"beaconId\": 1,\n" +
    "  \"distances\": [\n" +
    "    {\"anchorId\": 1, \"distance\": 15.2, \"rssi\": -45},\n" +
    "    {\"anchorId\": 2, \"distance\": 38.5, \"rssi\": -48},\n" +
    "    {\"anchorId\": 3, \"distance\": 42.1, \"rssi\": -52},\n" +
    "    {\"anchorId\": 4, \"distance\": 20.3, \"rssi\": -46}\n" +
    "  ],\n" +
    "  \"timestamp\": " + System.currentTimeMillis() + ",\n" +
    "  \"batteryLevel\": 85\n" +
    "}";

RequestBody body = RequestBody.create(
    json,
    MediaType.parse("application/json; charset=utf-8")
);

Request request = new Request.Builder()
    .url("http://server:5000/api/telemetry/measurement")
    .post(body)
    .build();

client.newCall(request).enqueue(new Callback() {
    @Override
    public void onResponse(Call call, Response response) throws IOException {
        String responseBody = response.body().string();
        Log.d("API", "Response: " + responseBody);
        // Парсить JSON и обновить UI
    }

    @Override
    public void onFailure(Call call, IOException e) {
        Log.e("API", "Error: " + e.getMessage());
    }
});

// Получить позицию
Request getRequest = new Request.Builder()
    .url("http://server:5000/api/positions/1")
    .get()
    .build();

client.newCall(getRequest).enqueue(new Callback() {
    @Override
    public void onResponse(Call call, Response response) throws IOException {
        JSONObject position = new JSONObject(response.body().string());
        double x = position.getDouble("x");
        double y = position.getDouble("y");
        double z = position.getDouble("z");
        double confidence = position.getDouble("confidence");
        
        // Отобразить позицию на карте
        updateMapPosition(x, y, confidence);
    }

    @Override
    public void onFailure(Call call, IOException e) {
        Log.e("API", "Failed to get position: " + e.getMessage());
    }
});

b) WebSocket - SignalR для Android
────────────────────────────────────

// build.gradle
dependencies {
    implementation 'com.microsoft.signalr:signalr:8.0.0'
}

// Код
import com.microsoft.signalr.HubConnection;
import com.microsoft.signalr.HubConnectionBuilder;

HubConnection connection = HubConnectionBuilder
    .create("ws://server:5000/hubs/positioning")
    .withAutomaticReconnect()
    .build();

connection.on("PositionUpdate", message -> {
    Log.d("WebSocket", "PositionUpdate: " + message);
    
    // Парсить message и обновить UI
    try {
        JSONObject position = new JSONObject(message);
        int beaconId = position.getInt("beaconId");
        double x = position.getDouble("x");
        double y = position.getDouble("y");
        double confidence = position.getDouble("confidence");
        
        // Обновить позицию на карте
        updatePlayerPosition(beaconId, x, y, confidence);
    } catch (JSONException e) {
        Log.e("WebSocket", "Parse error: " + e.getMessage());
    }
}, String.class);

connection.on("Connected", connectionData -> {
    Log.d("WebSocket", "Connected: " + connectionData);
}, String.class);

connection.start()
    .blockingAwait(); // или асинхронно в корутине

═════════════════════════════════════════════════════════════════════

5️⃣ JAVASCRIPT/WEB ПРИМЕРЫ

a) REST API - Fetch
──────────────────

// Отправить измерение
const measurement = {
    beaconId: 1,
    distances: [
        { anchorId: 1, distance: 15.2, rssi: -45 },
        { anchorId: 2, distance: 38.5, rssi: -48 },
        { anchorId: 3, distance: 42.1, rssi: -52 },
        { anchorId: 4, distance: 20.3, rssi: -46 }
    ],
    timestamp: Date.now(),
    batteryLevel: 85
};

fetch('http://server:5000/api/telemetry/measurement', {
    method: 'POST',
    headers: {
        'Content-Type': 'application/json'
    },
    body: JSON.stringify(measurement)
})
.then(response => response.json())
.then(data => {
    console.log('Success:', data.position);
    // Обновить UI с позицией
})
.catch(error => {
    console.error('Error:', error);
});

// Получить позицию
fetch('http://server:5000/api/positions/1')
    .then(response => response.json())
    .then(position => {
        console.log(`Beacon 1: x=${position.x}, y=${position.y}, confidence=${position.confidence}`);
        // Отобразить на карте
        displayPosition(position);
    });

// Получить все позиции
fetch('http://server:5000/api/positions')
    .then(response => response.json())
    .then(data => {
        data.positions.forEach(position => {
            console.log(`Beacon ${position.beaconId}: (${position.x}, ${position.y})`);
        });
        // Обновить все маяки на карте
        updateAllPositions(data.positions);
    });

b) WebSocket - SignalR для Web
───────────────────────────────

// npm install @microsoft/signalr

import * as signalR from "@microsoft/signalr";

const connection = new signalR.HubConnectionBuilder()
    .withUrl("ws://server:5000/hubs/positioning")
    .withAutomaticReconnect()
    .withHubProtocol(new signalR.JsonHubProtocol())
    .build();

connection.on("Connected", (connectionData) => {
    console.log("Connected:", connectionData);
});

connection.on("PositionUpdate", (position) => {
    console.log(`Beacon ${position.beaconId}: (${position.x}, ${position.y}, ${position.z})`);
    console.log(`Confidence: ${position.confidence}, Method: ${position.method}`);
    
    // Обновить позицию на карте
    updateMapMarker(position.beaconId, position.x, position.y, position.confidence);
});

connection.onreconnecting((error) => {
    console.log("Reconnecting:", error);
});

connection.onreconnected((connectionId) => {
    console.log("Reconnected:", connectionId);
});

connection.start()
    .then(() => console.log("Connected to server"))
    .catch(error => console.error("Connection failed:", error));

═════════════════════════════════════════════════════════════════════

6️⃣ PYTHON ПРИМЕРЫ (ДЛЯ ТЕСТИРОВАНИЯ)

# pip install requests websocket-client

import requests
import json
import time

SERVER = "http://localhost:5000"

# Отправить измерение
def send_measurement(beacon_id, distances, battery):
    data = {
        "beaconId": beacon_id,
        "distances": distances,
        "timestamp": int(time.time() * 1000),
        "batteryLevel": battery
    }
    
    response = requests.post(
        f"{SERVER}/api/telemetry/measurement",
        json=data
    )
    
    if response.status_code == 200:
        result = response.json()
        print(f"✅ Beacon {beacon_id} position: {result['position']}")
    else:
        print(f"❌ Error: {response.status_code}")

# Получить позицию
def get_position(beacon_id):
    response = requests.get(f"{SERVER}/api/positions/{beacon_id}")
    position = response.json()
    print(f"Position: x={position['x']:.2f}, y={position['y']:.2f}, z={position['z']:.2f}")
    print(f"Confidence: {position['confidence']:.2f}")

# Получить якоря (для расчета расстояний)
def get_anchors():
    response = requests.get(f"{SERVER}/api/anchors")
    anchors = response.json()
    for anchor in anchors:
        print(f"Anchor {anchor['id']}: (x={anchor['x']}, y={anchor['y']}, z={anchor['z']})")
    return anchors

# WebSocket
import websocket
import json

def on_message(ws, message):
    data = json.loads(message)
    if data.get('method') == 'PositionUpdate':
        position = data['arguments'][0]
        print(f"🎯 {position['beaconName']}: ({position['x']:.2f}, {position['y']:.2f})")

def on_error(ws, error):
    print(f"❌ Error: {error}")

def on_close(ws, close_status_code, close_msg):
    print("WebSocket closed")

def on_open(ws):
    print("WebSocket connected")

ws = websocket.WebSocketApp("ws://localhost:5000/hubs/positioning",
                          on_message=on_message,
                          on_error=on_error,
                          on_close=on_close,
                          on_open=on_open)

# ws.run_forever()

═════════════════════════════════════════════════════════════════════

7️⃣ ФАКТИЧЕСКИЙ ПРИМЕР: ДВИЖУЩИЙСЯ МАЯК

Координаты якорей:
  Якорь 1: (0,   0,  2) - верхний левый
  Якорь 2: (50,  0,  2) - верхний правый
  Якорь 3: (50, 50,  2) - нижний правый
  Якорь 4: (0,  50,  2) - нижний левый

Маяк движется по диагонали (0,0) → (50,50) за 10 вызовов

import math

def calculate_distance(beacon_x, beacon_y, beacon_z, anchor_x, anchor_y, anchor_z):
    return math.sqrt(
        (beacon_x - anchor_x)**2 + 
        (beacon_y - anchor_y)**2 + 
        (beacon_z - anchor_z)**2
    )

anchors = [
    (1, 0,   0,  2),
    (2, 50,  0,  2),
    (3, 50, 50,  2),
    (4, 0,  50,  2)
]

beacon_id = 1
beacon_z = 0.5  # высота маяка

# Движение
for step in range(10):
    # Интерполяция позиции
    t = step / 9.0  # 0.0 до 1.0
    beacon_x = 0 + 50 * t
    beacon_y = 0 + 50 * t
    
    # Вычислить расстояния
    distances = [
        {
            "anchorId": anchor_id,
            "distance": calculate_distance(beacon_x, beacon_y, beacon_z, 
                                         anchor_x, anchor_y, anchor_z)
        }
        for anchor_id, anchor_x, anchor_y, anchor_z in anchors
    ]
    
    # Отправить
    send_measurement(beacon_id, distances, battery=100-step)
    
    print(f"Step {step+1}: ({beacon_x:.1f}, {beacon_y:.1f}) → {distances}")
    time.sleep(0.5)

═════════════════════════════════════════════════════════════════════

8️⃣ ТИПИЧНЫЙ WORKFLOW ТЕСТИРОВАНИЯ

1. ПОДГОТОВКА:
   ✅ Убедитесь что сервер работает (http://server:5000 доступен)
   ✅ Получите координаты якорей (GET /api/anchors)

2. ТЕСТ 1 - Статичный маяк:
   ✅ Отправьте одно измерение (POST /api/telemetry/measurement)
   ✅ Получите позицию (GET /api/positions/1)
   ✅ Проверьте что confidence > 0.8

3. ТЕСТ 2 - Движущийся маяк:
   ✅ Отправьте 5+ измерений с разными позициями
   ✅ Проверьте историю (GET /api/positions/history/1)
   ✅ Убедитесь что координаты близки к ожидаемым

4. ТЕСТ 3 - Real-time с WebSocket:
   ✅ Подключитесь через ws://
   ✅ Отправьте измерение
   ✅ Получите событие PositionUpdate в WebSocket

5. ТЕСТ 4 - Несколько маяков:
   ✅ Отправьте измерения для маяков 1,2,3,4
   ✅ Проверьте GET /api/positions (должны быть все)

═════════════════════════════════════════════════════════════════════

✅ ГОТОВО!

Сервер полностью готов принимать данные от вашего клиентского приложения.
