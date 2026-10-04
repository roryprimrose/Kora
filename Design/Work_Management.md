# Work Management and Request Queue

Status: proposed full Slice A capability. The current implementation tracks bootstrap setup
tasks/progress and answers deterministic status commands; this is not proof
that the contextual work-management lane and executor below are shipped.

Related: [Architecture](Architecture.md), [Task Lifecycle](Task_Lifecycle.md), [Security and Data Flows](Security_Data_Flows.md), [Acceptance Criteria](Acceptance_Criteria.md).

## Two Independent Lanes

Kora has a work-management lane and a task-execution lane.
Management remains available while the executor is planning, waiting for approval, running a tool, or cancelling.
Only one task occupies the execution slot; management inference is not a second task executor.

The management lane:

- Interprets new requests against the current task and queue.
- Proposes queue additions, edits, ordering, removal, replacement, or clarification.
- Answers current-work and remaining-work questions from an authoritative ledger.
- Routes approval replies to the correct task and request.

The execution lane performs the admitted task's model/tool workflow.
Management cannot run task tools, acquire arbitrary context, or approve task actions.
Host-owned, explicitly requested context capture still uses the context broker.

## Contextual Decisions, Not a Fixed Queue Prompt

Use the model to decide what a new utterance means when context makes that decision clear.
Do not ask "queue or replace?" on every request.

| User request while busy | Expected interpretation |
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
If management inference fails or is unavailable, show the limitation and ask explicitly how to handle an ambiguous new request.
Local-only mode does not call a remote management model.

## Management Operating Envelope and Degraded Mode

The management lane is optional inference around a mandatory deterministic host core.
Exact cancel/stop/pause/clear commands, direct task-ID operations, queue listing, and factual ledger status never require management inference.
When inference is unavailable or budget-limited, an ambiguous request receives native choices such as Queue, Replace current, or Cancel; it is never guessed, dropped, or treated as task approval.

Initial per-profile limits:

- At most one management inference request in flight.
- Input is capped at 32 KiB of minimal approved request/ledger context; output is capped at 4 KiB and must parse as the typed proposal schema.
- The host deadline is 15 seconds with no automatic provider retry; the user may retry explicitly.
- Remote management is capped at 30 calls per rolling hour. Reaching the cap activates deterministic degraded mode and reports when capacity returns.
- Provider cost/quota/rate-limit responses never consume task approvals, switch providers, or borrow the execution session.
- Queue/status controls remain responsive while the provider is offline, signed out, throttled, over budget, or prohibited by local-only policy.

Provider enablement requires evidence that independently schedulable task and management sessions are allowed by the pinned SDK/API behavior, account tier, terms, and rate limits.
Record the tested version/tier and estimated worst-case management usage; do not claim concurrent management when the provider serialises or forbids it.
Users can disable model-assisted management, in which case deterministic choices are always used.

## Authoritative Work Ledger

The host owns records containing:

- Task ID, user-visible label, request reference, creation time, and queue position.
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
- A queue entry expires after 30 minutes waiting by default; notify the user and release its content references. Extending/requeuing requires a new decision. User-configured lifetimes follow [User Configuration](User_Configuration.md), including confirmation of affected entries.
- The active-task deadline begins when dispatched, not while queued.
- Normal successful completion dispatches the next ready entry automatically by default, after policy/context revalidation; user-selected manual dispatch adds a start decision. Queuing or approving dispatch is not action approval.
- Failure, cancellation, or unknown side effects pause automatic dispatch and explain why. The user can resume or explicitly choose a replacement.
- A voice cancellation request immediately pauses new dispatch; destructive cancellation requires owner presence and native confirmation, then preserves unrelated pending entries.
- "Clear the queue" pauses dispatch and requires owner presence/native confirmation before removing the displayed pending entries.
- "Stop all work" pauses dispatch immediately and requires owner presence/native confirmation before cancelling active execution and clearing pending entries.
- "Pause the queue" blocks new dispatch without suspending/cancelling the active task; resume still revalidates dependencies/policy.
- A replacement waits for host-side execution quiescence. If remote work may still be running, report the uncertainty and require an explicit decision before dispatching more work.
- Scheduling mutations and slot acquisition are atomic host operations. Concurrent model proposals with stale revisions are re-evaluated, not blindly replayed.

## Context and Approval Timing

Bind explicit "this clipboard" requests to the requested immutable snapshot at admission; do not read a later clipboard value silently.
If source selection is ambiguous, clarify before capturing it.
Queue interpretation receives only the context required to identify the work, not all snapshots or tool outputs.
Raw clipboard, retrieved documents, tool results, rendered content, and skill instruction bodies are never sent to management inference.
Host-derived task IDs, safe labels, state, dependencies, and the authenticated user management utterance are structurally separated; untrusted task labels cannot become management instructions.
Any inferred cancellation, replacement, removal, or reordering proposal pauses at native confirmation rather than mutating the ledger directly.

At dispatch, revalidate context existence/expiry, identities, dependencies, tool capabilities, policy, and any changed write base.
Missing/expired context requires a fresh selection or recapture decision.
Approval to add/reorder a task is not approval to transmit its content, execute a tool, apply a patch, or commit.
Task action approvals are obtained at the relevant execution point and are never shared with another queued task.

## Interaction and Recovery

Management status uses the same visual/TTS presenter but has its own response identity.
An explicit status question takes speech priority; task execution continues and its visual progress remains visible.
Call-aware policy still gates that speech; priority does not bypass suppression or a separate prompt's approval identity.
Do not overwrite an approval card with a queue acknowledgement, or interpret a queue clarification reply as action approval.
Only one voice prompt is foreground at a time, with an explicit prompt ID and expiry.
Showing another prompt withdraws the previous prompt's response eligibility; a paused action must be re-presented before accepting approval.
Do not silently extend an action approval's expiry while managing the queue.

Clearing a conversation first pauses its work, then requires owner presence/native confirmation for the displayed active/pending/context deletion set.
Confirmed deletion releases ephemeral ledger content; interrupted work remains visible until that decision so a bystander cannot erase it by voice.
Restart never restores/replays queued requests under the default memory-only retention policy.
Persist only content-minimising interruption/outcome metadata, not queue labels, plans, or request bodies.

## Future Parallel Task Execution

Concurrent management is required now; concurrent task execution is a separate potential extension of the scheduler.
The proposed first step would be a bounded number of independent read-only tasks, not unrestricted parallel agents.
This is not enabled in the MVP.

Before enabling it, require task-context isolation, per-task cancellation, resource conflict declarations, dependency checks, provider concurrency budgets, and fair scheduling.
Read-only tasks can still disclose data or contend for model/hardware resources; each retains its own egress controls.
Writes to a shared repository/resource must remain exclusive until a tested coordination strategy exists.
Approval prompts must remain task-scoped and serialised for the user, even when execution is concurrent.
The ledger/scheduler contracts keep task identity independent of queue position so a future execution-slot limit can change without redesigning status or cancellation.
