# Work Management and Request Queue

Status: proposed full Slice A capability. Bootstrap setup progress and deterministic
status coexist with delivered exact session/task controls, bounded authoritative
observation and atomic current-run local-version pre-dispatch wait cancellation
in consolidated schema v5, retaining schema-v4 ordered history. A fixed
local-version deterministic queue is delivered as described below. This is not
proof that contextual model routing, concurrent effect workers or the broader
management/execution lanes below are shipped.

Related: [Interaction and Sessions](Interaction_And_Sessions.md), [Architecture](Architecture.md), [Task Lifecycle](Task_Lifecycle.md), [Security and Data Flows](Security_Data_Flows.md), [Acceptance Criteria](Acceptance_Criteria.md).

## Delivered deterministic local-version queue

`SessionQueueService` and the consolidated store own a complete bounded
read-only slice: exact existing session IDs, original-user request/task IDs,
session generation, entry revision and queue revision; default ten pending entries
per session; one admitted current task per session; host default one global
slot (fixed read-only limits 1–2); FIFO heads and deterministic
least-recent-admission session fairness. A continuously ready session gets a
turn within one admission per other continuously ready session; blocked heads
never gain priority or bypass an earlier pending entry.

The fixed work profile is only `application.get_version` through the existing
read-only registry with a freshly admitted Native caller. There are no
user-provided arguments, content snapshots, model calls, execution tokens,
arbitrary resources or effect descriptors. IDs/revisions/dependencies are
metadata, not executable proposals or grants.

Enqueue is not dispatch. The shipped user-selected policy is manual:
`queue dispatch <session-id> <generation> <queue-revision>` processes at most
32 already enqueued current-run ready local reads fairly across eligible
sessions, never targets work by name or gives the invoking session priority.
Newly observed failure stops this dispatch batch; later work needs another
explicit decision. Exact pre-admission cancel/remove and confirmed pending
clear leave task receipts/history intact, cannot terminate admitted work and
never clear another session. Duplicate IDs, stale revisions, full capacity,
unknown subjects/profiles and authority failures refuse without eviction.

The pending lifetime defaults to 30 minutes, configurable within 1-120 integer
minutes for future newly enqueued admitted fixed reads only. Each entry captures
its lifetime at enqueue; later edits never reinterpret or change that deadline.
Expired heads remain visible and
non-dispatchable until explicit removal/clear. The five-minute active budget
starts at admission; a read returning after that budget cannot commit a
successful receipt. No general asynchronous worker deadline/termination is
claimed. This profile has no runtime approval/clarification step. Existing
admitted question waits are pre-dispatch, occupy no scheduler slot and have no
active-task clock to extend; their separate host gateway/expiry is preserved.
Broader in-task wait continuation remains unavailable, never inferred from
arbitrary question records.

Exact durable Succeeded dependencies are required. Unknown outcomes and
unclassified nonterminal work quarantine their session; dependent heads
elsewhere block while unrelated sessions remain eligible. No effect lease
can be invented to bypass uncertainty. Session/call/privacy/owner admission
is revalidated at the commit and immediate read/receipt boundaries.
Lock/unlock or ownership changes retire queued eligibility even if the desktop
later becomes private again; remove/requeue explicitly. No passive extension.

Restart never runs queues: previous-run pending work projects Interrupted,
previous dispatch projects Unknown, and existing startup recovery commits
task outcomes without replay. Exact status still identifies those retained
receipts. History ordering/citations, disposition redaction, independent
Perpetual grants and volatile inspection/caption isolation remain unchanged.

See [exact syntax](../docs/commands.md#deterministic-local-version-queue).
The rest of this document describes the broader proposed lanes; this bounded
core is not MG1 production inference or two-slot effect/provider qualification.

### Bounded queue settings

The delivered [R10/R13 configuration workflow](User_Configuration.md#delivered-bounded-fixed-local-version-queue-settings-r10r13) admits device-local pending capacity **1-10/default-reset 10**, fixed read-only slots **1-2/default-reset 1** and future pending lifetime **1-120 integer minutes/default-reset 30** only.
Native Settings and exact typed/current-name activated commands share host-held revisions, original-input privacy/call/ownership admission, atomic preferences, required audit/readback and durable control receipts.

The [queue service](../src/Kora.Application/Hosting/SessionQueueService.cs) reads current confirmed limits for each enqueue and fair admission, not once per batch; [native/exact work](../src/Kora.Application/Hosting/SessionWorkspaceService.cs) and [event observation](../src/Kora.Application/Interaction/AuthorityLocalEventSource.cs) consume the same configuration.
Short consuming transactions serialize with edits and refuse stale revisions or changed saved state.
The gate is released before active callbacks, preserving independent configuration/management.
Lowering pending capacity never evicts existing work; lowering slots never cancels active admissions.
FIFO, fairness and the existing pending/admission deadlines remain unchanged ([deterministic races](../tests/Kora.Application.UnitTests/Hosting/SessionWorkspaceServiceTests.Queue.cs), [durable deadline checks](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionQueueTests.cs)).

Pending lifetime is captured at new enqueue after confirmed activation, never applied to existing pending/cancelled/expired/running work. Active budget remains **5 minutes**, unavailable to edit; user-question expiry is independent.
Queue payload format 2 binds the chosen lifetime and exact expiry in the committed enqueue change digest. Legacy field-absent records remain exact fixed-30 and keep original bytes/digests; mixed known formats reopen without database schema migration. Partial/unknown/downgraded/unbound records refuse without replacing data.
Preference schema 2 adds the minutes override; valid known schema 1 preserves capacity/slots and gives the new option default 30 without a refresh-time write. Unknown/malformed/unconfirmed state holds new admissions with explicit recovery, never defaults.
No apply-now, automatic dispatch, general execution, effect/resource/provider workers or real two-slot qualification is added.
The broader proposed capacity/active-deadline contract is not enabled ([domain limits](../src/Kora.Core/Hosting/SessionQueueLimits.cs), [record validation](../src/Kora.Core/Hosting/SessionQueueEntry.cs)).

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

The [Sessions workspace](UI_Workspace_And_Windows.md#sessions-workspace) presents the session list beside full conversation/history, per-session task/queue, and All work.
The compact latest-interaction view shares selected session identity, not an execution slot; background progress/attention never replaces another session's conversation or question.
Window visibility, selection, and detail viewing do not cancel, prioritize, or merge work.

The host owns records containing:

- Session/task IDs, user-visible label, request reference, creation time, and per-session queue position.
- State: Needs Clarification, Queued, Active, terminal outcome, or Expired.
- Context references and capture times, selected identity/runtime, and declared dependencies.
- Observed current stage, last event time, blocker, and approval reference.
- Planned steps with Planned, In Progress, Completed, Failed, or Unknown status.
- Side-effect receipts and cancellation certainty.

Model-reported plans are intentions, not evidence of completion.
Only observed events and receipts establish completed steps.
Status reports distinguish "planned", "confirmed complete", "waiting", and "unknown".
If a runtime does not expose remaining steps, say that they are not known; report the stage and queue without inventing a checklist or ETA.
Status is labelled with its observation time when the underlying operation has not updated.
Task labels, requests, and step descriptions are potentially sensitive content, not content-free audit metadata.

## Queue and Dispatch

- Proposed initial capacity: 10 pending entries, configurable. Full means ask the user to remove/defer work; never evict silently.
- Default order is arrival order. Contextual reordering requires a clear user instruction and acknowledgement.
- A task needing clarification keeps its position and blocks dispatch at that position until clarified, removed, or explicitly deferred.
- Explicit dependencies must succeed before dependent work starts. Failed, cancelled, expired, or unknown prerequisites block dependent dispatch.
- A queue entry's execution eligibility expires after 30 minutes waiting by default; notify the user and release active context references, retaining its request/expiry as session history. Extending/requeuing requires a new decision and context revalidation. User-configured lifetimes follow [User Configuration](User_Configuration.md).
- The active-task deadline begins when dispatched, not while queued.
- Normal successful completion dispatches the next ready entry automatically by default, after policy/context revalidation; user-selected manual dispatch adds a start decision. Queuing or approving dispatch is not action approval.
- Failure, cancellation, or unknown side effects pause automatic dispatch and explain why. The user can resume or explicitly choose a replacement.
- A cancellation request immediately pauses the addressed dispatch; deliberate exact voice or UI confirmation completes cancellation while preserving unrelated pending entries.
- "Clear the queue" pauses the addressed queue and requires explicit voice or UI confirmation before removing its displayed pending entries from dispatch, not history.
- "Stop all work" explicitly targets all sessions, pauses dispatch immediately, and requires exact affected-work voice or UI confirmation before cancel/clear.
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

Bounded independent session execution, including writes to different resources, is required in the revised Slice A3 design.
Unrestricted parallel agents or multiple concurrent tasks within one session are not implied.

Before enabling it, prove session/task-context isolation, per-task cancellation, enforced resource conflict coordination, dependency checks, provider concurrency budgets, and fair scheduling.
Read-only tasks can still disclose data or contend for model/hardware resources; each retains its own egress controls.
Writes to a shared repository/resource remain exclusive, with base revalidation against outside changes.
Approval prompts remain session/task-scoped; only speech targeting is serialized, not explicitly addressed UI responses.
The ledger keeps identity independent of queue position and records observed resource-wait/provider-limit blockers without inventing progress.
