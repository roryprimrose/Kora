# Local Speech Input

Status: product direction approved on 2026-10-22; provider selection,
implementation and exact-profile enablement remain open.

Related: [activation names](Activation_Name.md),
[voice settings](User_Configuration.md#1-voice-input-and-activation),
[wake privacy](Security_Data_Flows.md#wake-listening-privacy),
[speaker privacy](Security_Data_Flows.md#shared-space-privacy-and-optional-speaker-verification),
and [speech acceptance](Acceptance_Criteria.md#configurable-local-speech-input-gate).

## Approved Outcome and Scope

Advance the approved [voice interaction outcome](MVP_Scope.md#product-admission-and-realignment):
the user says the configured name and a command without a compulsory pause,
receives timely capture feedback, and completes the existing reviewed operation.
Equivalent PTT, typed and native recovery remain available. The user can choose
qualified local speech options appropriate to their voice, microphone and
hardware rather than accepting one accuracy/resource trade-off.

The host owns microphone lifetime, input admission and shared settings because
they cross every command and permission boundary. They are not individual
tool/skill responsibilities. Speech components produce observations and final
text, never action authority. Existing text parsing, intent handling, tool
routing, confirmations, grants, audit and reasoning-provider admission remain
unchanged.

Hosted speech input, continuous general transcription, automatic cloud
fallback, command rewriting, mandatory biometrics and continuous large-model
training are excluded. The discussion does not approve a blanket owner-only
command mode; expanding speaker restrictions beyond existing selected
output/approval policy needs a separate product decision.

## Current Implementation Boundary

The desktop composes the [Windows recognition service](../src/Kora/Program.cs)
for explicitly activated capture. Its [factory](../src/Kora.Windows/Audio/WindowsActivatedCaptureFactory.cs)
selects an installed English `System.Speech` recognizer, loads command choices
and a dictation grammar, and feeds local WASAPI/NAudio PCM into it.
The [service](../src/Kora.Windows/Audio/WindowsVoiceRecognitionService.cs)
reports ambient listening unavailable. A dictation grammar alone does not
qualify literal composer dictation; that has its own
[capture contract](../src/Kora.Core/Voice/IActivatedVoiceRecognitionService.cs).

The configurable pipeline, performance profiles, wake detector, speaker
learning and enrolled verification described below are not delivered by this
document. A selected local reasoning model or Kokoro output voice is not a
speech-input provider. Source presence is not runtime readiness.

The mode-independent [hands-free host lifecycle](../src/Kora.Application/Voice/HandsFreeVoiceRecognitionService.cs)
defines wake-only processing, generation-bound command capture, joined resource
release, existing governed text dispatch and fresh-policy re-arm. Its
[platform seam](../src/Kora.Core/Voice/IHandsFreeAudioSession.cs) separates wake
observation from command-only transcription; the
[pre-roll buffer](../src/Kora.Core/Voice/WakeAudioBuffer.cs) retains at most two
seconds and consumes only the exact post-name interval. These contracts and
deterministic fakes are implementation groundwork, not an acoustic detector.
The [Windows adapter](../src/Kora.Windows/Audio/UnavailableHandsFreeAudioStage.cs)
is explicitly unavailable. D-002 still requires an approved exact engine,
configured-name/language, endpointing and playback-rejection profile; no package,
model, microphone capture or implicit consent is introduced by this groundwork.
In particular, token timestamps are not proof of an exact exclusive wake end.

## Processing and Data Boundaries

| Stage | Work and output | Lifetime and destination |
|---|---|---|
| Wake listening | Small local keyword detector for the supported configured name | Ambient samples reach only the local detector and existing bounded memory pre-roll; no general transcript |
| Activation admission | Bind the wake/PTT event to current host ownership, consent, device and generation | No context acquisition, action approval or model-reasoning call from activation alone |
| Command capture | Preserve immediate post-name words; local VAD/endpointing bounds speech | Only the admitted command interval reaches transcription; no unrelated ambient pre-roll |
| Optional speaker observations | Supported local checks for the explicitly selected protection | Host-only bounded confidence, not identity or permission |
| Transcription | Selected local ASR converts the eligible utterance to final text | No translation or LLM cleanup/rewrite by default; partial text cannot dispatch |
| Text admission | Recheck generation, ownership, privacy, selected protection and final-result validity | At most one final result enters the existing channel-appropriate text handler |
| Existing operation | Deterministic command or separately admitted reasoning request | Existing policy controls effects and any outgoing text/context |

The canonical [wake privacy rules](Security_Data_Flows.md#wake-listening-privacy)
own the two-second pre-roll cap, audio exclusions and clearing rules.
Preserve "Kora, open settings" without a pause, without transcribing the preceding
conversation. PTT enters command capture without requiring a wake name.
Host-opened replies remain bound to their exact question/revision and cannot
become general commands or extend ambient transcription.

Wake detection, VAD, speaker verification and ASR are separate capabilities:
speech presence is not a wake match; a wake match is not a speaker match; a
speaker match is not permission. If multiple voices overlap or a different
speaker follows a wake phrase, do not infer command-speaker continuity from the
wake word. An applicable selected protection must evaluate sufficient eligible
command audio or report uncertainty; no new ambient identification is allowed.

All speech stages run locally. Wake listening and transcription make zero
hosted inference requests and incur no hosted token/audio-duration charges.
They still consume local CPU/GPU, memory and power. Explicit asset acquisition
is separate consented setup, never ambient audio egress.
Enabling hosted command reasoning does not enable microphone transmission.
Only text/context from a separately admitted reasoning request may use that
provider under its existing policy. Local failure never selects hosted speech.

## Provider Choices and Selection

These are candidates, not approved dependencies or measured Kora profiles.
Pin code, native runtime, model bytes, licence and supported environment
separately before offering a profile. Do not accept arbitrary model URLs,
executable extensions or loading roots through a settings/model request.

| Capability | Candidate and fit | Selection concern |
|---|---|---|
| Wake detection | [sherpa-onnx keyword spotting](https://k2-fsa.github.io/sherpa/onnx/kws/index.html) permits configured keywords without retraining | Evaluate custom names first; supported language/tokenization, false activation, latency and native integration still need proof |
| Wake detection | [openWakeWord](https://github.com/dscripka/openWakeWord) uses prepared wake models and can add voice-specific filters | Custom names need model preparation; code and included pretrained-model licences differ, including non-commercial model terms |
| Wake detection | [Porcupine](https://github.com/Picovoice/porcupine) provides dedicated keyword detection and Windows/.NET integration | Console-prepared custom profiles, access-key lifecycle, redistribution and local preparation must fit Kora policy; no exception is granted here |
| Transcription | Existing installed Windows recognizer | Comparison baseline with grammar support; recognizer availability/identity is installation-dependent |
| Transcription | [Whisper](https://github.com/openai/whisper/blob/main/model-card.md) with a selected local runtime | Size/language choices; assess invented text, repetition, accents and cold/warm latency |
| Transcription | [Parakeet](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3) with a selected local runtime | Assess the exact converted model/backend on supported Windows hardware, not published throughput alone |
| Native integration | [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx), [whisper.cpp](https://github.com/ggml-org/whisper.cpp) or [Whisper.net](https://github.com/sandrohanea/whisper.net) | Verify exact API/runtime support, Windows/CPU requirements, packaging, cancellation and release; wrapper names do not qualify models |

Evaluate sherpa-onnx early for configurable-name fit, but retain the D-002
candidate comparison before selection. Supporting a display name does not prove
acoustic wake support; [activation-name validation](Activation_Name.md#production-wake-word-requirements)
remains authoritative. No silent default-name alias or engine fallback is added.
Handy's [speech pipeline](https://github.com/cjpais/Handy#how-it-works) is a useful
VAD/local-ASR reference, not a replacement for Kora's action admission.

Every provider advertises supported languages/names, timing controls,
personalization, confidence semantics and compute backends. Unsupported
features are unavailable, not simulated. Recognition confidence is neither
action correctness nor comparable across engines; the current Windows
threshold must not be copied to another provider without calibration.

## Configuration and Resource Profiles

Use the shared typed host settings and existing voice/UI review rules.
This defines setting concepts, not new registered IDs, schemas or command
syntax. Native controls and applicable typed/activated voice routes must
complete the same validated operation; model proposals acquire no authority.
The [protected-call exception](Call_Aware_Speech.md) still applies.

| Setting concept | Intended choice and boundary |
|---|---|
| Activation mode | Explicit PTT or qualified local wake-name listening; mute/recovery remains independent |
| Wake profile | Exact delivered engine/model and supported name/language; prepare and validate before cutover |
| Transcription profile | Exact delivered local engine/model, explicit supported language or qualified auto-detection |
| Optional component management | Inspect, explicitly install/verify, retry or remove eligible assets through Settings; installation is separate from selection/enablement |
| Compute | Supported CPU/GPU backend and bounded resource policy; unsupported hardware reports unavailable |
| Model residency | Keep loaded for lower startup delay, or unload after an explicit bounded idle interval for lower idle memory |
| Timing | Existing bounded speech-start, trailing-silence and maximum-utterance controls where supported |
| Personalization | Separately consented learning and supported calibration/vocabulary capabilities; never implied by microphone consent |
| Speaker protection | Existing independently selected enrolled-verifier privacy/approval policies; not a performance toggle |

Offer Low resource, Balanced, Accuracy focused and Custom profiles only after
their actual trade-offs are measured. A profile resolves to explicit versioned
settings; its label is not an accuracy guarantee or an automatic hardware
benchmark. Larger models are not presumed better for every command or accent.
Offer a measured default, not an unqualified "best model."

Keep saved preference, effective profile and unavailable reason distinct.
Selection does not silently download assets, open capture, re-enroll a voice,
clear a recovery hold or substitute another model. A missing/corrupt model or
unsupported backend leaves the affected path unavailable with retry, explicit
qualified alternative or typed/native recovery. Do not imply an unqualified
PTT path is a fallback.

Apply a changed input profile only after capture retirement, confirmed
quiescence, buffer clearing and a new generation. Each admitted utterance keeps
its immutable provider/configuration snapshot; never transcribe the same audio
automatically through another provider on failure. Residency controls model
memory, not microphone enablement or retained audio. Unloading/switching must
not dispose in-use resources or permit stale results to dispatch.
CPU/GPU limits and optional background updates must preserve UI responsiveness,
cancellation and safety controls, including contention with local reasoning.
Hardware suggestions require explicit acceptance; they cannot weaken selected
protection or silently change an admitted turn.

## Optional Installation and Runtime Lifecycle

Additional wake, VAD, ASR and optional verifier model assets must support both
explicit install-time preparation and later Kora Settings setup, like the
existing optional Ollama/Kokoro flows. This is a requirement for future
qualified components, not a claim that today's installer offers them.
[Environment Setup](Environment_Setup.md#optional-local-speech-component-lifecycle)
owns the shared catalogue, detection, consent, readiness and removal workflow;
[Distribution](Distribution_And_Updates.md#selected-windows-installer-direction)
owns WiX/Burn integration and launch prerequisites.

The WiX MSI owns the protected application payload and first-party adapters.
Burn may offer unchecked, individually actionable speech preparation choices
after native installation; a Kora-only path remains. Settings uses the same
approved preparation/verification services and exact component identities.
Do not duplicate URLs, digests, licence decisions or readiness semantics in a
second installer-only catalogue. Missing adapter/native executable code requires
an admitted application release, not a downloaded model that patches Kora.

Distinguish optional model data from bundled native code and its launch
dependencies. Declining a speech model does not prove that bundled ONNX/other
native imports or shared VC++/.NET requirements disappear. Assess installer
payload/native-loader closure and supported Windows/architecture on every
runtime/backend change; do not silently acquire a GPU toolkit or driver.

Installed, verified, compatible, qualified, selected and currently usable are
different states. Neither install-time acquisition nor Settings setup selects
a provider, opens a microphone, starts learning, enrolls a verifier or changes
reasoning/output settings. Runtime settings control only the explicitly
selected speech engine/model/backend, residency and supported personalization.
The running app revalidates component readiness and its current input policy.
Skipped/failed optional work leaves Kora installed and explains how to retry
through setup/Settings without weakening a selected protection.

Removal or replacement joins owned model/capture work before changing assets;
never remove shared/user-owned dependencies or another profile's required files
as collateral cleanup. Preserve saved choices as visibly unavailable when their
assets disappear, rather than selecting Windows/another model automatically.
App repair/upgrade/uninstall must preserve user assets/preferences by default
under distribution policy, disclose incompatible retained versions and never
migrate/retrain learned profiles or delete them implicitly. Explicit profile
deletion must include active, candidate and recovery revisions/cached features;
model-file removal and biometric-profile deletion are distinct operations.

## Optional Learning and Calibration

The [security contract](Security_Data_Flows.md#optional-local-frequent-speaker-learning)
owns separate consent, eligible samples, per-SID/device protection, deletion and
enrollment separation. Implementations must distinguish these mechanisms:

| Mechanism | Possible benefit | Limit |
|---|---|---|
| Wake pronunciation calibration | Better supported-name detection for a user's pronunciation | False-activation limits still apply; explicit calibration samples are bounded and not general learning input |
| Frequent-speaker features | Personalization or attribution of a commonly encountered voice | Not authenticated ownership; does not necessarily improve the transcript |
| Supported vocabulary/pronunciation adaptation | Better recognition of recurring names and terminology | Provider-specific; over-biasing can invent words or force noise into command phrases |
| Acoustic/model adaptation | Potential accent/speech improvement | Separate demonstrated capability and resource/privacy evidence; not implied by installing an ASR model |

Prioritize lightweight, bounded calibration and provider-supported
personalization over continuous large-model retraining. Stock Whisper/Parakeet
does not become more accurate simply because Kora uses it repeatedly.
Advertise improvement only for the selected mechanism and measured conditions.

Use only eligible deliberately activated commands after learning consent.
Exclude conversational replies, ambient audio, history, playback, mixed or
uncertain samples. Successful command execution or an accepted transcript is
not a correctness/speaker label. Corrections may inform supported vocabulary
adaptation through an explicit bounded workflow, not a retained audio archive.
Do not ask the command-reasoning model to infer identities or train a profile.

Bound update time, sample contribution, derived state and CPU/GPU/memory use.
Prefer scheduling optional updates when resources are available; cancellation,
consent withdrawal or interactive load must stop/defer them safely.
Publish a candidate profile atomically between utterances after validation;
keep a bounded known-good revision for explicit recovery without retaining raw
training recordings. Reject/quarantine unstable updates rather than silently
replacing the current profile or enrolled verifier. Few other-speaker samples
cannot replace the established primary candidate without confirmation.
Version changes cannot reinterpret incompatible learned features as valid.

Provide learning/ready/paused/unavailable status and supported
test/correct/reset/delete operations. Off stops adaptation immediately and is
not a claim that derived features were deleted. Missing learning capability
does not block baseline voice; unavailable selected protection never silently
reverts to the unprotected baseline. Replay/synthesis limits must be disclosed:
speaker resemblance is not strong authentication.

## Comparison and Qualification

A short, explicitly started local setup comparison may help choose among
already qualified installed profiles. It uses bounded representative phrases,
shows errors, end-of-speech-to-final-text delay, cold versus warm startup and
resource use, and discards raw audio afterward. Calibration/test phrases are
inert: never route them as commands, approve operations, acquire task context
or confer listening consent. Cancel leaves prior settings unchanged.
Do not conflate this with the existing no-transcription microphone
test or a release-quality benchmark; it is supporting setup, not a new utility.

The [acceptance criteria](Acceptance_Criteria.md#configurable-local-speech-input-gate)
own measurement and thresholds. Evaluate complete correct-command outcomes and
false-action cases, not word error rate alone. Report name/voice/noise/device
variation, false wakes, missed wakes, immediate-command loss, hallucinated
commands, playback, speaker acceptance/rejection, latency, idle cost and active
resource peaks separately. No average or personalized improvement waives a
critical safety failure.

## Delivery and Open Decisions

Follow [I/E/Q delivery policy](Implementation_Roadmap.md#delivery-and-qualification-dependency-policy):

- **I - Implementation:** bounded provider contracts, immutable generations,
  profile/settings admission, quiescent switching and deterministic fake
  lifecycle/denial tests may merge while production profiles remain unavailable.
  A broad R09/R24 label or absent physical trial alone is not an implementation
  dependency; cite the concrete consumed contract if missing.
- **E - Enablement:** exact offered wake/ASR/backend/name/personalization profiles
  need their applicable licence, native/offline, acoustic, resource and lifecycle
  evidence before exposure. Optional learning/verifier evidence gates only that
  capability, not baseline speech.
- **Q - RC:** integrated final-byte evidence applies to the actual enabled
  manifest, not every candidate or optional mechanism listed here.

D-002 retains engine/endpointing/playback selection. D-006 retains learning,
verification and privacy qualification. D-007 retains supported Windows and
reference hardware selection. Concrete profile defaults, resource/update
budgets, idle-unload intervals and provider-specific settings remain engineering
decisions backed by those measurements; this approval does not invent values
or waive the existing speech targets.
