# Скрипты деплоя (кратко)

В этой папке 3 отдельных скрипта для полного цикла:
- `bootstrap_server.sh` — первичная настройка сервера (инфраструктура)
- `deploy_from_user.sh` — выкладка новой версии приложения
- `smoke-test.sh` — проверка после деплоя

It also contains `deploy_serverai_from_user.sh` — a safe deployment helper for `ServerAI` into `/opt/tacsit/ServerAI`.

## Рекомендуемый порядок

1. Bootstrap (один раз, под root):

```bash
cd /path/to/TACSIT/StrikeballServer
sudo ./scripts/bootstrap_server.sh
```

С TLS:

```bash
sudo ./scripts/bootstrap_server.sh \
  --domain tacid.example.com \
  --tls-email admin@example.com \
  --setup-tls
```

2. Deploy приложения (обычный пользователь):

```bash
cd /home/youruser/TACSIT/scripts
./deploy_from_user.sh
```

3. Диагностика окружения (рекомендуется):

```bash
chmod +x ./doctor.sh
./doctor.sh --base-url http://localhost:5001
```

4. Проверка работоспособности:

```bash
# Для LAN/VPN профиля (без TLS)
./smoke-test.sh http://localhost:5001
```

## Что делает каждый скрипт

`bootstrap_server.sh`:
- Устанавливает зависимости: .NET 8, PostgreSQL, Redis, nginx, certbot, rsync
- Создаёт пользователя `strikeball` и каталоги `/opt/strikeball`, `/etc/strikeball`
- Настраивает PostgreSQL (БД/пользователь)
- Устанавливает `strikeball-server.service`
- Создаёт `/etc/strikeball/environment` (если файла нет)
- Опционально настраивает nginx + TLS

`deploy_from_user.sh`:
- Собирает `Server/` через `dotnet publish` в `~/publish`
- Останавливает `strikeball-server`
- Копирует файлы в `/opt/strikeball/server` через `rsync`
- Обновляет права доступа
- Запускает сервис и показывает статус

`smoke-test.sh`:
- Проверяет доступность API после деплоя
- Проверяет авторизацию/базовые endpoint-ы

## Важно

- Service name: `strikeball-server`
- `bootstrap_server.sh` можно запускать повторно (idempotent)
- Для production после deploy проверяйте логи:

```bash
sudo journalctl -u strikeball-server -n 200 --no-pager
```

## ServerAI deploy (Debian/Ubuntu)

Дополнительно доступен скрипт `deploy_serverai_from_user.sh` для безопасного деплоя `ServerAI` в `/opt/tacsit/ServerAI`.

Usage (on server as your normal user):

```bash
cd /home/youruser/TACSIT/scripts
./deploy_serverai_from_user.sh
```

What it does:
- Syncs `ServerAI/` sources to `/opt/tacsit/ServerAI` with `rsync`
- Ensures service user `serverai` exists
- Creates/updates `.venv` and installs `requirements.txt`
- Installs `/etc/systemd/system/serverai.service`
- Enables/restarts `serverai` and shows status

Notes:
- Requires `python3`, `python3-venv`, `rsync`, and `sudo`.
- If server path differs from `/opt/tacsit/ServerAI`, update `serverai.service` before deployment.
