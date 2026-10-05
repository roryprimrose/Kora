import tempfile
import unittest
from pathlib import Path

import numpy as np

from benchmark import capture_result, p95, summarize, write_summary
from capture_probe import CaptureProbe
from fixtures import RATE, read_pcm, write_pcm


FRAME = RATE // 100


def feed(probe, seconds, value=0, speech=False):
    for _ in range(round(seconds * 100)):
        probe.feed(np.full(FRAME, value, dtype=np.int16), speech)


class CaptureContractTests(unittest.TestCase):
    def test_preroll_is_exactly_bounded_and_overwritten(self):
        probe = CaptureProbe()
        feed(probe, 3, 100)
        feed(probe, 2, 200)
        self.assertEqual(probe.ring_samples, 2 * RATE)
        self.assertTrue(np.all(np.concatenate(tuple(probe.ring)) == 200))
        with self.assertRaisesRegex(ValueError, "bounded pre-roll"):
            probe.activate(0, 0)

    def test_immediate_prefix_preservation_and_wake_exclusion_with_oracle_boundary(self):
        probe = CaptureProbe()
        feed(probe, 1, 100)  # Ambient / wake, not admitted to command.
        command_start = probe.position
        feed(probe, 0.4, 900, True)  # Command starts before delayed wake callback.
        self.assertTrue(probe.activate(command_start, 0, buffered_speech=True))
        feed(probe, 0.2, 900, True)
        feed(probe, 0.99)
        self.assertEqual(probe.state, "capturing")
        feed(probe, 0.01)
        self.assertEqual(probe.reason, "silence_1s")
        self.assertEqual(probe.state, "wake")
        np.testing.assert_array_equal(probe.captured()[:round(0.6 * RATE)], 900)
        self.assertNotIn(100, probe.captured())

    def test_empty_activation_expires_exactly_at_five_seconds(self):
        probe = CaptureProbe()
        self.assertTrue(probe.activate(0, 0))
        feed(probe, 4.99)
        self.assertEqual(probe.state, "capturing")
        feed(probe, 0.01)
        self.assertEqual(probe.reason, "empty_5s")
        self.assertEqual(len(probe.captured()), 0)
        self.assertEqual(probe.ring_samples, 0)

    def test_paused_command_begins_before_empty_deadline(self):
        probe = CaptureProbe()
        probe.activate(0, 0)
        feed(probe, 4)
        feed(probe, 0.1, 900, True)
        feed(probe, 1)
        self.assertEqual(probe.reason, "silence_1s")
        self.assertTrue(np.any(probe.captured() == 900))

    def test_sixty_second_limit_includes_buffered_command_audio(self):
        probe = CaptureProbe()
        feed(probe, 0.4, 900, True)
        probe.activate(0, 0, buffered_speech=True)
        feed(probe, 59.59, 900, True)
        self.assertEqual(probe.state, "capturing")
        feed(probe, 0.01, 900, True)
        self.assertEqual(probe.reason, "limit_60s")
        self.assertEqual(len(probe.captured()), 60 * RATE)
        feed(probe, 1, 900, True)
        self.assertEqual(len(probe.captured()), 60 * RATE)

    def test_disable_clears_audio_and_rejects_retired_callbacks(self):
        probe = CaptureProbe()
        feed(probe, 1, 900)
        probe.activate(0, 0, buffered_speech=True)
        probe.disable()
        self.assertEqual(probe.ring_samples, 0)
        self.assertEqual(len(probe.captured()), 0)
        self.assertFalse(probe.activate(probe.position, 0))
        with self.assertRaisesRegex(RuntimeError, "disabled"):
            feed(probe, 0.01)
        probe.enable()
        self.assertFalse(probe.activate(probe.position, 0))
        self.assertTrue(probe.activate(probe.position, 1))

    def test_duplicate_activation_is_rejected(self):
        probe = CaptureProbe()
        self.assertTrue(probe.activate(0, 0))
        self.assertFalse(probe.activate(0, 0))

    def test_frame_shape_is_enforced(self):
        with self.assertRaisesRegex(ValueError, "10 ms"):
            CaptureProbe().feed(np.zeros(1, dtype=np.int16))

    def test_non_aligned_boundary_never_exceeds_sixty_seconds(self):
        probe = CaptureProbe()
        feed(probe, 0.4, 900, True)
        probe.activate(137, 0, buffered_speech=True)
        feed(probe, 60, 900, True)
        self.assertEqual(probe.reason, "limit_60s")
        self.assertEqual(len(probe.captured()), 60 * RATE)

    def test_real_frame_replay_preserves_oracle_prefix_and_endpoints(self):
        pcm = np.concatenate((
            np.full(RATE, 100, dtype=np.int16),
            np.full(RATE, 900, dtype=np.int16),
            np.zeros(2 * RATE, dtype=np.int16),
        ))
        result = capture_result(
            {"group": "immediate_spliced", "command_start_sample": RATE},
            [{"observed_sample": RATE + 1600}], pcm,
        )
        self.assertEqual(result["reason"], "silence_1s")
        self.assertTrue(result["first_200ms_command_exact"])
        self.assertEqual(result["captured_samples"], 2 * RATE)

    def test_missing_or_late_detection_is_not_success(self):
        case = {"group": "immediate_spliced", "command_start_sample": 0}
        self.assertIsNone(capture_result(case, [], np.zeros(4 * RATE, dtype=np.int16)))
        result = capture_result(case, [{"observed_sample": 3 * RATE}], np.zeros(4 * RATE, dtype=np.int16))
        self.assertEqual(result["state"], "unavailable")


class MeasurementTests(unittest.TestCase):
    def test_p95_uses_nearest_rank_and_reports_absence(self):
        self.assertIsNone(p95([]))
        self.assertEqual(p95(list(range(1, 31))), 29)

    def test_false_activation_counts_events_not_just_files(self):
        cases = [
            {"group": "negative_speech", "seconds": 1800, "events": [{}, {}]},
            {"group": "negative_silence", "seconds": 1800, "events": []},
            {"group": "continuous", "seconds": 5, "events": []},
            {"group": "self_tts_replay", "seconds": 5, "events": [{}]},
        ]
        summary = summarize(cases)
        self.assertEqual(summary["negative_total"]["false_activations_per_hour"], 2)
        self.assertEqual(summary["continuous"]["recall"], 0)
        self.assertNotIn("recall", summary["self_tts_replay"])

    def test_pcm_round_trip(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "fixture.wav"
            pcm = np.array([-32768, 0, 32767], dtype=np.int16)
            write_pcm(path, pcm)
            np.testing.assert_array_equal(read_pcm(path), pcm)

    def test_summary_preserves_failed_capture_and_actual_trial_counts(self):
        cases = [
            {"group": group, "seconds": 2, "events": [], "capture_probe": None}
            for group in ("wake_only", "immediate_spliced", "long_command_tone", "continuous",
                          "paused", "white_noise_10dB", "white_noise_0dB", "self_tts_replay", "negative_speech")
        ]
        cases[1]["capture_probe"] = {"state": "unavailable", "reason": "command_prefix_outside_preroll"}
        setting = {
            "threshold": 0.1, "cases": cases, "summary": summarize(cases),
            "cold_model_construction_ms": 10,
            "resources": {"frame_processing_p95_ms": 2, "incremental_peak_rss_bytes": 1024},
            "repeated_trials": {"clip_processing_p95_ms": 3, "warmups": 7, "measured": 35},
        }
        result = {
            "git_head": "test", "main_baseline": "test", "settings": [setting],
            "paced": {"wall_seconds": 60, "cpu_percent_total_capacity": 1,
                      "incremental_peak_rss_bytes": 1024, "frame_processing_p95_ms": 2},
        }
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / "summary.md"
            write_summary(result, output, "alternative.json")
            report = output.read_text("utf-8")
        self.assertIn("[alternative.json](alternative.json)", report)
        self.assertIn("7 warmups, 35 measured", report)
        header = "| Threshold | Immediate first 200 ms exact"
        self.assertIn(header, report)
        self.assertIn("| 0.1 | 0/1 | 0/1 | 0/1 |", report.split(header)[1])


if __name__ == "__main__":
    unittest.main()
