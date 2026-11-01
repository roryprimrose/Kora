# Local Speech Input

Status: product direction approved on 2026-10-22; span-end-capable local wake
component approved on 2026-10-28. The native/command implementation is present;
exact-profile enablement and acoustic qualification remain open.

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

The desktop [voice composition](../src/Kora/VoiceInputComposition.cs) shares one
host lifecycle across recognition, activated input and profile switching. It
borrows the DI-owned [Windows recognition service](../src/Kora.Windows/Audio/WindowsVoiceRecognitionService.cs)
for explicitly activated capture. Its [factory](../src/Kora.Windows/Audio/WindowsActivatedCaptureFactory.cs)
selects an installed English `System.Speech` recognizer, loads command choices
and a dictation grammar, and feeds local WASAPI/NAudio PCM into it.
The [service](../src/Kora.Windows/Audio/WindowsVoiceRecognitionService.cs)
reports ambient listening unavailable. A dictation grammar alone does not
qualify literal composer dictation; that has its own
[capture contract](../src/Kora.Core/Voice/IActivatedVoiceRecognitionService.cs).

Performance profiles, speaker learning and enrolled verification described
below are not delivered by this increment. A selected local reasoning model
or Kokoro output voice is not a speech-input provider. Source presence is not
runtime readiness.

The mode-independent [hands-free host lifecycle](../src/Kora.Application/Voice/HandsFreeVoiceRecognitionService.cs)
defines wake-only processing, generation-bound command capture, joined resource
release, existing governed text dispatch and fresh-policy re-arm. Its
[platform seam](../src/Kora.Core/Voice/IHandsFreeAudioSession.cs) separates wake
observation from command-only transcription; the
[pre-roll buffer](../src/Kora.Core/Voice/WakeAudioBuffer.cs) retains at most two
seconds and consumes only the exact post-name interval. These contracts and
deterministic fakes alone do not establish acoustic accuracy.
The [Windows stage](../src/Kora.Windows/Audio/WindowsHandsFreeAudioStage.cs)
now owns a real local native keyword-span path and command-only PCM consumer.
Its [host-owned release catalogue](../src/Kora.Windows/Audio/HandsFreeNativeProfileCatalogue.cs)
now binds a bundled, exact Windows x64 PocketSphinx library and an explicitly
prepared optional data package. Settings and optional installer assistance share
the [preparation offer](../src/Kora.Windows/Audio/HandsFreeSetupCatalogue.cs)
and existing speech-component service/store. Preparation verifies bytes and
the inert production asset binder; it does not load native code or execute a
model. The profile has **no qualified configured names**, so production capture
remains unavailable pending name/acoustic and conservative playback-exclusion
qualification. No microphone consent, profile selection or capture readiness
is inferred from preparation.

### One explicitly reviewed activated command

The [reviewed command owner](../src/Kora.Application/Voice/ReviewedVoiceCommandCapture.cs) implements supporting voice reliability: **Review one spoken command**, bounded local capture, **Finish capture**, exact inert transcript, optional correction, then separate **Dispatch this exact voice command once** or cancellation. This is one utterance, not a saved setting or a change to direct activated/PTT routing.

The [host binding](../src/Kora.Application/ViewModels/MainViewModel.ReviewedVoiceCommand.cs) captures original foreground/source, channel, name, device, topology, call, privacy, ownership/control and input revisions before opening capture. Recognition generations and confirmed native release belong to the capture owner. Any changed source or target expires the candidate rather than retargeting another conversation, editor, question or generic reply.

[Hands-free wake resumption](../src/Kora.Application/ViewModels/MainViewModel.HandsFreeVoice.cs) also waits for this owner to release capture and review; physical microphone release alone does not end the retained review.

Editing applies only shared reject-only [text and credential bounds](../src/Kora.Core/Conversation/ConversationText.cs), never command parsing, tools, model rewriting or execution. Native confirmation enters the [existing activated command admission](../src/Kora.Application/ViewModels/MainViewModel.cs) as **ActivatedVoice**; it cannot cleanse call/privacy/authentication restrictions or approve the resulting operation. Independent high-risk approval and exact hosted-send consent remain mandatory.

Existing question replies, approvals, prompt-input capture and literal composer/owned-editor dictation retain exclusive owners; reviewed command capture refuses those contexts ([host admission](../src/Kora.Application/ViewModels/MainViewModel.ReviewedVoiceCommand.cs)). Capture and review share a 60-second lifetime. Cancel, expiry, private/device/owner changes, native hide/deactivation/close and disposal clear volatile candidate text and join capture cleanup. No candidate audio/text archive, diagnostic content, training, profile enablement or hosted ASR is introduced ([capture lifecycle](../src/Kora.Application/Voice/ReviewedVoiceCommandCapture.cs), [native lifetime](../src/Kora/ResponseWindow.ReviewedVoiceCommand.cs)).

The [portable behavioral cases](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.ReviewedVoiceCommand.cs), [capture lifecycle cases](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.ReviewedVoiceLifecycle.cs) and [native-control cases](../tests/Kora.Windows.IntegrationTests/PushToTalkUiContractTests.cs) use deterministic audio/runtime seams. They are implementation evidence, not live microphone, provider, ASR-profile or acoustic qualification. See the [user interaction](../docs/commands.md#review-one-spoken-command-before-dispatch).

### Approved development direction and remaining admission evidence

The [opt-in conversational reply increment](Interaction_And_Sessions.md#opt-in-exact-playback-conversational-replies) reuses the existing local question recognizer only after successful exact foreground question playback.
Its saved default-off preference does not select or qualify a speech profile, enable ambient wake or grant microphone consent. Explicit native question voice controls remain independent ([host routing](../src/Kora.Application/ViewModels/MainViewModel.QuestionVoice.cs)).

On 2026-10-24 the product owner approved local CPU sherpa-onnx keyword spotting,
initially English configured names, a narrow Windows native adapter, Silero VAD,
existing activated-only Windows ASR and conservative no-barge-in playback
exclusion. This is development direction, not deployment, capture or download
approval. Unsupported names, unknown playback and missing qualification remain
unavailable; no hidden default-name alias or hosted fallback is permitted.

Source inspection establishes only these bounded facts:

- sherpa-onnx tag `v1.13.8` resolves to
  `11afbd009a7f8c08f4bcf2fc1b265d0df4670fbf`; its
  [source licence](https://github.com/k2-fsa/sherpa-onnx/blob/v1.13.8/LICENSE)
  is Apache-2.0. The versioned NuGet pages for
  [managed bindings](https://www.nuget.org/packages/org.k2fsa.sherpa.onnx/1.13.8),
  [Windows x64](https://www.nuget.org/packages/org.k2fsa.sherpa.onnx.runtime.win-x64/1.13.8)
  and [Windows x86](https://www.nuget.org/packages/org.k2fsa.sherpa.onnx.runtime.win-x86/1.13.8)
  display Apache-2.0. Package payloads, native dependencies and hashes have not
  been admitted by this implementation.
- Silero tag `v6.2` has an
  [MIT licence](https://github.com/snakers4/silero-vad/blob/v6.2/LICENSE).
  This is not evidence for an arbitrary ONNX export or third-party redistribution;
  the exact VAD bytes and provenance still require admission.
- The historical sherpa candidate English model is documented as
  [sherpa-onnx-kws-zipformer-gigaspeech-3.3M-2024-01-01](https://k2-fsa.github.io/sherpa/onnx/kws/pretrained_models/index.html#sherpa-onnx-kws-zipformer-gigaspeech-3-3m-2024-01-01-english).
  Its exact model licence, redistribution terms and digest are **not verified**.
  Engine/package licences must not be substituted for model licensing.
- The pinned [C# keyword result](https://github.com/k2-fsa/sherpa-onnx/blob/v1.13.8/scripts/dotnet/KeywordResult.cs)
  exposes the keyword, not a timing boundary. The
  [native result](https://github.com/k2-fsa/sherpa-onnx/blob/v1.13.8/sherpa-onnx/c-api/c-api.h)
  includes token timestamps and start time, but does not establish an exclusive
  wake end. A last-token timestamp, detection callback time or arbitrary padding
  cannot supply the required boundary. Decoder population records token emission
  frames, not word/name ends; it does not qualify this operation. Synthetic
  sample-boundary tests verify the host contract, not acoustic alignment.

### Span-end implementation decision - 2026-10-28

The product owner approved a local span-end-capable wake/alignment component
after the sherpa limitation was established, preserving no compulsory pause,
no ambient general transcription and all existing host authority. The selected
implementation is CPU PocketSphinx **v5.1.1**, official commit
`511126b492dcb267cf30d49d631946d7b61a9530`, in **single-keyphrase search**, not
ambient language-model or dictation search. This supersedes sherpa as the
proposed wake producer; it does not select a new general ASR product.

The pinned [segment contract](https://github.com/cmusphinx/pocketsphinx/blob/511126b492dcb267cf30d49d631946d7b61a9530/include/pocketsphinx.h#L967)
explicitly defines inclusive first/last active segment frames.
[Keyphrase search](https://github.com/cmusphinx/pocketsphinx/blob/511126b492dcb267cf30d49d631946d7b61a9530/src/kws_search.c#L282)
populates the entire matched phrase's first/last frame from final-phone HMM
history, and its segment iterator copies that span. Reporting delay controls
when the stored span becomes observable, not its end.
The [frontend clock](https://github.com/cmusphinx/pocketsphinx/blob/511126b492dcb267cf30d49d631946d7b61a9530/src/fe/fe_interface.c#L234)
is fixed and checked at 16 kHz / 100 frames per second. A fresh decoder and
utterance receive every original mono PCM sample in each host generation:
the exclusive command start is **`checked((lastFrame + 1L) * 160)`**.
The upstream [time formatter](https://github.com/cmusphinx/pocketsphinx/blob/511126b492dcb267cf30d49d631946d7b61a9530/programs/pocketsphinx_main.c#L118)
independently uses the same inclusive-frame duration convention. No callback,
last-token start, silence endpoint, window padding or reporting-delay padding
is used as a name end. This deterministic API mapping is not a claim that a
particular acoustic model always places the boundary accurately.

The [typed C-ABI detector](../src/Kora.Windows/Audio/PocketSphinxKeywordDetector.cs)
disables the native default log sink, explicitly disables alternative searches
and dump paths, checks the effective clock, and owns config/decoder/library
lifetimes. Windows C `long` parameters are 32-bit. The
[asset binder](../src/Kora.Windows/Audio/HandsFreeNativeProfile.cs)
requires compiled host metadata, complete size/SHA-256 identities, exact
supported English names, non-reparse paths and closed model/native inventories;
it holds verified file handles until native resources have joined. Editable
JSON, installed/fake platform availability and an arbitrary model directory
cannot grant qualification. The [bundled native provenance](../src/Kora.Windows/Audio/Native/README.md)
records the frozen source-built DLL and its KERNEL32-only import closure.
The preparation catalogue pins the official en-US acoustic files, CMU dictionary,
Silero ONNX and their separate notices/size/SHA-256 identities. Native code is
delivered with Kora, never installed as an optional data download.

One [WASAPI owner](../src/Kora.Windows/Audio/HandsFreeMicrophone.cs) feeds the
bounded volatile ring and keyword detector. Subsequent discontinuities or
unknown sample timing fail closed; initial packet origin starts the fresh
generation. The [session](../src/Kora.Windows/Audio/WindowsHandsFreeAudioSession.cs)
allocates no general recognizer before a valid name span. It consumes only
post-name pre-roll and subsequent bounded command PCM into the existing
`System.Speech` recognizer/grammar helper, **without opening a second microphone**.
[Silero endpointing](../src/Kora.Windows/Audio/SileroCommandEndpoint.cs) retains
the approved v6.2 direction, pinned to
`be95df9152c0d7618fa1edfeb296fc3dae32376f` and its
[official ONNX wrapper](https://github.com/snakers4/silero-vad/blob/be95df9152c0d7618fa1edfeb296fc3dae32376f/src/silero_vad/utils_vad.py#L51):
512 samples at 16 kHz, 64 samples context, float state `[2,1,128]`, scalar
int64 sample rate. It processes command audio only, never determines the name
boundary, and ends speech after 800 ms silence or an empty activation after
four seconds. The command keeps the existing 60-second/4,096-character limits.

The host supplies a default-deny live callback for the exact consent, owner,
session/call/device/name/profile generation and no-playback admission. It is
checked before acquisition/start, on each callback and after native work.
Invalidation permanently retires the ring, clears/ends command PCM, rejects
late observations/finals, and retains ownership until microphone callbacks,
ignoring decoder/open/recognizer work, VAD and file leases have joined.
The existing host closes capture **before** dispatching the final command into
the same governed operation and readable-result route used by typed/native
input. Wake detection alone performs no action or context acquisition.

Deterministic regressions exercise original chunk origins, inclusive/exclusive
mapping, no-pause command samples, forbidden preamble/name disclosure, stale or
expired boundaries, live admission/playback revocation, overflow, one final,
single use and ignoring native work/teardown. They execute no real model,
microphone or native keyword payload. Acoustic false
activation/boundary accuracy, echo/device playback exclusion and installed
resource qualification remain E/Q, and therefore production is **not enabled**
or advertised as a completed usable hands-free feature. Exact data preparation
is delivered separately through the [shared lifecycle](Environment_Setup.md#optional-local-speech-component-lifecycle).

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

Explicit command interruption of a retained-answer reading captures the
foreground session and exact playback at PTT activation, not final recognition.
The host revalidates that immutable binding and original generation, joins
command capture, cancels only that playback and joins output before reporting
interruption. Other model work, dictation and question replies remain untouched.
`IActivatedVoiceRecognitionService.IsSpeechInterruptionAvailable` defaults to
false; the Windows adapter remains unqualified and cannot open concurrent
capture for this operation. Deterministic fakes prove this bounded implementation,
not acoustic enablement. Typed exact-session stop and native Cancel reading
remain recovery routes; unknown or stale targets never select current output.

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

### Editor-targeted text admission

**Bounded activated-command implementation, not speech-profile qualification.** The
[foreground editor contract](Definition_Management.md#foreground-editor-voice-targeting)
allows the host to snapshot one eligible foreground Customizations editor at
admitted wake/PTT activation. The capture generation and original voice channel
stay bound to that instance/source/draft/component and host admission. Final
recognition or UI dispatch must not observe a different foreground window and
retarget the utterance. A background editor is not an implicit speech target.

This snapshot supplies routing context only. Local, tool-free interpretation
requires its own admitted, reviewed text/minimal-metadata disclosure; authoring
requires a separate complete draft/request disclosure and proposal review.
Neither wake detection, focus nor transcription grants those authorities.
Existing question/reply and literal-dictation capture retain exclusive ownership.
This adds no ambient transcription, ASR rewriting, hosted fallback or speech
profile qualification.

Ambiguous targeting asks one bounded addressed question. Retired, rejected or
unavailable editor input cannot become a session request or answer another
question; the [session routing exception](Interaction_And_Sessions.md#editor-targeting-precedence)
excludes automatic New session fallback. Focus/source/draft/capture or host
admission changes retire the snapshot, including late final results, without
revival on reacquisition. Host-owned modal handoffs preserve the original
binding rather than following arbitrary focus. Clarification and review use
native choices; this adds no spoken choice-answer capture or automatic session
handoff. Native text and existing exact commands remain recovery routes when
the selected speech/model capability is unavailable.

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

The bounded [native/typed/activated review](../src/Kora.Application/ViewModels/MainViewModel.SpeechInput.cs) completes an explicit switch or recovery for the existing Windows activated tuple. It also composes exact configured-name selection for `pocketsphinx-5-1-1-en-us-cpu-v1 Cpu CaptureLifetime` when the trusted Windows platform independently qualifies that name/profile. Review displays saved/desired/effective state, the immutable proposed tuple, configured name and required local capabilities; confirmation retains the admitted original request/channel through the [joined cutover](../src/Kora.Application/Configuration/SpeechInputConfigurationService.cs).

Cancellation or stale review does not replace saved intent. A pre-write retirement failure keeps the prior desired selection while withholding effective capture; uncertain persistence explicitly withholds saved-state confirmation. The [shared lifecycle](../src/Kora.Application/Voice/HandsFreeVoiceRecognitionService.Profiles.cs) excludes wake, PTT and reply capture throughout the exclusive cutover lease.

The [shared lifecycle](../src/Kora.Application/Voice/HandsFreeVoiceRecognitionService.Profiles.cs) physically joins the old keyword/capture/ASR generation before acquiring verified asset leases for the proposed profile. Unknown release quarantines ownership and blocks replacement. Rename still turns listening off; profile confirmation neither grants listening nor automatically rearms it. Unsupported names keep desired intent visible and effective wake unavailable, never retaining the old name or substituting Kora. The default Kora capture route is preserved; custom wake names require explicit native-profile selection.

The [actual native consumer](../src/Kora.Windows/Audio/PocketSphinxKeywordDetector.cs) receives a [bounded literal keyword](../src/Kora.Windows/Audio/PocketSphinxKeywordConfiguration.cs) derived from exact entries in the installed, hash-bound CMU dictionary. Strict UTF-8 and byte/entry/token bounds reject malformed or ambiguous mappings. There is no generated phonetics, network lookup, arbitrary keyword-file syntax or dictionary mutation. Native keyword and clock readback must match before capture. A [separately typed host wake admission](../src/Kora.Application/Voice/HandsFreeCommandActivation.cs) preserves the original request and fresh name/profile/capture generations; final recognized command text is unchanged, not reconstructed with a synthetic name prefix. The admitted span's immediate command reaches existing local ASR and command routing. Privacy-reducing Stop listening precedence is retained, including the supported name `Stop`.

Production native profiles still have **no qualified names** in the [platform catalogue](../src/Kora.Windows/Audio/HandsFreeNativeProfileCatalogue.cs). Installed assets, accepted keywords and deterministic [composed native API/resource fakes](../tests/Kora.Windows.IntegrationTests/Audio/CustomNameWakeCutoverTests.cs) do not establish acoustic, quality or playback qualification. The complete selection/cutover/native-configuration/span/ASR/command path is implemented (I); production availability remains independently gated (E/Q). Other wake/ASR/backend/residency choices remain unavailable. Unsupported legacy intent is retained until an explicit compatible reviewed switch. No download, automatic listening enablement, hosted audio, biometric enrollment or qualification claim is added.

## Optional Installation and Runtime Lifecycle

Additional wake, VAD, ASR and optional verifier model assets must support both
explicit install-time preparation and later Kora Settings setup, like the
existing optional Ollama/Kokoro flows. This is a requirement for future
qualified components, not a claim that today's installer offers them.
[Environment Setup](Environment_Setup.md#optional-local-speech-component-lifecycle)
owns the shared catalogue, detection, consent, readiness and removal workflow;
[Distribution](Distribution_And_Updates.md#selected-windows-installer-direction)
owns WiX/Burn integration and launch prerequisites.

The bounded [shared lifecycle implementation](../src/Kora.Application/Dependencies/SpeechComponentSetupService.cs)
now connects [Settings](../src/Kora/SettingsWindow.SpeechComponents.cs) and
[Burn preparation](../installer/Kora.Setup/SpeechComponentInstallChoice.cs).
The [production input-data catalogue](../src/Kora.Core/Dependencies/SpeechComponentCatalogue.cs)
is deliberately empty until an authoritative exact profile is selected and
qualified. Both surfaces report no offers; deterministic fixture preparation
does not qualify a production backend or advertise an installed model.

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
