# T.A.C.I.D. Краткая инструкция: ручной тест с ПК и телефона

## 1. Подготовка сервера

1. Установите .NET 8 SDK и Redis.
2. В папке проекта задайте переменные окружения.

Как получить значения параметров:

PowerShell (Windows):

```powershell
# 1) JWT signing key (64+ символов)
$jwtKey = -join ((48..57 + 65..90 + 97..122) | Get-Random -Count 80 | ForEach-Object {[char]$_})

# 2) Master key (ровно 32 байта в Base64)
$mk = New-Object byte[] 32
[System.Security.Cryptography.RandomNumberGenerator]::Fill($mk)
$masterKeyB64 = [Convert]::ToBase64String($mk)

# 3) Пароли (случайные)
$adminPass = [Convert]::ToBase64String((1..24 | ForEach-Object {Get-Random -Minimum 0 -Maximum 256}))
$observerPass = [Convert]::ToBase64String((1..24 | ForEach-Object {Get-Random -Minimum 0 -Maximum 256}))
$playerPass = [Convert]::ToBase64String((1..24 | ForEach-Object {Get-Random -Minimum 0 -Maximum 256}))

# 4) Beacon ID для player (любой положительный int)
$playerBeaconId = 100

# 5) Redis connection string
$redisConn = "localhost:6379,abortConnect=false"

# 6) IP сервера (для телефона/ПК)
Get-NetIPAddress -AddressFamily IPv4 | Where-Object {$_.IPAddress -notlike '127.*'} | Select-Object IPAddress, InterfaceAlias
```

Linux (bash):

```bash
# 1) JWT signing key (64+ символов)
JWT_KEY=$(openssl rand -base64 64 | tr -d '=+/' | cut -c1-80)

# 2) Master key (ровно 32 байта в Base64)
MASTER_KEY_B64=$(openssl rand -base64 32)

# 3) Пароли
ADMIN_PASS=$(openssl rand -base64 24)
OBSERVER_PASS=$(openssl rand -base64 24)
PLAYER_PASS=$(openssl rand -base64 24)

# 4) Beacon ID для player
PLAYER_BEACON_ID=100

# 5) Redis connection string
REDIS_CONN="localhost:6379,abortConnect=false"

# 6) IP сервера
hostname -I
```

Пример применения сгенерированных значений (PowerShell):

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
```

PowerShell (Windows):

```powershell
$env:TACID_JWT_SIGNING_KEY="<случайная_строка_64+>"
$env:TACID_MASTER_KEY_B64="<base64_32_байта>"
$env:TACID_ADMIN_LOGIN="admin"
$env:TACID_ADMIN_PASSWORD="<пароль_admin>"
$env:TACID_OBSERVER_LOGIN="observer"
$env:TACID_OBSERVER_PASSWORD="<пароль_observer>"
$env:TACID_PLAYER_LOGIN="player"
$env:TACID_PLAYER_PASSWORD="<пароль_player>"
$env:TACID_PLAYER_BEACON_ID="100"
$env:REDIS_CONNECTION_STRING="localhost:6379,abortConnect=false"
```

Linux (bash):

```bash
export TACID_JWT_SIGNING_KEY="<случайная_строка_64+>"
export TACID_MASTER_KEY_B64="<base64_32_байта>"
export TACID_ADMIN_LOGIN="admin"
export TACID_ADMIN_PASSWORD="<пароль_admin>"
export TACID_OBSERVER_LOGIN="observer"
export TACID_OBSERVER_PASSWORD="<пароль_observer>"
export TACID_PLAYER_LOGIN="player"
export TACID_PLAYER_PASSWORD="<пароль_player>"
export TACID_PLAYER_BEACON_ID="100"
export REDIS_CONNECTION_STRING="localhost:6379,abortConnect=false"
```

3. Запустите сервер:

```bash
# Профиль A: LAN/VPN или прямой доступ по IP без TLS
ASPNETCORE_ENVIRONMENT=Production \
ASPNETCORE_URLS="http://0.0.0.0:5001" \
TACID_ALLOW_INSECURE_HTTP=true \
dotnet run --project StrikeballServer/Server/StrikeballServer.csproj

# Профиль B: HTTPS (рекомендуется для интернета)
ASPNETCORE_ENVIRONMENT=Production \
ASPNETCORE_URLS="https://0.0.0.0:5001" \
dotnet run --project StrikeballServer/Server/StrikeballServer.csproj
```

4. Откройте API в браузере с ПК:

- Профиль A: http://<IP_сервера>:5001
- Профиль B: https://<IP_или_домен_сервера>:5001

## 2. Быстрый smoke-тест с ПК

Из корня workspace:

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

Успех: в конце есть строка `Smoke test PASSED`.

## 3. Тест с телефона (Android)

### Важно: HTTPS в Production теперь настраивается

По умолчанию в `Production` HTTPS обязателен.
Для локальной сети/VPN можно оставить `Production`, но включить HTTP-режим:

```bash
TACID_ALLOW_INSECURE_HTTP=true
ASPNETCORE_URLS="http://0.0.0.0:5001"
```

Это позволяет работать по HTTP без переключения в `Development` и без перехода на SQLite.

Есть два рабочих варианта:

---

### Вариант A: Быстрый тест на локальной сети/VPN без TLS (Production + HTTP-режим)

Подходит когда: телефон и сервер в одной Wi-Fi сети, реальный сертификат недоступен.

**1. Убедитесь что телефон и сервер в одной сети**

```bash
# На сервере — найти LAN IP:
ip addr show | grep "inet " | grep -v 127.0.0.1
# Пример: 192.168.1.42
```

**2. Запустите сервер в режиме Production с HTTP-режимом**

```bash
# Если запускаете вручную — нужно быть в папке с .dll (publish-output):
cd /opt/strikeball/server
ASPNETCORE_ENVIRONMENT=Production \
ASPNETCORE_URLS="http://0.0.0.0:5001" \
TACID_ALLOW_INSECURE_HTTP=true \
dotnet StrikeballServer.dll

# Или одной строкой с полным путём (можно запускать из любой папки):
ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS="http://0.0.0.0:5001" TACID_ALLOW_INSECURE_HTTP=true \
  dotnet /opt/strikeball/server/StrikeballServer.dll

# Или через systemd — временно поменяйте в /etc/strikeball/environment:
# ASPNETCORE_ENVIRONMENT=Production
# TACID_ALLOW_INSECURE_HTTP=true
# затем: sudo systemctl restart strikeball-server
```

> В этом режиме БД остаётся PostgreSQL (как в production), но HTTP допускается для LAN/VPN тестов.

**3. Откройте порт на firewall сервера**

```bash
# Ubuntu/Debian (ufw):
sudo ufw allow 5001/tcp
sudo ufw status

# или iptables:
sudo iptables -A INPUT -p tcp --dport 5001 -j ACCEPT
```

**4. Проверьте доступность с телефона через браузер**

Откройте в браузере телефона:
```
http://192.168.1.42:5001/api/auth/me
```

Ожидаемый ответ: `401 Unauthorized` (это хорошо — значит сервер отвечает).

---

### Вариант Б: nginx как TLS-терминатор (Production, рекомендуется)

Подходит когда: сервер реальный, нужен HTTPS, есть домен или self-signed cert.

#### Б1. Self-signed сертификат (для тестов без домена)

```bash
# Создать self-signed cert:
sudo mkdir -p /etc/nginx/ssl
sudo openssl req -x509 -nodes -days 365 -newkey rsa:2048 \
  -keyout /etc/nginx/ssl/strikeball.key \
  -out    /etc/nginx/ssl/strikeball.crt \
  -subj "/CN=192.168.1.42/O=Strikeball/C=RU" \
  -addext "subjectAltName=IP:192.168.1.42"
```

> Замените `192.168.1.42` на реальный IP вашего сервера.

```bash
# Установить nginx если нет:
sudo apt install -y nginx

# Конфиг /etc/nginx/sites-available/strikeball:
sudo tee /etc/nginx/sites-available/strikeball > /dev/null <<'EOF'
server {
    listen 443 ssl;
    server_name _;

    ssl_certificate     /etc/nginx/ssl/strikeball.crt;
    ssl_certificate_key /etc/nginx/ssl/strikeball.key;

    # Проксировать всё на dotnet-сервер
    location / {
        proxy_pass         http://127.0.0.1:5001;
        proxy_http_version 1.1;
        proxy_set_header   Upgrade $http_upgrade;
        proxy_set_header   Connection "upgrade";   # нужно для WebSocket / SignalR
        proxy_set_header   Host $host;
        proxy_set_header   X-Real-IP $remote_addr;
        proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
        proxy_read_timeout 3600s;                   # для долгоживущих WebSocket
    }
}

# Редирект HTTP → HTTPS
server {
    listen 80;
    return 301 https://$host$request_uri;
}
EOF

sudo ln -sf /etc/nginx/sites-available/strikeball /etc/nginx/sites-enabled/
sudo nginx -t && sudo systemctl reload nginx
sudo ufw allow 443/tcp
sudo ufw allow 80/tcp
```

**Установить сертификат на Android (для self-signed):**

```bash
# Скопировать cert на телефон (через adb или веб-сервер):
python3 -m http.server 8080 --directory /etc/nginx/ssl
# На телефоне открыть браузер: http://192.168.1.42:8080/strikeball.crt
# → Android предложит установить сертификат как "CA certificate"
# Путь: Настройки → Безопасность → Шифрование и учётные данные → Установить сертификат → CA Certificate
```

После установки сертификата подключение по адресу `https://192.168.1.42` должно открываться без предупреждений.

#### Б2. Let's Encrypt (если есть домен)

```bash
sudo apt install -y certbot python3-certbot-nginx
sudo certbot --nginx -d your.domain.com
# certbot сам обновит конфиг nginx и настроит автообновление
```

---

### Адрес в приложении

После настройки TLS укажите в приложении:

| Сценарий | Адрес |
|---|---|
| Вариант A (HTTP, LAN) | `http://192.168.1.42:5001` |
| Вариант Б, self-signed | `https://192.168.1.42` |
| Вариант Б, домен | `https://your.domain.com` |

---

### Шаги проверки с телефона

1. **Проверить достижимость** — открыть адрес в браузере телефона, ожидать `401` от `/api/auth/me`
2. **Войти** в приложении под учёткой (например, `admin` / `$adminPass`)
3. **Убедиться**, что открывается основной экран и данные загружаются
4. **Refresh-токен**: через 30 минут или после ручного истечения access-токена — приложение должно автоматически получить новый токен без запроса логина
5. **Logout**: после выхода повторные запросы должны возвращать `401` (токен помещается в denylist)

---

### Типичные проблемы

| Симптом | Причина | Решение |
|---|---|---|
| Телефон не пингует сервер | Firewall | `sudo ufw allow 5001/tcp` или 443 |
| `400 HTTPS is required` | HTTP без `TACID_ALLOW_INSECURE_HTTP=true` | Добавить `TACID_ALLOW_INSECURE_HTTP=true` или включить TLS |
| Браузер: `NET::ERR_CERT_AUTHORITY_INVALID` | Self-signed cert не установлен | Установить `.crt` как CA на телефоне |
| Android-приложение: `CLEARTEXT not permitted` | В `AndroidManifest.xml` нет cleartext-доступа | Для HTTP LAN-тестов добавить `android:usesCleartextTraffic="true"` в манифест или `network_security_config.xml` |
| `Connection refused` на 443 | nginx не запущен | `sudo systemctl status nginx` |
| WebSocket не подключается | nginx: нет `Upgrade` заголовка | Убедиться что в nginx-конфиге есть `proxy_set_header Upgrade $http_upgrade` |

## 4. Ручная проверка API с ПК (минимум)

1. Login: POST /api/auth/login
2. Refresh: POST /api/auth/refresh
3. Logout: POST /api/auth/logout
4. Positions: GET /api/positions
5. SignalR negotiate: POST /hubs/positioning/negotiate?negotiateVersion=1

## 5. Мини-чеклист перед полевым тестом

- Сервер запущен по HTTPS и доступен по IP/домену.
- Redis доступен.
- Учетки заданы через переменные окружения.
- Smoke-тест с ПК проходит.
- Телефон логинится и получает данные.
- Logout действительно отзывает доступ.

## 6. Полезные документы

- API: Docs/API.md
- Безопасность: Docs/SECURITY.md
- Полная документация: Docs/PROJECT_DOCUMENTATION.md
