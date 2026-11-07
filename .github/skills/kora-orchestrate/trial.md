# Observed delivery trial

This is an engineering workflow, not a Kora product feature. Start it only with
user authority for actual repair/ready-PR delivery. Do not mistake earlier
discovery-only pilots for that authority.

## Deployment

- The implementation worktree is the coordinator's owned control checkout.
  Read contracts/reducer/checkpoint from that exact absolute path. New production
  workers branch from current origin/main and read these control contracts by
  absolute path; unmerged definitions are not assumed to exist in their worktree.
- Retain checkpoints/worker receipts under control-workspace ignored scratch,
  outside all disposable worker worktrees. Only the coordinator writes its journal.
- Discover the exact coordinator chat and observer return address. Recover and
  exclude any active owners in the same repository before worker allocation.
- Autopilot is a host setting, not prompt text. `create_session` currently has no
  Autopilot parameter. If no supported control/evidence exists, report it and
  await the user's manual host selection before representing this as an
  Autopilot trial. Do not create automation to select the mode.
- Requested high-reasoning workers with unexposed effective settings are allowed
  by the user, but must be reported UNVERIFIED, not described as configured.

## First observation window

Begin with one repair worker and one admitted feature worker when each is
eligible. The separate pool limits remain 4 and 10; ramp up only after STARTED
and exclusive ownership are recorded for the first allocations. Do not wait
for repair completion to discover/dispatch a feature.

Every bounded planning pass first reconciles canonical remaining-I inventory and
topic owners, then covers at least two new concrete approved interaction
frontiers when two eligible or genuinely uncertain frontiers exist, plus the
current bounded PR repair inventory. Do not deepen explicitly delivered routes
or fabricate a second frontier to satisfy a numerical target. Two delivered operations
are not evidence that all feature scope is exhausted. Carry forward the
unexamined frontier and promptly assign the next finite pass; do not rediscover
the same delivered operation unless source/eligibility changes.
On a validated terminal PASS_ENDED, queue the next assignment to the same planner
as a separate turn even if the previous host turn still appears active. Do not
depend on a future completed-turn event to schedule this already-authorized
continuation. This does not relax production-worker stopped-writing or cleanup
verification and never starts a new pass through steering.

End the first assessment at verified publication plus cleanup in each eligible
pool, or an explicit evidence-backed lack of eligible work/access. No elapsed-time
or empty-message polling is authorized. A shortage report must name examined and
remaining scope; a material missing approval is parked, not manufactured.
If no eligible production work was dispatched, the outcome is **delivery not
exercised**, not a successful delivery trial. Report selection overhead and
transport evidence separately; do not promise that production worker validation,
publication, cleanup, or pool fairness worked from synthetic tests alone.

## Measurement and observer events

Send the observer one compact report for bootstrap, each planning-pass end,
worker STARTED, publication, cleanup, explicit blocker, and interruption.
Include exact chat/key/pass/head/PR plus the absolute checkpoint and receipt
paths. Steer the observer when active; queue if actually idle. Do not forward
every tool call or add heartbeat-only messages.

The observer reads the known coordinator/affected child contexts on these events.
Inspecting timestamps can identify suspect inactivity, but cannot prove a stall
or stopped writing. No event means no new inspection is guaranteed.

Derive these measures from journal timestamps and concrete receipts:

- Eligible candidates, reserved/running owners, publications and cleanups,
  separately for repair, admitted feature, and maintenance.
- Event-to-dispatch latency, candidate-to-STARTED latency, publication-to-cleanup
  latency, and released-slot-to-next-eligible-dispatch latency where observed.
- Every eligible feature left waiting despite free delivery capacity: classify
  file conflict, consumed prerequisite, authority, host failure, or dispatch defect.
- Duplicate/obsolete messages, repeated scans of unchanged delivered scope,
  missing terminal reports recovered from context, and unresolved action results.
- Actual validated pushed repair or ready PR/enrollment, not chat creation,
  ordinary CI success, Auto-merge alone, or synthetic reservations.

Record exact sources, elapsed values and available token/tool-call counts;
mark unavailable measures unknown. Do not invent timing thresholds or promise
continuous background monitoring. The observer's final assessment distinguishes
implemented safeguards, exercised transport, actual delivery, and unverified
host settings/remaining outcomes.

## Repairing workflow defects

The observer may fix defects in its exclusively owned control implementation,
run focused regressions, and steer the coordinator with the exact changed
contract/revision before another allocation. Never modify a live worker's
production files or discard unknown ownership to free slots.

On user interruption stop dispatch, notify owned children to pause, and retain
their work. Reconcile actual stopped-writing evidence; do not archive or remove
unfinished worktrees simply to make the trial look complete.
