"""Camera-free regressions for the Windows camera, tracking and HTTP pipeline."""

from contextlib import contextmanager
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import Mock, patch
from urllib.error import HTTPError
from urllib.request import urlopen
from itertools import chain, repeat
import json
import socket
import sys
import tempfile
import threading
import time
import unittest

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import face_tracker_registered as tracker
import fruitcatch_camera as camera
import fruitcatch_launcher as gui
import fruitcatch_preview as preview
from fruitcatch_tracking import Body, Observation, TargetLock, associate_body, collect_bodies


class TrackingTests(unittest.TestCase):
    def test_small_detection_at_analysis_resolution_and_reject_invalid_boxes(self):
        rows = np.zeros((4, 15), np.float32)
        rows[:, :4] = [(-20, 100, 100, 120), (200, 100, 18, 20),
                       (200, 100, 10, 10), (np.nan, 0, 100, 100)]
        faces = tracker.collect_faces(rows, 960, 540)
        self.assertEqual([face.rect for face in faces], [(0, 100, 80, 120), (200, 100, 18, 20)])

    def test_pose_hands_and_face_association_use_the_matching_person(self):
        def pose(x, wrist_y):
            points = [SimpleNamespace(x=x, y=0.25, visibility=1, presence=1) for _ in range(33)]
            points[11] = SimpleNamespace(x=x - 0.075, y=0.4)
            points[12] = SimpleNamespace(x=x + 0.125, y=0.4)
            points[15].y = points[16].y = wrist_y
            return points

        poses = [pose(0.75, 0.6), pose(0.35, 0.2)]
        self.assertTrue(tracker.has_raised_both_hands(poses))
        self.assertFalse(tracker.has_raised_both_hands(poses[:1]))
        bodies = collect_bodies(poses, 0.5625)
        small_face = tracker.Face((328, 124, 16, 22), (336, 135))
        audience_face = tracker.Face((700, 115, 40, 40), (720, 135))
        selected = associate_body(1, [audience_face, small_face], bodies, 960)
        self.assertIs(selected, bodies[1])
        self.assertAlmostEqual(selected.shoulders.x, 0.375)
        self.assertIsNone(associate_body(1, [audience_face, small_face], bodies[:1], 960))
        self.assertIsNone(associate_body(
            0, [small_face], [bodies[1], Body(bodies[0].shoulders, bodies[1].nose)], 960))

    def test_audience_arrival_and_direction_at_crossing(self):
        def person(x):
            return Observation(x, 0.25, 0.16)

        lock = TargetLock(person(0.35), 0)
        self.assertEqual(lock.select([person(0.68), person(0.39)], 0.10), 0.39)
        self.assertEqual(lock.select([person(0.60), person(0.433)], 0.20), 0.433)
        self.assertIsNone(lock.select([person(0.48), person(0.48)], 0.30))
        self.assertIsNone(lock.select([person(0.425), person(0.525)], 0.40))
        self.assertEqual(lock.select([person(0.38), person(0.55)], 0.46), 0.55)

    def test_loss_does_not_lock_to_distant_or_differently_sized_people(self):
        player = Observation(0.38, 0.25, 0.18)
        other = Observation(0.37, 0.25, 0.076)
        distant = Observation(0.65, 0.25, 0.18)
        lock = TargetLock(player, 0)
        self.assertIsNone(lock.select([other, distant], 0.1))
        self.assertIsNone(lock.select([], 0.2))
        self.assertIsNone(lock.select([other, player], 0.3))
        self.assertAlmostEqual(lock.select([player, distant], 0.36), player.x)
        for t in (1.8, 1.9, 2.0):
            self.assertIsNone(lock.select([other, distant], t))
        for t in (2.1, 2.2):
            self.assertIsNone(lock.select([player], t))
        self.assertAlmostEqual(lock.select([player], 2.3), player.x)

    def test_tracker_sends_existing_udp_format_for_both_tracking_modes(self):
        command = Mock()
        status = Mock()
        position = Mock()
        frame = np.zeros((360, 640, 3), np.uint8)
        face = tracker.Face((210, 80, 28, 24), (224, 92))
        person = Body(Observation(0.40, 0.28, 0.14), (0.35, 92 / 640))
        spectator = Body(Observation(0.67, 0.28, 0.14), (0.67, 92 / 640))

        for mode, expected, face_calls, pose_calls in (
                ("pose", b"0.4000", 1, 2), ("face", b"0.3500", 2, 1)):
            with self.subTest(mode=mode):
                command.recvfrom.side_effect = chain([(b"REGISTER", None)], repeat(BlockingIOError()))
                position.reset_mock()
                status.reset_mock()
                stop = threading.Event()
                cam = Mock()

                def next_frame(sequence):
                    if sequence == 2:
                        stop.set()
                        return None
                    return sequence + 1, frame, time.monotonic()

                cam.next_frame.side_effect = next_frame
                stream = Mock()
                stream.is_requested.return_value = False
                pose = Mock()
                with (patch.object(tracker, "detect_faces", return_value=[face]) as detect_faces,
                      patch.object(tracker, "collect_bodies", side_effect=lambda poses, _: (
                          [spectator, person] if poses else [])),
                      patch.object(tracker, "TRACKING_FPS", 200)):
                    tracker.track(cam, stream, command, status, position, Mock(), pose, mode, stop)
                self.assertEqual(detect_faces.call_count, face_calls)
                self.assertEqual(pose.detect.call_count, pose_calls)
                self.assertEqual(position.sendto.call_count, 2)
                self.assertEqual(position.sendto.call_args.args, (expected, ("127.0.0.1", 5015)))
                self.assertEqual([x.args[0] for x in status.sendto.call_args_list],
                                 [b"REGISTERING", b"REGISTERED", b"TRACKING"])


class CameraTests(unittest.TestCase):
    def test_saved_camera_does_not_follow_reordered_index(self):
        one = SimpleNamespace(name="カメラ", index=1, path="usb:a", backend=700)
        two = SimpleNamespace(name="カメラ", index=0, path="usb:b", backend=700)
        saved = camera.camera_config_for(camera.DEFAULTS, one)
        with tempfile.TemporaryDirectory() as directory:
            file = Path(directory) / "camera_config.json"
            camera.save_camera_config(saved, file)
            saved = camera.load_camera_config(file)
        self.assertEqual(saved["camera_name"], "カメラ")
        self.assertIs(camera.resolve_camera(saved, [two, one]), one)
        self.assertIs(camera.resolve_camera(camera.camera_config_for(saved, two), [one, two]), two)
        with self.assertRaises(RuntimeError):
            camera.resolve_camera(saved, [two])
        with self.assertRaises(RuntimeError):
            camera.resolve_camera(dict(saved, camera_path=""), [one, two])

    def test_gray_frames_convert_and_invalid_formats_are_rejected(self):
        gray = np.zeros((10, 20), np.uint8)
        self.assertEqual(camera.color_frame(gray, "Y800").shape, (10, 20, 3))
        raw = np.array([[[90, 255], [90, 60]]], np.uint8)
        self.assertEqual(camera.color_frame(raw, "YUY2").shape, (1, 2, 3))
        with self.assertRaises(ValueError):
            camera.color_frame(raw, "UNKNOWN")
        gray_jpeg = cv2.imencode(".jpg", np.full((16, 16), 80, np.uint8))[1].reshape(1, -1)
        self.assertEqual(camera.color_frame(gray_jpeg, "MJPG").shape, (16, 16, 3))

    def test_auto_falls_back_from_corrupt_mjpg_to_driver_format(self):
        corrupt = Mock()
        corrupt.get.return_value = cv2.VideoWriter_fourcc(*"MJPG")
        corrupt.read.return_value = True, np.array([[255, 216, 0, 0]], np.uint8)
        color = Mock()
        color.get.return_value = cv2.VideoWriter_fourcc(*"YUY2")
        color.read.return_value = True, np.tile(np.array([30, 60, 90], np.uint8), (4, 4, 1))
        with patch.object(camera.cv2, "VideoCapture", side_effect=[corrupt, color]):
            chosen = camera.configure_camera(0, 700, "test", camera.DEFAULTS)
        self.assertIs(chosen, color)
        corrupt.release.assert_called_once()
        color.release.assert_not_called()

    def test_latest_frame_drops_backlog_and_reopens_same_factory_after_failure(self):
        unblock = threading.Event()
        replacement = threading.Event()
        colored = np.tile(np.array([5, 90, 220], np.uint8), (4, 4, 1))
        original = Mock()
        original.get.return_value = cv2.VideoWriter_fourcc(*"MJPG")
        reads = iter([colored, colored, colored])

        def read():
            frame = next(reads, None)
            if frame is None:
                unblock.wait(2)
            return frame is not None, frame

        original.read.side_effect = read
        recovered = Mock()
        recovered.get.return_value = cv2.VideoWriter_fourcc(*"YUY2")

        def recovered_read():
            replacement.wait(2)
            time.sleep(0.005)
            return True, colored

        recovered.read.side_effect = recovered_read
        factory = Mock(side_effect=[original, recovered])
        cap = camera.LatestCamera(factory)
        cap.start()
        try:
            with cap.condition:
                self.assertTrue(cap.condition.wait_for(lambda: cap.sequence == 3, timeout=1))
            self.assertEqual(cap.latest()[0], 3)
            self.assertIsNone(cap.next_frame(3, timeout=0.01))
            unblock.set()
            with cap.condition:
                self.assertTrue(cap.condition.wait_for(lambda: cap.error, timeout=1))
            self.assertIsNone(cap.latest())
            replacement.set()
            self.assertIsNotNone(cap.next_frame(3, timeout=2))
            self.assertEqual(cap.fourcc, "YUY2")
        finally:
            unblock.set()
            replacement.set()
            self.assertTrue(cap.close())
        original.release.assert_called_once()
        recovered.release.assert_called_once()

    def test_second_camera_owner_cannot_bind_the_first_owner_port(self):
        with camera.exclusive_socket(0, socket.SOCK_DGRAM) as owner:
            with self.assertRaises(RuntimeError):
                camera.exclusive_socket(owner.getsockname()[1], socket.SOCK_DGRAM)


class PreviewTests(unittest.TestCase):
    @contextmanager
    def streaming(self, cap):
        stream = preview.PreviewStream(cap)
        with preview.PreviewServer(("127.0.0.1", 0), preview.make_preview_handler(stream)) as server:
            thread = threading.Thread(target=server.serve_forever, kwargs={"poll_interval": 0.02}, daemon=True)
            thread.start()
            stream.thread.start()
            try:
                yield (stream, f"http://127.0.0.1:{server.server_port}")
            finally:
                stream.stop_event.set()
                stream.thread.join(1)
                server.shutdown()
                thread.join(1)

    @staticmethod
    def synthetic_camera():
        class SyntheticCamera:
            error = None
            rate = camera.Rate()
            frame = np.zeros((80, 120, 3), np.uint8)
            frame[:, :60] = (10, 60, 200)
            enabled = True

            def next_frame(self, sequence=0, timeout=0):
                time.sleep(0.005)
                if self.enabled:
                    return sequence + 1, self.frame, time.monotonic()
                return None

            latest = next_frame

        return SyntheticCamera()

    def test_live_jpeg_uses_camera_without_inference_and_deduplicates_http(self):
        cap = self.synthetic_camera()
        with self.streaming(cap) as (stream, origin):
            with urlopen(origin + "/health", timeout=2) as response:
                self.assertEqual(json.load(response)["inference_fps"], 0)
            self.assertFalse(stream.is_requested())
            stream.request_frame()
            time.sleep(0.25)
            cap.enabled = False
            time.sleep(0.06)
            with urlopen(origin + "/frame.jpg", timeout=2) as response:
                jpeg = response.read()
                key = response.headers["X-Stream-Id"] + "-" + response.headers["X-Frame-Sequence"]
                self.assertGreater(float(response.headers["X-jpeg-fps"]), 20)
                self.assertEqual(response.headers["X-inference-fps"], "0.0")
            decoded = cv2.imdecode(np.frombuffer(jpeg, np.uint8), cv2.IMREAD_COLOR)
            self.assertLess(decoded[:, :40].mean(), 5)
            self.assertGreater(decoded[:, 80:, 2].mean(), 180)
            self.assertEqual(cap.frame[0, 0, 2], 200)
            with urlopen(origin + "/frame.jpg?after=" + key, timeout=2) as response:
                self.assertEqual(response.read(), b"")
            cap.error = "カメラ切断"
            with self.assertRaises(HTTPError) as err:
                urlopen(origin + "/frame.jpg", timeout=2)
            self.assertEqual(err.exception.code, 503)

    def test_registered_image_stays_pinned_and_new_registration_invalidates_it(self):
        cap = self.synthetic_camera()
        frozen = np.tile(np.array([110, 195, 5], np.uint8), (80, 120, 1))
        expected = preview.encode_preview(frozen)
        with self.streaming(cap) as (stream, origin):
            stream.request_frame()
            stream.publish(preview.PreviewStatus("A", 1, "DONE", time.monotonic()), frozen)
            deadline = time.monotonic() + 1
            while not stream.registration_image("A") and time.monotonic() < deadline:
                time.sleep(0.01)
            with urlopen(origin + "/registration.jpg?id=A", timeout=2) as response:
                self.assertEqual(response.headers["X-Registration-Id"], "A")
                self.assertEqual(response.read(), expected)
            with urlopen(origin + "/frame.jpg", timeout=2) as response:
                self.assertEqual(response.headers["X-Registered"], "1")
                self.assertNotEqual(response.read(), expected)
            stream.publish(preview.PreviewStatus("B", 0, "NO_FACE"))
            with self.assertRaises(HTTPError) as err:
                urlopen(origin + "/registration.jpg?id=A", timeout=2)
            self.assertEqual(err.exception.code, 404)
            stream.publish(preview.PreviewStatus("B", 1, "DONE"), cap.frame)
            deadline = time.monotonic() + 1
            while not stream.registration_image("B") and time.monotonic() < deadline:
                time.sleep(0.01)
            self.assertEqual(stream.registration_image("B"), preview.encode_preview(cap.frame))
            stream.publish(preview.PreviewStatus("B", 0, "WAITING"), reset=True)
            self.assertIsNone(stream.registration_image("B"))


class LauncherLifecycleTests(unittest.TestCase):
    def test_ready_tracker_starts_game_and_exit_releases_tracker_then_resumes_preview(self):
        launcher = gui.Launcher.__new__(gui.Launcher)
        launcher.game = None
        launcher.tracker_process = Mock()
        tracker_process = launcher.tracker_process
        tracker_process.poll.return_value = None
        tracker_process.stdin.closed = False
        launcher.start_deadline = time.monotonic() + 10
        launcher.token = "owner"
        launcher.monitor = Mock(get=Mock(return_value=gui.DEFAULT_MONITOR))
        launcher.read_health = Mock(side_effect=[{}, {"token": "owner", "ready": True}])
        launcher.status = Mock()
        launcher.error = Mock()
        launcher.set_busy = Mock()
        game = Mock(returncode=0)
        game.poll.return_value = 0
        with patch.object(gui.subprocess, "Popen", return_value=game) as start:
            launcher.poll_game()
            start.assert_not_called()
            launcher.poll_game()
            start.assert_called_once()
            self.assertIn("FruitCatch.exe", start.call_args.args[0][0])
            launcher.poll_game()
        tracker_process.stdin.close.assert_called_once()
        tracker_process.wait.assert_called_once()
        self.assertIsNone(launcher.game)
        self.assertIsNone(launcher.tracker_process)
        self.assertEqual(launcher.pending, "preview")


if __name__ == "__main__":
    unittest.main()
