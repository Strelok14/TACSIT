# T.A.C.I.D. — Деплой и Эксплуатация (единый регламент)

Документ задаёт единый operational-формат для production.
Архитектурные детали и API не дублируются: они описаны в `README.md` и `Docs/SECURITY.md`.

## 1. Карта документации

- `README.md` — архитектура, API, роли, базовый запуск
- `Docs/SECURITY.md` — требования безопасности, ключи, JWT/HMAC, TLS
- `scripts/README_DEPLOY_FROM_USER.md` — кратко про `bootstrap/deploy/smoke-test`
- `Docs/DEPLOYMENT_AND_PROJECT_ANALYSIS.md` (этот файл) — production runbook

## 2. Что считается "готово к прод"

- Сервис `strikeball-server` запущен и стабилен
- PostgreSQL и Redis доступны
- TLS работает (для публичного контура)
- `smoke-test.sh` проходит без ошибок
- Android клиент логинится и получает позиции

## 3. Production-поток (без дублей)

Используются 3 отдельных скрипта для контроля этапов:

1. `scripts/bootstrap_server.sh` — инфраструктура
2. `scripts/deploy_from_user.sh` (или `deploy_to_server.sh`) — выкладка приложения
3. `scripts/smoke-test.sh` — проверка после выкладки

## 4. Минимальный чеклист (clean server -> prod)

1. Передать репозиторий на сервер
2. Выполнить `bootstrap_server.sh`
3. Проверить/при необходимости поправить `/etc/strikeball/environment` через `tacid-manager`
4. Выполнить deploy-скрипт
5. Выполнить smoke-test
6. Проверить `systemctl status` и `journalctl`
7. Подключить Android клиент

Подробная команда-последовательность поддерживается в актуальном виде в `scripts/README_DEPLOY_FROM_USER.md`.

## 5. Команды эксплуатации

### 5.1 Статус и логи

```bash
sudo systemctl status strikeball-server --no-pager
sudo journalctl -u strikeball-server -n 200 --no-pager
sudo journalctl -u strikeball-server -f
```

### 5.2 Перезапуск после изменения env

```bash
sudo systemctl restart strikeball-server
```

### 5.3 Быстрые проверки зависимостей

```bash
redis-cli ping
sudo -u postgres psql -d strikeballdb -c "select now();"
```

## 6. Менеджер конфигурации (tacid-manager)

Где находится:

- Проект: `ServerManager/`
- Точка входа: `ServerManager/Program.cs`

Запуск:

```bash
dotnet run --project ServerManager/ServerManager.csproj
```

Linux production (файл окружения):

```bash
sudo dotnet ServerManager/bin/Release/net8.0/tacid-manager.dll --env-file /etc/strikeball/environment
```

Что меняется через менеджер:

- Ключи: `TACID_JWT_SIGNING_KEY`, `TACID_MASTER_KEY_B64`
- Учётные данные: `TACID_ADMIN_*`, `TACID_OBSERVER_*`, `TACID_PLAYER_*`
- Сеть: `ASPNETCORE_URLS`, `TACID_ALLOW_INSECURE_HTTP`
- БД/Redis: `ConnectionStrings__PostgreSQL`, `Redis__ConnectionString`

Важно:

- После изменения env нужно перезапускать сервис
- Смена JWT key инвалидирует текущие токены
- Смена Master key требует осознанной процедуры по ключам маяков (см. `Docs/SECURITY.md`)

## 7. Smoke-test политика

Перед запуском:

```bash
export TACID_TEST_LOGIN=admin
export TACID_TEST_PASSWORD=$(sudo awk -F= '/^TACID_ADMIN_PASSWORD=/{print $2}' /etc/strikeball/environment)
```

Проверка:

```bash
./scripts/smoke-test.sh your.domain.com
```

Если smoke-test не проходит, прод не считается готовым.

## 8. Rollback (операционный)

1. Остановить сервис
2. Вернуть предыдущую сборку в `/opt/strikeball/server`
3. Проверить права (`strikeball:strikeball`)
4. Запустить сервис
5. Прогнать smoke-test

Базовая команда:

```bash
sudo systemctl stop strikeball-server
# restore files in /opt/strikeball/server
sudo chown -R strikeball:strikeball /opt/strikeball/server
sudo systemctl start strikeball-server
./scripts/smoke-test.sh your.domain.com
```

## 9. Аудит документации (результат)

Проверены файлы:

- `README.md`
- `Docs/SECURITY.md`
- `Docs/PROJECT_COMPLETION_REPORT.md`
- `scripts/README_DEPLOY_FROM_USER.md`
- `Docs/DEPLOYMENT_AND_PROJECT_ANALYSIS.md`

Решения по унификации:

- Убран дублирующий англо-русский стиль в этом документе
- Убраны повторные длинные блоки команд, оставлены ссылки на единый источник в `scripts/README_DEPLOY_FROM_USER.md`
- Разделены зоны ответственности: архитектура в `README.md`, безопасность в `Docs/SECURITY.md`, операции в этом runbook

## 10. Источник истины

Для деплоя и операций использовать только:

1. `scripts/README_DEPLOY_FROM_USER.md`
2. `Docs/DEPLOYMENT_AND_PROJECT_ANALYSIS.md`
3. `Docs/SECURITY.md`

Остальные документы должны ссылаться на эти 3 файла, а не дублировать их содержимое.
