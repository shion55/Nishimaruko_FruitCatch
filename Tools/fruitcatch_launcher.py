"""Windows distribution launcher. Tk owns the UI; one worker owns the camera."""

from __future__ import annotations

import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import threading
import time
import traceback
from uuid import uuid4
from urllib.error import URLError
from urllib.request import urlopen

import tkinter as tk
from tkinter import messagebox, ttk

TRACKING_ROOT = Path(__file__).resolve().parent
DIST_ROOT = TRACKING_ROOT.parent
LOG_ROOT = DIST_ROOT / "Logs"
TRACKER_LOG = LOG_ROOT / "tracker.log"
GAME_LOG = LOG_ROOT / "unity.log"
MODE_LABELS = {"pose": "骨格追跡（両肩）", "face": "顔追跡"}
FORMAT_LABELS = {"AUTO": "自動", "DEFAULT": "ドライバー標準", "MJPG": "MJPG", "YUY2": "YUY2", "UYVY": "UYVY"}


def configure_logging():
    LOG_ROOT.mkdir(exist_ok=True)
    log_file = (LOG_ROOT / "launcher.log").open("a", encoding="utf-8", buffering=1)
    sys.stdout = sys.stderr = log_file
    return log_file


def latest_error():
    try:
        with TRACKER_LOG.open("rb") as stream:
            stream.seek(max(0, TRACKER_LOG.stat().st_size - 4096))
            lines = stream.read().decode("utf-8", errors="replace").splitlines()
        return next((line for line in reversed(lines) if line.strip()), "")
    except OSError:
        return ""


class DetectionCheck:
    def __init__(self, camera):
        self.camera = camera
        self.stop = threading.Event()
        self.result = None
        self.error = None
        self.thread = threading.Thread(target=self.run, daemon=True)
        self.thread.start()

    def run(self):
        try:
            tracker.ensure_models()
            detector = tracker.create_face_detector()
            sequence = 0
            with tracker.PoseDetector() as pose:
                while not self.stop.is_set():
                    item = self.camera.next_frame(sequence)
                    if item is None:
                        continue
                    sequence, frame, captured = item
                    image = tracker.mirrored(tracker.resize_to_max_width(frame, tracker.FACE_ANALYSIS_WIDTH))
                    height, width = image.shape[:2]
                    faces = tracker.detect_faces(detector, image)
                    poses = pose.detect(image, captured)
                    self.result = captured, width, height, faces, poses
                    self.stop.wait(0.08)
        except Exception as exc:
            self.error = str(exc)
            traceback.print_exc()


class Launcher:
    def __init__(self, root):
        self.root = root
        self.config = camera_api.DEFAULTS.copy()
        self.camera = self.camera_lease = self.check = None
        self.camera_stop_started = 0.0
        self.tracker_process = self.game = None
        self.start_deadline = 0.0
        self.next_health_check = 0.0
        self.token = ""
        self.pending = None
        self.closing = False
        self.selection = []
        self.sequence = 0
        self.matrix = None
        self.photo = None
        self.device = tk.StringVar()
        self.size = tk.StringVar()
        self.fps = tk.StringVar()
        self.video_format = tk.StringVar()
        self.tracking_mode = tk.StringVar()
        self.detection = tk.BooleanVar()
        self.status = tk.StringVar(value="カメラを選択してください。")
        self.metrics = tk.StringVar(value="撮影解像度・実測FPS —")
        self.error = tk.StringVar()

        root.title("FruitCatch ランチャー")
        root.protocol("WM_DELETE_WINDOW", self.close)
        ui = ttk.Frame(root, padding=16)
        ui.grid(sticky="nsew")
        root.columnconfigure(0, weight=1)
        ui.columnconfigure(0, weight=1)
        self.devices = ttk.Combobox(ui, textvariable=self.device, state="readonly", width=40)
        self.devices.grid(row=0, column=0, sticky="ew")
        self.devices.bind("<<ComboboxSelected>>", self.settings_changed)
        self.search = ttk.Button(ui, text="再検索", command=self.rescan)
        self.search.grid(row=0, column=1, padx=(8, 0))
        self.preview = tk.Label(ui, text="カメラ映像", background="#202328", foreground="white")
        self.preview.grid(row=1, column=0, columnspan=2, sticky="ew", pady=(10, 4), ipady=4)
        ttk.Label(ui, textvariable=self.metrics).grid(row=2, column=0, columnspan=2, sticky="w")
        ttk.Checkbutton(ui, text="顔・骨格の検出を表示", variable=self.detection,
                        command=self.toggle_detection).grid(row=3, column=0, sticky="w")
        ttk.Button(ui, text="詳細設定", command=self.toggle_details).grid(row=3, column=1, pady=4)
        self.details = ttk.Frame(ui, padding=(0, 4))
        self.details.grid(row=4, column=0, columnspan=2, sticky="ew")
        self.details.grid_remove()
        sizes = [f"{w} × {h}" for w, h in camera_api.RESOLUTIONS] + ["最大解像度"]
        self.controls = []
        for row, (label, variable, choices) in enumerate((
                ("撮影解像度", self.size, sizes), ("要求FPS", self.fps, ("15", "24", "25", "30", "50", "60")),
                ("映像形式", self.video_format, tuple(FORMAT_LABELS.values())),
                ("追跡方式", self.tracking_mode, tuple(MODE_LABELS.values())))):
            ttk.Label(self.details, text=label).grid(row=row, column=0, sticky="w")
            field = ttk.Combobox(self.details, textvariable=variable, values=choices, state="readonly")
            field.grid(row=row, column=1, sticky="w", padx=(12, 0), pady=2)
            if variable != self.tracking_mode:
                field.bind("<<ComboboxSelected>>", self.settings_changed)
            self.controls.append(field)
        self.save_button = ttk.Button(ui, text="設定を保存", command=self.save)
        self.save_button.grid(row=5, column=0, sticky="w", pady=(6, 4))
        self.start_button = ttk.Button(ui, text="ゲーム起動", command=self.start)
        self.start_button.grid(row=5, column=1, pady=(6, 4))
        ttk.Label(ui, textvariable=self.status, wraplength=630).grid(row=6, column=0, columnspan=2, sticky="w")
        ttk.Label(ui, textvariable=self.error, foreground="#a42020", wraplength=630).grid(
            row=7, column=0, columnspan=2, sticky="w")
        config_error = ""
        try:
            self.config = camera_api.load_camera_config()
        except RuntimeError as exc:
            config_error = str(exc)
        self.size.set("最大解像度" if self.config["use_max_resolution"] else
                      f"{self.config['width']} × {self.config['height']}")
        self.fps.set(str(self.config["fps"]))
        self.video_format.set(FORMAT_LABELS[self.config["video_format"]])
        self.tracking_mode.set(MODE_LABELS[self.config["tracking_mode"]])
        self.rescan(auto_select=not config_error)
        if config_error:
            self.error.set(config_error)
        root.after(0, self.poll)

    def toggle_details(self):
        if self.details.winfo_ismapped():
            self.details.grid_remove()
        else:
            self.details.grid()

    def rescan(self, auto_select=True):
        config = self.get_config() if self.devices.current() >= 0 else self.config
        self.stop_preview()
        self.pending = None
        self.selection = []
        self.device.set("")
        self.devices["values"] = ()
        try:
            self.selection = camera_api.discover_cameras(config["camera_backend"])
            self.devices["values"] = [f"{cam.name}  [{cam.index}]" for cam in self.selection]
            if auto_select:
                chosen = camera_api.resolve_camera(config, self.selection)
                self.devices.current(self.selection.index(chosen))
                self.settings_changed()
        except Exception as exc:
            self.status.set("カメラを選択してください。")
            self.error.set(f"{exc} カメラを再選択してください。")
            camera_api.log(f"[launcher] {exc}")

    def get_config(self):
        index = self.devices.current()
        if index < 0:
            raise RuntimeError("使用するカメラを選択してください。")
        config = camera_api.camera_config_for(self.config, self.selection[index])
        config["use_max_resolution"] = self.size.get() == "最大解像度"
        if not config["use_max_resolution"]:
            config["width"], config["height"] = map(int, self.size.get().split(" × "))
        config["fps"] = int(self.fps.get())
        config["video_format"] = next(key for key, value in FORMAT_LABELS.items() if value == self.video_format.get())
        config["tracking_mode"] = next(key for key, value in MODE_LABELS.items()
                                       if value == self.tracking_mode.get())
        return config

    def stop_preview(self):
        if self.check:
            self.check.stop.set()
        if self.camera and not self.camera.stop_event.is_set():
            self.camera_stop_started = time.monotonic()
            self.camera.stop_event.set()
        self.photo = None
        self.matrix = None
        self.preview.configure(image="", text="カメラ映像")
        self.metrics.set("撮影解像度・実測FPS —")

    def settings_changed(self, *_):
        self.error.set("")
        self.stop_preview()
        self.pending = "preview"
        self.status.set("カメラを準備しています…")

    def toggle_detection(self):
        if self.check:
            self.check.stop.set()
        # Redraw without the old overlay on the next display tick.
        self.sequence = 0

    def save(self):
        try:
            self.config = self.get_config()
            camera_api.save_camera_config(self.config)
            self.status.set("設定を保存しました。")
            return True
        except (OSError, RuntimeError, ValueError) as exc:
            self.error.set(str(exc))
            return False

    def start(self):
        self.error.set("")
        if not (DIST_ROOT / "FruitCatch.exe").is_file():
            self.error.set("ゲーム本体 FruitCatch.exe が見つかりません。")
            return
        if self.save():
            self.stop_preview()
            self.pending = "game"
            self.set_busy(True)
            self.status.set("プレビューを解放してゲームを準備しています…")

    def set_busy(self, active):
        for widget in (self.search, self.save_button, self.start_button):
            widget.configure(state="disabled" if active else "normal")
        for widget in (self.devices, *self.controls):
            widget.configure(state="disabled" if active else "readonly")

    def start_tracker(self):
        self.token = uuid4().hex
        env = os.environ.copy()
        env.update(PYTHONUTF8="1", PYTHONIOENCODING="utf-8", PYTHONUNBUFFERED="1",
                   FRUITCATCH_LAUNCH_TOKEN=self.token)
        python = Path(sys.executable).with_name("python.exe")
        with TRACKER_LOG.open("wb") as output:
            self.tracker_process = subprocess.Popen(
                [str(python), "-X", "utf8", "-u", "face_tracker_registered.py"], cwd=TRACKING_ROOT,
                env=env, stdin=subprocess.PIPE, stdout=output, stderr=subprocess.STDOUT,
                creationflags=subprocess.CREATE_NO_WINDOW)
        self.start_deadline = time.monotonic() + 45
        self.status.set("ゲーム用カメラと判定モデルを準備しています…")

    def stop_tracker(self):
        process = self.tracker_process
        if process is not None:
            if process.stdin and not process.stdin.closed:
                process.stdin.close()
            if process.poll() is None:
                try:
                    process.wait(timeout=6)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()
            self.tracker_process = None

    def poll_game(self):
        if self.game is not None:
            if self.game.poll() is not None:
                result = self.game.returncode
                self.game = None
                self.stop_tracker()
                self.set_busy(False)
                self.pending = "preview"
                self.status.set("ゲームが終了しました。")
                if result:
                    self.error.set(f"ゲーム終了コード {result}。ログ: {GAME_LOG}")
            elif self.tracker_process.poll() is not None:
                self.error.set(f"判定プログラムが終了しました。{latest_error()} ログ: {TRACKER_LOG}")
            elif time.monotonic() >= self.next_health_check:
                self.next_health_check = time.monotonic() + 1
                health = self.read_health()
                self.error.set(health.get("error") or (
                    "" if health.get("ready") else "カメラフレームを待っています。"))
            return

        if self.tracker_process is None:
            return
        if self.tracker_process.poll() is not None:
            raise RuntimeError(f"判定プログラムが終了しました。{latest_error()}")
        health = self.read_health()
        if health.get("token") == self.token:
            if health.get("ready"):
                self.game = subprocess.Popen([str(DIST_ROOT / "FruitCatch.exe"), "-logFile", str(GAME_LOG)], cwd=DIST_ROOT)
                self.status.set("ゲーム実行中")
                self.error.set("")
                return
            if health.get("error"):
                self.error.set(health["error"])
        if time.monotonic() > self.start_deadline:
            raise RuntimeError(f"判定プログラムを準備できませんでした。{latest_error()}")

    def read_health(self):
        try:
            with urlopen(f"http://127.0.0.1:{camera_api.PREVIEW_PORT}/health", timeout=0.16) as response:
                health = json.load(response)
                if health.get("token") == self.token:
                    return health
        except (OSError, URLError, ValueError):
            pass
        return {}

    def render(self):
        if self.camera.error:
            self.error.set(self.camera.error)
        frame = self.camera.latest(self.sequence)
        if frame is None:
            if self.camera.latest() is None and self.photo is not None:
                self.preview.configure(image="", text="カメラ映像を待っています")
                self.photo = None
                self.metrics.set("撮影解像度・実測FPS —")
            return
        self.sequence, raw, _ = frame
        h, w = raw.shape[:2]
        image = tracker.mirrored(tracker.resize_to_max_width(raw, 640)).copy()
        if self.detection.get() and self.check:
            if self.check.error:
                self.error.set(f"検出確認: {self.check.error}")
            result = self.check.result
            if result and time.monotonic() - result[0] < 0.5:
                self.draw_detections(image, result)
        rgb = cv2.cvtColor(image, cv2.COLOR_BGR2RGB)
        height, width = rgb.shape[:2]
        ppm = f"P6 {width} {height} 255\n".encode("ascii") + rgb.tobytes()
        first = self.photo is None
        self.photo = tk.PhotoImage(data=ppm, format="PPM")
        self.preview.configure(image=self.photo, text="")
        self.metrics.set(f"実際の撮影 {w} × {h} ｜ {self.camera.rate.value():.1f} fps ｜ "
                         f"{self.camera.fourcc or '形式未報告'}")
        if first:
            self.status.set("カラープレビュー")
            self.error.set("")

    @staticmethod
    def draw_detections(image, result):
        _, width, height, faces, poses = result
        h, w = image.shape[:2]
        for face in faces:
            x, y, fw, fh = face.rect
            cv2.rectangle(image, (int(x * w / width), int(y * h / height)),
                          (int((x + fw) * w / width), int((y + fh) * h / height)), (0, 255, 0), 2)
        for pose in poses:
            def point(index):
                return int(pose[index].x * w), int(pose[index].y * h)

            for a, b in ((0, 11), (0, 12), (11, 12), (11, 13), (13, 15), (12, 14), (14, 16),
                         (11, 23), (12, 24), (23, 24), (23, 25), (24, 26), (25, 27), (26, 28)):
                if tracker.visible(pose[a]) and tracker.visible(pose[b]):
                    cv2.line(image, point(a), point(b), (255, 180, 0), 2)

    def poll(self):
        try:
            if self.check and not self.check.thread.is_alive() and self.check.stop.is_set():
                self.check = None
            if self.camera and self.camera.stop_event.is_set() and not self.camera.thread.is_alive():
                self.camera = None
                self.camera_lease.close()
                self.camera_lease = None
            elif (self.camera and self.camera.stop_event.is_set() and self.pending and
                    time.monotonic() - self.camera_stop_started > 6):
                self.pending = None
                self.set_busy(False)
                raise RuntimeError("カメラの解放に時間がかかっています。ランチャーを閉じてカメラの接続を確認してください。")
            if self.closing and self.camera is None:
                self.root.destroy()
                return
            if not self.closing and self.pending and self.camera is None:
                action, self.pending = self.pending, None
                if action == "game":
                    self.start_tracker()
                else:
                    config = self.get_config()
                    self.camera_lease = camera_api.exclusive_socket(camera_api.COMMAND_PORT, socket.SOCK_DGRAM)

                    def open_selected(stop):
                        index, backend, name = camera_api.select_camera(config)
                        return camera_api.configure_camera(index, backend, name, config, stop)

                    self.camera = camera_api.LatestCamera(open_selected)
                    self.sequence = 0
                    self.camera.start()
            self.poll_game()
            if self.camera and not self.camera.stop_event.is_set():
                if self.detection.get() and self.check is None:
                    self.check = DetectionCheck(self.camera)
                self.render()
        except Exception as exc:
            traceback.print_exc()
            if self.game is None:
                self.stop_tracker()
                self.set_busy(False)
            if self.camera is None and self.camera_lease is not None:
                self.camera_lease.close()
                self.camera_lease = None
            self.status.set("処理を完了できませんでした。")
            self.error.set(f"{exc} ログ: {LOG_ROOT}")
        self.root.after(230 if self.tracker_process else 32, self.poll)

    def close(self):
        if self.game is not None and self.game.poll() is None:
            if not messagebox.askyesno("FruitCatch", "ゲームとランチャーを終了しますか？", parent=self.root):
                return
            self.game.terminate()
        self.stop_tracker()
        self.pending = None
        self.closing = True
        self.stop_preview()
        self.set_busy(True)
        self.status.set("カメラを解放しています…")
        # A driver can block inside read(); process exit still releases the device.
        self.root.after(2500, self.root.destroy)


if __name__ == "__main__":
    root = tk.Tk()
    root.withdraw()
    try:
        with configure_logging():
            try:
                import cv2
                import fruitcatch_camera as camera_api
                import face_tracker_registered as tracker

                with camera_api.exclusive_socket(camera_api.LAUNCHER_PORT, socket.SOCK_STREAM) as lease:
                    lease.listen(1)
                    print(f"[launcher] {time.strftime('%Y-%m-%d %H:%M:%S')}")
                    Launcher(root)
                    root.deiconify()
                    root.mainloop()
            except Exception:
                traceback.print_exc()
                raise
    except Exception as exc:
        messagebox.showerror("FruitCatch", f"{exc}\nSetup FruitCatch.bat と Logs フォルダーを確認してください。", parent=root)
        root.destroy()
