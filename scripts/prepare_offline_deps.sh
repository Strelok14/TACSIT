#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TARGET_DIR="$ROOT_DIR/offline_deps"

mkdir -p "$TARGET_DIR/nuget"
dotnet restore "$ROOT_DIR/Server/StrikeballServer.csproj" --packages "$TARGET_DIR/nuget"

echo "Скопируйте вручную в $TARGET_DIR:"
echo "  1. .NET 8 runtime для Linux/Windows"
echo "  2. PostgreSQL binaries"
echo "  3. Redis binaries"
echo "  4. osm_tiles для офлайн-карты"
echo "После этого перенесите папку offline_deps на офлайн-машину."