"""FruitCatch camera tracker. Unity position UDP 5015 remains a normalized X."""

from __future__ import annotations

from dataclasses import dataclass
import json
import math
import os
from pathlib import Path
import socket
import sys
import threading
import time
import traceback
import urllib.request

import cv2
import numpy as np

from fruitcatch_camera import (COMMAND_PORT, PREVIEW_PORT, LatestCamera,
                               configure_camera, exclusive_socket, load_camera_config, log, select_camera)
from fruitcatch_preview import (PreviewServer, PreviewStatus, PreviewStream,
                                make_preview_handler, mirrored, resize_to_max_width)
from fruitcatch_tracking import (Observation, TargetLock, associate_body, collect_bodies,
                                 face_observation, visible)

UNITY_IP = "127.0.0.1"
UNITY_POSITION_PORT = 5015
UNITY_STATUS_PORT = 5007

MIN_FACE_SIZE_PX = 16
FACE_CONFIDENCE = 0.65
FACE_ANALYSIS_WIDTH = 960
POSE_ANALYSIS_WIDTH = 640
HAND_FPS = 10
TRACKING_FPS = 20
IDENTITY_INTERVAL = 0.2
IDENTITY_MATCH = 0.42
IDENTITY_MARGIN = 0.04
# ====== 両手上げ判定 ======
HAND_RAISE_SHOULDER_MARGIN = 0.03  # 手首が肩より画面高の3%以上上
MIN_POSE_VISIBILITY = 0.55

# ====== 位置合わせ・登録 ======
HOLD_SECONDS = 3.0
DETECTION_GRACE_SECONDS = 0.25
CENTER_TOLERANCE_X = 0.12  # 元画像の中心から±12%
CENTER_TOLERANCE_Y = 0.13
MIN_FACE_WIDTH_RATIO = 0.025
MAX_FACE_WIDTH_RATIO = 0.49

MODEL_DIR = Path(__file__).resolve().parent / "face_models"
FACE_DETECTOR_PATH = MODEL_DIR / "face_detection_yunet_2023mar.onnx"
POSE_PATH = MODEL_DIR / "pose_landmarker_lite.task"
SFACE_PATH = MODEL_DIR / "face_recognition_sface_2021dec.onnx"
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
    detection: np.ndarray | None = None


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
    hold_seconds: float = HOLD_SECONDS
    hold_last_valid: float | None = None
    missing_since: float | None = None
    hold_face: Observation | None = None
    target: TargetLock | None = None
    descriptor: np.ndarray | None = None

    def reset_hold(self, hint: str) -> None:
        self.hold_start = None
        self.hold_elapsed = 0.0
        self.hold_last_valid = None
        self.missing_since = None
        self.hold_face = None
        self.progress = 0.0
        self.hint = hint

    def clear(self) -> None:
        self.registration_id = ""
        self.guide_region = None
        self.hold_seconds = HOLD_SECONDS
        self.pending_register = False
        self.auto_register = False
        self.active = False
        self.target = None
        self.descriptor = None
        self.reset_hold("WAITING")
        self.last_status = "IDLE"


def ensure_models(*, download=True) -> None:
    for target, url in MODEL_URLS.items():
        if target.is_file() and target.stat().st_size > 100_000:
            continue
        if not download:
            raise RuntimeError(f"Tracking model missing or incomplete: {target}")
        MODEL_DIR.mkdir(parents=True, exist_ok=True)
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
    """Keep the YuNet landmarks for SFace alignment when needed."""
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
            detection=row,
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


def read_commands(command_sock: socket.socket, state: TrackingState, status_sock: socket.socket) -> bool:
    changed = False
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
                if len(parts) not in (6, 7) or len(parts[1]) != 32 or any(c not in "0123456789ABCDEF" for c in parts[1]):
                    raise ValueError("Invalid registration id")
                region = tuple(float(value) for value in parts[2:6])
                hold_seconds = float(parts[6]) if len(parts) == 7 else HOLD_SECONDS
                if len(parts) == 7 and not 0.1 <= hold_seconds <= 15.0:
                    raise ValueError("Invalid registration hold")
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
            changed = True
            state.registration_id = parts[1]
            state.guide_region = region
            state.hold_seconds = hold_seconds
            state.pending_register = True
            state.auto_register = True
            state.hint = "NO_FACE"
            send_status(status_sock, state, "ALIGNING", force=True)
        elif command in ("AUTO", "REGISTER"):
            state.clear()
            changed = True
            state.pending_register = True
            state.auto_register = command == "AUTO"
            state.hint = "NO_FACE"
            send_status(status_sock, state, "ALIGNING" if state.auto_register else "REGISTERING", force=True)
        elif command == "CLEAR":
            state.clear()
            changed = True
            send_status(status_sock, state, "CLEARED", force=True)
        elif command == "PING":
            send_status(status_sock, state, "READY", force=True)
        else:
            send_status(status_sock, state, "UNKNOWN_COMMAND", force=True)
    return changed


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
        if not all(visible(point, MIN_POSE_VISIBILITY)
                   for point in (left_shoulder, right_shoulder, left_wrist, right_wrist)):
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
    match=None,
    captured_at: float | None = None,
    recognizer=None,
) -> int | None:
    now = time.monotonic() if captured_at is None else captured_at
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

    body_or_face = match(face_index) if match is not None else None
    if match is not None and body_or_face is None:
        if state.missing_since is None:
            state.missing_since = now
        state.hold_last_valid = None
        if state.hold_start is None or now - state.missing_since > DETECTION_GRACE_SECONDS:
            state.reset_hold("NO_BODY")
        state.hint = "NO_BODY"
        send_status(status_sock, state, "NO_BODY")
        return None

    if not state.auto_register:
        if recognizer is not None:
            state.descriptor = face_descriptor(recognizer, frame, face)
            if state.descriptor is None:
                state.reset_hold("NO_FACE")
                return None
        if body_or_face is not None:
            state.target = TargetLock(body_or_face, now)
        complete_registration(state, status_sock)
        return face_index

    if state.missing_since is not None and now - state.missing_since > DETECTION_GRACE_SECONDS:
        state.reset_hold("HOLD")
    state.missing_since = None
    current_face = face_observation(face, frame.shape[1])
    previous = state.hold_face
    if previous is not None and (
            math.hypot(previous.x - current_face.x, previous.y - current_face.y) >
            max(0.035, previous.size * 0.7) or abs(math.log(current_face.size / previous.size)) > 0.42):
        state.reset_hold("HOLD")
    state.hold_face = current_face
    if state.hold_start is None:
        state.hold_start = now

    if state.hold_last_valid is not None:
        gap = now - state.hold_last_valid
        # Slow inference should take longer, rather than resetting forever on
        # lower-spec PCs. A long stall cannot count as several unseen seconds.
        state.hold_elapsed += min(gap, DETECTION_GRACE_SECONDS)
    state.hold_last_valid = now
    state.progress = min(1.0, state.hold_elapsed / state.hold_seconds)
    state.hint = "HOLD"
    send_status(status_sock, state, "HOLD")
    if state.progress >= 1.0:
        if recognizer is not None:
            state.descriptor = face_descriptor(recognizer, frame, face)
            if state.descriptor is None:
                state.reset_hold("NO_FACE")
                return None
        if body_or_face is not None:
            state.target = TargetLock(body_or_face, now)
        complete_registration(state, status_sock)
        return face_index
    return None


class PoseDetector:
    def __init__(self):
        import mediapipe as mp

        self.mp = mp
        vision = mp.tasks.vision
        self.detector = vision.PoseLandmarker.create_from_options(vision.PoseLandmarkerOptions(
            base_options=mp.tasks.BaseOptions(model_asset_path=str(POSE_PATH)),
            running_mode=vision.RunningMode.VIDEO,
            num_poses=3,
            min_pose_detection_confidence=0.5,
            min_pose_presence_confidence=0.5,
            min_tracking_confidence=0.5,
        ))
        self.timestamp = -1

    def detect(self, frame, captured_at):
        rgb = cv2.cvtColor(resize_to_max_width(frame, POSE_ANALYSIS_WIDTH), cv2.COLOR_BGR2RGB)
        image = self.mp.Image(image_format=self.mp.ImageFormat.SRGB, data=rgb)
        self.timestamp = max(int(captured_at * 1000), self.timestamp + 1)
        return self.detector.detect_for_video(image, self.timestamp).pose_landmarks

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.detector.close()


def create_face_detector():
    return cv2.FaceDetectorYN.create(str(FACE_DETECTOR_PATH), "", (320, 320), FACE_CONFIDENCE, 0.3, 100)


def face_descriptor(recognizer, frame, face):
    if face.detection is None or len(face.detection) < 15 or not np.isfinite(face.detection).all():
        return None
    try:
        aligned = recognizer.alignCrop(frame, face.detection)
        descriptor = recognizer.feature(aligned).reshape(-1)
    except cv2.error:
        return None
    norm = float(np.linalg.norm(descriptor))
    return descriptor / norm if np.isfinite(norm) and norm > 1e-9 else None


def identify_face(recognizer, profile, frame, faces):
    ranked = []
    for index, face in enumerate(faces):
        feature = face_descriptor(recognizer, frame, face)
        if feature is not None:
            ranked.append((float(np.dot(feature, profile)), index))
    ranked.sort(reverse=True)
    if not ranked or ranked[0][0] < IDENTITY_MATCH:
        return None
    if len(ranked) > 1 and ranked[0][0] - ranked[1][0] < IDENTITY_MARGIN:
        return None
    return ranked[0][1]


def track(camera, stream, command_sock, status_sock, position_sock, detector, pose_detector, mode, stop, recognizer=None):
    state = TrackingState()
    sequence = 0
    last_inference_at = float("-inf")
    hands_raised_at = None
    last_report = time.monotonic()
    last_identity_at = float("-inf")
    verify_identity = False
    last_issue = None
    while not stop.is_set():
        if read_commands(command_sock, state, status_sock):
            verify_identity = False
            last_identity_at = float("-inf")
            stream.publish(PreviewStatus(state.registration_id, state.progress, state.hint), reset=True)
        preview_requested = stream.is_requested()
        if not (preview_requested or state.pending_register or state.active):
            stop.wait(0.025)
            continue
        interval = 1 / (TRACKING_FPS if state.active or state.pending_register else HAND_FPS)
        delay = last_inference_at + interval - time.monotonic()
        if delay > 0:
            stop.wait(min(delay, 0.025))
            continue
        captured = camera.next_frame(sequence)
        if captured is None:
            if state.active and state.target and time.monotonic() - state.target.seen_at > 0.35:
                state.target.mark_missing()
                verify_identity = state.descriptor is not None
                issue = "camera error" if camera.error else "no fresh camera frame"
                if issue != last_issue:
                    log(f"[tracking] {issue}: {camera.error or ''}")
                    last_issue = issue
                send_status(status_sock, state, "LOST")
            if camera.error and state.pending_register:
                state.reset_hold("NO_FACE")
            continue
        sequence, frame, captured_at = captured
        last_inference_at = time.monotonic()
        need_face = state.pending_register or (state.active and mode == "face")
        need_pose = state.pending_register or (state.active and mode == "pose") or preview_requested
        analysis = mirrored(resize_to_max_width(frame, FACE_ANALYSIS_WIDTH if need_face else POSE_ANALYSIS_WIDTH))
        height, width = analysis.shape[:2]
        faces = detect_faces(detector, analysis) if need_face else []
        poses = pose_detector.detect(analysis, captured_at) if need_pose and (not state.pending_register or faces) else []
        if time.monotonic() - captured_at >= 0.5:
            if last_issue != "inference result too old":
                log(f"[tracking] inference result too old: {(time.monotonic() - captured_at) * 1000:.0f}ms")
                last_issue = "inference result too old"
            continue
        bodies = collect_bodies(poses, height / width)
        if preview_requested and has_raised_both_hands(poses):
            hands_raised_at = captured_at
        else:
            hands_raised_at = None
        x = None
        snapshot = None
        if state.pending_register:
            def match(index):
                body = associate_body(index, faces, bodies, width)
                if body is None:
                    return None
                return body.shoulders if mode == "pose" else face_observation(faces[index], width)

            chosen = process_registration(state, analysis, faces, status_sock, match, captured_at, recognizer)
            if chosen is not None:
                x = state.target.target.x
                snapshot = frame
        elif state.active:
            candidates = ([body.shoulders for body in bodies] if mode == "pose" else
                          [face_observation(face, width) for face in faces])
            can_identify = state.descriptor is not None and recognizer is not None
            target = state.target
            if can_identify and captured_at - target.seen_at > 0.35:
                verify_identity = True
            if not verify_identity:
                x = target.select(candidates, captured_at)
                if can_identify and (target.ambiguous or (x is None and len(candidates) > 1)):
                    verify_identity = True
            issue = "ambiguous candidates" if target.ambiguous else "no matching geometry"
            if verify_identity:
                issue = "waiting for identity confirmation"
                if captured_at - last_identity_at >= IDENTITY_INTERVAL:
                    last_identity_at = captured_at
                    if mode == "pose":
                        face_image = mirrored(resize_to_max_width(frame, FACE_ANALYSIS_WIDTH))
                        face_candidates = detect_faces(detector, face_image)
                    else:
                        face_image, face_candidates = analysis, faces
                    index = identify_face(recognizer, state.descriptor, face_image, face_candidates)
                    if index is not None:
                        face_width = face_image.shape[1]
                        if mode == "pose":
                            body = associate_body(index, face_candidates, bodies, face_width)
                            observation = body.shoulders if body is not None else None
                        else:
                            observation = face_observation(face_candidates[index], face_width)
                        if observation is not None:
                            state.target = TargetLock(observation, captured_at)
                            verify_identity = False
                            x = observation.x
            send_status(status_sock, state, "LOST" if x is None else "TRACKING")
            if x is not None:
                issue = None if time.monotonic() - captured_at < 0.5 else "inference result too old"
            if issue != last_issue:
                log(f"[tracking] {issue or 'position output resumed'}")
                last_issue = issue

        if x is not None and time.monotonic() - captured_at < 0.5:
            try:
                position_sock.sendto(f"{x:.4f}".encode("ascii"), (UNITY_IP, UNITY_POSITION_PORT))
            except OSError as exc:
                log(f"[tracking] UDP position send failed: {exc}")

        stream.publish(PreviewStatus(state.registration_id, state.progress, state.hint, hands_raised_at), snapshot)
        now = time.monotonic()
        stream.inference_rate.tick(now)
        if now - last_report >= 5:
            log(f"[pipeline] {stream.metrics()} input_to_inference_ms={(now - captured_at) * 1000:.1f}")
            last_report = now


def run():
    stop = threading.Event()
    if os.environ.get("FRUITCATCH_LAUNCH_TOKEN"):
        def watch_launcher():
            os.read(sys.stdin.fileno(), 1)
            stop.set()
            # Also exit if a driver/model blocks native work after the launcher exits.
            if not shutdown_done.wait(5):
                os._exit(0)

        shutdown_done = threading.Event()
        threading.Thread(target=watch_launcher, name="FruitCatchParent", daemon=True).start()

    startup = time.monotonic()
    config = load_camera_config()
    preferred = json.loads(os.environ.get("FRUITCATCH_PREVIEW_MODE", "null"))

    def open_selected(stop_event):
        started = time.monotonic()
        index, backend, name = select_camera(config)
        log(f"[startup] camera enumeration: {time.monotonic() - started:.2f}s")
        return configure_camera(index, backend, name, config, stop_event, preferred)

    with (exclusive_socket(COMMAND_PORT, socket.SOCK_DGRAM) as commands,
          socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as status,
          socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as position):
        commands.setblocking(False)
        camera = LatestCamera(open_selected)
        stream = PreviewStream(camera)
        try:
            handler = make_preview_handler(stream, extra_headers={"X-Registration-Hold-Seconds": str(HOLD_SECONDS)})
            with PreviewServer((UNITY_IP, PREVIEW_PORT), handler) as server:
                camera.start()
                model_start = time.monotonic()
                ensure_models(download=not os.environ.get("FRUITCATCH_LAUNCH_TOKEN"))
                detector = create_face_detector()
                if not SFACE_PATH.is_file():
                    raise RuntimeError(f"SFace model missing from distribution: {SFACE_PATH}")
                recognizer = cv2.FaceRecognizerSF.create(str(SFACE_PATH), "")
                with PoseDetector() as pose_detector:
                    log(f"[startup] models ready: {time.monotonic() - model_start:.2f}s")
                    stream.thread.start()
                    server_thread = threading.Thread(target=server.serve_forever, daemon=True)
                    server_thread.start()
                    log(f"[startup] HTTP ready: {time.monotonic() - startup:.2f}s; mode={config['tracking_mode']}")
                    try:
                        track(camera, stream, commands, status, position, detector, pose_detector,
                              config["tracking_mode"], stop, recognizer)
                    finally:
                        stream.stop_event.set()
                        server.shutdown()
                        stream.thread.join(timeout=2)
                        server_thread.join(timeout=2)
        except OSError as exc:
            raise RuntimeError(f"トラッカーの通信またはポート {PREVIEW_PORT} を確認してください: {exc}") from exc
        finally:
            camera.close()
            if os.environ.get("FRUITCATCH_LAUNCH_TOKEN"):
                shutdown_done.set()


if __name__ == "__main__":
    for output in (sys.stdout, sys.stderr):
        if output is not None:
            output.reconfigure(encoding="utf-8", errors="backslashreplace", line_buffering=True)
    try:
        run()
    except KeyboardInterrupt:
        pass
    except Exception:
        traceback.print_exc()
        sys.exit(1)
