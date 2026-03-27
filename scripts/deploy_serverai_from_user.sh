#!/usr/bin/env bash
set -euo pipefail

# deploy_serverai_from_user.sh
# Безопасный деплой ServerAI:
# - синхронизирует исходники в /opt/tacsit/ServerAI
# - создает/обновляет venv
# - устанавливает Python-зависимости
# - устанавливает и перезапускает systemd unit serverai

if [ "$#" -ne 0 ]; then
  echo "Usage: ./deploy_serverai_from_user.sh"
  exit 1
fi

for cmd in sudo rsync python3; do
  if ! command -v "$cmd" >/dev/null 2>&1; then
    echo "Error: required command not found: $cmd"
    exit 1
  fi
done

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC_DIR="$REPO_ROOT/ServerAI"
TARGET_DIR="/opt/tacsit/ServerAI"
SERVICE_NAME="serverai"
SERVICE_USER="serverai"
SERVICE_GROUP="serverai"

if [ ! -f "$SRC_DIR/server.py" ]; then
  echo "Error: cannot find ServerAI sources at $SRC_DIR"
  exit 1
fi

echo "Source dir: $SRC_DIR"
echo "Target dir: $TARGET_DIR"

echo "1) Ensure target directory and service account"
sudo mkdir -p "$TARGET_DIR"
if ! id "$SERVICE_USER" >/dev/null 2>&1; then
  sudo useradd --system --home "$TARGET_DIR" --shell /usr/sbin/nologin "$SERVICE_USER"
fi

echo "2) Sync source files to target"
sudo rsync -a --delete \
  --exclude ".venv" \
  --exclude "__pycache__" \
  --exclude ".pytest_cache" \
  --exclude "*.pyc" \
  "$SRC_DIR/" "$TARGET_DIR/"

echo "3) Fix ownership"
sudo chown -R "$SERVICE_USER:$SERVICE_GROUP" "$TARGET_DIR"

echo "4) Create/update virtual environment"
if [ ! -x "$TARGET_DIR/.venv/bin/python" ]; then
  sudo -u "$SERVICE_USER" python3 -m venv "$TARGET_DIR/.venv"
fi

echo "5) Install Python dependencies"
sudo -u "$SERVICE_USER" "$TARGET_DIR/.venv/bin/pip" install --upgrade pip
sudo -u "$SERVICE_USER" "$TARGET_DIR/.venv/bin/pip" install -r "$TARGET_DIR/requirements.txt"

echo "6) Install and restart systemd unit"
sudo cp "$TARGET_DIR/serverai.service" "/etc/systemd/system/$SERVICE_NAME.service"
sudo systemctl daemon-reload
sudo systemctl enable "$SERVICE_NAME"
sudo systemctl restart "$SERVICE_NAME"

echo "7) Service status"
sudo systemctl status "$SERVICE_NAME" --no-pager

echo "8) Health check"
if command -v curl >/dev/null 2>&1; then
  curl -fsS "http://127.0.0.1:8080/" || true
else
  echo "curl not found; skip health check"
fi

echo "Done. Logs: sudo journalctl -u $SERVICE_NAME -n 200 --no-pager"