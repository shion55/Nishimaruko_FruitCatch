"""Camera-free regressions for tracking latency, preview, and Unity's UDP protocol.

Run: python -m unittest discover -s Tools/Verification
"""
import io
import itertools
import sys
import threading
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import MagicMock, Mock, patch

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import face_tracker_registered as tracker


class SinglePersonTrackingTests(unittest.TestCase):
    def test_detection_boxes_are_clipped_and_invalid_or_small_faces_rejected(self):
        detections = np.zeros((4, 15), dtype=np.float32)
        detections[:, :4] = [(-20, 100, 100, 120), (200, 100, 10, 10),
                              (np.nan, 0, 100, 100), (700, 0, 100, 100)]
        faces = tracker.collect_faces(detections, 640, 360)
        self.assertEqual(len(faces), 1)
        self.assertEqual(faces[0].rect, (0, 100, 80, 120))
        self.assertEqual(faces[0].center, (40, 160))
        self.assertEqual(tracker.collect_faces(None, 640, 360), [])

    def test_preview_request_expires_and_can_resume_for_next_preparation(self):
        shared = tracker.PreviewBuffer()
        self.assertFalse(shared.is_requested())
        with patch.object(tracker.time, "monotonic", return_value=10):
            shared.request()
            self.assertTrue(shared.is_requested())
        with patch.object(tracker.time, "monotonic", return_value=11.1):
            self.assertFalse(shared.is_requested())
            shared.request()
            self.assertTrue(shared.is_requested())

    def test_http_preview_mirrors_image_without_changing_capture_and_keeps_metadata(self):
        shared = tracker.PreviewBuffer()
        frame = np.zeros((120, 200, 3), dtype=np.uint8)
        frame[:, :100] = 240
        shared.update(frame, 0.4, "HOLD", False, True, "A" * 32)
        handler_type = tracker.make_preview_handler(shared)
        handler = handler_type.__new__(handler_type)
        handler.path = "/frame.jpg"
        handler.wfile = io.BytesIO()
        handler.send_response = Mock()
        handler.send_header = Mock()
        handler.end_headers = Mock()
        handler.do_GET()
        handler.send_response.assert_called_once_with(200)
        headers = dict(call.args for call in handler.send_header.call_args_list)
        self.assertEqual(headers["X-Registration-Id"], "A" * 32)
        self.assertEqual(headers["X-Progress"], "0.400")
        self.assertEqual(headers["X-Hands-Raised"], "1")
        jpeg = handler.wfile.getvalue()
        self.assertEqual(headers["Content-Length"], str(len(jpeg)))
        preview = cv2.imdecode(np.frombuffer(jpeg, np.uint8), cv2.IMREAD_COLOR)
        self.assertLess(preview[:, :80].mean(), 10)
        self.assertGreater(preview[:, 120:].mean(), 230)
        self.assertGreater(frame[:, :80].mean(), 230)
        self.assertTrue(shared.is_requested())

    def test_capture_discards_backlog_and_never_reuses_a_frame(self):
        class Camera:
            def __init__(self):
                self.index = 0
                self.allow_more = threading.Event()
                self.released = False

            def read(self):
                self.index += 1
                if self.index > 3:
                    self.allow_more.wait(timeout=2)
                    return False, None
                return True, np.full((2, 2, 3), self.index, np.uint8)

            def release(self):
                self.released = True

        cap = Camera()
        camera = tracker.LatestCamera(cap)
        camera.start()
        try:
            with camera.condition:
                self.assertTrue(camera.condition.wait_for(lambda: camera.sequence == 3, timeout=1))
            sequence, frame, captured_at = camera.next_frame(0)
            self.assertEqual(sequence, 3)
            self.assertEqual(frame[0, 0, 0], 3)
            self.assertGreater(captured_at, 0)
            self.assertIsNone(camera.next_frame(sequence, timeout=0.01))
        finally:
            cap.allow_more.set()
            camera.close()
        self.assertTrue(cap.released)
        self.assertFalse(camera.thread.is_alive())
        with self.assertRaises(RuntimeError):
            camera.next_frame(sequence)

    def test_main_loop_sends_position_recovers_after_loss_and_skips_unused_pose_and_jpeg(self):
        frame = np.zeros((720, 1280, 3), np.uint8)
        rows = np.zeros((1, 15), np.float32)
        rows[0, :4] = (160, 100, 80, 100)
        moved_rows = rows.copy()
        moved_rows[0, 0] = 400
        detector = Mock()
        detector.detect.side_effect = [(1, rows), (1, None), (1, moved_rows), (1, np.vstack([rows, moved_rows]))]
        camera = Mock()
        camera.next_frame.side_effect = [(i, frame, float(i)) for i in range(1, 5)] + [KeyboardInterrupt]
        position, status, commands = Mock(), Mock(), Mock()
        commands.recvfrom.side_effect = itertools.chain(
            [(b"REGISTER", ("127.0.0.1", 5006))], itertools.repeat(BlockingIOError()))
        vision = MagicMock()
        pose = vision.PoseLandmarker.create_from_options.return_value.__enter__.return_value = Mock()
        mp = SimpleNamespace(tasks=SimpleNamespace(vision=vision, BaseOptions=Mock()))

        with (patch.dict(sys.modules, {"mediapipe": mp}),
              patch.object(tracker, "ensure_models"),
              patch.object(tracker, "select_camera", return_value=(0, 700, "Test")),
              patch.object(tracker, "configure_camera"),
              patch.object(tracker, "LatestCamera", return_value=camera),
              patch.object(tracker.cv2, "FaceDetectorYN", Mock(create=Mock(return_value=detector))),
              patch.object(tracker, "ThreadingHTTPServer"),
              patch.object(tracker.threading, "Thread"),
              patch.object(tracker.socket, "socket", side_effect=[position, status, commands]),
              patch.object(tracker.cv2, "imencode") as encode):
            with self.assertRaises(KeyboardInterrupt):
                tracker.run()

        self.assertEqual([call.args for call in position.sendto.call_args_list], [
            (b"0.3125", ("127.0.0.1", 5015)),
            (b"0.6875", ("127.0.0.1", 5015)),
        ])
        self.assertEqual([call.args[0] for call in status.sendto.call_args_list],
                         [b"REGISTERING", b"REGISTERED", b"LOST", b"TRACKING", b"LOST"])
        pose.detect_for_video.assert_not_called()
        encode.assert_not_called()
        camera.close.assert_called_once()


if __name__ == "__main__":
    unittest.main()
