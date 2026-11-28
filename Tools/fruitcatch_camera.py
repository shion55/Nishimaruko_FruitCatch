"""Shared camera selection, color verification and latest-frame capture."""

from __future__ import annotations

from collections import deque
import json
from pathlib import Path
import socket
import sys
import threading
import time

import cv2
import numpy as np

CAMERA_CONFIG_PATH = Path(__file__).with_name("camera_config.json")
COMMAND_PORT = 5006
PREVIEW_PORT = 8765
LAUNCHER_PORT = 8766
RESOLUTIONS = ((3840, 2160), (2560, 1440), (1920, 1080), (1600, 1200),
               (1280, 960), (1280, 720), (1024, 768), (800, 600), (640, 480))
MAX_RESOLUTIONS = ((7680, 4320), (5120, 2880), (4096, 2160), (3840, 2160), (3264, 2448),
                   (2592, 1944), (2560, 1920), (2560, 1440), (2048, 1536), (1920, 1200),
                   (1920, 1080), (1600, 1200), (1440, 1080), (1280, 1024), (1280, 960),
                   (1280, 720), (1024, 768), (800, 600), (640, 480))
FORMATS = ("AUTO", "MJPG", "YUY2", "UYVY", "DEFAULT")
FOURCC_ALIASES = {"YUY2": ("YUY2", "YUYV", "YUNV"), "MJPG": ("MJPG", "JPEG")}
DEFAULT_CAMERA_BACKEND = cv2.CAP_AVFOUNDATION if sys.platform == "darwin" else cv2.CAP_DSHOW
DEFAULTS = {
    "camera_name": "", "camera_index": 0, "camera_path": "", "camera_backend": DEFAULT_CAMERA_BACKEND,
    "use_max_resolution": False, "width": 1280, "height": 720, "fps": 30,
    "video_format": "AUTO", "tracking_mode": "pose", "monitor": 0,
}


def log(message):
    print(message, flush=True)


def load_camera_config(path=CAMERA_CONFIG_PATH):
    config = DEFAULTS.copy()
    if not path.is_file():
        return config
    try:
        loaded = json.loads(path.read_text(encoding="utf-8-sig"))
        if not isinstance(loaded, dict):
            raise ValueError("最上位はJSONオブジェクトにしてください")
        config.update(loaded)
        if sys.platform == "darwin":
            # DirectShow IDs and indices refer to devices on the Windows machine.
            if str(loaded.get("camera_backend")) != str(cv2.CAP_AVFOUNDATION):
                config.update(camera_name="", camera_index=0, camera_path="")
            config.update(camera_backend=cv2.CAP_AVFOUNDATION, video_format="DEFAULT", use_max_resolution=False)
        for key in ("width", "height", "fps", "camera_index", "camera_backend", "monitor"):
            config[key] = int(config[key])
        if (min(config["width"], config["height"], config["fps"]) <= 0 or
                config["camera_index"] < 0 or config["video_format"] not in FORMATS or
                config["tracking_mode"] not in ("pose", "face")):
            raise ValueError("解像度・FPS・カメラ番号・映像形式・追跡方式を確認してください")
    except (OSError, TypeError, ValueError) as exc:
        raise RuntimeError(f"camera_config.jsonを読み込めません: {exc}") from exc
    return config


def save_camera_config(config, path=CAMERA_CONFIG_PATH):
    temp = path.with_suffix(".tmp")
    temp.write_text(json.dumps(config, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temp.replace(path)


def discover_cameras(backend=DEFAULT_CAMERA_BACKEND):
    from cv2_enumerate_cameras import enumerate_cameras

    return list(enumerate_cameras(backend))


def resolve_camera(config, cameras):
    name = str(config.get("camera_name") or "").strip()
    path = str(config.get("camera_path") or "")
    if path:
        matches = [item for item in cameras if str(item.path or "").casefold() == path.casefold()]
    elif name:
        matches = [item for item in cameras if item.name.casefold() == name.casefold()]
    else:
        matches = [item for item in cameras if item.index == config["camera_index"]]
        if (not matches and cameras and config.get("camera_backend") == cv2.CAP_AVFOUNDATION
                and config["camera_index"] == 0):
            matches = cameras[:1]
    if len(matches) != 1:
        raise RuntimeError(f"指定カメラが見つからないか一意ではありません ({name or config['camera_index']})。"
                           "再検索してカメラを選択してください。")
    return matches[0]


def select_camera(config):
    selected = resolve_camera(config, discover_cameras(config["camera_backend"]))
    log(f"[camera] 選択: {selected.name} index={selected.index} backend={selected.backend}")
    return selected.index, selected.backend, selected.name


def camera_config_for(config, camera):
    return dict(config, camera_name=camera.name, camera_index=camera.index,
                camera_path=str(camera.path or ""), camera_backend=camera.backend)


def exclusive_socket(port, kind):
    sock = socket.socket(socket.AF_INET, kind)
    try:
        if hasattr(socket, "SO_EXCLUSIVEADDRUSE"):
            sock.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
        sock.bind(("127.0.0.1", port))
        return sock
    except OSError as exc:
        sock.close()
        raise RuntimeError(f"ローカルポート {port} を使用できません。"
                           f"FruitCatchやカメラ確認が起動中か確認してください: {exc}") from exc


def fourcc_name(cap):
    value = int(cap.get(cv2.CAP_PROP_FOURCC)) & 0xffffffff
    return "".join(chr((value >> shift) & 255) for shift in range(0, 32, 8)).strip("\0 ")


def frame_description(frame, fourcc):
    channels = frame.shape[2] if frame.ndim == 3 else 1
    return f"shape={frame.shape} dtype={frame.dtype} channels={channels} FOURCC={fourcc!r}"


def color_frame(raw, fourcc):
    if raw is None or raw.size == 0 or raw.dtype != np.uint8:
        raise ValueError("空または未対応のカメラフレーム")
    if raw.ndim == 3:
        channels = raw.shape[2]
        if channels == 3:
            return raw
        if channels == 4:
            return cv2.cvtColor(raw, cv2.COLOR_BGRA2BGR)
        if channels == 2 and fourcc in ("YUY2", "YUYV", "YUNV", "UYVY") and raw.shape[1] % 2 == 0:
            code = cv2.COLOR_YUV2BGR_UYVY if fourcc == "UYVY" else cv2.COLOR_YUV2BGR_YUY2
            return cv2.cvtColor(raw, code)
    if fourcc in ("MJPG", "JPEG") and raw.ndim <= 3 and (
            raw.ndim < 3 or raw.shape[2] == 1):
        data = raw.reshape(-1)
        if data.size > 2 and data[0] == 255 and data[1] == 216:
            decoded = cv2.imdecode(data, cv2.IMREAD_UNCHANGED)
            if decoded is not None and decoded.ndim == 3 and decoded.shape[2] == 3:
                return decoded
            if decoded is not None and decoded.ndim == 2:
                return cv2.cvtColor(decoded, cv2.COLOR_GRAY2BGR)
            raise ValueError("Invalid MJPG frame")
    # Packed byte streams have one row. A decoded grayscale image has real image rows.
    if raw.shape[0] > 1 and raw.shape[1] > 1 and (
            raw.ndim == 2 or (raw.ndim == 3 and raw.shape[2] == 1)):
        return cv2.cvtColor(raw, cv2.COLOR_GRAY2BGR)
    raise ValueError(f"カラーに変換できない形式: {frame_description(raw, fourcc)}")


def configure_camera(index, backend, name, config, stop_event=None, preferred=None, require_mode=False):
    avfoundation = backend == cv2.CAP_AVFOUNDATION
    if avfoundation:
        # Let AVFoundation negotiate a native mode with the selected device.
        require_mode = False
    elif preferred is not None:
        try:
            log(f"[camera] Trying preview mode: {preferred}")
            return configure_camera(index, backend, name, dict(config, **preferred,
                                    use_max_resolution=False), stop_event, require_mode=True)
        except RuntimeError as exc:
            log(f"[camera] Preview mode failed; trying configured modes: {exc}")
    use_max = config["use_max_resolution"] and not avfoundation
    resolutions = MAX_RESOLUTIONS if use_max else ((config["width"], config["height"]),)
    requested_format = "DEFAULT" if avfoundation else config["video_format"]
    formats = ("MJPG", "DEFAULT", "YUY2", "UYVY") if requested_format == "AUTO" else (requested_format,)
    last_error = ""
    best = None
    for width, height in resolutions:
        for video_format in formats:
            if stop_event is not None and stop_event.is_set():
                raise RuntimeError("カメラを停止しました")
            started = time.monotonic()
            mode = "AVFoundation (カメラの標準設定)" if avfoundation else f"{width}x{height}/{config['fps']}fps/{video_format}"
            log(f"[camera] opening {name} {mode}")
            cap = cv2.VideoCapture(index, backend)
            log(f"[camera] open {width}x{height}/{video_format}: {time.monotonic() - started:.2f}s")
            keep = False
            try:
                if not cap.isOpened():
                    raise ValueError("カメラを開けません (他のアプリが使用していないか確認してください)")
                if not avfoundation:
                    cap.set(cv2.CAP_PROP_CONVERT_RGB, 1)
                    cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)
                    if video_format != "DEFAULT":
                        cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*video_format))
                    cap.set(cv2.CAP_PROP_FRAME_WIDTH, width)
                    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
                    cap.set(cv2.CAP_PROP_FPS, config["fps"])
                for attempt in range(8):
                    if stop_event is not None and stop_event.is_set():
                        raise ValueError("カメラを停止しました")
                    ok, raw = cap.read()
                    if not ok or raw is None:
                        last_error = "フレーム取得に失敗しました"
                        continue
                    fourcc = fourcc_name(cap)
                    description = frame_description(raw, fourcc)
                    if attempt == 0 or not last_error:
                        log(f"[camera] {name} 要求={width}x{height}/{config['fps']}fps/{video_format} {description}")
                    if (video_format != "DEFAULT" and fourcc and
                            fourcc not in FOURCC_ALIASES.get(video_format, (video_format,))):
                        raise ValueError(f"指定形式 {video_format} を設定できません (実際 {fourcc})")
                    try:
                        frame = color_frame(raw, fourcc)
                    except ValueError as exc:
                        last_error = str(exc)
                        continue
                    actual_height, actual_width = frame.shape[:2]
                    actual_fps = cap.get(cv2.CAP_PROP_FPS)
                    if require_mode and ((actual_width, actual_height) != (width, height) or
                                         (actual_fps > 0 and abs(actual_fps - config["fps"]) > 0.5) or
                                         (video_format != "DEFAULT" and not fourcc)):
                        raise ValueError(f"Preview mode did not negotiate: {description} fps={actual_fps}")
                    area = actual_width * actual_height
                    if use_max and (actual_width, actual_height) != (width, height):
                        if best is None or area > best[0]:
                            best = area, width, height, video_format
                        break
                    if use_max and best is not None and best[0] > area:
                        break
                    log(f"[camera] frame validated in {time.monotonic() - started:.2f}s; "
                        f"{actual_width}x{actual_height} driver FPS={actual_fps:.1f} {description}")
                    keep = True
                    return cap
                else:
                    raise ValueError(last_error)
            except (ValueError, cv2.error) as exc:
                last_error = str(exc)
                log(f"[camera] {name} {video_format}/{width}x{height}: {last_error}")
            finally:
                if not keep:
                    cap.release()
        if best is not None and best[0] >= width * height:
            break
    if best is not None:
        _, width, height, mode = best
        return configure_camera(index, backend, name, dict(config, use_max_resolution=False,
                                width=width, height=height, video_format=mode), stop_event)
    raise RuntimeError(f"{name}: {last_error}")


class Rate:
    def __init__(self):
        self.times = deque()
        self.lock = threading.Lock()

    def tick(self, now):
        with self.lock:
            self.times.append(now)
            self._trim(now)

    def _trim(self, now):
        while self.times and now - self.times[0] > 2:
            self.times.popleft()

    def value(self):
        with self.lock:
            self._trim(time.monotonic())
            n = len(self.times)
            return (n - 1) / max(0.001, self.times[-1] - self.times[0]) if n > 1 else 0.0


class LatestCamera:
    """Only the capture thread opens, reads and releases the device."""

    def __init__(self, open_camera):
        self.open_camera = open_camera
        self.condition = threading.Condition()
        self.stop_event = threading.Event()
        self.frame = None
        self.sequence = 0
        self.captured_at = 0.0
        self.error = None
        self.fourcc = ""
        self.mode = None
        self.rate = Rate()
        self.thread = threading.Thread(target=self._capture, name="FruitCatchCamera", daemon=True)

    def start(self):
        self.thread.start()

    def _capture(self):
        while not self.stop_event.is_set():
            cap = None
            try:
                cap = self.open_camera(self.stop_event)
                fourcc = fourcc_name(cap)
                fps = cap.get(cv2.CAP_PROP_FPS)
                first_frame = True
                failures = 0
                while not self.stop_event.is_set():
                    ok, raw = cap.read()
                    captured_at = time.monotonic()
                    if not ok or raw is None:
                        failures += 1
                        if failures >= 3:
                            raise RuntimeError(f"カメラフレームが連続して取得できません (FOURCC={fourcc})")
                        self.stop_event.wait(0.02)
                        continue
                    failures = 0
                    frame = color_frame(raw, fourcc)
                    if first_frame:
                        height, width = frame.shape[:2]
                        video_format = next((key for key in FORMATS[1:-1]
                                             if fourcc in FOURCC_ALIASES.get(key, (key,))), None)
                        if video_format is None:
                            video_format = fourcc if len(fourcc) == 4 and fourcc.isprintable() else "DEFAULT"
                        mode = dict(width=width, height=height, video_format=video_format)
                        if np.isfinite(fps) and fps > 0:
                            mode["fps"] = fps
                        log(f"[camera] First live frame: {mode}")
                        first_frame = False
                    self.rate.tick(captured_at)
                    with self.condition:
                        if self.error:
                            log("[camera] カラー映像の取得が復旧しました")
                        self.error = None
                        self.fourcc = fourcc
                        self.mode = mode
                        self.frame = frame
                        self.captured_at = captured_at
                        self.sequence += 1
                        self.condition.notify_all()
            except Exception as exc:
                if not self.stop_event.is_set():
                    log(f"[camera] {exc}。同じカメラを再接続します。")
                    with self.condition:
                        self.frame = None
                        self.mode = None
                        self.error = str(exc)
                        self.condition.notify_all()
            finally:
                if cap is not None:
                    cap.release()
            self.stop_event.wait(1)

    def next_frame(self, after_sequence, timeout=0.05):
        with self.condition:
            self.condition.wait_for(lambda: self.sequence > after_sequence and self.frame is not None
                                    or self.stop_event.is_set(), timeout=timeout)
            return self.latest(after_sequence)

    def latest(self, after_sequence=0):
        with self.condition:
            if (self.frame is None or self.sequence <= after_sequence or
                    time.monotonic() - self.captured_at > 0.5):
                return None
            return self.sequence, self.frame, self.captured_at

    def close(self, timeout=2):
        self.stop_event.set()
        with self.condition:
            self.condition.notify_all()
        if self.thread.ident is not None:
            self.thread.join(timeout)
        return not self.thread.is_alive()
