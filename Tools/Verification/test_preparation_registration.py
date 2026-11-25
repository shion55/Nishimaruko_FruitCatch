"""Camera-free checks for Preparation's guide/hold/session protocol.

Run: python -m unittest discover -s Tools/Verification -p test_preparation_registration.py
"""
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import face_tracker_registered as tracker


REGION = (0.06, 0.24, 0.42, 0.64)  # Same normalized region as the scene, top-left origin.
TOKEN = "A" * 32


class Commands:
    def __init__(self, *commands):
        self.commands = list(commands)

    def recvfrom(self, _):
        if not self.commands:
            raise BlockingIOError()
        return self.commands.pop(0).encode(), ("127.0.0.1", 5006)


class Status:
    def sendto(self, *_):
        pass


class PreparationRegistrationTests(unittest.TestCase):
    def setUp(self):
        self.state = tracker.TrackingState(pending_register=True, auto_register=True, guide_region=REGION)
        self.frame = np.zeros((480, 640, 3), dtype=np.uint8)
        self.status = Status()

    def face(self, cx=0.27, cy=0.58, width=0.16, height=0.36):
        return tracker.Face((0, 0, int(width * 640), int(height * 480)),
                            (cx * 640, cy * 480))

    def tick(self, now, faces=None):
        with patch.object(tracker.time, "monotonic", return_value=now):
            return tracker.process_registration(self.state, self.frame,
                                                [self.face()] if faces is None else faces, self.status)

    def test_visible_frame_checks_position_but_not_face_size(self):
        self.assertEqual(tracker.check_alignment(self.face(), 640, 480, REGION), "HOLD")
        self.assertEqual(tracker.check_alignment(self.face(cx=0.55), 640, 480, REGION), "MOVE_CENTER")
        self.assertEqual(tracker.check_alignment(self.face(width=0.03, height=0.05), 640, 480, REGION), "HOLD")
        self.assertEqual(tracker.check_alignment(self.face(width=0.7, height=0.9), 640, 480, REGION), "HOLD")

    def test_three_seconds_then_register(self):
        for i in range(30):
            self.tick(i / 10)
            self.assertFalse(self.state.active)
        self.tick(3.01)
        self.assertTrue(self.state.active)
        self.assertEqual(self.state.progress, 1)

    def test_short_dropout_pauses_without_reset_or_counting_missing_time(self):
        for i in range(11):
            self.tick(i / 10)
        self.tick(1.1, [])
        self.tick(1.2)
        self.assertAlmostEqual(self.state.progress, 1 / 3)
        self.tick(1.3)
        self.assertAlmostEqual(self.state.progress, 1.1 / 3)

    def test_long_dropout_resets_even_with_only_one_missing_sample(self):
        self.tick(0)
        self.tick(0.1)
        self.tick(0.2, [])
        self.tick(0.6)
        self.assertEqual(self.state.progress, 0)

    def test_low_processing_rate_never_completes_on_stale_samples(self):
        for i in range(8):
            self.tick(float(i))
        self.assertFalse(self.state.active)
        self.assertGreater(self.state.progress, 0)
        for i in range(8, 15):
            self.tick(float(i))
        self.assertTrue(self.state.active)

    def test_detection_outside_oval_does_not_block_alignment(self):
        other = self.face(cx=0.75)
        for i in range(32):
            result = self.tick(i / 10, [other, self.face()])
            if self.state.active:
                self.assertEqual(result, 1)
                break
        self.assertTrue(self.state.active)

    def test_multiple_faces_or_leaving_oval_reset_progress(self):
        self.tick(0)
        self.tick(0.1)
        self.tick(0.2, [self.face(), self.face(cx=0.3)])
        self.assertEqual(self.state.hint, "MULTIPLE_FACES")
        self.assertEqual(self.state.progress, 0)
        self.tick(0.3)
        self.tick(0.4)
        self.tick(0.5, [self.face(cx=0.6)])
        self.assertEqual(self.state.progress, 0)
        self.assertEqual(self.state.hint, "MOVE_CENTER")

    def test_immediate_registration_needs_no_identity_profile(self):
        self.state.auto_register = False
        self.assertEqual(self.tick(0), 0)
        self.assertTrue(self.state.active)

    def test_command_retries_are_idempotent_but_new_session_clears_registration(self):
        command = f"AUTO_REGION {TOKEN} 0.16 0.32 0.22 0.52"
        tracker.read_commands(Commands(command), self.state, self.status)
        self.tick(0)
        self.tick(0.1)
        tracker.read_commands(Commands(command), self.state, self.status)
        self.assertGreater(self.state.progress, 0)
        self.state.active = True
        tracker.read_commands(Commands(command.replace(TOKEN, "B" * 32)), self.state, self.status)
        self.assertFalse(self.state.active)
        self.assertEqual(self.state.progress, 0)
        self.assertEqual(self.state.registration_id, "B" * 32)

    def test_invalid_region_does_not_replace_valid_session(self):
        self.state.registration_id = TOKEN
        for bounds in ("nan 0 0.2 0.5", "0.9 0 0.2 0.5", "0 0 -1 1"):
            tracker.read_commands(Commands(f"AUTO_REGION {'B' * 32} {bounds}"), self.state, self.status)
            self.assertEqual(self.state.registration_id, TOKEN)

    def test_preview_metadata_has_session_and_progress_from_same_snapshot(self):
        shared = tracker.PreviewBuffer()
        shared.update(self.frame, 0.4, "HOLD", False, True, TOKEN)
        frame, *metadata = shared.snapshot()
        self.assertIs(frame, self.frame)
        self.assertEqual(metadata, [0.4, "HOLD", False, True, TOKEN])


if __name__ == "__main__":
    unittest.main()
