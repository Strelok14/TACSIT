#!/bin/bash
# 🚀 ИНСТРУКЦИЯ ДЛЯ РАЗВЕРТЫВАНИЯ НА LINUX И ТЕСТИРОВАНИЯ

# ============================================
# ЧАСТЬ 1: ПОДГОТОВКА НА WINDOWS (ВЫ)
# ============================================

echo "📋 ЧАСТЬ 1: ПОДГОТОВКА НА WINDOWS"
echo ""
echo "1. Убедитесь что у Вас есть:"
echo "   ☐ Linux сервер (Debian 11+ или Ubuntu 20.04+)"
echo "   ☐ SSH доступ (или консоль на сервере)"
echo "   ☐ Интернет на сервере"
echo "   ☐ Минимум 2GB RAM, 10GB диска"
echo ""
echo "2. Перенести папку StrikeballServer на сервер:"
echo "   Вариант A: SSH + SCP"
echo "   $ cd c:\Я\ICIDS\serv_1"
echo "   $ scp -r StrikeballServer user@server:/opt/"
echo ""
echo "   Вариант B: GitHub (если есть репозиторий)"
echo "   $ git clone <your-repo> /opt/StrikeballServer"
echo ""
echo "   Вариант C: Вручную (скопировать USB/облако)"
echo ""

# ============================================
# ЧАСТЬ 2: НА LINUX СЕРВЕРЕ
# ============================================

echo ""
echo "============================================"
echo "ЧАСТЬ 2: РАЗВЕРТЫВАНИЕ НА LINUX"
echo "============================================"
echo ""
echo "Запустите эти команды на вашем Linux сервере:"
echo ""

cat << 'EOF'
# 1. ПОДКЛЮЧИТЕСЬ К СЕРВЕРУ
ssh user@your_server_ip

# 2. ПЕРЕЙДИТЕ В ПАПКУ ПРОЕКТА
cd /opt/StrikeballServer

# 3. ДАЙТЕ ПРАВА НА ВЫПОЛНЕНИЕ СКРИПТА
chmod +x deploy.sh

# 4. ЗАПУСТИТЕ РАЗВЕРТЫВАНИЕ (требует sudo)
sudo ./deploy.sh

# Скрипт автоматически:
# ✅ Установит .NET 8 Runtime
# ✅ Установит PostgreSQL
# ✅ Создаст пользователя strikeball
# ✅ Скомпилирует приложение
# ✅ Установит systemd сервис
# ✅ Запустит сервис
# ✅ Покажет статус

# 5. ПРОВЕРЬТЕ СТАТУС (должно быть "active (running)")
sudo systemctl status strikeball-server

# 6. СМОТРИТЕ ЛОГИ
sudo journalctl -u strikeball-server -f

# 7. ТЕСТИРУЙТЕ (из другого окна)
curl http://localhost:5000/swagger/index.html
EOF

echo ""
echo "============================================"
echo "ЧАСТЬ 3: ПОСЛЕ РАЗВЕРТЫВАНИЯ"
echo "============================================"
echo ""

cat << 'EOF'
# 1. ПРОВЕРИТЬ ЧТО СЕРВЕР РАБОТАЕТ
curl -I http://localhost:5000

# Должен быть ответ:
# HTTP/1.1 200 OK

# 2. ПРОВЕРИТЬ ЯКОРЯ (должно быть 4)
curl http://localhost:5000/api/anchors

# 3. ПРОВЕРИТЬ МАЯКИ (должно быть 2)
curl http://localhost:5000/api/beacons

# 4. ПРОВЕРИТЬ ПОЗИЦИИ (должно быть пусто)
curl http://localhost:5000/api/positions

# 5. ОТПРАВИТЬ ТЕСТОВОЕ ИЗМЕРЕНИЕ
curl -X POST http://localhost:5000/api/telemetry/measurement \
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

# Должен вернуть позицию с координатами и confidence

# 6. ПРОВЕРИТЬ ПОЗИЦИЮ
curl http://localhost:5000/api/positions/1
EOF

echo ""
echo "============================================"
echo "ЧАСТЬ 4: ТЕСТИРОВАНИЕ С КЛИЕНТОМ"
echo "============================================"
echo ""

cat << 'EOF'
Теперь ваше клиентское приложение может использовать:

REST API Base URL:
  http://<server_ip>:5000/api

WebSocket URL:
  ws://<server_ip>:5000/hubs/positioning

ENDPOINTS КОТОРЫЕ ИСПОЛЬЗУЕТ КЛИЕНТ:

1. Отправить измерение от маяка:
   POST /api/telemetry/measurement
   Content-Type: application/json
   {
     "beaconId": <beacon_id>,
     "distances": [
       {"anchorId": 1, "distance": <meters>},
       {"anchorId": 2, "distance": <meters>},
       ...
     ],
     "timestamp": <unix_ms>,
     "batteryLevel": <0-100>
   }

2. Получить позицию маяка:
   GET /api/positions/<beacon_id>

3. Получить все позиции:
   GET /api/positions

4. Подключиться к WebSocket для real-time обновлений:
   ws://<server_ip>:5000/hubs/positioning
   
   После подключения слушать события:
   - "Connected" - подтверждение подключения
   - "PositionUpdate" - обновление позиции маяка
EOF

echo ""
echo "============================================"
echo "ЧАСТЬ 5: ПРАКТИЧЕСКИЙ ПРИМЕР"
echo "============================================"
echo ""

cat << 'EOF'
СЦЕНАРИЙ: Ваш маяк находится на полигоне в позиции (25, 25, 0.5)

Якоря расположены так:
  Якорь 1: (0,   0,  2)   - угол 1
  Якорь 2: (50,  0,  2)   - угол 2
  Якорь 3: (50, 50,  2)   - угол 3
  Якорь 4: (0,  50,  2)   - угол 4

Расстояния от маяка до якорей:
  До якоря 1: √[(25-0)² + (25-0)² + (0.5-2)²] ≈ 35.4м
  До якоря 2: √[(25-50)² + (25-0)² + (0.5-2)²] ≈ 36.4м
  До якоря 3: √[(25-50)² + (25-50)² + (0.5-2)²] ≈ 35.4м
  До якоря 4: √[(25-0)² + (25-50)² + (0.5-2)²] ≈ 36.4м

Команда для отправки:
curl -X POST http://server:5000/api/telemetry/measurement \
  -H "Content-Type: application/json" \
  -d '{
    "beaconId": 1,
    "distances": [
      {"anchorId": 1, "distance": 35.4},
      {"anchorId": 2, "distance": 36.4},
      {"anchorId": 3, "distance": 35.4},
      {"anchorId": 4, "distance": 36.4}
    ],
    "timestamp": 1709048400000,
    "batteryLevel": 90
  }'

Ожидаемый результат:
{
  "success": true,
  "position": {
    "beaconId": 1,
    "x": 24.5,    ← примерно 25
    "y": 25.0,    ← примерно 25
    "z": 0.4,     ← примерно 0.5
    "confidence": 0.95,  ← высокая уверенность
    "method": "TWR",
    "timestamp": "2026-03-05T10:30:00Z",
    "anchorsUsed": 4
  }
}
EOF

echo ""
echo "============================================"
echo "ЧАСТЬ 6: МОНИТОРИНГ И ОТЛАДКА"
echo "============================================"
echo ""

cat << 'EOF'
Просмотр логов в реальном времени:
  sudo journalctl -u strikeball-server -f

Просмотр последних 50 строк логов:
  sudo journalctl -u strikeball-server -n 50

Проверка статуса статус базы данных:
  sudo -u postgres psql -d strikeballdb -c "\dt"

Проверка размера БД:
  sudo -u postgres psql -d strikeballdb -c "SELECT pg_size_pretty(pg_database_size(current_database()));"

Перезапуск сервиса:
  sudo systemctl restart strikeball-server

Остановка сервиса:
  sudo systemctl stop strikeball-server

Просмотр использования памяти:
  ps aux | grep StrikeballServer

Проверка открытых портов:
  sudo netstat -tulpn | grep LISTEN
  # Должен быть порт 5432 (PostgreSQL) и 5000 (Server)
EOF

echo ""
echo "============================================"
echo "ЧАСТЬ 7: ЕСЛИ ЧТО-ТО НЕ РАБОТАЕТ"
echo "============================================"
echo ""

cat << 'EOF'
ПРОБЛЕМА: Сервис не запускается
РЕШЕНИЕ:
  sudo journalctl -u strikeball-server -n 20
  # Посмотреть ошибки в логах

ПРОБЛЕМА: PostgreSQL не подключается
РЕШЕНИЕ:
  sudo -u postgres psql -d strikeballdb
  # Если не входит - требуется пересоздание БД:
  sudo ./deploy.sh  # Заново запустить deploy

ПРОБЛЕМА: Порт 5000 уже занят
РЕШЕНИЕ:
  sudo netstat -tulpn | grep 5000
  sudo kill <PID>
  или изменить порт в appsettings.Production.json

ПРОБЛЕМА: Недостаточно памяти
РЕШЕНИЕ:
  Очистить старые логи:
    sudo journalctl --vacuum-time=7d
  Очистить старые позиции (старше 30 дней):
    sudo -u postgres psql -d strikeballdb <<EOF
    DELETE FROM "Positions" WHERE "Timestamp" < NOW() - INTERVAL '30 days';
    DELETE FROM "Measurements" WHERE "Timestamp" < NOW() - INTERVAL '30 days';
EOF
EOF

echo ""
echo "✅ Инструкция завершена!"
echo "🚀 Начинайте развертывание!"
