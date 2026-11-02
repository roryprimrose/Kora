# Call-Aware Speech and Voice Configuration

Status: bounded manual host mode, independent device-local feedback and conservative output/origin gates delivered;
full R15 remains partial. A bounded Windows communications-notification adapter and host integration are implemented but the exact native profile is unqualified and unavailable.
The broader tool detectors and call-wide/persistent relaxation contracts below remain proposed. The [bounded speak-once interaction](#delivered-exact-local-speak-once-review) is implemented separately.

[Session Permission Levels](Session_Permission_Levels.md#voice-approval-and-protected-calls)
owns the implemented call-channel policy: spoken approvals default Off, with
separate reviewed native enable/disable controls; applicable grants and session
levels remain effective. This does not qualify automatic call detectors.

The [delivered R18 broker](Proactive_Interaction.md#delivered-r18-trusted-local-visual-broker---2026-10-09)
is visual/native only. It neither requests synthesis/playback nor inherits
Voice/Both feedback as permission. Protected Active/Suspected/Unknown call
evidence holds its admission; a changed call/privacy/lock/owner epoch retires
held action eligibility. Call end never opens a window, speaks a backlog,
changes a question target, enables listening or creates reusable authority.

## Bounded Windows Communications Activity Observations - 2026-10-26

This supports the [approved voice/typed/native interaction outcome](MVP_Scope.md#product-admission-and-realignment), not an actual-call or Teams detector.
The [Windows adapter](../src/Kora.Windows/Communication/WindowsCommunicationsActivitySource.cs) emits metadata-only observations.
The [application owner](../src/Kora.Application/Communication/CommunicationActivityCallStateService.cs) combines explicit source selection, source health, freshness and host eligibility into the existing [call policy](../src/Kora.Application/Communication/CallCommunicationPolicy.cs).

The local profile uses [Windows communications duck notifications](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessionmanager2-registerducknotification) with a null media-session filter: it observes notifications without opting out of ducking or opening an audio stream.
The fixed scope is the current default communications output endpoint and unlocked Windows session; it is not a selectable process/account or universal call census ([native lease](../src/Kora.Windows/Communication/WindowsCommunicationsNotificationPlatform.cs)).

- Registration and missing callbacks start **Unknown**, never Clear. A resolved non-Kora communications stream is at most **Suspected**. Process IDs are used only to exclude Kora through session metadata, never to scrape executable names, windows or content.
- **Clear** requires a witnessed matching end of a previously observed single non-Kora communications stream in the same device epoch. Multiple streams, unresolved identity, unmatched ends and malformed/overflowed notifications cannot establish Clear.
  Windows [session enumeration can omit new sessions](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessionmanager2-getsessionenumerator); a missing match therefore remains Unknown.
- One-second device/session health observations maintain the witnessed baseline, not a newly inferred clearance.
  The host rejects observations at age **5 seconds or greater**, future observations, invalid state/health, stale generations and non-increasing sequences.
  The freshness timer checks every 250 ms; these deterministic bounds are not a driver/acoustic stop-latency qualification.
- Device/session/privacy/ownership loss closes the observation generation and remains Unknown.
  Cancellation joins the owned worker; unregister failure retires callbacks, preserves the COM owner in quarantine and prevents replacement until release is confirmed.
  Clear/disable never reopens capture, replays suppressed speech or changes manual call state.

The device-local **`calls.activity-source`** defaults/resets to **None**.
Native Settings > Calls and exact typed/activated discovery/get/status/set/reset share the [admitted, audited writer](../src/Kora.Application/Configuration/CommunicationActivityConfigurationService.cs).
Protected original voice requests are rejected, including a later native click inside that voice request; fresh native/typed initiation remains required.
Atomic saved bytes, readback and the unconfirmed marker are independent of microphone consent, grants and manual mode.

**Availability:** the [production source](../src/Kora.Windows/Communication/WindowsCommunicationsActivitySource.cs) deliberately reports unqualified.
The native source-selection button is disabled; typed selection is denied without a preference write or native subscription.
Previously selected but unavailable or unconfirmed sources remain Unknown, not implicitly disabled. No source configured preserves ordinary speech policy after preference observation.
The narrow initial/multiple-stream limitations are implementation semantics, not coverage that a qualification flag can add.
Native accessibility, notification health, installed/client/device behavior and acoustic timing remain exact-profile E/Q work; deterministic fakes grant none of that readiness.

[Windows seam regressions](../tests/Kora.Windows.IntegrationTests/CommunicationActivitySourceTests.cs) and [host source tests](../tests/Kora.Application.UnitTests/Communication/CommunicationActivityCallStateTests.cs) exercise observation and resource ownership.
[Configuration tests](../tests/Kora.Application.UnitTests/Configuration/CommunicationActivityTests.cs) and [native-consumer tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.CommunicationActivity.cs) exercise the complete selected-source operation without calls, microphone/playback, network or accounts.
The existing call/manual/grant and spoken-approval policies remain authoritative.

## Delivered Bounded Manual Mode - 2026-10-07

Settings > Calls provides native **I'm in a call** and **Clear manual call mode**
controls. The application-owned [communication policy](../src/Kora.Application/Communication/CallCommunicationPolicy.cs)
keeps automatic evidence separate from a run-scoped manual layer. Manual Active
lasts until explicitly cleared or host restart; it is not persisted, and clearing
it never fabricates detector Clear. Active, Suspected and Unknown automatic
observations remain independently protective. No configured source and no manual
state leaves ordinary output eligible and automatic detection truthfully unavailable.

With the default saved policy, protected calls force visual output, including
approval readbacks and voice previews. Call entry invalidates pending synthesis/
playback before asynchronous UI dispatch or cleanup. Clearance does not replay
suppressed output, answer questions or reopen capture. Saved explicit visual/
activation preferences retain their existing format and behavior; Unknown now
uses the same conservative protection. New general speech protection
downgrades and call-wide temporary overrides remain unavailable. Call-time activation has a
[separate exact native preference review](#reviewed-call-time-activation-preference). The separate
[exact local speak-once review](#delivered-exact-local-speak-once-review) composes
only one current otherwise-eligible response; it does not disable protection.

Manual changes bind original `RequestOrigin` and observed call revision, then
recheck live ownership/privacy immediately before application. Host-system or
unknown mutation origin is not UI authority. Existing voice/input/output and
approval-prefix preference surfaces also use the protected-call origin rule;
later UI confirmation cannot relabel an admitted voice request. A stale or
denied request is not deferred. New UI initiation is required. Read-only status,
stop speech, disable listening and cancellation remain available.

Protected calls ignore legacy Session/Always action-name reuse without changing
the saved records. Fresh Once approval retains only its existing bounded legacy
authority; it is not migrated to an exact content grant. Call revision changes
invalidate pending legacy approvals and reusable dispatch, including final lock/
restart checks after asynchronous audio shutdown. The same
[host observation rule](../src/Kora.Core/Authorization/HostAuthorizationPolicy.cs)
is used by R05 exact grants. Maintained
[durable composition tests](../tests/Kora.Windows.IntegrationTests/CallPolicyCompositionTests.cs)
exercise real SQLite approval/consume transactions with these observations and
host proposal revisions; the native version question publishes truthful call
policy but has no effect proposal or grant-consuming route.

Maintained [policy tests](../tests/Kora.Application.UnitTests/Communication/CallCommunicationPolicyTests.cs)
and [call/audio host tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.Communication.cs)
cover mixed manual/automatic evidence, Unknown, original voice provenance, stale
revision, audit-admission races, cancellation, output invalidation, disposal and
legacy reuse races. Native control bindings are inspected without launching Kora.
These deterministic results do not complete native accessibility, real calls,
acoustic leakage/stop timing, detector/provider, optional owner-aware privacy or
A0-A4 acceptance.

Root Release/analyzers and all three maintained suites pass with no skipped
tests. Fresh merged portable line/branch coverage passes the existing exact
100% thresholds without exclusions. Version/publication process contracts and
the 123 static source-tool contracts also pass. Validation uses fakes and owned
private fixtures; no microphone, playback, call, Kora launch or session transition
was performed for this bounded delivery.

Experiment disposition: retain the speech, MG1 and containment executables and
historical receipts. Their acoustic/provider/native-runtime/OS-denial and
lost-receipt proofs are not equivalent to these fake policy or SQLite tests.
No call-only executable with maintained equivalent evidence was identified for
retirement, and none is deleted or rerun.

Related: [Proactive Interaction](Proactive_Interaction.md), [Task Lifecycle](Task_Lifecycle.md), [Environment Setup](Environment_Setup.md), [Security](Security_Data_Flows.md).

## Delivered Manual Command Parity - 2026-10-08

The separately delivered [deliberate current-response read](../docs/commands.md#read-the-current-response-aloud-once) uses the same speech admission and summary caps. It still refuses Active/Suspected/Unknown protection even for native initiation; it cannot substitute for the [separate exact speak-once review](#delivered-exact-local-speak-once-review).
Call revision changes retire the request, and clearance does not replay it ([host boundary](../src/Kora.Application/ViewModels/MainViewModel.ResponseRead.cs), [tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.ResponseRead.cs)).

The existing run-only layer now has shared native and exact typed/activated
`list call settings`, `get/status call.manual-active`, `set ... to on/off` and
`reset call.manual-active` routes. The current installed prefix is optional;
the fixed phrases are in both activation/recognition grammars. Reserved call
input precedes pending-question dispatch. Cached inspection is passive and
bounded (1,024-byte input / 64-KiB complete JSON); it never creates intent,
approval, unrelated activity, speech or automatic evidence. Mutation refuses
pending questions/approvals and preserves complete required visual content.

[ManualCallControl](../src/Kora.Application/Communication/ManualCallControl.cs)
reuses the common original-input/committed-intent
[HostControlAdmission](../src/Kora.Application/Hosting/HostControlAdmission.cs)
with its own session domain, and delegates to a dedicated
[host-only storage seam](../src/Kora.Core/Storage/IManualCallControlStore.cs),
not fabricated audio/maintenance authority. The host allocates original-user
control identity, commits intent, and revalidates current session generation,
own live context, original channel, observation revision and captured native
lifetime/owner/privacy/input gates. Required requested and outcome authority
audits use the consolidated lease/connection; their unique audit identities
correlate through the same exact host request/session/task and causal trace,
not caller fields or an invented approval. The same lease remains owned across
awaited resource closure before its truthful terminal audit; closure failure
or cancellation cannot acquire a successful terminal receipt.

The current observation lock surrounds the transition's generation fence and
source/admission recheck. Manual on/off/reset fence pending synthesis/playback
and capture/callback generations; late old requests may finish visually but
cannot synthesize after the layer clears. Resource release is awaited without
reopening capture or replaying output. Changed input needs explicit fresh
listening enablement/PTT. These are process-memory effects, not an atomic
persisted manual flag. Lost outcome evidence or unconfirmed resource closure
does not certify rollback: a separate explicit run-owned
`ManualControlEvidenceUnavailable` hold prevents speech, activation and action
admission through mandatory host gates, without revoking grants or changing automatic observations or saved flags.
Further manual mutations cannot use that unknown state as authority.

Perpetual/session grant records remain unchanged. The additive spoken-approval
preference defaults Off, including known older saved tuples. Other unavailable
`ExactReviewUnavailable` relaxation paths remain unchanged.
Restart starts manual-off with the composed detector still unavailable. This
does not deliver automatic detection, persisted call state, new settings/model
tools, speak-once, output mode/volume, microphone/consent, or full R15.
Maintained portable/native-binding and actual private-SQLite tests cover
parity, grammar/bounds, original input, source/generation and retirement/audit
failure without installed devices/providers or live effects.

**Experiment disposition:** no executable or receipt is removed or rerun.
Speech's acoustic/hardware/provider evidence, MG1's actual runtime envelope and
late-effect receipts, runtime/worker/containment proofs, and storage-engine/
encryption/artifact/release/source consumers are not equivalent to these
deterministic command/policy and standard-SQLite compositions. No call-only
maintained equivalent executable was found. Native accessibility, automatic
provider detection, real-call/acoustic/runtime/installed and A0-A4 qualification
remain separately gated.

## Delivered Natural Manual Call Phrases

The [same parser and fixed phrase/help catalogue](../src/Kora.Application/Communication/ManualCallCommand.cs) accepts these exact whole utterances, case-insensitively, with an optional current assistant-name prefix:

| Phrase | Existing manual operation |
| --- | --- |
| `turn on manual call protection` | Set the current-run manual layer on. |
| `turn off manual call protection` | Clear only that manual layer, like `set call.manual-active to off`; schema `reset call.manual-active` remains available. |
| `show manual call status` | Inspect the cached combined manual/automatic observation and its availability/recovery information. |

The [shared consumer route](../src/Kora.Application/ViewModels/MainViewModel.cs) uses this parser for busy-state command availability, transcript dispatch and recognition phrases. Reserved malformed natural commands clarify before model/question fallback; there is no substring/fuzzy invocation or alias for the ambiguous conversational claims `I am on a call` / `my call is over`.

The aliases use [existing committed control](../src/Kora.Application/Communication/ManualCallControl.cs), preserving original input, session generation, observed revision and host/privacy/native-lifetime/voice admission. Protected-call voice writes remain denied; capture may be unavailable. No exception, grant, replay or microphone reopening is introduced.

Off/reset never assert automatic Clear while Active/Unknown evidence or a lost-receipt hold remains. [Passive status and retirement](../src/Kora.Application/ViewModels/MainViewModel.Communication.cs) preserve pending questions/security previews. Changed manual state awaits owned resource retirement; late old callbacks cannot replay or reopen input.

Bounded **I - Implementation** uses [parser tests](../tests/Kora.Application.UnitTests/Communication/ManualCallCommandTests.cs) and [consumer tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.ManualCallCommands.cs). These deterministic fakes do not qualify real-call, acoustic, hardware or installed **E - Enablement / Q - RC** profiles.

## Delivered Exact Local Speak-Once Review

The [host interaction](../src/Kora.Application/ViewModels/MainViewModel.CallSpeech.cs) supports one otherwise-eligible **current ordinary response** while call protection applies. In the visible response, choose **Review speak once during call**, inspect the complete exact speech text and selected/effective speaker, voice, volume, rate and output policy in the existing [native Details presenter](../src/Kora/ResponseWindowController.cs), then separately choose **Confirm speak once**. **Cancel call speech** retires the review and joins any owned playback; **Call speech status** is passive.

Exact typed `review call speech`, `confirm call speech`, `cancel call speech` and `call speech status` share that operation. Review and confirmation require fresh original local UI input; original voice is denied even when a native control is later invoked inside that request. Activated voice may inspect status or cancel but cannot mint a review or lease. Unknown provenance, session/source identity, ownership, output or manual-control evidence remains closed ([grammar](../src/Kora.Application/Voice/CallSpeechCommand.cs), [review authority](../src/Kora.Application/Communication/CallSpeechReview.cs)).

The host pins response/request identity and revision, pending-decision/input epochs, native presentation lifetime, owner/privacy/recovery, call revision, speech selection/limits, ordinary output and feedback modes, selected/effective endpoint, volume and rate. A review expires after two minutes from creation, including any subsequent playback. Confirmation consumes it before awaited audit/admission; the [typed lease](../src/Kora.Application/Communication/CallSpeechLease.cs) permits one start under the [shared call-policy lock](../src/Kora.Application/Communication/CallCommunicationPolicy.cs), for only the reviewed text and pinned concrete endpoint. Changes, cancellation, failure and late completion never authorize replay or a second start.

This narrow exception overrides only call speech suppression and the call feedback output selection for that one response. Ordinary VisualOnly, both summary caps, lock/privacy/owner, mute/zero volume, active capture, selected consent/security protections and local speech availability remain mandatory. No preference, call state, listening consent, approval, grant or retained data is changed; neither ordinary read commands nor unrelated speech inherit the lease. Requested/terminal audits carry opaque response identity and outcomes, never the reviewed content ([shared read lifecycle](../src/Kora.Application/ViewModels/MainViewModel.ResponseRead.cs), [summary gate](../src/Kora.Application/Configuration/SpeechConfigurationService.cs)).

The [service regressions](../tests/Kora.Application.UnitTests/Communication/CallSpeechReviewTests.cs) and [consumer regressions](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.CallSpeech.cs) use deterministic fakes. This is bounded **I - Implementation**, not actual-call, acoustic, provider, physical-device, installed-native accessibility or full R15 **E/Q** qualification. Existing unavailable profiles remain unavailable; no experiment is removed or rerun. Arbitrary retained-answer selection, questions/approvals, call-wide relaxation and persistent downgrade remain outside this increment.

## Ordinary Speech Stop and Recovery

Ordinary responses and voice previews retain **Stop speaking** and active output
state while the provider reports playback active, even if its speech operation
has returned, failed or been cancelled. Returning from Stop is not a silence
receipt: native, exact typed/activated-voice and tray Stop share the host's
physical-release check. Unconfirmed release reports failure and retains recovery;
only observed release permits "No speech playback is active."

New response/preview speech is withheld during that recovery, without automatic
retry or replay. Full visual results remain available; captions still retire on
Stop, invalidation or unsuccessful completion, and private presence stays hidden
when the host is ineligible. Passive playback observation can clear active state
after the provider actually releases output. Existing call leases, retained-read
joins, consent/privacy and call suppression remain unchanged.

The [ordinary recovery regressions](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.OrdinarySpeechRecovery.cs)
use deterministic providers for operation return, physical activity, Stop failure
and cancellation. This is **I - Implementation** repair, not acoustic stop timing,
real provider/device, installed-native accessibility or **E/Q** qualification;
no wake/model/native profile is enabled.

## Delivered Bounded In-Call Feedback

The independent device-local **`calls.feedback-mode`** defaults to **UI** and
offers **Voice / UI / Both / Inherit** in native Settings and exact
typed/current-name ACTIVATED discovery/get/status/set/reset. The
[authoritative contract](User_Configuration.md#delivered-bounded-device-local-in-call-feedback-r10r15)
defines source provenance, reset, atomic/audited confirmation and recovery.
It applies before ordinary task/queue/session/device output only under
effective Active/Suspected, including manual Active. Unknown/invalid evidence
does not activate a mode override and always withholds speech with full visual
recovery; Clear/Unavailable use ordinary precedence. Unavailable/corrupt/pending
feedback storage also withholds output across restart, without disabling input.

Feedback does not grant speech permission. Voice/Both/Inherit retain independent
call suppression and every ownership/privacy/mute/capture/lifetime/visual gate.
No automatic detector, speak-once exception, downgrade review, general model
settings tool or proactive configuration is added. Original protected-call
voice mutations remain denied; fresh admitted UI changes remain possible.
Changes retire queued/active output without replay/capture/consent/grant changes.
Native accessibility, source-age/detector integration and real acoustic leakage/
stop timing remain separate acceptance. All experiment evidence is retained;
see [the assessed disposition](Archive/Implementation_Roadmap_2026-10-11.md#bounded-in-call-feedback-experiment-disposition).

## Delivered Bounded Local Caption UX - 2026-10-09

The disabled-by-default R25 local caption adjunct now supports working-area
corner placement, a validated 0-30-second normal-completion dismissal delay
(default 5), and run-only pin/unpin. Native and exact typed/current-name
ACTIVATED discovery/get/status/set/reset share the
[caption configuration contract](User_Configuration.md#delivered-bounded-caption-ux-options-r25---2026-10-09).
Placement/delay reuse the existing admitted session/generation, original-channel,
revision, audit/atomic/readback/receipt and unconfirmed-evidence gates.
They never enable captions or change speech/output/call/capture policy.

Only already-observed matching host-admitted response playback text can remain
after normal successful completion, explicitly labelled **PREVIOUS SPEECH**.
Pinning cannot retain unplayed/queued/failed/suppressed text or defeat immediate
stop/cancel/replacement, call revision, privacy/ownership, input-recovery or
configuration retirement. Clearance/unlock/unpin never replays speech or revives
a retired caption. Original protected-call voice pin/option mutation is denied;
inspection remains visual and passive. Required full native recovery remains
independent. Caption content is never persisted, logged, put in history or sent
to a model.

The [2026-10-10 native run-only display choice](User_Configuration.md#delivered-run-only-native-caption-display-choice-r25---2026-10-10) preserves primary as the untouched default and permits explicit current-source selection/reset only from original native UI.
Source/snapshot/selection lifetime revalidation and bounded working-area/DPI geometry add no playback, capture, durable setting or authority. Missing/ambiguous explicit displays hide and retire even pinned captions without fallback.

Explicit reselect/Return to primary retains every current host/privacy/call/request/generation gate and never clears unconfirmed preferences or restores retired text. A successfully retained caption can move without changing its original completion deadline or pin semantics.
Required response/question/recovery panels remain independent. No new installed/native, multimonitor or acoustic acceptance is inferred.

The [SAPI source-range sentence timeline](Information_Display.md#optional-speech-text)
is implemented, but [production profile admission remains closed](../src/Kora.Windows/Audio/WindowsTextToSpeechService.SentenceAlignment.cs#L28).
This is not all-engine word alignment or hardware qualification. No estimated
timing, detector, call-feedback change, model egress or rich renderer is introduced.
Existing bounded ordinary-speech caps remain intact; a brief
spoken offer for an over-limit detailed answer is separate work. Maintained
tests use fake playback/time and private preference fixtures, with source-tested
native bindings; they do not qualify installed accessibility, live audio/calls
or acoustic stop timing. All experiment evidence is retained.

## Default Behaviour

Suppress automatic speech during a detected/suspected call, or while an enabled detector cannot establish current state.
This covers requested answers, proactive suggestions, summaries, approval readbacks, and setup/maintenance prompts.
Use visual output and explain the suppression reason; do not silently discard the answer or cancel its task.
Only an explicit speak-once request can bypass this call rule under the default policy.

Call gating does not itself disable wake listening.
The user can still say "Kora, ..." to request work or inspect preferences, but voice-initiated voice-setting and in-call-option changes are rejected while call protection applies.
Voice activation during detected calls is an independent device-local setting
and remains enabled by default. If the user disables it, an Active or Suspected
observation closes active capture and blocks new activation until the call
clears; it never grants listening consent or silently reopens capture.
The same gate blocks Awaiting Conversational Reply and prefix-free answer
capture. A visual question shown during that state cannot bypass disabled
call-time activation merely because Kora asked it before the observation
changed.
Locked-session microphone and speech restrictions remain mandatory and cannot be overridden.

### Reviewed call-time activation preference

The [native Calls controls](../src/Kora/SettingsWindow.axaml) can restore a previously disabled device-local activation preference through **Review enabling activation**, then separate **Confirm reviewed activation** or **Cancel activation review**.
The [review owner](../src/Kora.Application/ViewModels/MainViewModel.CallVoiceActivation.cs) shows Off -> On, device scope, current call observation and the independent consent/speech/approval boundaries. Original voice, model invocation and unknown origins cannot prepare or relabel this review.

The single-use [policy review](../src/Kora.Application/Communication/CallCommunicationPolicy.cs) binds the exact settings, call/privacy/ownership/input/recovery revisions and original active Calls window lifetime. Hide, deactivation, tab change or close retires it. The shared audited writer revalidates after requested audit, checks unchanged saved state, saves atomically and verifies readback before publishing.
Unconfirmed persistence, audit or invalidated authority is not success or a rollback guarantee; start a fresh review rather than retrying the old confirmation ([regressions](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.CallVoiceActivation.cs)).

Saving permits only otherwise eligible activation/question replies. It never opens capture, clears manual/recovery holds, grants listening consent, changes speech/spoken approval preferences or changes grants. Separate **Enable listening** still requires fresh existing gates. Unknown/manual-evidence-unavailable protection remains authoritative ([native regressions](../tests/Kora.Windows.IntegrationTests/CallVoiceActivationNativeTests.cs)).
This is bounded implementation, not detector, provider, hardware, acoustic or installed accessibility qualification.

The separate in-call feedback override defaults to UI-only, with Voice/UI/Both/Inherit choices through the shared settings registry.
It takes precedence over ordinary task/queue/session/device response mode, subject to mandatory privacy/lock/mute/speech policy and an explicitly permitted single-response exception.
Inherit restores ordinary output precedence; this setting remains independent of voice activation and does not implicitly relax separate speech-suppression rules.
The model-facing `calls.*`, `speech.*`, and `settings.*` boundaries are defined in [Internal Model Tools](Internal_Model_Tools.md).

## Ignoring Reusable Grants During Calls

The former default-On `calls.ignoreReusableGrants` behavior is superseded by [Spoken Approvals During Calls](#spoken-approvals-during-calls). Keep this heading for historical links. Call entry does not withdraw applicable grants or selected session levels.

## Spoken Approvals During Calls

Device-local **`calls.allowSpokenApprovals`** is **Off by default**. During manual Active, supported enabled Active/Suspected, or enabled Unknown/stale/failed call evidence, reject spoken Once/Session/Always approval and disclosure-consent answers while preserving the pending UI card. UI can select every otherwise valid grant duration; calls do not force Once.

Existing applicable grants, preapproved destinations and the selected Review all / Model review / Approve all level remain effective. Call entry does not revoke/ignore those grants or turn automatic admission into a fresh-approval requirement. Safe and Approve-all eligible actions can continue without prompting; independent consent and mandatory gates still apply.

The setting is independent of voice activation, feedback and speech suppression. Voice commands may remain eligible even when spoken approval is not. Feedback Voice/Both, speak-once readback or an already-open microphone cannot enable spoken approval. No detector/manual state means detection unavailable, not proof of no call.

Enabling permits normal deliberate voice approval subject to exact proposal/target, expiry, required authentication and normal input policy. Enabling during protection requires fresh original UI initiation and explicit confirmation. Voice-originated changes to this voice/in-call setting remain rejected; later UI confirmation cannot relabel their origin.

Recheck call evidence/setting revision at voice-answer acceptance. Call entry or disabling the setting rejects stale unaccepted voice answers; do not revoke a committed grant or accepted decision solely because the channel became unavailable. Mandatory operation/policy/content/cancellation checks still run at dispatch.

Clearance never accepts rejected speech, answers cards, restores grants, reopens capture or replays work. Show **Spoken approvals disabled during call; UI available** separately from the level. See the [voice/call contract](Session_Permission_Levels.md#voice-approval-and-protected-calls) and [acceptance gate](Acceptance_Criteria.md#session-permission-level-gate).

The [host call observation](../src/Kora.Application/Communication/CallPolicyObservation.cs)
keeps reusable grants applicable and supplies current spoken-approval eligibility.
[Native review/confirmation](../src/Kora.Application/Interaction/SessionPermission/MainViewModel.SpokenApprovals.cs)
binds the change to fresh UI origin, call/privacy/ownership revisions and the
shared audited preference writer. Known older settings migrate Off; stored
grants are not deleted, widened or converted. Production reviewer and
automatic-detector qualification remain separate.

## In-Call Settings Origin Gate

While manual Active, any enabled Active/Suspected source, or enabled Unknown call evidence applies, the host rejects voice-initiated changes to voice settings and **any setting related to in-call options**.
This covers input/output/activation preferences, in-call feedback, spoken-approval enablement, call speech/Unknown handling, detectors/accounts, manual call state, temporary/speak-once exceptions, and reset/undo.
The restriction applies to all scopes, including ordinary device/session/task settings whose voice effects would otherwise be masked by an in-call override.
Inspecting settings/status remains permitted; unrelated settings are not made UI-only.

Mark affected options/effects in the host-owned registry. Preserve trusted initiating-channel lineage across model interpretation, tasks, proposed settings, and tool calls; model claims or a later approval click cannot relabel a voice-originated request.
Reject such requests without mutation, a confirmation proposal, or deferred application when the call ends.
Explain the rejection visually and offer the settings surface; the user must initiate a fresh UI/mouse/keyboard change with normal validation and confirmation.
Unknown initiating-channel provenance is not treated as UI authorization.
Check protected call evidence both when accepting the request and immediately before applying it; entering a protected call invalidates pending voice-originated changes.
These controls are host policy, not another voice-configurable bypass switch.

Deterministic safety actions such as stop speech, mute/release capture, and cancellation remain eligible through voice; they are not configuration writes and cannot implicitly unmute, reopen capture, or clear call protection.
Fresh single-use approval for an ordinary operation remains independently eligible through voice/UI under the action-authorization policy; the settings restriction does not prove that voice input is free from conference/media playback.
It also covers frequent-speaker learning/profile changes; read-only profile status and default voice availability remain independent.
This is an explicit privacy/security exception to ordinary voice/UI parity, not a claim that all mouse input is stronger authentication.
The [accepted Windows-session trust boundary](Security_Data_Flows.md#trust-boundaries) does not make speaker identity or perfect external-playback rejection mandatory for baseline voice authorization.

## Built-In Gate, Extensible Detectors

All TTS/playback paths use one host-owned speech-policy service.
Detectors supply observations; they cannot play audio, change policy, or grant an override.
The host evaluates session state, voice consent, mute/quiet settings, playback safety, capture/prompt ownership, and call policy before enqueueing and starting speech.
Re-evaluate during playback; a new blocking call observation stops current speech rather than only affecting the next response.

Detector observations contain source/account/device scope, state, observation time, freshness limit, evidence class, and health/limitations.
Normalised states:

- Active: a supported call signal reports active communication.
- Suspected: evidence suggests communication but cannot establish an actual call.
- Clear: the detector's supported checks currently report no call, within its declared scope.
- Unknown: stale, inaccessible, ambiguous, or failed state.
- Disabled/Not Configured: no detection promise for that source.

Any enabled Active/Suspected source gates speech.
Without such a source, an enabled Unknown source also gates speech by default.
An enabled detector does not become Clear merely because a request failed, credentials expired, or the app produced no recent audio.
Clear means clear within tested coverage, not proof that the user is in no call anywhere.

If no detector is configured, show "automatic call detection unavailable", offer setup/manual call mode, and preserve ordinary speech policy.
Do not silently claim call protection or make first-run speech permanently unavailable.
Disabling a detector is an explicit preference change, not an automatic recovery from a failed probe.

## Teams Detection

Prefer supported local metadata/signals where validated; do not rely on private Teams databases, unsupported log scraping, or screen/audio recording.
Windows audio-session metadata can provide supporting evidence, but audio activity alone does not identify a call.
Exclude Kora's own capture/playback from candidate activity.
Test muted calls, output-only calls, device changes, client upgrades, and multiple clients; a process/window being open is insufficient.

An optional Microsoft Graph adapter can read the configured user's Teams presence:

- Use delegated `Presence.Read` for the user's own work/school account; do not request tenant-wide or write permission for this feature.
- Personal Microsoft accounts are not supported by this Graph operation.
- Interpret `inACall` and `presenting` as blocking communication/activity signals.
- Calendar-derived `inAMeeting` is a conservative Suspected signal, not proof of joining a call.
- Generic Busy/DND is not asserted as a call; an independent preference can suppress speech for those states.
- Account presence is aggregated across sessions/devices and can be delayed or overridden; disclose that scope.
- Offline, missing, stale, unsupported, or inconsistent state must not be interpreted as authoritative local clearance.

Proposed polling interval: 15 seconds when enabled, with bounded requests/backoff; a sample older than 60 seconds becomes Unknown.
Those limits describe observation freshness, not guaranteed Teams backend latency.
Graph detection requires explicit account/network consent and is unavailable during local-only operation.
If an enabled Graph detector becomes unavailable under that policy, it remains Unknown until the user chooses another detector or disables it.

Teams automatic detection needs an integration proof on supported clients/accounts.
If no reliable local signal is available and Graph cannot be used, offer manual call mode and explain the limitation.
Do not make an unsupported platform claim to satisfy the feature.

## Other Communication Tools

Use narrow detector adapters for supported Zoom, Slack, Discord, browser calling, or other tools when reliable signals are available.
These are future integrations, not claims of initial coverage.
A generic "communication app audio active" heuristic is Suspected and labelled as such.
Do not request meeting contents, participants, transcripts, or audio samples solely to control speech.
Calendar events may support a separate conservative meeting preference but cannot establish actual attendance.

## Voice Configuration

Settings are host-owned, device-local preferences, not editable application policy code.
Use the shared registry and voice/UI contract in [User Configuration](User_Configuration.md); detector-specific settings are not UI-only.
Outside protected calls, route unambiguous voice commands to typed preference operations; ask for clarification about ambiguous scope/duration.
During protected calls, reject affected voice-originated changes under the [settings origin gate](#in-call-settings-origin-gate); do not offer a voice-confirmable downgrade.
Apply the chosen setting immediately, display the effective policy, and speak acknowledgement only if the resulting policy allows it.

Examples:

| Utterance after "Kora" | Effect |
|---|---|
| "Don't speak while I'm in a call" | Persist suppression for all automatic speech during calls |
| "During calls, only speak when I ask" | Select default suppression with speak-once override |
| "Only suppress unsolicited suggestions during calls" | Persist a policy allowing requested answers but not proactive speech |
| "Stay silent when you can't tell whether I'm in a call" | Persist conservative Unknown handling |
| "Use normal speech when call detection is unavailable" | Open an exact voice/UI confirmation for the reduced-protection Unknown fallback |
| "I'm in a call" | Set manual Active override until explicitly cleared |
| "My call has ended" | During protected calls, reject the voice-originated manual-clear change; require a new UI request, and enabled automatic sources still apply |
| "Allow spoken approvals during calls" | Request the separate spoken-approval setting; during protection require fresh native initiation and confirmation, without changing grants or session levels |
| "Require UI approvals during calls" | Disable the spoken channel without withdrawing applicable grants or otherwise eligible automatic admission |
| "Read that answer aloud once" | Normal speech policy applies outside protected calls; a speak-once call exception must be UI-initiated while protected |
| "Allow normal speech for this call" | Open exact voice/UI confirmation for a bounded call-wide relaxation; default expiry 1 hour or observed call end, whichever is first |
| "Restore call-aware speech defaults" | Reset configurable call preferences and temporary overrides |
| "What are my call speech settings?" | Show policy, sources, freshness, and override state; speak only if permitted |

All example setters/resets/temporary overrides follow the origin gate; voice availability outside calls does not permit voice configuration changes during calls.
An explicit persistent request can propose normal speech during calls, but deliberate exact voice or UI confirmation is required before the privacy downgrade and it never overrides lock, microphone consent, user mute, an enabled owner-aware private-speech policy, or another mandatory rule.
Speak-once tokens bind a response/prompt ID and current policy generation, expire after 2 minutes, and are consumed once.
They cannot drain accumulated speech or change persistent settings.
Outside protected calls, voice may request a single identified response; a call-policy exception during a protected call requires fresh UI initiation. Ambiguous intent or an unsatisfied optional speaker preference requires clarification/explicit alternate-channel confirmation.
Temporary overrides are revoked on lock/sign-out/restart; unresolved call-end detection cannot extend their time limit.
Intent/target ambiguity produces visual clarification while speech is gated.

## Deferred Speech

Continue visual results and task state while speech is suppressed.
Do not queue a backlog of spoken answers or replay all old suggestions when the call ends.
At most offer one still-relevant summary after call clearance, subject to proactive policy.
An unseen approval does not become eligible for voice acceptance merely because its readback was suppressed.
Keep prompt identity/expiry and visual approval behaviour intact.
Activation cues must also respect call quieting; use visual feedback rather than an audible cue when gated.

## Reference Evidence

- [Teams presence behaviour](https://learn.microsoft.com/en-us/microsoftteams/presence-admins): calendar-derived meetings and multi-device status.
- [Graph presence permutations](https://learn.microsoft.com/en-us/graph/cloud-communications-manage-presence-state): `busy/inACall`, `busy/inAMeeting`, and session aggregation.
- [Get own presence](https://learn.microsoft.com/en-us/graph/api/presence-get?view=graph-rest-1.0): delegated `Presence.Read` and account support.
- [Windows audio sessions](https://learn.microsoft.com/en-us/windows/win32/coreaudio/audio-sessions): audio stream grouping, not a universal call-state API.
