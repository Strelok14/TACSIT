# T.A.C.I.D. GPS Local Demo

Проект T.A.C.I.D. в ветке gps-local-demo это локальная система тактического мониторинга, где телефоны игроков сами определяют GPS-координаты, а клиентский AI-модуль отправляет на сервер события распознавания (свой/чужой). Система рассчитана на работу без интернета в одной LAN/Wi-Fi сети.

## Для чего эта система

1. Видеть на карте текущие позиции участников.
2. Получать и хранить события AI-распознавания людей.
3. Работать офлайн на ноутбуке/мини-ПК без внешних сервисов.
4. Сохранять защищённый канал даже в HTTP-LAN режиме: JWT + HMAC + replay + rate limit.

## Ключевые компоненты

1. Сервер
- ASP.NET Core 8 API и SignalR hub.
- PostgreSQL для истории и сущностей домена.
- Redis для replay-защиты, rate limit и denylist JWT.

2. Android клиент
- Логин по JWT.
- Фоновая отправка GPS (1 Гц).
- Отправка AI detections.
- Подпись каждого пакета HMAC-SHA256.

3. Web интерфейс
- Встроенная страница на сервере.
- Карта текущих позиций.
- События detections и история по пользователю.

## Архитектура (кратко)

1. Пользователь логинится в API и получает:
- access token (JWT)
- refresh token
- персональный HMAC ключ (Base64)

2. Клиент отправляет GPS и detections пакетами:
- JWT в Authorization
- X-User-Id, X-Sequence, X-Timestamp, X-Signature
- Подпись строится по канонической строке:
  userId|seq|timestamp|payload

3. Сервер проверяет пакет по шагам:
- корректный JWT
- совпадение X-User-Id и user_id из JWT
- timestamp окно
- HMAC подпись
- replay (sequence)
- rate limit

4. После валидации сервер:
- сохраняет запись в PostgreSQL
- публикует обновление через SignalR
- отдаёт данные web UI и другим клиентам

Подробный разбор архитектуры: [Docs/GPS_SYSTEM_ARCHITECTURE.md](Docs/GPS_SYSTEM_ARCHITECTURE.md)

## Модель данных

1. Users
- учётные записи
- роль
- зашифрованный HMAC ключ

2. GpsPositions
- userId
- latitude/longitude/altitude/accuracy
- timestamp
- sequenceNumber

3. DetectedPersons
- отправитель (userId)
- targetUserId (опционально)
- isAlly
- skeletonData JSON
- geo-привязка и timestamp

## API (актуально для gps-local-demo)

1. Auth
- POST /api/auth/login
- POST /api/auth/refresh
- POST /api/auth/logout
- GET /api/auth/me

2. GPS
- POST /api/gps
- GET /api/gps/current
- GET /api/gps/history/{userId}

3. Detections
- POST /api/detections
- GET /api/detections/recent

4. Dashboard and realtime
- GET /api/dashboard/config
- GET/WS /hubs/positioning

## Принцип работы в реальном цикле

1. Игрок запускает приложение и входит по логину/паролю.
2. Фоновый сервис клиента раз в секунду шлёт GPS.
3. AI-камера отправляет detections по мере появления наблюдений.
4. Сервер пишет всё в БД и пушит обновления в realtime.
5. Наблюдатель открывает веб-панель и видит карту и события.

## Минимальные метрики (ориентиры демо)

Это практические ориентиры для полевого запуска, не SLA:

1. Частота GPS
- базовая: 1 пакет/сек на клиента

2. Рекомендуемый масштаб
- 20-50 активных клиентов в одной LAN при типичном ноутбуке-сервере

3. Лимиты защиты
- GPS: 10 пакетов/сек на клиента
- Detections: 5 пакетов/сек на клиента

4. Целевое время визуального обновления
- 1-3 секунды до появления события на web UI в нормальной сети

## Офлайн-развёртывание

1. Полный офлайн-гайд:
[Docs/GPS_LOCAL_OFFLINE_QUICKSTART.md](Docs/GPS_LOCAL_OFFLINE_QUICKSTART.md)

2. Короткий полевой чеклист:
[Docs/GPS_FIELD_CHECKLIST.md](Docs/GPS_FIELD_CHECKLIST.md)

3. Структура офлайн-зависимостей:
[offline_deps/README.md](offline_deps/README.md)

## Быстрый запуск

Windows:

```powershell
.\setup.ps1
.\run.ps1
```

Linux:

```bash
chmod +x setup.sh run.sh
./setup.sh
./run.sh
```

## Что проверить после старта

1. API доступен:
- GET http://<server-ip>:5001/api/auth/me возвращает 401 без токена.

2. Логин работает:
- POST /api/auth/login для observer/admin.

3. Web UI доступен:
- http://<server-ip>:5001/

4. Данные идут:
- GET /api/gps/current
- GET /api/detections/recent

## Что считается legacy в этой ветке

UWB-контур сохранён в репозитории для совместимости, но не является основным сценарием gps-local-demo. Основной рабочий путь этой ветки это GPS + AI detections + offline LAN deployment.

## Ссылки по документации

1. Архитектура: [Docs/GPS_SYSTEM_ARCHITECTURE.md](Docs/GPS_SYSTEM_ARCHITECTURE.md)
2. Безопасность: [Docs/SECURITY.md](Docs/SECURITY.md)
3. Офлайн quickstart: [Docs/GPS_LOCAL_OFFLINE_QUICKSTART.md](Docs/GPS_LOCAL_OFFLINE_QUICKSTART.md)
4. Полевой чеклист: [Docs/GPS_FIELD_CHECKLIST.md](Docs/GPS_FIELD_CHECKLIST.md)
