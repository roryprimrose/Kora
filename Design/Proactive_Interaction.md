# Proactive Voice Interaction

Status: bounded trusted local visual broker delivered; broader proactive
conversation, reminder and speech capability remains proposed.

R17/R18's delivered release foundation is a **passive native notify-only
status**, not this general proactive broker or voice capability. Settings/
Tray maintenance review explicitly permits public metadata for this run,
checks canonical stable/beta releases with bounded cadence/backoff, shows
verification age and supports reviewed canonical-page navigation and a
version-specific 24-hour per-run snooze. It never opens a window, takes focus
or speaks unsolicited. Privacy/ownership/protected-call closure invalidates
checks and navigation; no unlock/call-end replay exists. Remote notes are
not rendered, and models/tools cannot supply a feed, availability or URL.
The additional local broker below does not enable voice deferral/replies or
general reminder preferences. See the [maintenance limits](Distribution_And_Updates.md#delivered-bounded-r17r18-native-foundation).

Related: [Task Lifecycle](Task_Lifecycle.md), [Work Management](Work_Management.md), [Distribution and Updates](Distribution_And_Updates.md), [Security and Data Flows](Security_Data_Flows.md).

## Delivered R18 trusted local visual broker - 2026-10-09

`AuthorityLocalEventSource` reads the existing bounded atomic selected-session
work snapshot, never model text. Its fixed admitted sources are local-version
queue ready/current/success/failure/blocked/Unknown states, the exact genuine
current-run local-version pending question, and already-verified cached
maintenance availability. Done/removed subjects, expired/retired admissions,
earlier FIFO entries, interrupted work, unclassified questions and stale or
snoozed maintenance cannot manufacture an event. Snapshot omissions remain
explicit; absence is not an inferred success or detector observation.

The host event has stable content-free ID, source/type/category, exact session,
optional task and source subject IDs, session generation, source/related
revisions, original source origin, UTC observation/expiry and fixed priority.
Broker revisions advance on coalesced source changes and dismiss/defer commits.
Summaries are bounded fixed host statements: no task titles, question text,
remote notes, user content or model phrasing. Queue deadlines remain their
original 30-minute lifetime; questions keep their original expiry; maintenance
expires six hours after its actual verified observation. Observation is not
execution, approval, a renewed deadline or reusable authority.

`LocalEventBroker` serializes observation and exact actions, re-reading the
authority under its owning storage lease and holding the maintenance cache
lock across the suppression commit. Source/profile/origin/generation/revision,
original channel, selected native lifetime and call/privacy/lock/owner epochs
are rechecked, including immediately before returning presentation. Incoming
trace/provider fields never select a session. Source changes return stale/
unavailable, not a retargeted event; clock rollback holds until the durable UTC
high watermark is reached. UTC deadlines do not depend on local DST.

Only the already-open native **Sessions** work surface observes and displays
these events during its existing passive refresh. It never opens/activates a
window, steals focus, replaces or answers a question, changes voice targeting,
resumes/dispatches work, consumes grants or extends meaningful activity.
There are at most eight visible delivery/status rows with explicit omissions.
New eligible notices are considered separately from suppressed status rows,
so old high-priority receipts do not starve another category.
Fixed fatigue budgets are Work 3, Failure 2, Attention 2 and Maintenance 1 per
UTC hour, with a one-minute per-category spacing. Presented, dismissed,
deferred, expired and category-limited rows are truthful **status**, not new
notifications. No speech is requested, including under VoiceOnly output.

Native review/dismiss/defer and [exact original typed/current-name activated
commands](../docs/commands.md#trusted-local-events) share this broker. They
require a host-held exact event ID/revision and fresh admission. Review/status
is passive metadata, not question review or maintenance navigation. Dismiss
rejects that exact event revision. Defer is fifteen minutes capped at the
original source expiry, never a work or approval deadline extension. Cached
maintenance's existing Check/Open/release-specific snooze remain separate.
Reserved event commands preserve any pending foreground question/approval.

Schema-1 `local-events.json` and `local-events-unconfirmed.txt` use
`IApplicationDataPaths` and the shared atomic preference store. At most 64
content-free suppression receipts and four fatigue budgets fit the complete
64-KiB bound; no source content or effect payload is persisted. Expired receipts
retain revision watermarks until their session is retired; capacity exhaustion
holds the broker without evicting suppression or disabling independent work
inspection. Requested/terminal trusted audit, exact readback and marker
confirmation precede delivery. Lost receipts, corrupt/obsolete formats,
unconfirmed storage and future clock state fail closed; no rollback, default
or automatic repair is claimed. The bounded commit marker remains `0` after
confirmation and becomes `1` before a write; a missing member of this persisted
pair is unknown, not a fresh-run default. Retention serializes broker-before-authority
lock order and removes owned receipts before deletion; logical disposition
also retires its presentation. Global fatigue budgets remain independent.

Restart loads suppression only, never current events. Fresh authority is
required; presented/dismissed/expired effects are not replayed. An unexpired
explicit deferral may become eligible only after a new admitted native
observation, under the same original expiry and category limits.
There is no ambient observer, wake/listening change, unsolicited inference,
network check/consent, browser/download/install or automatic execution route.
Broader multi-session proactive conversation, detector-driven speech,
configurable reminders/quiet hours and installed accessibility/release
acceptance remain gated. All R02 experiment evidence is retained unchanged.

## Delivered run-only routine notice quiet mode - 2026-10-10

In an already-open **Sessions** selected work surface, **Quiet routine notices for this run**, **Turn routine quiet off**, and **Reset routine quiet to Off** control one combined Work/Maintenance choice. Default is Off on every process start; On lasts until explicitly cleared/reset or Kora restarts. No quiet preference, timer, scheduling, network check or consent is saved.

The serialized broker suppresses first routine deliveries **before** presentation and fatigue accounting, and hides routine notice rows while On. Failures and Attention retain their original budgets and admission.
Authoritative work/queue/status, history/citations, errors, mandatory security output, questions/approvals and private recovery remain independently reachable. Quiet never interrupts or relabels a pending question; synthesis remains NotRequested even under VoiceOnly.

Native mutation requires the exact live original LocalUi request/session, current selected native lifetime, unchanged private host/call/control/retirement epochs and quiet choice revision. HostSystem, model/ambient callbacks and voice relabelling cannot invoke this internal seam.
The real window's visibility generation is bound before admission; a hidden, not-yet-open or closed Sessions surface cannot mutate. Hide/reopen cannot revive a prior callback.
Requested/terminal typed configuration audit and content-free generated diagnostics retain actual host correlation; failure/cancellation holds notice admission with explicit recovery, not a success-shaped default or silent rollback.

Quiet suppression retains exact source/generation/revision/expiry and advances existing eligible/deferred receipt revisions. `RoutineSuppressed` is not Presented and consumes no category budget.
Clear also observes and burns current muted sources under the same owning-source/cache locks; it permits only future new admitted notices, never a deferred backlog or network rerun. Already-presented/dismissed receipts retain their truthful dispositions.
After clear, old muted sources may appear only as explicitly labelled `RoutineSuppressedNoReplay` passive status; expiry still wins.

The existing two suppression files, 64-receipt/64-KiB bounds, UTC watermark, four fixed fatigue budgets and retirement rules are unchanged. Canonical schema 1 is read without rewriting; explicit suppression writes known schema 2, adding only the validated routine-only disposition.
Schema 1 cannot contain that disposition; unknown versions and invalid/nonroutine suppression fail closed. The run-only flag/revision is never serialized. Restart resets only the choice, not no-replay history or budgets.

Snapshots carry quiet and control generations; old render callbacks cannot publish a pre-quiet routine result after mutation, private closure/reopening or disposal.
An admitted quiet request revokes in-flight observations and exact actions before waiting for the broker/source lease, preventing late routine presentation or fatigue consumption by those callbacks.
Status separately counts all omitted rows, routine rows hidden by quiet and current sources suppressed without presentation. Other sessions are not ambiently observed; this remains the bounded already-open native surface, not a reminder scheduler.

Scheduled quiet hours, multi-session reminders, proactive speech, broad model/typed quiet settings and installed accessibility/full R18/RC acceptance remain unavailable.
Existing exact event commands remain unchanged for Failure/Attention; exact routine defer cannot revive a quiet-suppressed source. All R02 runtime/native/containment/speech/evidence procedures remain unique historical evidence and are retained, not rerun or promoted by deterministic quiet tests.

## Product Behaviour

Kora can start a conversation without a preceding user utterance.
Voice interaction is bidirectional, not solely request/response.

Examples:

- "A new Kora release is available. Would you like to review its canonical release page?"
- "The explanation is ready. Would you like the short summary?"
- "I need you to choose an environment before I can continue."
- "The next request is blocked because its prerequisite failed."

Proactive speech is a suggestion or report, not permission to perform the proposed action.
It does not authorise background collection, new model calls, tools, or application maintenance.

## Host-Owned Interaction Broker

The broker accepts typed events from authorised components, such as a verified update checker or the current task ledger.
Each event has an ID, originating session/task where applicable, source, deduplication key, creation/expiry time, relevance, sensitivity, and permitted response actions.
Events derived from task content retain the task's provenance and restrictions.
Untrusted tool output cannot manufacture a trusted maintenance prompt or bypass an approval.

Kora renders a short grounded statement and, when needed, opens a prompt bound to that event.
A model may phrase eligible task content only under normal egress rules; it cannot invent system events, release availability, or approval destinations.
Update suggestions use host-verified release metadata and need no model.

## Delivery Policy

- Explain and obtain consent for proactive speech during setup, with mute, quiet hours, and visual-only preferences.
- Speak only while the session is unlocked, speech is permitted, and the user has explicitly enabled the voice interaction session.
- Apply [Call-Aware Speech](Call_Aware_Speech.md): the default suppresses proactive and requested speech during calls or Unknown enabled-detector state; configurable changes use the shared registry, but while protected voice-originated voice/in-call changes are rejected and require new UI initiation.
- Never speak over user capture or another spoken prompt.
- Routine suggestions wait for a conversational gap; ongoing task execution alone need not prevent a status/clarification message.
- Update suggestions wait until the user is not capturing/responding and do not displace an action approval.
- Coalesce duplicate events and avoid repeated reminders; an ignored routine suggestion does not repeat in the same voice session.
- "Not now" defers a suggestion, "don't remind me about this version" suppresses that release, and quiet mode keeps eligible notifications visual.
- Expired or invalid events are removed, not spoken later as though still current.
- Suppressed events remain eligible for visual presentation; do not replay a speech backlog when a call ends.
- Lock/disconnect stops speech and suppresses sensitive presentation; unlock does not silently enable listening or proactive speech.

Initial suggested reminder deferral is 24 hours, configurable.
Do not wake/activate the microphone to deliver a notification or force the user out of mute.
Local-only mode can deliver local task events; remote update checks require an explicitly permitted maintenance network operation.

## Reply Routing

Only one spoken foreground prompt is eligible for a response.
Several sessions can have explicitly addressed native question/approval cards; background events do not change session selection or that voice target.
The user responds with "Kora, ..." or optional push-to-talk; unsolicited speech does not leave command transcription continuously open.
Bind a reply to the exact current prompt ID, action, and expiry.
Ambiguous "yes" or a reply after another prompt took focus requires clarification, not inferred approval.
Wake detection is not authentication, and playback cannot approve its own prompt.

Management acknowledgements and task approvals use distinct scopes.
Unsigned-phase update interaction is notify-only and creates no maintenance approval.
Interrupting a prompt preserves truthful task state and follows existing prompt withdrawal/re-presentation rules.

## Update Example

1. The configured release checker discovers an eligible release from the canonical repository whose identity and digest match protected maintenance configuration, and emits a host event.
2. Kora says which version is available, that it is unsigned, and that replacement is manual.
3. "Kora, not now" defers; "Kora, show that release" opens an exact canonical release-page proposal.
4. Native browser navigation may open that page after normal URL review; it is not download, staging, execution, or installation approval.
5. Changed release identity/origin invalidates the proposal and requires a fresh request.

No release is downloaded, staged, executed, or installed by Kora during the unsigned phase.
The model and skill runtimes never receive an installer tool or authority to select executable payloads.
See [Distribution and Updates](Distribution_And_Updates.md) for the maintenance boundary.
