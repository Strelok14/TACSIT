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

# --- Redis: auto-build from source if prebuilt binary is missing ---
REDIS_BIN="$OFFLINE_DIR/redis/linux-x64/redis-server"
if [[ ! -x "$REDIS_BIN" ]]; then
  REDIS_SRC_DIR=""
  # Find the extracted source directory (redis-x.y.z/)
  for d in "$REDIS_LINUX_SOURCE_DIR"/redis-*/; do
    [[ -d "$d" ]] && REDIS_SRC_DIR="$d" && break
  done

  if [[ -n "$REDIS_SRC_DIR" ]]; then
    echo "Building Redis from source: $REDIS_SRC_DIR"
    if ! command -v make &>/dev/null || ! command -v gcc &>/dev/null; then
      echo "ERROR: 'make' and 'gcc' are required to build Redis from source." >&2
      echo "  Install them: apt install -y build-essential" >&2
      exit 1
    fi
    ( cd "$REDIS_SRC_DIR" && make -j"$(nproc)" )
    mkdir -p "$OFFLINE_DIR/redis/linux-x64"
    cp "$REDIS_SRC_DIR/src/redis-server" "$REDIS_BIN"
    echo "Redis built OK: $REDIS_BIN"
  else
    echo "ERROR: No Redis binary and no source found." >&2
    echo "  Run download_offline_deps.ps1 on an online machine first." >&2
    exit 1
  fi
fi

nohup "$REDIS_BIN" --port 6379 --save "" --appendonly no > "$ROOT_DIR/App_Data/logs/redis.log" 2>&1 &
echo "Redis started (pid $!)"

# --- PostgreSQL: prebuilt binaries or system-installed ---
PG_CTL=""
if [[ -x "$POSTGRES_LINUX_DIR/bin/pg_ctl" ]]; then
  PG_CTL="$POSTGRES_LINUX_DIR/bin/pg_ctl"
  PG_INITDB="$POSTGRES_LINUX_DIR/bin/initdb"
elif command -v pg_ctlcluster &>/dev/null; then
  echo "Using system PostgreSQL (pg_ctlcluster). Skipping initdb — manage via systemd."
  PG_CTL=""
elif command -v pg_ctl &>/dev/null; then
  PG_CTL="$(command -v pg_ctl)"
  PG_INITDB="$(command -v initdb)"
else
  echo "ERROR: PostgreSQL binaries not found." >&2
  echo "" >&2
  echo "  Option A (recommended, needs any internet/mirror):" >&2
  echo "    apt install -y postgresql-16" >&2
  echo "" >&2
  echo "  Option B (full offline, download .deb on Windows):" >&2
  echo "    1. On Windows, open https://apt.postgresql.org in a browser." >&2
  echo "    2. Download postgresql-16, libpq5, and dependencies into a folder." >&2
  echo "    3. Copy folder to this machine." >&2
  echo "    4. dpkg -i *.deb" >&2
  echo "" >&2
  echo "  Then re-run: ./setup.sh" >&2
  exit 1
fi

if [[ -n "$PG_CTL" ]]; then
  if [[ ! -d "$ROOT_DIR/App_Data/postgres/base" ]]; then
    "$PG_INITDB" -D "$ROOT_DIR/App_Data/postgres"
  fi
  "$PG_CTL" -D "$ROOT_DIR/App_Data/postgres" -l "$ROOT_DIR/App_Data/logs/postgres.log" start || true
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