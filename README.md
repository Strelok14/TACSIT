# T.A.C.I.D. GPS Local Demo

Актуальная ветка: `gps-local-demo`.

Проект переведён на GPS-позиционирование телефонов и локальный (offline/LAN) режим работы.

## Текущее состояние

1. Сервер: ASP.NET Core 8 + PostgreSQL + Redis.
2. Клиент: Android (GPS + AI detections + JWT/HMAC).
3. Безопасность канала: JWT, HMAC-SHA256, replay (timestamp+sequence), rate limit.
4. Web UI: встроенная страница карты и текущих событий на `http://<server>:5001/`.
5. Offline deployment: поддерживается через `offline_deps`, `setup.ps1`, `setup.sh`.

## Что реализовано в GPS-ветке

1. Новые сущности: `Users`, `GpsPositions`, `DetectedPersons`.
2. API:
   - `POST /api/gps`
   - `GET /api/gps/current`
   - `GET /api/gps/history/{userId}`
   - `POST /api/detections`
   - `GET /api/detections/recent`
3. Auth:
   - `POST /api/auth/login`
   - `POST /api/auth/refresh`
   - `POST /api/auth/logout`
   - `GET /api/auth/me`
4. SignalR hub: `/hubs/positioning`.

## Офлайн-развёртывание

1. Полный гид: [Docs/GPS_LOCAL_OFFLINE_QUICKSTART.md](Docs/GPS_LOCAL_OFFLINE_QUICKSTART.md)
2. Короткий боевой чеклист: [Docs/GPS_FIELD_CHECKLIST.md](Docs/GPS_FIELD_CHECKLIST.md)
3. Структура offline пакетов: [offline_deps/README.md](offline_deps/README.md)

## Быстрый запуск

### Windows

```powershell
.\setup.ps1
.\run.ps1
```

### Linux

```bash
chmod +x setup.sh run.sh
./setup.sh
./run.sh
```

## Проверка после старта

1. `GET http://<server-ip>:5001/api/auth/me` → `401` без токена.
2. Логин observer/admin через `POST /api/auth/login`.
3. Открыть `http://<server-ip>:5001/`.
4. Проверить данные в:
   - `GET /api/gps/current`
   - `GET /api/detections/recent`

## Сборка

```bash
dotnet build Server/StrikeballServer.csproj
dotnet build ServerManager/ServerManager.csproj
```

## Примечания

1. Частота GPS по умолчанию: 1 Гц (баланс плавности и устойчивости к rate limit).
2. UWB/legacy код сохранён в репозитории, но в GPS-ветке не используется по умолчанию.
3. Детали безопасности: [Docs/SECURITY.md](Docs/SECURITY.md)
