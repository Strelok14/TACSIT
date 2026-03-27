#!/usr/bin/env bash
set -euo pipefail

# deploy_from_user.sh
# Безопасный deploy: публикуем из-под обычного пользователя в домашнюю папке,
# затем синхронизируем в /opt/strikeball/server под sudo и перезапускаем systemd.

if [ "$#" -ne 0 ]; then
  echo "Usage: ./deploy_from_user.sh"
  exit 1
fi

USER_HOME=$(eval echo "~$USER")
PUBLISH_DIR="$USER_HOME/publish"
SRC_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/Server"
TARGET_DIR="/opt/strikeball/server"
SERVICE_NAME="strikeball-server"

# If running as root or sudo is unavailable, execute privileged commands directly.
if command -v sudo >/dev/null 2>&1; then
  SUDO="sudo"
elif [[ "${EUID}" -eq 0 ]]; then
  SUDO=""
else
  echo "Error: 'sudo' is not installed and script is not running as root."
  echo "Install sudo or run this script as root."
  exit 1
fi

echo "Source dir: $SRC_DIR"
echo "Publish dir: $PUBLISH_DIR"
echo "Target dir: $TARGET_DIR"

echo "1) Clean previous publish"
rm -rf "$PUBLISH_DIR"
mkdir -p "$PUBLISH_DIR"

echo "2) Build and publish (as current user)"
cd "$SRC_DIR"
dotnet restore
dotnet publish -c Release -o "$PUBLISH_DIR"

echo "3) Stop service (allow atomic replacement)"
${SUDO} systemctl stop "$SERVICE_NAME" || true

echo "4) Sync publish -> $TARGET_DIR"
${SUDO} rsync -a --delete "$PUBLISH_DIR/" "$TARGET_DIR/"

echo "5) Fix ownership & permissions"
${SUDO} chown -R strikeball:strikeball "$TARGET_DIR"
${SUDO} chmod -R u+rwX,g+rX,o-rwx "$TARGET_DIR"

echo "6) Start service"
${SUDO} systemctl start "$SERVICE_NAME"
${SUDO} systemctl status "$SERVICE_NAME" --no-pager

echo "Done. If anything failed, inspect journal: ${SUDO:+sudo }journalctl -u $SERVICE_NAME -n 200 --no-pager"
