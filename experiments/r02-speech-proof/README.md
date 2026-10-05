# R02 speech / hardware feasibility: D-002 and D-007

Status: **partial offline proof; acoustic/packaged-host acceptance blocked**.
D-002 and D-007 remain open. This experiment does not change the decision
register, production recognition/TTS, shared manifests, solution or CI.
On 2026-10-05 the user approved publishing and squash auto-completion of this
PR **as partial research evidence**, with outstanding testing retained.
This is not approval of production speech or closure of D-002/D-007.

> **Testing to run later:** at the time of deferral the machine was locked
> and accessed remotely. Only file-based tests were eligible; live capture/playback
> requires an unlocked interactive Windows profile and a physically present
> test operator. See the [outstanding testing checklist](#outstanding-testing-checklist)
> for prerequisites, measurements and evidence to retain. Unlocking alone
> does not authorize capture or complete the missing integration.

## Baseline and contracts

The session worktree was clean before `git fetch origin` and
`git rebase origin/main`. Merged R01 PR #19, commit
`7d5e6a352261dce48f2ca4d3048650ee13f51705`, was verified in ancestry and the
approved contract text before work began. After the scoped code commit, a
second fetch/rebase onto `origin/main` completed without conflicts, still at
that R01 baseline, before final validation and first push. The full asset/
fixture regeneration, compilation, 15 tests and threshold benchmark were
repeated after rebasing. Fixture regeneration was bit-identical on this
machine; see [final validation](evidence/validation.json).

Read contracts:
[activation](../../Design/Activation_Name.md),
[voice lifecycle](../../Design/Task_Lifecycle.md#wake-listening-and-command-capture),
[privacy and consent](../../Design/Security_Data_Flows.md#wake-listening-privacy),
[speech acceptance](../../Design/Acceptance_Criteria.md#wake-word-quality-gate),
[decisions](../../Design/Decision_Register.md), and
[R02/R03 roadmap](../../Design/Implementation_Roadmap.md).
The current [user voice](../../docs/voice-and-audio.md) and
[privacy](../../docs/privacy-safety-and-logs.md) guides describe bootstrap
behavior, not completion of the R01 consent/wake-only contract.

Inspected existing implementations:

- [Windows recognition](../../src/Kora.Windows/Audio/WindowsVoiceRecognitionService.cs):
  16 kHz mono PCM16 WASAPI input; English installed recognizer; host phrase
  grammar and assistant-prefixed dictation; confidence >=0.62.
  Ambient audio enters Windows recognition before activation matching.
- [Blocking stream](../../src/Kora.Windows/Audio/BlockingAudioStream.cs):
  unbounded `BlockingCollection<byte[]>`, not the designed two-second ring.
  No explicit five-second empty/one-second silence/60-second capture control
  was established by this inspection.
- [TTS](../../src/Kora.Windows/Audio/WindowsTextToSpeechService.cs):
  Windows SAPI or optional local
  [Kokoro](../../src/Kora.Windows/Audio/KokoroTextToSpeechProvider.cs) synthesis
  to memory and selected WASAPI output; cancellation/stop paths exist.
  No real sample-stop timing was measured.
- [Application coordination](../../src/Kora.Application/ViewModels/MainViewModel.cs):
  during active TTS, unprefixed recognition and normalized phrases contained
  in the active spoken text are ignored; another prefixed result stops
  playback before dispatch. This text filter is not acoustic AEC or source
  attribution. Approval speech temporarily releases capture.

Nothing above was modified or advertised as production wake recognition.
R03 owns capture consent/generations/ownership/OS-event integration; R09 and
integrated acceptance own the eventual wake front-end and playback policy.

## Reproduce (Windows x64, CPython 3.12)

Run from the repository root in PowerShell:

```powershell
python -m venv experiments\r02-speech-proof\.venv
& experiments\r02-speech-proof\.venv\Scripts\python.exe -m pip install --only-binary=:all: --require-hashes -r experiments\r02-speech-proof\requirements.txt
Set-Location experiments\r02-speech-proof
& .venv\Scripts\python.exe prepare.py
.\Render-Fixtures.ps1
& .venv\Scripts\python.exe fixtures.py
.\Validate.ps1
& .venv\Scripts\python.exe benchmark.py --output evidence\recorded-results.json --thresholds 0.1 0.25 0.5 --warmups 5 --repetitions 30 --paced-seconds 60
```

Only dependency installation and `prepare.py` can download anything.
Inference reads local assets/files. There is **no microphone or audible
playback path** in this harness; SAPI renders to files silently.
No application, clipboard, model-runtime, task or approval dispatch is wired.
This is not network-blocked/air-gap verification: the host network was not
disabled, and absence of a Python network call is not a packet capture.

All Python dependency versions and Windows CPython wheel SHA-256 digests are
local to [requirements.txt](requirements.txt); no packages are installed into
shared environments. The 17,626,723-byte official sherpa archive is pinned by
URL and SHA-256 in [prepare.py](prepare.py). Its upstream release provides no
signed checksum: this pin reproduces the bytes observed, not publisher
signature verification. Only the tokenizer, vocabulary, model README and
chosen ONNX files are extracted; upstream test audio is not used.

Fixture/model receipts carry per-file hashes. The benchmark checks them
before inference and records source-file hashes and the Git baseline.
Model/audio assets, environment and scratch runs are ignored locally;
only small JSON/text evidence is committed. Do not `git add -f` those assets.
Windows/SAPI versions or voices may change synthesized waveforms: regenerate
and retain the new provenance rather than asserting cross-machine bit identity.

`Validate.ps1` checks dependency closure, compiles the Python sources, and
runs the deterministic tests. It writes [unit-tests.txt](evidence/unit-tests.txt).
The benchmark writes [recorded-results.json](evidence/recorded-results.json)
and a generated [summary](evidence/recorded-summary.md).
Use another output directory for exploratory runs; retain the committed
evidence as the measured snapshot, not a fabricated expected result.

## Candidate availability and separate code/model terms

Review date: 2026-10-05. [candidates.json](candidates.json) records versions,
settings, sources, NOT_RUN states and blockers. Unrun candidates have **no**
performance scores; they are not treated as zero recall or passing tests.

| Candidate | Code/bindings | Models / engine redistribution | Actual evaluation |
|---|---|---|---|
| openWakeWord 0.6.0 | [Apache-2.0 source](https://github.com/dscripka/openWakeWord/blob/v0.6.0/README.md#license); universal Python wheel, Windows ONNX path | Supplied pretrained models are **CC BY-NC-SA 4.0**, not Apache-2.0. No supplied Kora classifier. Rights for custom model/data and commercial-purpose evaluation remain unreviewed. | Not installed/run; no cleared Kora model. Reviewed starting score threshold 0.5, 16 kHz PCM16 / 1,280 samples; not measured settings. |
| sherpa-onnx 1.13.8 + core 1.13.8 | [Apache-2.0 engine](https://github.com/k2-fsa/sherpa-onnx/blob/v1.13.8/LICENSE); actual Windows CPython 3.12 AMD64 wheels exercised | English GigaSpeech 3.3M KWS author [model card](https://www.modelscope.cn/api/v1/models/pkufool/sherpa-onnx-kws-zipformer-gigaspeech-3.3M-2024-01-01/repo?Revision=master&FilePath=README.md) and exact archive README separately declare Apache License 2.0. Model trained on GigaSpeech XL; that declaration is not a warranty of all training-data rights. | Available, account-free local KORA BPE keyword experiment. No custom training. Int8 encoder/joiner, float decoder; thresholds 0.1/0.25/0.5. |
| Porcupine 4.0.3 | Public Python binding [Apache-2.0](https://github.com/Picovoice/porcupine); SDK documents Windows AMD64 | [Proprietary model/inference engine](https://picovoice.ai/docs/faq/general/), subject to [Picovoice terms](https://picovoice.ai/docs/terms-of-use/) effective 2026-03-30 and any additional agreement; binding license is not redistribution permission. | Not installed/run: no authorized account/AccessKey/evaluation agreement or Windows Kora `.ppn`. Reviewed sensitivity 0.5; runtime frame length/sample rate must be read from a licensed initialized handle. |

openWakeWord's noncommercial model restriction applies to purpose, not simply
whether files are committed: internal commercial evaluation is not assumed
exempt. The relevant [CC legal terms](https://creativecommons.org/licenses/by-nc-sa/4.0/legalcode.en)
need clearance or an independently licensed trained Kora model.
An unrelated Hey Jarvis test is not substituted for a Kora benchmark.

Porcupine's on-device inference does not establish indefinitely disconnected
licensing. Cold-start activation/usage metadata and benchmark publication
requirements (terms section 7.4) need explicit verification. No key is sought,
persisted or included in this branch; no Console training/upload is performed.

Apache-2.0 redistribution requires the applicable license, copyright/NOTICE
preservation and modified-file notices. Before a product package, independently
inventory native-runtime/third-party binary notices (including ONNX Runtime)
and review exact bundled model bytes. This branch redistributes none of those
models/binaries and is not a legal sign-off for a Windows/.NET package.

## Fixtures and what the tests actually prove

[Render-Fixtures.ps1](Render-Fixtures.ps1) uses installed **David and Zira
Desktop en-US** voices on the recorded machine, each at SAPI rates -1 and +1.
Text is authored for this experiment, with no human recording, enterprise
content, secrets or externally sourced audio. Installed Windows voices remain
subject to their own terms; generated audio is local/ignored, not relicensed
or distributed. The provenance receipt lists text, voice, rate and source hash.

[fixtures.py](fixtures.py) generates 42 mono 16 kHz PCM16 cases, 557.02 seconds:
four each of wake-only, zero-gap spliced immediate command, natural continuous
"Kora, open settings", 0.8-second paused command, 65-second tone command,
10 dB and 0 dB synthetic white-noise mixtures, raw self-TTS replay, digital
overlap, and similar-word negative speech; plus 30 seconds each of silence
and generated noise. PCG64 seed 2007 is recorded. Only 107.847 seconds are
negative exposure. The 65-second tone tests a bound, not speech quality.

Two synthetic US-English voices are **not** five human speakers. Rate
variants are not extra speakers. White noise is **not recorded office noise**.
No Australian/other accents, dysarthria, far-field/reverberation, headset
frequency response, real conversation/media, echo cancellation or Windows
device routing is represented. Do not generalize percentages to those cases.

For each threshold:

- Raw keyword detections and all per-case events are retained, including
  failures. False activation counts events, not just files, divided by actual
  negative duration. Zero events here cannot establish <=1/hour.
- Fresh subprocesses isolate per-setting memory; CPU is process user+kernel
  time divided by wall time and logical CPU count. Accelerated inference CPU
  is not wake-idle CPU. A separate 60-second paced repeated-negative pilot
  reports total-capacity CPU and sampled RSS. Repetition adds no corpus diversity.
- Five warmups precede 30 repeated-clip trials. Nearest-rank p95 is used.
  Cold **model construction** is separate, excluding Python imports/startup.
  Processing milliseconds are not visible activation/STT/TTS-stop latency.
- [capture_probe.py](capture_probe.py) checks two-second overwritten pre-roll,
  five-second empty timeout, one-second trailing silence, 60-second
  sample-bounded capture, duplicate activation and stale-generation clearing.
  Tests include exact threshold edges and an unaligned command boundary.
- Actual sherpa wake events drive the recorded capture probe for annotated
  cases. First 200 ms of spliced command audio are compared sample-for-sample.
  Wake exclusion uses a **fixture oracle boundary**, with a >200 energy mask
  at 10 ms cadence. Real provider alignment/VAD/transcription is absent;
  natural continuous utterances intentionally have no invented word boundary.
- Self-TTS and overlap are fed **without** source attribution. Playback
  triggers are retained as an observed failure, not hidden by a filename/label
  filter or a gate that suppresses all user interruption.

## Result and recommendation

See the generated [measured summary](evidence/recorded-summary.md) for timings,
resources and individual-condition counts. At the default threshold 0.25,
continuous utterances detect 2/4, 10 dB noise 2/4, 0 dB noise 0/4, and raw TTS
produces five wake events. Lowering to 0.1 improves this tiny quiet set but
still produces eight TTS events and detects only 3/4 at 10 dB noise.
No production threshold is selected by tuning on this same small corpus.

**No production candidate or hardware floor is recommended.** Sherpa is the
available account-free candidate for a separately approved follow-up because
Kora-specific local inference and sample-buffer feasibility were exercised.
It is not a solution to playback rejection, word alignment or human barge-in.
The inspected string-based production playback filter is not substituted for
these missing measurements. Per the contract, an integrated setup that cannot
establish self-TTS rejection must explicitly disable spoken output, not wake
activation; this experiment changes neither setting nor production policy.

## D-007 reference and remaining matrix

The JSON records exact CPU/OS build revision, physical memory, SSD, runtime,
power plan and running Code/Kora/Ollama process counts without machine
serials/account identifiers. Recorded pilot hardware:

- HP ZBook Ultra G1a, AMD Ryzen AI MAX 385, 8 physical / 16 logical cores.
- 25,530,408,960 bytes physical RAM (23.777 GiB); SK hynix PC801 SSD.
- Windows 11 Enterprise x64, OS family build 26300; full revision in JSON.
  Microsoft's [release matrix](https://learn.microsoft.com/en-us/windows/release-health/windows11-release-information)
  identifies build 26300 as 26H2. Product support is not established by this run.
- CPython 3.12.10; pinned package versions in JSON; CPU provider, two requested
  inference threads, no GPU/NPU used; High performance power scheme.
- VS Code was open, Kora/Ollama were not. Kora was not launched because its
  bootstrap auto-opens capture. Other shared-machine contention is uncontrolled.
  No wired headset or speaker/mic setup was identified/used.

| Matrix row | Evidence | Remaining |
|---|---|---|
| This Windows x64 workstation, synthetic files | Actual native Python KWS, buffers, CPU/RSS/processing latency | Acoustic and packaged .NET host trial |
| Supported Windows 11 release(s), >=8 logical cores / >=16 GiB / SSD | Acceptance target only; this stronger machine is not a minimum-floor test | Named lab owner, servicing revision and lower-floor Intel/AMD trials |
| Wired headset and selected output endpoint | Not used | Endpoint identity, volume, mute/failure recovery, distances and 30-minute wake-only overhead |
| Speaker + microphone with supported playback reference/AEC | Not used | Self-TTS/output correlation and live human interruption |
| Native/tray/keyboard/screen-reader recovery | Source contract only | Real accessibility evidence coordinated with R03/R09 |

## Outstanding testing checklist

The [shared deferred-validation register](../../Design/Deferred_Validation.md)
links this acoustic checklist and the independent containment/distribution
proofs. Their approval scopes and capability acceptance remain separate.

The user explicitly selected **recorded synthetic fixtures only; live trials
deferred** in this session. No microphone capture or audible output occurred.
The scripts do not offer a live switch. Do not start Kora as a shortcut to a
live trial. Coordinate with the machine/test owner and R03 before a separate
bounded trial; consent must cover participants/bystanders, duration,
endpoint/volume, retention and a visible stop control. Consent to a test is
not ongoing microphone consent.

### Safe to rerun remotely, including while locked

These are file-based checks only. They do not require opening capture,
unlocking Windows or starting Kora. If the environment/fixtures have not been
prepared, follow [Reproduce](#reproduce-windows-x64-cpython-312) first.

- [ ] Rerun dependency validation, source compilation and the 15 deterministic
  tests with `.\Validate.ps1`. This refreshes the local test receipt, not
  acoustic evidence.
- [ ] Rerun the threshold sweep into an ignored scratch directory rather than
  replacing the committed measured snapshot:

  ```powershell
  # From experiments\r02-speech-proof
  & .venv\Scripts\python.exe benchmark.py --output scratch\later-recorded-results.json --thresholds 0.1 0.25 0.5 --warmups 5 --repetitions 30 --paced-seconds 60
  ```

- [ ] Optionally repeat with `--paced-seconds 1800` for a 30-minute **file-only
  Python** resource pilot. This still does not satisfy the integrated
  wake-only overhead gate with Kora UI, devices and actual muted-idle baseline.
- [ ] Review candidate/model redistribution clearance and packaged binary
  notices. openWakeWord and Porcupine remain blocked until their documented
  availability/licensing prerequisites are met; do not supply credentials or
  download unreviewed models as a shortcut.

### Before a live test session

The current scripts cannot run live trials. The checklist below is a test
plan for a separately prepared instrumented host/front-end, **not a command
to launch the existing bootstrap and assume it implements the contract**.

- [ ] Arrange a time with a physically present operator using the intended
  Windows profile, plus consent from every participant and potentially
  recorded bystander. Identify a wired headset and a separate supported
  speaker/microphone setup; schedule each independently.
- [ ] Have the operator unlock Windows locally. Confirm the session is
  connected and authoritative Unlocked; Locked, Disconnected or Unknown is
  a stop condition. Remote Desktop audio redirection, virtual endpoints and
  loopback must be recorded; redirected audio is not proof of the machine's
  physical acoustic setup.
- [ ] Coordinate with R03/R09 and identify a test build implementing the
  relevant consent, single-owner capture, generation, wake-only, endpointing
  and playback-reference controls. Do not run a second audio owner alongside
  another Kora build. If these controls or measurement hooks are unavailable,
  mark the affected trial **blocked**, not passed.
- [ ] Agree on bounded duration, microphone/output endpoints, comfortable
  volume, distances, fixture sources, local retention/deletion and a visible
  immediate stop control. Use synthetic or licensed/consented material and
  non-destructive commands such as "open settings"; do not exercise power
  controls or collect private clipboard/context data for this speech proof.
- [ ] Start capture only after current test consent and fresh
  ownership/session/permission/device gates. Unlock/resume/reconnection alone
  must not reopen it; explicit Enable listening is required. On lock, consent
  withdrawal, permission/device loss or an unknown session state, stop the
  trial and verify capture closes and buffers clear.

### Live / instrumented acceptance work still outstanding

Every row below is currently **not run**, even where deterministic probes
passed. Record headset and speaker/microphone results separately; do not
combine conditions or hide failed trials in averages.

| Check | Procedure and required result | Evidence to retain |
|---|---|---|
| Real command capture and endpointing | Speak immediate "Kora, open settings" and wake-then-command; verify first command words survive and wake/unrelated ambient audio is excluded from actual transcription. Wake-only expires after 5 s without model/context/tool calls; 1 s trailing silence endpoints; commands stop at 60 s; pre-roll never exceeds 2 s and is overwritten. Include custom-only name switching, retired callbacks and duplicate activation. | Annotated utterance/detection/capture/transcript timestamps, buffer sample counts, transition reasons and zero duplicate dispatch; not oracle-only results. |
| Wake recall | Per setup, >=100 activations across >=5 consented speakers in each quiet, recorded office-noise and during-TTS condition. Require >=95% quiet and >=90% noise/during-TTS recall. Record accents and report each condition separately. | Trial IDs, speaker pseudonyms, conditions, model/threshold, detections/misses and denominators. |
| False activation | Per setup, >=10 h diverse licensed/consented negative audio including similar words, conversation and media; <=1 false activation/hour. Repeating the short synthetic corpus does not establish diversity. | Actual exposure duration, fixture provenance and every false event; any activation still obeys context/action policy. |
| Self-TTS and other playback rejection | Replay >=100 Kora-generated outputs containing "Kora"/activation cues; require zero self-activations and playback-derived commands. Test supported active Windows output/loopback routes; disclose unsupported system-output correlation. | Output endpoint, reference/AEC configuration, source attribution and event/command counts. If self-TTS rejection cannot be established, suspend spoken output explicitly while retaining wake activation and visual recovery. |
| Human interruption and latency | A person says "Kora, stop" / "Kora, stop speaking" during real TTS. After >=5 warmups, measure >=30 trials: interruption p95 <=500 ms, stop activation to final playback sample p95 <=250 ms, wake feedback p95 <=500 ms, final transcript for a <=10 s utterance p95 <=2 s. Record cold start separately. | Synchronized acoustic wake-end, capture/UI, stop activation, last-output-sample and transcript timestamps; individual results and p95, not file-processing time. |
| Integrated resources | >=30 min wake-only with UI and normal local services open; mean CPU <=5% of total machine capacity and incremental working set <=200 MiB over actual muted idle. | CPU/RSS time series, muted-idle baseline, OS/runtime/model versions, power plan and competing workload. |
| Hardware, recovery and accessibility | Repeat relevant gates on proposed supported Windows/CPU/RAM configurations, including the intended lower hardware floor. Check selected endpoint identity, mute/removal/failure recovery and native/tray/keyboard/screen-reader fallback. | Named test owner, exact hardware/OS revisions, endpoint configuration and observed recovery/accessibility outcomes. |
| Packaging / offline boundary | Exercise the pinned detector/assets in the packaged Windows host; review native/model notices. Verify the intended local-only behavior with separately controlled network-blocking/observation without disrupting the remote session. | Final artifact identity/hashes, notices, installation/probe results and observed network boundary. Do not infer this from Python-only inference. |

### Recording a later run

Keep raw live recordings local and outside version control under the agreed
retention policy; do not include private speech or device/account identifiers
in diagnostic logs. Commit only reviewed, content-minimizing measurements and
licensed fixture provenance. For each trial group, record:

- Date, test/build revision, operator, session state, consent scope and
  headset versus speaker/microphone setup.
- Exact OS build, CPU/logical cores/RAM/storage, power profile, runtime/model
  hashes, thresholds, endpoint routing, distances, volume and AEC/reference.
- Fixture license/consent/provenance, speaker pseudonyms/accents, noise source,
  repetitions/warmups and individual observed outcomes/timings.
- Actual versus expected result, **pass / fail / blocked / not run**, residual
  limitations and any interruption of the test session.

Do not edit [recorded-results.json](evidence/recorded-results.json) or
[validation.json](evidence/validation.json) to imply that a later live trial
was part of this offline run. Add separately identified reviewed acoustic
evidence once it exists.

### Merge versus acceptance

On 2026-10-05 the user requested publishing and auto-completing the PR after
the outstanding-testing documentation was added. The approved merge scope
is **partial research evidence**, not a completed feasibility/acceptance gate.
Live trials are not prerequisites for merging that limited scope; all
outstanding gates above remain required before claiming D-002/D-007 completion
or authorizing the corresponding production integration.

No required checks/reviews may be bypassed. R01 has landed; unrelated R02
proof branches are not prerequisites for safe experiment work. The PR may be
marked ready and squash auto-merge requested for this limited scope, subject
to normal GitHub checks/reviews. The original
[validation receipt](evidence/validation.json) preserves the draft disposition
at the time of the offline run; it is historical evidence, not the current
publication status. Merging this research does not change its measured results,
make an unrun test pass, or enable any production capture/playback capability.
