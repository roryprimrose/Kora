# Recorded synthetic-audio results

Generated from [recorded-results.json](recorded-results.json). Not acoustic or release proof.
Source revision: `7d5e6a352261dce48f2ca4d3048650ee13f51705`; main baseline: `7d5e6a352261dce48f2ca4d3048650ee13f51705`.
Source-file hashes in the JSON identify the measured experiment even if evidence changes later.

| Threshold | Wake | Spliced immediate | Continuous | Paused | White noise 10 dB | White noise 0 dB | Raw TTS events |
|---|---|---|---|---|---|---|---|
| 0.1 | 4/4 | 4/4 | 4/4 | 4/4 | 3/4 | 0/4 | 8 |
| 0.25 | 4/4 | 4/4 | 2/4 | 4/4 | 2/4 | 0/4 | 5 |
| 0.5 | 0/4 | 1/4 | 0/4 | 0/4 | 0/4 | 0/4 | 1 |

Threshold 0.1: 0 false events over 107.847 seconds of synthetic negative audio.
Threshold 0.25: 0 false events over 107.847 seconds of synthetic negative audio.
Threshold 0.5: 0 false events over 107.847 seconds of synthetic negative audio.
This is not evidence for <=1 false activation/hour; the required diverse 10-hour corpus is absent.
Raw TTS wake events demonstrate that the detector alone cannot reject self-playback.
No user commands or tools were dispatched.

| Threshold | Cold construction ms | Accelerated frame p95 ms | Clip processing p95 ms | Incremental observed RSS MiB |
|---|---|---|---|---|
| 0.1 | 1133.70 | 7.12 | 98.45 | 52.01 |
| 0.25 | 1074.25 | 7.27 | 87.08 | 51.86 |
| 0.5 | 1081.55 | 7.04 | 87.27 | 52.13 |

Paced default-threshold run: 60.00 seconds, 2.508% of total CPU capacity, 49.26 MiB incremental observed RSS, 8.76 ms frame-processing p95.
Python imported/fixture-loaded idle is the memory baseline, not muted Kora.
RSS is sampled between frames, not a guaranteed transient peak.
Cold construction excludes Python startup/import time. These metrics are not end-to-end feedback latency.

Threshold 0.1: 5 warmups, 30 measured repeated-clip trials.
Threshold 0.25: 5 warmups, 30 measured repeated-clip trials.
Threshold 0.5: 5 warmups, 30 measured repeated-clip trials.

| Threshold | Wake-only annotated audio delay min/max ms |
|---|---|
| 0.1 | 477.06 / 652.00 |
| 0.25 | 477.06 / 652.00 |
| 0.5 | Not detected |
Delays use the fixture's trimmed wake end and the first observed feed/decode sample.
They include 100 ms feed quantization and model lookahead, not real end-of-word/visible UI latency.
No visible-feedback latency release gate is established.

| Threshold | Immediate first 200 ms exact | Empty ended at 5 s | Long capture bounded to 60 s |
|---|---|---|---|
| 0.1 | 4/4 | 4/4 | 4/4 |
| 0.25 | 4/4 | 4/4 | 4/4 |
| 0.5 | 1/4 | 0/4 | 1/4 |

Endpointing uses a synthetic energy mask and oracle wake/command boundary, not provider alignment or real VAD.
One-second silence and two-second overwrite bounds also have deterministic unit tests.
Real wake removal, command transcription, TTS sample-stop and human barge-in remain unproven.

Live microphone/playback trials were deferred by the user. D-002 and D-007 remain open.
