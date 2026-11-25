"""カメラに映る1人の顔位置を追跡する。本人照合・表情推定は行わない。

PythonがWebカメラを1台だけ開き、Unityのプレビューと顔追跡に共用する。

Unity -> Python  UDP 5006: AUTO (3秒自動登録), REGISTER (従来の即時登録), CLEAR, PING
Python -> Unity  UDP 5015: 顔のX座標 (従来どおり "0.5123")
Python -> Unity  UDP 5007: 登録・追跡状態 (REGISTERED / TRACKING / LOSTなど)
Python -> Unity  HTTP 127.0.0.1:8765/frame.jpg: JPEGおよび進捗ヘッダー

Unity側で円形UI Maskを使い、画像の中央だけを丸く切り抜いて表示する。
カメラ画像を顔へ自動追従して切り抜くと位置合わせが無意味になるため、
登録プレビューは常に固定中心で切り抜く。3秒の位置合わせ後に操作を有効にする。
撮影は最新1枚だけを保持し、JPEG圧縮はHTTPスレッドで行う。

依存: py -m pip install mediapipe opencv-contrib-python numpy
初回起動に限り、公式配布モデルをダウンロードする。
"""

from __future__ import annotations

from dataclasses import dataclass
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import json
import socket
import math
import threading
import time
import urllib.request

import cv2
import numpy as np

# ====== カメラ・通信 ======
CAMERA_CONFIG_PATH = Path(__file__).resolve().parent / "camera_config.json"
UNITY_IP = "127.0.0.1"
UNITY_POSITION_PORT = 5015
PYTHON_COMMAND_PORT = 5006
UNITY_STATUS_PORT = 5007
PREVIEW_HOST = "127.0.0.1"
PREVIEW_PORT = 8765

MIRROR_IMAGE = True
MIN_FACE_SIZE_PX = 30
ANALYSIS_MAX_WIDTH = 640
PREVIEW_MAX_WIDTH = 1280
REQUESTED_CAMERA_FPS = 30
PREVIEW_IDLE_SECONDS = 1.0  # PreparationからのHTTP取得が止まったら姿勢推定も休止。
POSE_FPS = 10
JPEG_QUALITY = 73
SHOW_DEBUG_WINDOW = False  # デバッグが必要な場合のみTrueにする

# DirectShowで一般的な高解像度モード。上から試し、実際に取得できた最大サイズを使う。
RESOLUTION_CANDIDATES = (
    (7680, 4320),
    (5120, 2880),
    (4096, 2160),
    (3840, 2160),
    (3264, 2448),
    (2592, 1944),
    (2560, 1920),
    (2560, 1440),
    (2048, 1536),
    (1920, 1200),
    (1920, 1080),
    (1600, 1200),
    (1440, 1080),
    (1280, 1024),
    (1280, 960),
    (1280, 720),
    (1024, 768),
    (800, 600),
    (640, 480),
)

# ====== 両手上げ判定 ======
HAND_RAISE_SHOULDER_MARGIN = 0.03  # 手首が肩より画面高の3%以上上
MIN_POSE_VISIBILITY = 0.55

# ====== 位置合わせ・登録 ======
HOLD_SECONDS = 3.0
DETECTION_GRACE_SECONDS = 0.25
CENTER_TOLERANCE_X = 0.12  # 元画像の中心から±12%
CENTER_TOLERANCE_Y = 0.13
MIN_FACE_WIDTH_RATIO = 0.21
MAX_FACE_WIDTH_RATIO = 0.49

MODEL_DIR = Path(__file__).resolve().parent / "face_models"
FACE_DETECTOR_PATH = MODEL_DIR / "face_detection_yunet_2023mar.onnx"
POSE_PATH = MODEL_DIR / "pose_landmarker_lite.task"
MODEL_URLS = {
    FACE_DETECTOR_PATH:
        "https://media.githubusercontent.com/media/opencv/opencv_zoo/main/models/face_detection_yunet/face_detection_yunet_2023mar.onnx",
    POSE_PATH:
        "https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_lite/float16/1/pose_landmarker_lite.task",
}


@dataclass
class Face:
    rect: tuple[int, int, int, int]
    center: tuple[float, float]


@dataclass
class TrackingState:
    pending_register: bool = False
    auto_register: bool = False
    active: bool = False
    hold_start: float | None = None
    progress: float = 0.0
    hint: str = "WAITING"
    last_status: str = "IDLE"
    registration_id: str = ""
    guide_region: tuple[float, float, float, float] | None = None
    hold_elapsed: float = 0.0
    hold_last_valid: float | None = None
    missing_since: float | None = None

    def reset_hold(self, hint: str) -> None:
        self.hold_start = None
        self.hold_elapsed = 0.0
        self.hold_last_valid = None
        self.missing_since = None
        self.progress = 0.0
        self.hint = hint

    def clear(self) -> None:
        self.registration_id = ""
        self.guide_region = None
        self.pending_register = False
        self.auto_register = False
        self.active = False
        self.reset_hold("WAITING")
        self.last_status = "IDLE"


class LatestCamera:
    """推論中も撮影を続け、未処理フレームを蓄積せず最新1枚で上書きする。"""

    def __init__(self, cap) -> None:
        self.cap = cap
        self.condition = threading.Condition()
        self.stop_event = threading.Event()
        self.frame: np.ndarray | None = None
        self.sequence = 0
        self.captured_at = 0.0
        self.finished = False
        self.error: Exception | None = None
        self.thread = threading.Thread(target=self._capture, name="FruitCatchCamera", daemon=True)

    def start(self) -> None:
        self.thread.start()

    def _capture(self) -> None:
        try:
            while not self.stop_event.is_set():
                ok, frame = self.cap.read()
                if not ok or frame is None or frame.size == 0:
                    raise RuntimeError("カメラフレームの取得に失敗しました")
                captured_at = time.monotonic()
                with self.condition:
                    self.frame = frame
                    self.captured_at = captured_at
                    self.sequence += 1
                    self.condition.notify_all()
        except Exception as exc:
            self.error = exc
        finally:
            self.cap.release()
            with self.condition:
                self.finished = True
                self.condition.notify_all()

    def next_frame(self, after_sequence: int, timeout: float = 0.05):
        with self.condition:
            self.condition.wait_for(
                lambda: self.sequence > after_sequence or self.finished, timeout=timeout)
            if self.finished:
                raise RuntimeError("カメラの撮影が停止しました") from self.error
            if self.sequence <= after_sequence:
                return None
            return self.sequence, self.frame, self.captured_at

    def close(self) -> None:
        self.stop_event.set()
        if self.thread.ident is None:
            self.cap.release()
            return
        # VideoCaptureのreadとreleaseは同じスレッドで実行する。
        self.thread.join(timeout=2.0)


class PreviewBuffer:
    """画像と同じ時点の進捗をHTTPへ渡す。画像は公開後に変更しない。"""

    def __init__(self) -> None:
        self.lock = threading.Lock()
        self.frame: np.ndarray | None = None
        self.last_requested_at: float | None = None
        self.progress = 0.0
        self.hint = "WAITING"
        self.registered = False
        self.hands_raised = False
        self.registration_id = ""

    def update(self, frame: np.ndarray, progress: float, hint: str,
               registered: bool, hands_raised: bool, registration_id: str = "") -> None:
        with self.lock:
            self.frame = frame
            self.progress = progress
            self.hint = hint
            self.registered = registered
            self.hands_raised = hands_raised
            self.registration_id = registration_id

    def request(self) -> None:
        with self.lock:
            self.last_requested_at = time.monotonic()

    def is_requested(self) -> bool:
        with self.lock:
            return (self.last_requested_at is not None and
                    time.monotonic() - self.last_requested_at < PREVIEW_IDLE_SECONDS)

    def snapshot(self) -> tuple[np.ndarray | None, float, str, bool, bool, str]:
        with self.lock:
            return self.frame, self.progress, self.hint, self.registered, self.hands_raised, self.registration_id


def make_preview_handler(shared: PreviewBuffer):
    class PreviewHandler(BaseHTTPRequestHandler):
        def do_GET(self):
            if self.path.split("?")[0] != "/frame.jpg":
                self.send_error(404)
                return

            shared.request()
            frame, progress, hint, registered, hands_raised, registration_id = shared.snapshot()
            if frame is None:
                self.send_response(503)
                self.send_header("Content-Length", "0")
                self.end_headers()
                return

            # JPEG圧縮を追跡ループから分離。取得がない本編では圧縮しない。
            preview = resize_to_max_width(frame, PREVIEW_MAX_WIDTH)
            if MIRROR_IMAGE:
                preview = cv2.flip(preview, 1)
            success, encoded = cv2.imencode(".jpg", preview,
                                            [cv2.IMWRITE_JPEG_QUALITY, JPEG_QUALITY])
            if not success:
                self.send_error(503, "Preview encoding failed")
                return
            jpeg = encoded.tobytes()

            self.send_response(200)
            self.send_header("Content-Type", "image/jpeg")
            self.send_header("Content-Length", str(len(jpeg)))
            self.send_header("Cache-Control", "no-store, no-cache, must-revalidate")
            self.send_header("X-Progress", f"{progress:.3f}")
            self.send_header("X-Hint", hint)
            self.send_header("X-Registered", "1" if registered else "0")
            self.send_header("X-Hands-Raised", "1" if hands_raised else "0")
            self.send_header("X-Registration-Id", registration_id)
            self.end_headers()
            try:
                self.wfile.write(jpeg)
            except (BrokenPipeError, ConnectionResetError):
                pass

        def log_message(self, format, *args):
            return  # 1秒に十数件のHTTPアクセスログを抑制

    return PreviewHandler


def load_camera_config() -> dict:
    config = {
        "camera_name": "",
        "camera_index": 0,
        "use_max_resolution": False,
        "width": 1280,
        "height": 720,
    }
    if not CAMERA_CONFIG_PATH.is_file():
        print(f"カメラ設定がないため初期値を使用します: {CAMERA_CONFIG_PATH}")
        return config

    try:
        loaded = json.loads(CAMERA_CONFIG_PATH.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError) as exc:
        raise RuntimeError(f"camera_config.jsonを読み込めません: {exc}") from exc
    if not isinstance(loaded, dict):
        raise RuntimeError("camera_config.jsonの最上位はJSONオブジェクトにしてください")
    config.update(loaded)
    return config


def select_camera(config: dict) -> tuple[int, int, str]:
    requested_name = str(config.get("camera_name") or "").strip()
    fallback_index = int(config.get("camera_index", 0))
    try:
        from cv2_enumerate_cameras import enumerate_cameras
        cameras = enumerate_cameras(cv2.CAP_DSHOW)
    except (ImportError, NotImplementedError) as exc:
        if requested_name:
            raise RuntimeError(
                "型番指定にはcv2-enumerate-camerasが必要です。Setup FruitCatch.batを再実行してください"
            ) from exc
        print(f"カメラ名を列挙できないため番号{fallback_index}を使用します: {exc}")
        return fallback_index, cv2.CAP_DSHOW, f"Camera {fallback_index}"

    if not cameras:
        raise RuntimeError("Windowsに認識されているカメラがありません")

    print("検出したカメラ:")
    for camera in cameras:
        print(f"  [{camera.index}] {camera.name}")

    if requested_name:
        key = requested_name.casefold()
        exact = [camera for camera in cameras if camera.name.casefold() == key]
        matches = exact or [camera for camera in cameras if key in camera.name.casefold()]
        if not matches:
            available = ", ".join(camera.name for camera in cameras)
            raise RuntimeError(f"指定したカメラ '{requested_name}' が見つかりません。検出結果: {available}")
        if len(matches) > 1:
            print(f"同名候補が{len(matches)}台あるため、先頭のカメラを使用します")
        selected = matches[0]
    else:
        selected = next((camera for camera in cameras if camera.index == fallback_index), cameras[0])

    print(f"選択したカメラ: [{selected.index}] {selected.name}")
    return selected.index, selected.backend, selected.name


def read_camera_frame(cap, attempts: int = 3) -> np.ndarray | None:
    frame = None
    for _ in range(attempts):
        ok, candidate = cap.read()
        if ok and candidate is not None and candidate.size > 0:
            frame = candidate
    return frame


def set_capture_mode(cap, width: int, height: int) -> np.ndarray | None:
    cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*"MJPG"))
    cap.set(cv2.CAP_PROP_FPS, REQUESTED_CAMERA_FPS)
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, width)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
    return read_camera_frame(cap)


def configure_camera(index: int, backend: int, name: str, config: dict):
    cap = cv2.VideoCapture(index, backend)
    if not cap.isOpened():
        raise RuntimeError(f"カメラを開けません: {name} (index={index})")

    use_max = bool(config.get("use_max_resolution", False))
    best = None
    if use_max:
        for requested_width, requested_height in RESOLUTION_CANDIDATES:
            frame = set_capture_mode(cap, requested_width, requested_height)
            if frame is None:
                continue
            actual_height, actual_width = frame.shape[:2]
            candidate = (actual_width * actual_height, actual_width, actual_height,
                         requested_width, requested_height)
            if best is None or candidate[:3] > best[:3]:
                best = candidate
            if actual_width == requested_width and actual_height == requested_height:
                break
    else:
        requested_width = max(1, int(config.get("width", 1280)))
        requested_height = max(1, int(config.get("height", 720)))
        frame = set_capture_mode(cap, requested_width, requested_height)
        if frame is not None:
            actual_height, actual_width = frame.shape[:2]
            best = (actual_width * actual_height, actual_width, actual_height,
                    requested_width, requested_height)

    if best is None:
        cap.release()
        raise RuntimeError(f"カメラ映像を取得できません: {name}")

    _, _, _, requested_width, requested_height = best
    frame = set_capture_mode(cap, requested_width, requested_height)
    if frame is None:
        cap.release()
        raise RuntimeError(f"選択した解像度で映像を取得できません: {name}")

    actual_height, actual_width = frame.shape[:2]
    actual_fps = cap.get(cv2.CAP_PROP_FPS)
    print(f"撮影モード: {actual_width}x{actual_height} / {actual_fps:.1f}fps")
    return cap


def resize_to_max_width(frame: np.ndarray, max_width: int) -> np.ndarray:
    if frame.shape[1] <= max_width:
        return frame
    scale = max_width / frame.shape[1]
    size = (max_width, max(1, int(round(frame.shape[0] * scale))))
    return cv2.resize(frame, size, interpolation=cv2.INTER_AREA)


def ensure_models() -> None:
    MODEL_DIR.mkdir(parents=True, exist_ok=True)
    for target, url in MODEL_URLS.items():
        if target.is_file() and target.stat().st_size > 100_000:
            continue
        print(f"モデルのダウンロード: {target.name}")
        temp = target.with_name(target.name + ".download")
        try:
            with urllib.request.urlopen(url, timeout=60) as response, temp.open("wb") as out:
                while chunk := response.read(1024 * 1024):
                    out.write(chunk)
            if temp.stat().st_size < 100_000:
                raise RuntimeError(f"モデルが小さすぎます: {target.name}")
            temp.replace(target)
        except Exception:
            temp.unlink(missing_ok=True)
            print(f"手動でモデルを取得し、{target} に保存してください:\n{url}")
            raise


def collect_faces(detections: np.ndarray | None, width: int, height: int) -> list[Face]:
    """YuNetの矩形だけを使用する。本人照合用の特徴抽出は不要。"""
    faces = []
    if detections is None:
        return faces
    for row in detections:
        if not np.all(np.isfinite(row[:4])):
            continue
        x, y, box_width, box_height = row[:4]
        x0, y0 = int(np.clip(x, 0, width)), int(np.clip(y, 0, height))
        x1 = int(np.clip(x + box_width, 0, width))
        y1 = int(np.clip(y + box_height, 0, height))
        w, h = x1 - x0, y1 - y0
        if min(w, h) < MIN_FACE_SIZE_PX:
            continue
        faces.append(Face(
            rect=(x0, y0, w, h),
            center=((x0 + x1) / 2.0, (y0 + y1) / 2.0),
        ))
    return faces


def detect_faces(detector, frame: np.ndarray) -> list[Face]:
    height, width = frame.shape[:2]
    detector.setInputSize((width, height))
    _, detections = detector.detect(frame)
    return collect_faces(detections, width, height)


def send_status(sock: socket.socket, state: TrackingState, status: str, force: bool = False) -> None:
    if status == state.last_status and not force:
        return
    state.last_status = status
    sock.sendto(status.encode("ascii"), (UNITY_IP, UNITY_STATUS_PORT))
    print("STATE:", status)


def read_commands(command_sock: socket.socket, state: TrackingState, status_sock: socket.socket) -> None:
    while True:
        try:
            data, _ = command_sock.recvfrom(256)
        except BlockingIOError:
            break
        except OSError as exc:
            print("コマンド受信エラー:", exc)
            break
        command = data.decode("utf-8", errors="replace").strip().upper()
        if command.startswith("AUTO_REGION "):
            parts = command.split()
            try:
                if len(parts) != 6 or len(parts[1]) != 32 or any(c not in "0123456789ABCDEF" for c in parts[1]):
                    raise ValueError("Invalid registration id")
                region = tuple(float(value) for value in parts[2:])
                x, y, w, h = region
                if (not all(math.isfinite(v) for v in region) or
                        x < 0 or y < 0 or w <= 0 or h <= 0 or x + w > 1.001 or y + h > 1.001):
                    raise ValueError("Guide must be inside the preview")
            except ValueError:
                send_status(status_sock, state, "INVALID_REGION", force=True)
                continue
            # Retries acknowledge the same request without restarting the hold timer.
            if state.registration_id == parts[1]:
                continue
            state.clear()
            state.registration_id = parts[1]
            state.guide_region = region
            state.pending_register = True
            state.auto_register = True
            state.hint = "NO_FACE"
            send_status(status_sock, state, "ALIGNING", force=True)
        elif command in ("AUTO", "REGISTER"):
            state.clear()
            state.pending_register = True
            state.auto_register = command == "AUTO"
            state.hint = "NO_FACE"
            send_status(status_sock, state, "ALIGNING" if state.auto_register else "REGISTERING", force=True)
        elif command == "CLEAR":
            state.clear()
            send_status(status_sock, state, "CLEARED", force=True)
        elif command == "PING":
            send_status(status_sock, state, "READY", force=True)
        else:
            send_status(status_sock, state, "UNKNOWN_COMMAND", force=True)


def check_alignment(face: Face, frame_width: int, frame_height: int,
                    region: tuple[float, float, float, float] | None = None) -> str:
    """案内枠内に顔の中心があるかを確認する。顔の表示サイズは問わない。"""
    cx = face.center[0] / frame_width
    cy = face.center[1] / frame_height
    if region is not None:
        x, y, w, h = region
        return "HOLD" if x <= cx <= x + w and y <= cy <= y + h else "MOVE_CENTER"

    width_ratio = face.rect[2] / frame_width
    if width_ratio < MIN_FACE_WIDTH_RATIO:
        return "MOVE_CLOSER"
    if width_ratio > MAX_FACE_WIDTH_RATIO:
        return "MOVE_BACK"
    if abs(cx - 0.5) > CENTER_TOLERANCE_X or abs(cy - 0.5) > CENTER_TOLERANCE_Y:
        return "MOVE_CENTER"
    return "HOLD"


def has_raised_both_hands(pose_landmarks: list) -> bool:
    """画面内の誰か1人が両手首を両肩より上に上げているかを返す。"""
    for points in pose_landmarks:
        if len(points) <= 16:
            continue
        left_shoulder, right_shoulder = points[11], points[12]
        left_wrist, right_wrist = points[15], points[16]
        visible = min(
            getattr(left_shoulder, "visibility", 1.0),
            getattr(right_shoulder, "visibility", 1.0),
            getattr(left_wrist, "visibility", 1.0),
            getattr(right_wrist, "visibility", 1.0),
        )
        if visible < MIN_POSE_VISIBILITY:
            continue
        if (left_wrist.y < left_shoulder.y - HAND_RAISE_SHOULDER_MARGIN and
                right_wrist.y < right_shoulder.y - HAND_RAISE_SHOULDER_MARGIN):
            return True
    return False


def complete_registration(
    state: TrackingState,
    status_sock: socket.socket,
) -> None:
    state.active = True
    state.pending_register = False
    state.auto_register = False
    state.hold_start = None
    state.progress = 1.0
    state.hint = "DONE"
    send_status(status_sock, state, "REGISTERED", force=True)


def process_registration(
    state: TrackingState,
    frame: np.ndarray,
    faces: list[Face],
    status_sock: socket.socket,
) -> int | None:
    candidates = list(enumerate(faces))
    if state.auto_register and state.guide_region is not None:
        x, y, w, h = state.guide_region
        candidates = [(i, face) for i, face in candidates
                      if x <= face.center[0] / frame.shape[1] <= x + w and
                      y <= face.center[1] / frame.shape[0] <= y + h]
    if not candidates and faces and state.guide_region is not None:
        state.reset_hold("MOVE_CENTER")
        send_status(status_sock, state, "MOVE_CENTER")
        return None
    if not candidates:
        now = time.monotonic()
        if state.missing_since is None:
            state.missing_since = now
        state.hold_last_valid = None  # Pause rather than count unseen time.
        if state.hold_start is None or now - state.missing_since > DETECTION_GRACE_SECONDS:
            state.reset_hold("NO_FACE")
        send_status(status_sock, state, "NO_FACE")
        return None
    if len(candidates) != 1:
        state.reset_hold("MULTIPLE_FACES")
        send_status(status_sock, state, "MULTIPLE_FACES")
        return None

    face_index, face = candidates[0]
    if state.auto_register:
        hint = check_alignment(face, frame.shape[1], frame.shape[0], state.guide_region)
        if hint != "HOLD":
            state.reset_hold(hint)
            send_status(status_sock, state, hint)
            return None

    if not state.auto_register:
        complete_registration(state, status_sock)
        return face_index

    now = time.monotonic()
    if state.missing_since is not None and now - state.missing_since > DETECTION_GRACE_SECONDS:
        state.reset_hold("HOLD")
    state.missing_since = None
    if state.hold_start is None:
        state.hold_start = now

    if state.hold_last_valid is not None:
        gap = now - state.hold_last_valid
        # Slow inference should take longer, rather than resetting forever on
        # lower-spec PCs. A long stall cannot count as several unseen seconds.
        state.hold_elapsed += min(gap, DETECTION_GRACE_SECONDS)
    state.hold_last_valid = now
    state.progress = min(1.0, state.hold_elapsed / HOLD_SECONDS)
    state.hint = "HOLD"
    send_status(status_sock, state, "HOLD")
    if state.progress >= 1.0:
        complete_registration(state, status_sock)
        return face_index
    return None


def select_single_face(faces: list[Face]) -> int | None:
    # 1人だけが映る運用。複数検出時は対象を勝手に切り替えない。
    return 0 if len(faces) == 1 else None


def run() -> None:
    import mediapipe as mp  # 準備画面の両手上げ判定にだけ使用。

    ensure_models()
    if not hasattr(cv2, "FaceDetectorYN"):
        raise RuntimeError("OpenCV 4.8以上が必要です。Setup FruitCatch.batを再実行してください")

    camera_config = load_camera_config()
    camera_index, camera_backend, camera_name = select_camera(camera_config)

    detector = cv2.FaceDetectorYN.create(str(FACE_DETECTOR_PATH), "", (320, 320), 0.8, 0.3)
    pose_options = mp.tasks.vision.PoseLandmarkerOptions(
        base_options=mp.tasks.BaseOptions(model_asset_path=str(POSE_PATH)),
        running_mode=mp.tasks.vision.RunningMode.VIDEO,
        num_poses=1,
        min_pose_detection_confidence=0.5,
        min_pose_presence_confidence=0.5,
        min_tracking_confidence=0.5,
    )

    cap = configure_camera(camera_index, camera_backend, camera_name, camera_config)
    camera = LatestCamera(cap)

    position_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    status_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    command_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    http_server = None
    server_thread = None
    try:
        camera.start()
        command_sock.bind(("127.0.0.1", PYTHON_COMMAND_PORT))
        command_sock.setblocking(False)

        shared = PreviewBuffer()
        http_server = ThreadingHTTPServer((PREVIEW_HOST, PREVIEW_PORT), make_preview_handler(shared))
        http_server.daemon_threads = True
        server_thread = threading.Thread(target=http_server.serve_forever, daemon=True)
        server_thread.start()

        state = TrackingState()
        last_timestamp_ms = -1
        last_pose_at = 0.0
        sequence = 0
        hands_raised = False
        print(f"Unity preview: http://{PREVIEW_HOST}:{PREVIEW_PORT}/frame.jpg")
        print(f"Unity command UDP: {PYTHON_COMMAND_PORT}; tracking X UDP: {UNITY_POSITION_PORT}; status UDP: {UNITY_STATUS_PORT}")
        print("登録待ち。Unityの登録画面から AUTO コマンドを送信してください。")

        with mp.tasks.vision.PoseLandmarker.create_from_options(pose_options) as pose_detector:
            while True:
                read_commands(command_sock, state, status_sock)
                captured = camera.next_frame(sequence)
                if captured is None:
                    continue
                sequence, frame, captured_at = captured

                analysis_frame = resize_to_max_width(frame, ANALYSIS_MAX_WIDTH)
                if MIRROR_IMAGE:
                    analysis_frame = cv2.flip(analysis_frame, 1)
                faces = detect_faces(detector, analysis_frame) if state.pending_register or state.active else []

                selected = None
                if state.pending_register:
                    selected = process_registration(state, analysis_frame, faces, status_sock)
                elif state.active:
                    selected = select_single_face(faces)
                    if selected is None:
                        send_status(status_sock, state, "LOST")
                    else:
                        send_status(status_sock, state, "TRACKING")

                if selected is not None:
                    normalized_x = float(np.clip(faces[selected].center[0] / analysis_frame.shape[1], 0.0, 1.0))
                    position_sock.sendto(f"{normalized_x:.4f}".encode("utf-8"),
                                         (UNITY_IP, UNITY_POSITION_PORT))

                now = time.monotonic()
                if shared.is_requested():
                    if now - last_pose_at >= 1.0 / POSE_FPS:
                        rgb = cv2.cvtColor(analysis_frame, cv2.COLOR_BGR2RGB)
                        mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
                        timestamp_ms = max(int(captured_at * 1000), last_timestamp_ms + 1)
                        last_timestamp_ms = timestamp_ms
                        pose_result = pose_detector.detect_for_video(mp_image, timestamp_ms)
                        hands_raised = has_raised_both_hands(pose_result.pose_landmarks)
                        last_pose_at = now
                else:
                    hands_raised = False
                shared.update(frame, state.progress, state.hint, state.active,
                              hands_raised, state.registration_id)

                if SHOW_DEBUG_WINDOW:
                    debug = analysis_frame.copy()
                    for index, face in enumerate(faces):
                        x, y, w, h = face.rect
                        color = (0, 220, 0) if index == selected else (120, 120, 120)
                        cv2.rectangle(debug, (x, y), (x + w, y + h), color, 2)
                    cv2.putText(debug, f"{state.hint} {state.progress:.0%}", (12, 28),
                                cv2.FONT_HERSHEY_SIMPLEX, 0.65, (0, 220, 255), 2)
                    cv2.imshow("Face Tracking [Q to quit]", debug)
                    if cv2.waitKey(1) & 0xFF == ord("q"):
                        break

    finally:
        camera.close()
        command_sock.close()
        position_sock.close()
        status_sock.close()
        if http_server is not None:
            http_server.shutdown()
            http_server.server_close()
        cv2.destroyAllWindows()


if __name__ == "__main__":
    try:
        run()
    except KeyboardInterrupt:
        pass
