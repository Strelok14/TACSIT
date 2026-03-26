# Deploy from user (safe workflow)

This folder contains `deploy_from_user.sh` — a safe deployment helper for publishing the project as a normal user and atomically syncing files to `/opt/strikeball/server`.

It also contains `deploy_serverai_from_user.sh` — a safe deployment helper for `ServerAI` into `/opt/tacsit/ServerAI`.

Usage (on server as your normal user):

```bash
cd /home/youruser/TACSIT/scripts
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

## ServerAI deploy (Debian/Ubuntu)

Usage (on server as your normal user):

```bash
cd /home/youruser/TACSIT/scripts
./deploy_serverai_from_user.sh
```

What it does:
- Syncs `ServerAI/` sources to `/opt/tacsit/ServerAI` with `rsync`
- Ensures service user `serverai` exists
- Creates/updates `.venv` and installs `requirements.txt`
- Installs `/etc/systemd/system/serverai.service`
- Enables/restarts `serverai` and shows status

Notes:
- Requires `python3`, `python3-venv`, `rsync`, and `sudo`.
- If server path differs from `/opt/tacsit/ServerAI`, update `serverai.service` before deployment.
