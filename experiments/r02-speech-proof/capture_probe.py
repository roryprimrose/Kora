"""Annotation-driven bounds experiment, NOT production endpointing or word alignment."""

from collections import deque

import numpy as np

from fixtures import RATE


class CaptureProbe:
    def __init__(self):
        self.position = 0
        self.ring = deque()
        self.ring_samples = 0
        self.command = []
        self.state = "wake"
        self.generation = 0
        self.activation = 0
        self.command_start = 0
        self.last_speech = None
        self.reason = None

    def feed(self, pcm: np.ndarray, speech: bool = False) -> None:
        if pcm.dtype != np.int16 or pcm.ndim != 1 or len(pcm) != RATE // 100:
            raise ValueError("Capture probe requires consecutive 10 ms mono PCM16 frames")
        if self.state == "disabled":
            raise RuntimeError("Capture is disabled; explicit enable is required")
        self.position += len(pcm)
        self.ring.append(pcm.copy())
        self.ring_samples += len(pcm)
        while self.ring_samples > 2 * RATE:
            self.ring_samples -= len(self.ring.popleft())
        if self.state != "capturing":
            return
        remaining = max(0, 60 * RATE - (self.position - len(pcm) - self.command_start))
        self.command.append(pcm[:remaining].copy())
        if speech:
            self.last_speech = self.position
        if self.position - self.command_start >= 60 * RATE:
            self.finish("limit_60s")
        elif self.last_speech is None and self.position - self.activation >= 5 * RATE:
            self.finish("empty_5s")
        elif self.last_speech is not None and self.position - self.last_speech >= RATE:
            self.finish("silence_1s")

    def activate(self, command_start: int, generation: int, buffered_speech: bool = False) -> bool:
        if generation != self.generation or self.state != "wake":
            return False
        earliest = self.position - self.ring_samples
        if not earliest <= command_start <= self.position:
            raise ValueError("Command boundary is outside the bounded pre-roll")
        self.activation = self.position
        self.command_start = command_start
        buffered = np.concatenate(tuple(self.ring)) if self.ring else np.empty(0, dtype=np.int16)
        self.command = [buffered[command_start - earliest:].copy()]
        self.last_speech = self.position if buffered_speech else None
        self.reason = None
        self.state = "capturing"
        return True

    def finish(self, reason: str) -> None:
        self.reason = reason
        self.state = "wake"
        self.ring.clear()
        self.ring_samples = 0
        if reason == "empty_5s":
            self.command.clear()

    def disable(self) -> None:
        self.generation += 1
        self.state = "disabled"
        self.ring.clear()
        self.ring_samples = 0
        self.command.clear()
        self.last_speech = None

    def enable(self) -> None:
        if self.state != "disabled":
            raise RuntimeError("Probe is not disabled")
        self.state = "wake"

    def captured(self) -> np.ndarray:
        return np.concatenate(self.command) if self.command else np.empty(0, dtype=np.int16)
