# SECURITY.md — T.A.C.I.D. Security Guide

## Содержание

1. [Переменные окружения (secrets)](#1-переменные-окружения-secrets)
2. [Ключи шифрования маяков и ротация](#2-ключи-шифрования-маяков-и-ротация)
3. [JWT и управление сессиями](#3-jwt-и-управление-сессиями)
4. [Refresh Token — жизненный цикл](#4-refresh-token--жизненный-цикл)
5. [JWT Denylist](#5-jwt-denylist)
6. [HMAC-аутентификация маяков](#6-hmac-аутентификация-маяков)
7. [HTTPS / TLS](#7-https--tls)
8. [Redis и персистентность denylist](#8-redis-и-персистентность-denylist)
9. [Роли и разграничение доступа (RBAC)](#9-роли-и-разграничение-доступа-rbac)
10. [Рекомендации по хранению секретов в production](#10-рекомендации-по-хранению-секретов-в-production)
11. [Security checklist перед деплоем](#11-security-checklist-перед-деплоем)

---

## 1. Переменные окружения (secrets)

Все секреты передаются **исключительно через переменные окружения** (не через `appsettings.json`).

| Переменная | Описание | Требования |
|---|---|---|
| `TACID_JWT_SIGNING_KEY` | Ключ подписи JWT (HMAC-SHA256) | ≥ 64 символа, случайная строка |
| `TACID_MASTER_KEY_B64` | Мастер-ключ AES-256-GCM для шифрования ключей маяков | Ровно 32 байта, Base64, случайный |
| `TACID_ADMIN_LOGIN` | Логин администратора | Непустая строка |
| `TACID_ADMIN_PASSWORD` | Пароль администратора | ≥ 16 символов, high entropy |
| `TACID_OBSERVER_LOGIN` | Логин наблюдателя | Непустая строка |
| `TACID_OBSERVER_PASSWORD` | Пароль наблюдателя | ≥ 16 символов |
| `TACID_PLAYER_LOGIN` | Логин оператора маяка | Непустая строка |
| `TACID_PLAYER_PASSWORD` | Пароль оператора маяка | ≥ 16 символов |
| `TACID_PLAYER_BEACON_ID` | BeaconId для роли player в JWT | Положительное целое |
| `ConnectionStrings__PostgreSQL` | Строка подключения к БД | `Host=…;Database=…;Username=…;Password=…` |
| `REDIS_CONNECTION_STRING` | Адрес Redis (для denylist и rate limit) | `hostname:6379,password=…,ssl=true` |
| `Redis__ConnectionString` | Альтернативная .NET нотация для Redis | Эквивалент `REDIS_CONNECTION_STRING` |

### Генерация безопасных значений

```bash
# TACID_JWT_SIGNING_KEY — 64 случайных символа
openssl rand -base64 48 | tr -d '=/+' | head -c 64

# TACID_MASTER_KEY_B64 — 32 байта в Base64
openssl rand -base64 32

# Пароль пользователя
openssl rand -base64 24
```

> **Важно:** Никогда не коммитьте секреты в git. Добавьте `.env` в `.gitignore`.

---

## 2. Ключи шифрования маяков и ротация

Каждый маяк имеет собственный симметричный ключ (32 байта), хранящийся в БД в зашифрованном виде (AES-256-GCM с мастер-ключом `TACID_MASTER_KEY_B64`).

### Структура хранилища (`BeaconSecrets`)

| Поле | Содержимое |
|---|---|
| `Nonce` | 12 байт (AES-GCM nonce), уникален для каждого ключа |
| `Ciphertext` | Зашифрованный ключ маяка |
| `Tag` | 16 байт (тег аутентичности AES-GCM) |
| `KeyVersion` | Версия ключа (для поддержки ротации) |

### Ротация ключа маяка

```http
POST /api/security/beacons/{id}/rotate
Authorization: Bearer <admin_token>
```

После ротации маяк **должен** немедленно получить новый ключ через безопасный канал (прошивка STM32 / BLE provisioning). Старая версия ключа хранится для обработки in-flight пакетов.

### Ротация мастер-ключа

В текущей версии API автоматическая ротация мастер-ключа через endpoint не реализована.

Текущий безопасный процесс:

1. Остановить запись телеметрии (maintenance mode).
2. Сгенерировать новый мастер-ключ: `openssl rand -base64 32`.
3. Выполнить служебную процедуру ре-шифрования `BeaconSecrets` (админ-скрипт/утилита в контуре эксплуатации).
4. Обновить `TACID_MASTER_KEY_B64`.
5. Перезапустить приложение и проверить прием телеметрии smoke-тестом.

---

## 3. JWT и управление сессиями

### Access Token

- Алгоритм: `HS256` (HMAC-SHA256)
- Время жизни: **30 минут** (настраивается `Jwt:AccessTokenMinutes` в `appsettings.json`)
- Claims: `sub` (userId), `role`, `jti` (UUID для denylist)
- Ключ подписи: `TACID_JWT_SIGNING_KEY` (≥ 64 символа)

### Эндпоинты аутентификации

| Метод | Путь | Описание |
|---|---|---|
| `POST` | `/api/auth/login` (совместимо с `/auth/login`) | Логин → возвращает `token` + `refreshToken` |
| `POST` | `/api/auth/refresh` (совместимо с `/auth/refresh`) | Ротация refresh token → новая пара токенов |
| `POST` | `/api/auth/logout` (совместимо с `/auth/logout`) | Отзыв токена (access → denylist, refresh → revoked в БД) |
| `GET`  | `/api/auth/me` (совместимо с `/auth/me`) | Информация о текущем пользователе |

### Пример ответа `/auth/login`

```json
{
  "success": true,
  "token": "eyJ...",
  "refreshToken": "a3f9c...",
  "refreshExpiresAtUtc": "2025-09-01T12:00:00Z",
  "role": "Admin"
}
```

---

## 4. Refresh Token — жизненный цикл

```
[Login]
   └─► Access Token (30 мин)  +  Refresh Token (30 дней)
          │                              │
          │ [истёк]                      │ [однократное использование]
          └──────────────► [POST /auth/refresh]
                                  │
                                  └─► Старый refresh помечается IsRevoked=true
                                      Новый Access Token + новый Refresh Token
          │
          │ [POST /auth/logout]
          └─► JTI добавляется в denylist (Redis/in-memory)
              Refresh token помечается IsRevoked=true в БД
```

### Свойства refresh token

- Хранится в БД (таблица `RefreshTokens`)
- 64 байта из `RandomNumberGenerator` → hex-строка (128 символов)
- Индекс уникальности по полю `Token`
- `IsRevoked = true` после использования или logout (single-use rotation)
- `ExpiryUtc` проверяется при каждом запросе `/auth/refresh`

### Хранение на клиенте (Android)

Refresh token должен храниться в `EncryptedSharedPreferences` (Android Jetpack Security), **не** в обычных SharedPreferences или файлах. Access token хранится только в памяти.

---

## 5. JWT Denylist

Реализован через `IJwtDenylistService` / `RedisJwtDenylistService`.

### Механизм

1. `POST /api/auth/logout` → JTI текущего access token записывается в Redis: `SET tacid:jti:deny:{jti} 1 EX {remaining_ttl}`
2. При каждом запросе в `OnTokenValidated` (JWT middleware) проверяется: `denylist.IsDeniedAsync(jti)`
3. Если JTI в запрещённом списке → `context.Fail("Token has been revoked")` → `401 Unauthorized`

### Fallback без Redis

Если Redis недоступен, автоматически используется `ConcurrentDictionary<string, DateTime>` в памяти. **Недостаток:** при перезапуске сервера denylist очищается. Для production **обязательно** используйте Redis.

### TTL denylist записи

TTL = время до истечения JTI access token. Записи в Redis не накапливаются бесконечно — автоматически очищаются при истечении токена.

---

## 6. HMAC-аутентификация маяков

Каждый пакет телеметрии подписывается маяком (STM32) с использованием HMAC-SHA256.

### Формат подписи

Canonical строка (для вычисления подписи):
```
{beaconId}|{seq}|{timestamp_unix}|{anchorId}:{distance}:{rssi};{anchorId2}:{distance2}:{rssi2}
```
Якоря сортируются по `anchorId` в порядке возрастания.

### Заголовки запроса

| Заголовок | Значение |
|---|---|
| `X-Beacon-Id` | ID маяка (uint32) |
| `X-Beacon-Sig` | Base64(HMAC-SHA256(canonical, beacon_key)) |
| `X-Beacon-Ts` | Unix timestamp (секунды) |
| `X-Key-Version` | Версия ключа (uint32) |

### Защита от replay-атак

- Максимальный разброс временной метки: настраивается `TelemetrySecurity:MaxTimestampDriftMs` (по умолчанию 5000 мс)
- Счётчик пакетов (`seq`) должен монотонно возрастать; повторный или более низкий `seq` → `409 Conflict`
- Состояние `seq` хранится в Redis (ключ `tacid:seq:{beaconId}`)

---

## 7. HTTPS / TLS

### Обязательность

В production **все** соединения должны использовать TLS 1.2+. HTTP → HTTPS редирект включён автоматически при `ASPNETCORE_ENVIRONMENT=Production`.

### Nginx (рекомендуемый reverse proxy)

Конфигурационный шаблон: `scripts/nginx/tacid.conf`

```nginx
ssl_protocols TLSv1.2 TLSv1.3;
ssl_ciphers ECDHE-ECDSA-AES256-GCM-SHA384:ECDHE-RSA-AES256-GCM-SHA384:...;
ssl_prefer_server_ciphers on;
add_header Strict-Transport-Security "max-age=63072000; includeSubdomains" always;
```

### Сертификаты

- Development: self-signed (создаются `dotnet dev-certs https`)
- Production: Let's Encrypt (certbot) или корпоративный CA
- Wildcard-сертификат не рекомендуется; используйте отдельный сертификат для каждого хоста

---

## 8. Redis и персистентность denylist

| Режим | JWT denylist | Rate limit state | Replay protection (seq) |
|---|---|---|---|
| **Redis** (рекомендуется) | Персистентен между перезапусками | Персистентен | Персистентен |
| **In-memory** (fallback) | Очищается при рестарте | Очищается при рестарте | Очищается при рестарте |

### Требования к Redis

- Redis 6.0+, AOF persistence (`appendonly yes`) для надёжности
- TLS соединение (`REDIS_CONNECTION_STRING=…,ssl=true`)
- Пароль обязателен (`requirepass` в redis.conf)
- Отдельная база данных (не 0) для изоляции: `…,defaultDatabase=1`

---

## 9. Роли и разграничение доступа (RBAC)

| Роль | Доступ |
|---|---|
| `Admin` | Полный доступ ко всем эндпоинтам, включая управление ключами и ротацию |
| `Player` | Чтение позиций (`GET /api/positions`), отправка телеметрии |
| `Observer` | Только чтение: позиции, якоря, маяки |

Роль записывается в JWT claim `role`. Проверяется через `[Authorize(Roles = "Admin,...")]` на контроллерах.

---

## 10. Рекомендации по хранению секретов в production

### HashiCorp Vault

```bash
vault kv put secret/tacid \
  jwt_signing_key="$(openssl rand -base64 48 | tr -d '=/+' | head -c 64)" \
  master_key_b64="$(openssl rand -base64 32)"
```

Используйте [Vault Agent](https://developer.hashicorp.com/vault/docs/agent-and-proxy/agent) или ASP.NET Core Vault provider для автоматической загрузки секретов.

### AWS Secrets Manager / Parameter Store

```bash
aws secretsmanager create-secret \
  --name "tacid/prod" \
  --secret-string '{"TACID_JWT_SIGNING_KEY":"…","TACID_MASTER_KEY_B64":"…"}'
```

### Kubernetes Secrets

```yaml
apiVersion: v1
kind: Secret
metadata:
  name: tacid-secrets
type: Opaque
stringData:
  TACID_JWT_SIGNING_KEY: "<генерированная строка>"
  TACID_MASTER_KEY_B64: "<base64 ключ>"
```

Монтируйте как env vars, **не** как файлы в volume (чтобы избежать утечки через /proc).

---

## 11. Security checklist перед деплоем

- [ ] `TACID_JWT_SIGNING_KEY` задан, длина ≥ 64 символов, не дефолтный
- [ ] `TACID_MASTER_KEY_B64` задан, ровно 32 байта, сгенерирован `openssl rand -base64 32`
- [ ] Все пароли пользователей соответствуют политике (≥ 16 символов, высокая энтропия)
- [ ] `ConnectionStrings__PostgreSQL` указывает на production БД с SSL
- [ ] `REDIS_CONNECTION_STRING` указывает на Redis с TLS и паролем
- [ ] `ASPNETCORE_ENVIRONMENT=Production` установлен (включает HTTPS enforcement, RequireHttpsMetadata)
- [ ] nginx настроен с TLS 1.2+, HSTS, актуальным сертификатом
- [ ] Сертификаты Let's Encrypt / корпоративного CA не самоподписанные
- [ ] Redis настроен с `appendonly yes` (AOF persistence)
- [ ] Процедура ротации мастер-ключа (off-line) задокументирована и протестирована
- [ ] Ключи маяков провизиованы через безопасный канал (не по-открытому HTTP)
- [ ] Логи не содержат токенов, паролей или ключей (проверьте `appsettings.json` log levels)
- [ ] Проведены интеграционные тесты: `dotnet test Tests.Integration`
- [ ] Smoke-тест прошёл: `bash scripts/smoke-test.sh <server>`
- [ ] Отключены неиспользуемые эндпоинты (dev-only эндпоинты под `if (IsDevelopment())`)
- [ ] Firewall: только порты 443 (nginx) и 6379 (Redis, только с localhost/VPC) открыты
