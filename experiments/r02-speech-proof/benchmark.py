"""Offline Kora-specific KWS measurements; never acquires capture/playback devices."""

import argparse
from collections import defaultdict
from datetime import datetime, timezone
import importlib.metadata
import json
import math
import os
from pathlib import Path
import platform
import subprocess
import sys
import time

import numpy as np
import psutil
import sherpa_onnx

from fixtures import RATE, read_pcm
from capture_probe import CaptureProbe
from prepare import ASSETS, MODEL_NAME, ROOT, STEM, digest


def p95(values):
    return sorted(values)[math.ceil(len(values) * 0.95) - 1] if values else None


def summarize(cases):
    groups = defaultdict(list)
    for case in cases:
        groups[case["group"]].append(case)
    result = {}
    for group, items in groups.items():
        events = sum(len(item["events"]) for item in items)
        summary = {
            "cases": len(items),
            "cases_detected": sum(bool(item["events"]) for item in items),
            "raw_events": events,
            "unique_audio_seconds": sum(item["seconds"] for item in items),
        }
        if group.startswith("negative"):
            summary["false_activations_per_hour"] = events * 3600 / summary["unique_audio_seconds"]
        elif group not in ("self_tts_replay", "synthetic_overlap"):
            summary["recall"] = summary["cases_detected"] / len(items)
        else:
            summary["source_attribution"] = "unavailable; raw wake events are not accepted user commands"
        result[group] = summary
    negatives = [item for item in cases if item["group"].startswith("negative")]
    seconds = sum(item["seconds"] for item in negatives)
    result["negative_total"] = {
        "unique_audio_seconds": seconds,
        "raw_events": sum(len(item["events"]) for item in negatives),
        "false_activations_per_hour": sum(len(item["events"]) for item in negatives) * 3600 / seconds,
        "warning": "Small synthetic sample, not the required 10-hour diverse acoustic negative corpus",
    }
    return result


def load_inputs():
    receipt = json.loads((ASSETS / "receipt.json").read_text("utf-8"))
    model_dir = ASSETS / MODEL_NAME
    for filename, expected in receipt["files"].items():
        if digest(model_dir / filename) != expected:
            raise ValueError(f"Model/token digest mismatch: {filename}")
    manifest = json.loads((ROOT / "fixtures" / "manifest.json").read_text("utf-8"))
    if manifest["sample_rate"] != RATE:
        raise ValueError("Fixture sample rate mismatch")
    inputs = {}
    for case in manifest["cases"]:
        path = ROOT / "fixtures" / case["file"]
        if digest(path) != case["sha256"]:
            raise ValueError(f"Fixture digest mismatch: {case['id']}")
        pcm = read_pcm(path)
        if len(pcm) != case["samples"]:
            raise ValueError(f"Fixture sample count mismatch: {case['id']}")
        inputs[case["id"]] = pcm
    return receipt, manifest, inputs


def create_detector(threshold: float):
    model_dir = ASSETS / MODEL_NAME
    return sherpa_onnx.KeywordSpotter(
        tokens=str(model_dir / "tokens.txt"),
        encoder=str(model_dir / ("encoder" + STEM + ".int8.onnx")),
        decoder=str(model_dir / ("decoder" + STEM + ".onnx")),
        joiner=str(model_dir / ("joiner" + STEM + ".int8.onnx")),
        keywords_file=str(model_dir / "kora.txt"),
        num_threads=2, provider="cpu", sample_rate=RATE, feature_dim=80,
        max_active_paths=4, keywords_score=1.0, keywords_threshold=threshold,
        num_trailing_blanks=1,
    )


def replay(detector, pcm, process):
    stream = detector.create_stream()
    events = []
    frames_ms = []
    peak_rss = process.memory_info().rss
    for start in range(0, len(pcm), 1600):
        frame = pcm[start:start + 1600].astype(np.float32) / 32768
        before = time.perf_counter()
        stream.accept_waveform(RATE, frame)
        while detector.is_ready(stream):
            detector.decode_stream(stream)
            keyword = detector.get_result(stream)
            if keyword:
                events.append({"keyword": keyword, "observed_sample": start + len(frame)})
                detector.reset_stream(stream)
        frames_ms.append((time.perf_counter() - before) * 1000)
        peak_rss = max(peak_rss, process.memory_info().rss)
    # No hidden post-file padding: fixtures contain their declared trailing silence.
    return events, frames_ms, peak_rss


def capture_result(case, events, pcm):
    boundary = case["command_start_sample"]
    if case["group"] == "wake_only":
        boundary = case["wake_end_sample"]
    if boundary is None or not events:
        return None
    event_position = events[0]["observed_sample"]
    if boundary < event_position - 2 * RATE:
        return {"state": "unavailable", "reason": "command_prefix_outside_preroll"}
    probe = CaptureProbe()
    capture_start = min(boundary, event_position)
    frame_size = RATE // 100
    for offset in range(0, len(pcm) - frame_size + 1, frame_size):
        frame = pcm[offset:offset + frame_size]
        energy_speech = bool(np.any(np.abs(frame.astype(np.int32)) > 200))
        probe.feed(frame, energy_speech)
        if probe.position == event_position:
            buffered_speech = bool(np.any(np.abs(pcm[capture_start:event_position].astype(np.int32)) > 200))
            probe.activate(capture_start, 0, buffered_speech)
        if probe.reason is not None:
            break
    captured = probe.captured()
    first_words_samples = min(RATE // 5, len(pcm) - boundary)
    relative_boundary = boundary - capture_start
    preserved = (
        case["command_start_sample"] is not None
        and np.array_equal(
            captured[relative_boundary:relative_boundary + first_words_samples],
            pcm[boundary:boundary + first_words_samples],
        )
    )
    return {
        "state": probe.state, "reason": probe.reason,
        "activation_sample": event_position,
        "captured_samples": len(captured), "captured_seconds": len(captured) / RATE,
        "transition_sample": probe.position,
        "first_200ms_command_exact": preserved if case["command_start_sample"] is not None else None,
        "boundary": "Oracle fixture wake/command boundary; energy mask >200, 10 ms cadence; not production VAD/alignment/STT",
    }


def measure_candidate(threshold, manifest, inputs, warmups, repetitions):
    process = psutil.Process()
    baseline_rss = process.memory_info().rss
    cold_start = time.perf_counter()
    detector = create_detector(threshold)
    cold_ms = (time.perf_counter() - cold_start) * 1000
    model_rss = process.memory_info().rss
    cases = []
    frame_times = []
    peak_rss = model_rss
    cpu_start = time.process_time()
    wall_start = time.perf_counter()
    for case in manifest["cases"]:
        events, timings, peak = replay(detector, inputs[case["id"]], process)
        measured = {"id": case["id"], "group": case["group"], "seconds": case["seconds"], "events": events}
        if case["command_start_sample"] is not None:
            boundary = case["command_start_sample"]
            measured["oracle_command_head_availability"] = (
                "not_detected" if not events else
                "future_audio" if events[0]["observed_sample"] < boundary else
                "in_preroll" if events[0]["observed_sample"] - 2 * RATE <= boundary else "lost"
            )
        measured["capture_probe"] = capture_result(case, events, inputs[case["id"]])
        if case["wake_end_sample"] is not None and events:
            measured["first_event_audio_delay_ms"] = (
                events[0]["observed_sample"] - case["wake_end_sample"]
            ) * 1000 / RATE
        cases.append(measured)
        frame_times.extend(timings)
        peak_rss = max(peak_rss, peak)
    wall = time.perf_counter() - wall_start
    cpu = time.process_time() - cpu_start
    trial_pcm = inputs[next(case["id"] for case in manifest["cases"] if case["group"] == "immediate_spliced")]
    for _ in range(warmups):
        replay(detector, trial_pcm, process)
    trials = []
    for _ in range(repetitions):
        start = time.perf_counter()
        events, timings, peak = replay(detector, trial_pcm, process)
        trials.append({
            "wall_ms": (time.perf_counter() - start) * 1000,
            "first_event_sample": events[0]["observed_sample"] if events else None,
            "events": len(events), "frame_p95_ms": p95(timings),
        })
        peak_rss = max(peak_rss, peak)
    return {
        "threshold": threshold,
        "configuration": {
            "provider": "cpu", "threads": 2, "sample_rate": RATE, "feed_samples": 1600,
            "max_active_paths": 4, "keywords_score": 1.0, "num_trailing_blanks": 1,
            "keyword": "KORA", "encoder": "int8", "decoder": "float32", "joiner": "int8",
            "vad": "none in detector", "aec": "none", "playback_reference": "none",
        },
        "cold_model_construction_ms": cold_ms,
        "resources": {
            "accelerated_wall_seconds": wall, "process_cpu_seconds": cpu,
            "cpu_percent_total_capacity": cpu / wall / psutil.cpu_count() * 100,
            "baseline_imported_python_rss_bytes": baseline_rss,
            "model_loaded_rss_bytes": model_rss,
            "observed_peak_rss_bytes": peak_rss,
            "incremental_peak_rss_bytes": peak_rss - baseline_rss,
            "frame_processing_p95_ms": p95(frame_times),
            "boundary": "Accelerated file inference in isolated Python; not Kora UI/muted-idle or acoustic latency",
        },
        "summary": summarize(cases), "cases": cases,
        "repeated_trials": {
            "warmups": warmups, "measured": repetitions,
            "utterance_id": next(case["id"] for case in manifest["cases"] if case["group"] == "immediate_spliced"),
            "clip_processing_p95_ms": p95([trial["wall_ms"] for trial in trials]),
            "trials": trials,
            "note": "Repeated synthetic utterance; not independent speakers or end-to-end feedback trials",
        },
    }


def paced_measurement(seconds, threshold, pcm):
    process = psutil.Process()
    baseline_rss = process.memory_info().rss
    detector = create_detector(threshold)
    stream = detector.create_stream()
    peak_rss = process.memory_info().rss
    start = time.perf_counter()
    cpu_start = time.process_time()
    samples = 0
    events = 0
    frame_times = []
    while samples / RATE < seconds:
        offset = samples % len(pcm)
        frame = pcm[offset:offset + min(1600, round(seconds * RATE) - samples)]
        if not len(frame):
            break
        before = time.perf_counter()
        stream.accept_waveform(RATE, frame.astype(np.float32) / 32768)
        while detector.is_ready(stream):
            detector.decode_stream(stream)
            if detector.get_result(stream):
                events += 1
                detector.reset_stream(stream)
        frame_times.append((time.perf_counter() - before) * 1000)
        samples += len(frame)
        peak_rss = max(peak_rss, process.memory_info().rss)
        time.sleep(max(0, start + samples / RATE - time.perf_counter()))
    wall = time.perf_counter() - start
    cpu = time.process_time() - cpu_start
    return {
        "threshold": threshold, "wall_seconds": wall, "audio_seconds": samples / RATE,
        "cpu_seconds": cpu, "cpu_percent_total_capacity": cpu / wall / psutil.cpu_count() * 100,
        "observed_peak_rss_bytes": peak_rss, "baseline_imported_python_rss_bytes": baseline_rss,
        "incremental_peak_rss_bytes": peak_rss - baseline_rss,
        "frame_processing_p95_ms": p95(frame_times), "raw_events": events,
        "note": "Paced repeated synthetic negative speech; not independent negative exposure, not application overhead",
    }


def hardware():
    script = r"""
        [ordered]@{
            cpu=@(Get-CimInstance Win32_Processor | Select-Object Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed)
            os=Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,BuildNumber,OSArchitecture
            windows_revision=Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' | Select-Object DisplayVersion,UBR
            system=Get-CimInstance Win32_ComputerSystem | Select-Object TotalPhysicalMemory,Manufacturer,Model
            disk=@(Get-PhysicalDisk | Select-Object FriendlyName,MediaType)
            power=(powercfg /getactivescheme)
            ui_processes=@(Get-Process | Where-Object {$_.ProcessName -match '^(Kora|Code|ollama)$'} | Group-Object ProcessName | Select-Object Name,Count)
        } | ConvertTo-Json -Depth 5
    """
    return json.loads(subprocess.check_output(
        ["powershell.exe", "-NoProfile", "-Command", "$ErrorActionPreference='Stop';" + script],
        text=True, encoding="utf-8",
    ))


def write_summary(result, output, result_filename):
    rows = [
        "# Recorded synthetic-audio results", "",
        f"Generated from [{result_filename}]({result_filename}). Not acoustic or release proof.",
        f"Source revision: `{result['git_head']}`; main baseline: `{result['main_baseline']}`.",
        "Source-file hashes in the JSON identify the measured experiment even if evidence changes later.", "",
        "| Threshold | Wake | Spliced immediate | Continuous | Paused | White noise 10 dB | White noise 0 dB | Raw TTS events |",
        "|---|---|---|---|---|---|---|---|",
    ]
    for setting in result["settings"]:
        s = setting["summary"]
        counts = [f"{s[g]['cases_detected']}/{s[g]['cases']}" for g in (
            "wake_only", "immediate_spliced", "continuous", "paused", "white_noise_10dB", "white_noise_0dB",
        )]
        rows.append(f"| {setting['threshold']} | " + " | ".join(counts) + f" | {s['self_tts_replay']['raw_events']} |")
    rows.append("")
    for setting in result["settings"]:
        negative = setting["summary"]["negative_total"]
        rows.append(f"Threshold {setting['threshold']}: {negative['raw_events']} false events over "
                    f"{negative['unique_audio_seconds']:.3f} seconds of synthetic negative audio.")
    rows.extend((
        "This is not evidence for <=1 false activation/hour; the required diverse 10-hour corpus is absent.",
        "Raw TTS wake events demonstrate that the detector alone cannot reject self-playback.",
        "No user commands or tools were dispatched.", "",
        "| Threshold | Cold construction ms | Accelerated frame p95 ms | Clip processing p95 ms | Incremental observed RSS MiB |",
        "|---|---|---|---|---|",
    ))
    for setting in result["settings"]:
        rows.append(
            f"| {setting['threshold']} | {setting['cold_model_construction_ms']:.2f} | "
            f"{setting['resources']['frame_processing_p95_ms']:.2f} | "
            f"{setting['repeated_trials']['clip_processing_p95_ms']:.2f} | "
            f"{setting['resources']['incremental_peak_rss_bytes'] / 1048576:.2f} |"
        )
    paced = result["paced"]
    rows.extend((
        "", f"Paced default-threshold run: {paced['wall_seconds']:.2f} seconds, "
        f"{paced['cpu_percent_total_capacity']:.3f}% of total CPU capacity, "
        f"{paced['incremental_peak_rss_bytes'] / 1048576:.2f} MiB incremental observed RSS, "
        f"{paced['frame_processing_p95_ms']:.2f} ms frame-processing p95.",
        "Python imported/fixture-loaded idle is the memory baseline, not muted Kora.",
        "RSS is sampled between frames, not a guaranteed transient peak.",
        "Cold construction excludes Python startup/import time. These metrics are not end-to-end feedback latency.", "",
    ))
    for setting in result["settings"]:
        trials = setting["repeated_trials"]
        rows.append(f"Threshold {setting['threshold']}: {trials['warmups']} warmups, {trials['measured']} measured repeated-clip trials.")
    rows.append("")
    rows.extend((
        "| Threshold | Wake-only annotated audio delay min/max ms |",
        "|---|---|",
    ))
    for setting in result["settings"]:
        delays = [case["first_event_audio_delay_ms"] for case in setting["cases"]
                  if case["group"] == "wake_only" and "first_event_audio_delay_ms" in case]
        delay_range = f"{min(delays):.2f} / {max(delays):.2f}" if delays else "Not detected"
        rows.append(f"| {setting['threshold']} | {delay_range} |")
    rows.extend((
        "Delays use the fixture's trimmed wake end and the first observed feed/decode sample.",
        "They include 100 ms feed quantization and model lookahead, not real end-of-word/visible UI latency.",
        "No visible-feedback latency release gate is established.", "",
        "| Threshold | Immediate first 200 ms exact | Empty ended at 5 s | Long capture bounded to 60 s |",
        "|---|---|---|---|",
    ))
    for setting in result["settings"]:
        counts = []
        for group, predicate in (
            ("immediate_spliced", lambda p: p["first_200ms_command_exact"] and p["reason"] == "silence_1s"),
            ("wake_only", lambda p: p["reason"] == "empty_5s" and p["captured_samples"] == 0
             and p["transition_sample"] - p["activation_sample"] == 5 * RATE),
            ("long_command_tone", lambda p: p["reason"] == "limit_60s" and p["captured_samples"] == 60 * RATE),
        ):
            cases = [case for case in setting["cases"] if case["group"] == group]
            passed = sum(bool(case["capture_probe"]) and case["capture_probe"]["state"] != "unavailable"
                         and predicate(case["capture_probe"]) for case in cases)
            counts.append(f"{passed}/{len(cases)}")
        rows.append(f"| {setting['threshold']} | " + " | ".join(counts) + " |")
    rows.extend((
        "",
        "Endpointing uses a synthetic energy mask and oracle wake/command boundary, not provider alignment or real VAD.",
        "One-second silence and two-second overwrite bounds also have deterministic unit tests.",
        "Real wake removal, command transcription, TTS sample-stop and human barge-in remain unproven.", "",
        "Live microphone/playback trials were deferred by the user. D-002 and D-007 remain open.",
    ))
    output.write_text("\n".join(rows) + "\n", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--thresholds", type=float, nargs="+", default=[0.1, 0.25, 0.5])
    parser.add_argument("--warmups", type=int, default=5)
    parser.add_argument("--repetitions", type=int, default=30)
    parser.add_argument("--paced-seconds", type=int, default=60)
    parser.add_argument("--worker", choices=["quality", "paced"], help=argparse.SUPPRESS)
    parser.add_argument("--worker-threshold", type=float, default=0.25, help=argparse.SUPPRESS)
    args = parser.parse_args()
    if any(not 0 < threshold <= 1 for threshold in (*args.thresholds, args.worker_threshold)):
        parser.error("thresholds must be in (0, 1]")
    if args.warmups < 5 or args.repetitions < 30 or args.paced_seconds <= 0:
        parser.error("require >=5 warmups, >=30 trials and positive paced duration")
    receipt, manifest, inputs = load_inputs()
    negative = inputs[next(case["id"] for case in manifest["cases"] if case["group"] == "negative_speech")]
    if args.worker:
        measured = (
            measure_candidate(args.worker_threshold, manifest, inputs, args.warmups, args.repetitions)
            if args.worker == "quality" else paced_measurement(args.paced_seconds, 0.25, negative)
        )
        args.output.write_text(json.dumps(measured, indent=2) + "\n", encoding="utf-8")
        return
    scratch = ROOT / "scratch"
    scratch.mkdir(exist_ok=True)
    settings = []
    for index, threshold in enumerate(args.thresholds):
        worker_file = scratch / f"quality-worker-{index}.json"
        subprocess.run([
            sys.executable, str(Path(__file__).resolve()), "--worker", "quality",
            "--worker-threshold", str(threshold), "--warmups", str(args.warmups),
            "--repetitions", str(args.repetitions), "--output", str(worker_file),
        ], check=True)
        settings.append(json.loads(worker_file.read_text("utf-8")))
        worker_file.unlink()
    worker_file = scratch / "paced-worker.json"
    subprocess.run([
        sys.executable, str(Path(__file__).resolve()), "--worker", "paced",
        "--paced-seconds", str(args.paced_seconds), "--output", str(worker_file),
    ], check=True)
    paced = json.loads(worker_file.read_text("utf-8"))
    worker_file.unlink()
    result = {
        "schema_version": 1, "created_utc": datetime.now(timezone.utc).isoformat(),
        "kind": "recorded_synthetic_audio_not_acoustic_proof",
        "git_head": subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip(),
        "main_baseline": subprocess.check_output(["git", "rev-parse", "origin/main"], text=True).strip(),
        "hardware": hardware(), "python": platform.python_version(), "logical_cpus": os.cpu_count(),
        "versions": {name: importlib.metadata.version(name) for name in
                     ("numpy", "psutil", "sentencepiece", "sherpa-onnx", "sherpa-onnx-core")},
        "source_files": {name: digest(ROOT / name) for name in
                         ("prepare.py", "fixtures.py", "capture_probe.py", "benchmark.py",
                          "test_benchmark.py", "Render-Fixtures.ps1", "Validate.ps1", "requirements.txt")},
        "assets": receipt, "fixtures": manifest, "settings": settings,
        "paced": paced,
        "measurement_isolation": "Fresh Python process per threshold and paced run; RSS includes imports/fixtures",
        "live_trials": "Not consented. No microphone or audible playback used.",
        "production_changes": False, "accepted_commands_or_tool_invocations": 0,
        "release_gate": "INCOMPLETE: small synthetic corpus, no acoustic front-end, packaged host or reference headset trial",
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    write_summary(result, args.output.with_name("recorded-summary.md"), args.output.name)
    for candidate in settings:
        s = candidate["summary"]
        print(f"threshold={candidate['threshold']}; "
              f"continuous={s['continuous']['cases_detected']}/{s['continuous']['cases']}; "
              f"raw_tts_events={s['self_tts_replay']['raw_events']}; "
              f"false_events={s['negative_total']['raw_events']}")
    print("Paced:", json.dumps(result["paced"]))
    print(f"Evidence: {args.output}")


if __name__ == "__main__":
    main()
