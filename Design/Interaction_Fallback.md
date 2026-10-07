# Shared Questions and Voice Readiness Recovery

Status: required host capability from Slice A. Voice-first must not mean voice-only.

R05 supplies a portable
[typed question service](../src/Kora.Application/Interaction/HostQuestionService.cs)
with matching host IDs/revisions, bounded single/multiple-choice/text answers,
mixed-channel draft edits, explicit submit/cancel and expiry rejection.
A bounded native presenter is now composed through the existing durable
local-version query: tray **Review local version (native question)**. It uses
the production question service/store, explicit draft/submit/cancel and exact
original question/session targeting. The shared native component supports
single/multiple-choice and bounded text, and routes admitted exact approvals
through the authorization service, not ordinary submit. No production effect
proposal or generic onboarding/device question workflow is enabled.
The separate bounded tray microphone controls below use existing host services,
not the generic question gateway.
Generic voice focus, secure forms, full workspace integration and real native
accessibility acceptance remain pending.

## Delivered Bounded Native Question - 2026-10-07

The host creates one Active durable session for the existing harmless query,
publishes a fresh ordinary-question policy snapshot with no exact proposal and
mandatory-effect gates closed, and presents a five-minute question under the
committed nonterminal task. A single owned, non-topmost native window displays
session/task/question identity, original request origin, generation, revision,
expiry, source/purpose and answer bounds. No option is preselected; existing
committed drafts may be restored. Local edits are not durable until **Save
draft**; saving advances the key and invalidates the old review. **Submit** and
**Cancel question** are explicit. Closing/Escape only closes presentation and
does not fabricate a submitted answer, approval or grant use.

Native activation rechecks the exact target through the audited review service
without counting activation as user review. Expiry and live privacy/ownership
loss disable input and clear presentation. Every decision revalidates the
serialized host snapshot and live desktop gate. Stale/revised/closed/foreign
targets fail closed instead of refreshing to another question. Deferred native
operations use linked new trace roots for the original host request, never
incoming correlation or foreground selection as identity.

**Review exact record** displays the complete immutable host question/proposal
record through the bounded passive plain-text renderer. All binding digests,
policy/proposal revisions, invocation, scope options and origin remain exact.
These are the existing host-resolved fields, not fabricated source bytes or
digests. The contract does not contain operation/script source bytes: their
absence is explicit, and source acquisition, containment and deployment gates
are not satisfied by the native reader. Approval additionally requires the
current complete review to have been displayed; the authorization service
still revalidates exact applicability and protected-call origin restrictions.
Review, answer/approval and grant consumption are distinct; the presenter has
no consume/effect route and legacy action-name grants are not migrated.

Atomic audit/storage failure disables retry on that target and reports failure,
not success. Explicit cancellation inside the existing query leaves an
incomplete dispatch record; startup recovery records Unknown without replay.
No cancelled query is labelled Succeeded. Close/conflict/privacy failures also
claim no successful answer/query receipt. Starting another review is a new
host request, not an automatic retry. The ordinary typed/voice version command
is unchanged. Tests use production services and private disposable SQLite,
with no app launch, speech, devices, models, network or side effects. Native
visual/keyboard/screen-reader/DPI and speech acceptance require separate approval.

Related: [Interaction and Sessions](Interaction_And_Sessions.md), [Environment Setup](Environment_Setup.md), [Task Lifecycle](Task_Lifecycle.md), [User Configuration](User_Configuration.md), [Information Display](Information_Display.md).

## Product Requirement

Kora can ask questions and receive mouse input when no microphone is configured, permitted, connected, or usable.
Voice, UI-only, and mixed-channel questions are equal supported paths, not merely microphone-failure fallbacks.
Single-choice, multi-choice checkbox, bounded text/form, draft, submit/cancel, and session ownership semantics are defined in [Interaction and Sessions](Interaction_And_Sessions.md#structured-questions-and-mixed-channel-replies).
This also applies when speech recognition/wake assets are unavailable or the user intentionally closes the microphone.
The fallback is always present in the native shell; it needs no model, skill, network, working microphone, or TTS engine.

**Settings > Calls** now includes run-scoped manual Active/clear and separate
automatic availability/status. Default protected-call output is visual-only;
pending synthesis/playback is invalidated before UI work, and clearance replays
nothing. Native input does not convert an earlier voice request into UI origin.
A protected-call voice/in-call option change needs fresh UI initiation, original
call revision and current ownership/privacy admission. Downgrades requiring
complete exact review remain unavailable, rather than borrowing an ordinary
question or legacy action-name approval. Stop/cancel and readable status need no
spoken acknowledgement. Native accessibility and actual audio/call acceptance
remain separate outstanding gates.

All questions appear as readable native prompt cards with explicit submit/cancel controls and selectable typed answers where possible.
Speech is an optional additional delivery channel when existing output consent/policy permits, not a prerequisite for answering.
Hidden speech text, visual detail preferences, or an unavailable rich renderer cannot hide these controls.
Provide equivalent keyboard/screen-reader interaction; mouse use never requires a spoken acknowledgement.

## First Launch: Obtain Ongoing Voice Consent

1. Open accessible first-run onboarding, even when the normal configured startup presentation is tray/minimal.
2. Enumerate audio capture endpoints before opening capture.
3. Select System without recording when no Kora override exists. Explain
   ongoing local wake listening and obtain explicit voice consent before
   enabling it; later ordinary startup uses saved consent and fresh gates.
4. Show each detected endpoint with friendly name, stable host identity, current availability, and a default-device badge where applicable.
5. Provide an immediately accessible Disable listening action that releases the
   microphone for the current run, plus microphone selection and test actions.
6. Explain local configured-name detection ("Kora" initially), bounded ambient audio, command transcription, visible listening status, and mandatory locked-session denial.
7. Offer "Enable voice" with explained ongoing consent, an explicitly consented
   "Test selected microphone", "Choose another microphone", and "Continue
   without voice"; also expose Disable listening once capture is active.
8. Before automatic startup or explicit recovery, revalidate session,
   endpoint, permissions, and assets; confirm readiness only after the real checks succeed.
9. If prerequisites are missing or opening fails, show the exact blocker and next actions; remain fully usable through mouse-based prompts.

After saved ongoing consent, ordinary safe startup attempts capture
automatically because listening is Kora's primary purpose. Declined/withdrawn
consent stays closed across restart. The
[microphone matrix](Security_Data_Flows.md#microphone-consent-and-enablement-matrix)
separates consent from current-run enablement and recovery holds.
Device selection by itself does not restart capture after manual disablement,
lock/unlock, suspend/resume, permission/device loss or capture/asset failure.
Offer [Activation Name](Activation_Name.md) selection before ongoing listening, including a custom-only recommendation for shared offices; this is available by mouse before voice is ready.
Distinguish duplicate friendly names with device details; bind operations to an enumerated endpoint ID, never the displayed label alone.
A system-default change may reroute active capture only while System is selected;
specific endpoint overrides remain pinned.

An optional microphone test opens only the chosen device after explicit consent, for at most 5 seconds.
Show a live level indicator, not continuous transcription; do not save/transmit test audio or invoke a model.
Stop/release on completion/cancel/lock/removal. Testing does not grant ongoing wake listening.

## Failure and Device Changes

Watch endpoint topology/default changes and capture errors; re-enumerate on refresh/open/recovery.
Represent at least Not Configured, Permission Denied, Missing/Disconnected, Disabled, Open Failed, Speech Assets Missing, Muted, Session Locked, and Ready.
Report "busy/in use" only when the actual API supports that diagnosis; do not guess from a generic open failure.

When selected capture becomes unusable:

- Close the audio-generation gate, discard incomplete command audio/pre-roll, invalidate stale callbacks/transcripts, and release owned device/recognition resources.
- Mark voice input unavailable, not listening. Retain the last requested endpoint preference, separately from effective availability.
- Preserve active task/queue/approval state; losing the microphone is not implicit task cancellation or approval.
- Show a recovery question: "Your microphone is no longer available. Which microphone should I use?"
- List current endpoints, the unavailable previous selection, refresh, permission help, and "Continue without voice".
- Require explicit selected-endpoint enablement before reopening capture, even if the original device returns.
- Revalidate playback rejection on a changed input/output route; disable TTS with explanation if necessary, rather than dropping required wake activation.

Hot-plug alone never starts recording, selects another device, or grants consent.
Opening a selection while devices change uses a topology/configuration revision; stale selection reports the change and refreshes rather than opening a similarly named substitute.
Bound capture-open attempts (proposed 5-second deadline) and allow cancellation without blocking queue/status controls.
Late successful opens after cancellation/lock/device replacement are closed and cannot restore listening.

No detected device: explain that none is available, offer Refresh and Continue without voice.
Permission denied: explain Windows privacy restrictions and offer a fixed trusted host action to open the relevant Windows settings page; Kora cannot grant itself permission or silently elevate.
Muted: offer an explicit Enable listening control; do not label intentional mute as missing hardware.
Locked/disconnected/unknown Windows session: no capture/test/interactive approval. Hide sensitive prompts; generic readiness resumes only after unlocking and explicit user action.

## Mouse Answers Across Kora

Different-build takeover and original-version return use native mouse prompts under [Instance Coordination](Instance_Coordination.md); they require no working microphone, and the waiting/supervisor process cannot act as another assistant.

Use the same prompt identity, validation, scope, and expiry for voice and visual responses.
Examples include microphone/provider selection, setup plans, task clarification, queue choices, skill revision review, update proposals, and permitted action approval.
Prefer selectable typed choices and source-qualified device/task/skill labels over forcing text entry.
Show the selected answer and its consequences; Apply/Confirm commits only the current valid proposal.

For genuinely free-form answers, provide a normal text field with an optional OS on-screen keyboard route and explicit "Use clipboard as my answer".
Neither typing nor clipboard use is mandatory for the microphone-selection workflow.
Clipboard input is an explicitly requested bounded snapshot with provenance and normal context/egress review, not silent polling or automatic submission.
Passwords/tokens remain in secure authentication flows, never model-visible prompt fields or clipboard-answer shortcuts.

Only one foreground voice-reply target exists, distinct from session selection and task execution; explicitly addressed valid UI cards in other sessions remain usable.
Device recovery is a separate host card; it cannot replace an action approval and let an old Confirm click apply to a new question.
Changing voice focus withdraws generic spoken eligibility; present/revalidate a background proposal before accepting its verbally targeted response.
Double clicks, delayed events, stale devices/configuration revisions, expired grants, and lock races produce no duplicate action.
An expired prompt shows Expired and a request to re-present/revalidate, never a silent lifetime extension.
Changing input channel does not extend deadlines, weaken action-specific review, or bypass mandatory OS/provider checks. High risk alone does not require mouse input.

Model-generated option text remains untrusted: the host binds each selection to a validated typed proposal.
A webpage, Markdown button, or skill instruction cannot impersonate a native prompt/control.

## Visibility and Recovery Access

First-run/unconfigured interaction opens the native onboarding panel.
During normal operation, device-loss status and a recovery card are available without stealing focus or forcing topmost UI.
If Kora is hidden, its persistent tray/status indicator exposes "Choose microphone", "Enable listening", "Show questions", and local cancel/status controls by mouse.
Never require saying "Kora" to reveal a microphone-recovery question.
The tray/settings route also exposes [Activation Name](Activation_Name.md) recovery if a custom profile is unusable; restoring "Kora" is explicit, never an automatic office-wide fallback.
Dismissal chooses no change/continue without voice; it is not consent, and the recovery entry remains reachable.
Avoid repeated modal recovery prompts while the user has chosen non-voice operation.

## System Tray Icon and Context Menu

### Delivered bounded R03/R09 tray recovery

The native tray now displays a generic, non-sensitive input status: closed,
consent not granted, access denied/unknown, selected endpoint unavailable,
push-to-talk ready (microphone closed; wake unavailable), or actual PTT capture.
Tooltip/status text contains no endpoint IDs/names, transcript, task or account
content. Microphone menu labels expose friendly endpoint names only while
ownership and Windows presentation gates are eligible; native radio marks
represent the saved preference, never recording.

Opening the menu or choosing **Refresh microphones** performs metadata-only
enumeration/permission/privacy observation on one background worker, with a
five-second caller deadline. Concurrent refreshes share the current request.
Native enumeration cannot be forcibly killed: after timeout/cancellation,
there is at most one outstanding worker, and late results cannot republish
the menu or restore enablement. Failures close input and report explicit
Refresh/Settings recovery, not a substituted endpoint or success indicator.

**System** follows the Windows multimedia default and explicitly shows
availability; an unavailable saved pin remains visible/marked and is not
selectable as an available endpoint. An explicit System choice can remove an
unavailable pin even when no default exists, without opening capture.
Selection is bound to the displayed topology/configuration revision and
revalidates bounded metadata plus current ownership/privacy before using the
existing audited preference store. Saving failure retains the prior preference.
Changed selection invalidates/releases input and requires explicit enablement.

**Listening controls > Enable listening** revalidates the displayed revision
and existing consent/readiness/session/call gates. It only arms PTT; no capture
or wake is started. **Disable listening** is an explicit idempotent close,
not a stale toggle, and remains usable while other work is busy. **Stop
speaking** calls the existing playback stop service and changes no input
selection, consent or task state. **Voice consent / push-to-talk** opens the
existing Settings recovery surface. Delayed native callbacks are retired on
disposal. All prior show/single-click/double-click, Settings, Sessions,
maintenance, documentation, skill inspection, evidence and exit routes remain.

This is a bounded delivery, not completion of the recommended menu or full
R03/R09. No microphone test, ambient wake, automatic selection, new consent
flow, generic device-question architecture or new OS side effect is added.
Explorer restart/tray failure, screen-reader/overflow/native Windows races,
hardware/acoustic/closure latency and full matrix acceptance remain open.

The Windows notification-area icon is the primary non-voice entry point, present for the lifetime of the normal desktop instance, even when the presence/details are hidden.
It is not optional while Kora is running; Windows may place it in the notification-area overflow and Kora must not claim it can force taskbar pinning.
Voice is a convenient primary path, not compulsory; the tray is deterministic host functionality requiring no model or working audio device.
A single left-click shows and activates the existing Kora window. A
double-click opens or activates the single Settings window. Because Windows
reports the first click before it knows whether a second will follow, the
single-click action waits for the configured Windows double-click interval;
the second click cancels that pending show action.

Remaining target context menu (not an implemented capability list):

```text
Kora - Voice input unavailable       [read-only actual state]
Show Kora
Show questions
Microphone >
    Headset microphone              [selected preference; availability]
    USB microphone                  [available]
    Laptop microphone               [system default; available]
    Refresh devices
    Choose/test microphone...
Enable listening
Mute Kora
Stop speaking
Work and queue...
Sessions and history...
Settings...
Check for updates
Exit Kora
```

Friendly names above are illustrative; populate from validated endpoint enumeration.
Use a native selection mark for the saved microphone preference and separate text for effective availability/listening, so selected never falsely means capturing.
Opening the menu refreshes bounded endpoint metadata without recording.
Keep unavailable selected devices visible with the reason; do not silently remove the preference or select a replacement.

Selecting a device saves the endpoint choice after revision validation but never implicitly enables capture.
If another endpoint is currently capturing, selecting a replacement closes that capture, invalidates its generation, and exposes Enable listening for the new endpoint.
"Choose/test microphone..." opens the native question card with richer details, permission help, explicit test consent, and the combined selection/enable action.
"Enable listening" is an explicit user request for the current displayed endpoint; it follows the same consent/readiness/session checks as onboarding.
If blocked, explain the reason and open recovery/help rather than leaving a disabled control as the only route.
"Mute Kora" follows the existing host intent: close input, stop speech, and clear audio; it does not cancel task execution.
"Stop speaking" affects only playback. Device selection, mute, enable, and refresh never silently approve queued work.

The delayed single-click Show Kora action activates the compact status/questions surface; Sessions and history opens the list-plus-conversation workspace, and right-click opens this menu.
These routes follow [Coordinated Window Design](UI_Workspace_And_Windows.md); they do not open a new window per running session.
Tray activation never toggles recording as an implicit side effect.
All actions share existing host services and lifecycle/approval rules; Exit Kora still confirms affected work, and Check for updates installs nothing.
Expose accessible icon/menu names and textual state, not colour alone; omit sensitive task/account/content details from the tooltip.
During locked/disconnected/unknown session state, sensitive menus/prompts are hidden and capture/approval actions are denied, including late clicks.

Re-register the icon after Explorer/notification-area restart without restarting Kora or reopening capture.
Retain an accessible standard window/launcher route that reveals the existing single instance if the tray API fails or is unavailable.
Report tray failure visibly where possible; a missing tray icon must not strand a hidden application behind a voice-only recovery requirement.
If neither tray nor native window can expose trustworthy microphone state, close capture and require explicit re-enabling after the visible recovery route is restored.

Closing a prompt is distinct from exiting Kora, cancelling a task, denying an action, marking a session Done, or deleting it; label those choices explicitly.
Session list/switch/new/Done/delete and pending question controls are reachable without voice, with the same lifecycle validation and exact confirmation rules.
Failures include actionable information and truthful state, not a success indicator or a silent device fallback.
Voice readiness must not prevent local settings, task management, basic output, or setup; model-dependent answering remains unavailable when its provider is not ready.

## Release Gate

Test first launch with zero/one/multiple endpoints, duplicate names, absent speech assets, privacy denial, intentionally muted input, and no model/network/TTS.
Complete microphone selection and all confirmations using only the mouse.
Test device removal/replacement/default change during wake listening, command capture, transcription, TTS, prompt selection, and blocked device open.
Verify no capture before consent and none after lock/cancel/topology invalidation, including delayed callbacks and successful late opens.
Reconnection cannot automatically record; selected endpoint identity and saved/effective configuration remain distinct.
Generic open failures do not become invented "device busy" diagnoses.
Mouse/voice answers share prompt IDs, expiry, validation, and exactly-once commit; recovery cannot approve unrelated work.
Hidden presence/caption/voice-only settings do not strand the user; tray recovery and accessible controls remain available.
Test native tray/menu device selection and Enable listening with no usable microphone, including notification-area overflow, Explorer restart, stale menu selection, lock races, and tray API failure.
