# Task Lifecycle and Recovery

Status: proposed full task lifecycle. The current bootstrap implements setup task progress
and registered command/model routing, not this complete execution queue or
script-backed skill lifecycle.

Related: [Architecture](Architecture.md), [Security and Data Flows](Security_Data_Flows.md), [Acceptance Criteria](Acceptance_Criteria.md).

## State Model

Assistant startup and release/debug takeover obey [Instance Coordination](Instance_Coordination.md): one exclusive active owner, explicit quiescent transfer, and no task/grant/listening replay on return.

The revised MVP permits bounded concurrent Kora sessions, one executing task per session, plus per-session pending work and an independently responsive management lane.
Session Active/Done lifecycle and persistence are separate from task outcomes; see [Human Interaction and Persistent Sessions](Interaction_And_Sessions.md).
Requests enter a ledger as Queued or Needs Clarification before dispatch to Preparing.
Voice capture/transcription feeds management independently of the active task's state.
Voice capture and playback have their own states; stopping playback does not necessarily stop task execution.
Task Idle does not mean the microphone is closed: a separate voice state can be Wake Listening after consent.

```text
Queued -> Preparing
            |
            +-> Awaiting Clarification -> Preparing
            +-> Awaiting Approval -> Preparing / Running
            `-> Running
                  |
                  +-> Awaiting Approval -> Running
                  +-> Completed
                  +-> Failed
                  `-> Cancelling -> Cancelled / Outcome Unknown
```

Any non-terminal state can fail or receive cancellation.
An approval denial cancels the pending action; the task can continue only if a safe alternative is explicitly selected.
There is no implied retry with another destination.

Speech output moves independently through queued, speaking, stopped, and finished states.
Partial visual output may appear during Running and remains labelled incomplete until terminal success.
Queueing, contextual routing, dispatch, and status semantics are defined in [Work Management](Work_Management.md).

## Request Interpretation and Tool Results

[Commands, Tools, Skills, and Model Interaction](Commands_Tools_And_Skills.md)
defines the shared voice/typed app -> model -> app path.
Preparing resolves relevant tool definitions and enabled pinned skill summaries.
A skill selection resolves a workflow, not an approval or an independent script
dispatch. Every tool/task proposal reaches host validation and its applicable
grant gate before Running.
Awaiting Clarification supplies missing intent/inputs; it never satisfies
Awaiting Approval. Approval replies stay bound to the host proposal.

Running records correlated structured observations/receipts and returns
approved bounded results to the task runtime for continued reasoning.
The final model answer is presentation, not evidence of a completed effect.
Denied/cancelled/failed/unknown results remain explicit; cancellation prevents
late calls and responses from resuming the task.
Exact local commands can query or control the same host services without
inference; effect-specific approval still applies.

## Wake Listening and Command Capture

Voice states are Muted, Wake Listening, Awaiting Conversational Reply,
Capturing Command/Reply, Transcribing, Session Locked, and Unavailable.
Wake Listening is the primary ready state after explicit setup consent; push-to-talk is optional.
The [microphone consent and enablement matrix](Security_Data_Flows.md#microphone-consent-and-enablement-matrix)
governs first launch, ordinary restart, unlock/resume and failure recovery.
Saved ongoing consent is not transient enablement: consent withdrawal persists,
while manual disablement and recovery holds last for the run.
If speech/wake dependencies are missing, the host reports Unavailable and opens [Environment Setup](Environment_Setup.md), rather than claiming listening readiness or falling back to cloud capture.
No configured/usable microphone opens a native device-choice question with mouse answers under [Interaction Fallback](Interaction_Fallback.md).
Endpoint loss invalidates capture/transcript generations and offers recovery without cancelling unrelated task work; reconnection never silently restores capture.

- Say a currently active name ("Kora" by default) to activate, then speak the command either immediately or after the activation cue.
- Only the local wake detector processes ambient audio. A rolling pre-roll of at most 2 seconds remains in memory and is overwritten continuously.
- On activation, preserve trailing command audio from the buffer; exclude the wake word and unrelated pre-activation audio from transcription.
- Provide a visible capture transition and a short non-speech cue that does not clip the command or re-trigger detection.
- Wait up to 5 seconds for command speech, then abandon an empty activation without calling the runtime or reading context.
- End a spoken command after 1 second of trailing silence, or at the 60-second capture limit.
- Unsolicited requests, late follow-ups after reply eligibility expires, and
  speech with no unique host question start with the current active name or
  optional push-to-talk.
- After Kora speaks a unique host-owned foreground question, it may enter
  Awaiting Conversational Reply for the configured 15-second default. During
  that bounded turn, schema-valid replies omit the name. One accepted answer,
  timeout, cancellation, target change, or gate loss closes capture; another
  host question creates a new turn rather than extending the old microphone.
- During running tasks, local controls are handled immediately. The independent manager routes other requests to the explicitly addressed/clearly related Active session or a new session, while the host scheduler enforces concurrent-slot and resource limits.
- During TTS, keep local wake detection active with playback echo rejection. User activation stops TTS and captures the command; it does not by itself cancel the task.
- When Windows exposes a supported system-output/loopback reference, correlate all device playback, not only Kora TTS, and reject commands attributable to local media or conference output. If that proof is unavailable, disclose the limitation and prefer headset/PTT for disruptive controls.
- If playback rejection cannot be established, suspend TTS and explain why; wake activation remains available with visual output.
- After capture/transcription, return to Wake Listening if consent is still active, independently of task execution or approval waits.
- A mute control closes microphone capture and clears buffered audio. Voice cannot unmute a closed microphone; use the explicit UI/control.
- Lock, sign-out, or suspend closes the microphone and clears audio buffers. Unlock/resume requires explicit re-enabling, not silent listening.
- Windows Locked/Disconnected/Unknown state overrides all activation requests; locked-session microphone policy is independent of the skill that requested a lock.

Wake detection is not identity verification or permission to execute an action.
The [accepted trust boundary](Security_Data_Flows.md#trust-boundaries) does not require authenticating every speaker or disabling consequential voice merely because system-output correlation is unavailable.
Optional learning uses only separately consented new activated-command samples under [its local profile contract](Security_Data_Flows.md#optional-local-frequent-speaker-learning), never ambient wake audio or archived conversations.
Rename/alias-mode changes follow [Custom Activation Names](Activation_Name.md): validate the whole active set, commit a new audio generation atomically, and invalidate removed-name callbacks without changing consent.
Optional local speaker verification is only a profile-owner confidence signal for privacy decisions; it never supplies an approval or action grant.
False activations with no valid command end locally; recognised commands still pass normal context and action controls.
Conversational reply audio follows the same memory-only transcription
lifecycle as activated command audio and is bound to the exact host question
generation. It is never general ambient transcription or authority to route a
new request.
When owner-aware private speech is enabled, sensitive status and context use a
neutral visual notice for `Uncertain`, `NotOwner`, or `Unavailable` confidence,
rather than spoken disclosure. Absent/disabled protection leaves
baseline voice under normal content/output/call policy; missing verification
alone never adds a mandatory speaker check.
Safety-preserving stop-speech, mute, pause-dispatch, and lock controls remain available without owner matching.
Lock intentionally prioritises confidentiality over availability and remains
callable without speaker authentication, but under the proposed common
execution gate it still needs action-specific approval. Priority dispatch
does not bypass the gate. Today exact direct lock is ungated and
model-suggested lock requests approval; media-loopback rejection, availability
disclosure, and false-activation tests also matter.

## Voice Controls

| Intent | Behaviour |
|---|---|
| "Stop speaking" | Stop TTS and clear its playback queue; task continues |
| "Cancel task" | Pause the identified task/tool dispatch immediately; exact affected-work voice or UI confirmation completes cancellation/revocation |
| "Lock the machine" | Proposed: invoke the fixed bundled lock skill through the common action-specific gate as a priority session control; enforce microphone shutdown on the actual Windows lock event. Today exact direct lock calls the Windows API without that gate |
| "Shut down the computer" / "Restart the computer" | Open a fixed local power proposal; require matching action-specific voice or UI confirmation, safe handling of all sessions, mandatory OS checks, and a cancellable host countdown |
| "Hide Kora" / "Show Kora" | Change presentation only; do not mute, exit, or alter active work |
| "Exit Kora" / "Restart Kora" | Graceful app lifecycle action; explicitly confirm affected pending/active work, never substitute computer restart |
| "What are you currently working on?" | Report the active task, observed stage, and blocker without interrupting execution |
| "What do you have left to do?" | Report known remaining active steps and queued work; explicitly identify unknowns |
| "Do that next" | Contextually move the uniquely identified pending task next; clarify ambiguous targets |
| "Cancel the queued documentation update" | Remove the identified pending entry; do not cancel unrelated work |
| "Clear the queue" | Pause the addressed dispatch and present affected entries; require exact voice or UI confirmation before removing dispatch eligibility, not history |
| "Resume the queue" | Resume paused dispatch after revalidation and any required uncertainty decision |
| "Pause the queue" | Stop new dispatch while the current task continues |
| "Stop all work" | Immediately pause dispatch across sessions, then require exact voice or UI confirmation before cancellation and clearing dispatch entries |
| "Stop" | Stop TTS immediately and pause new dispatch; destructive task cancellation requires the separate confirmed cancel flow |
| "Repeat the summary" | Replay the current safe summary without rerunning tools |
| "Use the clipboard" | Capture a new snapshot; do not reuse an old snapshot silently |
| "Explain the next item" | Navigate the existing result without rerunning the original task |
| "This session is done" | Resolve live work explicitly, then archive with history retained and session grants revoked |
| "Delete this session" / "Clear this conversation" | Pause its work, preview the exact retained data, and confirm by voice or UI before lifecycle-safe deletion |

Equivalent visual controls are always available.
Questions/clarifications accept mouse-based typed choices under the same prompt identity, expiry, validation, and approval rules, including when voice input is unavailable.
The intents above are spoken after a current active name ("Kora" by default, for example "Kora, stop") or optional push-to-talk.
See [OOTB Phrase Catalogue](OOTB_Phrases.md) for aliases, confirmation phrases, application lifecycle, and maintenance commands.
Push-to-talk during TTS stops playback before capturing a new utterance.
Wake-triggered interruption during TTS is included from Slice A; general wake-word-free barge-in is not.
Windows session lock is a narrowly host-admitted lifecycle control, not another general task executor.
See [Bundled Skills](Built_In_Skills.md) for script identity, error handling, and locked-session behaviour.
Its [standalone binding rule](Built_In_Skills.md#standalone-lock-work-session-binding)
creates and commits a new control work session without routing inference;
Session approval needs that Active identity, never a selected-window shortcut.
For [power proposals](Security_Data_Flows.md#management-power-proposal-authority),
M/E submit intent only; the host owns all-session review, approval, countdown
and fixed-action dispatch through the admitted gateway/worker.
Kora may also initiate eligible speech without a user utterance; see [Proactive Interaction](Proactive_Interaction.md).
Unsolicited responses require a current active name or optional PTT.
Schema-valid replies during a live host-opened conversational turn are the
bounded exception and route only to that trusted prompt, never an unbounded
listening window.
Every speech path applies [Call-Aware Speech](Call_Aware_Speech.md), including requested answers and audible activation cues.
The user can configure the call policy verbally or request one identified response aloud; visual fallback remains available while gated.

## Transcription and Clarification

- Show recognised text before actions and expose a voice correction path.
- Do not rely on a confidence score that the selected engine cannot actually supply.
- If an action target, quantity, destination, or approval is ambiguous, ask for clarification.
- Read back the resolved high-impact action and show its exact parameters.
- Background audio or uncertain "yes" must not satisfy an unrelated approval.
- Optional speaker confidence can enforce an explicitly selected spoken-approval/private-output preference, but matching alone is not approval or strong authentication.
- Voice approvals address the presented foreground proposal; explicit UI responses address their visible valid card. Both bind the same session/task/proposal revision and expiry.

Routine non-action answers need not add a confirmation prompt after every utterance.
The primary clipboard flow already contains a context/destination review when remote processing is required.

## Defaults and Bounds

These are initial proposed defaults and must remain visible/configurable where appropriate:
Supported user-adjustable ranges and verbal operations are defined in [User Configuration](User_Configuration.md); fixed safety controls remain non-overridable.

| Limit | Default | Behaviour at limit |
|---|---|---|
| Ambient wake pre-roll | 2 seconds | Overwrite oldest audio; never persist or transmit it |
| Speech start after activation | 5 seconds | Abandon empty activation locally and return to Wake Listening |
| Command endpoint | 1 second trailing silence | Stop capture and transcribe the activated command |
| Activated/PTT utterance | 60 seconds | Stop capture, notify user, transcribe captured audio |
| Plain-text clipboard snapshot | 256 KiB UTF-8 | Reject with size and options; no silent truncation |
| Individual model-bound tool result | 64 KiB UTF-8 | Return bounded excerpt/reference and mark truncation explicitly |
| Active task | 5 minutes excluding user approval/clarification waits | Cancel work and report deadline |
| Approval/clarification wait | 2 minutes | Expire request and cancel pending action |
| Tool invocation | 60 seconds unless registered otherwise | Cancel invocation; classify side-effect certainty |
| Local worker cancellation grace | 5 seconds | Terminate its tracked process tree if still running |
| Spoken summary | At most 3 sentences and 80 words | Offer voice navigation for details |
| Pending request queue | 10 entries, configurable | Explain capacity and ask which work to defer/remove; no silent eviction |
| Pending request lifetime | 30 minutes | Expire execution eligibility, notify user, release active context references; retain history |
| Session automatic archive | 24 inactive hours; configurable | Mark Done only after live/uncertain work is safely resolved; preserve history |
| Session automatic deletion | 30 inactive days; configurable | Lifecycle-safe removal of session content/artifacts/indexes; passive browsing does not reset activity |
| Concurrent session tasks | Proposed 2; configurable within verified limits | Queue fairly and expose provider/resource blockers; do not exceed the admitted budget |
| Management inference | 15 seconds | Report timeout and offer explicit queue/replace clarification; local controls remain available |

Context packing respects the selected runtime's actual limits.
Required input that will not fit causes an explicit error or a user-approved reduction; it is not silently dropped.
When a referenced result is unavailable to a runtime, Kora offers a permitted bounded selection rather than an inaccessible reference.

## Cancellation Semantics

Cancellation:

1. Marks the task cancelling and blocks new invocations.
2. Revokes unused approval tokens.
3. Clears the task's command audio, stops its own playback where active, and requests its runtime/tool cancellation without stopping another session's work.
4. Stops tracked local workers after their grace period.
5. Suppresses late output from changing the visible final state.
6. Records action receipts and any uncertain side effects in the originating session; other sessions' speech/work are not implicitly cancelled.

Task cancellation clears command audio but does not revoke wake-listening consent; return to Wake Listening unless muted, locked, or unavailable.
Stopping all microphone listening is a separate explicit mute operation.
Cancelling active work preserves the queue but pauses automatic dispatch; cancellation does not immediately start the next request.
Replacement waits for execution quiescence and resolution or explicit acknowledgement of uncertain remote effects.

Cancellation cannot undo a completed write or prove a remote operation never ran.
If a remote service acknowledges only request cancellation, Kora reports that remote completion may still occur.
Late receipts can refine the action record without turning a cancelled task into a successful one.

R02 observed a non-cooperative admitted tool completing after SDK abort
acknowledgement. Block new dispatch and provider egress for the cancelled
generation; quarantine uncertain inference and retain Outcome Unknown until
receipt/reconciliation. See the [runtime continuation](Runtime_Provider_Feasibility.md)
and [management envelope](Work_Management.md#management-operating-envelope-and-degraded-mode).

## Failure and Retry Rules

| Failure | Required response |
|---|---|
| Microphone unavailable | Show device error; offer device selection; do not switch to cloud capture |
| Wake detector unavailable | Show verbal activation as unavailable and offer repair/setup; optional PTT remains usable but does not satisfy the Slice A release gate |
| Playback echo rejection unavailable | Suspend spoken output, explain the device limitation, and retain wake activation and visual responses |
| Clipboard unavailable/empty | Explain capture result; offer retry after user changes source |
| Speech/model runtime missing | Show setup requirement and affected capabilities |
| Authentication expired | Suspend affected call; use supported sign-in flow |
| Context transmission denied | Keep content local; offer a compatible local runtime or cancel |
| Tool denied or schema changed | Do not invoke; explain the denied scope or required review |
| Runtime disconnected | Retain incomplete output with failure label; no silent provider switch |
| Write target changed after approval | Reject stale action and regenerate/review proposal |
| Tool timeout after possible write | Mark outcome unknown and offer read-only reconciliation |
| Worker crash | Fail its invocation, contain UI impact, show diagnostic reference |

At most one automatic retry is allowed for a classified transient read-only operation, within the original deadline and unchanged permissions.
Management inference is a stricter exception: no automatic retry is allowed;
the host final request gate must block SDK retry attempts independently of
error hooks under [Work Management](Work_Management.md#management-operating-envelope-and-degraded-mode).
Do not automatically retry writes, process executions, or requests with unknown outcomes.
A write may be retried only after reconciliation and a fresh user decision, unless the tool has a tested idempotency contract.

## Builder Lifecycle

The MVP Builder creates and improves declarative user skills by voice; it never updates Kora's code.
See [Skill Authoring](Skill_Authoring.md).

1. Clarify the voice request and registered tool dependencies.
2. Read the selected user skill revision, or begin a new staged package.
3. Generate/refine a proposal and show its diff with a spoken summary.
4. Run built-in structural/dependency validation and simulated examples.
5. Approve the exact revision and revalidate the unchanged base and safe destination.
6. Save through the bounded skill-store writer; track partial application explicitly.
7. Separately review and enable the validated revision for future invocations.
8. Admit any real trial as its own task under ordinary permissions.

If validation fails, retain the proposal and failure result; do not save or enable it.
Rollback is another write and must not overwrite unrelated edits.
Editing a user skill does not activate it automatically; activation requires dependency/policy validation.
The MVP never launches a build, writes application code, or modifies tool implementation/configuration to satisfy a skill dependency.
Future external repository build/test, commit, or push capabilities each require separate approvals and application-integrity enforcement; none may target Kora.

## Restart and Recovery

Restart does not resume actions or replay approval tokens.
Session history, artifacts, requests, and content-bearing ledger evidence are restored for reading on restart, not automatically dispatched.
An app restart applies the ordinary automatic listening policy after fresh
readiness checks and saved ongoing consent under the
[microphone matrix](Security_Data_Flows.md#microphone-consent-and-enablement-matrix);
persisted device preferences select the route but do not bypass ownership,
session, permission, device, asset, or call-policy gates.
Persisted task metadata marks interrupted non-terminal tasks as interrupted, with unresolved side effects where applicable.
Permitted retained conversation/context snapshots remain available under session retention and current source access, but reuse requires freshness, policy, and egress review.
Offer read-only reconciliation for a persisted remote operation ID when the connector supports it.

Recovery must never manufacture a successful result from missing evidence.
