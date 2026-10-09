# 2026-10-09 unattended file-only speech proof

**Partial synthetic proof only. D-002 and D-007 remain open.** This run
neither changes production speech nor qualifies acoustic, packaged-host,
reference-hardware or release acceptance. No microphone, audible output,
participant, UI, audio endpoint, power profile or device setting was used or
changed. No live `capture_probe` invocation occurred; its deterministic
buffer model was exercised only by tests and file inference.

The [bounded receipt](unattended-20261009.json) retains measured counts,
code hashes and the hash identities of the unchanged raw evidence retained
in assigned local session proof storage. Historical receipts remain untouched.
No generated audio, model, environment, binary, machine-specific filesystem
input or secret is committed.

## Actual source and preparation

Measured HEAD and main baseline were
`e0692f438a058de0a20021b3420a981849706dbf`. The source worktree was **dirty**
with the experiment-local fixes; this is not an assertion that unchanged
baseline code produced these results. The receipt's source hashes identify
the exact measured executable sources independently of subsequent commits
or rebases.

Initial `Validate.ps1` correctly stopped because no experiment environment
existed. Only then was a local venv created with the already installed
CPython 3.12.10 launcher. All five dependencies used the reviewed Windows
wheel hashes and binary-only installation. `prepare.py` checked the existing
pinned sherpa archive SHA-256 before extracting only reviewed model files.
There was no global dependency/runtime install or additional candidate download.

The original silent renderer wrote its first WAV but then failed with
`0x8004503A` at `AudioOutputStream = null`, attempting to resolve default
audio output. Retrying in Windows PowerShell also failed. The renderer now
keeps the voice bound to its file stream throughout and closes/releases the
stream without reverting to a device. With that fix it rendered **24 source
files** silently. Fixture generation produced **42 cases / 557.021 seconds**;
all 42 audio hashes match the historical synthetic fixture snapshot. This
does not establish portability of SAPI bytes to another machine.

`Validate.ps1 -TestOutput` now permits a fresh receipt instead of overwriting
the historical default. Dependency closure, source compilation and
**27/27 deterministic tests** passed. The 15 speech tests and 12 privacy
receipt tests are synthetic; none establishes an actual OS/device release
timing. Test-only temporary files are scoped to ignored experiment scratch
and cleaned automatically.

## Measured counts, including misses

Four cases per group represent two installed en-US SAPI voices at two rates,
not four human speakers. Thresholds are exploratory, not selected production
settings.

| Threshold | Wake | Spliced immediate | Continuous | Paused | White noise 10 dB | White noise 0 dB | Raw self-TTS events | Raw digital overlap events |
|---|---|---|---|---|---|---|---|---|
| 0.1 | 4/4 | 4/4 | 4/4 | 4/4 | 3/4 | 0/4 | 8 | 2 |
| 0.25 | 4/4 | 4/4 | 3/4 | 4/4 | 2/4 | 0/4 | 6 | 1 |
| 0.5 | 0/4 | 1/4 | 1/4 | 0/4 | 0/4 | 0/4 | 2 | 0 |

Every threshold had **0 false events / 107.847 seconds** of unique synthetic
negative exposure. The paced replay adds no independent corpus diversity.
This is not proof of the <=1/hour gate. Raw playback wake events remain an
observed failure of detector-only self-TTS rejection, not admitted commands.

| Threshold | Exact first 200 ms + 1 s silence endpoint | Empty endpoint exactly 5 s | Long capture exactly 60 s |
|---|---|---|---|
| 0.1 | 4/4 | 4/4 | 4/4 |
| 0.25 | 4/4 | 4/4 | 3/4 |
| 0.5 | 1/4 | 0/4 | 1/4 |

Denominators retain missed detections and unsuccessful probes. These checks
use annotated fixture boundaries and a >200 energy mask, not production
VAD, word alignment or actual command transcription. The two-second
overwritten pre-roll, retired generations and exact boundary cases also
remain covered by deterministic tests.

Results differ from historical detections despite identical fixture audio.
No cause is established and no accuracy improvement is claimed from one
run. Preserve both snapshots; neither replaces the required acoustic corpus.

## Qualified processing pilot, not acoustic latency

The host reported a **Microsoft virtual machine**, AMD EPYC 7763, 8 physical /
16 logical cores, 68,665,831,424 bytes RAM, Windows 11 Enterprise x64
26H2 `10.0.26300.9457`, and High performance power profile. This is not
the earlier physical workstation or an intended lower-floor rig. Parallel
proof/build workloads and VS Code were present; contention was uncontrolled.
Inference used CPU only with two requested threads.

Each threshold used a fresh subprocess, **5 warmups / 30 repeated-clip trials**,
and nearest-rank p95. Cold construction excludes interpreter imports/startup.

| Threshold | Cold construction ms | Accelerated frame p95 ms | Repeated clip p95 ms | Incremental sampled RSS MiB |
|---|---|---|---|---|
| 0.1 | 2445.48 | 22.33 | 672.19 | 52.83 |
| 0.25 | 3744.99 | 17.12 | 410.31 | 53.73 |
| 0.5 | 2376.22 | 13.66 | 216.52 | 52.10 |

The threshold-0.25 paced pilot ran **60.00 s**, with **3.760% total-capacity
CPU**, **50.25 MiB incremental sampled RSS**, and **18.72 ms frame p95**.
It has an imported Python/fixture baseline, not muted Kora. RSS sampling
does not guarantee transient peaks. Audio-sample wake delays were
477.06–652.00 ms at 0.1 and 0.25, with no wake-only detection at 0.5; these
include 100 ms feed quantization/lookahead, not actual UI feedback latency.

## Gates still open

| Gate | Required | This run |
|---|---|---|
| Acoustic recall | Per setup >=100 activations, >=5 consented speakers; >=95% quiet, >=90% noise/during TTS | **Not run**; tiny synthetic voices and white noise do not qualify |
| False activation | >=10 h diverse negative exposure per setup; <=1/hour | **Not qualified**; 107.847 s unique synthetic negatives |
| Self-TTS rejection | >=100 outputs; zero self-activations/playback-derived commands | **Not qualified**; 8/6/2 raw events, no source attribution |
| Acoustic wake/interrupt/stop/transcript latency | >=5 warmups / >=30 trials; p95 <=500/500/250 ms and <=2 s transcript | **Not measured**; processing time cannot substitute |
| Integrated resources | >=30 min wake-only; <=5% total CPU and <=200 MiB over actual muted idle | **Not qualified**; 60 s isolated Python pilot despite smaller numeric readings |
| Reference-floor hardware/accessibility/endpoint recovery | Intended physical configurations and observed recovery | **Not run**; shared VM and no devices |
| Packaged-host lock/resource privacy | Actual correlated OS observation, buffer clear and confirmed release evidence | **Not measured**; synthetic receipt tests only |

No threshold, physical speech setup, unsupported routing, redistribution
clearance or production speech capability is accepted by this evidence.
openWakeWord and Porcupine remain unrun with their existing prerequisites.
The full [outstanding checklist](../README.md#outstanding-testing-checklist)
is unchanged. Normal required CI/review/up-to-date rules still apply to
merging this bounded research.

## Commands

All executed shell commands started at the isolated worktree root. These
repository-relative equivalents omit machine-specific interpreter and
artifact-storage path inputs:

```powershell
& .\experiments\r02-speech-proof\Validate.ps1
# Expected first failure: local environment absent.
py -3.12 -m venv experiments\r02-speech-proof\.venv
& .\experiments\r02-speech-proof\.venv\Scripts\python.exe -m pip install --only-binary=:all: --require-hashes -r .\experiments\r02-speech-proof\requirements.txt
& .\experiments\r02-speech-proof\.venv\Scripts\python.exe .\experiments\r02-speech-proof\prepare.py
& .\experiments\r02-speech-proof\Render-Fixtures.ps1
# Original renderer failed at the null reset; rerun after the file-bound fix.
& .\experiments\r02-speech-proof\.venv\Scripts\python.exe .\experiments\r02-speech-proof\fixtures.py
& .\experiments\r02-speech-proof\Validate.ps1 -TestOutput .\experiments\r02-speech-proof\scratch\unattended-20261009\unit-tests.txt
& .\experiments\r02-speech-proof\.venv\Scripts\python.exe .\experiments\r02-speech-proof\benchmark.py --output .\experiments\r02-speech-proof\scratch\unattended-20261009\recorded-results.json --thresholds 0.1 0.25 0.5 --warmups 5 --repetitions 30 --paced-seconds 60
```

Raw JSON, generated summary, test output, console output, model/fixture
receipts and locally generated file inputs are retained outside version
control in assigned proof storage before worktree cleanup. Benchmark worker
JSON and test-only scratch directories were removed; no temporary code was
introduced. The parent owns final isolated-worktree cleanup.
