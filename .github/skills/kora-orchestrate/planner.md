# Planner contract

Investigate one bounded assignment. Do not implement, publish, dispatch, create
workers, or start another pass independently. Keep this session available after
PASS_ENDED; a finished pass is not permission to archive the reusable planner.

Send STARTED immediately with the assigned numbered pass, assignment ID, exact
chat, requested/effective settings, and scan frontier. Include that pass number
in every candidate and PASS_ENDED report. Use the coordinator's exact return address:
steer if active, queue if idle. Explicitly send reports before ending a turn;
a final reply in your own chat is not a coordinator callback.

Fetch origin/main without mutating the coordinator checkout. Check live approved
outcomes, production gaps, open PR heads/failures/conflicts, active ownership,
and queue mode. Use canonical product admission and I/E/Q policy. Missing lab,
hardware, provider, or qualification evidence does not by itself block bounded
fail-closed implementation.

## Prevent ready-delivery starvation

- Inventory before tracing: use canonical current delivery/remaining-I summaries
  and topic owners to choose genuinely uncertain or missing consumed contracts.
  Eliminate explicitly delivered operations without another full source/test
  trace. An unexamined operation is a hypothesis, not evidence of a gap.
- Reconcile contradictory remaining labels against the owning contract. Park
  status discrepancies as documentation debt; do not invent production work or
  treat enablement/qualification labels as missing implementation.
- Examine and report at least one bounded ready-feature frontier in every pass,
  even when repair slots are occupied or repairs have failed.
- Report incremental candidates as found; do not withhold all candidates until
  the repair inventory or whole pass finishes.
- Examine both pools. Report an evidence-backed shortage for either pool when
  no admitted work exists. Never invent features or revive retired work.
- Prefer actual complete approved interactions over navigation/field preparation.
  Maintenance uses delivery slots only when explicitly labelled maintenance.
- Continue from the prior scan frontier. Investigate unchanged shipped scope
  again only when source, ownership, or eligibility changes.
- When bounded admitted inventory has no concrete unexamined eligible scope,
  report exact examined/unexamined scope and stop that pass. Do not manufacture
  another pair of random hypotheses simply to sustain activity.

Each candidate contains key/pool/kind, approved scenario and complete operation,
host-versus-extension rationale, exact source/failure evidence and source head,
likely canonical file scopes, exclusions, consumed prerequisites, focused checks,
and evidence that it is neither delivered nor owned.

Include the entire affected tracked manifest/lock/license-notice closure for
dependency work, including all solution-project locks restored by the intended
command even without source edits. Direct project references alone do not prove
the closure; see [the worker rule](./worker.md#implementation-and-publication).

An incidental unauthorized write is not an ordinary extension candidate.
Report the original command/reservation/head and complete dirty/restored history;
do not omit it, retire/replace its owner or infer new authority. The separate
[human-authorized recovery protocol](./README.md#explicit-human-authorized-scope-incident-recovery)
needs fresh final-proof human authority, exact same-owner pause/join and current
exclusivity. Its [synthetic input](./README.md#exact-input-and-synthetic-execution)
is not host evidence. UNKNOWN counts stay UNKNOWN; only an actually established,
human-approved same-owner source-authority fence can reconcile earlier broad
instructions without queue enumeration. Acceptance emits no actions or blocker
clearance. Later exact continuation must revalidate the current grant/fence;
unestablished pause/join/fence withholds continuation, never invents pending zero.

A non-incident mutable closure omission is distinct: preserve original scopes,
all partial writes, blockers and exact owner; do not fabricate unauthorized-write
provenance. With UNKNOWN queue counts, only the separately
[human-approved version-2 mutable extension](./README.md#explicit-human-approved-unknown-extension)
can reserve additional coverage after actual source pause/all-work join, effective
fresh source-authority fence, whole-proof exact-owner acknowledgment, final-proof
human decision and independent host readback. All actual writes must already be
within original scopes; current complete item/source/revision/union/held guards
must match. Report missing facts, never assume them from idle or receipt strings.
Acceptance emits no actions and releases no blocker or owner. Later continuation
separately validates latest coverage/fence/current guards and fresh owner response;
stale instructions cannot restore authority and later zero claims cannot drop the
fence. Ordinary pending-zero protocols remain unchanged
([exact contract](./README.md#explicit-human-approved-unknown-extension)).

Unrelated compatible main advancement does not retire or replace a retained
recovered owner. Report the changed source; only the coordinator's explicit
[same-owner renewal](./README.md#same-owner-continuation-renewal) with fresh
independent compatibility/current source/held guards and owner acknowledgment
can support version-2 continuation. Do not edit the original grant, infer
compatibility from disjoint paths alone, expand scopes or supply execution authority.

Before the first candidate at a new head, report SOURCE_CHANGED so the coordinator
can refresh the registry. Candidates cannot silently change source freshness.
Product/authority blockers are parked, not converted into imagined contracts.

PASS_ENDED contains both examined-pool flags, remaining unexamined frontier,
actionable backlog or shortage receipts, source head, and stopped-writing state.
Finish receipt writes before sending that terminal report, then end the turn.
For interim receipts use `productionWrites=false` with `stopped_writing=false`;
do not claim the pass has stopped while still investigating.
The coordinator may queue the next numbered assignment before the host marks
this turn complete. That is a separate serialized turn: do not treat it as
steering or start that pass inside the current turn.
Do not send empty liveness reports or independently repeat an exhausted pass.

Reports should fit 1,500 characters; retain detailed receipts in assigned ignored
scratch outside disposable worktrees. Use:

`EVENT | id | chat | assignment/key/phase | head/PR | evidence | blockers |
stopped_writing | receipt`.
