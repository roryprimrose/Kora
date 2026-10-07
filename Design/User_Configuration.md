# User Configuration and Verbal Settings

Status: partial. The bounded appearance subset below is delivered through a
shared typed UI/exact local command path; the complete verbal preference and
model-facing contract remains proposed, subject to protected-call origin gates
and mandatory secure workflows.

Related: [OOTB Phrases](OOTB_Phrases.md), [Environment Setup](Environment_Setup.md), [Call-Aware Speech](Call_Aware_Speech.md), [Security](Security_Data_Flows.md), [Interaction and Sessions](Interaction_And_Sessions.md).

## Configuration Contract

### Delivered bounded appearance subset (R10)

Nine existing independently persisted options are admitted:
`appearance.theme`, `appearance.presence-timeout`,
`appearance.response-timeout`, `appearance.presence-size`,
`appearance.dot-size`, `appearance.dot-density`,
`appearance.movement-speed`, `appearance.speech-scaling` and
`appearance.speech-scale-amount`. The [end-user reference](../docs/settings.md#appearance)
lists their exact types, units, defaults and bounds. Each descriptor declares
device-local scope, appearance-only effect, local-host availability,
immediate-after-save timing and per-option reset. Animation driven by playback
is not a voice-output or call-sensitive option.

The host-owned [registry](../src/Kora.Core/Configuration/AppearanceOptionRegistry.cs)
reuses existing domain validation; the
[service](../src/Kora.Application/Configuration/AppearanceConfigurationService.cs)
owns get/propose/apply/reset, a process-local revision, proposal provenance,
serialized revalidation/write/notification and typed audit outcomes.
Revision and proposal identity are not durable cross-process authority.
The instance-owner boundary admits the local host. No arbitrary option,
path, JSON patch, external configuration file or model proposal is accepted.
Saved-format parsing stays in the preference domain; malformed saved state
still throws `InvalidDataException`. Missing preferences use declared defaults
without claiming those defaults were written.

Direct appearance controls and exact deterministic discovery/get/set/reset
use the same service and notify all open surfaces. Cancellation is checked
before the existing synchronous atomic write; cancellation after commit does
not report an unsaved change. Save failure retains the previous value/revision
and produces visible failure; stale proposals require a new operation.
Reset restores one admitted default; no multi-file transaction, whole-profile
reset or undo is implemented. Shared response-window pin/topmost/position
and presence placement remain direct UX outside the registry.
Voice/audio/call/manual state, grants, models, retention, dependencies and
startup are not registered. No model tools or broader execution authority
are exposed by this slice. The following complete contract remains future work.

Voice and settings UI use the same typed host configuration service.
There are no UI-only preferences or hidden configuration-file edits required for normal use.
An extension cannot introduce a settings screen without registering equivalent verbal discovery/get/set/reset operations.
Common unambiguous settings operations also register exact deterministic
commands, while the model receives typed discovery/get/set/reset actions for
natural-language interpretation. Both routes call the same host configuration
operation and policy gate; model identification is never execution authority.
Any setting that cannot safely support one of these routes documents the
specific interaction, ambiguity, availability, privacy, or origin constraint
in the capability catalogue.

The desktop system-tray menu exposes a single-instance Settings window with
every setting currently implemented by the host. All open settings surfaces
observe the same live state: mouse and validated verbal mutations publish the
same change notifications, so neither surface requires reopening or polling to
show the new effective value.

Bounded R15 now supplies run-scoped manual call controls and truthful automatic
availability in **Settings > Calls**, outside the future generic settings/tool
registry. Original voice provenance, observed call revision and live ownership/
privacy gate manual changes. Protected calls reject voice-originated voice and
in-call option writes, including reset/clear semantics; read-only inspection and
stop/cancel remain eligible. Saved output/activation preferences are retained.
New call-protection downgrades and temporary/speak-once exceptions remain
unavailable pending complete exact trusted review; they are not exposed as
working verbal/model operations. See the [bounded R15 receipt](Call_Aware_Speech.md#delivered-bounded-manual-mode---2026-10-07).

Each registered option declares:

- Stable ID/category, description, spoken names/aliases, type, units, allowed values/range, and default.
- Scope: device, conversation/task, source/connector, or bounded temporary override.
- Availability/dependencies, validation, sensitivity, confirmation rule, and application timing.
- Whether it can be reset, and the changes affected by reset.
- Host-assigned voice/in-call effect classification, including whether the protected-call initiating-channel restriction applies.

The model may interpret a request into an option/value proposal; the host validates it against the registry.
It cannot supply a configuration-file path, arbitrary JSON patch, shell command, executable reference, or undocumented option.
Exact basic settings commands remain usable without a model/network.
Changing a supported preference is not permission to alter policy implementation, install software, grant tools, or execute an action.
Model-facing discovery/get/change/reset/undo operations are inventoried in [Internal Model Tools](Internal_Model_Tools.md#grants-settings-and-evidence).
The [in-call settings origin gate](Call_Aware_Speech.md#in-call-settings-origin-gate) rejects voice-initiated changes to voice settings and every in-call-related option while protected call evidence applies.
This includes reset/undo, detector/manual-state changes, grant-ignore and temporary/speak-once call overrides; a new UI-originated request is required, not UI confirmation of the rejected voice request.
Deterministic safety controls and read-only inspection remain available; this is an explicit exception to ordinary verbal-setting availability.

## Voice Interaction

Examples after the currently configured assistant name (initially "Kora"):

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
Persisted preferences include the explicit ongoing voice-consent choice, not
live capture or run-scoped recovery holds. Manual listening disablement, manual
call state and temporary overrides do not persist across ordinary restart;
consent withdrawal does. See the
[microphone matrix](Security_Data_Flows.md#microphone-consent-and-enablement-matrix).

## 1. Voice Input and Activation

| Option | Default / limits | Example verbal setter |
|---|---|---|
| Input device | System by default and follows live Windows multimedia-default changes; a specific endpoint-ID override remains pinned until changed back to System; changing selection after manual disablement does not restart capture | "Use my headset microphone" |
| Listening enabled | Closed until explicit first-launch ongoing consent; saved consent permits automatic ordinary safe startup; manual disablement/recovery holds release capture for the current run | "Stop listening" |
| Assistant display and command name | "Kora" initially; one validated custom-only name of 1-3 words and at most 32 characters | "Change your name to Nova" |
| Optional PTT shortcut | Unassigned until selected; validate conflicts | "Set push-to-talk to Control Shift Space" |
| Activation feedback | Visual always; non-speech cue when output policy permits | "Turn off the activation sound" |
| Command speech-start wait | 5 seconds; 2-10 seconds | "Wait seven seconds after I say Kora" |
| Conversational replies | On after ongoing listening consent; allows prefix-free answers only to the unique question Kora just presented | "Require me to say Kora for every answer" |
| Conversational reply speech-start wait | 15 seconds; 5-60 seconds; expiry returns to wake listening without dismissing the question | "Wait twenty seconds for my answer" |
| Trailing-silence endpoint | 1 second; 0.5-3 seconds | "Allow two seconds of silence before finishing my command" |
| Maximum utterance | 60 seconds; 10-120 seconds | "Limit commands to forty-five seconds" |
| Recognition language/model | Initial supported local English configuration; choose only delivered/ready assets | "Use the more accurate installed recognition model" |
| Learn my voice (`voice.learnFrequentSpeaker`) | Off until separate explained consent; local Windows-profile/device-scoped adaptation from new activated commands only; non-authorizing; status/test/correct/reset/delete workflows | "Learn my voice to improve recognition" |
| Owner-aware private speech | Optional; On for an explicitly enrolled supported verifier, not baseline voice or learning; while enabled, private content is visual-only for `Uncertain`, `NotOwner`, or `Unavailable`, without silently switching protection Off | "Only read private information when you recognise my voice" |

The default assistant name is "Kora"; [Assistant and Activation Name](Activation_Name.md)
defines the implemented display/command identity, its device-local persistence,
and the stronger detector requirements that remain future work. The current
name is custom-only: after commit, the previous name is not retained as a
hidden alias. The executable, application-data roots, assemblies, log names,
and trust identity remain Kora.
Device switching revalidates session, capture, and playback-rejection safety.
First run selects System without recording and obtains explicit ongoing voice
consent before enabling listening. Later ordinary startup may automatically
enable after saved consent and fresh gates under the
[microphone matrix](Security_Data_Flows.md#microphone-consent-and-enablement-matrix).
System uses Windows default-device stream routing during enabled capture, so later
multimedia-default changes are applied without creating a Kora endpoint override
or reopening capture. Device-loss
replacement always works by mouse through
[Interaction Fallback](Interaction_Fallback.md); hot-plug/default changes do
not silently replace a specifically selected endpoint. The persistent system tray
context menu lists detected microphones and provides Disable listening while
capture is active plus Enable listening for recovery after manual disablement or
failure; selecting a replacement alone does not restart recording.
Unlock/resume, restored permissions, reconnection, asset repair and selection
changes never release a current-run recovery hold without explicit Enable
listening. Without owner-aware protection selected, normal privacy/output/call
policy governs baseline speech; unavailable verification alone does not mute it.
"Start listening" can set the preference only through an already available explicit input channel; a closed microphone cannot receive the utterance.
Do not keep a secret listening path merely to support voice unmute.
Speaker enrollment, replacement, deletion, and verifier threshold policy remain protected biometric workflows with Windows Hello or equivalent OS reauthentication.
That requirement applies to explicitly enrolled verification, not merely optional non-authorizing frequent-speaker learning.
Learning consent/status/reset/deletion follow [Optional Local Frequent-Speaker Learning](Security_Data_Flows.md#optional-local-frequent-speaker-learning); Off stops learning but is not an implicit deletion of derived features.
Voice can initiate verification enrollment but cannot bypass its secure flow. Changes to owner-aware output/approval preferences use explicit exact voice/UI confirmation, not automatic template adaptation.
All voice-profile settings and management changes obey the protected-call origin gate; status inspection remains available.

## 2. Spoken and Visual Responses

| Option | Default / limits | Example verbal setter |
|---|---|---|
| Response mode | Hybrid device default; explicit session and transient task/queue overrides; applicable in-call > task > queue > session > device, subject to mandatory policy and explicit permitted speak-once exception | "Use visual responses only for this session" |
| Output device | System by default and follows live Windows multimedia-default changes; a specific endpoint-ID override remains pinned until changed back to System; stale/missing/unselected/muted/zero-volume/open/playback failure forces visual fallback | "Speak through my headphones" |
| Local voice | Supported installed voice; do not silently download on selection | "Use the second installed voice" |
| Speech rate | Engine normal; advertised supported range | "Speak twenty percent slower" |
| Kora playback volume | Normal configured level; 0-100%, affects only Kora | "Set your volume to thirty percent" |
| Spoken summary length | At most 3 sentences/80 words; user may lower either limit | "Keep spoken summaries under forty words" |
| Detail presentation | Offer by default / Open automatically / Link only; applies to host-classified detail-recommended finalized foreground responses | "Always open detailed results" / "Stop asking about details" |
| Speech text / rich display | Independent optional captions, source/rendered Markdown, diagram and viewer preferences | "Show the words you're saying" |
| Theme | System by default; System follows live Windows appearance, while Light/Dark override every Kora visual surface | "Use the dark theme" |
| Presence timeout | 10 seconds; 1-60 seconds; reset by Kora interaction; automatically hides idle/listening presence without prompts, never work, speech, or required attention | "Hide your presence after ten seconds without interaction" |
| Response timeout | 5 seconds; 1-60 seconds; independent device-local setting for unpinned response-window inactivity; pending prompts/actions retain the response | "Hide your response after fifteen seconds without interaction" |
| Presence size | 360 px; 240-600 px; applies immediately and preserves bottom-right anchoring | "Make your presence 400 pixels wide" |
| Presence dot size | 100%; 50-200%; changes particle diameter without changing particle count | "Make the presence dots 120 percent" |
| Presence movement speed | 100%; 25-200%; scales state-driven particle movement | "Set presence movement speed to 75 percent" |
| Presence placement | Bottom-right working area; validated display/corner/margin | "Put your presence in the top-right of my second monitor" |
| Reduced motion | Follow system; may enable explicitly | "Use reduced motion" |

Visual-only does not close the microphone. Output-device changes never change global system/call volume.
Voice-only still preserves required visual approval/error/fallback surfaces; it cannot suppress safety information.
When speech output is missing, temporarily unavailable, or fails during playback, the response text and window are forced visible regardless of the effective response mode.
Task and queue response-mode overrides expire with their execution scope; an explicitly chosen session mode is persisted with that session until reset/deletion. The device default changes only explicitly.
Specific microphone and output selections are device-local Kora overrides. A
saved override wins on later starts while that stable endpoint exists. Missing
saved or current explicit endpoints are not replaced silently; the user must
select a replacement or System. Selecting System clears the relevant override
immediately and follows the current and subsequent Windows multimedia defaults.
Endpoint presence, active state, Windows software mute/zero volume, open errors, and playback errors are detectable.
Software mute forces visual output; Kora never changes global mute or volume automatically.
Physical audibility beyond Windows (powered-off speakers, disconnected analog paths, unreported hardware mute/volume) is not reliably detectable and is covered by explicit preview/recovery UX.
Unsupported voices/languages/display IDs produce an explicit available-choice response.
Exploratory animation styles and native visual features are configurable only once implemented and capability-tested.
The exact caption, rich rendering, viewer, and text-scale options are defined in [Information Display](Information_Display.md#voice-settings-and-navigation); they use this same configuration contract.

## 3. Calls and Proactive Interaction

| Option | Default / limits | Example verbal setter |
|---|---|---|
| Call speech mode | Suppress automatic requested/proactive speech; one-shot override allowed | "Only suppress unsolicited suggestions during calls" |
| In-call feedback override | UI-only by default; Voice / UI / Both / Inherit; Active/Suspected observations use this separate device-local setting ahead of task/queue/session/device response mode, subject to speech/privacy policy | "Use UI responses only when I'm in a call" |
| Ignore reusable grants during calls (`calls.ignoreReusableGrants`) | On by default; Boolean device-local setting; require fresh single-use approval instead of Session/Perpetual reuse while call protection applies; Off requires exact protection-downgrade confirmation | "Ignore saved grants while I'm in a call" |
| Voice activation during calls | On; independently configurable and does not reopen capture without explicit listening consent | "Disable voice activation during calls" |
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
The in-call feedback override and voice-activation setting are device-local and independent: UI-only output does not close the microphone, while disabling call-time voice activation closes active capture and blocks re-enabling it until the call clears.
Inherit restores normal task/queue/session/device feedback precedence; changing the feedback mode does not silently relax separate call speech suppression/privacy rules.
The grant-ignore setting is independent of feedback/listening/speech suppression and does not alter stored grants or their retention.
Its evidence, dispatch-race, and approval rules are defined in [Ignoring Reusable Grants During Calls](Call_Aware_Speech.md#ignoring-reusable-grants-during-calls).
When no detector is configured, Kora reports automatic call detection as unavailable and preserves the ordinary response and listening configuration.
Hard lock/mute rules always outrank call/proactive preferences.
Notification settings cannot hide necessary action approval from the visual interface or turn silence into approval.

## 4. Work and Context

| Option | Default / limits | Example verbal setter |
|---|---|---|
| Pending queue capacity | 10; 1-50 entries | "Set the queue limit to twenty" |
| Successful-completion dispatch | Automatic next ready item; manual mode optional | "Ask before starting each queued task" |
| Pending request lifetime | 30 minutes; 1-120 minutes | "Keep queued requests for an hour" |
| Active-task deadline | 5 minutes excluding user waits; 1-60 minutes within tool/provider limits | "Give tasks ten minutes by default" |
| Session automatic archive inactivity | 24 hours; configurable finite positive duration | "Mark sessions done after two idle days" |
| Session automatic deletion inactivity | 30 days from last meaningful activity; configurable and later than archive | "Delete sessions after sixty idle days" |
| Concurrent session task limit | Proposed default 2; positive limit within verified provider/hardware envelope; one task per session | "Run up to three sessions at once" |
| Model-assisted session routing | On where verified; explicit targeting wins, clear Active match continues, otherwise new session/clarification | "Turn off automatic related-session matching" |
| Owner-voice approval preference | Optional additional confidence; Off by default; available only after verifier proof/enrollment | "Require voice matching for spoken high-risk approvals" |
| Clipboard text limit | 256 KiB maximum; may lower | "Limit clipboard snapshots to sixty-four kilobytes" |
| Model-bound tool-result limit | 64 KiB maximum; may lower | "Limit tool excerpts to thirty-two kilobytes" |

Lowering capacity never evicts existing work; hold admissions until occupancy fits.
Shortening lifetime/expiry shows affected requests/content and requires confirmation before expiring them immediately.
Archive and deletion are independent configurable values using one inactivity clock, not time since creation/Done.
Passive selection/history queries do not extend it; actual user/work activity and explicit resume do.
Changing retention previews resulting due dates and requires a separate apply-now decision for immediate archive/deletion; otherwise existing due dates remain until subsequent meaningful activity, with new sessions using the new policy.
Normal automatic expiry under the disclosed policy does not require repeated per-session confirmation.
Lowering concurrency affects future admissions, not active-task cancellation; unknown resource effects still require exclusive coordination.
Extending a default deadline affects future tasks; changing the current task requires explicit task scope and revalidation.
Manual dispatch is an additional admission decision, never a substitute for tool approval.
Concurrency is bounded by verified provider/hardware capability and host resource coordination, never a permission to share context/grants or race writes.
Fixed approval lifetimes, process cancellation grace, power confirmations, secret blocking, and bounded raw-audio retention are safety controls, not arbitrary user knobs.

## 5. Providers, Local Models, and Connections

| Option | Default / limits | Example verbal setter |
|---|---|---|
| Local model execution | On; device-local; disabling cancels active local inference while deterministic built-ins remain available | "Disable local models" |
| Hosted model execution | Off until explicitly enabled; still requires a configured supported provider and credentials | "Enable hosted models" |
| Processing mode | Selected explicitly in setup; local-only or remote-enabled | "Use local-only processing" |
| Default runtime/provider | Compatible installed/configured choices | "Use my local model by default" |
| Local model | Verified installed compatible model | "Use the smaller installed model" |
| Ollama endpoint | Validated local endpoint by default | "Use my existing Ollama endpoint" |
| Account/connector selection | Explicit supported signed-in identity | "Use my work account for Teams detection" |
| Connector enablement | Only registered/validated connector | "Disable the GitHub connector" |

Changes increasing remote exposure require deliberate exact voice or UI confirmation in the unlocked session, with any explicitly selected speaker protection and mandatory OS/provider checks.
That confirms the setting only; each new outgoing payload still follows exact egress approval and the selected speaker/privacy and mandatory OS/provider requirements.
Remote endpoints require a validated destination and setup/network decision, not guessing or silent fallback.
Provider changes do not transplant active task context or continue a task under another account automatically.
Credentials/tokens/passwords are not dictatable option values; voice starts supported sign-in/sign-out/configuration flows.

## 6. Skills and Local Data

| Option | Default / limits | Example verbal setter |
|---|---|---|
| Shared skill sources | User-selected bounded read-only roots | "Use skills from my Copilot profile" |
| Skill enablement/default source binding | Reviewed digest/device-local binding | "Use the Kora-specific deployment skill when I say deployment" |
| Source refresh preference | Revalidate before dispatch; optional bounded discovery refresh | "Refresh shared skill discovery every hour" |
| Knowledge source registration | None by default; explicit reviewed file/folder and session/managed scope | "Add my Team TSGs folder as a knowledge source" |
| Knowledge source refresh | Manual initially; later bounded scheduled refresh only after R26 background-work gates | "Refresh Team TSGs" |
| Knowledge retrieval strategy | Lexical initially; verified hybrid/vector retrieval optional later | "Use lexical retrieval for Team TSGs" |
| Knowledge source limits | Host defaults within verified file/source/context maxima; user may lower them | "Limit knowledge files to one megabyte" |
| Knowledge citation detail | Source plus heading/page/line location | "Show detailed knowledge citations" |
| Diagnostic database retention | 30 days by default; independently configurable within the registered bounded schema; daily JSON remains limited to 30 files/30 days | "Keep diagnostic events for fourteen days" |
| Audit retention | 90 days by default; configurable from 30-365 days | "Keep audit metadata for six months" |
| Diagnostic verbosity | Content-minimising normal; bounded metadata-only detail | "Use detailed diagnostics for this session" |

Paths can be spoken or taken from explicitly selected clipboard text, then resolved/read back and validated.
Do not require typing a path, but do not infer one from unrelated context.
Long, ambiguous or low-confidence knowledge paths fall back to the native
picker rather than being guessed. Adding a knowledge source, using its excerpts
with a hosted model and deleting Kora's derived copies are separate decisions.
The complete source, retrieval, settings and voice contract is
[File and Folder Ingestion](File_And_Folder_Ingestion.md).
The Roaming AppData skill store and protected installation/source layout are architectural boundaries, not voice-selectable arbitrary write roots.
Permitted conversation history uses durable standard SQLite under verified private profile permissions and session retention. Copies outside that boundary are readable; database encryption is not required. Raw audio/secret persistence, silent remote diagnostic upload, automatic executable imports and secret logging remain unsupported.
Diagnostic, audit and session retention are independent. Session deletion does
not remove content-minimising audit records, while audit expiry does not remove
perpetual grants. Browsing/search/reasoning never refreshes either diagnostic or
audit retention. Reducing audit retention previews affected records and requires
separate apply-now confirmation before reducing existing due dates; otherwise
the new 30-365-day policy applies to new audit records. Removing source
enablement explains any immediate destructive/invalidation effect before
confirmation.

## 7. Startup and Updates

| Option | Default / limits | Example verbal setter |
|---|---|---|
| Start at logon | Explicit opt-in | "Enable start at logon" |
| Startup presentation | Tray/minimal shell; details optional | "Open the details view when you start" |
| Automatic update checking | Notify-only; enabled only for configured/permitted maintenance networking | "Only check for updates when I ask" |
| Update-check interval | 6 hours; 30 minutes-24 hours with backoff/jitter | "Check for updates twice a day" |
| Release channel | Stable; only trusted published channels | "Use the preview release channel" |
| Update reminder preference | Per-release reminder/suppression through proactive policy | "Don't remind me about this version" |

During the unsigned phase, update installation is external to Kora; no install/stage/download approval or "automatic install" setting exists.
Changing channels does not authorise a downgrade, skip verification, or rewrite the protected feed/trust configuration.
Logon startup uses saved ongoing consent and fresh gates, not a pre-logon
listener or a waiver of locked-session policy; current-run unlock/resume
recovery still requires explicit Enable listening under the microphone matrix.

## 8. Permissions and Approvals

Permissions and approvals are host-owned security records, not ordinary preference values.
The current bootstrap provides once/session/always preferences for named
model-suggested built-in actions and host-validated grant-change proposals.
This is not an executable/script grant, and exact direct lock does not yet
use the same gate. The inventory and finer-grained grants below are proposed;
future side-effecting built-ins and skills share the content/version-bound
execution gate in [skill and task execution design](../docs/skill-and-task-execution-design.md).
The native Permissions & Approvals page distinguishes single-use, session, and perpetual grants, including applicable, inapplicable, content-revoked, consumed, session-ended, and explicitly removed states.
Show stable ID, capability/action, canonical resource, identity, destination, scope/bound session, creator channel, creation/edit history, last use/use count, policy revision, and reason for inapplicability/revocation/removal.
Perpetual grants have no expiry/retention/eviction; see [Grant Types and Inheritance](Security_Data_Flows.md#grant-types-and-inheritance).
It never needs to display raw sensitive payloads; show content/parameter hashes and safe labels.

Deterministic voice commands may open or filter that page, explain why an action is currently allowed, and request revocation.
Explicit voice or UI confirmation completes removal, scope narrowing, or bulk removal against the exact displayed/read-back scope.
Broadening scope, changing identity/destination, choosing session/perpetual scope, or replacing consumed/ended/revoked/removed access requires newly reviewed action-specific approval through either channel and any mandatory OS/provider checks.
No approvable action category is categorically denied session/always duration; future execution grants bind exact implementation/dependency digests, invocation, and resources, unlike today's named model-action preferences.
Extending beyond a host maximum, granting unspecified effects, or approving a prohibited action is rejected, not overridable by approval.
Revocation blocks new calls immediately and reports in-flight work as cancelled, completed, or uncertain.

Required operations:

- List grants by scope and applicable/consumed/session-ended/inapplicable/content-revoked/removed state.
- Inspect one grant and its use history.
- Revoke one grant.
- Revoke all grants for a provider, account, resource, skill/revision, or capability.
- Narrow resources/capabilities or explicitly change the grant scope; never set retention/eviction for perpetual grants.
- Export content-minimising approval/audit metadata after preview.
- Explain which new approval would be required to restore or broaden access.

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
OS sign-in/OAuth/credential entry and required OS elevation remain separate secure workflows, not user-option values dictated to the model.
Kora risk determines review and confirmation specificity, not mandatory mouse use. Optional speaker matching and baseline UI input are not reauthentication.
No option can enable self-modification, disable mandatory lock policy, bypass executable isolation/egress/action approvals, or change "Kora" into a hidden always-transcribing microphone.
