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

### Delivered closed targeted metadata reads - 2026-10-12

All six existing read-only registry actions now have bounded queued routes. `capabilities.get` takes one explicitly selected exact descriptor ID from the current six-record catalogue. `runtime.get_status` has only the host-fixed `local.inference` target. These are metadata selections, not arbitrary tool requests, parameters, resources or effect authority.

Native Sessions displays exact catalogue ID/schema/input/output/availability metadata with no default target. Enqueue requires an explicit choice, cleared after success or stale source/private/session changes. The fixed runtime button has no runtime-ID input. All six buttons inherit the existing explicit None/exact-task prerequisite and real task-revision guard.

The application builds typed `CapabilityIdInput` through the shared serializer, freshly admits the Native caller for the queued host request and invokes the existing `CapabilitiesGet` or `RuntimeGetStatus` action. The original four inputs stay exactly `{}` with first-page/default count six. Reads never probe, run inference/setup, query provider health or grant model execution.

New targeted entries alone use explicit payload version 5: the capability profile carries one typed exact catalogue target; runtime status cannot carry or override a stored target. Absent/2/3/4 canonical bytes, original admission digests and captured clocks remain unchanged. Unknown/partial/future/mismatched formats or targets fail closed.

Success retains exactly one matching typed Descriptor or fixed Local Runtime observation, within the unchanged complete 4096 UTF-8-byte registry envelope. Shared registry/queue wire validation rejects wrong IDs/shapes, additional result fields and runtime tool-loop qualification claims.
Denied/failed/cancelled/late/private-retired results cannot become successful data; existing monotonic deadlines, receipt/audit fencing and Unknown quarantine remain authoritative.

Work/AllWork, cancellation/move/requeue reviews and targeted local notices expose only closed profile/target metadata, not retained result bodies. Exact private status/Inspect reads the already-saved snapshot without rerunning it. Original notice suppression formats are unchanged; new targeted notice metadata is inert. The complete work projection stays 64 KiB.

Pause, FIFO/order lineage, fair manual batches, captured budgets, selected atomic cancellation, retention and no-replay recovery are unchanged. Reviewed expired replacement preserves the exact profile and descriptor target under separate fresh request/task IDs; it cannot retarget dependents or extend the old deadline.

Maintained [portable contract tests](../tests/Kora.Core.UnitTests/Hosting/SessionQueueTargetedReadTests.cs) and [actual registry tests](../tests/Kora.Application.UnitTests/Tools/ReadOnlyCapabilityRegistryTests.cs) cover closed input/result binding.
[Private SQLite consumers](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionQueueTests.TargetedReads.cs) and [native wiring](../tests/Kora.Windows.IntegrationTests/SessionsQueuePrerequisiteTests.cs) cover the production paths.
No experiment is modified or retired. Unique RT1/RT2/MG1/PV1, offline CPU/inference, containment, hardware, installed-native/accessibility and physical protected-storage/STOP gates remain independent; catalogue Available and retained observations do not qualify live providers/workers or inference. Profile-section security editing remains deferred.

### Delivered optional native enqueue prerequisite - 2026-10-12

Native Sessions now offers explicit **None** or an independent exact prerequisite selection for each of the six fixed-read Enqueue buttons. The picker uses only the existing at-most-50 same-session nonqueue task metadata projection, not task bodies, history, artifacts, providers or another session.
Each inert row shows immutable task/session IDs, observed generation/state/revision, UTC observation time and host source.

None is the initial unchanged route. Enqueue captures the exact selected ID and real task revision through the same `SessionCommand`, application service and audited SQLite enqueue transaction as typed `[after <task-id>]`.
The optional native revision guard restricts that observation to the addressed session and is checked again before physical COMMIT; typed arbitrary exact after-task semantics need no new field and remain unchanged.

Selection is independent of Work/question/cancellation selections and is never inferred by name or index. Work/queue refresh, session/source/generation/private/control changes and closure revoke it. Missing/deleted/changed tasks refuse without falling back to None.
Successful enqueue clears the choice; explicitly choose None or reselect before the next read. Unavailable/stale and empty observations are labelled; no omitted-task scan is performed.

Only exact Succeeded permits normal dispatch. Running/pending/terminal prerequisites may be retained under the existing dependency contract; failed/cancelled/Unknown or pending prerequisites can block the FIFO head. Normal dispatch reevaluates current state, not a frozen success or approval.
Enqueue never dispatches, grants authority, edits existing dependencies or changes clocks, pause/hold, capacity, order, digests or no-replay recovery.

Maintained [actual native wiring and commit-boundary tests](../tests/Kora.Windows.IntegrationTests/SessionsQueuePrerequisiteTests.cs) and [private SQLite contract tests](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionQueueTests.Prerequisites.cs) cover this bounded parity.
Existing queue/order/retention and fixed-profile consumers remain authoritative. Cross-session native selection, automatic/model management and existing dependency editing are not delivered.

No experiment is edited or retired. These deterministic consumer checks do not replace unique RT1/RT2/MG1/PV1, local/offline quality, containment, hardware/storage, installed-native/accessibility or physical-stop qualification.

### Delivered atomic selected pending cancellation - 2026-10-12

Native Sessions has an independent current-run pending-only multi-selection, not the existing task/question selection. Review lists the complete count and exact dispatch-ordered IDs/revisions, profiles and original deadlines. Separate **Cancel these pending entries only** confirms all and only those entries; cancel review keeps the selection and draft without writes.

Exact typed/current-name activated `cancel-selected-review`, `cancel-selected-confirm` and `cancel-selected-discard` share the same host-issued review and audited service/store path. One to the shared maximum ten unique IDs/revisions is allowed, even after configured capacity falls. Input remains 1024 UTF-8 bytes; complete review/receipt projection remains 64 KiB, never silently truncated.

The short existing private SQLite mutation/admission fence validates the entire set, queue revision, exact session generation, current run, pause receipt and private/source admission again at physical commit. One stale/missing/nonpending member or intervening enqueue/admission/removal/reorder refuses all, requiring fresh review; no loop of independently committed single cancels.

Selected rows/tasks become retained Cancelled receipts in the same transaction as requested/terminal typed audit, complete member provenance, existing schema-10 order lineage and terminal control task.
Original positions, enqueue/expiry/captured clocks, canonical formats/digests and unselected data/order remain unchanged. Dependents remain; cancelled prerequisites block them under existing policy, not an automatic descendant cascade.

Pause/hold is preserved on success. Cancellation never dispatches, changes slots/budgets, renews meaningful activity, terminates running callbacks/effects, deletes history or acquires model/grant authority. Expired pending entries remain exactly cancellable, without renewed eligibility; previous-run Interrupted/Unknown/nonpending work refuses. Running callbacks do not hold the review/commit fence.

Audit/write/readback/cancellation/commit uncertainty holds admission with explicit recovery, not partial success, rollback promises or automatic retry. A known commit remains completed if delivery later fails; restart never replays retained work. Session/selection/private closure invalidates native proposals and late delivery cannot render another session.

Maintained [domain selection](../tests/Kora.Core.UnitTests/Hosting/SessionQueueCancellationTests.cs) and [shared service](../tests/Kora.Application.UnitTests/Hosting/SessionWorkspaceServiceTests.QueueCancellation.cs) checks cover this bounded slice.
[Actual private SQLite](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionQueueTests.Cancellation.cs) and [native view-model](../tests/Kora.Windows.IntegrationTests/SessionsViewModelQueueTests.cs) checks exercise the production consumers.

### Delivered reviewed adjacent pending order - 2026-10-12

Native Selected Work offers **Review Move up/down**, exact current/proposed pair order, profiles, immutable task IDs/revisions and original enqueue/expiry times, followed by separate **Confirm exact adjacent move** or **Cancel move review**.
Original typed/current-name activated commands use the same `SessionQueueService` review and audited store transaction. No name/position targeting, inferred reordering, bulk/cross-session shuffle or dispatch is introduced.

An explicit version-1 order lineage in consolidated schema 10 selects a bounded permutation of the exact same-session/current-run pending set. Default/no lineage remains arrival FIFO.
Original `Position`, timestamps, IDs, dependencies, profiles/results, absent/2/3/4 canonical queue payloads and their recorded digests are untouched. Removal/admission removes only its exact pending ID; enqueue appends.
Committed pair receipts and order changes append typed requested/terminal audit, rather than rewriting historical intent.

Confirmation reacquires the short physical mutation/admission fence, revalidating both exact entry revisions, adjacency, queue revision, generation, run, private epoch and current control. A stale pair refuses with refresh/review recovery, never another neighbor.
Pending prerequisite inversion, expired/retired/previous-run pairs and nonpending targets refuse. Cross-session dependencies still require observed success at the chosen head; Unknown/unclassified quarantine, least-recent-admission fairness and the oldest original pending-arrival tie remain unchanged.

Review/cancel/move neither renews meaningful activity nor extends deadlines, pauses/resumes/dispatches work, waits for running callbacks, changes capacity/slots, or grants execution authority. Explicit pause remains independent.
Audit/storage/cancellation/delivery uncertainty holds admission with truthful recovery, not optimistic list publication or a rollback promise. Restart retains order as inspectable lineage but previous-run pending work remains Interrupted, never replayed.

Shared domain ordering drives actual admission, Work/All work, native rows, local event eligibility and queue/status results.
[Deterministic core tests](../tests/Kora.Core.UnitTests/Hosting/SessionQueueOrderingTests.cs) and [shared service tests](../tests/Kora.Application.UnitTests/Hosting/SessionWorkspaceServiceTests.QueueMove.cs) cover portable behavior.
[Real private SQLite/callback tests](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionQueueTests.Move.cs) and [native tests](../tests/Kora.Windows.IntegrationTests/SessionsViewModelQueueTests.cs) cover this bounded increment.
Maintained fixed-version native fixtures retain their unique assertions; no experiment is modified or retired. Full R13, model management, providers/workers/resource leases, installed-native/hardware/accessibility, RT2/PV1 and physical-stop qualification remain separate gates.

### Delivered fixed first-page read profiles - 2026-10-12

The existing version syntax/default is unchanged. Native Sessions offers three
additional explicit enqueue buttons; typed/current-name activated enqueue accepts
the exact `capabilities.list`, `readiness.get` or `runtime.list` profile token.
All four invoke the same registry and existing C# actions, not another dispatcher.
These original four accept no capability ID argument, JSON, count, offset, refresh, provider/model selection,
grant or execution descriptor is accepted.

New page entries use explicit payload format 4 with a closed typed profile and
the same captured pending/active clocks. Unknown/missing profiles and malformed
or downgraded formats fail closed. Original absent/2/3 canonical bytes, audit
digests and transitions stay unchanged; the version default continues formats
2/3 and its original typed transient Version receipt.

Successful page results are typed fixed metadata, bounded by the authoritative
registry wire envelope (six records, 4096 serialized UTF-8 bytes). The private
queue row and its committed digest bind the result to the exact original profile,
request/session/admission and terminal outcome. Failure/denial, shape mismatch,
oversize, cancellation or late output never publishes successful data. Result
admission and publication recheck current private epochs and strict monotonic
budget; uncertain storage remains Unknown, not invented success or replay.

Manual dispatch exposes results; native Inspect selected work and exact
`queue status` retrieve the retained page snapshot. The 64 KiB batch limit may
omit page data with an explicit exact-status recovery marker, without losing
receipts or creating another dispatch. Work/AllWork/events remain metadata-only;
passive status does not invoke, probe, renew clocks/activity or requeue.
Removed-session status is unavailable; existing retention deletion removes
queue rows. No raw probe detail, user content, model output or arbitrary dump
enters the authority row or audit (which retains its typed change digest only).

The [real private SQLite/registry tests](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionQueueTests.Profiles.cs)
and [native consumer tests](../tests/Kora.Windows.IntegrationTests/SessionsViewModelQueueTests.cs)
cover the new routes. Maintained native-UX fixtures continue their original
version-only source profile; no maintained experiment consumes the new profiles.
No experiment is edited or retired. Installed/native accessibility, hardware,
provider/runtime containment and broader R13 management remain independent E/Q
qualification gates, not claims of this deterministic metadata increment.

### Delivered exact current-run dispatch pause - 2026-10-11

Native Sessions and original typed/current-name activated `queue pause`/`queue resume` share `SessionQueueService` and the consolidated host store. `queue pause-status`/Work/All work observe the same host-owned run receipt and domain eligibility reasons, never a view-model readiness heuristic.
Controls bind the existing exact session ID/generation, queue revision, current run ID, process-memory pause revision and unchanged private control epoch. Fresh runs are unpaused; no preference/schema or stored pause authority is introduced. Requested/terminal typed audit and the original control task receipt precede publication under the same exclusive short lease as physical dispatch COMMIT.
Pause blocks NEW admissions only: a committed callback drains normally, including its captured active deadline and final receipt. It does not report stop, suspend/cancel work, clear pending entries, consume grants, reprioritize, change clocks, mark Done or extend meaningful activity/retention.
An ongoing bounded fair manual batch skips the paused session without bypassing its FIFO head or Unknown quarantine; other eligible sessions remain fair candidates. Neither the dispatch batch gate nor running callbacks hold the pause fence.
Resume withdraws only the current matching explicit pause. Dependencies/current policy are revalidated by normal atomic admission; resume never dispatches automatically, restores private-epoch eligibility or replays previous-run Interrupted/Unknown work.
Unconfirmed audit/storage/cancellation/outcome state retains an explicit admission hold with truthful recovery, not default-unpaused state or invented rollback certainty. Old generations, runs, origins, closed sources and stale receipts cannot mutate the current control. Pause and routine-notice quiet mode remain independent fixed safety controls, not grants or permission levels.

See [exact commands and native controls](../docs/commands.md#deterministic-local-version-queue) and [maintained real SQLite fence/callback tests](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionQueueTests.Pause.cs). General workers/providers, autoscheduling, current-task cancellation and installed-native qualification remain unavailable/separate.

### Delivered passive cross-session work observation - 2026-10-11

The [native All work / attention overview](UI_Workspace_And_Windows.md#delivered-bounded-passive-all-work--attention-overview---2026-10-11) consumes existing admitted metadata and per-session atomic work reads, never another authority database or global observer.

Each manual call bounds current work reads to ten session identities, plus bounded metadata lookahead, existing fifty task/question limits and one complete 64 KiB projection, with a ten-second storage timeout and explicit Cancel.

Stored Active/Done, exact immutable IDs/generations/metadata revisions, authority/queue revisions, UTC observation time, confirmed fixed-profile slots/capacity, observed current fixed reads, captured pending/active deadlines, queue policy reasons, task states and pending-question identities retain their original source.

No duplicate readiness/blocked policy, provider resource contention, ETA, unread durability or full-scope count is invented. Bodies/history/memory/attachments/diagnostic text do not enter this projection.

All/Active/Done and observed needs-answer are bounded scan/snapshot filters. Nonmatches advance the query-bound cursor, including zero-match pages; byte-deferred IDs do not. Omissions/unavailable/oversized groups are explicit, never empty-success or all-clear. Pages are independently observed mutable keysets, not globally atomic; behind-cursor changes require Refresh.

Native navigation rereads one exact session through private owner/call/source admission and cannot resume/reorder/dispatch/cancel/enqueue, consume grants, renew activity or retarget voice/questions. Lifecycle/disposition/retention/hold changes invalidate rows and in-flight reads. Closure clears private page/filter/IDs and cancels late callbacks; browsing adds no cleanup or timer.

Full work management, multi-session effects/controls, general dispatch/provider coordination and installed-native qualification remain separate. Permission-level proposal #168 is design only; no tiers or grant controls are adopted.

`SessionQueueService` and the consolidated store own a complete bounded
read-only slice: exact existing session IDs, original-user request/task IDs,
session generation, entry revision and queue revision; default ten pending entries
per session; one admitted current task per session; host default one global
slot (fixed read-only limits 1–2); FIFO heads and deterministic
least-recent-admission session fairness. A continuously ready session gets a
turn within one admission per other continuously ready session; blocked heads
never gain priority or bypass an earlier pending entry.

The original fixed work profiles are `application.get_version`, `capabilities.list`,
`readiness.get` and `runtime.list`, now joined by the closed targeted reads above, through the existing read-only registry with a
freshly admitted Native caller bound to the actual queued request/session. The
three page reads always supply host-owned `{}` (first page, default count six).
Readiness/runtime return only already-recorded bootstrap observations, never
a new probe, setup or inference. There are no
arbitrary user-provided arguments, content snapshots, model calls, execution tokens,
arbitrary resources or effect descriptors. IDs/revisions/dependencies are
metadata, not executable proposals or grants.

Enqueue is not dispatch. The shipped user-selected policy is manual:
`queue dispatch <session-id> <generation> <queue-revision>` processes at most
32 already enqueued current-run ready fixed reads fairly across eligible
sessions, never targets work by name or gives the invoking session priority.
Newly observed failure stops this dispatch batch; later work needs another
explicit decision. Exact pre-admission cancel/remove and confirmed pending
clear leave task receipts/history intact, cannot terminate admitted work and
never clear another session. Duplicate IDs, stale revisions, full capacity,
unknown subjects/profiles and authority failures refuse without eviction.

The pending lifetime defaults to 30 minutes, configurable within 1-120 integer
minutes for future newly enqueued admitted fixed reads only. Each entry captures
its lifetime at enqueue; later edits never reinterpret or change that deadline.
Expired heads remain visible and non-dispatchable until explicit removal/clear or the separately reviewed current-run removal/new-decision transaction below. The active budget defaults to five
minutes, configurable within 1-60 integer minutes for future admissions only.
The chosen budget and precise UTC deadline are captured once with the original
dispatch; monotonic elapsed time includes commit/dispatch delay and must be strictly
less than that budget at invocation/receipt/publication to permit success. Equal/late
results suppress data; failures, denial, wrong shapes and oversize output cannot
become Succeeded.
No general asynchronous worker deadline/termination is
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

The delivered [R10/R13 configuration workflow](User_Configuration.md#delivered-bounded-fixed-local-version-queue-settings-r10r13) admits device-local pending capacity **1-10/default-reset 10**, fixed read-only slots **1-2/default-reset 1**, future pending lifetime **1-120 minutes/default-reset 30** and future active budget **1-60 minutes/default-reset 5** only.
Native Settings and exact typed/current-name activated commands share host-held revisions, original-input privacy/call/ownership admission, atomic preferences, required audit/readback and durable control receipts.

The [queue service](../src/Kora.Application/Hosting/SessionQueueService.cs) reads current confirmed limits for each enqueue and fair admission, not once per batch; [native/exact work](../src/Kora.Application/Hosting/SessionWorkspaceService.cs) and [event observation](../src/Kora.Application/Interaction/AuthorityLocalEventSource.cs) consume the same configuration.
Short consuming transactions serialize with edits and refuse stale revisions or changed saved state.
The gate is released before active callbacks, preserving independent configuration/management.
Lowering pending capacity never evicts existing work; lowering slots never cancels active admissions.
FIFO, fairness and the existing pending/admission deadlines remain unchanged ([deterministic races](../tests/Kora.Application.UnitTests/Hosting/SessionWorkspaceServiceTests.Queue.cs), [durable deadline checks](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionQueueTests.cs)).

Pending lifetime is captured at new enqueue after confirmed activation. Active budget is captured at future admission, including from an old pending entry, without modifying pending metadata. Editing the default never alters admitted/running/Unknown/terminal records or their results; user-question expiry and R08 model deadline remain independent.
Queue payload format 2 binds the chosen lifetime and exact expiry in the committed enqueue change digest. Legacy field-absent records remain exact fixed-30 and keep original bytes/digests; mixed known formats reopen without database schema migration. Partial/unknown/downgraded/unbound records refuse without replacing data.
Payload format 3 captures active minutes/precise UTC admission/deadline, validated against the original dispatch audit/digest even after terminal changes. Already-admitted legacy/format-2 rows retain five minutes from the original audit. Same-run monotonic admission receipts drive actual enforcement; restart never reuses them. No database rewrite/migration occurs.
Preference schema 3 preserves valid schema-1/2 prior fields and supplies only new defaults (pending 30 for schema 1; active 5 for both), without write-on-read. Unknown/malformed/unconfirmed state holds new admissions with explicit recovery, never defaults.
Current Work/status and current-event eligibility consume the captured active deadline, not mutable configuration or pending/question clocks. Cancellation/late receipt handling does not physically stop callbacks: admission drains them; uncertainty remains Unknown/quarantined.
No apply-now/current-task extension, automatic dispatch, general execution, effect/resource/provider workers or real two-slot qualification is added ([domain limits](../src/Kora.Core/Hosting/SessionQueueLimits.cs), [record validation](../src/Kora.Core/Hosting/SessionQueueEntry.cs), [actual dispatch tests](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionQueueTests.ActiveDeadline.cs)).

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

### Delivered reviewed expired pending removal and fresh tail decision - 2026-10-12

Native Sessions and [exact typed/current-name activated commands](../docs/commands.md#deterministic-local-version-queue) review one exact expired **stored Pending**, never-admitted fixed read in the active session/current run and original private admission epoch.
Expiry is the original captured UTC deadline at or before now; previous-run Interrupted, Unknown, terminal, running and privacy-retired work are not replacement sources.

The host issues separate fresh request/task IDs and displays the old request/task, original enqueue/expiry/profile/dependency, generation/entry/queue/order/pause/configuration revisions and new captured UTC enqueue/lifetime/expiry.
Separate exact confirmation removes only the old source from dispatch, retaining Removed queue history and its Cancelled task outcome, and enqueues a new same-profile decision strictly at the authoritative session tail.

The [shared store transaction](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.QueueRequeue.cs) validates stored state, old task/run, private/source admission, current settings, capacity and complete reviewed lineage under the existing short private SQLite admission fence.
One freed slot is used in that same transaction; lowered capacity below the resulting total refuses without eviction.
Typed REQUESTED/terminal audit, exact original/fresh association and readback precede publication; uncertain commit/delivery retains an explicit admission hold.

All six fixed profiles retain their existing formats and exact closed target under normal manual dispatch policy. The original payload format, UTC deadlines, IDs and historical digests remain evidence; only truthful removal/terminal revisions change.
Active budget is captured only at later normal dispatch. Cancel review admits no fresh IDs and changes nothing; stale proposals refuse wholly, without retry or target substitution.

The original prerequisite ID is retained and revalidated, never silently retargeted. Current same-session pending prerequisites before the new tail or exact durable Succeeded prerequisites are admitted; missing, invalid or cyclic lineage refuses.
Existing dependents of the removed task stay blocked; no cascade or global descendant count is claimed. Unselected order, fairness, slots, running callbacks and pause/hold fences remain independent.

This is a new explicit user decision, not deadline extension, old-request replay, success/result copying or automatic dispatch/resume. Restart projects pending work Interrupted and admitted uncertainty Unknown without replay. Passive preview/list does not renew meaningful activity.
[Storage tests](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionQueueTests.Requeue.cs) exercise actual private transactions; installed-native, hardware, physical-stop and broader worker/provider qualifications remain open.

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
- Default order is arrival order. Contextual reordering requires a clear user instruction and acknowledgement; the delivered bounded route is the separately reviewed exact adjacent pending pair above.
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
