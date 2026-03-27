#!/usr/bin/env bash
set -euo pipefail

# bootstrap_server.sh
# Инициализация Linux-сервера под deployment T.A.C.I.D.
# Выполняет:
# - установку системных зависимостей
# - установку .NET 8 SDK/runtime через Microsoft packages
# - создание system user/directories
# - настройку PostgreSQL и Redis
# - установку systemd unit
# - генерацию env-файла с секретами, если он ещё не существует
# - опциональную настройку nginx + TLS

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

APP_USER="strikeball"
APP_GROUP="strikeball"
APP_ROOT="/opt/strikeball"
ENV_DIR="/etc/strikeball"
ENV_FILE="$ENV_DIR/environment"
SERVICE_NAME="strikeball-server"
SERVICE_FILE_SRC="$REPO_ROOT/strikeball-server.service"
SERVICE_FILE_DST="/etc/systemd/system/${SERVICE_NAME}.service"
NGINX_TEMPLATE="$SCRIPT_DIR/nginx/tacid.conf"
NGINX_SITE_DST="/etc/nginx/sites-available/tacid.conf"
NGINX_SITE_LINK="/etc/nginx/sites-enabled/tacid.conf"

DOMAIN=""
TLS_EMAIL=""
DB_NAME="strikeballdb"
DB_USER="strikeballuser"
DB_PASSWORD=""
PLAYER_BEACON_ID="1"
ALLOW_INSECURE_HTTP="false"
SETUP_TLS=0
OVERWRITE_ENV=0

usage() {
  cat <<'EOF'
Usage:
  sudo ./scripts/bootstrap_server.sh [options]

Options:
  --domain <fqdn>           Домен для nginx/certbot
  --tls-email <email>       Email для Let's Encrypt
  --db-name <name>          Имя PostgreSQL базы (default: strikeballdb)
  --db-user <name>          Имя PostgreSQL пользователя (default: strikeballuser)
  --db-password <password>  Пароль PostgreSQL пользователя (default: auto-generate)
  --player-beacon-id <id>   beacon_id для роли player (default: 1)
  --allow-insecure-http     Включить LAN/VPN HTTP режим без TLS
  --setup-tls               Настроить nginx + certbot (требует --domain и --tls-email)
  --overwrite-env           Пересоздать /etc/strikeball/environment
  --help                    Показать эту справку

Examples:
  sudo ./scripts/bootstrap_server.sh
  sudo ./scripts/bootstrap_server.sh --domain tacid.example.com --tls-email admin@example.com --setup-tls
  sudo ./scripts/bootstrap_server.sh --allow-insecure-http --db-password 'StrongPassword123!'
EOF
}

log() {
  echo "[bootstrap] $*"
}

fail() {
  echo "[bootstrap][error] $*" >&2
  exit 1
}

require_root() {
  if [[ "${EUID}" -ne 0 ]]; then
    fail "Скрипт нужно запускать через sudo/root"
  fi
}

random_b64() {
  local bytes="$1"
  openssl rand -base64 "$bytes" | tr -d '\n'
}

random_urlsafe() {
  local bytes="$1"
  openssl rand -base64 "$bytes" | tr -d '\n=+/'
}

sql_escape() {
  printf "%s" "$1" | sed "s/'/''/g"
}

extract_env_connection_password() {
  local file="$1"
  [[ -f "$file" ]] || return 1

  # Read last connection string line to respect user overrides later in file.
  local line
  line="$(grep '^ConnectionStrings__PostgreSQL=' "$file" | tail -n 1 || true)"
  [[ -n "$line" ]] || return 1

  line="${line#ConnectionStrings__PostgreSQL=}"
  printf "%s" "$line" | sed -n 's/.*Password=\([^;]*\).*/\1/p'
}

parse_args() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --domain)
        DOMAIN="${2:-}"
        shift 2
        ;;
      --tls-email)
        TLS_EMAIL="${2:-}"
        shift 2
        ;;
      --db-name)
        DB_NAME="${2:-}"
        shift 2
        ;;
      --db-user)
        DB_USER="${2:-}"
        shift 2
        ;;
      --db-password)
        DB_PASSWORD="${2:-}"
        shift 2
        ;;
      --player-beacon-id)
        PLAYER_BEACON_ID="${2:-}"
        shift 2
        ;;
      --allow-insecure-http)
        ALLOW_INSECURE_HTTP="true"
        shift
        ;;
      --setup-tls)
        SETUP_TLS=1
        shift
        ;;
      --overwrite-env)
        OVERWRITE_ENV=1
        shift
        ;;
      --help|-h)
        usage
        exit 0
        ;;
      *)
        fail "Неизвестный аргумент: $1"
        ;;
    esac
  done

  if [[ "$SETUP_TLS" -eq 1 ]]; then
    [[ -n "$DOMAIN" ]] || fail "Для --setup-tls требуется --domain"
    [[ -n "$TLS_EMAIL" ]] || fail "Для --setup-tls требуется --tls-email"
  fi
}

detect_os() {
  [[ -f /etc/os-release ]] || fail "Не найден /etc/os-release"
  # shellcheck disable=SC1091
  source /etc/os-release

  case "${ID:-}" in
    ubuntu|debian)
      log "Обнаружена ОС: ${PRETTY_NAME}"
      ;;
    *)
      fail "Поддерживаются только Debian/Ubuntu. Обнаружено: ${ID:-unknown}"
      ;;
  esac

  OS_ID="$ID"
  OS_VERSION_ID="$VERSION_ID"
}

install_base_packages() {
  log "Устанавливаю базовые пакеты"
  apt-get update
  DEBIAN_FRONTEND=noninteractive apt-get install -y \
    apt-transport-https \
    ca-certificates \
    curl \
    gnupg \
    jq \
    git \
    lsb-release \
    nginx \
    openssl \
    postgresql \
    postgresql-contrib \
    redis-server \
    rsync \
    unzip \
    certbot \
    python3-certbot-nginx
}

install_dotnet() {
  if dpkg -s dotnet-sdk-8.0 >/dev/null 2>&1 && dpkg -s dotnet-runtime-8.0 >/dev/null 2>&1; then
    log ".NET 8 уже установлен"
    return
  fi

  log "Устанавливаю Microsoft package feed и .NET 8"
  local pkg="/tmp/packages-microsoft-prod.deb"
  curl -fsSL "https://packages.microsoft.com/config/${OS_ID}/${OS_VERSION_ID}/packages-microsoft-prod.deb" -o "$pkg"
  dpkg -i "$pkg"
  rm -f "$pkg"
  apt-get update
  DEBIAN_FRONTEND=noninteractive apt-get install -y dotnet-sdk-8.0 aspnetcore-runtime-8.0
}

create_system_user_and_dirs() {
  log "Создаю system user и каталоги"

  if ! getent group "$APP_GROUP" >/dev/null 2>&1; then
    groupadd --system "$APP_GROUP"
  fi

  if ! id "$APP_USER" >/dev/null 2>&1; then
    useradd --system --gid "$APP_GROUP" --home-dir "$APP_ROOT" --shell /usr/sbin/nologin "$APP_USER"
  fi

  install -d -m 0755 -o root -g root "$APP_ROOT"
  install -d -m 0755 -o "$APP_USER" -g "$APP_GROUP" \
    "$APP_ROOT/server" \
    "$APP_ROOT/publish" \
    "$APP_ROOT/repo" \
    "$APP_ROOT/backups"
  install -d -m 0750 -o root -g "$APP_GROUP" "$ENV_DIR"
  install -d -m 0755 -o www-data -g www-data /var/www/certbot
}

configure_postgres() {
  log "Настраиваю PostgreSQL"
  systemctl enable postgresql
  systemctl start postgresql

  # Keep DB password and env in sync on repeated bootstrap runs.
  # If env already exists and caller did not request overwrite/new password,
  # reuse password from env instead of generating and applying a different one.
  if [[ -z "$DB_PASSWORD" ]]; then
    if [[ -f "$ENV_FILE" && "$OVERWRITE_ENV" -ne 1 ]]; then
      local existing_password
      existing_password="$(extract_env_connection_password "$ENV_FILE" || true)"
      if [[ -n "$existing_password" ]]; then
        DB_PASSWORD="$existing_password"
        log "Использую пароль PostgreSQL из существующего env-файла"
      else
        DB_PASSWORD="$(random_urlsafe 24)"
        log "В env нет ConnectionStrings__PostgreSQL/Password; сгенерирован новый пароль БД"
      fi
    else
      DB_PASSWORD="$(random_urlsafe 24)"
    fi
  fi

  local db_user_escaped db_password_escaped db_name_escaped
  db_user_escaped="$(sql_escape "$DB_USER")"
  db_password_escaped="$(sql_escape "$DB_PASSWORD")"
  db_name_escaped="$(sql_escape "$DB_NAME")"

  if ! sudo -u postgres psql -tAc "SELECT 1 FROM pg_roles WHERE rolname='${db_user_escaped}'" | grep -q 1; then
    sudo -u postgres psql -c "CREATE USER \"${DB_USER}\" WITH PASSWORD '${db_password_escaped}';"
  else
    sudo -u postgres psql -c "ALTER USER \"${DB_USER}\" WITH PASSWORD '${db_password_escaped}';"
  fi

  if ! sudo -u postgres psql -tAc "SELECT 1 FROM pg_database WHERE datname='${db_name_escaped}'" | grep -q 1; then
    sudo -u postgres psql -c "CREATE DATABASE \"${DB_NAME}\" OWNER \"${DB_USER}\";"
  fi
}

configure_redis() {
  log "Настраиваю Redis"
  systemctl enable redis-server
  systemctl start redis-server
  redis-cli ping >/dev/null
}

install_service_file() {
  [[ -f "$SERVICE_FILE_SRC" ]] || fail "Не найден service template: $SERVICE_FILE_SRC"

  log "Устанавливаю systemd unit"
  install -m 0644 "$SERVICE_FILE_SRC" "$SERVICE_FILE_DST"
  systemctl daemon-reload
  systemctl enable "$SERVICE_NAME"
}

create_env_file() {
  local jwt_key master_key admin_password observer_password player_password
  jwt_key="$(random_urlsafe 48)"
  master_key="$(random_b64 32)"
  admin_password="$(random_urlsafe 18)"
  observer_password="$(random_urlsafe 18)"
  player_password="$(random_urlsafe 18)"

  log "Создаю env-файл: $ENV_FILE"
  umask 027
  cat > "$ENV_FILE" <<EOF
TACID_JWT_SIGNING_KEY=${jwt_key}
TACID_MASTER_KEY_B64=${master_key}
TACID_ADMIN_LOGIN=admin
TACID_ADMIN_PASSWORD=${admin_password}
TACID_OBSERVER_LOGIN=observer
TACID_OBSERVER_PASSWORD=${observer_password}
TACID_PLAYER_LOGIN=player
TACID_PLAYER_PASSWORD=${player_password}
TACID_PLAYER_BEACON_ID=${PLAYER_BEACON_ID}
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://0.0.0.0:5001
TACID_ALLOW_INSECURE_HTTP=${ALLOW_INSECURE_HTTP}
ConnectionStrings__PostgreSQL=Host=localhost;Port=5432;Database=${DB_NAME};Username=${DB_USER};Password=${DB_PASSWORD}
Redis__ConnectionString=localhost:6379,abortConnect=false
Jwt__Issuer=tacid-server
Jwt__Audience=tacid-clients
EOF
  chown root:"$APP_GROUP" "$ENV_FILE"
  chmod 0640 "$ENV_FILE"

  cat <<EOF

Bootstrap secrets summary:
  PostgreSQL database: ${DB_NAME}
  PostgreSQL user:     ${DB_USER}
  PostgreSQL password: ${DB_PASSWORD}
  admin login:         admin
  admin password:      ${admin_password}
  observer login:      observer
  observer password:   ${observer_password}
  player login:        player
  player password:     ${player_password}
  player beacon id:    ${PLAYER_BEACON_ID}

Secrets stored in ${ENV_FILE}
EOF
}

ensure_env_file() {
  if [[ -f "$ENV_FILE" && "$OVERWRITE_ENV" -ne 1 ]]; then
    log "env-файл уже существует, не перезаписываю: $ENV_FILE"
    chown root:"$APP_GROUP" "$ENV_FILE"
    chmod 0640 "$ENV_FILE"
    return
  fi

  create_env_file
}

configure_nginx_and_tls() {
  [[ -f "$NGINX_TEMPLATE" ]] || fail "Не найден nginx template: $NGINX_TEMPLATE"

  log "Настраиваю nginx для домена $DOMAIN"
  cat > "$NGINX_SITE_DST" <<EOF
server {
    listen 80;
    server_name ${DOMAIN};

    location /.well-known/acme-challenge/ {
        root /var/www/certbot;
    }

    location / {
        return 200 'TLS bootstrap pending';
        add_header Content-Type text/plain;
    }
}
EOF
  ln -sf "$NGINX_SITE_DST" "$NGINX_SITE_LINK"
  rm -f /etc/nginx/sites-enabled/default

  nginx -t
  systemctl enable nginx
  systemctl restart nginx

  log "Получаю Let's Encrypt сертификат для $DOMAIN"
  certbot certonly --webroot -w /var/www/certbot \
    -d "$DOMAIN" \
    --non-interactive \
    --agree-tos \
    -m "$TLS_EMAIL" \
    --keep-until-expiring

  install -m 0644 "$NGINX_TEMPLATE" "$NGINX_SITE_DST"
  sed -i "s/tacid.example.com/${DOMAIN}/g" "$NGINX_SITE_DST"

  nginx -t
  systemctl reload nginx
}

maybe_start_service() {
  if [[ -f "$APP_ROOT/server/StrikeballServer.dll" ]]; then
    log "Найден опубликованный сервер, перезапускаю service"
    systemctl restart "$SERVICE_NAME"
    systemctl status "$SERVICE_NAME" --no-pager
  else
    log "Серверные бинарники ещё не развернуты в $APP_ROOT/server"
    log "Следующий шаг: запустить scripts/deploy_to_server.sh или scripts/deploy_from_user.sh"
  fi
}

main() {
  require_root
  parse_args "$@"
  detect_os
  install_base_packages
  install_dotnet
  create_system_user_and_dirs
  configure_postgres
  configure_redis
  install_service_file
  ensure_env_file

  if [[ "$SETUP_TLS" -eq 1 ]]; then
    configure_nginx_and_tls
  else
    log "TLS-настройка пропущена. Для HTTPS повторно запустите с --setup-tls --domain --tls-email"
  fi

  maybe_start_service

  cat <<EOF

Bootstrap complete.

Next steps:
  1. Разверните приложение в ${APP_ROOT}/server
  2. Проверьте env-файл: ${ENV_FILE}
  3. Запустите smoke-test после деплоя: ${REPO_ROOT}/scripts/smoke-test.sh

Useful commands:
  systemctl status ${SERVICE_NAME}
  journalctl -u ${SERVICE_NAME} -n 200 --no-pager
  redis-cli ping
  sudo -u postgres psql -d ${DB_NAME}
EOF
}

main "$@"