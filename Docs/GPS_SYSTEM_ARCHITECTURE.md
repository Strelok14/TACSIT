# GPS Local Demo: System Architecture

Документ объясняет архитектуру и принцип работы текущей ветки gps-local-demo для читателя, который не знаком с проектом.

## 1. Общая идея

Система собирает два типа данных от Android клиентов:

1. GPS позиции игроков.
2. AI detections (свой/чужой, скелет, метаданные наблюдения).

Сервер принимает эти данные, валидирует безопасность пакетов, сохраняет историю в БД и раздаёт обновления в реальном времени через SignalR и web UI.

## 2. Состав системы

1. Android клиент
- Аутентификация по логину/паролю.
- Получение JWT и персонального HMAC ключа.
- Фоновая отправка GPS.
- Отправка detections из AI экрана.

2. API сервер
- REST endpoints для auth, gps, detections.
- Middleware для проверки подписи, sequence и лимитов.
- Публикация realtime событий в SignalR.

3. PostgreSQL
- Хранение пользователей, истории GPS и detections.

4. Redis
- Replay state (sequence tracking).
- Rate limiting.
- JWT denylist для отозванных токенов.

5. Web Dashboard
- Просмотр текущих позиций.
- Просмотр detections и истории.

## 3. Потоки данных

### 3.1 Login flow

1. Клиент вызывает POST /api/auth/login.
2. Сервер проверяет пароль.
3. Сервер выдаёт:
- JWT
- refresh token
- HMAC key (Base64)
4. Клиент сохраняет данные в EncryptedSharedPreferences.

### 3.2 GPS flow

1. Клиент собирает GPS координаты.
2. Формирует payload и заголовки подписи.
3. Отправляет POST /api/gps.
4. Сервер проводит security checks.
5. Сервер пишет запись в GpsPositions.
6. Сервер публикует событие в SignalR.

### 3.3 Detections flow

1. AI модуль формирует detection payload.
2. Клиент подписывает пакет HMAC.
3. Отправляет POST /api/detections.
4. Сервер валидирует пакет.
5. Сервер пишет запись в DetectedPersons.
6. Сервер публикует событие в SignalR.

## 4. Контур безопасности

Проверки выполняются в строгом порядке:

1. JWT аутентификация.
2. Сверка X-User-Id с user_id в JWT.
3. Проверка timestamp окна.
4. Проверка HMAC подписи:
- canonical = userId|seq|timestamp|payload
- HMAC-SHA256
5. Replay защита по sequence.
6. Rate limiting по типу канала и пользователю.

Эта цепочка защищает от подмены пакета, повтора пакета и flood-атаки.

## 5. Роли и доступ

1. admin
- полный доступ к API и данным

2. observer
- просмотр карты, истории, detections

3. player
- отправка GPS и detections
- доступ к своим данным по политике приложения

## 6. Данные и хранение

### 6.1 Users

Хранит:
- login
- password hash
- role
- displayName
- зашифрованный HMAC ключ

### 6.2 GpsPositions

Хранит:
- userId
- координаты и точность
- timestamp
- sequenceNumber

### 6.3 DetectedPersons

Хранит:
- userId отправителя
- targetUserId (если известен)
- isAlly
- skeletonData JSON
- timestamp и sequenceNumber

## 7. Offline/LAN режим

Система рассчитана на работу без интернета:

1. Все зависимости можно заранее подготовить в offline_deps.
2. setup.ps1/setup.sh разворачивают окружение локально.
3. Сервер работает по HTTP в доверенной LAN.
4. Секреты генерируются и хранятся локально.

## 8. Минимальные метрики для эксплуатации

Ориентиры для полевого сценария:

1. Частота GPS: 1 Гц на клиента.
2. Rate limits:
- GPS: 10 req/sec/user
- Detections: 5 req/sec/user
3. Рекомендуемая нагрузка: 20-50 клиентов на типичном ноутбуке.
4. Визуальная задержка до карты: обычно 1-3 секунды в стабильной LAN.

## 9. Границы и допущения

1. Основной сценарий: одна локальная сеть, стабильный Wi-Fi.
2. HTTP допустим только в доверенном периметре.
3. Для масштабирования выше полевого сценария нужно отдельное нагрузочное тестирование и тюнинг БД/Redis.

## 10. С чего начать новому разработчику

1. Прочитать README.
2. Пройти [GPS_LOCAL_OFFLINE_QUICKSTART.md](GPS_LOCAL_OFFLINE_QUICKSTART.md).
3. Запустить сервер локально.
4. Проверить login, /api/gps/current и web UI.
5. После этого переходить к деталям в коде Server и ClientApp.
