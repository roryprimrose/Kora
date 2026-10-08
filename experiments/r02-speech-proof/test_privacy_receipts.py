import copy
import json
from pathlib import Path
import unittest
from unittest.mock import patch

import privacy_receipts
from privacy_receipts import CAPTURE_CATEGORY, OBSERVATION_CATEGORY, InvalidReceipt, inspect


OBSERVATION = "c64ef180-bc0d-47ef-a36f-51152aee4510"


def field(kind, value):
    return {"Kind": kind, "CanonicalValue": value}


def observation(state="Locked", frequency=1000):
    return {
        "SchemaVersion": 1,
        "EventId": 208,
        "Category": OBSERVATION_CATEGORY,
        "Properties": {
            "PrivacyObservationId": field(5, OBSERVATION),
            "SessionState": field(4, state),
            "ObservedTimestamp": field(2, "1000"),
            "TimestampFrequency": field(2, str(frequency)),
        },
    }


def capture(released=1500, confirmed=True, claimed=True):
    return {
        "SchemaVersion": 1,
        "EventId": 210,
        "Category": CAPTURE_CATEGORY,
        "Properties": {
            "PrivacyObservationId": field(5, OBSERVATION),
            "Generation": field(2, "1"),
            "WasRecording": field(1, "true"),
            "HadRecorder": field(1, "true"),
            "HadPendingOpen": field(1, "false"),
            "BuffersClearedTimestamp": field(2, "1001"),
            "BufferedBytesAfterClear": field(2, "0"),
            "RecorderReleasedTimestamp": field(0, None) if released is None else field(2, str(released)),
            "ReleaseConfirmed": field(1, "true" if confirmed else "false"),
            "LockReleaseWithinTarget": field(0, None) if claimed is None else field(1, "true" if claimed else "false"),
        },
    }


def daily(*envelopes):
    return "".join(json.dumps({
        "MessageTemplate": "Private content must not survive export.",
        "Properties": {"EvidenceEnvelope": json.dumps(envelope), "Transcript": "private-utterance"},
    }) + "\n" for envelope in envelopes).encode()


class PrivacyReceiptTests(unittest.TestCase):
    def test_exact_boundary_is_synthetic_metadata_not_live_or_authority(self):
        report = inspect(daily(observation(), capture()), synthetic=True)
        self.assertEqual(report["measurements"][0]["status"], "Met")
        self.assertEqual(report["measurements"][0]["observed_to_release_ms"], 500)
        self.assertEqual(report["evidence_kind"], "synthetic-only")
        self.assertFalse(report["live_trial_performed_by_fixture"])
        self.assertFalse(report["production_wake_qualified"])
        self.assertFalse(report["hardware_floor_qualified"])

    def test_over_boundary_and_unconfirmed_release_miss_without_rounding(self):
        for receipt, frequency in ((capture(1501, claimed=False), 1000),
                                   (capture(1500, confirmed=False, claimed=False), 1000),
                                   (capture(600001001, claimed=False), 1200000000)):
            report = inspect(daily(observation(frequency=frequency), receipt))
            self.assertEqual(report["measurements"][0]["status"], "Missed")

    def test_file_order_is_not_assumed_to_be_callback_order(self):
        report = inspect(daily(capture(), observation()))
        self.assertEqual(report["measurements"][0]["status"], "Met")
        self.assertEqual(report["evidence_kind"], "unverified-file-observations")
        self.assertEqual(inspect(daily(observation(), observation(), capture()))["measurements"][0]["status"], "Met")

    def test_absent_observation_release_or_corpus_never_passes(self):
        for records in ((capture(),), (observation(), capture(None, False, None))):
            report = inspect(daily(*records))
            self.assertEqual(report["measurements"][0]["status"], "NotMeasured")
        self.assertEqual(inspect(b"")["measurements"], [])

    def test_inactive_or_non_lock_measurements_are_inapplicable(self):
        for state in ("Unknown", "Unlocked", "Disconnected", "Suspended", "SignedOut"):
            report = inspect(daily(observation(state), capture(claimed=None)))
            self.assertEqual(report["measurements"][0]["status"], "NotApplicable")
        receipt = capture(None, False, None)
        receipt["Properties"]["HadRecorder"] = field(1, "false")
        self.assertEqual(inspect(daily(observation(), receipt))["measurements"][0]["status"], "NotApplicable")
        retired = capture(999, claimed=None)
        retired["Properties"]["WasRecording"] = field(1, "false")
        retired["Properties"]["BuffersClearedTimestamp"] = field(2, "999")
        self.assertEqual(inspect(daily(observation(), retired))["measurements"][0]["status"], "NotApplicable")

    def test_buffer_and_clock_inconsistencies_fail_closed(self):
        receipt = capture()
        receipt["Properties"]["BufferedBytesAfterClear"] = field(2, "1")
        self.assertEqual(inspect(daily(observation(), receipt))["measurements"][0]["status"], "Missed")
        for name, value in (("BuffersClearedTimestamp", "999"), ("BuffersClearedTimestamp", "1501"),
                            ("RecorderReleasedTimestamp", "999"), ("Generation", "-1"),
                            ("Generation", str(2**63))):
            invalid = capture()
            invalid["Properties"][name] = field(2, value)
            with self.subTest(name=name, value=value), self.assertRaises(InvalidReceipt):
                inspect(daily(observation(), invalid))
        with self.assertRaises(InvalidReceipt):
            inspect(daily(observation(frequency=0), capture()))

    def test_forged_summary_wrong_types_duplicates_and_schema_are_rejected(self):
        for records in ((observation(), capture(claimed=False)),
                        (observation(), capture(), capture()),
                        (observation(), observation("Unlocked"), capture())):
            with self.assertRaises(InvalidReceipt):
                inspect(daily(*records))
        for name, value in (("SchemaVersion", 2), ("SchemaVersion", True)):
            invalid = capture()
            invalid[name] = value
            with self.assertRaises(InvalidReceipt):
                inspect(daily(observation(), invalid))
        for value in ({"Kind": True, "CanonicalValue": "true"}, field(1, True), field(1, "yes")):
            invalid = capture()
            invalid["Properties"]["ReleaseConfirmed"] = value
            with self.assertRaises(InvalidReceipt):
                inspect(daily(observation(), invalid))
        for value in (b'{"Properties":{},"Properties":{}}\n', b'{"Properties":{},"number":NaN}\n',
                      b'{"Properties":{}}', b"\xff\n"):
            with self.assertRaises(InvalidReceipt):
                inspect(value)

    def test_report_omits_every_identity_and_private_content(self):
        records = (observation(), capture())
        for envelope in records:
            envelope["Host"] = {"SessionId": "private-session", "RequestId": "private-request"}
            envelope["Properties"]["Unexpected"] = field(4, "private-device-account")
        serialized = json.dumps(inspect(daily(*records)))
        for sensitive in (OBSERVATION, "private-session", "private-request", "private-device-account",
                          "private-utterance", "Private content"):
            self.assertNotIn(sensitive, serialized)

    def test_unrelated_lookalike_message_does_not_acquire_authority(self):
        unrelated = copy.deepcopy(capture())
        unrelated["Category"] = "Untrusted.Capture"
        report = inspect(daily(observation(), unrelated))
        self.assertEqual(report["measurements"], [])

    def test_file_and_line_limits_reject_partial_or_oversized_inputs(self):
        with patch.object(privacy_receipts, "MAXIMUM_BYTES", 1), self.assertRaises(InvalidReceipt):
            inspect(b"{}\n")
        with patch.object(privacy_receipts, "MAXIMUM_LINE_BYTES", 1), self.assertRaises(InvalidReceipt):
            inspect(b"{}\n")
        with patch.object(privacy_receipts, "MAXIMUM_LINES", 1), self.assertRaises(InvalidReceipt):
            inspect(b"{}\n{}\n")

    def test_output_is_exclusive_and_only_under_ignored_scratch(self):
        for name in ("..\\outside.json", "private/path.json", "C:\\private.json", "receipt.txt"):
            with self.assertRaises(InvalidReceipt):
                privacy_receipts.write_report({}, name)
        # Virtualize I/O: deterministic tests never read user logs or write test artifacts.
        with patch.object(Path, "is_symlink", return_value=True), self.assertRaises(InvalidReceipt):
            privacy_receipts.write_report({}, "receipt.json")
        with patch.object(Path, "is_symlink", return_value=False), \
             patch.object(Path, "is_junction", return_value=False), \
             patch.object(Path, "mkdir"), \
             patch.object(Path, "open", side_effect=FileExistsError) as open_file, \
             patch.object(Path, "unlink") as unlink, self.assertRaises(FileExistsError):
            privacy_receipts.write_report({}, "receipt.json")
        open_file.assert_called_once_with("x", encoding="utf-8", newline="\n")
        unlink.assert_not_called()

    def test_failed_owned_output_is_cleaned_without_deleting_existing_files(self):
        with patch.object(Path, "is_symlink", return_value=False), \
             patch.object(Path, "is_junction", return_value=False), \
             patch.object(Path, "mkdir"), \
             patch.object(Path, "open"), \
             patch.object(json, "dump", side_effect=OSError), \
             patch.object(Path, "unlink") as unlink, self.assertRaises(OSError):
            privacy_receipts.write_report({}, "receipt.json")
        unlink.assert_called_once()


if __name__ == "__main__":
    unittest.main()
