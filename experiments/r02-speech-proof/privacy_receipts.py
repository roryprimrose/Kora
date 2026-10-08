"""File-only inspection of production privacy receipts; never audio or authority."""

import argparse
from fractions import Fraction
import hashlib
import json
from pathlib import Path
import re
import sys
from uuid import UUID


MAXIMUM_BYTES = 16 * 1024 * 1024
MAXIMUM_LINE_BYTES = 256 * 1024
MAXIMUM_LINES = 10000
OBSERVATION_CATEGORY = "Kora.Windows.Session.WindowsPrivacyObservationService"
CAPTURE_CATEGORY = "Kora.Windows.Audio.WindowsVoiceRecognitionService"


class InvalidReceipt(ValueError):
    pass


def _unique_object(pairs):
    result = {}
    for name, value in pairs:
        if name in result:
            raise InvalidReceipt("Duplicate JSON property.")
        result[name] = value
    return result


def _decode(value):
    def reject_constant(_):
        raise InvalidReceipt("Non-finite JSON number.")

    try:
        return json.loads(value, object_pairs_hook=_unique_object, parse_constant=reject_constant)
    except (ValueError, RecursionError) as error:
        raise InvalidReceipt("Malformed JSON receipt.") from error


def _property(properties, name, kind):
    value = properties.get(name)
    if not isinstance(value, dict) or set(value) != {"Kind", "CanonicalValue"}:
        raise InvalidReceipt(f"Missing or malformed field: {name}.")
    if type(value["Kind"]) is not int or value["Kind"] != kind:
        raise InvalidReceipt(f"Incorrect field kind: {name}.")
    return value["CanonicalValue"]


def _integer(properties, name, nullable=False, positive=False):
    if nullable and properties.get(name) == {"Kind": 0, "CanonicalValue": None}:
        return None
    value = _property(properties, name, 2)
    if not isinstance(value, str) or not re.fullmatch(r"0|[1-9][0-9]{0,18}", value):
        raise InvalidReceipt(f"Invalid integer: {name}.")
    number = int(value)
    if number > 2**63 - 1 or positive and number == 0:
        raise InvalidReceipt(f"Integer outside bounds: {name}.")
    return number


def _boolean(properties, name, nullable=False):
    if nullable and properties.get(name) == {"Kind": 0, "CanonicalValue": None}:
        return None
    value = _property(properties, name, 1)
    if value not in ("true", "false"):
        raise InvalidReceipt(f"Invalid boolean: {name}.")
    return value == "true"


def _observation_id(properties):
    value = _property(properties, "PrivacyObservationId", 5)
    try:
        identifier = UUID(value)
    except (ValueError, TypeError, AttributeError) as error:
        raise InvalidReceipt("Invalid observation identifier.") from error
    if identifier.int == 0 or str(identifier) != value:
        raise InvalidReceipt("Invalid observation identifier.")
    return value


def inspect(data, synthetic=False):
    """Only copied JSONL files are accepted; an observed file is not authenticated."""
    if not isinstance(data, bytes) or len(data) > MAXIMUM_BYTES:
        raise InvalidReceipt("Input exceeds the bounded file limit.")
    if data and not data.endswith(b"\n"):
        raise InvalidReceipt("Incomplete final JSONL record.")
    lines = data.splitlines()
    if len(lines) > MAXIMUM_LINES:
        raise InvalidReceipt("Input exceeds the bounded record limit.")
    observations = {}
    captures = []
    seen_captures = set()
    for line in lines:
        if not line or len(line) > MAXIMUM_LINE_BYTES:
            raise InvalidReceipt("Empty or oversized JSONL record.")
        try:
            outer = _decode(line.decode("utf-8"))
        except UnicodeError as error:
            raise InvalidReceipt("Input is not UTF-8.") from error
        if not isinstance(outer, dict) or not isinstance(outer.get("Properties"), dict):
            raise InvalidReceipt("Expected a production daily JSONL record.")
        encoded = outer["Properties"].get("EvidenceEnvelope")
        if encoded is None:
            continue
        if not isinstance(encoded, str):
            raise InvalidReceipt("Expected the encoded diagnostic envelope.")
        envelope = _decode(encoded)
        if not isinstance(envelope, dict):
            raise InvalidReceipt("Expected a diagnostic envelope.")
        event = envelope.get("EventId")
        category = envelope.get("Category")
        if (event, category) not in ((208, OBSERVATION_CATEGORY), (210, CAPTURE_CATEGORY)):
            continue
        if type(event) is not int or type(envelope.get("SchemaVersion")) is not int or envelope["SchemaVersion"] != 1:
            raise InvalidReceipt("Unsupported diagnostic schema.")
        properties = envelope.get("Properties")
        if not isinstance(properties, dict):
            raise InvalidReceipt("Missing structured diagnostic properties.")
        observation = _observation_id(properties)
        if event == 208:
            state = _property(properties, "SessionState", 4)
            if state not in ("Unknown", "Unlocked", "Locked", "Disconnected", "Suspended", "SignedOut"):
                raise InvalidReceipt("Unknown session state.")
            values = (
                state,
                _integer(properties, "ObservedTimestamp"),
                _integer(properties, "TimestampFrequency", positive=True),
            )
            # One native observation can publish both an immediate session
            # update and a requery. Only consistent correlation is usable.
            if observation in observations and observations[observation] != values:
                raise InvalidReceipt("Conflicting privacy observation.")
            observations[observation] = values
        else:
            generation = _integer(properties, "Generation")
            key = (observation, generation)
            if key in seen_captures:
                raise InvalidReceipt("Duplicate capture receipt.")
            seen_captures.add(key)
            captures.append((observation, properties))

    measurements = []
    for observation, properties in captures:
        observed = observations.get(observation)
        recording = _boolean(properties, "WasRecording")
        recorder = _boolean(properties, "HadRecorder")
        _boolean(properties, "HadPendingOpen")
        confirmed = _boolean(properties, "ReleaseConfirmed")
        cleared = _integer(properties, "BuffersClearedTimestamp")
        remaining = _integer(properties, "BufferedBytesAfterClear")
        released = _integer(properties, "RecorderReleasedTimestamp", nullable=True)
        claimed = _boolean(properties, "LockReleaseWithinTarget", nullable=True)
        status = "NotMeasured"
        elapsed = None
        # Match CapturePrivacyReceiptEventArgs: no recorder/non-lock/pre-released
        # rows cannot establish the active-lock release target.
        if observed is not None:
            state, timestamp, frequency = observed
            if state != "Locked" or not recorder or released is not None and not recording and released < timestamp:
                status = "NotApplicable"
                if claimed is not None:
                    raise InvalidReceipt("Inapplicable receipt claims a lock measurement.")
            elif released is None:
                if claimed is not None:
                    raise InvalidReceipt("Missing release timestamp claims a lock measurement.")
            else:
                delta = Fraction((released - timestamp) * 1000, frequency)
                if released < timestamp or cleared < timestamp or cleared > released:
                    raise InvalidReceipt("Capture timestamps are inconsistent.")
                target_met = confirmed and delta <= 500
                if claimed is not target_met:
                    raise InvalidReceipt("Reported lock target contradicts measured timestamps.")
                elapsed = float(delta)
                status = "Met" if target_met and remaining == 0 else "Missed"
        measurements.append({
            "measurement": len(measurements) + 1,
            "status": status,
            "observed_to_release_ms": elapsed,
            "buffers_empty": remaining == 0,
            "release_confirmed": confirmed,
        })
    return {
        "schema_version": 1,
        "evidence_kind": "synthetic-only" if synthetic else "unverified-file-observations",
        "input_sha256": hashlib.sha256(data).hexdigest(),
        "input_records": len(lines),
        "lock_release_target_ms": 500,
        "measurements": measurements,
        "live_trial_performed_by_fixture": False,
        "production_wake_qualified": False,
        "hardware_floor_qualified": False,
        "limitations": [
            "Diagnostic metadata is not consent, ownership, audit authority or authenticated live evidence.",
            "No capture, playback, wake detection, corpus exposure or acoustic timing is performed.",
            "Operator/build/rig provenance, OS notification delay and other privacy transitions require separate review.",
            "Missing or inapplicable active-lock measurements cannot establish the 500 ms target.",
        ],
    }


def write_report(report, output_name):
    if not re.fullmatch(r"[a-zA-Z0-9][a-zA-Z0-9_-]{0,79}\.json", output_name):
        raise InvalidReceipt("Output must be a simple JSON filename.")
    scratch = Path(__file__).resolve().parent / "scratch"
    if scratch.is_symlink() or scratch.is_junction():
        raise InvalidReceipt("Scratch directory must not be redirected.")
    scratch.mkdir(exist_ok=True)
    output = scratch / output_name
    created = False
    try:
        with output.open("x", encoding="utf-8", newline="\n") as stream:
            created = True
            json.dump(report, stream, indent=2, allow_nan=False)
            stream.write("\n")
    except BaseException:
        if created:
            output.unlink()
        raise


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", type=Path, help="Explicit operator-exported daily JSONL snapshot; never auto-discovered.")
    parser.add_argument("--synthetic", action="store_true", help="Label generated test input, not live evidence.")
    parser.add_argument("--output-name", default="privacy-receipt.json", help="New filename under ignored experiment scratch.")
    args = parser.parse_args()
    try:
        with args.input.open("rb") as stream:
            data = stream.read(MAXIMUM_BYTES + 1)
        report = inspect(data, args.synthetic)
        write_report(report, args.output_name)
    except (OSError, InvalidReceipt):
        # Do not echo a private input path, JSON payload or exception text.
        print("Receipt inspection failed; no valid report was published.", file=sys.stderr)
        return 1
    print("Metadata-only report written under experiment scratch; no live trial or capability qualification.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
