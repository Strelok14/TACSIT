# Скрипты деплоя (кратко)

В этой папке 3 отдельных скрипта для полного цикла:
- `bootstrap_server.sh` — первичная настройка сервера (инфраструктура)
- `deploy_from_user.sh` — выкладка новой версии приложения
- `smoke-test.sh` — проверка после деплоя

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
cd /home/youruser/TACSIT/StrikeballServer/scripts
./deploy_from_user.sh
```

3. Проверка работоспособности:

```bash
./smoke-test.sh
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
