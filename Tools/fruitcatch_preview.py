"""Encode only the latest camera frame and keep the registration image separately."""

from __future__ import annotations

from dataclasses import dataclass
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
import socket
import threading
import time
from urllib.parse import parse_qs, urlsplit
from uuid import uuid4

import cv2

from fruitcatch_camera import Rate, log

MIRROR_IMAGE = True
PREVIEW_FPS = 30
PREVIEW_MAX_WIDTH = 1280
PREVIEW_IDLE_SECONDS = 1.0
JPEG_QUALITY = 73


def resize_to_max_width(frame, max_width):
    if frame.shape[1] <= max_width:
        return frame
    scale = max_width / frame.shape[1]
    return cv2.resize(frame, (max_width, max(1, round(frame.shape[0] * scale))),
                      interpolation=cv2.INTER_AREA)


def mirrored(frame):
    return cv2.flip(frame, 1) if MIRROR_IMAGE else frame


def encode_preview(frame):
    ok, result = cv2.imencode(".jpg", mirrored(resize_to_max_width(frame, PREVIEW_MAX_WIDTH)),
                              [cv2.IMWRITE_JPEG_QUALITY, JPEG_QUALITY])
    if not ok:
        raise RuntimeError("プレビュー画像のJPEG圧縮に失敗しました")
    return result.tobytes()


@dataclass(frozen=True)
class PreviewStatus:
    registration_id: str = ""
    progress: float = 0.0
    hint: str = "WAITING"
    hands_raised_at: float | None = None


class PreviewStream:
    def __init__(self, camera):
        self.camera = camera
        self.lock = threading.Lock()
        self.id = uuid4().hex
        self.token = os.environ.get("FRUITCATCH_LAUNCH_TOKEN", "")
        self.last_requested_at = float("-inf")
        self.latest_jpeg = None
        self.status = PreviewStatus()
        self.pending_snapshot = None
        self.snapshot_jpeg = None
        self.snapshot_failed = False
        self.jpeg_rate = Rate()
        self.inference_rate = Rate()
        self.stop_event = threading.Event()
        self.thread = threading.Thread(target=self._encode, name="FruitCatchJPEG", daemon=True)

    def publish(self, status, snapshot=None, *, reset=False):
        with self.lock:
            if reset or status.registration_id != self.status.registration_id:
                self.pending_snapshot = self.snapshot_jpeg = None
                self.snapshot_failed = False
            self.status = status
            if snapshot is not None:
                self.pending_snapshot = (status.registration_id, snapshot)

    def is_requested(self):
        with self.lock:
            return time.monotonic() - self.last_requested_at < PREVIEW_IDLE_SECONDS

    def request_frame(self):
        with self.lock:
            self.last_requested_at = time.monotonic()
            status = self.status
            if self.snapshot_failed:
                status = PreviewStatus(status.registration_id, 0, "SNAPSHOT_ERROR")
            return self.latest_jpeg, status, self.snapshot_jpeg is not None

    def registration_image(self, registration_id):
        with self.lock:
            if self.snapshot_jpeg is not None and self.snapshot_jpeg[0] == registration_id:
                return self.snapshot_jpeg[1]
            return None

    def _encode(self):
        sequence = 0
        deadline = 0.0
        while not self.stop_event.is_set():
            with self.lock:
                pending = self.pending_snapshot
            if pending is not None:
                registration_id, frame = pending
                jpeg = self._try_encode(frame)
                with self.lock:
                    if self.pending_snapshot is pending:
                        self.pending_snapshot = None
                        if jpeg is not None:
                            self.snapshot_jpeg = (registration_id, jpeg)
                        else:
                            self.snapshot_failed = True
            if not self.is_requested():
                self.stop_event.wait(0.025)
                deadline = 0.0
                continue
            now = time.monotonic()
            if now < deadline:
                self.stop_event.wait(deadline - now)
                continue
            captured = self.camera.next_frame(sequence, timeout=0.03)
            if captured is None:
                continue
            sequence, frame, captured_at = captured
            deadline = max(deadline + 1 / PREVIEW_FPS, time.monotonic()) if deadline else now + 1 / PREVIEW_FPS
            jpeg = self._try_encode(frame)
            if jpeg is not None:
                with self.lock:
                    self.latest_jpeg = sequence, jpeg, captured_at
                self.jpeg_rate.tick(time.monotonic())

    @staticmethod
    def _try_encode(frame):
        try:
            return encode_preview(frame)
        except (cv2.error, RuntimeError) as exc:
            log(f"[jpeg] {exc}")
            return None

    def metrics(self):
        return {"capture_fps": round(self.camera.rate.value(), 1),
                "inference_fps": round(self.inference_rate.value(), 1),
                "jpeg_fps": round(self.jpeg_rate.value(), 1)}


class PreviewServer(ThreadingHTTPServer):
    # Windows SO_REUSEADDR allows overlapping listeners, including unrelated processes.
    allow_reuse_address = False

    def server_bind(self):
        if hasattr(socket, "SO_EXCLUSIVEADDRUSE"):
            self.socket.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
        else:
            # Allow immediate restart after HTTP connections close on POSIX.
            self.socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        super().server_bind()


def make_preview_handler(stream):
    class PreviewHandler(BaseHTTPRequestHandler):
        def do_GET(self):
            url = urlsplit(self.path)
            query = parse_qs(url.query)
            if url.path == "/health":
                error = stream.camera.error
                ready = stream.camera.latest() is not None and not error
                data = json.dumps(dict(ready=ready, error=error, token=stream.token,
                                       **stream.metrics()), ensure_ascii=False).encode("utf-8")
                self.respond(200, data, "application/json; charset=utf-8")
            elif url.path == "/registration.jpg":
                registration_id = query.get("id", [""])[0]
                jpeg = stream.registration_image(registration_id)
                if jpeg is None:
                    self.respond(404)
                else:
                    self.respond(200, jpeg, headers={"X-Registration-Id": registration_id})
            elif url.path == "/frame.jpg":
                latest, state, registered = stream.request_frame()
                now = time.monotonic()
                if latest is None or now - latest[2] > 0.5 or stream.camera.error:
                    self.respond(503, headers={"X-Hint": "CAMERA_ERROR" if stream.camera.error else "OFFLINE"})
                    return
                sequence, jpeg, captured_at = latest
                headers = {
                    "X-Stream-Id": stream.id,
                    "X-Frame-Sequence": str(sequence),
                    "X-Frame-Age-Ms": f"{(now - captured_at) * 1000:.1f}",
                    "X-Progress": f"{state.progress:.3f}",
                    "X-Hint": state.hint,
                    "X-Registered": "1" if registered else "0",
                    "X-Hands-Raised": "1" if (state.hands_raised_at is not None and
                                             now - state.hands_raised_at < 0.5) else "0",
                    "X-Registration-Id": state.registration_id,
                }
                headers.update({f"X-{key.replace('_', '-')}": str(value)
                                for key, value in stream.metrics().items()})
                if query.get("after") == [f"{stream.id}-{sequence}"]:
                    jpeg = b""
                self.respond(200, jpeg, headers=headers)
            else:
                self.respond(404)

        def respond(self, status, data=b"", content_type="image/jpeg", headers=None):
            try:
                self.send_response(status)
                self.send_header("Content-Type", content_type)
                self.send_header("Content-Length", str(len(data)))
                self.send_header("Cache-Control", "no-store")
                for key, value in (headers or {}).items():
                    self.send_header(key, value)
                self.end_headers()
                self.wfile.write(data)
            except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
                pass

        def log_message(self, format, *args):
            pass

    return PreviewHandler
