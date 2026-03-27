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

Быстрая настройка Redis через `tacid-manager`:

1. Открыть меню `4. База данных и Redis`.
2. Для LAN-сервера выбрать `5. Redis пресеты` → `1. LAN / localhost`.
3. Либо пройти `4. Redis мастер настройки (пошагово)` для ручной сборки строки.
4. Выполнить `6. Проверить доступность Redis endpoint`.
5. Сохранить изменения через главное меню `7. Сохранить файл`.
6. Перезапустить сервис: `sudo systemctl restart strikeball-server`.

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
./scripts/smoke-test.sh http://localhost:5001
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
./scripts/smoke-test.sh http://localhost:5001
```

---

## 11. Боевой запуск — пошаговый регламент

### Шаг 1. Развернуть сервер (чистая машина)

```bash
git clone https://github.com/Strelok14/TACSIT.git
cd TACSIT/StrikeballServer

# LAN/VPN профиль без TLS:
sudo ./scripts/bootstrap_server.sh --allow-insecure-http

# Интернет-профиль с TLS:
sudo ./scripts/bootstrap_server.sh \
  --domain tacid.example.com \
  --tls-email admin@example.com \
  --setup-tls
```

Bootstrap автоматически генерирует ключи безопасности и создаёт `/etc/strikeball/environment`.

### Шаг 2. Добавить учётные данные пользователей

Bootstrap не создаёт логины — их необходимо добавить вручную:

```bash
sudo nano /etc/strikeball/environment
```

Добавить в конец файла:

```
TACID_ADMIN_LOGIN=admin
TACID_ADMIN_PASSWORD=НадёжныйПароль123
TACID_OBSERVER_LOGIN=observer
TACID_OBSERVER_PASSWORD=НаблюдательПароль456
TACID_PLAYER_LOGIN=player
TACID_PLAYER_PASSWORD=ИгрокПароль789
TACID_PLAYER_BEACON_ID=1
```

```bash
sudo systemctl restart strikeball-server
```

### Шаг 3. Выложить приложение

```bash
cd ~/TACSIT/StrikeballServer/scripts
./deploy_from_user.sh
```

### Шаг 4. Диагностика

```bash
chmod +x ./doctor.sh

# Только проверить:
./doctor.sh --base-url http://localhost:5001

# Проверить и попытаться исправить (дрейф пароля БД, остановленный сервис):
./doctor.sh --base-url http://localhost:5001 --fix
```

### Шаг 5. Smoke-test

```bash
export TACID_TEST_PASSWORD=$(sudo sed -n 's/^TACID_ADMIN_PASSWORD=//p' /etc/strikeball/environment | tail -1)
./smoke-test.sh http://localhost:5001
```

Готово, если в выводе нет строк `[FAIL]`.

---

## 12. Подключение Android-клиента

### Сборка APK

```bash
# На dev-машине:
cd StrikeballServer/ClientApp
./gradlew assembleDebug
# APK: app/build/outputs/apk/debug/app-debug.apk

# Установить на устройство через USB:
adb install app/build/outputs/apk/debug/app-debug.apk
```

### Что вводить на экране входа

| Поле | Значение | Пример |
|---|---|---|
| **IP сервера** | IP или hostname сервера | `192.168.1.50` |
| **Логин** | `TACID_ADMIN_LOGIN` из env | `admin` |
| **Пароль** | `TACID_ADMIN_PASSWORD` из env | `НадёжныйПароль123` |

Посмотреть учётные данные на сервере:

```bash
sudo grep -E '^TACID_(ADMIN|OBSERVER|PLAYER)_(LOGIN|PASSWORD)' /etc/strikeball/environment
```

**Как клиент выбирает HTTP/HTTPS:**

- IPv4 (любой) / localhost / `*.local` → `http://HOST:5001`
- Домен (например `tacid.example.com`) → `https://HOST:5001`
- Порт `5001` добавляется автоматически, если не указан

### Роли

| Роль | Доступ |
|---|---|
| `admin` | Полный доступ ко всем экранам |
| `observer` | Только карта (позиции маяков в реальном времени) |
| `player` | Карта + отправка телеметрии + AI-камера |

### AI-камера — сигнальный URL (ServerAI)

При открытии экрана AI-камеры клиент автоматически вычисляет URL, но его можно изменить вручную.

| Ситуация | URL в поле |
|---|---|
| ServerAI на LAN-сервере | `ws://192.168.1.50:8080/ws` (заполняется автоматически) |
| ServerAI на VPN | `ws://10.8.0.1:8080/ws` |
| Обработка прямо на устройстве (ML Kit) | Введите `local` |
| ServerAI не используется | Оставьте пустым или введите `local` |

Статус ServerAI на сервере:

```bash
sudo systemctl status serverai
```

### Типичные проблемы

| Симптом | Причина | Решение |
|---|---|---|
| «Ошибка сети» | Сервер недоступен | Проверить IP и порт: `curl http://IP:5001/` |
| «Неверные учётные данные» | Пароль не совпадает с env на сервере | Сверить с `/etc/strikeball/environment` |
| «Слишком много запросов (429)» | Превышен лимит телеметрии | В Measurement включить авто-отправку с интервалом >= 700 мс (рекомендуется 1000 мс) |
| Карта не обновляется | SignalR не подключился | Клиент сам переходит на HTTP polling; проверить логи сервиса |
| AI-камера: «Invalid URL» | Неверный ws-адрес | Должен начинаться с `ws://` или `wss://`, либо введите `local` |
| Нет позиций на карте | Маяки не присылают данные | Убедитесь что маяки отправляют `POST /api/telemetry/measurement` |

---

## 13. Финальный минимальный чеклист (LAN без TLS)

Используйте этот список как единственный сценарий перед боевым тестом в LAN/VPN-контуре.

1. На сервере выполнить bootstrap один раз:

```bash
cd ~/TACSIT/StrikeballServer
sudo ./scripts/bootstrap_server.sh --allow-insecure-http
```

2. Проверить/задать пользователей в `/etc/strikeball/environment`:

```bash
sudo grep -E '^TACID_(ADMIN|OBSERVER|PLAYER)_(LOGIN|PASSWORD|BEACON_ID)' /etc/strikeball/environment
```

3. Выложить актуальную сборку:

```bash
cd ~/TACSIT/StrikeballServer/scripts
./deploy_from_user.sh
```

4. Выполнить авто-диагностику с фиксом:

```bash
./doctor.sh --base-url http://localhost:5001 --fix
```

5. Прогнать smoke-test:

```bash
export TACID_TEST_PASSWORD=$(sudo sed -n 's/^TACID_ADMIN_PASSWORD=//p' /etc/strikeball/environment | tail -1)
./smoke-test.sh http://localhost:5001
```

6. Проверить сервис и логи:

```bash
sudo systemctl status strikeball-server --no-pager
sudo journalctl -u strikeball-server -n 120 --no-pager
```

7. Подключить Android-клиент:

- В поле IP указать LAN IP сервера (пример: `192.168.1.50`).
- Логин/пароль взять из `/etc/strikeball/environment`.
- Для AI: оставить авто-URL `ws://<server-ip>:8080/ws` или ввести `local`.

Если шаги 4 и 5 без `[FAIL]`, контур готов к боевому тесту без дополнительных импровизаций.
