"""macOS-only operator menu and process supervisor."""

from contextlib import contextmanager
import fcntl
import json
import os
from pathlib import Path
import plistlib
import signal
import socket
import subprocess
import sys
import time
from urllib.request import ProxyHandler, build_opener
from uuid import uuid4

import fruitcatch_camera as camera_api


TRACKING = Path(__file__).resolve().parent
APP = TRACKING.parent / "FruitCatch.app"
PLAYER_PORT = 5015
LOCAL_HTTP = build_opener(ProxyHandler({}))


@contextmanager
def single_launcher():
    directory = Path.home() / "Library" / "Application Support" / "FruitCatch"
    directory.mkdir(parents=True, exist_ok=True)
    with (directory / "mac-launcher.lock").open("a") as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError as exc:
            raise RuntimeError("別の FruitCatch 起動メニューが開いています。") from exc
        yield


def bundle_info():
    with (APP / "Contents" / "Info.plist").open("rb") as plist:
        info = plistlib.load(plist)
    executable = info["CFBundleExecutable"]
    if not executable or Path(executable).name != executable:
        raise RuntimeError("FruitCatch.app の実行ファイル設定を読み込めません。")
    return info, APP / "Contents" / "MacOS" / executable


def ensure_player_can_start(info, executable):
    if not os.access(executable, os.X_OK):
        raise RuntimeError("Unity の実行権限がありません。Setup FruitCatch.command を実行してください。")
    if not info.get("NSMicrophoneUsageDescription"):
        raise RuntimeError("マイク使用目的が入っていません。Unity の macOS 配布メニューから再ビルドしてください。")

    from AppKit import NSWorkspace

    for application in NSWorkspace.sharedWorkspace().runningApplications():
        url = application.bundleURL()
        if url is not None and Path(url.path()).resolve() == APP:
            raise RuntimeError("FruitCatch.app がすでに起動しています。終了してから開始してください。")
    with camera_api.exclusive_socket(PLAYER_PORT, socket.SOCK_DGRAM):
        pass


def choose_camera(config):
    cameras = camera_api.discover_cameras(config["camera_backend"])
    if not cameras:
        print("カメラが見つかりません。接続と macOS のカメラの許可を確認してください。")
        return
    for index, camera in enumerate(cameras, 1):
        print(f"  {index}. {camera.name}  (AVFoundation #{camera.index})")
    answer = input("カメラの番号を入力してください（Enterで戻る）> ").strip()
    if not answer:
        return
    if not answer.isdecimal() or not 1 <= int(answer) <= len(cameras):
        print("一覧にある番号を入力してください。")
        return
    selected = cameras[int(answer) - 1]
    camera_api.save_camera_config(camera_api.camera_config_for(config, selected))
    print(f"選択したカメラを保存しました。 {selected.name}")


def wait_ready(tracker, token):
    url = f"http://127.0.0.1:{camera_api.PREVIEW_PORT}/health"
    deadline = time.monotonic() + 75
    camera_error = None
    while time.monotonic() < deadline:
        if tracker.poll() is not None:
            raise RuntimeError(f"トラッカーが終了しました（終了コード {tracker.returncode}）。"
                               "上のログでカメラ・モデル・ポートのエラーを確認してください。")
        try:
            with LOCAL_HTTP.open(url, timeout=0.5) as response:
                health = json.load(response)
        except (OSError, ValueError):
            time.sleep(0.2)
            continue
        if not isinstance(health, dict) or health.get("token") != token:
            raise RuntimeError("別のトラッカーが映像配信用ポートを使用しています。")
        if health.get("ready"):
            return
        camera_error = health.get("error")
        time.sleep(0.2)
    raise RuntimeError(camera_error or "カメラ映像と追跡の準備ができませんでした。"
                       "カメラの許可・選択・接続を確認してください。")


def stop_child(child, timeout):
    if child is None or child.poll() is not None:
        return
    child.terminate()
    try:
        child.wait(timeout=timeout)
    except subprocess.TimeoutExpired:
        child.kill()
        child.wait()


def play(config):
    info, executable = bundle_info()
    ensure_player_can_start(info, executable)
    camera_api.select_camera(config)
    token = uuid4().hex
    env = dict(os.environ, FRUITCATCH_LAUNCH_TOKEN=token)
    env.pop("FRUITCATCH_PREVIEW_MODE", None)
    game = None
    tracker = subprocess.Popen(
        [sys.executable, "-X", "utf8", "-u", "face_tracker_registered.py"],
        cwd=TRACKING, stdin=subprocess.PIPE, env=env, start_new_session=True)
    try:
        print("カメラ・追跡モデル・通信の準備を待っています…", flush=True)
        wait_ready(tracker, token)
        game = subprocess.Popen([str(executable)], cwd=APP.parent, start_new_session=True)
        while game.poll() is None:
            if tracker.poll() is not None:
                raise RuntimeError("トラッカーが停止しました。メニューからゲームを再起動してください。")
            time.sleep(0.2)
        if game.returncode != 0:
            print(f"ゲームが終了しました（終了コード {game.returncode}）。")
    finally:
        # EOF also stops the existing tracker if the launcher is unexpectedly closed.
        try:
            stop_child(game, 3)
        finally:
            tracker.stdin.close()
            try:
                tracker.wait(timeout=6)
            except subprocess.TimeoutExpired:
                stop_child(tracker, 2)


def main():
    with single_launcher():
        while True:
            print("\nFruitCatch macOS  [Enter/1] ゲーム開始  [2] カメラを選択  [Q] 終了")
            choice = input("> ").strip().lower()
            if choice == "q":
                return
            if choice not in ("", "1", "2"):
                continue
            try:
                config = camera_api.load_camera_config()
                if choice == "2":
                    choose_camera(config)
                else:
                    play(config)
            except (OSError, RuntimeError, ValueError, KeyError) as exc:
                print(f"FruitCatch: {exc}", file=sys.stderr)


def interrupt(_signal, _frame):
    raise KeyboardInterrupt


if __name__ == "__main__":
    signal.signal(signal.SIGHUP, interrupt)
    signal.signal(signal.SIGTERM, interrupt)
    try:
        main()
    except (EOFError, KeyboardInterrupt):
        pass
    except (OSError, RuntimeError) as exc:
        print(f"FruitCatch: {exc}", file=sys.stderr)
        sys.exit(1)
