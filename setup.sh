#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OFFLINE_DIR="$ROOT_DIR/offline_deps"
DOTNET_LINUX_DIR="$OFFLINE_DIR/dotnet/linux-x64"
POSTGRES_LINUX_DIR="$OFFLINE_DIR/postgres/linux-x64"
POSTGRES_LINUX_SOURCE_DIR="$OFFLINE_DIR/postgres/linux-x64-source"
REDIS_LINUX_SOURCE_DIR="$OFFLINE_DIR/redis/linux-x64-source"
SERVER_DIR="$ROOT_DIR/Server"
ENV_FILE="${TACID_ENV_FILE:-$ROOT_DIR/server.local.env}"
APP_URL="${ASPNETCORE_URLS:-http://0.0.0.0:5001}"

require_path() {
  local path="$1"
  if [[ ! -e "$path" ]]; then
    echo "Missing required offline dependency: $path" >&2
    exit 1
  fi
}

require_path "$DOTNET_LINUX_DIR"
require_path "$OFFLINE_DIR/nuget"
require_path "$OFFLINE_DIR/postgres"
require_path "$OFFLINE_DIR/redis"

export DOTNET_ROOT="${DOTNET_ROOT:-$DOTNET_LINUX_DIR}"
export PATH="$DOTNET_ROOT:$PATH"
export ASPNETCORE_ENVIRONMENT="Local"
export ASPNETCORE_URLS="$APP_URL"
export TACID_ALLOW_INSECURE_HTTP="true"
export ConnectionStrings__PostgreSQL="${ConnectionStrings__PostgreSQL:-Host=127.0.0.1;Port=5432;Database=tacid_local_demo;Username=tacid;Password=tacid-demo}"
export Redis__ConnectionString="${Redis__ConnectionString:-127.0.0.1:6379,abortConnect=false}"
export Security__SecretStoreDirectory="${Security__SecretStoreDirectory:-$ROOT_DIR/App_Data/keys}"

mkdir -p "$ROOT_DIR/App_Data/keys" "$ROOT_DIR/App_Data/postgres" "$ROOT_DIR/App_Data/logs"

if [[ ! -f "$ENV_FILE" ]]; then
  cat > "$ENV_FILE" <<EOF
ASPNETCORE_ENVIRONMENT=Local
ASPNETCORE_URLS=$APP_URL
TACID_ALLOW_INSECURE_HTTP=true
ConnectionStrings__PostgreSQL=$ConnectionStrings__PostgreSQL
Redis__ConnectionString=$Redis__ConnectionString
Security__SecretStoreDirectory=$Security__SecretStoreDirectory
EOF
fi

if [[ -x "$POSTGRES_LINUX_DIR/bin/initdb" && ! -d "$ROOT_DIR/App_Data/postgres/base" ]]; then
  "$POSTGRES_LINUX_DIR/bin/initdb" -D "$ROOT_DIR/App_Data/postgres"
elif [[ -d "$POSTGRES_LINUX_SOURCE_DIR" ]]; then
  echo "PostgreSQL linux binaries not found; source is present at $POSTGRES_LINUX_SOURCE_DIR"
fi

if [[ -x "$OFFLINE_DIR/redis/linux-x64/redis-server" ]]; then
  nohup "$OFFLINE_DIR/redis/linux-x64/redis-server" --port 6379 --save "" --appendonly no > "$ROOT_DIR/App_Data/logs/redis.log" 2>&1 &
elif [[ -d "$REDIS_LINUX_SOURCE_DIR" ]]; then
  echo "Redis linux binaries not found; source is present at $REDIS_LINUX_SOURCE_DIR"
fi

if [[ -x "$POSTGRES_LINUX_DIR/bin/pg_ctl" ]]; then
  "$POSTGRES_LINUX_DIR/bin/pg_ctl" -D "$ROOT_DIR/App_Data/postgres" -l "$ROOT_DIR/App_Data/logs/postgres.log" start || true
fi

dotnet restore "$SERVER_DIR/StrikeballServer.csproj" --packages "$OFFLINE_DIR/nuget"
dotnet build "$SERVER_DIR/StrikeballServer.csproj" --no-restore

cat > "$ROOT_DIR/strikeball-local-demo.service" <<EOF
[Unit]
Description=T.A.C.I.D. GPS Local Demo
After=network.target

[Service]
WorkingDirectory=$ROOT_DIR
EnvironmentFile=$ENV_FILE
ExecStart=$DOTNET_ROOT/dotnet $SERVER_DIR/bin/Debug/net8.0/StrikeballServer.dll
Restart=always

[Install]
WantedBy=multi-user.target
EOF

echo "Offline setup completed. Service template: $ROOT_DIR/strikeball-local-demo.service"