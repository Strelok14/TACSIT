# T.A.C.I.D. One-Page: боевой ручной прогон

## 0. Заполнить перед стартом

- SERVER_HOST: `<IP_или_домен_сервера>`
- SERVER_PORT: `5001`
- ADMIN_LOGIN: `admin`
- ADMIN_PASSWORD: `<пароль_admin>`

Итоговый URL:

- LAN/VPN без TLS: `http://<IP_или_домен_сервера>:5001`
- HTTPS режим: `https://<IP_или_домен_сервера>:5001`

Как быстро получить все параметры:

PowerShell (Windows):

```powershell
# Ключи
$jwtKey = -join ((48..57 + 65..90 + 97..122) | Get-Random -Count 80 | ForEach-Object {[char]$_})
$mk = New-Object byte[] 32
[System.Security.Cryptography.RandomNumberGenerator]::Fill($mk)
$masterKeyB64 = [Convert]::ToBase64String($mk)

# Пароли
$adminPass = [Convert]::ToBase64String((1..24 | ForEach-Object {Get-Random -Minimum 0 -Maximum 256}))
$observerPass = [Convert]::ToBase64String((1..24 | ForEach-Object {Get-Random -Minimum 0 -Maximum 256}))
$playerPass = [Convert]::ToBase64String((1..24 | ForEach-Object {Get-Random -Minimum 0 -Maximum 256}))

# Остальные параметры
$playerBeaconId = 100
$redisConn = "localhost:6379,abortConnect=false"

# Показать IP сервера
Get-NetIPAddress -AddressFamily IPv4 | Where-Object {$_.IPAddress -notlike '127.*'} | Select-Object IPAddress, InterfaceAlias
```

Linux (bash):

```bash
JWT_KEY=$(openssl rand -base64 64 | tr -d '=+/' | cut -c1-80)
MASTER_KEY_B64=$(openssl rand -base64 32)
ADMIN_PASS=$(openssl rand -base64 24)
OBSERVER_PASS=$(openssl rand -base64 24)
PLAYER_PASS=$(openssl rand -base64 24)
PLAYER_BEACON_ID=100
REDIS_CONN="localhost:6379,abortConnect=false"

hostname -I
```

## 1. Поднять сервер

Windows PowerShell:

```powershell
$env:TACID_JWT_SIGNING_KEY=$jwtKey
$env:TACID_MASTER_KEY_B64=$masterKeyB64
$env:TACID_ADMIN_LOGIN="admin"
$env:TACID_ADMIN_PASSWORD=$adminPass
$env:TACID_OBSERVER_LOGIN="observer"
$env:TACID_OBSERVER_PASSWORD=$observerPass
$env:TACID_PLAYER_LOGIN="player"
$env:TACID_PLAYER_PASSWORD=$playerPass
$env:TACID_PLAYER_BEACON_ID="$playerBeaconId"
$env:REDIS_CONNECTION_STRING=$redisConn

# Для LAN/VPN без TLS (оставляем Production + PostgreSQL)
$env:TACID_ALLOW_INSECURE_HTTP="true"

# В отдельном окне:
dotnet run --project StrikeballServer/Server/StrikeballServer.csproj --urls http://0.0.0.0:5001
```

Linux bash:

```bash
export TACID_JWT_SIGNING_KEY="$JWT_KEY"
export TACID_MASTER_KEY_B64="$MASTER_KEY_B64"
export TACID_ADMIN_LOGIN="admin"
export TACID_ADMIN_PASSWORD="$ADMIN_PASS"
export TACID_OBSERVER_LOGIN="observer"
export TACID_OBSERVER_PASSWORD="$OBSERVER_PASS"
export TACID_PLAYER_LOGIN="player"
export TACID_PLAYER_PASSWORD="$PLAYER_PASS"
export TACID_PLAYER_BEACON_ID="$PLAYER_BEACON_ID"
export REDIS_CONNECTION_STRING="$REDIS_CONN"
export TACID_ALLOW_INSECURE_HTTP="true"

dotnet run --project StrikeballServer/Server/StrikeballServer.csproj --urls http://0.0.0.0:5001
```

## 2. Smoke с ПК (обязательно)

PowerShell:

```powershell
$env:TACID_TEST_LOGIN="admin"
$env:TACID_TEST_PASSWORD="<пароль_admin>"
.\StrikeballServer\scripts\smoke-test.ps1 -Server <IP_или_домен_сервера>:5001
```

Bash:

```bash
export TACID_TEST_LOGIN="admin"
export TACID_TEST_PASSWORD="<пароль_admin>"
bash StrikeballServer/scripts/smoke-test.sh <IP_или_домен_сервера>:5001
```

Критерий успеха:

- есть строка `Smoke test PASSED`

## 3. Тест с телефона (Android)

1. Телефон и сервер в одной сети/VPN, порт 5001 доступен.
2. В приложении вводим сервер: `<IP_или_домен_сервера>` (клиент для IP автоматически выбирает `http://`).
3. Логинимся под `admin`.
4. Проверяем:

- вход успешен;
- данные/экраны загружаются;
- после паузы приложение не вылетает из сессии мгновенно (refresh работает);
- после logout нужен повторный вход.

## 4. Быстрый API-минимум (ручной)

- `POST /api/auth/login`
- `POST /api/auth/refresh`
- `POST /api/auth/logout`
- `GET /api/positions`
- `POST /hubs/positioning/negotiate?negotiateVersion=1`

## 5. Стоп-критерии (если что-то не так)

Останавливаем тест и фиксируем логи, если:

- smoke не проходит;
- телефон не логинится;
- после logout защищенные endpoints продолжают отвечать 200;
- серверные ошибки 500 повторяются.

## 6. Мини-результат теста (шаблон)

Скопировать и заполнить:

```text
Дата/время:
Сервер:
Версия/коммит:
Smoke с ПК: PASS/FAIL
Логин с телефона: PASS/FAIL
Refresh на телефоне: PASS/FAIL
Logout revoke: PASS/FAIL
SignalR negotiate: PASS/FAIL
Итог: GO / NO-GO
Комментарии:
```

## 7. Важно про режимы

- Для LAN/VPN режима используйте `TACID_ALLOW_INSECURE_HTTP=true` и URL `http://...`.
- Для публичного HTTPS уберите `TACID_ALLOW_INSECURE_HTTP` и запускайте с `https://...`.
