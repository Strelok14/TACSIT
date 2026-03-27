#!/usr/bin/env bash
set -euo pipefail

# This script used to perform destructive operations (DB drop/recreate).
# Use bootstrap + deploy_from_user flow instead.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

cat <<'EOF'
[deploy.sh] This script is deprecated and now acts as a safe wrapper.

Recommended deployment flow:
  1) sudo ./scripts/bootstrap_server.sh
  2) ./scripts/deploy_from_user.sh
  3) ./scripts/doctor.sh --base-url http://localhost:5001
  4) ./scripts/smoke-test.sh http://localhost:5001

Run bootstrap now? [y/N]
EOF

read -r answer
if [[ "${answer:-}" =~ ^[Yy]$ ]]; then
  sudo "$SCRIPT_DIR/scripts/bootstrap_server.sh"
  echo "Bootstrap completed. Continue with deploy_from_user.sh as regular user."
else
  echo "No changes applied."
fi
