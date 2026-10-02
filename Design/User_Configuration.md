# User Configuration and Verbal Settings

Status: proposed. Every supported user preference must be discoverable, inspected, and set verbally once local voice input is ready.

Related: [OOTB Phrases](OOTB_Phrases.md), [Environment Setup](Environment_Setup.md), [Call-Aware Speech](Call_Aware_Speech.md), [Security](Security_Data_Flows.md).

## Configuration Contract

Voice and settings UI use the same typed host configuration service.
There are no UI-only preferences or hidden configuration-file edits required for normal use.
An extension cannot introduce a settings screen without registering equivalent verbal discovery/get/set/reset operations.

Each registered option declares:

- Stable ID/category, description, spoken names/aliases, type, units, allowed values/range, and default.
- Scope: device, conversation/task, source/connector, or bounded temporary override.
- Availability/dependencies, validation, sensitivity, confirmation rule, and application timing.
- Whether it can be reset, and the changes affected by reset.

The model may interpret a request into an option/value proposal; the host validates it against the registry.
It cannot supply a configuration-file path, arbitrary JSON patch, shell command, executable reference, or undocumented option.
Exact basic settings commands remain usable without a model/network.
Changing a supported preference is not permission to alter policy implementation, install software, grant tools, or execute an action.

## Voice Interaction

Examples after "Kora":

- "What settings can I change?"
- "What are my speech settings?"
- "What is my queue limit?"
- "Set the queue limit to twenty."
- "Speak a little slower."
- "Keep that setting just for this conversation."
- "Make that my default."
- "Reset speech settings to defaults."
- "Undo the last settings change."

Resolve the option, value, units, target, and duration. Ask if any is ambiguous.
Read back material changes or show the exact proposal when speech is gated.
Apply immediately only when validation and the applicable confirmation succeed.
Report both the saved preference and effective value when capability/policy limits differ.
Never silently clamp an invalid value or use a success-shaped fallback.

Relative changes such as "slower" resolve to a displayed concrete value within the engine's advertised range.
Changes that disable speech are acknowledged visually under the resulting policy.
Use device-local persistence by default; explicit task/conversation/temporary scope does not change the default.
Do not roam preferences, grants, device IDs, or credentials with skill files.
Persisted preferences do not persist listening consent, manual call state, or temporary overrides across lock/restart.

## 1. Voice Input and Activation

| Option | Default / limits | Example verbal setter |
|---|---|---|
| Input device | No automatic first-run selection/capture; suggest system default and select an enumerated endpoint explicitly | "Use my headset microphone" |
| Listening enabled | Explicit consent and re-enabling required under lifecycle policy | "Stop listening" |
| Activation name and alias mode | "Kora" initially; validated custom name with explicit custom-only or Kora-and-custom choice | "Change your activation name to Nova" |
| Optional PTT shortcut | Unassigned until selected; validate conflicts | "Set push-to-talk to Control Shift Space" |
| Activation feedback | Visual always; non-speech cue when output policy permits | "Turn off the activation sound" |
| Command speech-start wait | 5 seconds; 2-10 seconds | "Wait seven seconds after I say Kora" |
| Trailing-silence endpoint | 1 second; 0.5-3 seconds | "Allow two seconds of silence before finishing my command" |
| Maximum utterance | 60 seconds; 10-120 seconds | "Limit commands to forty-five seconds" |
| Recognition language/model | Initial supported local English configuration; choose only delivered/ready assets | "Use the more accurate installed recognition model" |
| Owner-aware private speech | On when an enrolled supported verifier is available; private content is visual-only for `Uncertain`, `NotOwner`, or `Unavailable` | "Only read private information when you recognise my voice" |

The default activation name is "Kora"; [Custom Activation Names](Activation_Name.md) defines the rename flow, its explicit custom-only/both choice, local detector requirements, and atomic application.
Device switching revalidates consent, capture, and playback-rejection safety.
First-run and device-loss microphone selection always work by mouse through [Interaction Fallback](Interaction_Fallback.md); hot-plug/default changes do not silently switch or reopen capture.
The persistent system tray context menu lists detected microphones and provides Choose/test microphone and explicit Enable listening; selecting a replacement alone does not start recording.
"Start listening" can set the preference only through an already available explicit input channel; a closed microphone cannot receive the utterance.
Do not keep a secret listening path merely to support voice unmute.
Speaker enrollment, replacement, deletion, threshold policy, and any relaxation of owner-aware private speech are security-sensitive native workflows, not ordinary verbally settable preferences.
Voice may open the relevant settings page or report non-sensitive enrollment availability, but Windows Hello or equivalent native reauthentication and visual confirmation complete the change.

## 2. Spoken and Visual Responses

| Option | Default / limits | Example verbal setter |
|---|---|---|
| Response mode | Hybrid; voice-only or visual-only supported | "Use visual responses only" |
| Output device | Selected system-default device initially; enumerated outputs | "Speak through my headphones" |
| Local voice | Supported installed voice; do not silently download on selection | "Use the second installed voice" |
| Speech rate | Engine normal; advertised supported range | "Speak twenty percent slower" |
| Kora playback volume | Normal configured level; 0-100%, affects only Kora | "Set your volume to thirty percent" |
| Spoken summary length | At most 3 sentences/80 words; user may lower either limit | "Keep spoken summaries under forty words" |
| Visual detail level | Concise by default; detailed on request | "Show detailed results by default" |
| Speech text / rich display | Independent optional captions, source/rendered Markdown, diagram and viewer preferences | "Show the words you're saying" |
| Theme | Follow system; light/dark/system | "Use the dark theme" |
| Presence placement | Bottom-right working area; validated display/corner/margin | "Put your presence in the top-right of my second monitor" |
| Reduced motion | Follow system; may enable explicitly | "Use reduced motion" |

Visual-only does not close the microphone. Output-device changes never change global system/call volume.
Voice-only still preserves required visual approval/error/fallback surfaces; it cannot suppress safety information.
Unsupported voices/languages/display IDs produce an explicit available-choice response.
Exploratory animation styles and native visual features are configurable only once implemented and capability-tested.
The exact caption, rich rendering, viewer, and text-scale options are defined in [Information Display](Information_Display.md#voice-settings-and-navigation); they use this same configuration contract.

## 3. Calls and Proactive Interaction

| Option | Default / limits | Example verbal setter |
|---|---|---|
| Call speech mode | Suppress automatic requested/proactive speech; one-shot override allowed | "Only suppress unsolicited suggestions during calls" |
| Unknown enabled-detector behaviour | Suppress automatic speech | "Use normal speech when detection is unavailable" |
| Detector enablement/account | Explicitly configured supported sources only | "Use Teams presence to decide when to stay quiet" |
| Busy/DND/meeting quieting | Call/Suspected meeting handling as in call policy; additional Busy/DND preference off | "Stay quiet when Teams says do not disturb" |
| Temporary call override | Up to 1 hour or observed call end; never persistent implicitly | "Allow spoken answers for this call for fifteen minutes" |
| Proactive speech consent | Explained and chosen in setup | "You can make suggestions again" |
| Quiet hours | None until chosen; local time/time zone shown | "Don't interrupt me between nine and five on weekdays" |
| Quiet mode | Off normally; conversation or persistent scope explicit | "Use quiet mode for this conversation" |
| Notification categories | Eligible task/setup/update events; categories individually configurable | "Tell me about task failures but not completed tasks" |
| Reminder deferral | 24 hours; 1 hour-7 days | "Remind me about updates tomorrow" |

Detectors need explicit setup/account/network consent; setting a preference does not create Graph credentials.
Hard lock/mute rules always outrank call/proactive preferences.
Notification settings cannot hide necessary action approval from the visual interface or turn silence into approval.

## 4. Work and Context

| Option | Default / limits | Example verbal setter |
|---|---|---|
| Pending queue capacity | 10; 1-50 entries | "Set the queue limit to twenty" |
| Successful-completion dispatch | Automatic next ready item; manual mode optional | "Ask before starting each queued task" |
| Pending request lifetime | 30 minutes; 1-120 minutes | "Keep queued requests for an hour" |
| Active-task deadline | 5 minutes excluding user waits; 1-60 minutes within tool/provider limits | "Give tasks ten minutes by default" |
| Conversation idle expiry | 30 minutes; may reduce to 1-30 minutes | "Clear idle context after ten minutes" |
| Clipboard text limit | 256 KiB maximum; may lower | "Limit clipboard snapshots to sixty-four kilobytes" |
| Model-bound tool-result limit | 64 KiB maximum; may lower | "Limit tool excerpts to thirty-two kilobytes" |

Lowering capacity never evicts existing work; hold admissions until occupancy fits.
Shortening lifetime/expiry shows affected requests/content and requires confirmation before expiring them immediately.
Extending a default deadline affects future tasks; changing the current task requires explicit task scope and revalidation.
Manual dispatch is an additional admission decision, never a substitute for tool approval.
Executor count remains one; it is not a configurable parallelism option in the MVP.
Fixed approval lifetimes, process cancellation grace, power confirmations, secret blocking, and bounded raw-audio retention are safety controls, not arbitrary user knobs.

## 5. Providers, Local Models, and Connections

| Option | Default / limits | Example verbal setter |
|---|---|---|
| Processing mode | Selected explicitly in setup; local-only or remote-enabled | "Use local-only processing" |
| Default runtime/provider | Compatible installed/configured choices | "Use my local model by default" |
| Local model | Verified installed compatible model | "Use the smaller installed model" |
| Ollama endpoint | Validated local endpoint by default | "Use my existing Ollama endpoint" |
| Account/connector selection | Explicit supported signed-in identity | "Use my work account for Teams detection" |
| Connector enablement | Only registered/validated connector | "Disable the GitHub connector" |

Changes increasing remote exposure require an exact named voice confirmation such as "confirm remote-enabled processing".
That confirms the setting only; each new outgoing payload still follows egress policy.
Remote endpoints require a validated destination and setup/network decision, not guessing or silent fallback.
Provider changes do not transplant active task context or continue a task under another account automatically.
Credentials/tokens/passwords are not dictatable option values; voice starts supported sign-in/sign-out/configuration flows.

## 6. Skills and Local Data

| Option | Default / limits | Example verbal setter |
|---|---|---|
| Shared skill sources | User-selected bounded read-only roots | "Use skills from my Copilot profile" |
| Skill enablement/default source binding | Reviewed digest/device-local binding | "Use the Kora-specific deployment skill when I say deployment" |
| Source refresh preference | Revalidate before dispatch; optional bounded discovery refresh | "Refresh shared skill discovery every hour" |
| Audit retention | 30 days maximum; may reduce to 1-30 days | "Keep audit metadata for seven days" |
| Diagnostic verbosity | Content-minimising normal; bounded metadata-only detail | "Use detailed diagnostics for this session" |

Paths can be spoken or taken from explicitly selected clipboard text, then resolved/read back and validated.
Do not require typing a path, but do not infer one from unrelated context.
The Roaming AppData skill store and protected installation/source layout are architectural boundaries, not voice-selectable arbitrary write roots.
Raw audio/conversation persistence, silent remote diagnostic upload, automatic executable imports, and secret logging remain unsupported.
Reducing audit retention or removing source enablement explains any immediate destructive/invalidation effect before confirmation.

## 7. Startup and Updates

| Option | Default / limits | Example verbal setter |
|---|---|---|
| Start at logon | Explicit opt-in | "Enable start at logon" |
| Startup presentation | Tray/minimal shell; details optional | "Open the details view when you start" |
| Automatic update checking | Enabled only for configured/permitted maintenance networking | "Only check for updates when I ask" |
| Update-check interval | 6 hours; 30 minutes-24 hours with backoff/jitter | "Check for updates twice a day" |
| Release channel | Stable; only trusted published channels | "Use the preview release channel" |
| Update reminder preference | Per-release reminder/suppression through proactive policy | "Don't remind me about this version" |

Update installation always requires per-release approval; "automatic install" is not an available setting.
Changing channels does not authorise a downgrade, skip verification, or rewrite the protected feed/trust configuration.
Logon startup does not waive explicit microphone re-enabling or locked-session policy.

## Applying, Resetting, and Undoing

Validate configuration and capability dependencies, stage a typed change, and save atomically through the host store.
On failure, retain the prior valid value and report the failed operation.
Dependent changes show the complete proposal; do not install/download software merely to make a setting appear applied.
Revalidate settings changes against a configuration revision so concurrent voice/UI edits cannot overwrite each other silently.

Reset can target one option or a category. "Reset all settings" requires explicit named confirmation and shows affected registrations and existing grants that must be invalidated.
Reset never deletes skills, databases, or credentials as an implicit side effect.
Undo restores the previous compatible preference value, not a side effect such as reinstalling software or resurrecting a consumed approval.
Reset/undo use normal validation, confirmation, and policy checks; restoring a weaker privacy preference is not exempt from confirmation.
If an external registration/action changed, report that a separate compensating action requires approval.
Never treat undo as reinstating expired grants or microphone consent.

## Unavoidable Boundaries

All supported preference values have verbal operations while Kora can hear the user.
Closed microphone, locked session, missing recogniser, and signed-out OS prevent receiving speech; an explicit physical/visual activation/setup path is necessary.
This is a physical/security constraint, not a reason to omit voice setters from ready capabilities.
OS sign-in/OAuth/credential entry and mandatory elevated/destructive operation approvals remain separate workflows, not user-option values dictated to the model.
No option can enable self-modification, disable mandatory lock policy, bypass executable isolation/egress/action approvals, or change "Kora" into a hidden always-transcribing microphone.
