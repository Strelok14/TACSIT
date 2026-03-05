# Deploy from user (safe workflow)

This folder contains `deploy_from_user.sh` — a safe deployment helper for publishing the project as a normal user and atomically syncing files to `/opt/strikeball/server`.

Usage (on server as your normal user):

```bash
cd /home/youruser/TACSIT/StrikeballServer/scripts
./deploy_from_user.sh
```

What it does:
- Builds the project from `Server/` with `dotnet publish` into `~/publish` (no sudo required)
- Stops the `strikeball-server` systemd service
- Uses `rsync` as root to copy files into `/opt/strikeball/server`
- Fixes ownership to `strikeball:strikeball` and secure permissions
- Starts the service and shows status

Notes:
- Requires `rsync` and `dotnet` installed and available in PATH.
- The `systemd` service name is `strikeball-server`.
- This keeps the publish step non-root and limits privileged operations to a single atomic copy.
