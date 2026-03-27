import asyncio
import json
import logging
import os
import time
from typing import Any

from aiohttp import WSMsgType, web
from aiortc import RTCConfiguration, RTCIceCandidate, RTCPeerConnection, RTCSessionDescription
from aiortc.sdp import candidate_from_sdp
from ultralytics import YOLO


LOGGER = logging.getLogger("serverai")
logging.basicConfig(
    level=os.getenv("LOG_LEVEL", "INFO").upper(),
    format="%(asctime)s %(levelname)s %(name)s %(message)s",
)

SERVER_HOST = os.getenv("SERVER_AI_HOST", "0.0.0.0")
SERVER_PORT = int(os.getenv("SERVER_AI_PORT", "8080"))
YOLO_MODEL = os.getenv("YOLO_MODEL", "yolov8n.pt")
YOLO_CONFIDENCE = float(os.getenv("YOLO_CONFIDENCE", "0.35"))
DETECTION_INTERVAL_MS = int(os.getenv("DETECTION_INTERVAL_MS", "50"))

_model: YOLO | None = None
_model_lock = asyncio.Lock()
_inference_lock = asyncio.Lock()


async def get_model() -> YOLO:
    global _model

    if _model is None:
        async with _model_lock:
            if _model is None:
                LOGGER.info("Loading YOLO model: %s", YOLO_MODEL)
                _model = await asyncio.to_thread(YOLO, YOLO_MODEL)
    return _model


def run_inference_sync(model: YOLO, frame_bgr) -> dict[str, Any]:
    frame_h, frame_w = frame_bgr.shape[:2]
    result = model.predict(
        source=frame_bgr,
        classes=[0],
        conf=YOLO_CONFIDENCE,
        imgsz=640,
        device="cpu",
        verbose=False,
    )[0]

    detections: list[dict[str, Any]] = []
    for box in result.boxes:
        xyxy = box.xyxy[0].tolist()
        detections.append(
            {
                "cls": "person",
                "conf": round(float(box.conf[0]), 4),
                "x1": int(xyxy[0]),
                "y1": int(xyxy[1]),
                "x2": int(xyxy[2]),
                "y2": int(xyxy[3]),
            }
        )

    return {
        "type": "detections",
        "ts_ms": int(time.time() * 1000),
        "frame_w": frame_w,
        "frame_h": frame_h,
        "detections": detections,
    }


async def detect_people(frame_bgr) -> dict[str, Any]:
    model = await get_model()
    async with _inference_lock:
        return await asyncio.to_thread(run_inference_sync, model, frame_bgr)


def parse_ice_candidate(payload: dict[str, Any]) -> RTCIceCandidate:
    candidate = candidate_from_sdp(payload["candidate"])
    candidate.sdpMid = payload.get("sdpMid")
    candidate.sdpMLineIndex = payload.get("sdpMLineIndex")
    return candidate


async def wait_for_ice_gathering_complete(peer_connection: RTCPeerConnection, timeout_seconds: float = 5.0) -> None:
    deadline = time.monotonic() + timeout_seconds
    while peer_connection.iceGatheringState != "complete" and time.monotonic() < deadline:
        await asyncio.sleep(0.1)


async def forward_detections(track, get_channel) -> None:
    last_inference_at = 0.0

    while True:
        frame = await track.recv()
        now = time.monotonic()
        if (now - last_inference_at) * 1000 < DETECTION_INTERVAL_MS:
            continue

        last_inference_at = now
        frame_bgr = frame.to_ndarray(format="bgr24")
        payload = await detect_people(frame_bgr)
        channel = get_channel()
        if channel is None or channel.readyState != "open":
            continue

        try:
            channel.send(json.dumps(payload))
        except Exception:
            LOGGER.exception("Failed to send detections over DataChannel")


async def websocket_handler(request: web.Request) -> web.WebSocketResponse:
    websocket = web.WebSocketResponse(heartbeat=20)
    await websocket.prepare(request)

    peer_connection = RTCPeerConnection(RTCConfiguration(iceServers=[]))
    request.app["peer_connections"].add(peer_connection)
    channel_ref: dict[str, Any] = {"channel": None}
    tasks: list[asyncio.Task[Any]] = []

    LOGGER.info("Signaling client connected")

    @peer_connection.on("datachannel")
    def on_datachannel(channel) -> None:
        LOGGER.info("DataChannel received: %s", channel.label)
        channel_ref["channel"] = channel

    @peer_connection.on("track")
    def on_track(track) -> None:
        LOGGER.info("Incoming track kind=%s", track.kind)
        if track.kind == "video":
            tasks.append(asyncio.create_task(forward_detections(track, lambda: channel_ref["channel"])))

    @peer_connection.on("iceconnectionstatechange")
    async def on_iceconnectionstatechange() -> None:
        LOGGER.info("ICE state changed: %s", peer_connection.iceConnectionState)
        if peer_connection.iceConnectionState in {"failed", "closed", "disconnected"}:
            await peer_connection.close()

    try:
        async for message in websocket:
            if message.type != WSMsgType.TEXT:
                if message.type == WSMsgType.ERROR:
                    LOGGER.warning("WebSocket error: %s", websocket.exception())
                continue

            payload = json.loads(message.data)
            message_type = payload.get("type")

            if message_type == "offer":
                offer = RTCSessionDescription(sdp=payload["sdp"], type="offer")
                await peer_connection.setRemoteDescription(offer)
                answer = await peer_connection.createAnswer()
                await peer_connection.setLocalDescription(answer)
                await wait_for_ice_gathering_complete(peer_connection)
                await websocket.send_json(
                    {
                        "type": "answer",
                        "sdp": peer_connection.localDescription.sdp,
                    }
                )
                continue

            if message_type == "candidate":
                await peer_connection.addIceCandidate(parse_ice_candidate(payload))
                continue

            if message_type == "ping":
                await websocket.send_json({"type": "pong"})
                continue

            await websocket.send_json({"type": "error", "message": f"Unsupported message type: {message_type}"})
    except Exception as exception:
        LOGGER.exception("Signaling session crashed")
        if not websocket.closed:
            await websocket.send_json({"type": "error", "message": str(exception)})
    finally:
        for task in tasks:
            task.cancel()
        if tasks:
            await asyncio.gather(*tasks, return_exceptions=True)
        await peer_connection.close()
        request.app["peer_connections"].discard(peer_connection)
        LOGGER.info("Signaling client disconnected")

    return websocket


async def health_handler(_: web.Request) -> web.Response:
    return web.json_response({"status": "ok", "port": SERVER_PORT})


async def on_shutdown(app: web.Application) -> None:
    peer_connections = list(app["peer_connections"])
    for peer_connection in peer_connections:
        await peer_connection.close()
    app["peer_connections"].clear()


def create_app() -> web.Application:
    app = web.Application()
    app["peer_connections"] = set()
    app.router.add_get("/", health_handler)
    app.router.add_get("/ws", websocket_handler)
    app.on_shutdown.append(on_shutdown)
    return app


if __name__ == "__main__":
    web.run_app(create_app(), host=SERVER_HOST, port=SERVER_PORT)