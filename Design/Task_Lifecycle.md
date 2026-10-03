# Task Lifecycle and Recovery

Status: proposed.

Related: [Architecture](Architecture.md), [Security and Data Flows](Security_Data_Flows.md), [Acceptance Criteria](Acceptance_Criteria.md).

## State Model

Assistant startup and release/debug takeover obey [Instance Coordination](Instance_Coordination.md): one exclusive active owner, explicit quiescent transfer, and no task/grant/listening replay on return.

The MVP permits one task in the execution slot, plus pending work and a concurrently responsive management lane.
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

## Wake Listening and Command Capture

Voice states are Muted, Wake Listening, Capturing Command, Transcribing, Session Locked, and Unavailable.
Wake Listening is the primary ready state after explicit setup consent; push-to-talk is optional.
If speech/wake dependencies are missing, the host reports Unavailable and opens [Environment Setup](Environment_Setup.md), rather than claiming listening readiness or falling back to cloud capture.
No configured/usable microphone opens a native device-choice question with mouse answers under [Interaction Fallback](Interaction_Fallback.md).
Endpoint loss invalidates capture/transcript generations and offers recovery without cancelling unrelated task work; reconnection never silently restores capture.

- Say a currently active name ("Kora" by default) to activate, then speak the command either immediately or after the activation cue.
- Only the local wake detector processes ambient audio. A rolling pre-roll of at most 2 seconds remains in memory and is overwritten continuously.
- On activation, preserve trailing command audio from the buffer; exclude the wake word and unrelated pre-activation audio from transcription.
- Provide a visible capture transition and a short non-speech cue that does not clip the command or re-trigger detection.
- Wait up to 5 seconds for command speech, then abandon an empty activation without calling the runtime or reading context.
- End a spoken command after 1 second of trailing silence, or at the 60-second capture limit.
- Each follow-up, correction, or low-risk voice approval starts with a current active name or optional push-to-talk; there is no unbounded open conversation microphone.
- During a running task, local control commands are handled immediately. The independent manager interprets other requests in context, updates the queue when intent is clear, and asks when ambiguous; it never starts a second task executor.
- During TTS, keep local wake detection active with playback echo rejection. User activation stops TTS and captures the command; it does not by itself cancel the task.
- When Windows exposes a supported system-output/loopback reference, correlate all device playback, not only Kora TTS, and reject commands attributable to local media or conference output. If that proof is unavailable, disclose the limitation and prefer headset/PTT for disruptive controls.
- If playback rejection cannot be established, suspend TTS and explain why; wake activation remains available with visual output.
- After capture/transcription, return to Wake Listening if consent is still active, independently of task execution or approval waits.
- A mute control closes microphone capture and clears buffered audio. Voice cannot unmute a closed microphone; use the explicit UI/control.
- Lock, sign-out, or suspend closes the microphone and clears audio buffers. Unlock/resume requires explicit re-enabling, not silent listening.
- Windows Locked/Disconnected/Unknown state overrides all activation requests; locked-session microphone policy is independent of the skill that requested a lock.

Wake detection is not identity verification or permission to execute an action.
Rename/alias-mode changes follow [Custom Activation Names](Activation_Name.md): validate the whole active set, commit a new audio generation atomically, and invalidate removed-name callbacks without changing consent.
Optional local speaker verification is only a profile-owner confidence signal for privacy decisions; it never supplies an approval or action grant.
False activations with no valid command end locally; recognised commands still pass normal context and action controls.
When the speaker is not `LikelyOwner` or verification is unavailable, sensitive status and context default to a neutral visual notice rather than spoken disclosure.
Safety-preserving stop-speech, mute, pause-dispatch, and lock controls remain available without owner matching.
Lock intentionally prioritises confidentiality over availability and therefore remains callable without owner authentication; media-loopback rejection, explicit availability disclosure, and false-activation tests mitigate but cannot eliminate acoustic nuisance locking.

## Voice Controls

| Intent | Behaviour |
|---|---|
| "Stop speaking" | Stop TTS and clear its playback queue; task continues |
| "Cancel task" | Pause new task/tool dispatch immediately; require owner presence and native confirmation before destructive cancellation/revocation |
| "Lock the machine" | Invoke the fixed bundled lock skill as a priority session control; enforce microphone shutdown on the actual Windows lock event |
| "Shut down the computer" / "Restart the computer" | Open a fixed local power proposal; require matching named confirmation, safe work handling, a native secure confirmation outside the speech/model path, and a cancellable host countdown |
| "Hide Kora" / "Show Kora" | Change presentation only; do not mute, exit, or alter active work |
| "Exit Kora" / "Restart Kora" | Graceful app lifecycle action; explicitly confirm affected pending/active work, never substitute computer restart |
| "What are you currently working on?" | Report the active task, observed stage, and blocker without interrupting execution |
| "What do you have left to do?" | Report known remaining active steps and queued work; explicitly identify unknowns |
| "Do that next" | Contextually move the uniquely identified pending task next; clarify ambiguous targets |
| "Cancel the queued documentation update" | Remove the identified pending entry; do not cancel unrelated work |
| "Clear the queue" | Pause dispatch and present the affected entries; require owner presence and native confirmation before removal |
| "Resume the queue" | Resume paused dispatch after revalidation and any required uncertainty decision |
| "Pause the queue" | Stop new dispatch while the current task continues |
| "Stop all work" | Immediately pause new dispatch, then require owner presence and native confirmation before cancelling active execution and clearing pending entries |
| "Stop" | Stop TTS immediately and pause new dispatch; destructive task cancellation requires the separate confirmed cancel flow |
| "Repeat the summary" | Replay the current safe summary without rerunning tools |
| "Use the clipboard" | Capture a new snapshot; do not reuse an old snapshot silently |
| "Explain the next item" | Navigate the existing result without rerunning the original task |
| "Clear this conversation" | Pause its work and show affected state; require owner presence and native confirmation before cancellation, queue removal, and context deletion |

Equivalent visual controls are always available.
Questions/clarifications accept mouse-based typed choices under the same prompt identity, expiry, validation, and approval rules, including when voice input is unavailable.
The intents above are spoken after a current active name ("Kora" by default, for example "Kora, stop") or optional push-to-talk.
See [OOTB Phrase Catalogue](OOTB_Phrases.md) for aliases, confirmation phrases, application lifecycle, and maintenance commands.
Push-to-talk during TTS stops playback before capturing a new utterance.
Wake-triggered interruption during TTS is included from Slice A; general wake-word-free barge-in is not.
Session lock is a narrowly host-admitted lifecycle control, not permission for a parallel general task executor.
See [Bundled Skills](Built_In_Skills.md) for script identity, error handling, and locked-session behaviour.
Kora may also initiate eligible speech without a user utterance; see [Proactive Interaction](Proactive_Interaction.md).
Responses still require a current active name or optional PTT and are routed to the current trusted prompt, not an unbounded listening window.
Every speech path applies [Call-Aware Speech](Call_Aware_Speech.md), including requested answers and audible activation cues.
The user can configure the call policy verbally or request one identified response aloud; visual fallback remains available while gated.

## Transcription and Clarification

- Show recognised text before actions and expose a voice correction path.
- Do not rely on a confidence score that the selected engine cannot actually supply.
- If an action target, quantity, destination, or approval is ambiguous, ask for clarification.
- Read back the resolved high-impact action and show its exact parameters.
- Background audio or uncertain "yes" must not satisfy an unrelated approval.
- Speaker confidence may suppress private speech or trigger native confirmation, but cannot make an otherwise insufficient voice approval sufficient.
- Approval responses are accepted only for the currently displayed, unexpired request.

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
| Pending request lifetime | 30 minutes | Expire entry, notify user, release content references |
| Management inference | 15 seconds | Report timeout and offer explicit queue/replace clarification; local controls remain available |

Context packing respects the selected runtime's actual limits.
Required input that will not fit causes an explicit error or a user-approved reduction; it is not silently dropped.
When a referenced result is unavailable to a runtime, Kora offers a permitted bounded selection rather than an inaccessible reference.

## Cancellation Semantics

Cancellation:

1. Marks the task cancelling and blocks new invocations.
2. Revokes unused approval tokens.
3. Stops audio capture/playback and requests runtime/tool cancellation.
4. Stops tracked local workers after their grace period.
5. Suppresses late output from changing the visible final state.
6. Records action receipts and any uncertain side effects.

Task cancellation clears command audio but does not revoke wake-listening consent; return to Wake Listening unless muted, locked, or unavailable.
Stopping all microphone listening is a separate explicit mute operation.
Cancelling active work preserves the queue but pauses automatic dispatch; cancellation does not immediately start the next request.
Replacement waits for execution quiescence and resolution or explicit acknowledgement of uncertain remote effects.

Cancellation cannot undo a completed write or prove a remote operation never ran.
If a remote service acknowledges only request cancellation, Kora reports that remote completion may still occur.
Late receipts can refine the action record without turning a cancelled task into a successful one.

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
Pending queue bodies and content-bearing ledger records are memory-only and are not restored on restart.
An app restart applies the ordinary automatic listening policy after fresh
readiness checks; persisted device preferences select the route but do not bypass
session, device, or call-policy gates.
Persisted task metadata marks interrupted non-terminal tasks as interrupted, with unresolved side effects where applicable.
No raw conversation or clipboard content is restored under the default retention policy.
Offer read-only reconciliation for a persisted remote operation ID when the connector supports it.

Recovery must never manufacture a successful result from missing evidence.
