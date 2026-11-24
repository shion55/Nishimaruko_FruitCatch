"""Unityの丸型カメラプレビューから3秒静止で顔を登録し、登録者だけを追跡する。

PythonがWebカメラを1台だけ開き、Unityのプレビューと顔追跡に共用する。

Unity -> Python  UDP 5006: AUTO (3秒自動登録), REGISTER (従来の即時登録), CLEAR, PING
Python -> Unity  UDP 5015: 登録者のX座標 (従来どおり "0.5123")
Python -> Unity  UDP 5007: 登録・追跡状態 (REGISTERED / TRACKING / LOSTなど)
Python -> Unity  HTTP 127.0.0.1:8765/frame.jpg: 640x480 JPEGおよび進捗ヘッダー

Unity側で円形UI Maskを使い、画像の中央だけを丸く切り抜いて表示する。
カメラ画像を顔へ自動追従して切り抜くと位置合わせが無意味になるため、
登録プレビューは常に固定中心で切り抜く。登録顔の特徴はメモリにだけ保存する。

依存: py -m pip install mediapipe opencv-contrib-python numpy
初回起動に限り、公式配布モデルをダウンロードする。
"""

from __future__ import annotations

from dataclasses import dataclass, field
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import json
import socket
import math
import threading
import time
import urllib.request

import cv2
import mediapipe as mp
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
MAX_DETECTED_FACES = 4
MIN_FACE_SIZE_PX = 60
ANALYSIS_MAX_WIDTH = 1280
PREVIEW_MAX_WIDTH = 1920
REQUESTED_CAMERA_FPS = 30
PREVIEW_FPS = 15  # Unity側は約12〜15fpsのプレビュー（ゲーム中のトラッキングは毎フレーム）
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
HOLD_IDENTITY_THRESHOLD = 0.40  # 静止途中で人物が入れ替わっていないか

# ====== 登録者照合（会場環境に合わせ調整可） ======
MATCH_THRESHOLD = 0.42
AMBIGUITY_MARGIN = 0.04
ADD_PROFILE_THRESHOLD = 0.65
MAX_FACE_PROFILES = 6

MODEL_DIR = Path(__file__).resolve().parent / "face_models"
LANDMARKER_PATH = MODEL_DIR / "face_landmarker.task"
SFACE_PATH = MODEL_DIR / "face_recognition_sface_2021dec.onnx"
POSE_PATH = MODEL_DIR / "pose_landmarker_lite.task"
MODEL_URLS = {
    LANDMARKER_PATH:
        "https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/1/face_landmarker.task",
    SFACE_PATH:
        "https://media.githubusercontent.com/media/opencv/opencv_zoo/main/models/face_recognition_sface/face_recognition_sface_2021dec.onnx",
    POSE_PATH:
        "https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_lite/float16/1/pose_landmarker_lite.task",
}


@dataclass
class Face:
    rect: tuple[int, int, int, int]
    sface_input: np.ndarray
    center: tuple[float, float]
    descriptor: np.ndarray | None = None


@dataclass
class TrackingState:
    profiles: list[np.ndarray] = field(default_factory=list)
    pending_register: bool = False
    auto_register: bool = False
    active: bool = False
    hold_start: float | None = None
    hold_descriptor: np.ndarray | None = None
    progress: float = 0.0
    hint: str = "WAITING"
    last_center: tuple[float, float] | None = None
    last_seen_at: float = 0.0
    last_status: str = "IDLE"
    frame_count: int = 0
    registration_id: str = ""
    guide_region: tuple[float, float, float, float] | None = None
    hold_elapsed: float = 0.0
    hold_last_valid: float | None = None
    missing_since: float | None = None

    def reset_hold(self, hint: str) -> None:
        self.hold_start = None
        self.hold_descriptor = None
        self.hold_elapsed = 0.0
        self.hold_last_valid = None
        self.missing_since = None
        self.progress = 0.0
        self.hint = hint

    def clear(self) -> None:
        self.registration_id = ""
        self.guide_region = None
        self.profiles.clear()
        self.pending_register = False
        self.auto_register = False
        self.active = False
        self.reset_hold("WAITING")
        self.last_center = None
        self.last_seen_at = 0.0
        self.last_status = "IDLE"


class PreviewBuffer:
    """HTTP配信スレッドとMediaPipeの推論ループの間で最新フレームを受け渡す。"""

    def __init__(self) -> None:
        self.lock = threading.Lock()
        self.jpeg: bytes | None = None
        self.progress = 0.0
        self.hint = "WAITING"
        self.registered = False
        self.hands_raised = False
        self.registration_id = ""

    def update(self, jpeg: bytes | None, progress: float, hint: str,
               registered: bool, hands_raised: bool, registration_id: str = "") -> None:
        with self.lock:
            if jpeg is not None:
                self.jpeg = jpeg
            self.progress = progress
            self.hint = hint
            self.registered = registered
            self.hands_raised = hands_raised
            self.registration_id = registration_id

    def snapshot(self) -> tuple[bytes | None, float, str, bool, bool, str]:
        with self.lock:
            return self.jpeg, self.progress, self.hint, self.registered, self.hands_raised, self.registration_id


def make_preview_handler(shared: PreviewBuffer):
    class PreviewHandler(BaseHTTPRequestHandler):
        def do_GET(self):
            if self.path.split("?")[0] != "/frame.jpg":
                self.send_error(404)
                return

            jpeg, progress, hint, registered, hands_raised, registration_id = shared.snapshot()
            if jpeg is None:
                self.send_response(503)
                self.send_header("Content-Length", "0")
                self.end_headers()
                return

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
        "use_max_resolution": True,
        "width": 1920,
        "height": 1080,
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

    use_max = bool(config.get("use_max_resolution", True))
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
        requested_width = max(1, int(config.get("width", 1920)))
        requested_height = max(1, int(config.get("height", 1080)))
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


def average_landmark(points: list, ids: tuple[int, ...], width: int, height: int) -> tuple[float, float]:
    return (
        float(np.mean([points[i].x for i in ids]) * width),
        float(np.mean([points[i].y for i in ids]) * height),
    )


def collect_faces(face_landmarks: list, width: int, height: int) -> list[Face]:
    faces = []
    for points in face_landmarks:
        xs = np.asarray([point.x for point in points]) * width
        ys = np.asarray([point.y for point in points]) * height
        x0, y0 = int(np.clip(xs.min(), 0, width - 1)), int(np.clip(ys.min(), 0, height - 1))
        x1, y1 = int(np.clip(xs.max(), 0, width - 1)), int(np.clip(ys.max(), 0, height - 1))
        w, h = x1 - x0, y1 - y0
        if min(w, h) < MIN_FACE_SIZE_PX:
            continue

        eye_a = average_landmark(points, (33, 133, 159, 145), width, height)
        eye_b = average_landmark(points, (362, 263, 386, 374), width, height)
        eye_left, eye_right = sorted((eye_a, eye_b), key=lambda p: p[0])
        nose = average_landmark(points, (1,), width, height)
        mouth_a = average_landmark(points, (61,), width, height)
        mouth_b = average_landmark(points, (291,), width, height)
        mouth_left, mouth_right = sorted((mouth_a, mouth_b), key=lambda p: p[0])
        face_row = np.asarray([
            x0, y0, w, h,
            *eye_left, *eye_right, *nose, *mouth_left, *mouth_right,
            1.0,
        ], dtype=np.float32)
        faces.append(Face(
            rect=(x0, y0, w, h),
            sface_input=face_row,
            center=((x0 + x1) / 2.0, (y0 + y1) / 2.0),
        ))
    return faces


def extract_feature(recognizer, frame: np.ndarray, face: Face) -> np.ndarray | None:
    if face.descriptor is not None:
        return face.descriptor
    try:
        aligned = recognizer.alignCrop(frame, face.sface_input)
        descriptor = recognizer.feature(aligned).reshape(-1).astype(np.float32)
        magnitude = float(np.linalg.norm(descriptor))
        if magnitude < 1e-9 or not np.isfinite(magnitude):
            return None
        face.descriptor = descriptor / magnitude
        return face.descriptor
    except cv2.error:
        return None


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
    face: Face,
    descriptor: np.ndarray,
    status_sock: socket.socket,
) -> None:
    # 初期の顔特徴と、3秒後の顔特徴を登録（メモリ内のみ）
    state.profiles = [descriptor.copy()]
    if state.hold_descriptor is not None:
        initial = state.hold_descriptor
        if float(np.dot(initial, descriptor)) < 0.98:
            state.profiles.append(initial.copy())
    state.active = True
    state.pending_register = False
    state.auto_register = False
    state.hold_start = None
    state.hold_descriptor = None
    state.progress = 1.0
    state.hint = "DONE"
    state.last_center = face.center
    state.last_seen_at = time.monotonic()
    send_status(status_sock, state, "REGISTERED", force=True)


def process_registration(
    state: TrackingState,
    recognizer,
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

    descriptor = extract_feature(recognizer, frame, face)
    if descriptor is None:
        state.reset_hold("REGISTER_FAILED")
        send_status(status_sock, state, "REGISTER_FAILED")
        return None

    if not state.auto_register:
        complete_registration(state, face, descriptor, status_sock)
        return face_index

    now = time.monotonic()
    if state.missing_since is not None and now - state.missing_since > DETECTION_GRACE_SECONDS:
        state.reset_hold("HOLD")
    state.missing_since = None
    if state.hold_descriptor is not None:
        similarity = float(np.dot(state.hold_descriptor, descriptor))
        if similarity < HOLD_IDENTITY_THRESHOLD:
            state.reset_hold("HOLD")  # 別人の顔へ途中で切り替わったらカウントをやり直す

    if state.hold_start is None:
        state.hold_start = now
        state.hold_descriptor = descriptor.copy()

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
        complete_registration(state, face, descriptor, status_sock)
        return face_index
    return None


def select_registered_face(
    state: TrackingState,
    recognizer,
    frame: np.ndarray,
    faces: list[Face],
) -> tuple[int | None, float]:
    scores = []
    for face in faces:
        descriptor = extract_feature(recognizer, frame, face)
        if descriptor is None:
            scores.append(-1.0)
        else:
            scores.append(max(float(np.dot(descriptor, p)) for p in state.profiles))
    if not scores:
        return None, 0.0
    ranked = sorted(range(len(scores)), key=lambda i: scores[i], reverse=True)
    best = ranked[0]
    score = scores[best]
    if score < MATCH_THRESHOLD:
        return None, score
    if len(ranked) > 1 and scores[ranked[1]] >= score - AMBIGUITY_MARGIN:
        return None, score

    if state.last_center is not None and time.monotonic() - state.last_seen_at < 0.4:
        distance = np.hypot(
            faces[best].center[0] - state.last_center[0],
            faces[best].center[1] - state.last_center[1],
        )
        if distance > 0.45 * frame.shape[1] and score < 0.70:
            return None, score
    return best, score


def run() -> None:
    ensure_models()
    if not hasattr(cv2, "FaceRecognizerSF"):
        raise RuntimeError("opencv-contrib-pythonが必要です（FaceRecognizerSF がありません）")

    camera_config = load_camera_config()
    camera_index, camera_backend, camera_name = select_camera(camera_config)

    recognizer = cv2.FaceRecognizerSF.create(str(SFACE_PATH), "")
    options = mp.tasks.vision.FaceLandmarkerOptions(
        base_options=mp.tasks.BaseOptions(model_asset_path=str(LANDMARKER_PATH)),
        running_mode=mp.tasks.vision.RunningMode.VIDEO,
        num_faces=MAX_DETECTED_FACES,
        min_face_detection_confidence=0.5,
        min_face_presence_confidence=0.5,
        min_tracking_confidence=0.5,
    )
    pose_options = mp.tasks.vision.PoseLandmarkerOptions(
        base_options=mp.tasks.BaseOptions(model_asset_path=str(POSE_PATH)),
        running_mode=mp.tasks.vision.RunningMode.VIDEO,
        num_poses=4,
        min_pose_detection_confidence=0.5,
        min_pose_presence_confidence=0.5,
        min_tracking_confidence=0.5,
    )

    cap = configure_camera(camera_index, camera_backend, camera_name, camera_config)

    position_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    status_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    command_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    http_server = None
    server_thread = None
    try:
        command_sock.bind(("127.0.0.1", PYTHON_COMMAND_PORT))
        command_sock.setblocking(False)

        shared = PreviewBuffer()
        http_server = ThreadingHTTPServer((PREVIEW_HOST, PREVIEW_PORT), make_preview_handler(shared))
        http_server.daemon_threads = True
        server_thread = threading.Thread(target=http_server.serve_forever, daemon=True)
        server_thread.start()

        state = TrackingState()
        last_timestamp_ms = -1
        last_preview_at = 0.0
        print(f"Unity preview: http://{PREVIEW_HOST}:{PREVIEW_PORT}/frame.jpg")
        print(f"Unity command UDP: {PYTHON_COMMAND_PORT}; tracking X UDP: {UNITY_POSITION_PORT}; status UDP: {UNITY_STATUS_PORT}")
        print("登録待ち。Unityの登録画面から AUTO コマンドを送信してください。")

        with (mp.tasks.vision.FaceLandmarker.create_from_options(options) as detector,
              mp.tasks.vision.PoseLandmarker.create_from_options(pose_options) as pose_detector):
            while True:
                read_commands(command_sock, state, status_sock)
                ok, frame = cap.read()
                if not ok:
                    print("カメラフレームの取得に失敗しました")
                    break
                if MIRROR_IMAGE:
                    frame = cv2.flip(frame, 1)

                analysis_frame = resize_to_max_width(frame, ANALYSIS_MAX_WIDTH)
                rgb = cv2.cvtColor(analysis_frame, cv2.COLOR_BGR2RGB)
                mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
                timestamp_ms = max(int(time.monotonic() * 1000), last_timestamp_ms + 1)
                last_timestamp_ms = timestamp_ms
                result = detector.detect_for_video(mp_image, timestamp_ms)
                pose_result = pose_detector.detect_for_video(mp_image, timestamp_ms)
                faces = collect_faces(result.face_landmarks, analysis_frame.shape[1], analysis_frame.shape[0])
                hands_raised = has_raised_both_hands(pose_result.pose_landmarks)

                selected = None
                score = 0.0
                if state.pending_register:
                    selected = process_registration(state, recognizer, analysis_frame, faces, status_sock)
                elif state.active:
                    selected, score = select_registered_face(state, recognizer, analysis_frame, faces)
                    if selected is None:
                        send_status(status_sock, state, "LOST")
                    else:
                        state.last_center = faces[selected].center
                        state.last_seen_at = time.monotonic()
                        send_status(status_sock, state, "TRACKING")
                        descriptor = faces[selected].descriptor
                        if descriptor is not None and score >= ADD_PROFILE_THRESHOLD and state.frame_count % 15 == 0:
                            max_similarity = max(float(np.dot(descriptor, p)) for p in state.profiles)
                            if max_similarity < 0.97:
                                state.profiles.append(descriptor.copy())
                                if len(state.profiles) > MAX_FACE_PROFILES:
                                    state.profiles.pop(1)

                if selected is not None:
                    normalized_x = float(np.clip(faces[selected].center[0] / analysis_frame.shape[1], 0.0, 1.0))
                    position_sock.sendto(f"{normalized_x:.4f}".encode("utf-8"),
                                         (UNITY_IP, UNITY_POSITION_PORT))

                now = time.monotonic()
                if now - last_preview_at >= 1.0 / PREVIEW_FPS:
                    # 描画画像に検出矩形を合成しない。Unityのガイドを重ねて使う。
                    preview_frame = resize_to_max_width(frame, PREVIEW_MAX_WIDTH)
                    success, encoded = cv2.imencode(".jpg", preview_frame,
                                                    [cv2.IMWRITE_JPEG_QUALITY, JPEG_QUALITY])
                    shared.update(encoded.tobytes() if success else None,
                                  state.progress, state.hint, state.active, hands_raised, state.registration_id)
                    last_preview_at = now

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

                state.frame_count += 1
    finally:
        cap.release()
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
