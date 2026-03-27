#!/bin/bash

# 🚀 Скрипт развертывания Strikeball Server на Linux Debian/Ubuntu
# Использование: sudo ./deploy.sh

set -e

echo "=========================================="
echo "🚀 Развертывание Strikeball Server v1.0"
echo "=========================================="

# 1. Проверка root
if [ "$EUID" -ne 0 ]; then 
   echo "❌ Этот скрипт должен запускаться с sudo"
   exit 1
fi

# Helper: execute command as postgres without hard dependency on sudo.
run_as_postgres() {
    if command -v sudo >/dev/null 2>&1; then
        sudo -u postgres "$@"
    elif command -v runuser >/dev/null 2>&1; then
        runuser -u postgres -- "$@"
    else
        su -s /bin/bash postgres -c "$*"
    fi
}

# 2. Установка зависимостей
echo "📦 Установка зависимостей..."
apt-get update
apt-get install -y curl wget postgresql postgresql-contrib ca-certificates zlib1g libssl3 libgssapi-krb5-2

# ICU необходим для globalization в .NET (иначе возможен SIGABRT при старте)
apt-get install -y libicu72 || apt-get install -y libicu71 || apt-get install -y libicu70 || apt-get install -y libicu67 || apt-get install -y libicu-dev

# 3. Установка .NET 8 SDK (нужен для dotnet publish)
echo "📦 Установка .NET 8 SDK..."
wget https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh

# Важно: ставим SDK, т.к. ниже выполняется dotnet publish
/tmp/dotnet-install.sh --channel 8.0 --install-dir /usr/share/dotnet

# Добавляем .NET в PATH и системные ссылки
export PATH="$PATH:/usr/share/dotnet"
ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet || true
ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet || true

# Быстрая проверка SDK
dotnet --list-sdks | grep '^8\.' >/dev/null

# 4. Создание пользователя
echo "👤 Создание пользователя strikeball..."
useradd -m -s /bin/false strikeball || echo "Пользователь уже существует"

# 5. Создание директорий
echo "📁 Создание директорий..."
mkdir -p /opt/strikeball/server
mkdir -p /var/log/strikeball
chown -R strikeball:strikeball /opt/strikeball
chown -R strikeball:strikeball /var/log/strikeball

# 6. Копирование приложения
echo "📋 Копирование приложения..."
if [ -d "Server" ]; then
    # Если запускаем из папки с исходниками
    cd Server
    dotnet publish -c Release -o /opt/strikeball/server
    cd ..
else
    echo "❌ Ошибка: не найдена папка Server"
    exit 1
fi

chmod +x /opt/strikeball/server/StrikeballServer

# 7. Настройка PostgreSQL
echo "🗃️  Настройка PostgreSQL..."
systemctl start postgresql
run_as_postgres psql <<EOF
DROP DATABASE IF EXISTS strikeballdb;
DROP USER IF EXISTS strikeballuser;

CREATE USER strikeballuser WITH PASSWORD 'strikeballpassword123';
CREATE DATABASE strikeballdb OWNER strikeballuser;
GRANT ALL PRIVILEGES ON DATABASE strikeballdb TO strikeballuser;

\c strikeballdb
GRANT ALL ON SCHEMA public TO strikeballuser;
EOF

echo "✅ PostgreSQL настроена"

# 8. Генерация файла секретов /etc/strikeball/environment
echo "🔑 Настройка секретов..."
mkdir -p /etc/strikeball
chmod 750 /etc/strikeball

if [ ! -f /etc/strikeball/environment ]; then
    echo "  Генерация новых ключей..."
    JWT_KEY=$(cat /dev/urandom | tr -dc 'A-Za-z0-9' | head -c 80)
    cat > /etc/strikeball/environment <<ENVEOF
# Strikeball Server — файл секретов
# НЕ КОММИТИТЬ в git, НЕ давать права на чтение посторонним

# JWT
TACID_JWT_SIGNING_KEY=${JWT_KEY}

# База данных PostgreSQL
ConnectionStrings__PostgreSQL=Host=localhost;Database=strikeballdb;Username=strikeballuser;Password=strikeballpassword123;Port=5432;SSL Mode=Disable;
ENVEOF
    echo "  ✅ Ключи сгенерированы: /etc/strikeball/environment"
else
    echo "  ⚠️  Файл /etc/strikeball/environment уже существует — не перезаписываю."
    echo "     Убедитесь, что TACID_JWT_SIGNING_KEY задан в нём."
fi

# Права: root владеет, группа strikeball читает, другие — ничего
chown root:strikeball /etc/strikeball/environment
chmod 640 /etc/strikeball/environment

# 9. Установка systemd service
echo "⚙️  Установка systemd service..."
cp strikeball-server.service /etc/systemd/system/
systemctl daemon-reload
systemctl enable strikeball-server
systemctl restart strikeball-server

# 9. Проверка статуса
echo ""
echo "=========================================="
echo "✅ Развертывание завершено!"
echo "=========================================="
echo ""
echo "📊 Статус сервиса:"
systemctl status strikeball-server --no-pager || true
echo ""
echo "📝 Логи:"
journalctl -u strikeball-server -n 10 --no-pager || true
echo ""
echo "🌐 Доступ:"
echo "   Swagger UI: http://localhost:5000"
echo "   API: http://localhost:5000/api"
echo "   WebSocket: ws://localhost:5000/hubs/positioning"
echo ""
echo "📋 Полезные команды:"
echo "   Просмотр логов: journalctl -u strikeball-server -f"
echo "   Перезапуск: systemctl restart strikeball-server"
echo "   Остановка: systemctl stop strikeball-server"
echo "   Статус: systemctl status strikeball-server"
echo ""
