# T.A.C.I.D. GPS Local Demo

Ветка gps-local-demo переводит демонстрационный сценарий с UWB-маяков на GPS телефонов в локальной сети. UWB-код сохранён в проекте для совместимости и исследований, но основной контур этой ветки использует:

- Android-клиенты с GPS и on-device AI.
- ASP.NET Core 8 сервер с PostgreSQL, Redis, JWT, HMAC-SHA256, replay-защитой и rate limiting.
- Простой web dashboard на встроенном статическом фронтенде.
- Полностью офлайн-установимый набор через offline_deps, setup.sh и setup.ps1.

Краткая пошаговая инструкция по офлайн-развёртыванию для этой ветки:
[Docs/GPS_LOCAL_OFFLINE_QUICKSTART.md](Docs/GPS_LOCAL_OFFLINE_QUICKSTART.md)

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

## Быстрая проверка после запуска

1. Проверить API без токена:
  - `GET http://<server-ip>:5001/api/auth/me` → `401`.
2. Залогиниться observer/admin через `POST /api/auth/login`.
3. Открыть встроенный web UI:
  - `http://<server-ip>:5001/`
4. Проверить поступление данных:
  - `GET /api/gps/current`
  - `GET /api/detections/recent`

## Актуальные API для GPS-ветки

### Auth

- `POST /api/auth/login`
- `POST /api/auth/refresh`
- `POST /api/auth/logout`
- `GET /api/auth/me`

### GPS и история

- `POST /api/gps`
- `GET /api/gps/current`
- `GET /api/gps/history/{userId}`

### Detections

- `POST /api/detections`
- `GET /api/detections/recent`

### Real-time

- `GET/WS /hubs/positioning`

## Документация

- Короткий офлайн-гайд: [Docs/GPS_LOCAL_OFFLINE_QUICKSTART.md](Docs/GPS_LOCAL_OFFLINE_QUICKSTART.md)
- Безопасность: [Docs/SECURITY.md](Docs/SECURITY.md)
