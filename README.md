# T.A.C.I.D. GPS Local Demo

Ветка gps-local-demo переводит демонстрационный сценарий с UWB-маяков на GPS телефонов в локальной сети. UWB-код сохранён в проекте для совместимости и исследований, но основной контур этой ветки использует:

- Android-клиенты с GPS и on-device AI.
- ASP.NET Core 8 сервер с PostgreSQL, Redis, JWT, HMAC-SHA256, replay-защитой и rate limiting.
- Простой web dashboard на встроенном статическом фронтенде.
- Полностью офлайн-установимый набор через offline_deps, setup.sh и setup.ps1.

## Что изменено в этой ветке

- Добавлены сущности Users, GpsPositions и DetectedPersons.
- AuthController переведён на пользователей из БД.
- JWT содержит user_id и role.
- HMAC теперь выдаётся на пользователя и шифруется в БД через AES-256-GCM.
- Добавлены POST /api/gps и POST /api/detections.
- Добавлены GET /api/gps/current, GET /api/gps/history/{userId}, GET /api/detections/recent.
- Встроенный web UI доступен с корня сервера.
- Android после логина запускает foreground GPS service с частотой 1 Гц.

## Почему 1 Гц для GPS

В локальной WiFi-сети для тактической карты 1 Гц даёт достаточно плавное обновление без лишней нагрузки на батарею и без входа в зону постоянных 429 при штатном лимите 10 пакетов/с на пользователя. Более высокий темп легко включить позже конфигом сервиса Android, но для демо-ветки 1 Гц практичнее и устойчивее.

## Контур безопасности

1. Пользователь логинится через /api/auth/login.
2. Сервер выдаёт JWT и индивидуальный HMAC ключ в Base64.
3. Android хранит их в EncryptedSharedPreferences.
4. Каждый пакет на /api/gps и /api/detections подписывается как userId|seq|timestamp|payload.
5. Сервер проверяет JWT, X-User-Id, подпись, timestamp, replay и rate limit.

## Серверные endpoints

- POST /api/auth/login
- POST /api/auth/refresh
- POST /api/auth/logout
- GET /api/auth/me
- POST /api/gps
- GET /api/gps/current
- GET /api/gps/history/{userId}
- POST /api/detections
- GET /api/detections/recent
- GET /api/dashboard/config
- SignalR hub: /hubs/positioning

## Web dashboard

Корень сервера отдаёт встроенную страницу наблюдателя. Она умеет:

- авторизоваться как observer/admin;
- получать текущие GPS-позиции;
- показывать детекции свой/чужой;
- запрашивать историю по выбранному пользователю.

Текущий UI использует офлайн-дружественную координатную подложку без внешних CDN. Если в offline_deps/osm_tiles будут подготовлены локальные XYZ-тайлы, их можно подключить через Map:TileUrlTemplate.

## Офлайн-установка

Структура offline_deps описана в [offline_deps/README.md](offline_deps/README.md).

### Linux

```bash
chmod +x setup.sh run.sh scripts/prepare_offline_deps.sh
./setup.sh
./run.sh
```

### Windows

```powershell
.\setup.ps1
.\run.ps1
```

### Подготовка кэша на онлайн-машине

```bash
./scripts/prepare_offline_deps.sh
```

или

```powershell
.\scripts\prepare_offline_deps.ps1
```

После этого вручную докладываются runtime-бинарники .NET 8, PostgreSQL, Redis и офлайн-тайлы карты.

## Пример локальной конфигурации

См. [Server/appsettings.Local.json](Server/appsettings.Local.json).

Ключевые настройки:

- Security:AllowInsecureHttp=true
- Security:SecretStoreDirectory=App_Data/keys
- TelemetrySecurity:Limits:gps=10/s
- TelemetrySecurity:Limits:detections=5/s
- Map:TileUrlTemplate=/offline_deps/osm_tiles/{z}/{x}/{y}.png

## Android клиент

После логина приложение:

1. сохраняет JWT, refresh token, userId и HMAC ключ;
2. запускает foreground GPS service;
3. AI camera при наличии локальных детекций отправляет их на /api/detections тем же подписанным контуром.

## ServerManager

ServerManager оставлен как локальный конфигуратор офлайн-режима: ключи, LAN HTTP режим, PostgreSQL/Redis и bootstrap-учётки для первого запуска. Основные пользователи после старта уже хранятся в БД сервера.

## Сборка

Сервер:

```bash
dotnet build Server/StrikeballServer.csproj
```

Manager:

```bash
dotnet build ServerManager/ServerManager.csproj
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
