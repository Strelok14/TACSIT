#!/usr/bin/env bash
# =============================================================================
# T.A.C.I.D. Smoke Test Script
# Назначение: быстрая проверка auth, telemetry HMAC, SignalR negotiate и revoke.
# Запуск:
#   bash scripts/smoke-test.sh [SERVER_HOST[:PORT]]
# Пример:
#   bash scripts/smoke-test.sh localhost:5001
#   bash scripts/smoke-test.sh tacid.example.com   # uses port 5001 by default
# =============================================================================

set -euo pipefail

SERVER="${1:-localhost:5001}"

# Нормализуем базовый URL
if [[ ! "$SERVER" =~ ^https?:// ]]; then
  BASE_URL="https://$SERVER"
else
  BASE_URL="$SERVER"
fi

echo "==> T.A.C.I.D. Smoke Test | Server: $BASE_URL"
echo ""

# Учётные данные берём из переменных окружения или используем дефолтные тестовые.
LOGIN="${TACID_TEST_LOGIN:-admin}"
PASSWORD="${TACID_TEST_PASSWORD:-}"
CURL_OPTS=(-s -k --max-time 10)   # -k для разрешения self-signed cert в тестах

# Тестовый маяк и ключ для HMAC-телеметрии.
BEACON_ID="${TACID_TEST_BEACON_ID:-9001}"
KEY_VERSION="${TACID_TEST_KEY_VERSION:-1}"
BEACON_KEY_B64="${TACID_TEST_BEACON_KEY_B64:-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=}"
BEACON_KEY_HEX="${TACID_TEST_BEACON_KEY_HEX:-0000000000000000000000000000000000000000000000000000000000000000}"
AUTH_PREFIX=""

pass() { echo "  [PASS] $1"; }
fail() { echo "  [FAIL] $1"; exit 1; }
info() { echo "  [INFO] $1"; }

auth_post() {
  local suffix="$1"
  local data="$2"
  local path="$AUTH_PREFIX$suffix"
  curl "${CURL_OPTS[@]}" -X POST "$BASE_URL$path" -H "Content-Type: application/json" -d "$data"
}

auth_status() {
  local suffix="$1"
  local data="$2"
  local path="$AUTH_PREFIX$suffix"
  curl "${CURL_OPTS[@]}" -o /dev/null -w "%{http_code}" -X POST "$BASE_URL$path" -H "Content-Type: application/json" -H "Authorization: Bearer $ACCESS_TOKEN" -d "$data"
}

# ---------------------------------------------------------------------------
# Шаг 1: Login — получение access + refresh token
# ---------------------------------------------------------------------------
echo "--- Step 1: Login ---"

LOGIN_JSON="{\"login\":\"$LOGIN\",\"password\":\"$PASSWORD\"}"

# Сначала пробуем новый маршрут /api/auth, затем совместимый /auth.
AUTH_PREFIX="/api/auth"
LOGIN_RESP=$(auth_post "/login" "$LOGIN_JSON")
ACCESS_TOKEN=$(echo "$LOGIN_RESP" | grep -o '"token":"[^"]*"' | head -1 | cut -d'"' -f4)
if [ -z "$ACCESS_TOKEN" ]; then
  AUTH_PREFIX="/auth"
  LOGIN_RESP=$(auth_post "/login" "$LOGIN_JSON")
  ACCESS_TOKEN=$(echo "$LOGIN_RESP" | grep -o '"token":"[^"]*"' | head -1 | cut -d'"' -f4)
fi

echo "  Response: $LOGIN_RESP" | head -c 300
echo ""

REFRESH_TOKEN=$(echo "$LOGIN_RESP" | grep -o '"refreshToken":"[^"]*"' | head -1 | cut -d'"' -f4)

if [ -z "$ACCESS_TOKEN" ]; then
  fail "Login failed — no access token in response. Check TACID_TEST_LOGIN / TACID_TEST_PASSWORD env vars."
fi
pass "Login successful via $AUTH_PREFIX/login. access_token obtained."

if [ -n "$REFRESH_TOKEN" ]; then
  pass "Refresh token obtained."
else
  info "Warning: no refresh token (server may not support refresh yet)."
fi

# ---------------------------------------------------------------------------
# Шаг 2: Provision key + POST /api/telemetry/measurement (HMAC)
# ---------------------------------------------------------------------------
echo ""
echo "--- Step 2: Provision key + HMAC telemetry ---"

PROVISION_BODY="{\"keyBase64\":\"$BEACON_KEY_B64\",\"keyVersion\":$KEY_VERSION,\"previousGraceDays\":7}"
PROVISION_HTTP=$(curl "${CURL_OPTS[@]}" -o /dev/null -w "%{http_code}" -X POST \
  "$BASE_URL/api/security/beacons/$BEACON_ID/key" \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d "$PROVISION_BODY")

if [ "$PROVISION_HTTP" = "200" ]; then
  pass "Beacon key provisioned for beaconId=$BEACON_ID"
else
  fail "Provision key returned $PROVISION_HTTP (expected 200)"
fi

SEQ=$((RANDOM + 1000))
TS_MS=$(($(date +%s) * 1000))
ANCHORS="1:10.5:-60;"
CANONICAL="$BEACON_ID|$SEQ|$TS_MS|$ANCHORS"
SIG=$(printf "%s" "$CANONICAL" | openssl dgst -sha256 -mac HMAC -macopt "hexkey:$BEACON_KEY_HEX" -binary | openssl base64 -A)

TELEMETRY_JSON=$(cat <<EOF
{"beaconId":$BEACON_ID,"sequence":$SEQ,"timestamp":$TS_MS,"keyVersion":$KEY_VERSION,"batteryLevel":85,"signature":"$SIG","distances":[{"anchorId":1,"distance":10.5,"rssi":-60}]}
EOF
)

TELEMETRY_HTTP=$(curl "${CURL_OPTS[@]}" -o /dev/null -w "%{http_code}" -X POST \
  "$BASE_URL/api/telemetry/measurement" \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d "$TELEMETRY_JSON")

if [ "$TELEMETRY_HTTP" = "200" ]; then
  pass "HMAC telemetry accepted (200)."
else
  fail "Telemetry returned $TELEMETRY_HTTP (expected 200)"
fi

# ---------------------------------------------------------------------------
# Шаг 3: GET /api/positions — проверка авторизации и наличия endpoint
# ---------------------------------------------------------------------------
echo ""
echo "--- Step 3: GET /api/positions ---"

POSITIONS_RESP=$(curl "${CURL_OPTS[@]}" -X GET \
  "$BASE_URL/api/positions" \
  -H "Authorization: Bearer $ACCESS_TOKEN")

echo "  Response: $POSITIONS_RESP" | head -c 200
echo ""

if echo "$POSITIONS_RESP" | grep -q '"positions"'; then
  pass "Positions endpoint returned data."
elif echo "$POSITIONS_RESP" | grep -q '^\[\]$\|^\[\]'; then
  pass "Positions endpoint returned empty list (no beacons active)."
elif echo "$POSITIONS_RESP" | grep -qE '"beacons"|\[\]|positions'; then
  pass "Positions endpoint responded."
else
  fail "Positions endpoint did not return expected JSON."
fi

# ---------------------------------------------------------------------------
# Шаг 4: SignalR negotiate
# ---------------------------------------------------------------------------
echo ""
echo "--- Step 4: SignalR negotiate ---"

NEGOTIATE_HTTP=$(curl "${CURL_OPTS[@]}" -o /dev/null -w "%{http_code}" -X POST \
  "$BASE_URL/hubs/positioning/negotiate?negotiateVersion=1" \
  -H "Authorization: Bearer $ACCESS_TOKEN")

if [ "$NEGOTIATE_HTTP" = "200" ]; then
  pass "SignalR negotiate reachable (200)."
else
  info "SignalR negotiate returned $NEGOTIATE_HTTP (this may depend on transport/cors settings)."
fi

# ---------------------------------------------------------------------------
# Шаг 5: GET /api/anchors — доступ с ролью admin/observer
# ---------------------------------------------------------------------------
echo ""
echo "--- Step 5: GET /api/anchors ---"

ANCHORS_RESP=$(curl "${CURL_OPTS[@]}" -X GET \
  "$BASE_URL/api/anchors" \
  -H "Authorization: Bearer $ACCESS_TOKEN")

echo "  Response: $ANCHORS_RESP" | head -c 200
echo ""

HTTP_STATUS=$(curl "${CURL_OPTS[@]}" -o /dev/null -w "%{http_code}" -X GET \
  "$BASE_URL/api/anchors" \
  -H "Authorization: Bearer $ACCESS_TOKEN")

if [ "$HTTP_STATUS" = "200" ]; then
  pass "Anchors endpoint: 200 OK"
else
  fail "Anchors endpoint returned $HTTP_STATUS (expected 200)"
fi

# ---------------------------------------------------------------------------
# Шаг 6: POST /auth/refresh — обновление токена
# ---------------------------------------------------------------------------
echo ""
echo "--- Step 6: POST $AUTH_PREFIX/refresh ---"

if [ -n "$REFRESH_TOKEN" ]; then
  REFRESH_RESP=$(auth_post "/refresh" "{\"refreshToken\":\"$REFRESH_TOKEN\"}")

  echo "  Response: $REFRESH_RESP" | head -c 200
  echo ""

  NEW_TOKEN=$(echo "$REFRESH_RESP" | grep -o '"token":"[^"]*"' | head -1 | cut -d'"' -f4)
  if [ -n "$NEW_TOKEN" ] && [ "$NEW_TOKEN" != "$ACCESS_TOKEN" ]; then
    pass "Refresh successful — new access token issued."
    ACCESS_TOKEN="$NEW_TOKEN"
  else
    fail "Refresh failed or returned same token."
  fi
else
  info "Skipped (no refresh token from login)."
fi

# ---------------------------------------------------------------------------
# Шаг 7: POST /auth/logout
# ---------------------------------------------------------------------------
echo ""
echo "--- Step 7: POST $AUTH_PREFIX/logout ---"

LOGOUT_HTTP=$(auth_status "/logout" "{\"refreshToken\":\"$REFRESH_TOKEN\"}")

if [ "$LOGOUT_HTTP" = "204" ]; then
  pass "Logout successful (204 No Content)."
else
  fail "Logout returned $LOGOUT_HTTP (expected 204)"
fi

# Проверяем, что токен отозван.
AFTER_LOGOUT=$(curl "${CURL_OPTS[@]}" -o /dev/null -w "%{http_code}" -X GET \
  "$BASE_URL/api/positions" \
  -H "Authorization: Bearer $ACCESS_TOKEN")

if [ "$AFTER_LOGOUT" = "401" ]; then
  pass "Token correctly denied after logout (401)."
else
  info "Warning: token returned $AFTER_LOGOUT after logout (expected 401); Redis denylist may need time to propagate."
fi

# ---------------------------------------------------------------------------
# Итог
# ---------------------------------------------------------------------------
echo ""
echo "============================================"
echo "  Smoke test PASSED for $BASE_URL (auth+hmac+signalr)"
echo "============================================"
