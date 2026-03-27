# T.A.C.I.D. — Tactical Combat Identification Device

Система позиционирования игроков в реальном времени на страйкбольном полигоне.
UWB-маяки на игроках измеряют расстояния до стационарных якорей, сервер вычисляет
координаты методом трилатерации и транслирует их Android КПК через SignalR.

---

## Архитектура

```
[Маяк STM32F405 + DWM3000]
       │  HMAC-SHA256 пакет
       │  (beacon_id, distances[], timestamp, seq, signature)
       ▼ LTE / Wi-Fi / LoRa
[Сервер ASP.NET Core 8]
   ├── TelemetrySecurityMiddleware
   │       rate limit → timestamp check → HMAC verify → replay check
   ├── PositioningService   — трилатерация + фильтр Калмана
   ├── PostgreSQL           — хранение измерений, позиций, ключей маяков
   ├── Redis                — replay-защита, rate limit, JWT denylist
   └── SignalR Hub /hubs/positioning
               │  JSON позиции всех игроков
               ▼ WebSocket (ws:// или wss://)
[Android КПК (Java)]
   ├── Retrofit + JWT auth  — POST /api/auth/login → access + refresh token
   ├── SignalR клиент       — подписка на обновления позиций
   └── osmdroid карта       — отображение игроков в реальном времени
```

---

## Взаимодействие компонентов

### Маяк → Сервер (телеметрия)

Маяк отправляет `POST /api/telemetry/measurement` с JSON-пакетом:

```json
{
  "beaconId": 1,
  "distances": [
    {"anchorId": 1, "distance": 5.21, "rssi": -45},
    {"anchorId": 2, "distance": 7.30, "rssi": -48},
    {"anchorId": 3, "distance": 4.83, "rssi": -43}
  ],
  "timestamp": 1711450012000,
  "batteryLevel": 87,
  "sequence": 1042,
  "keyVersion": 1,
  "signature": "<Base64 HMAC-SHA256>"
}
```

Сервер проверяет (в порядке):
1. Rate limit по `beaconId` + IP (Redis)
2. Временна́я метка — отклонение ≤ 5 с, или пакет из буфера ≤ 120 с
3. HMAC-SHA256 по ключу маяка из БД (AES-256-GCM)
4. Replay protection — `sequence` должен расти (Redis)
5. Физические ограничения — расстояния 0.01–200 м, скорость ≤ 10 м/с

При успехе — трилатерация → фильтр Калмана → запись в PostgreSQL →
`PositioningHub.Clients.All.SendAsync("PositionUpdate", ...)`.

### Сервер → Android КПК (SignalR)

Клиент подключается к `ws[s]://<host>:5001/hubs/positioning?access_token=<JWT>`.
После подключения сервер рассылает событие `PositionUpdate` при каждом обновлении позиции.

Структура события:
```json
{
  "positions": [
    {
      "beaconId": 1, "beaconName": "Иванов",
      "x": 15.2, "y": 22.7, "z": 1.5,
      "confidence": 0.94, "method": "TWR",
      "timestamp": "2026-03-26T10:15:00Z",
      "anchorsUsed": 3
    }
  ]
}
```

### Android КПК — аутентификация

```
POST /api/auth/login   { "login": "observer", "password": "..." }
  → { "token": "<JWT>", "refreshToken": "...", "role": "observer", "expiresAtUtc": "..." }

POST /api/auth/refresh { "refreshToken": "..." }
  → новая пара токенов (single-use rotation)

POST /api/auth/logout   — JTI попадает в denylist Redis
```

JWT-токен прикладывается к каждому запросу:
`Authorization: Bearer <token>`

---

## Роли доступа

| Роль       | Что может                                                  |
|------------|------------------------------------------------------------|
| `admin`    | Всё: управление якорями, маяками, ключами, чтение позиций  |
| `observer` | Подключение к SignalR, чтение позиций (только GET)         |
| `player`   | Отправка телеметрии (`POST /api/telemetry/measurement`)    |

Учётные данные для каждой роли задаются через ENV-переменные (см. ниже).

---

## Настройка и первый запуск

### Шаг 1 — Установить зависимости

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Redis (`sudo apt install redis-server` на Linux, или Docker)
- PostgreSQL (Production) — для разработки достаточно SQLite

### Шаг 2 — Настроить конфигурацию

**Рекомендуется: tacid-manager** (интерактивная консоль с подсказками)

```bash
# Запуск менеджера конфигурации
dotnet run --project StrikeballServer/ServerManager/ServerManager.csproj

# или на Linux с существующим env-файлом:
sudo dotnet tacid-manager.dll --env-file /etc/strikeball/environment
```

Менеджер генерирует ключи через CSPRNG, проверяет корректность конфигурации
и показывает предупреждения перед сохранением.

**Вручную** — задать ENV-переменные (минимально необходимые):

```bash
# Ключи (генерируются один раз)
export TACID_JWT_SIGNING_KEY=$(openssl rand -base64 48 | tr -d '=+/')
export TACID_MASTER_KEY_B64=$(openssl rand -base64 32)

# Учётные данные
export TACID_ADMIN_LOGIN="admin"
export TACID_ADMIN_PASSWORD=$(openssl rand -base64 18)
export TACID_OBSERVER_LOGIN="observer"
export TACID_OBSERVER_PASSWORD=$(openssl rand -base64 18)
export TACID_PLAYER_LOGIN="player"
export TACID_PLAYER_PASSWORD=$(openssl rand -base64 18)
export TACID_PLAYER_BEACON_ID="1"

# Инфраструктура
export Redis__ConnectionString="localhost:6379,abortConnect=false"
# Production: также задать ConnectionStrings__PostgreSQL
```

PowerShell (Windows):

```powershell
$mk = New-Object byte[] 32
[System.Security.Cryptography.RandomNumberGenerator]::Fill($mk)

$env:TACID_JWT_SIGNING_KEY = -join ((48..57+65..90+97..122) | Get-Random -Count 64 | % {[char]$_})
$env:TACID_MASTER_KEY_B64  = [Convert]::ToBase64String($mk)
$env:TACID_ADMIN_LOGIN     = "admin"
$env:TACID_ADMIN_PASSWORD  = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(18))
$env:TACID_OBSERVER_LOGIN  = "observer"
$env:TACID_OBSERVER_PASSWORD = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(18))
$env:TACID_PLAYER_LOGIN    = "player"
$env:TACID_PLAYER_PASSWORD = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(18))
$env:TACID_PLAYER_BEACON_ID = "1"
$env:Redis__ConnectionString = "localhost:6379,abortConnect=false"
```

### Шаг 3 — Выбрать режим подключения

| Режим | Когда использовать | TACID_ALLOW_INSECURE_HTTP | ASPNETCORE_URLS |
|---|---|---|---|
| LAN / WireGuard VPN | Полигон без интернета, одна сеть | `true` | `http://0.0.0.0:5001` |
| Публичный IP (тест) | Клиент подключается напрямую | `true` | `http://0.0.0.0:5001` |
| Production HTTPS | nginx + TLS, интернет | `false` | `http://127.0.0.1:5001` (*) |

(*) При HTTPS-режиме nginx слушает 443 и проксирует на localhost:5001.

### Шаг 4 — Запустить сервер

```bash
# LAN / VPN (PostgreSQL остаётся, TLS отключён)
ASPNETCORE_ENVIRONMENT=Production \
ASPNETCORE_URLS="http://0.0.0.0:5001" \
TACID_ALLOW_INSECURE_HTTP=true \
dotnet run --project StrikeballServer/Server/StrikeballServer.csproj

# HTTPS (reverse proxy настроен)
ASPNETCORE_ENVIRONMENT=Production \
ASPNETCORE_URLS="http://127.0.0.1:5001" \
dotnet run --project StrikeballServer/Server/StrikeballServer.csproj

# Разработка (SQLite, Swagger UI на /)
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="http://localhost:5001" \
TACID_ALLOW_INSECURE_HTTP=true \
dotnet run --project StrikeballServer/Server/StrikeballServer.csproj
```

Сервер готов когда в логе появится:
```
Strikeball Positioning Server started [Production]
SignalR Hub endpoint: /hubs/positioning
HTTPS enforcement: disabled (LAN/VPN mode)
```

---

## Тестовый запуск

### Проверка сервера (curl / браузер)

```bash
# Должен вернуть 401 (сервер работает, но токен не передан)
curl -i http://localhost:5001/api/auth/me

# Логин — получить токен
curl -s -X POST http://localhost:5001/api/auth/login \
  -H "Content-Type: application/json" \
  -d "{\"login\":\"$TACID_ADMIN_LOGIN\",\"password\":\"$TACID_ADMIN_PASSWORD\"}" | jq .

# Запрос с токеном
curl -H "Authorization: Bearer <token>" http://localhost:5001/api/auth/me
```

### Автоматический smoke-тест

Проверяет: login, HMAC-телеметрию, SignalR negotiate, logout/denylist.

```bash
# Linux / macOS
export TACID_TEST_LOGIN="$TACID_ADMIN_LOGIN"
export TACID_TEST_PASSWORD="$TACID_ADMIN_PASSWORD"
bash StrikeballServer/scripts/smoke-test.sh localhost:5001

# Успех: последняя строка — "Smoke test PASSED"
```

```powershell
# Windows PowerShell
$env:TACID_TEST_LOGIN    = $env:TACID_ADMIN_LOGIN
$env:TACID_TEST_PASSWORD = $env:TACID_ADMIN_PASSWORD
.\StrikeballServer\scripts\smoke-test.ps1 -Server localhost:5001
```

### Симулятор маяков

Отправляет HMAC-подписанные пакеты телеметрии от имени тестового маяка:

```bash
# Запустить сервер (окно 1), затем симулятор (окно 2):
dotnet run --project StrikeballServer/Tests/Tests.csproj
```

Симулятор создаёт маяк ID=1, движущийся по диагонали полигона,
и отправляет 20 пакетов каждые 100 мс. В логах сервера появятся строки
`PositionUpdate` с координатами.

### Тест с Android телефона

1. Убедитесь что телефон и сервер в одной сети (Wi-Fi/VPN).
2. Запустить сервер с `TACID_ALLOW_INSECURE_HTTP=true` (LAN-профиль).
3. Узнать IP сервера:
   ```bash
   ip addr show | grep "inet " | grep -v 127
   # Windows: ipconfig | findstr "IPv4"
   ```
4. В приложении ввести адрес: `192.168.x.x` (без http:// — клиент подставит сам).
   - IP-адрес → клиент использует `http://`
   - Домен (.example.com) → клиент использует `https://`
5. Войти с логином `observer` / `player`.
6. Проверить: карта открылась, маяки отображаются.

Если браузер с телефона открывает `http://<IP>:5001/api/auth/me` и возвращает `401` —
сервер доступен, можно подключать приложение.

---

## API — краткий справочник

Порт по умолчанию: **5001**. В Development Swagger UI доступен на `/`.

### Аутентификация

| Метод | Путь | Роль | Описание |
|---|---|---|---|
| POST | `/api/auth/login` | — | Получить access + refresh токен |
| POST | `/api/auth/refresh` | — | Обновить токен (single-use) |
| POST | `/api/auth/logout` | любая | Отозвать токен (Redis denylist) |
| GET  | `/api/auth/me` | любая | Информация о текущем пользователе |

### Телеметрия и позиции

| Метод | Путь | Роль | Описание |
|---|---|---|---|
| POST | `/api/telemetry/measurement` | player | Пакет измерений маяка (с HMAC) |
| GET  | `/api/positions` | observer | Текущие позиции всех маяков |
| GET  | `/api/positions/{beaconId}` | observer | Позиция конкретного маяка |
| GET  | `/api/positions/history/{beaconId}` | observer | История позиций |

### Якоря и маяки (только admin)

| Метод | Путь | Описание |
|---|---|---|
| GET/POST | `/api/anchors` | Список / добавить якорь |
| PUT/DELETE | `/api/anchors/{id}` | Обновить / удалить якорь |
| GET/POST | `/api/beacons` | Список / зарегистрировать маяк |
| PUT/DELETE | `/api/beacons/{id}` | Обновить / удалить маяк |

### Управление ключами маяков (только admin)

| Метод | Путь | Описание |
|---|---|---|
| POST | `/api/security/beacons/{id}/key` | Загрузить HMAC-ключ маяка |
| POST | `/api/security/beacons/{id}/rotate` | Ротировать ключ (grace period) |

Пример загрузки ключа:
```bash
# Сгенерировать ключ маяка:
KEY=$(openssl rand -base64 32)

# Загрузить в БД:
curl -X POST http://localhost:5001/api/security/beacons/1/key \
  -H "Authorization: Bearer <admin-token>" \
  -H "Content-Type: application/json" \
  -d "{\"keyBase64\":\"$KEY\",\"keyVersion\":1}"
```

### SignalR Hub

```
Подключение: ws[s]://<host>:5001/hubs/positioning?access_token=<JWT>
Роли:        observer, player, admin
События:     PositionUpdate  →  AllPositionsDto  (список всех позиций)
```

---

## Production-развёртывание

Готовые скрипты в `scripts/`:

| Скрипт | Назначение |
|---|---|
| `deploy.sh` / `scripts/deploy_to_server.sh` | Сборка + копирование на сервер |
| `scripts/setup_tls_nginx.sh` | Установка nginx + Let's Encrypt |
| `scripts/smoke-test.sh` | Проверка после деплоя |
| `strikeball-server.service` | systemd unit для автозапуска |

ENV-секреты на Linux хранятся в `/etc/strikeball/environment`
(права `640`, владелец `root:strikeball`).

Детали безопасности, ротации ключей, JWT denylist и RBAC — см. [Docs/SECURITY.md](Docs/SECURITY.md).

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

## 🤖 ServerAI MVP

В репозиторий добавлен отдельный модуль `ServerAI/` для AI-детекции людей по WebRTC видеопотоку.

Что входит в MVP:

- `aiohttp` WebSocket сигналинг на `ws://<server>:8080/ws`
- `aiortc` для приема WebRTC video track и DataChannel
- `ultralytics` + `torch` (CPU) для YOLO-инференса
- отправка JSON с детекциями `person` обратно в Android-клиент через DataChannel

Быстрый запуск:

```bash
cd ServerAI
python -m venv .venv
. .venv/bin/activate
pip install -r requirements.txt
python server.py
```

Android-клиент в `ClientApp/` теперь содержит отдельный AI экран для локального preview, WebRTC uplink и отрисовки bbox поверх видео.

Краткая инструкция для ручного теста с ПК и телефона: `Docs/QUICK_MANUAL_TEST.md`

One-page версия для боевого прогона: `Docs/QUICK_MANUAL_TEST_ONEPAGE.md`

## 🐧 Развертывание на Linux

См. инструкции в `Docs/PROJECT_DOCUMENTATION.md`, раздел "Развертывание".

Для TLS через Nginx используйте:

```bash
sudo bash scripts/setup_tls_nginx.sh your-domain.example
```
