🚀 БЫСТРОЕ РАЗВЕРТЫВАНИЕ И ТЕСТИРОВАНИЕ НА LINUX
═════════════════════════════════════════════════════════════════════

ЦЕЛЬ: Развернуть сервер на Linux за 10 минут и начать принимать данные от клиента

═════════════════════════════════════════════════════════════════════

⏱️ ПОШАГОВО (предполагаются 10 минут на выполнение)

ШАГ 1: ПЕРЕНОС ПРОЕКТА НА LINUX (Windows - 1-2 минуты)
═════════════════════════════════════════════════════════

Если у вас есть SSH доступ к Linux серверу:

# На Windows, в PowerShell, в папке c:\Я\ICIDS\serv_1

.\transfer-to-linux.ps1 -Server 192.168.1.100 -User root

# Система попросит пароль / подтверждение
# Все файлы перенесутся в /opt/StrikeballServer

Если нет SSH, можете:
- Скопировать файлы через SCP вручную
- Залить на GitHub и клонировать с сервера
- Скопировать на USB/облако

═════════════════════════════════════════════════════════════════════

ШАГ 2: РАЗВЕРТЫВАНИЕ НА LINUX (Linux - 3-5 минут)
════════════════════════════════════════════════════

На Linux сервере запустите:

# Подключитесь к серверу
ssh root@192.168.1.100

# Перейдите в папку с проектом
cd /opt/StrikeballServer

# Дайте права на выполнение скрипта развертывания
chmod +x deploy.sh

# Запустите скрипт (требуется sudo)
sudo ./deploy.sh

# Скрипт автоматически:
# ✅ Установит необходимые пакеты
# ✅ Установит .NET 8 Runtime
# ✅ Установит PostgreSQL
# ✅ Создаст БД и users
# ✅ Скомпилирует приложение
# ✅ Установит systemd сервис
# ✅ Запустит сервис

# После завершения вы увидите:
# ✅ Статус сервиса
# ✅ Последние логи
# ✅ URL для доступа

═════════════════════════════════════════════════════════════════════

ШАГ 3: ПРОВЕРКА РАБОТОСПОСОБНОСТИ (Linux - 2 минуты)
════════════════════════════════════════════════════

# Проверить что сервис работает
sudo systemctl status strikeball-server

# Проверить в браузере или curl
curl -I http://localhost:5000

# Смотреть логи в реальном времени
sudo journalctl -u strikeball-server -f

# Нажмите Ctrl+C чтобы выйти из логов

═════════════════════════════════════════════════════════════════════

ШАГ 4: ТЕСТИРОВАНИЕ API (Linux или Windows - 3 минуты)
══════════════════════════════════════════════════════

Все команды можно запускать как на самом сервере, так и удаленно.

# 1. Получить список якорей
curl http://192.168.1.100:5000/api/anchors

# Должны вернуться 4 якоря:
# [
#   {"id": 1, "name": "Anchor_1", "x": 0, "y": 0, "z": 2, "status": "Active"},
#   ...
# ]

# 2. Получить список маяков
curl http://192.168.1.100:5000/api/beacons

# Должны вернуться 2 маяка

# 3. ОТПРАВИТЬ ТЕСТОВОЕ ИЗМЕРЕНИЕ
curl -X POST http://192.168.1.100:5000/api/telemetry/measurement \
  -H "Content-Type: application/json" \
  -d '{
    "beaconId": 1,
    "distances": [
      {"anchorId": 1, "distance": 15.2},
      {"anchorId": 2, "distance": 38.5},
      {"anchorId": 3, "distance": 42.1},
      {"anchorId": 4, "distance": 20.3}
    ],
    "timestamp": 1709048400000,
    "batteryLevel": 85
  }'

# Должен вернуться JSON с вычисленной позицией:
# {
#   "success": true,
#   "position": {
#     "beaconId": 1,
#     "x": 23.45,
#     "y": 24.56,
#     "z": 0.15,
#     "confidence": 0.92,
#     ...
#   }
# }

# 4. Получить позицию маяка
curl http://192.168.1.100:5000/api/positions/1

# 5. Получить все позиции
curl http://192.168.1.100:5000/api/positions

═════════════════════════════════════════════════════════════════════

ШАГ 5: ГОТОВНОСТЬ К КЛИЕНТСКОМУ ПРИЛОЖЕНИЮ
════════════════════════════════════════════

Сервер готов для вашего приложения!

ОБНОВИТЕ В СВОЕМ КЛИЕНТСКОМ КОДЕ:

Java/Kotlin:
  BASE_URL = "http://192.168.1.100:5000/api"

JavaScript:
  const SERVER = "http://192.168.1.100:5000"
  const WS_URL = "ws://192.168.1.100:5000/hubs/positioning"

Python:
  SERVER = "http://192.168.1.100:5000"

ОСНОВНЫЕ ENDPOINTS ДЛЯ ИСПОЛЬЗОВАНИЯ:

1. Отправить измерение от маяка:
   POST /api/telemetry/measurement
   {beaconId, distances[], timestamp, batteryLevel}

2. Получить текущую позицию:
   GET /api/positions/{beaconId}

3. Получить ВСЕ позиции (для карты):
   GET /api/positions

4. Real-time подписка:
   WebSocket ws://server:5000/hubs/positioning
   Событие: "PositionUpdate"

═════════════════════════════════════════════════════════════════════

📊 МОНИТОРИНГ И ОТЛАДКА

Просмотр логов:
  sudo journalctl -u strikeball-server -f

Проверка статуса БД:
  sudo -u postgres psql -d strikeballdb -c "\dt"

Размер БД:
  sudo -u postgres psql -d strikeballdb -c \
    "SELECT pg_size_pretty(pg_database_size(current_database()))"

Количество позиций в БД:
  sudo -u postgres psql -d strikeballdb -c \
    "SELECT COUNT(*) FROM \"Positions\""

Перезапуск сервиса:
  sudo systemctl restart strikeball-server

═════════════════════════════════════════════════════════════════════

🚨 ЕСЛИ ЕСТЬ ПРОБЛЕМЫ

ПРОБЛЕМА: Сервис не запускается после deploy.sh
РЕШЕНИЕ:
  sudo journalctl -u strikeball-server -n 20
  # Посмотреть развернутые ошибки
  sudo systemctl restart strikeball-server

ПРОБЛЕМА: Cannot connect to PostgreSQL
РЕШЕНИЕ:
  sudo systemctl status postgresql
  sudo -u postgres psql
  # Если БД не создана - нужно переоздать:
  sudo -u postgres psql <<EOF
  CREATE DATABASE strikeballdb;
  CREATE USER strikeballuser WITH PASSWORD 'strikeballpassword123';
  GRANT ALL PRIVILEGES ON DATABASE strikeballdb TO strikeballuser;
  EOF

ПРОБЛЕМА: Порт 5000 не отвечает
РЕШЕНИЕ:
  sudo netstat -tulpn | grep 5000
  curl localhost:5000  # Проверить локально
  # Может быть проблема с брандмауэром:
  sudo ufw allow 5000
  sudo ufw allow 5432

ПРОБЛЕМА: Высокое использование памяти
РЕШЕНИЕ:
  # Очистить старые данные
  sudo -u postgres psql -d strikeballdb <<EOF
  DELETE FROM "Positions" WHERE "Timestamp" < NOW() - INTERVAL '7 days';
  VACUUM;
EOF

═════════════════════════════════════════════════════════════════════

✅ ПОЛНЫЙ WORKFLOW ТЕСТИРОВАНИЯ

1. Развернули сервер ✓
2. Проверили что работает ✓
3. Отправили тестовое измерение ✓
4. Получили позицию ✓
5. Готовы к клиентскому приложению ✓

ТЕПЕРЬ:
- Ваше приложение отправляет данные → POST /api/telemetry/measurement
- Сервер мгновенно вычисляет позицию
- Приложение получает позицию → GET /api/positions или WebSocket

═════════════════════════════════════════════════════════════════════

📁 ФАЙЛЫ КОТОРЫЕ ВАМ ПОМОГУТ

LINUX_DEPLOYMENT_STEP_BY_STEP.sh - Подробная инструкция развертывания
TEST_API_WITH_CURL.sh              - Примеры curl команд для тестирования
CLIENT_INTEGRATION_GUIDE.md         - Как интегрировать клиент (Java, JS, Python)
transfer-to-linux.ps1              - Скрипт для переноса на Linux

═════════════════════════════════════════════════════════════════════

🎯 ИТОГ

Вы имеете полностью работающий сервер Strikeball:
✅ Принимает данные от маяков
✅ Вычисляет 3D позиции в реальном времени
✅ Отправляет позиции через REST API и WebSocket
✅ Хранит историю в PostgreSQL
✅ Готов к production

Начинайте тестировать с вашим клиентским приложением! 🚀
