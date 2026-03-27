# ServerAI

`ServerAI` is a standalone Python MVP service for person detection over WebRTC.

## Stack

- Python 3.11
- aiohttp WebSocket signaling
- aiortc for WebRTC transport
- ultralytics + torch (CPU) for YOLO inference
- av + numpy for frame handling

## Signaling

- WebSocket: `ws://SERVER_IP:8080/ws`
- Android sends a WebRTC offer over WebSocket
- Server responds with a WebRTC answer
- Detection payloads are sent back over the `detections` DataChannel

## Quick Start

```bash
python -m venv .venv
. .venv/bin/activate
pip install -r requirements.txt
python server.py
```

## Detection Payload

```json
{
  "type": "detections",
  "ts_ms": 1712345678123,
  "frame_w": 1280,
  "frame_h": 720,
  "detections": [
    {"cls": "person", "conf": 0.81, "x1": 120, "y1": 55, "x2": 330, "y2": 610}
  ]
}
```

## Runtime Notes

- Default model: `yolov8n.pt`
- Default signaling port: `8080`
- Default detection interval: `50 ms`
- Trickle ICE is optional; this MVP works in non-trickle mode by default

Environment variables:

- `SERVER_AI_HOST=0.0.0.0`
- `SERVER_AI_PORT=8080`
- `YOLO_MODEL=yolov8n.pt`
- `YOLO_CONFIDENCE=0.35`
- `DETECTION_INTERVAL_MS=50`

## Debian 13 systemd setup

### One-command deploy (recommended)

If this repository is already on the target Linux host, use:

```bash
cd /path/to/TACSIT/scripts
./deploy_serverai_from_user.sh
```

This script syncs files into `/opt/tacsit/ServerAI`, updates the virtual environment, installs `serverai.service`, and restarts the service.

### Manual setup

1. Install Python venv tooling and runtime deps:

```bash
sudo apt update
sudo apt install -y python3 python3-venv python3-pip
```

2. Copy project to target host, for example:

```bash
sudo mkdir -p /opt/tacsit
sudo rsync -av ./ServerAI/ /opt/tacsit/ServerAI/
```

3. Create a dedicated system user:

```bash
sudo useradd --system --home /opt/tacsit/ServerAI --shell /usr/sbin/nologin serverai || true
sudo chown -R serverai:serverai /opt/tacsit/ServerAI
```

4. Create virtual environment and install dependencies:

```bash
cd /opt/tacsit/ServerAI
sudo -u serverai python3 -m venv .venv
sudo -u serverai .venv/bin/pip install --upgrade pip
sudo -u serverai .venv/bin/pip install -r requirements.txt
```

5. Install systemd unit from template:

```bash
sudo cp /opt/tacsit/ServerAI/serverai.service /etc/systemd/system/serverai.service
sudo systemctl daemon-reload
sudo systemctl enable --now serverai
```

6. Verify service health:

```bash
sudo systemctl status serverai --no-pager
journalctl -u serverai -f
curl http://127.0.0.1:8080/
```

If your deployment path is not `/opt/tacsit/ServerAI`, update `WorkingDirectory` and `ExecStart` in `serverai.service` before copying it into `/etc/systemd/system/`.