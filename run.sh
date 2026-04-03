#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export DOTNET_ROOT="${DOTNET_ROOT:-$ROOT_DIR/offline_deps/dotnet/linux-x64}"
export PATH="$DOTNET_ROOT:$PATH"
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Local}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://0.0.0.0:5001}"
export TACID_ALLOW_INSECURE_HTTP="true"

"$DOTNET_ROOT/dotnet" "$ROOT_DIR/Server/bin/Debug/net8.0/StrikeballServer.dll"