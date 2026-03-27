#!/usr/bin/env bash
set -euo pipefail

# doctor.sh
# Быстрая проверка готовности сервера к запуску и smoke-test.
# Usage:
#   bash scripts/doctor.sh
#   bash scripts/doctor.sh --base-url http://localhost:5001
# --fix  # попытаться исправить найденные проблемы (дрейф пароля БД, остановленный сервис)

ENV_FILE="/etc/strikeball/environment"
BASE_URL="${BASE_URL:-http://localhost:5001}"
FIX_MODE=0
ERRORS=0
MISSING_ENV=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --base-url)
      BASE_URL="${2:-}"
      shift 2
      ;;
        --fix)
          FIX_MODE=1
          shift
          ;;
    *)
      echo "Unknown argument: $1"
      exit 1
      ;;
  esac
done

if command -v sudo >/dev/null 2>&1; then
  SUDO="sudo"
else
  SUDO=""
fi

pass() { echo "[PASS] $1"; }
warn() { echo "[WARN] $1"; }
fail() {
  echo "[FAIL] $1"
  if [[ "$FIX_MODE" -eq 0 ]]; then
    exit 1
  fi
  ERRORS=$(( ERRORS + 1 ))
}

read_env_value() {
  local key="$1"
  local line
  line="$($SUDO sed -n "s/^${key}=//p" "$ENV_FILE" | tail -n 1)"
  printf "%s" "$line"
}

echo "==> T.A.C.I.D Doctor$([ "$FIX_MODE" -eq 1 ] && echo ' [FIX MODE]' || true) | base URL: $BASE_URL"

[[ -f "$ENV_FILE" ]] || fail "env file not found: $ENV_FILE"
pass "env file exists: $ENV_FILE"

JWT_KEY="$(read_env_value TACID_JWT_SIGNING_KEY)"
MASTER_KEY="$(read_env_value TACID_MASTER_KEY_B64)"
ADMIN_LOGIN="$(read_env_value TACID_ADMIN_LOGIN)"
ADMIN_PASSWORD="$(read_env_value TACID_ADMIN_PASSWORD)"
PG_CONN="$(read_env_value ConnectionStrings__PostgreSQL)"
REDIS_CONN="$(read_env_value Redis__ConnectionString)"

[[ -n "$JWT_KEY" ]] || fail "TACID_JWT_SIGNING_KEY is empty"
[[ -n "$MASTER_KEY" ]] || fail "TACID_MASTER_KEY_B64 is empty"
[[ -n "$ADMIN_LOGIN" ]] || fail "TACID_ADMIN_LOGIN is empty"
[[ -n "$ADMIN_PASSWORD" ]] || fail "TACID_ADMIN_PASSWORD is empty"
[[ -n "$PG_CONN" ]] || fail "ConnectionStrings__PostgreSQL is empty"
[[ -n "$REDIS_CONN" ]] || fail "Redis__ConnectionString is empty"

if [[ -z "$JWT_KEY" || -z "$MASTER_KEY" || -z "$ADMIN_LOGIN" || -z "$ADMIN_PASSWORD" || -z "$PG_CONN" || -z "$REDIS_CONN" ]]; then
  MISSING_ENV=1
fi

if [[ "$MISSING_ENV" -eq 0 ]]; then
  pass "required env keys are set"
else
  warn "required env keys are missing"
fi

if [[ -n "$MASTER_KEY" ]]; then
  MASTER_LEN=$(printf "%s" "$MASTER_KEY" | base64 -d 2>/dev/null | wc -c | tr -d ' ')
  [[ "$MASTER_LEN" = "32" ]] || fail "TACID_MASTER_KEY_B64 must decode to 32 bytes, got $MASTER_LEN"
  pass "master key format is valid"
fi

if command -v redis-cli >/dev/null 2>&1; then
  if redis-cli ping >/dev/null 2>&1; then
    pass "redis ping"
  else
    if [[ "$FIX_MODE" -eq 1 ]]; then
      echo "[FIX] Trying to start redis..."
      $SUDO systemctl start redis-server 2>/dev/null || $SUDO systemctl start redis 2>/dev/null || true
      sleep 1
      if redis-cli ping >/dev/null 2>&1; then
        pass "redis started"
      else
        warn "redis ping still fails after start attempt"
      fi
    else
      warn "redis ping failed"
    fi
  fi
else
  warn "redis-cli not installed"
fi

if [[ "$MISSING_ENV" -eq 0 ]]; then
  DB_USER=$(printf "%s" "$PG_CONN" | sed -n 's/.*Username=\([^;]*\).*/\1/p')
  DB_PASS=$(printf "%s" "$PG_CONN" | sed -n 's/.*Password=\([^;]*\).*/\1/p')
  DB_NAME=$(printf "%s" "$PG_CONN" | sed -n 's/.*Database=\([^;]*\).*/\1/p')
  DB_HOST=$(printf "%s" "$PG_CONN" | sed -n 's/.*Host=\([^;]*\).*/\1/p')
  DB_PORT=$(printf "%s" "$PG_CONN" | sed -n 's/.*Port=\([^;]*\).*/\1/p')

  DB_HOST="${DB_HOST:-localhost}"
  DB_PORT="${DB_PORT:-5432}"

  if command -v psql >/dev/null 2>&1; then
    if PGPASSWORD="$DB_PASS" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -c "select 1" >/dev/null 2>&1; then
      pass "postgres credentials from env are valid"
    else
      if [[ "$FIX_MODE" -eq 0 ]]; then
        fail "postgres login failed for user '$DB_USER' using ConnectionStrings__PostgreSQL"
      else
        echo "[FIX] Syncing PostgreSQL password for '$DB_USER' to match env file..."
        ESCAPED_PASS="${DB_PASS//\'/\'\'}"
        if printf "ALTER USER \"%s\" WITH PASSWORD '%s';\n" "$DB_USER" "$ESCAPED_PASS" \
            | $SUDO -u postgres psql >/dev/null 2>&1; then
          if PGPASSWORD="$DB_PASS" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -c "select 1" >/dev/null 2>&1; then
            pass "postgres password synced — credentials are now valid"
          else
            fail "postgres login still failed after password sync"
          fi
        else
          fail "could not sync postgres password (need sudo / postgres superuser)"
        fi
      fi
    fi
  else
    warn "psql not installed"
  fi
else
  warn "skipping postgres checks because required env keys are missing"
fi

SERVICE_ACTIVE=0
if $SUDO systemctl is-active --quiet strikeball-server 2>/dev/null; then
  pass "service strikeball-server is active"
  SERVICE_ACTIVE=1
else
  if [[ "$FIX_MODE" -eq 1 ]]; then
    echo "[FIX] Starting strikeball-server..."
    $SUDO systemctl start strikeball-server 2>/dev/null || true
    sleep 3
    if $SUDO systemctl is-active --quiet strikeball-server 2>/dev/null; then
      pass "service strikeball-server started"
      SERVICE_ACTIVE=1
    else
      warn "service failed to start — check: journalctl -u strikeball-server -n 50"
      ERRORS=$(( ERRORS + 1 ))
    fi
  else
    warn "service strikeball-server is not active"
  fi
fi

if [[ "$SERVICE_ACTIVE" -eq 1 && "$MISSING_ENV" -eq 0 ]]; then
  LOGIN_JSON=$(printf '{"login":"%s","password":"%s"}' "$ADMIN_LOGIN" "$ADMIN_PASSWORD")
  LOGIN_RESP=$(curl -sS --max-time 10 -H "Content-Type: application/json" -d "$LOGIN_JSON" "$BASE_URL/api/auth/login" || true)
  if printf "%s" "$LOGIN_RESP" | grep -q '"success":true'; then
    pass "auth login works"
  else
    warn "auth login failed, response: $LOGIN_RESP"
  fi
elif [[ "$SERVICE_ACTIVE" -eq 1 ]]; then
  warn "skipping auth check because required env keys are missing"
else
  warn "skipping auth check — service is not running"
fi

echo ""
if [[ "$FIX_MODE" -eq 1 && "$ERRORS" -gt 0 ]]; then
  echo "Doctor completed with $ERRORS unresolved issue(s). Review [FAIL]/[WARN] messages above."
  exit 1
fi
echo "Doctor completed."
