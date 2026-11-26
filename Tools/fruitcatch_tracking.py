"""Associate a YuNet face with a pose and track geometry without identity models."""

from __future__ import annotations

from dataclasses import dataclass
import math


@dataclass(frozen=True)
class Observation:
    x: float
    y: float
    size: float
    shape: float = 1.0


@dataclass(frozen=True)
class Body:
    shoulders: Observation
    nose: tuple[float, float] | None


def visible(point, threshold=0.5):
    return (getattr(point, "visibility", 1.0) >= threshold and
            getattr(point, "presence", 1.0) >= threshold and
            math.isfinite(point.x) and math.isfinite(point.y))


def collect_bodies(poses, aspect):
    bodies = []
    for points in poses:
        if len(points) <= 24 or not (visible(points[11]) and visible(points[12])):
            continue
        left, right = points[11], points[12]
        x = (left.x + right.x) / 2
        y = (left.y + right.y) / 2 * aspect
        width = math.hypot(left.x - right.x, (left.y - right.y) * aspect)
        if not (0 <= x <= 1 and 0 <= y <= aspect and width >= 0.025):
            continue
        nose = (points[0].x, points[0].y * aspect) if visible(points[0]) else None
        if visible(points[23]) and visible(points[24]):
            hip_x = (points[23].x + points[24].x) / 2
            hip_y = (points[23].y + points[24].y) / 2 * aspect
            shape = max(0.25, min(4.0, math.hypot(x - hip_x, y - hip_y) / width))
        else:
            shape = 0.0  # Cropped lower bodies are still valid.
        bodies.append(Body(Observation(x, y, width, shape), nose))
    return bodies


def face_observation(face, frame_width):
    _, _, w, h = face.rect
    return Observation(face.center[0] / frame_width, face.center[1] / frame_width,
                       math.sqrt(w * h) / frame_width, w / h)


def associate_body(face_index, faces, bodies, frame_width):
    """Use the nose landmark and face extent in the same mirrored frame."""
    def score(face, body):
        if body.nose is None:
            return math.inf
        x, y = face.center
        _, _, w, h = face.rect
        dx = (body.nose[0] * frame_width - x) / w
        dy = (body.nose[1] * frame_width - y) / h
        return math.hypot(dx, dy)

    face = faces[face_index]
    candidates = sorted(((score(face, body), i) for i, body in enumerate(bodies)))
    if not candidates or candidates[0][0] > 0.67:
        return None
    distance, index = candidates[0]
    if len(candidates) > 1 and candidates[1][0] < distance + 0.23:
        return None
    body = bodies[index]
    if any(i != face_index and score(other, body) < distance + 0.15
           for i, other in enumerate(faces)):
        return None
    return body


class TargetLock:
    """Bounded prediction, ambiguity rejection and confirmed reacquisition."""

    def __init__(self, target, now):
        self.target = target
        self.seen_at = now
        self.vx = self.vy = 0.0
        self.missed = False
        self.ambiguous = False
        self.candidate = None
        self.confirmations = 0

    def select(self, observations, now):
        self.ambiguous = False
        dt = max(0.001, now - self.seen_at)
        predicted_x = self.target.x + self.vx * min(0.25, dt)
        predicted_y = self.target.y + self.vy * min(0.25, dt)
        gate = min(0.15, 0.03 + self.target.size * 0.3 + min(dt, 0.35) * 0.15)
        if dt > 1.2:
            # After a long absence only accept a return near the last known location.
            predicted_x, predicted_y = self.target.x, self.target.y
            gate = min(gate, 0.065)
        ranked = []
        for item in observations:
            size_error = abs(math.log(item.size / self.target.size))
            if size_error > (0.44 if dt < 1.2 else 0.26):
                continue
            distance = math.hypot(item.x - predicted_x, item.y - predicted_y)
            if distance > gate:
                continue
            shape_error = (abs(math.log(item.shape / self.target.shape))
                           if item.shape and self.target.shape else 0.0)
            ranked.append((distance / gate + size_error * 0.9 + shape_error * 0.16, item))
        ranked.sort(key=lambda entry: entry[0])
        self.ambiguous = len(ranked) > 1 and (
            ranked[1][0] - ranked[0][0] < 0.28 or
            math.hypot(ranked[1][1].x - ranked[0][1].x,
                       ranked[1][1].y - ranked[0][1].y) < min(0.065, self.target.size * 0.65))
        if not ranked or self.ambiguous:
            self.mark_missing()
            return None
        item = ranked[0][1]
        if self.missed or dt > 0.35:
            if self.candidate is None or math.hypot(
                    item.x - self.candidate.x, item.y - self.candidate.y) > 0.065:
                self.confirmations = 1
            else:
                self.confirmations += 1
            self.candidate = item
            if self.confirmations < (3 if dt > 1.2 else 2):
                return None
            self.vx = self.vy = 0.0
        else:
            self.vx = self._velocity(self.vx, (item.x - self.target.x) / dt)
            self.vy = self._velocity(self.vy, (item.y - self.target.y) / dt)
        self.target = Observation(item.x, item.y, 0.75 * self.target.size + 0.25 * item.size,
                                  item.shape or self.target.shape)
        self.seen_at = now
        self.missed = False
        self.candidate = None
        self.confirmations = 0
        return item.x

    def mark_missing(self):
        self.missed = True
        self.candidate = None
        self.confirmations = 0

    @staticmethod
    def _velocity(previous, measured):
        return 0.45 * previous + 0.55 * max(-1.3, min(1.3, measured))
