# Work Management and Request Queue

Status: authoritative work inspection, exact conversation controls and reviewed
cancellation/application lifecycle are implemented; resource-allocation seams
do not qualify production concurrency or model-assisted management. The fixed
local-version queue remains internal compatibility, not a public scheduler.
The [roadmap](Implementation_Roadmap.md#current-delivery) owns delivery status.

Related: [Interaction and Sessions](Interaction_And_Sessions.md), [Architecture](Architecture.md), [Task Lifecycle](Task_Lifecycle.md), [Security and Data Flows](Security_Data_Flows.md), [Acceptance Criteria](Acceptance_Criteria.md).

## Product delivery boundary

The [approved realignment](MVP_Scope.md#approved-retirement-and-simplification) retires productisation of the fixed diagnostic-read scaffold.
Preserve its internal lifecycle/authority checks and inspect/cancel existing work safely; do not create further user-facing diagnostics plans, prerequisite/replacement/reordering preparation or capacity-setting variants as substitutes for useful conversation/tool tasks.

Normal work surfaces should answer what is running, waiting, blocked or pending and let users manage admitted work through shared voice/UI operations. Internal IDs/revisions remain host bindings rather than the required ordinary spoken vocabulary.
Public fixed-read scheduling is removed. The retained internal boundary below does not qualify full task execution or authorize expansions.

## Passive selected-conversation search - 2026-10-13

The [exact-conversation search](Interaction_And_Sessions.md#delivered-passive-exact-conversation-lexical-search--2026-10-13) reads admitted retained conversation fields under its own role/source contract. Work/AllWork/task/question summaries remain body-free authoritative projections; old receipt history is not retroactively populated with composer messages.

Search, Next, Cancel and Clear issue no control intent, renew no meaningful activity, run no cleanup and change no queue/task/approval/provider/retention authority. Done remains readable without Resume. Query edits and result closure leave memory composer/dirty-draft decisions and current foreground question/voice target independent.

### All Work selected metadata literal Find

The native [All Work guide](../docs/windows-and-tray.md#all-work--attention-overview)
provides Ctrl+F / First / Next / Previous / Clear over exactly the immutable
displayed selected-row details projection. The explicit row admission captures
that string and its existing selection/page/control epoch; binding getters are
cached and do not construct Find state, scan or perform I/O. Existing selection
retirement hooks also retire Find, including future ordering/query controls that
retire selection. It does not depend on outstanding ordering or query-preparation
implementations.

The shared `AdmittedMetadataFindState` opts into the authoritative
`SnapshotLiteralFind.FindCounted` nonoverlapping policy. Counts and selected
ordinal are complete only within that exact captured metadata string; boundary
navigation does not wrap or claim a displayed match. Native focus must be
acknowledged and remain admitted; failure clears the match and navigation.
Unavailable rows admit only their displayed safe projection, never former
observed metadata. No additional fields, bodies, source paths or other rows are
retrieved. Query edits/Clear are read-free; source/private/control/page/selection
retirement and close erase query, capture, match, counts and navigation.

Find is native-only presentation, not current task/session selection, identity,
ownership, permission, execution or clipboard authority. No query/capture/count
or match is sent to Main response, transcript, speech, logs, activities or baggage.
The consumed implementation contracts are the existing bounded row admission,
selection retirement, shared literal validation/scanner and native focus controls;
unrelated provider, installed/hardware or retired experiment qualification is
not a prerequisite.

## Delivered deterministic local-version queue

The [workspace command boundary](../src/Kora.Application/Hosting/SessionWorkspaceService.Commands.cs) refuses new public diagnostic admission, dispatch, reorder, requeue and pause/resume, including hand-built commands.

Existing records retain exact reviewed inspection/cancellation/removal/clear, without replay or deadline rewriting ([grammar](../src/Kora.Core/Commands/SessionCommand.cs), [commands](../docs/commands.md#deterministic-local-version-queue)).

The internal scheduler, fixed read actions, private store/domain, configuration validation and historical audit remain maintained infrastructure and test seams, not a public task runner. Direct version/capability/readiness queries are unchanged. General model/tool task management remains the separate proposed outcome below.

### Delivered atomic selected pending cancellation - 2026-10-12

Existing pending entries retain exact-set review and separate atomic confirmation through the [session queue service](../src/Kora.Application/Hosting/SessionQueueService.cs). Selection never includes running effects or another session; cancelled dependents do not disappear, original deadlines/history remain and Unknown stays held.

### Delivered passive cross-session work observation - 2026-10-11

The [All work window](../src/Kora/AllWorkWindow.axaml) retains lifecycle, name/exact-session navigation, observed task state, Needs answer, loaded-page ordering and Find in displayed metadata. It observes bounded independent snapshots, not an atomic global inventory or all-clear.

Raw task/request correlation predicates and authority/queue/count-range widgets are removed. Displayed provenance, explicit omissions/unavailable/output-bound status and full consumed-source validation remain mandatory. Browsing neither dispatches/resumes work, answers a question, changes voice focus nor renews activity.

### Waiting-question overview through typed and activated voice requests

The approved concurrent-conversation outcome includes **show sessions needing an
answer**, optionally activated by the current assistant name. This host-owned
operation opens/reuses native All work and completes one actual bounded
`SessionWorkspaceService.ReadWorkOverviewAsync` observation. It is not a tool,
MCP operation or skill: private presentation, source admission and original input
currency remain host responsibilities.

The fresh query is Needs answer only, first page, all stored lifecycles, no
carried-over name/exact-ID or task-state predicates. Native Needs answer plus
Refresh remains equivalent; ordinary open all work stays read-free. No saved
filter or new technical predicate is introduced.

The original host request/channel and deferred causal continuation survive UI
dispatch and the read. Changed input, name, call, privacy, ownership, source,
filters or window retire publication, page/selection and Find authority.
Cancellation/close own and join the original returned read task, including
noncooperative storage. The existing ten-second authority deadline is not
renewed and is not a physical-shutdown guarantee. Failed, busy, disposed or
withdrawn readers never report completed navigation as a completed observation.

Waiting groups retain explicit unavailable/output-bound rows, omitted question
counts, source scope and Next status, including zero-match pages with Next.
No page or end-of-observation proves nobody is waiting. The operation never
answers/retargets a question, consumes a review, submits composer text, dispatches,
resumes, grants, renews activity or invokes a provider. See the
[user guide](../docs/waiting-questions.md) for the complete interaction.

## Delivered native authority observation

The [bounded R18 visual broker](Proactive_Interaction.md#delivered-r18-trusted-local-visual-broker---2026-10-09)
consumes these same fixed-profile queue/task/question facts, not model progress
or response text. Broker status/review/dismiss/defer touches only bounded
suppression metadata; it cannot alter FIFO, dependencies, deadlines, receipts,
question targets, meaningful activity, grants or manual dispatch. Category
limits and unavailable broker storage never establish work eligibility or
disable the independent authoritative work snapshot.

The [R14 selected-session work surface](UI_Workspace_And_Windows.md#delivered-authoritative-sessions-work-surface---2026-10-09)
adds a bounded atomic `SessionWorkSnapshot` over delivered queue/task/question
authority. Native and `queue list` use the same host/store snapshot and configured
limits; dispatch eligibility calls the same domain policy as fair scheduling.
All live queue entries plus 25 recent receipts, up to 50 nonqueue task records
and 50 pending question identities carry explicit omitted counts and a complete
64 KiB ceiling. Current admission deadlines derive from their exact audit
receipt, not presentation time. Passive refresh never extends activity or
dispatches; selection never answers or retargets a question. Unknown, blocked
heads, retired admission and no-replay recovery remain distinct observations.
The remaining lanes below are proposed, not enabled by this native increment.

## Two Independent Lanes

Kora has a work-management lane and a task-execution lane.
Management remains available while the executor is planning, waiting for approval, running a tool, or cancelling.
A bounded scheduler admits one task per Kora session and multiple independent sessions concurrently; management inference is not a task executor.
The execution limit is configurable within verified capability, initially proposed as two, with resource leases and per-session isolation.

The management lane:

- Routes general requests to a new session unless clearly related to an Active session; explicit session targeting wins.
- Interprets requests against the addressed session's task and queue.
- Proposes queue additions, edits, ordering, removal, replacement, or clarification.
- Submits explicitly admitted typed host lifecycle proposals, including
  shutdown/restart, without approval or execution authority.
- Answers current-work and remaining-work questions from an authoritative ledger.
- Routes approval replies to the correct task and request.

Execution lanes perform admitted session tasks' isolated model/tool workflows.
Management cannot run task tools, acquire arbitrary context, or approve task actions.
Host-owned, explicitly requested context capture still uses the context broker.

The app -> model -> app contract in
[Commands, Tools, Skills, and Model Interaction](Commands_Tools_And_Skills.md)
does not expand management authority. Management receives the minimal
authoritative ledger/status snapshot and proposes typed management operations;
it does not load skill instruction bodies or inherit executable-tool definitions.
The task runtime performs admitted skill/tool iteration and receives approved
structured results. Direct status presentation and runtime state queries reuse
host-owned query services while enforcing each caller's data scope.
Approval replies are correlated to the existing host prompt, not interpreted
by management inference as permission to create a grant.

The delivered [general-request destination operation](../docs/session-routing.md#untargeted-requests)
separately discloses the exact original input and bounded Active metadata before
optional local-only relatedness advice. The user can instead choose an exact
destination without advice or cancel. Destination acknowledgment and ordinary
submission remain separate; advice consent cannot authorize an answering provider,
source transfer, queue editing or task replacement
([routing admission](../src/Kora.Application/Hosting/SessionRouting/SessionRoutingService.cs)).

## Contextual Decisions, Not a Fixed Queue Prompt

Use the model to decide what a new utterance means when context makes that decision clear.
Do not ask "queue or replace?" on every request.

| User request explicitly addressing a busy session | Expected interpretation |
|---|---|
| "Also explain this other exception" | Queue a separate task if its source is clear |
| "Do that before the documentation update" | Reorder the identified pending tasks |
| "Forget the documentation update" | Remove the uniquely identified queued task |
| "Stop this and explain the new exception instead" | Cancel the active task and prepare its replacement |
| "What are you currently working on?" | Report active task, stage, and any wait/blocker |
| "What do you have left to do?" | Report known remaining active steps and queued tasks |
| "Change that one" with several possible targets | Ask a targeted clarification |

These are examples, not permission to cancel or replace work merely because a new task seems more interesting.
The model proposes typed operations with task IDs and an expected ledger revision.
Kora verifies that targets exist, the proposal matches the user's request, and applicable constraints are satisfied.
Ambiguous targets, unclear replacement intent, dependencies, or unavailable context require clarification.
Never silently merge, drop, or change the meaning of a request.
Acknowledge accepted decisions with the task label and queue position; the user can correct them.

Exact stop/cancel controls and basic ledger status have a deterministic local path.
They do not wait for the management model, task runtime, tool, or network.

The delivered addressed remaining-work operation completes a passive host answer through **What work remains?** on one selected native conversation or the [exact typed/current-name activated grammar](../docs/commands.md#ask-what-remains-in-one-conversation).
Setup status remains separate; neither Work-panel navigation nor opening All Work substitutes for the answer ([native owner](../src/Kora/SessionsViewModel.RemainingWork.cs)).

The [query owner](../src/Kora.Application/Hosting/SessionConversationService.RemainingWork.cs) captures body-free pending and running facts together under the existing conversation owner gate, then fences durable task/question facts and volatile revisions again.
The [projection](../src/Kora.Application/Hosting/SessionRemainingWorkAnswer.cs) distinguishes durable/source times, known host steps, ordinary FIFO positions/holds/expiry, pending questions and unresolved Unknown/Interrupted records.
Manual ordinary dispatch, failure/cancellation recovery and application lifecycle holds are observed independently; the passive answer neither releases a hold nor renews an original deadline.

The sources are not a fictitious globally atomic plan. Omitted records, accepting originals, absent workers and unavailable steps remain explicit; missing volatile ownership never proves durable completion.
Changed sources refuse publication rather than inferring absence. Execution, queue state, source activity, retention and grants are unchanged; no provider/model/tool/resource dispatch is part of this query ([query](../src/Kora.Application/Hosting/SessionConversationService.RemainingWork.cs)).

The proposed bundled session-lock skill has a narrowly host-admitted priority
control path; this does not grant management inference executable-tool access
or waive the future action-specific approval gate. Today direct exact lock
calls the Windows API without that gate; model-suggested lock asks for approval.
See [Bundled Skills](Built_In_Skills.md).
Standalone lock uses [durable control-session binding](Built_In_Skills.md#standalone-lock-work-session-binding)
without routing inference; window selection never supplies Session authority.
For [power proposals](Security_Data_Flows.md#management-power-proposal-authority),
the deterministic host lifecycle controller owns all-session review,
approval/countdown and gated fixed-worker dispatch. M/E propose only.
If management inference fails or is unavailable, show the limitation and ask explicitly how to handle an ambiguous new request.
Local-only mode does not call a remote management model.

## Management Operating Envelope and Degraded Mode

The [bounded native conversation consumer](Interaction_And_Sessions.md#delivered-exact-session-conversationcomposer-foundation--2026-10-12)
is not a fixed-read queue profile or a provider execution slot. It creates
fresh exact-session original-user task/content intent atomically, refuses
other live/Unknown/question work and invokes current configured turn admission.
No model exists in the production catalogue; retained user content receives a
truthful unavailable task/HOST status. There is no adapter execution, bootstrap
fallback, aggregate session quota, automatic retry or retained-history context.
Queue counts/settings/readiness do not grant provider/resource permission.

The management lane is optional inference around a mandatory deterministic host core.
The delivered bounded session core now accepts exact typed/activated-voice
`session list/status/inspect/create/rename/done/resume`, with `session help`,
through the existing guarded durable workspace service. IDs and explicit
revisions, never titles or selected windows, address controls. Reads describe
only existing authority and task/question records; they do not infer progress,
queue work, generally cancel effects or restore approvals. Fresh user lineage/control
intents, live privacy/ownership/origin/revision checks and lifecycle blockers
remain required. Protected-call voice mutations are explicitly unavailable.
See [the bounded syntax](../docs/commands.md#bounded-exact-id-session-commands).
This closes no scheduler, inference, concurrency or full work-routing gate.

The delivered exact task extension adds factual `task status/inspect` by
session/task ID and explicit revision/generation/question-bound cancellation
of only the already admitted current-run local-version pre-dispatch wait.
Native selection-bound inspect/cancel uses the same workspace workflow, not
a model or separate executor. Task/question/audit authority is consolidated
into one transactional store; answer/cancel/admission races cannot produce
a dispatch after committed cancellation. Previously dispatched, terminal,
Unknown, expired, foreign and previous-run work is not silently cancelled.
No remaining-step inference, scheduling, workers, task tools or replay is
added. See [the exact controls](../docs/commands.md#bounded-exact-id-session-commands).

Exact session list/select/new/Done/delete and cancel/stop/pause/clear commands, direct session/task-ID operations, queue listing, and factual ledger status never require management inference.
When inference is unavailable or budget-limited, an ambiguous request receives native choices such as Queue, Replace current, or Cancel; it is never guessed, dropped, or treated as task approval.

Initial per-profile limits:

- At most one management inference request in flight.
- Input selection and the complete serialized outbound model body are each capped at 32 KiB UTF-8 bytes, including system/framing/history overhead; the complete typed proposal JSON is capped at 4 KiB UTF-8 bytes. Output overflow or schema failure degrades explicitly, never truncates into a committed proposal.
- The host inference deadline is 15 seconds from dispatch, independent of SDK send/abort acknowledgement or provider completion. Setup/authentication are separately cancellable host flows, not hidden inside that allowance.
- No automatic provider retry is admitted. The initial single-inference management profile forwards at most one inference attempt per request, enforced at the final request boundary; an explicit user retry is a new bounded request. Typed management proposals still reach host validation, but a result-to-model continuation would require a separately proved budget/profile.
- Remote management is capped at 30 calls per rolling hour. Reaching the cap activates deterministic degraded mode and reports when capacity returns.
- Provider cost/quota/rate-limit responses never consume task approvals, switch providers, or borrow the execution session.
- Queue/status controls remain responsive while the provider is offline, signed out, throttled, over budget, or prohibited by local-only policy.

Provider enablement requires evidence that independently schedulable task and management sessions are allowed by the pinned SDK/API behavior, account tier, terms, and rate limits.
Record the tested version/tier and estimated worst-case management usage; do not claim concurrent management when the provider serialises or forbids it.
Users can disable model-assisted management, in which case deterministic choices are always used.

R02's loopback proof passed the byte/deadline boundaries and independent
conversations, but observed SDK retry attempts despite the error-abort hook;
the final request gate blocked them. These observations support the host
envelope above, not an approved hosted provider. Start with a fresh isolated
management conversation per request; cumulative-history reuse requires its
own budget/isolation evidence. Unconfirmed termination quarantines the
affected inference request/conversation rather than releasing its slot based
only on SDK acknowledgement; deterministic controls remain available.

[R02-MG1 and R02-PV1](Implementation_Roadmap.md#r02-runtimeprovider-follow-up-gates)
separate .NET host-envelope proof from account/provider permission, actual
concurrency, service quotas and billed-cost validation. The
[technical continuation](Runtime_Provider_Feasibility.md#management-remains-optional-and-host-bounded)
does not expand the experiment's status-only schema into production approval
or execution authority. Implement deterministic management first; do not
make it wait for hosted-model enablement.

The separate [actual .NET MG1 proof](../experiments/r02-dotnet-management-proof/README.md)
now passes for the explicitly approved **released NuGet SDK 1.0.16** and the
unchanged RT1 native 1.0.90 minimal HTTP/stdio bytes. The original source-built
RT1 artifact did not reproduce here; its hashes and historical evidence were
not rewritten. The user approved the released profile separately, and all 45
RT1 control-point regressions were repeated before MG1. No byte equivalence
between those SDK artifacts is claimed.

MG1 measures complete UTF-8 requests at 32768/32769 bytes and complete typed
JSON at 4096/4097 bytes, real held inference and stalled native abort
acknowledgement, 30 actual failed runtime attempts plus a new request at the
exact rolling-hour boundary, cancellation/deadline/retry admission races and
an independent manager while two execution conversations remain held.
Unknown computation/effect termination remains quarantined after SDK
acknowledgement; local choices/status/cancel do not wait for it. A socket
closure is only connection evidence, not physical computation stop or rollback.
R13 must carry these limits into host-owned dispatch, target/revision validation
and truthful receipts using R04 identities/audit/no-replay contracts. The
fixture has no production ledger, task slots, resource leases, account capacity
or approval authority; RT2/PV1 and production admission remain separate.

## Authoritative Work Ledger

The host ledger records admitted intent, original session/task identities, observed state, questions, decisions and terminal outcomes. Queue policy and source revisions remain authoritative inside the host; ordinary controls do not expose engineering predicates as a separate product.

Retired fixed-read replacement workflows do not create new tasks. Retained history and safe cancellation remain governed by the [compatibility boundary](#delivered-deterministic-local-version-queue). The [workspace service](../src/Kora.Application/Hosting/SessionWorkspaceService.cs) and private store, not model text or presentation selection, resolve authority.

## Queue and Dispatch

### Ordinary submitted conversation turns

The [ordinary conversation owner](../src/Kora.Application/Hosting/SessionConversationService.Pending.cs)
accepts another explicitly submitted request to an exact existing Active
conversation while its previous turn runs. Acceptance physically retains the
immutable original USER body and task intent with the existing typed audit.
Qualified admission captures the original provider/account/model, policy,
serialized context and reviewed instruction sources before reporting queued
position. Unqualified providers return their existing unavailable outcome,
never a success-shaped queued fallback. Questions, approvals, hosted review,
MCP and other specialized operations retain their separate admission fences.

Arrival order is authoritative, with at most ten pending requests per session
using the existing domain maximum. There is no new queue-settings product,
allocator, reorder surface, eviction or aggregate storage quota. The existing
100-message session and complete input bounds still apply. Concurrent admission
to the same session is explicitly refused until the first acceptance completes,
not silently replayed. Independent sessions have independent admission lanes.

Eligibility is bounded by both the existing thirty-minute pending default and
the **unchanged original context/source authorization expiry**, whichever is
earlier. In particular, ordinary local input still has its original fifteen-second
context lifetime; queueing does not turn it into thirty-minute consent.
Expiry retains a denied HOST/task outcome without resource acquisition or
provider dispatch. A fresh explicit request/review is required, not automatic
deadline extension. The active provider budget remains owned by the runtime
and begins at dispatch.

Only successful, confirmed terminal retention after the preceding physical
resource join permits automatic next dispatch. Failure, pending/active
cancellation or uncertainty holds the addressed lane; **Resume held dispatch**
is deliberate and cannot renew original authority. The exact ledger transaction
also refuses a later request before an earlier live intent or alongside another
dispatched turn. Unknown/unclassified work and pending questions cannot become
queue authority. The existing runtime allocator alone owns execution resources;
pending turns acquire no lease and the queue never waits globally for other
sessions.

Before dispatch, the same original source/session/generation/metadata,
privacy/control, provider policy/profile/qualification and context fences run
again. No later global setting, foreground selection, new history, grant or
unseen prior answer is substituted. Invalidated authority becomes an explicit
denied or unconfirmed/held recovery outcome, never a renewed grant or fallback.
Deferred execution uses a fresh activity linked to the captured admitted context
while retaining the host-issued request/channel and trusted typed audit.
Bodies and reviewed sources are not telemetry.

Sessions and compact conversation share factual pending position, the submitted
USER input, exact selected pending cancellation and held/resume controls.
The same [lane owner](../src/Kora.Application/Hosting/SessionConversationService.Pending.cs)
also supports an independent, deliberate **Pause ordinary dispatch** for one
exact Active conversation, including a healthy lane with no pending requests.
It suppresses the next ordinary start decision after the physical terminal join;
a turn already selected for dispatch or running continues unchanged. New
submissions retain the same provider qualification, immutable original source,
FIFO, message/byte bounds and original authorization expiry. Native waiting
rows show their original expiry, not a renewed deadline.

**Release manual dispatch hold** clears only the typed original-user manual
reason. **Resume held dispatch** clears only the separate failure/cancellation
recovery hold; when both exist, both require their own deliberate operation.
Neither action clears storage, Unknown, permission, source, consent, privacy or
expiry fences. An expired/invalid head cannot be skipped to dispatch a later
request. Expiry is denied without acquiring resources; fresh input/review is
required, never renewal or replay. Manual start decisions are not action approval.

The [exact typed/activated-voice adapter](../src/Kora.Application/Hosting/SessionRequestCommandService.cs)
uses `session dispatch status|pause|resume <session-id>` and the same admitted
manual-control action as native Sessions/compact. Current input, channel,
private control, exact session/generation/metadata, host-run admission and lane
revision are host bindings; no model is needed or authorized to create this
control. Done is not resumed, no session is created and pause/release itself
does not renew activity, change history/context, grant permission or invoke a
provider. The holder is volatile to this host run and generation, not durable
authority; restart recovery of older pending work is unchanged.

This is ordinary conversation control, **not** the retired public fixed-read
diagnostic queue pause/resume, reorder or settings product. Those command
operations remain denied. There is no global pause, second scheduler, new
queue policy setting or task-stepping execution approval. See the
[user controls](../docs/windows-and-tray.md#ordinary-conversation-dispatch-hold)
and [behavioral regressions](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.ManualDispatch.cs).

Cancellation wins atomically against dispatch, preserves submitted history and
unrelated requests, and has zero provider/egress/resource effects for that
pending turn. Active cancellation stays with the existing running-turn owner.
Logical cancellation and caller observation retirement do not release physical
resources; the service retains and joins its workers, quarantining uncertain
late output through the runtime. Restart uses existing Interrupted/Unknown
history recovery and never replays pending requests.

The composer clears only after real accepted intent, then remains available
for another request or independent memory-only unsent text. Selection changes
after acceptance do not retarget submitted work; results remain in the original
conversation. This is bounded implementation, not new provider/hardware
qualification, foreground-relative voice authority, effect execution or
revival of the retired diagnostic scheduler.

### Delivered Stop All submitted work

`stop all work` and the explicit native All Work control share the existing
host-owned full-set review and physical-join owners. This is not a bounded
overview operation: opening, Refresh, Find, selection and close remain passive.
Separate current original-channel confirmation is required before exact pending
withdrawal/running cancellation. The reversible review hold releases only its
own reference; acceptance retains joins through view closure and failures.
Already dispatched compatibility reads finish rather than pretend to cancel.
Results identify completed, partial-unconfirmed, stale, cancelled or truly empty
reviews, never a persistent global pause or future cancellation grant.
See [interaction contract](Interaction_And_Sessions.md#stop-all-submitted-work-while-staying-open).

### Clear ordinary pending work only

The [ordinary lane owner](../src/Kora.Application/Hosting/SessionConversationService.PendingClear.cs) completes a distinct **Clear queue** operation for one explicitly addressed Active conversation. It is not Stop All, active-turn cancellation, or the retired public fixed-read diagnostic scheduler.

Review captures every current never-dispatched pending identity, FIFO position, count, original expiry, session generation/metadata and lane revision in a complete body-free snapshot. Original bodies remain separately inspectable through the existing conversation reader. Review/discard changes no dispatch hold, task, history, grant, retention or provider state ([native regression](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.PendingClear.cs)).

Separate original-source confirmation closes admission/dispatch only for that lane's transaction. The canonical [terminal transaction](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.Conversation.cs) additionally requires the exact current session/metadata and an undispatched intent; the existing workers retain Cancelled USER/task/audit receipts and join their readback. Running or already-dispatching work and other conversations are not cancelled.

New arrivals, dispatch, expiry, ownership/private/control/channel/call/input changes invalidate review rather than selecting replacements. Acceptance cannot renew any original request, acquire a provider/resource lease or clear an independent manual/recovery hold. Partial durable effects and unconfirmed readback are enumerated per original; no rollback, replay, physical-stop or durable all-clear is inferred ([boundary regressions](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.PendingClear.cs)).

Native Sessions/expanded conversation and [exact typed/activated commands](../docs/commands.md#clear-one-ordinary-conversations-pending-work) share this owner. An activated review can be separately confirmed natively without replacing its original voice source; a new capture that retires that source requires fresh review. Model/source prose confers no control authority ([routing regressions](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.PendingClear.cs)).

Clearing reviewed pending work retains an existing manual hold and the cancellation recovery hold independently; releasing either cannot release the other. Changing a manual hold invalidates the captured lane review ([composition regressions](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.PendingClear.cs)).

Passive remaining-work inspection does not consume or invalidate a pending-clear review. Confirmed clearing invalidates the earlier remaining-work observation; a fresh answer reports the remaining running owner and independent dispatch holds without cancelled FIFO entries, new writes or an inferred all-clear ([native composition regression](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.PendingClear.cs)).

### Broader proposed management policy

- Proposed initial capacity: 10 pending entries, configurable. Full means ask the user to remove/defer work; never evict silently.
- Default order is arrival order. Contextual reordering requires a clear user instruction and acknowledgement; the delivered bounded route is the separately reviewed exact adjacent pending pair above.
- A task needing clarification keeps its position and blocks dispatch at that position until clarified, removed, or explicitly deferred.
- Explicit dependencies must succeed before dependent work starts. Failed, cancelled, expired, or unknown prerequisites block dependent dispatch.
- A queue entry's execution eligibility expires after 30 minutes waiting by default; notify the user and release active context references, retaining its request/expiry as session history. Extending/requeuing requires a new decision and context revalidation. User-configured lifetimes follow [User Configuration](User_Configuration.md).
- The active-task deadline begins when dispatched, not while queued.
- Normal successful completion dispatches the next ready entry automatically by default, after policy/context revalidation; user-selected manual dispatch adds a start decision. Queuing or approving dispatch is not action approval.
- Failure, cancellation, or unknown side effects pause automatic dispatch and explain why. The user can resume or explicitly choose a replacement.
- A cancellation request immediately pauses the addressed dispatch; deliberate exact voice or UI confirmation completes cancellation while preserving unrelated pending entries.
- "Clear the queue" for ordinary conversation requests uses the delivered pending-only operation above; reviewed removal is from dispatch, not history.
- "Stop all work" uses the delivered reversible full-set review above; it does not clear retained history.
- "Pause the queue" blocks new dispatch without suspending/cancelling the active task; resume still revalidates dependencies/policy.
- A replacement waits for host-side execution quiescence. If remote work may still be running, report the uncertainty and require an explicit decision before dispatching more work.
- Scheduling mutations, execution-slot acquisition, and canonical shared/exclusive resource leases are atomic host operations. Concurrent proposals with stale revisions are re-evaluated, not blindly replayed.
- Writes to different declared resources can proceed concurrently; conflicting reads/writes, OS lifecycle, and unknown effects require enforced exclusive coordination or rejection.

## Context and Approval Timing

Bind explicit "this clipboard" requests to the requested immutable snapshot at admission; do not read a later clipboard value silently.
If source selection is ambiguous, clarify before capturing it.
Session/queue interpretation receives only permitted bounded descriptors and the context required to identify the work, not all snapshots or tool outputs.
Raw clipboard, retrieved documents, tool results, rendered content, and skill instruction bodies are never sent to management inference.
Host-derived task IDs, safe labels, state, dependencies, and the authenticated user management utterance are structurally separated; untrusted task labels cannot become management instructions.
Any inferred cancellation, replacement, removal, or reordering proposal pauses at explicit voice or UI confirmation rather than mutating the ledger directly.

At dispatch, revalidate context existence/expiry, identities, dependencies, tool capabilities, policy, and any changed write base.
Missing/expired context requires a fresh selection or recapture decision.
Approval to add/reorder a task is not approval to transmit its content, execute a tool, apply a patch, or commit.
Task action approvals are obtained at the relevant execution point and are never shared with another queued task.

## Interaction and Recovery

Native question terminal state and completion now share a [private publication boundary](Interaction_Fallback.md#synchronized-native-terminal-publication---2026-10-12). Concurrent original/external committed-cancellation channels retain the actual Cancelled receipt; an earlier genuine withdrawal or failure still wins. Later privacy/source closure clears presentation without relabelling committed work or granting dispatch authority.

Management status uses the same visual/TTS presenter but has its own response identity.
An explicit status question takes speech priority; task execution continues and its visual progress remains visible.
Call-aware policy still gates that speech; priority does not bypass suppression or a separate prompt's approval identity.
Do not overwrite an approval card with a queue acknowledgement, or interpret a queue clarification reply as action approval.
Only one voice prompt is foreground at a time, with session/task/prompt IDs, revision, and expiry.
Changing voice focus withdraws generic spoken eligibility; explicitly addressed valid UI cards remain usable in other sessions.
The [cross-window focus contract](UI_Workspace_And_Windows.md#selection-voice-focus-notifications-and-concurrency) specifies target presentation, clearing/expiry, late updates and no automatic handoff to another session's pending question.
A verbally targeted background approval is re-presented/revalidated before accepting its action-specific confirmation.
Do not silently extend an action approval's expiry while managing the queue.

Deleting a session first pauses its work, then requires exact affected-work/data voice or UI confirmation and the lifecycle checks in [Interaction and Sessions](Interaction_And_Sessions.md).
Done archives, dismissal hides, and deleting removes retained content; these are different operations.
Restart restores readable request/ledger history with interrupted/unknown work, never automatically replays queues or approval tokens.
Pending-request/context execution expiry does not erase durable history or extend session retention.

## Required Concurrent Session Execution

### Ordinary-session resource allocation implementation

The [host allocator](../src/Kora.Application/Dependencies/RuntimeResourceAllocator.cs) issues single-use, owner-checked resource leases before [ordinary retained dispatch](../src/Kora.Application/Hosting/SessionConversationService.Execution.cs). Legacy qualification admits only one exclusive execution across unknown physical resources, including hosted single-flight runtimes; it never implies capacity two.

The trusted [runtime qualification seam](../src/Kora.Application/Dependencies/IRuntimeCapacityQualification.cs) can supply current exact provider/account/profile/runtime-generation evidence for two independently owned transports and canonical shared/exclusive resource identities. The allocator rejects unknown identities, shared-resource/profile conflicts, duplicate session ownership and reused active transports. It bounds FIFO waiting to ten requests and at most fifteen seconds, within original context expiry.

Waiting/running/Stop use the [same exact-request cancellation owner](../src/Kora.Application/Hosting/SessionConversationService.Cancellation.cs). Cancellation does not release resources before physical join; each original request joins only its own lease. Failed joins or release-audit failures quarantine affected resources, preventing reuse rather than claiming cancellation or rollback. Required content-free acquisition/release/quarantine audit uses the existing typed audit sink.

[Reviewed tool-skill submissions](../src/Kora.Application/Hosting/SessionConversationService.ToolSkills.cs) use that same retained-execution join, not an additional host-wide wait. Caller shutdown retains source ownership until the exact dispatched turn joins. Resource admission refusal retains the exact unavailable receipt without dispatching a provider or fetching a page; an unconfirmed owned join still prevents terminal success.

Reviewed stop-and-delete observes the same exact resource join before deletion. A failed physical join or required resource-release audit remains **Stop unconfirmed**, not work-stopped success; confirmed resource closure with an unconfirmed terminal receipt remains **Work stopped; not deleted**. Neither outcome permits deletion or waiting on another session's resource.

This is implementation, not new profile qualification. [Production composition](../src/Kora/OllamaModelProviderFactory.cs) supplies only existing independently qualified single-execution registrations; no production two-capacity qualification source is composed. [Deterministic consumer regressions](../tests/Kora.Application.UnitTests/Dependencies/RuntimeResourceAllocatorTests.cs) exercise admitted capacity two without enabling or advertising it for physical hardware/providers. Exact two-capacity enablement evidence remains required.

Bounded independent session execution, including writes to different resources, is required in the revised Slice A3 design.
Unrestricted parallel agents or multiple concurrent tasks within one session are not implied.

Before enabling it, prove session/task-context isolation, per-task cancellation, enforced resource conflict coordination, dependency checks, provider concurrency budgets, and fair scheduling.
Read-only tasks can still disclose data or contend for model/hardware resources; each retains its own egress controls.
Writes to a shared repository/resource remain exclusive, with base revalidation against outside changes.
Approval prompts remain session/task-scoped; only speech targeting is serialized, not explicitly addressed UI responses.
The ledger keeps identity independent of queue position and records observed resource-wait/provider-limit blockers without inventing progress.
