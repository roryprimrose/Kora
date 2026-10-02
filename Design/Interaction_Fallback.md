# Mouse-Based Questions and Voice Readiness Recovery

Status: required host capability from Slice A. Voice-first must not mean voice-only.

Related: [Environment Setup](Environment_Setup.md), [Task Lifecycle](Task_Lifecycle.md), [User Configuration](User_Configuration.md), [Information Display](Information_Display.md).

## Product Requirement

Kora can ask questions and receive mouse input when no microphone is configured, permitted, connected, or usable.
This also applies when speech recognition/wake assets are unavailable or the user intentionally closes the microphone.
The fallback is always present in the native shell; it needs no model, skill, network, working microphone, or TTS engine.

All questions appear as readable native prompt cards with explicit submit/cancel controls and selectable typed answers where possible.
Speech is an optional additional delivery channel when existing output consent/policy permits, not a prerequisite for answering.
Hidden speech text, visual detail preferences, or an unavailable rich renderer cannot hide these controls.
Provide equivalent keyboard/screen-reader interaction; mouse use never requires a spoken acknowledgement.

## First Launch: Select a Microphone

1. Open accessible first-run onboarding, even when the normal configured startup presentation is tray/minimal.
2. Enumerate audio capture endpoints without recording or probing audio before consent.
3. Ask visibly: "Which microphone should I use to listen for instructions?"
4. Show each detected endpoint with friendly name, stable host identity, current availability, and a default-device badge where applicable.
5. The user clicks a device. Selection alone saves/proposes the preference; it does not start capture.
6. Explain local configured-name detection ("Kora" initially), bounded ambient audio, command transcription, visible listening status, and mandatory locked-session denial.
7. Offer "Use selected microphone and enable listening", an explicitly consented "Test selected microphone", and "Continue without voice".
8. On explicit enable, revalidate session/endpoint/permissions/assets and attempt capture; confirm readiness only after the real checks succeed.
9. If prerequisites are missing or opening fails, show the exact blocker and next actions; remain fully usable through mouse-based prompts.

Even a single detected microphone needs explicit selection/enablement; do not silently open it.
The system default may be suggested, not assumed or captured automatically.
Offer [Activation Name](Activation_Name.md) selection before ongoing listening, including a custom-only recommendation for shared offices; this is available by mouse before voice is ready.
Distinguish duplicate friendly names with device details; bind operations to an enumerated endpoint ID, never the displayed label alone.
A system-default change cannot silently switch capture to another endpoint; using the new default requires explicit confirmation of the new endpoint.

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

Use the same prompt identity, validation, scope, and expiry for voice and visual responses.
Examples include microphone/provider selection, setup plans, task clarification, queue choices, skill revision review, update proposals, and permitted action approval.
Prefer selectable typed choices and source-qualified device/task/skill labels over forcing text entry.
Show the selected answer and its consequences; Apply/Confirm commits only the current valid proposal.

For genuinely free-form answers, provide a normal text field with an optional OS on-screen keyboard route and explicit "Use clipboard as my answer".
Neither typing nor clipboard use is mandatory for the microphone-selection workflow.
Clipboard input is an explicitly requested bounded snapshot with provenance and normal context/egress review, not silent polling or automatic submission.
Passwords/tokens remain in secure authentication flows, never model-visible prompt fields or clipboard-answer shortcuts.

Only one foreground response-eligible prompt exists, as in task lifecycle.
Device recovery is a separate host card; it cannot replace an action approval and let an old Confirm click apply to a new question.
Changing prompt focus withdraws prior reply eligibility; show/revalidate it again before accepting a response.
Double clicks, delayed events, stale devices/configuration revisions, expired grants, and lock races produce no duplicate action.
An expired prompt shows Expired and a request to re-present/revalidate, never a silent lifetime extension.
Changing input channel does not extend deadlines or relax high-risk visual confirmation.

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

The Windows notification-area icon is the primary non-voice entry point, present for the lifetime of the normal desktop instance, even when the presence/details are hidden.
It is not optional while Kora is running; Windows may place it in the notification-area overflow and Kora must not claim it can force taskbar pinning.
Voice remains the primary normal interaction; the tray is deterministic host functionality requiring no model or working audio device.

Recommended context menu:

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

Left-clicking the icon opens the native status/questions panel; right-click opens this menu.
Tray activation never toggles recording as an implicit side effect.
All actions share existing host services and lifecycle/approval rules; Exit Kora still confirms affected work, and Check for updates installs nothing.
Expose accessible icon/menu names and textual state, not colour alone; omit sensitive task/account/content details from the tooltip.
During locked/disconnected/unknown session state, sensitive menus/prompts are hidden and capture/approval actions are denied, including late clicks.

Re-register the icon after Explorer/notification-area restart without restarting Kora or reopening capture.
Retain an accessible standard window/launcher route that reveals the existing single instance if the tray API fails or is unavailable.
Report tray failure visibly where possible; a missing tray icon must not strand a hidden application behind a voice-only recovery requirement.
If neither tray nor native window can expose trustworthy microphone state, close capture and require explicit re-enabling after the visible recovery route is restored.

Closing a prompt is distinct from exiting Kora, cancelling a task, or denying a pending action; label those choices explicitly.
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
