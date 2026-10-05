# Deferred Proof Validation

Status: outstanding validation register, not passed acceptance or permission
to run a test. Partial feasibility evidence may merge while the affected
capabilities remain disabled and their decisions/gates stay open.

Use this page when the operator returns to an interactive Windows session.
Read the linked proof's prerequisites and consent boundaries before preparing
any trial. Being back at the machine is not approval to install, elevate,
capture audio, change security policy, lock, shutdown or restart it.

Related: [Acceptance Criteria](Acceptance_Criteria.md),
[Implementation Roadmap](Implementation_Roadmap.md),
[Decision Register](Decision_Register.md).

## Proof Checklists

| Proof | Existing evidence / runnable checks | Deferred validation and preparation | Gates still open |
|---|---|---|---|
| R02 speech/hardware | [Merged synthetic proof and safe file-only reruns](../experiments/r02-speech-proof/README.md#safe-to-rerun-remotely-including-while-locked) | [Before a live test session](../experiments/r02-speech-proof/README.md#before-a-live-test-session), then [live/instrumented acceptance](../experiments/r02-speech-proof/README.md#live--instrumented-acceptance-work-still-outstanding). Obtain participant/bystander consent and an instrumented host with R03/R09 ownership/privacy controls; the current scripts cannot run live trials. | D-002/D-007; packaged acoustics, playback rejection, latency, reference floor and capture/recovery acceptance |
| R02 Windows containment | [Fixed owned-scratch reproduction](../experiments/r02-containment-proof/README.md#reproduce); partial OS denials and lifetime/Unknown receipts already observed | [Containment outstanding-testing checklist](../experiments/r02-containment-proof/README.md#outstanding-testing-checklist): supported-OS repeat, attributable network denial, dependency/control mechanism, independent deployment and aliases, helper contracts, actual controlled effects and host-death/race recovery. Most rows require a new bounded fixture or instrumented implementation; the current runner is not a general executor. | D-013 and [W1-W4](Implementation_Roadmap.md#r02-windows-containment-follow-up); R11/R16/R17 exposure remains gated |
| R02 distribution | [Draft proof #24](https://github.com/roryprimrose/Kora/pull/24); separate build/inspection evidence, not an implementation dependency of the containment proof | Its approved scope is build/inspect only. Linux package construction and actual Windows installation, effective ACL/token protection, native/runtime-only launch and recovery require separate validation. Before any real installation/launch/registry/privileged trial, obtain a new scoped approval and use a disposable lab deployment. Follow the distribution proof's checklist when its documentation is integrated. | D-005/R17; no production worker/catalogue acceptance from package inspection or the absence of those components |

The speech checklist remains the owner of detailed acoustic procedures; this
register does not replace it or weaken its consent requirements. The
distribution entry records coordination with a published draft, not a claim
that its implementation has landed on main. Preserve both distribution and
containment roadmap plans when integrating that branch.

## Interactive Session Workflow

1. Select the proof and exact row to validate. Record current source revision,
   supported Windows servicing build, machine/resources, runtime/native assets,
   endpoint or token identities and a named operator. Confirm that the required
   measurement hooks and recovery path exist; otherwise leave the row Blocked.
2. Agree on resource scope, bounded duration, owned synthetic inputs, output/
   retention, cleanup and a visible stop path. Record any required privileges
   and exact requested effects before obtaining approval. Do not treat approval
   of one proof as authority for another.
3. Rerun applicable safe deterministic/build checks into new ignored evidence
   directories. Existing measured snapshots remain historical; do not overwrite
   them or relabel simulation, timeout, package inspection or absence as a pass.
4. Execute only the separately approved trial. Record individual results,
   positive controls, native errors, correlated receipts and cleanup. Stop on
   unexpected identities/rights/effects; uncertain effects remain Unknown and
   must not trigger automatic replay.
5. Attach redacted evidence to the owning checklist, update its row and the
   applicable canonical decision/roadmap/acceptance records. Close a capability
   gate only when all its required real-boundary evidence passes; merging
   partial research or this register does not close it.

## Publication Versus Capability Acceptance

The containment proof's strict exit `2` and eight unproven network assertions
remain truthful research findings, not passing runtime acceptance. The PR may
be published/merged as partial evidence with completed build/hygiene checks,
this actionable deferred-testing handoff and normal repository checks/reviews.
It must not enable a worker, grant broader authority or change the result to
success to make the PR mergeable.

No live speech, protected installation, privileged diagnostics or disruptive
computer-control validation was performed by adding this register.
