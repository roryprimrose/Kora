---
name: kora-orchestrate
description: 'Use for Kora child-chat orchestration, delivery starvation, stalled owners, reservations, and finite message-driven coordinator turns. No automation.'
---

# Kora event-turn orchestration

## Runtime

Runtime support: `LocalOnly`. Requires a local Windows checkout and discovered host session tools; no Azure SRE-native execution adapter is supplied. Scripts validate local inputs, not host configuration or authority.

## Bootstrap and transport

1. Discover the exact coordinator chat and existing related owners using host
   session tools. Include nested chat IDs, not just the enclosing session.
2. Recover the registry; do not start a second workflow for the same owners.
   Verify repository-owned `.scratch/` is ignored before writing checkpoints.
   Use an absolute `.scratch\orchestration\<coordinator-id>` path outside any
   worktree scheduled for removal. Pass that path and required access to children.
3. Discover supported session/messaging tools. Related chats use
   `currentSession`; set an explicit workspace and isolation for worker writes.
   Do not use blocking subagents, synchronous waits, or automatic replacements.
4. Record requested/effective model settings separately. Request `gpt-6.1-sol`
   for workers. Apply the worker agent/high effort only through supported host
   controls; the current `create_session` API has no agent/effort parameter.
   Loading a contract in prompt text does not select its profile. Unexposed
   effective settings are permitted but must remain `UNVERIFIED`.
5. Reserve the reusable planner before launch. Give it the
   [planner contract](./planner.md), exact return address, registry exclusions,
   current source, separate available capacities, and a bounded scan frontier.
   Require STARTED and incremental reports, not a blocking whole-pass result.
6. Finish bootstrap after dispatch. Do not hold the coordinator turn open
   awaiting STARTED or worker completion. Persist `enabled=true`; ending a turn
   must not archive the coordinator or disable the workflow.

Every child report uses host messaging before the child ends. Read coordinator
status immediately before sending: **steer an active turn; queue a separate
turn when idle**. Record the returned message ID/status. If steering is refused
because the target became idle and no delivery occurred, queue once. Do not
blindly replay an ambiguous send. Event IDs make retries idempotent.

Queue success proves acceptance, not consumption or idle resumption. On a genuine
event, read a known child's context/status to recover missing reports. Do not
poll. Inactivity means suspect, not stopped; retain ownership. If no child report,
host notification, or user event arrives, this workflow cannot wake itself.
Report `AwaitingExternalEvent`, not guaranteed continuous operation.

## Registry and deterministic decisions

Use [the registry reducer](./scripts/registry.cjs) for every state change. It
enforces pool limits, duplicate ownership, overlapping scopes, freshness,
publication-before-release, coalesced discovery, and paused-state dispatch.
It does **not** verify GitHub, filesystem, host configuration, or receipts:
the coordinator must derive its verified inputs from concrete child evidence.

The CLI reads one JSON object from standard input and writes one JSON result;
it never schedules, sends messages, accesses a registry/network, or persists files.
Initialize with `{"operation":"init","coordinator":"<exact chat>","head":"<main SHA>","mode":"observe"}`.
`observe` is the default and never emits implementation launch actions.
Use `"mode":"delivery"` only with recorded user ready-PR/repair authority.
Authority is fixed at initialization: do not silently promote a discovery pilot.
If source is not yet known, explicitly use `"head":null`: reserve/start the
planner immediately, but admit no candidates until its verified SOURCE_CHANGED
report supplies a `refresh` head. Do not inspect production yourself or invent
a SHA merely to bootstrap the registry.
Subsequent input is `{"state":<checkpoint>,"event":<event>}`.

Example using an approved absolute scratch input path:

```powershell
Get-Content -LiteralPath $InputPath -Raw |
    node .\.github\skills\kora-orchestrate\scripts\registry.cjs
```

For live operation use the [durable checkpoint CLI](./scripts/checkpoint.cjs),
not a conversation-only JSON blob. It wraps the same reducer with an atomic
checkpoint, exclusive write lock, expected revision, and pending-action journal.
It has no timer, scheduler, network, session tool, or autonomous execution.

Pass the absolute approved scratch checkpoint path as its only argument, and
one input object on stdin:

```powershell
Get-Content -LiteralPath $InputPath -Raw |
    node .\.github\skills\kora-orchestrate\scripts\checkpoint.cjs $CheckpointPath
```

- `init`: `expectedRevision=0`, coordinator, head, and authorized mode.
- `read`: no mutation; recover state, revision, action journal and event times.
- `apply`: current `expectedRevision` and one reducer `event`.
- `ack`: current `expectedRevision`, `actionId`, `status=done|blocked`, and
  concrete `evidence` (exact child/message identity, verified result, or blocker).

Never treat a pending action as a successful send. Reconcile unknown results
through the exact host chat before acknowledging. A lock failure or stale
revision is an explicit ownership/concurrency blocker; do not delete another
writer's lock or overwrite state. Atomic replacement failure preserves the old
checkpoint. This local helper is synchronous and exits after one operation.

Persist the returned state and pending actions before carrying out those actions.
Keep action statuses, host message IDs/revisions, and receipt links beside that
state in session storage or approved scratch. One coordinator is the only
registry writer. On resume, reconcile pending actions before replaying anything.
Ambiguous launch results retain the reservation: inspect known sessions before
any retry. A reserved item without an attached owner is not permission to create
a second child.

| Event | Required inputs / action |
| --- | --- |
| `planner` | Attach its exact `owner`; at most one reusable planner. |
| `candidate` | Planner `owner`, assigned `pass`, `key`, unique `outcome`, `pool`, `kind`, `head`, `scopes`, admission `evidence`; repair also requires `pr`. |
| `revalidate-candidate` | Active planner `owner/pass`, existing unowned candidate `key`, complete `expectedItemDigest`, current `head`, canonical complete `scopes`, fresh `evidence`, `manifestComplete=true`. Atomic current-checkpoint apply; held conflict rejects without mutation. See [same-key input and migration](./README.md#same-key-unowned-candidate-revalidation). |
| `adopt` | Existing worker `owner` and the candidate fields, with concrete exclusive-ownership evidence; no new launch. |
| `attached` | Reserved `key`, exact worker `owner`, and optional assigned `pr`. |
| `launch-blocked` | Unattached reserved `key` and explicit `reason`; retain slot, scope and unknown launch evidence. |
| `published` | Worker `owner/key`; coordinator-verified `proof`: matching `pushedHead/remoteHead`, `pr`; ready also `ready=true`, `enrolled=true`. |
| `blocked` | Worker `owner/key`, explicit `reason`; retain slot and scope. |
| `refine-scopes` | Attached stopped running/published `owner/key`, `expectedScopeRevision`, complete versioned `proof` and fresh exact-owner `acknowledgment`; shrink only, retain ownership and cleanup. See [exact input and sequencing](./README.md#shrink-only-reservation-protocol). |
| `extend-scopes` | Attached running nonretired `owner/key`, current `expectedScopeRevision`, complete actual/planned/preservation, temporary pause, exact additional union/exclusivity `proof` and fresh whole-proof `acknowledgment`. Coverage only; continuation is separate. See [mutable input and migration](./README.md#active-mutable-owner-scope-extension). |
| `supersede-write-reservations` | Versioned whole `proof` and explicit human `authorization`, bound to the current durable revision/source/coordinator, complete candidate and every exact prior holder. Local-ledger activation only; external preparation never launches. See [exact input and limitations](./README.md#exact-path-write-reservation-supersession). |
| `completed` | Published worker `owner/key`; verified cleanup `proof`: matching `remoteHead`, `stoppedWriting`, `worktreeAbsent`, `branchAbsent`, `pendingInstructions=0`. |
| `pass-ended` | Planner `owner`, assigned `pass`, `readyExamined=true`, `repairExamined=true`, boolean `remaining`; optional shortage receipt. |
| `refresh` | Verified current `head`; invalidate stale undispatched candidates and coalesce discovery. |
| `pause` | Disable dispatch, notify owned children; retain unfinished ownership. |
| `handoff` | Paused workflow, exact `fromCoordinator/toCoordinator`, verified stopped old-owner addresses and `pendingInstructions=0`; retain reservations and stay paused. |
| `resume` | Exact coordinator `owner`, `authorityConfirmed=true`, explicit `discover=true|false`; preserve authority mode. |

Each event needs a unique `id`. Unknown/malformed events fail explicitly.
Resume clears cached source freshness. Retained candidates cannot dispatch until
the planner supplies a newly verified source `refresh`; retained owners remain
reserved while that discovery runs.
Initial source discovery belongs to the current pass; unchanged source reports
do not request another pass. Use `refresh` with `rescan=true` only for an explicit
new eligibility/ownership refresh request, not an empty liveness message.
Only admitted, currently eligible proposals become `candidate` events. Keep
parked product/authority/prerequisite proposals alongside the checkpoint; do not
insert them into the dispatchable backlog until their blockers are resolved.
Canonical scopes are repository-relative Windows paths, with no globs or `..`.
Name directories when exact file scope is not yet known; broad scopes deliberately
exclude conflicting work. Scope expansion requires reconciliation before writes.
For a mutable running owner, use `extend-scopes` only after fresh complete
temporary-pause/current-union/exclusivity/owner proof. Preserve all original
scopes and blockers, persist the accepted grant, then issue continuation
separately. Neither a proposed union nor acknowledgment alone authorizes writes.
This does not replace product/effect authority or qualify runtime.
After accepted refinement the original owner's source writes are permanently
retired, including inside its retained scopes. Refinement is not reconciliation
for expansion or permission to restart. It preserves slots, PRs, publication,
blockers and cleanup obligations; only disjoint independently admitted work can
gain eligibility. Never infer an acknowledgment or stopped state from idle.

For [accepted exact-path supersession](./README.md#exact-path-write-reservation-supersession), use exported `heldReservations(state,key)` for effective planner/worker/external-consumer exclusions, never subtract ancestors or overwrite historical item scopes.
Original owners keep publication, blockers, slots and cleanup; derived prior source retirement is permanent even when the legacy item flag is false.

Only a verified controlling single ledger can activate a transfer. Complete external snapshots can be recorded as nonactivating preparation; retain all their effective guards and withhold the beneficiary. No snapshot, merged publication, supplied receipt string or digest proves fresh human authority, host quiescence or a live authority handoff. Unknown facts stay unknown.

`launch` actions already hold reservations. Send the relevant worker contract,
one bounded assignment, publisher/authority, exclusions, scratch, and return
address. Record the created chat through `attached`. Do not infer agent selection
from the contract link. Independent pool dispatch must continue when another
pool contains blocked workers.

Generate production launch arguments with [worker-launch.cjs](./scripts/worker-launch.cjs)
using one JSON stdin object containing `state`, the recorded `action`, and
`settings`: `repository`, `publicationAuthorized=true`, absolute
`controlWorkspace`, `productionWorkspace`, control-owned `scratch`, and optional
verified `expectedPublisher`. Read its one JSON result, then pass those arguments
to the supported host create-session tool. This helper does not create sessions
or prove source/GitHub/host settings; it rejects observation authority, stale or
attached reservations, unsafe receipt paths, and control-checkout production use.

Workers have separate isolated worktrees (`worktree=true`) and asynchronous
related chats (`currentSession`). The exposed host `independent` option is only
for work unrelated to the current plan; do not pretend coordinated workers are
unrelated to obtain standalone sessions. Execution/file isolation is required;
standalone session isolation remains a distinct host capability.

`discover` starts one bounded pass; `steer-planner` coalesces changed state into
an active pass. Send its numbered `pass`, current reservations, free capacities,
and changed keys. Reject delayed reports from earlier passes without ending the
current pass or changing its frontier.
Ready-feature candidates precede maintenance within the delivery backlog.
Maintenance waits for the finite discovery pass to finish so early maintenance
reports cannot occupy all slots before feature candidates arrive. Feature and
repair candidates dispatch incrementally without waiting for the whole pass.
Every pass examines both pools, even when repair capacity is full.

### Terminal planner report and serialized next turn

A matching final PASS_ENDED with both examined flags, final receipt, numbered
pass, and remaining frontier is the event for the planner transition. Do not
wait for a later host `complete` status before queuing the next pass: that later
status may not produce another coordinator event.

After validating that terminal report, apply `pass-ended` once and persist the
resulting discover action. Send the next pass with `delivery=queue` to the
**same planner chat**, even if its previous turn still shows `inProgress`.
This is an intended separate turn, never steering permission to begin a new
pass inside the old turn. Host queue serialization prevents overlapping
execution. Retain the returned message ID/status and accept STARTED only for
the assigned pass; do not create another planner or replay an uncertain send.
If the host cannot serialize queued turns, report `UnsupportedQueuedTurns`
instead of replacing the planner or assuming safe overlap.

This rule only schedules the next read-only pass. It does not release file
ownership, archive a child, or weaken worker publication/cleanup proof.
Interim `productionWrites=false` is not terminal `stopped_writing=true`.
If a report is missing, inconsistent, or from an obsolete pass, retain ownership
and reconcile only on a genuine event; do not infer completion from silence.

After publication, preserve ownership; after proven cleanup, archive the exact
worker chat, communicate released capacity to the planner, and refill. Cancel
superseded pending instructions using exact IDs/revisions before replacement
or archival. An already-processing instruction requires steering, not fictional
cancellation. Never archive an enclosing session containing other owners.

When a pass has no remaining unexamined scope and no changed state, do not repeat
it to keep the chat alive. Park evidence-backed shortages. Finish the event turn
with the workflow enabled and wait for genuine notifications.

## Interruption

On explicit interruption, apply `pause` before sends. Stop dispatch, cancel only
owned pending assignments, steer active owners to pause safely, and record their
actual stopped-writing state. Preserve unfinished work and receipts. Do not
delete worktrees as interruption cleanup or affect unrelated sessions/processes.
For a caller-created genuinely new standalone session, follow
[the handoff contract](./handoff.md). Freeze old authority and verify stopped
owners before adopting the same checkpoint; never create a second live registry.

## Validation

Run the dependency-free [regressions](./scripts/registry.test.cjs):

```powershell
node --test .\.github\skills\kora-orchestrate\scripts\registry.test.cjs .\.github\skills\kora-orchestrate\scripts\checkpoint.test.cjs .\.github\skills\kora-orchestrate\scripts\worker-launch.test.cjs
```

See [runtime evidence and limitations](./README.md). Deterministic tests do not
establish client loading, effective reasoning effort, or idle callback delivery.
Use the [delivery trial contract](./trial.md) when a user authorizes an observed
production run. It measures actual delivery instead of counting launched chats.
