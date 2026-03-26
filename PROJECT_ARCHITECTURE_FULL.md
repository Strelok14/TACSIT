# T.A.C.I.D. — Полная архитектура проекта

Этот документ предназначен для доклада, изучения и передачи проекта.
Содержит целостное описание архитектуры, технических решений, протоколов, алгоритмов и operational-процессов.

## 1. Назначение системы

T.A.C.I.D. (Tactical Combat Identification Device) — это система позиционирования игроков в реальном времени для страйкбольного полигона.

Цели системы:
- получать телеметрию расстояний от носимых маяков;
- вычислять координаты игроков по сети якорей;
- передавать позиции на Android-клиент в режиме реального времени;
- обеспечивать криптографическую защиту входящих данных и контроль доступа.

## 2. Высокоуровневая архитектура

Система состоит из 4 основных контуров:

1. Контур телеметрии (маяк -> сервер)
- Маяк формирует пакет измерений и подписывает его HMAC-SHA256.
- Пакет отправляется в `POST /api/telemetry/measurement`.

2. Контур вычислений (сервер)
- Сервер валидирует пакет (time window, replay, signature, rate limit).
- Сервис позиционирования вычисляет координату по данным якорей.
- Результат сохраняется в БД и публикуется в SignalR Hub.

3. Контур клиента (сервер -> Android)
- Android получает JWT и подключается к Hub `/hubs/positioning`.
- Позиции приходят по WebSocket событием `PositionUpdate`.
- При недоступности Hub используется fallback через HTTP polling.

4. Контур эксплуатации (Ops)
- `bootstrap_server.sh` поднимает инфраструктуру сервера.
- `deploy_from_user.sh` выкладывает сборку приложения.
- `smoke-test.sh` проверяет готовность прод-контуров.

## 3. Технологический стек

### 3.1 Сервер
- Язык и платформа: C#, .NET 8
- Web framework: ASP.NET Core
- API: REST Controllers
- Realtime: SignalR
- ORM: Entity Framework Core
- БД:
  - Production: PostgreSQL (Npgsql)
  - Development: SQLite
- Кэш/состояние безопасности: Redis (StackExchange.Redis)
- Аутентификация: JWT Bearer
- Логирование: Console + Debug (в Development)

### 3.2 Клиент
- Платформа: Android (Java)
- HTTP: Retrofit + OkHttp
- Realtime: SignalR Java client
- Карта: osmdroid
- Хранение сессии: EncryptedSharedPreferences

### 3.3 Инфраструктура
- ОС сервера: Linux (Debian/Ubuntu)
- Process manager: systemd (`strikeball-server.service`)
- Reverse proxy/TLS: nginx + certbot (Let's Encrypt)
- Скрипты эксплуатации: bash

## 4. Серверная композиция по слоям

Путь: `Server/`

1. `Program.cs`
- корневой composition root и pipeline конфигурация;
- выбор БД по окружению;
- настройка JWT, CORS, SignalR, middleware и DI;
- применение security-миграции схемы;
- публикация endpoint-ов API и Hub.

2. `Controllers/`
- `AuthController` — login/refresh/logout/me;
- `TelemetryController` — прием телеметрии;
- `PositionsController` — получение позиций;
- `AnchorsController` — CRUD якорей;
- `BeaconsController` — CRUD маяков;
- `SecurityController` — управление ключами маяков.

3. `Middleware/TelemetrySecurityMiddleware.cs`
- выполняется до бизнес-логики телеметрии;
- проверяет криптографическую и временную валидность пакета;
- формирует ClaimsPrincipal для роли player после успешной проверки.

4. `Services/`
- доменные и security-сервисы (позиционирование, фильтрация, replay, denylist, rate limit, key store);
- фильтрация в текущей реализации выполняется EMA-сглаживанием (а не полноценным Kalman), включается флагом `PositioningSettings:KalmanFilterEnabled`;
- hosted-service ротации ключей (`BeaconKeyRotationHostedService`).

5. `Hubs/PositioningHub.cs`
- realtime канал для клиентов;
- role-aware подписки на группы маяков;
- ограничение player к своему `beacon_id`.

6. `Data/ApplicationDbContext.cs`
- EF Core контекст и таблицы домена/безопасности.

## 5. Жизненный цикл запроса телеметрии

Точка входа: `POST /api/telemetry/measurement`

1. Получение raw JSON и десериализация в `MeasurementPacketDto`.
2. Rate limiting по `beaconId + sourceIp`.
3. Проверка timestamp:
- drift относительно server time;
- backlog окно для буферизованных пакетов.
4. Проверка обязательных auth-полей: `sequence`, `signature`.
5. Получение кандидатов ключей маяка из `IBeaconKeyStore`.
6. Канонизация payload (`BuildCanonical`) и проверка HMAC.
7. Replay-protection через `IReplayProtectionService`.
8. Установка claims (`role=player`, `beacon_id`, `key_version`).
9. Передача в контроллер и бизнес-логику.

Это гарантирует, что в доменный слой попадает только валидный и не replay-пакет.

### 5.1 Канонизация payload для HMAC (важный нюанс)

Подпись вычисляется по канонической строке, а не по raw JSON:

`beaconId|sequence|timestamp|anchorId:distance:rssi;...`

Ключевые детали реализации:
- `Distances` сортируется по `AnchorId` перед расчетом подписи;
- `distance` сериализуется через `InvariantCulture` (точка как десятичный разделитель);
- если `rssi` отсутствует, в каноническую строку подставляется `0`;
- сравнение подписи проводится через constant-time (`FixedTimeEquals`).

Следствие для firmware/client:
- любое расхождение в сортировке, форматировании `double` или обработке `null` даст `InvalidSignature`.

## 6. Криптография и безопасность

### 6.1 JWT
- Алгоритм: HMAC-SHA256
- Ключ: `TACID_JWT_SIGNING_KEY`
- Проверяется issuer/audience/lifetime/signature
- Для Hub токен читается из query `access_token`
- На `OnTokenValidated` проверяется JTI denylist

### 6.2 HMAC телеметрии
- Каждому маяку соответствует секретный ключ
- Подпись: `HMACSHA256(canonicalPayload, beaconKey)`
- Сравнение подписи выполняется constant-time
- Поддерживается `keyVersion` + fallback по ключам

### 6.3 Шифрование ключей маяков
- Master key: `TACID_MASTER_KEY_B64` (32 байта)
- Ключи маяков хранятся в БД в зашифрованном виде
- Используется AES-256-GCM

### 6.4 Replay и rate-limit
- Replay: sequence tracking в Redis (или in-memory fallback)
- Rate limit: Redis token-bucket (или in-memory fallback)
- Replay ключ: `tacid:replay:{beaconId}`, TTL 3600 сек.
- Rate-limit ключ: `tacid:rl:{beaconId}:{sourceIp}`, TTL 60 сек.

### 6.5 RBAC
- Роли: `admin`, `observer`, `player`
- Политики авторизации в `Program.cs`
- Ограничение player на свой beacon в SignalR Hub

### 6.6 HTTPS модель
- В production HTTPS обязателен (если не включен LAN/VPN режим)
- `TACID_ALLOW_INSECURE_HTTP=true` только для доверенных сетей

## 7. Данные и модель домена

Основные сущности:
- Anchor — опорная станция с фиксированной координатой
- Beacon — носимый маяк игрока
- Measurement — пакет измерений расстояний
- Position — вычисленная координата игрока
- RefreshToken — refresh lifecycle
- BeaconSecret — защищенное хранение ключей маяков

Хранилища:
- PostgreSQL: фактические данные и состояние домена
- Redis: краткоживущее security-состояние и anti-abuse механизмы

## 8. Realtime подсистема

Hub: `/hubs/positioning`

Ключевые методы:
- `SubscribeToBeacon(int beaconId)`
- `UnsubscribeFromBeacon(int beaconId)`
- `GetServerStatus()`

Публикация:
- Через extension `SendPositionToBeaconGroup(...)`
- Канал сообщения: `PositionUpdate`

Текущий нюанс реализации:
- в `TelemetryController` отправка делается через `Clients.All.SendAsync("PositionUpdate", ...)`;
- group-based delivery уже реализован в Hub, но пока не является основным маршрутом публикации.

Семантика доставки:
- Клиент подписывается на группу конкретного маяка
- Observer/admin могут подписываться на нужные группы
- Player ограничивается своим `beacon_id`

## 9. Android клиент: устройство и поток данных

Путь: `ClientApp/`

Ключевые компоненты:
- `MainActivity` — вход и получение токена
- `MenuActivity` — навигация
- `MapActivity` — карта и realtime позиции
- `MeasurementActivity` — ручная отправка telemetry

Network layer:
- `AuthServiceFactory` — Retrofit, auth interceptor, refresh logic
- `SessionManager` — encrypted persistence токенов
- `PositioningHubClient` — SignalR клиент с reconnect

Режимы обновления карты:
- Основной: WebSocket/SignalR
- Fallback: HTTP polling

## 10. Менеджер конфигурации сервера (ServerManager)

Путь: `ServerManager/`

Назначение:
- интерактивная настройка env-параметров сервера;
- генерация криптографических ключей;
- валидация конфигурации перед запуском.

Структура:
- `Core/CryptoUtils.cs` — генерация/валидация ключей
- `Core/EnvConfig.cs` — парсинг, изменение, сохранение env
- `UI/ConsoleUI.cs` — консольный интерфейс
- `Menus/*` — разделы по предметным зонам

Важные свойства реализации:
- отслеживание несохраненных изменений (dirty-state)
- атомарное сохранение env-файла
- backup env при сохранении
- validate-only режим для CI/скриптов

## 11. Скрипты эксплуатации и деплой

Путь: `scripts/`

1. `bootstrap_server.sh`
- установка .NET, PostgreSQL, Redis, nginx, certbot;
- создание пользователя и каталогов;
- установка service unit;
- генерация базового env (если нет).

2. `deploy_from_user.sh`
- publish приложения;
- rsync в `/opt/strikeball/server`;
- restart `strikeball-server`.

3. `smoke-test.sh`
- проверка login, telemetry, positions, SignalR negotiate, revoke.

Production-пайплайн:
1. bootstrap
2. deploy
3. smoke-test
4. подключение клиента

## 12. Нефункциональные характеристики

### 12.1 Надёжность
- Фоновая ротация ключей
- Global exception handler
- Restart политики через systemd
- Деградация на in-memory при недоступности Redis
- Reconnect на Android-клиенте с fallback на HTTP polling

### 12.2 Производительность
- Основной поток чтения клиентом через WebSocket
- Снижение нагрузки по сравнению с постоянным polling
- Caching/security-состояние в Redis

### 12.3 Масштабируемость
- Горизонтально ограничена stateful аспектами (SignalR + Redis coordination)
- Возможна эволюция в сторону backplane для multi-instance realtime

### 12.4 Поддерживаемость
- Хорошее разбиение на контексты
- Явные security boundary в middleware
- Скриптизированный deployment

## 13. Ключевые инженерные решения и компромиссы

1. Redis fallback в память
- Плюс: сервер не падает при потере Redis
- Минус: теряется персистентность denylist/replay state при рестарте

2. HTTP LAN/VPN режим
- Плюс: работает на закрытых полигонах без PKI
- Минус: нельзя использовать в недоверенной сети

3. JWT + refresh rotation
- Плюс: стандартная и быстрая auth-модель
- Минус: требует аккуратной политики хранения refresh на клиенте

4. SignalR + fallback polling
- Плюс: realtime в норме + живучесть при проблемах websocket
- Минус: fallback увеличивает нагрузку

## 14. Риски и зоны развития

Технические риски:
- некорректная операционная ротация master key;
- запуск production без TLS;
- отсутствие единого мониторинга метрик/алертов.

Рекомендуемые улучшения:
- централизованный observability стек (Prometheus/Grafana);
- preflight-check перед деплоем;
- расширение CI для конфиг-валидации;
- автоматизация rollback-процедуры.

## 15. Заключение

Проект реализует полноценную защищенную систему позиционирования:
- сбор телеметрии от маяков с криптографической проверкой;
- вычисление и хранение позиций;
- realtime доставку данных клиенту;
- эксплуатационный контур для production deployment.

Архитектура соответствует задачам предметной области и имеет хороший баланс:
- между безопасностью и практической применимостью в полевых условиях;
- между realtime-требованиями и операционной устойчивостью;
- между модульностью кода и скоростью сопровождения.

## 16. Runtime pipeline и порядок middleware

Фактический порядок обработки в ASP.NET Core:
1. `UseExceptionHandler`
2. `UseForwardedHeaders`
3. `UseHsts` (prod, если включен HTTPS enforcement)
4. `UseHttpsRedirection` (prod, если включен HTTPS enforcement)
5. Кастомный middleware запрета plain HTTP (для non-loopback)
6. `UseCors("StrictCors")`
7. `UseAuthentication()`
8. `UseMiddleware<TelemetrySecurityMiddleware>()`
9. `UseAuthorization()`
10. `MapControllers()` / `MapHub("/hubs/positioning")`

Почему это критично:
- telemetry-middleware идет после authentication и до authorization;
- middleware может сформировать `ClaimsPrincipal` (`role=player`, `beacon_id`), и запрос затем проходит `[Authorize]` в контроллере;
- JWT denylist проверяется в `OnTokenValidated`, то есть до бизнес-логики.

## 17. Математика позиционирования (least squares) и фильтрация

### 17.1 Линеаризация трилатерации

Для якорей $i=1..N$ и референсного якоря $0$:

$$
2(x_i-x_0)X + 2(y_i-y_0)Y + 2(z_i-z_0)Z = (d_0^2-d_i^2) + (x_i^2-x_0^2) + (y_i^2-y_0^2) + (z_i^2-z_0^2)
$$

В матричном виде:

$$
A\theta=b,\quad \theta=(X,Y,Z)^T,\quad \hat{\theta}=(A^TA)^{-1}A^Tb
$$

Практические условия устойчивости:
- минимум 3 активных якоря;
- геометрия якорей должна быть невырожденной (иначе матрица плохо обусловлена);
- перед расчетом применяется `CalibrationOffset` якорей.

### 17.2 Confidence через RMSE

$$
RMSE = \sqrt{\frac{1}{N}\sum_{i=1}^{N}(\hat{d_i}-d_i)^2}
$$

$$
confidence = clamp(1 - RMSE/5, 0, 1)
$$

### 17.3 Реальная фильтрация трека

Текущая фильтрация реализована как EMA с коэффициентом $\alpha=0.3$:

$$
x_t = \alpha x_{t-1} + (1-\alpha)x_t^{raw}
$$

Аналогично для $y, z$.

Нюанс треков:
- если разрыв между позициями > 5 сек, фильтр не продолжает старый трек, а принимает точку как новую.

## 18. Security-state в Redis: структуры и ключи

1. Replay protection
- `Sorted Set`: `tacid:replay:{beaconId}`
- score/value: `sequence`
- Lua-скрипт атомарно запрещает `sequence <= maxSeen`
- TTL 3600 сек.

2. Telemetry rate limit
- `Hash`: поля `tokens`, `ts`
- ключ `tacid:rl:{beaconId}:{sourceIp}`
- token bucket: `capacity=20`, `refill~15 токенов/сек`
- TTL 60 сек.

3. JWT denylist
- ключ `tacid:jti:deny:{jti}`
- значение-маркер: `"1"`
- TTL равен оставшемуся времени жизни токена.

## 19. Деградация и поведение при отказах

1. Недоступен Redis
- replay/rate-limit/denylist переключаются на in-memory fallback;
- приложение продолжает работу, но теряет кросс-инстанс консистентность security-state.

2. Дубликаты и повторная доставка telemetry
- отсекаются в middleware (replay);
- либо ловятся при сохранении как `DuplicateSequenceInDb`.

3. Аномальная кинематика
- позиция отклоняется, если скорость выше `TelemetrySecurity:MaxSpeedMetersPerSec`.

4. Потеря WebSocket на клиенте
- клиент остается работоспособным через HTTP polling fallback.

## 20. Параметры, которые сильнее всего влияют на поведение системы

- `TACID_JWT_SIGNING_KEY` — подпись access token.
- `TACID_MASTER_KEY_B64` — корневой ключ для шифрования beacon secrets.
- `REDIS_CONNECTION_STRING` — доступность security-state и анти-abuse контуров.
- `TACID_ALLOW_INSECURE_HTTP` — политика TLS в production.
- `TelemetrySecurity:MaxTimestampDriftMs` — допуск drift часов (по умолчанию 5000 мс).
- `TelemetrySecurity:MaxBacklogAgeMs` — допустимая «давность» backlog-пакета (по умолчанию 120000 мс).
- `TelemetrySecurity:MaxSpeedMetersPerSec` — отсечение физически невозможной скорости.

## 21. Известные инженерные нюансы и technical debt

1. Group-based SignalR доставка есть, но основной путь публикации пока `Clients.All`.
2. In-memory fallback улучшает живучесть single-node, но не решает консистентность в кластере.
3. Использование `EnsureCreated` упрощает старт, но менее удобно для зрелого schema lifecycle, чем EF migrations.

## 22. Практические шаги для следующей итерации

1. Перевести broadcast позиций на group-based delivery по beacon.
2. Ввести EF migrations с контролируемой эволюцией схемы.
3. Добавить preflight-конфиг-проверку перед деплоем.
4. Добавить метрики:
- latency `ingest -> calculate -> publish`;
- доля отклоненных пакетов по причинам;
- доля клиентов в fallback-режиме.
5. Для multi-instance режима добавить SignalR backplane и централизованный security-state.

Для доклада этот документ можно использовать как основной технический reference, а детали endpoint-ов и security-параметров брать из:
- `README.md`
- `Docs/SECURITY.md`
- `Docs/DEPLOYMENT_AND_PROJECT_ANALYSIS.md`
