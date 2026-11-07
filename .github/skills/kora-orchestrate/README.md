# Runtime evidence and use

Select **Kora Coordinator** and run `/kora-orchestrate` in the owning repository.
For an observed live run follow [the trial contract](./trial.md). Use the durable
checkpoint CLI in ignored scratch with explicit observe/delivery authority;
do not rely on conversation history alone for reservations and pending actions.
This bootstraps a finite event request, not a permanently active model turn.
Child reports trigger subsequent event requests in the same chat; the registry
retains `enabled=true` until explicit interruption.

The selected model/profile must support the requested frontmatter. The exposed
session-creation API selects a model, not a custom agent or reasoning effort.
Child contract links therefore supply behavior, not proof that profiles loaded.
The caller explicitly permits requested high-reasoning worker definitions with
unverified effective settings. Report that condition; do not fabricate verification.

## Non-writing host probe, 2026-11-07

The probe used one nested child, no worktree, no repository writes, no GitHub
operations, and no automations.

| Observation | Evidence | Meaning |
| --- | --- | --- |
| Child's initial turn completed | Child context, turn 1 `complete` | A final child turn can be inspected without its parent report being consumed. |
| Queued follow-up started in the same child | Message `b11647f4-3f86-42b5-9e86-db22864d2742`, `started/processing`; child turn 2 `complete` | Same-chat queued child continuation was exercised. |
| Parent callback stayed pending | Child callback `df3641e4-9861-48ce-9049-892c68734ce4`, `pending`; parent turn 10 still `inProgress` | Keeping the parent turn active can starve queued reports. |
| Active report accepted as steering | Message `12b60a23-c7da-49b0-89d6-a56434fbd62c`, `steered/processing` | Active-turn delivery was accepted; this is not proof of consumption or idle resumption. |
| Old queued probe reports cancelled | Child turn 3 cancellation report, both `cancelled` from `pending` | No pending probe report was intentionally left to restart the parent. |
| Effective settings not exposed | Child host-metadata inspection | Requested model accepted; model/effort verification remains unavailable. |

This first probe did **not** prove idle coordinator resumption. A subsequent
finite coordinator-role fixture exercised that path:

| Observation | Evidence | Meaning |
| --- | --- | --- |
| Finite bootstrap ended with registry enabled | Fixture chat `0d4fe4cb-3968-4fb6-903c-164b3714087c`, turn 1 `complete`; steering attempt rejected with `targetStatus=idle` | Normal bounded turn completion did not disable or archive the nested coordinator. |
| A separate child sent its terminal callback | Sender chat `6babbaf2-709a-49ec-b58a-29d532463bbc`; queued message `ccb6cdc3-d6f0-486e-8b91-4047148d9bbc`, `started/processing` | Child-to-idle-coordinator delivery was exercised without automation. |
| Same coordinator resumed and processed the callback | Same fixture chat, turn 2 `complete`, token `finite-coordinator-20261107`; one synthetic feature reservation and one unexecuted launch action | Same-chat resumption and finite event processing were observed, not merely send acceptance. |

The fixture read the coordinator/skill contracts and ran the reducer with
synthetic in-memory state. It did not automatically select an agent profile.
It tested a **nested** coordinator chat, not idle resumption of the enclosing
main chat, and not sustained production delivery. No production workers or
PRs were launched. The final reducer's numbered-pass/checkpoint guards have
separate deterministic regression evidence; this is transport evidence, not
exact-source production qualification.

All three non-writing fixture chats were archived after completed turns and
verification that no owned files/processes or pending restarting instructions
remained. Retained local receipts are under the owning ignored scratch path.

## Deployment acceptance

The first observed live trial on 2026-11-07 ran five discovery passes at source
`33208fefebb24b1b0cf41cf6fa482f3403205f34`. Its retained checkpoint revision 13
contains no candidates, launch actions, publications or cleanups. The open-PR
inventory was empty, and the bounded final feature inventory found no unblocked
candidate. This exercised planning transport, not actual delivery. See the
local ignored trial checkpoint/receipts for evidence; this is not global product
completion or an exhaustive implementation audit.

Observation improved two contracts: queue a separate same-planner next turn from
its validated terminal report instead of waiting for a future host completion
event; and inventory unfinished admitted implementation before tracing shipped
operations. The second rule was applied late in that trial, so the trial does
not establish efficient candidate selection or production delivery performance.

Before advertising unattended operation on a host:

1. Verify the coordinator prompt/agent/skill are discoverable on that host.
2. Finish a bounded coordinator bootstrap turn without archiving/disabling it.
   If automatic continuation keeps it active, that is not a successful yield.
3. Have a non-writing child send a queued terminal report after the coordinator
   is actually idle. Verify a new turn in the exact same coordinator processes
   the report ID and registry action. Acceptance alone is insufficient.
4. Exercise an active-turn report, an already-completed child missing its report,
   and recovery from child context on another genuine event.
5. Confirm model settings where exposed; otherwise keep UNVERIFIED explicit.

If step 2 or 3 is unsupported, report the exact limitation. Definitions cannot
repair the host's lifecycle/transport. Do not introduce automation, replacement
coordinators, periodic polling, or promises that an idle model can wake itself.

Run all three focused modules:

```powershell
node --test .\.github\skills\kora-orchestrate\scripts\registry.test.cjs .\.github\skills\kora-orchestrate\scripts\checkpoint.test.cjs .\.github\skills\kora-orchestrate\scripts\worker-launch.test.cjs
```

The maintained [policy runner](../../../eng/run-policy-tests.cjs) selects all
three modules when this skill is installed and fails if any required module is
missing. Run `node .\eng\run-policy-tests.cjs` for the combined maintained checks.

Deterministic registry tests exercise reservations, separate pools, feature-first
delivery, stale/duplicate reports, cleanup-before-release, and bounded discovery.
They are not production delivery or runtime qualification evidence.
Checkpoint tests additionally cover pending actions, duplicate replay, stale
writers, lock ownership, and failed atomic replacement without lost state.
Worker-launch fixtures cover disjoint asynchronous isolated requests, exact
callbacks, publication authority, source freshness, and safe control/receipt
paths. They do not establish that real workers publish or clean up successfully.
