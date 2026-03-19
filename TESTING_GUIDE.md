# 🎯 ИНСТРУКЦИЯ ПО ТЕСТИРОВАНИЮ И РАЗВЕРТЫВАНИЮ СЕРВЕРА

**Дата:** 5 марта 2026  
**Версия:** 1.0 MVP  
**Статус:** ✅ ПОЛНОСТЬЮ ГОТОВ К ТЕСТИРОВАНИЮ

---

## 📋 СОДЕРЖАНИЕ

1. [Быстрый старт (локально)](#быстрый-старт)
2. [Тестирование с симулятором](#тестирование-с-симулятором)
3. [Развертывание на Linux сервер](#развертывание-на-linux)
4. [Развертывание с Docker](#развертывание-с-docker)
5. [Тестирование API](#тестирование-api)
6. [Интеграция с клиентом](#интеграция-с-клиентом)

---

## 🚀 Быстрый старт

### На Windows (локально)

```powershell
# Перейти в папку проекта
cd StrikeballServer

# Запустить сервер (в одном окне PowerShell)
.\start-server.ps1

# В другом окне PowerShell запустить симулятор
.\start-simulator.ps1
```

**Результат:**
- Сервер доступен: http://localhost:5000
- Swagger UI: http://localhost:5000
- WebSocket: ws://localhost:5000/hubs/positioning

---

## 🧪 Тестирование с симулятором

### Сценарий 1: Маяк движется по диагонали

Симулятор автоматически:
1. Создает маяк 1 ("Player_1")
2. Симулирует его движение от (10,10,0) к (40,40,0)
3. Отправляет 20 пакетов измерений
4. Показывает вычисленные позиции

**Проверяем в Swagger:**
```
GET /api/positions/1
```

Должны увидеть позиции с координатами, приближающимися к (25,25,0) с высокой confidence.

### Сценарий 2: Несколько маяков одновременно

```powershell
# Запустить несколько симуляторов в разных окнах:
# Окно 1: Маяк 1 движется
.\start-simulator.ps1

# Окно 2: Маяк 2 статичный (опционально)
# или модифицировать BeaconSimulator.cs
```

---

## 📡 Развертывание на Linux

### Цель: Запустить сервер на реальном сервере

### Шаг 1. Подготовка сервера

```bash
# На вашем Linux сервере (Debian/Ubuntu)
sudo apt-get update
sudo apt-get install -y git

# Клонировать репозиторий (или перенести файлы файлом)
cd /opt
git clone <your-repo-url> strikeball-server
cd strikeball-server
```

### Шаг 2. Автоматическое развертывание

```bash
# Дать права на выполнение скрипта
chmod +x deploy.sh

# Запустить развертывание (требует sudo)
sudo ./deploy.sh
```

Скрипт автоматически:
- ✅ Установит .NET 8 Runtime
- ✅ Создаст пользователя `strikeball`
- ✅ Установит и настроит PostgreSQL
- ✅ Опубликует приложение
- ✅ Установит systemd сервис
- ✅ Запустит сервер

### Шаг 3. Проверка

```bash
# Смотреть статус
sudo systemctl status strikeball-server

# Смотреть логи (real-time)
sudo journalctl -u strikeball-server -f

# Проверить доступность (с любого компьютера в сети)
curl http://<ip-сервера>:5000/swagger/index.html
```

---

## 🐳 Развертывание с Docker

### Требования:
- Docker & Docker Compose установлены

### Запуск за 1 минуту

```bash
cd StrikeballServer

# Используем готовый docker-compose.yml
docker-compose up -d

# Проверяем статус
docker-compose ps

# Смотрим логи
docker-compose logs -f server
```

**Что происходит:**
1. Автоматически скачивается и запускается PostgreSQL
2. Собирается Docker image Strikeball Server
3. Сервер запускается на http://localhost:5000
4. БД автоматически инициализируется

---

## 📊 Тестирование API

### 1. Проверка здоровья сервера

```bash
curl http://localhost:5000/swagger/index.html
```

### 2. Получить список якорей (должно быть 4)

```bash
curl http://localhost:5000/api/anchors
```

**Ответ:**
```json
[
  {
    "id": 1,
    "name": "Anchor_1",
    "x": 0.0,
    "y": 0.0,
    "z": 2.0,
    "status": 0
  },
  ...
]
```

### 3. Отправить тестовое измерение

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

**Ответ:**
```json
{
  "success": true,
  "position": {
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
}
```

### 4. Получить историю позиций маяка 1

```bash
curl http://localhost:5000/api/positions/history/1
```

### 5. WebSocket подключение (для real-time обновлений)

```bash
# Используйте wscat (npm install -g wscat)
wscat -c ws://localhost:5000/hubs/positioning

# Затем отправьте любой пакет в /api/telemetry/measurement
# и увидите PositionUpdate в консоли wscat
```

---

## 📱 Интеграция с клиентом

### Для вашего Android приложения

#### REST API Endpoints:

```
BASE_URL = "http://<server-ip>:5000/api"

// Отправить измерение от маяка
POST /telemetry/measurement

// Получить текущую позицию маяка
GET /positions/{beaconId}

// Получить все позиции (все маяки)
GET /positions

// Получить историю позиций
GET /positions/history/{beaconId}

// Получить список якорей (для расчетов на клиенте)
GET /anchors

// Получить список маяков
GET /beacons
```

#### WebSocket (SignalR):

```javascript
// Пример на JavaScript/TypeScript
import * as signalR from '@microsoft/signalr';

const connection = new signalR.HubConnectionBuilder()
    .withUrl("ws://server-ip:5000/hubs/positioning")
    .withAutomaticReconnect()
    .build();

connection.on("PositionUpdate", (position) => {
    console.log(`Маяк ${position.beaconId} в позиции:`, {
        x: position.x,
        y: position.y,
        z: position.z,
        confidence: position.confidence
    });
});

connection.on("Connected", (data) => {
    console.log("Подключено к серверу", data);
});

connection.start().catch(err => console.error(err));
```

#### Примечания для Android:

1. **Используйте OkHttp** для REST вызовов
2. **Для WebSocket используйте:**
   - `okhttp3` с WebSocket поддержкой, или
   - `Socket.IO` клиент (если добавить поддержку на сервере)
3. **Обработайте переподключение** при потере сети
4. **Кэшируйте позиции** для offline-режима (опционально)

---

## 🔍 Отладка проблем

### Сервер не запускается

```bash
# Проверить версию .NET
dotnet --version  # Должно быть 8.0+

# Пересобрать проект
cd Server
dotnet clean
dotnet restore
dotnet build
dotnet run
```

### БД не инициализируется

```bash
# Проверить доступность PostgreSQL
psql -h localhost -U strikeballuser -d strikeballdb

# Или создать БД вручную
sudo -u postgres psql <<EOF
CREATE DATABASE strikeballdb;
CREATE USER strikeballuser WITH PASSWORD 'password';
GRANT ALL ON DATABASE strikeballdb TO strikeballuser;
EOF
```

### WebSocket не подключается

```bash
# Проверить что сервер запущен
curl -I http://localhost:5000

# Проверить что SignalR hub доступен
curl http://localhost:5000/hubs/positioning/negotiate

# Проверить логи сервера
sudo journalctl -u strikeball-server -n 50
```

### Медленные ответы

```bash
# Показать размер БД
du -sh /opt/strikeball/server/strikeball.db

# Или для PostgreSQL
sudo -u postgres psql -d strikeballdb -c 'SELECT pg_size_pretty(pg_database_size(current_database()));'

# Очистить старые позиции (если необходимо)
sudo -u postgres psql -d strikeballdb <<EOF
DELETE FROM "Positions" WHERE "Timestamp" < NOW() - INTERVAL '30 days';
DELETE FROM "Measurements" WHERE "Timestamp" < NOW() - INTERVAL '30 days';
EOF
```

---

## 📈 Мониторинг в production

### Что смотреть

```bash
# Статус сервиса
sudo systemctl status strikeball-server

# Логи в реальном времени
sudo journalctl -u strikeball-server -f --output=short-iso

# Использование памяти
top -p $(systemctl show -p MainPID --value strikeball-server)

# Размер БД
du -sh /var/lib/postgresql/strikeballdb  # Для PostgreSQL

# Количество подключений
sudo -u postgres psql -d strikeballdb -c "SELECT count(*) FROM pg_stat_activity;"
```

### Рекомендуемые лимиты

```bash
# Максимум хранить последние 30 дней данных
# Удалять старые записи ежедневно (cron):

# Добавить в crontab:
0 0 * * * sudo -u postgres psql -d strikeballdb -c 'DELETE FROM "Positions" WHERE "Timestamp" < NOW() - INTERVAL '"'"'30 days'"'"';'
```

---

## ✅ Чек-лист перед тестированием с клиентом

- [ ] Сервер запущен и доступен по HTTP
- [ ] Swagger UI открывается (http://server:5000)
- [ ] WebSocket подключается (ws://server:5000/hubs/positioning)
- [ ] БД содержит 4 якоря и 2-4 маяка
- [ ] Симулятор отправляет данные успешно
- [ ] Позиции вычисляются (confidence > 0.5)
- [ ] Клиент может отправить измерение через API
- [ ] Клиент получает WebSocket обновления real-time
- [ ] Истории позиций содержат историческую информацию
- [ ] Логи не содержат критических ошибок

---

## 🎉 Готово!

Сервер полностью готов к тестированию с вашим клиентским приложением.

**Контакты/вопросы:**
- Смотрите полную документацию в `Docs/PROJECT_DOCUMENTATION.md`
- Проверьте API в `Docs/API.md`
- Развертывание подробнее в `LINUX_DEPLOYMENT_STEP_BY_STEP.sh`

**Дата готовности:** 5 марта 2026  
**Версия:** 1.0 MVP ✅
