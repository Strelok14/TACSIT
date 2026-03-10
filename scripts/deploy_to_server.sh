#!/usr/bin/env bash
set -euo pipefail

# Simple deploy script to run on the target Linux server.
# Usage: sudo ./deploy_to_server.sh [repo_url] [branch]
# Example: sudo ./deploy_to_server.sh https://github.com/Strelok14/TACSIT.git main

REPO_URL=${1:-https://github.com/Strelok14/TACSIT.git}
BRANCH=${2:-main}

WORKDIR=/opt/strikeball
REPO_DIR="$WORKDIR/repo"
PUBLISH_DIR="$WORKDIR/publish"
TARGET_DIR="$WORKDIR/server"
SERVICE_NAME=strikeball-server

echo "Deploying repo $REPO_URL (branch $BRANCH) to $TARGET_DIR"

# Ensure base dirs
sudo mkdir -p "$WORKDIR"
sudo chown "$USER":"$USER" "$WORKDIR"

if [ ! -d "$REPO_DIR/.git" ]; then
  echo "Cloning repository..."
  git clone --branch "$BRANCH" "$REPO_URL" "$REPO_DIR"
else
  echo "Updating repository..."
  git -C "$REPO_DIR" fetch --all --prune
  git -C "$REPO_DIR" checkout "$BRANCH"
  git -C "$REPO_DIR" pull origin "$BRANCH"
fi

echo "Restoring and publishing .NET project"
pushd "$REPO_DIR/Server" >/dev/null

# Restore and publish release build
dotnet restore
dotnet publish -c Release -o "$PUBLISH_DIR"
popd >/dev/null

echo "Backing up current target (if exists)"
if [ -d "$TARGET_DIR" ]; then
  ts=$(date -u +%Y%m%dT%H%M%SZ)
  sudo mv "$TARGET_DIR" "${TARGET_DIR}_bak_$ts"
fi

echo "Deploying publish output to $TARGET_DIR"
sudo mkdir -p "$TARGET_DIR"
sudo rsync -a --delete "$PUBLISH_DIR/" "$TARGET_DIR/"

# Ensure permissions: keep service user if exists
if id strikeball >/dev/null 2>&1; then
  sudo chown -R strikeball: "$TARGET_DIR"
fi

echo "Restarting systemd service $SERVICE_NAME"
sudo systemctl daemon-reload || true
sudo systemctl restart "$SERVICE_NAME"
sudo systemctl status "$SERVICE_NAME" --no-pager

echo "Last 60 journal lines for $SERVICE_NAME:"
sudo journalctl -u "$SERVICE_NAME" -n 60 --no-pager

echo "Deploy finished."
