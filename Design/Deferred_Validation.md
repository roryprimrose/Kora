# Deferred Proof Validation

Status: outstanding validation register, not passed acceptance or permission
to run a test. Partial feasibility evidence may merge while the affected
capabilities remain disabled and their decisions/gates stay open.

Use this page to plan remaining validation. Safe file/database-only scratch
reruns may run remotely while the physical console is locked; only rows
requiring live input, actual effects or protected setup need the corresponding
interactive/privileged preparation.
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
| R02 storage/key | [Synthetic storage proof and safe Windows reruns](../experiments/r02-storage-proof/README.md#reproduce); authenticated content, DPAPI/key-file ACLs and transaction/artifact interruption evidence | [Storage admission follow-up](#storage-admission-follow-up): maintained native selection, installed x64/x86 loading, production profile-path/CurrentUser/permission integration, and integrated recovery/deletion. Safe proof reruns require a loaded Windows profile, not an unlocked console. Routine second-account OS-denial trials are optional for profile-local storage. | D-009; R02 native admission, R04 integration and R12 lifecycle/deletion remain open; no production store is enabled |
| R02 Windows containment | [Fixed owned-scratch reproduction](../experiments/r02-containment-proof/README.md#reproduce); partial OS denials and lifetime/Unknown receipts already observed | [Containment outstanding-testing checklist](../experiments/r02-containment-proof/README.md#outstanding-testing-checklist): supported-OS repeat, attributable network denial, dependency/control mechanism, independent deployment and aliases, helper contracts, actual controlled effects and host-death/race recovery. Most rows require a new bounded fixture or instrumented implementation; the current runner is not a general executor. | D-013 and [W1-W4](Implementation_Roadmap.md#r02-windows-containment-follow-up); R11/R16/R17 exposure remains gated |
| R02 distribution | [Draft proof #24](https://github.com/roryprimrose/Kora/pull/24); separate build/inspection evidence, not an implementation dependency of the containment proof | Its approved scope is build/inspect only. Linux package construction and actual Windows installation, effective ACL/token protection, native/runtime-only launch and recovery require separate validation. Before any real installation/launch/registry/privileged trial, obtain a new scoped approval and use a disposable lab deployment. Follow the distribution proof's checklist when its documentation is integrated. | D-005/R17; no production worker/catalogue acceptance from package inspection or the absence of those components |

The speech checklist remains the owner of detailed acoustic procedures; this
register does not replace it or weaken its consent requirements. The
distribution entry records coordination with a published draft, not a claim
that its implementation has landed on main. Preserve both distribution and
containment roadmap plans when integrating that branch.

## Storage Admission Follow-Up

The [profile-boundary contract](Architecture.md#profile-boundary-and-validation-responsibility)
trusts Windows per-user isolation but requires Kora to demonstrate correct
use of it. A profile-local path alone does not cover permissive ACLs,
LocalMachine wrapping, shared staging or unkeyed backup/export copies.
The storage research may merge with these application/deployment gates open:

| Follow-up | Owner / package | Required evidence and scope |
|---|---|---|
| S1 - Admit maintained native assets | Storage/release leads, R02 then R17 | Review engine/provider provenance, notices and servicing; rerun synthetic authentication/recovery tests on the candidate and demonstrate installed Windows x64/x86 loading. Package publication alone is insufficient. Installation/protected setup needs separate bounded approval. |
| S2 - Integrate the profile boundary | Storage/application leads, R04 | Verify production CurrentUser wrapping without machine/shared fallback, every managed path/copy under the intended local profile, and effective directory/key-file ACLs. Exercise unavailable profile/key/permissions without replacement data or success-shaped fallback. Use owned synthetic fixtures, not the user's existing database. |
| S3 - Integrate recovery and migration | Storage/application leads, R04 | Interrupt key-wrapper/backup-generation and artifact publication, verify legacy-source preservation and rekey recovery, and demonstrate real durable intent/receipt recovery without automatic dispatch. Define final host types independently of the private prototype fixtures. |
| S4 - Integrate deletion and lifecycle | Storage/security/application leads, R12 | Exercise source revocation, late appends, live/unknown-work holds and configured lifecycle; remove or rewrite managed recoverable copies while preserving unrelated sessions and independent grants. Disclose exported/provider/forensic limits. |

No second-account denial result is claimed. Optional actual-account
corroboration becomes required if introducing shared storage, service or
impersonated identities, cross-profile import/migration or custom cross-user
authorization, or investigating inconsistent effective permissions.
Such trials need approved real accounts and explicit fixture/effect scope;
the retained [optional handoff protocol](../experiments/r02-storage-proof/README.md#optional-real-cross-user-handoff-protocol)
does not create an account or grant authority to test another profile.
Per-user DPAPI is not same-user worker containment; D-013/W1-W4 remain separate.

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

The storage proof's historical blocked second-account result remains in its
original snapshot. Its revised automated run verifies application-controlled
scope and permissions and returns success only for that bounded proof;
optional OS-boundary corroboration is not relabelled as passed.
Neither merging its design direction nor a successful synthetic run closes
S1-S4 or D-009.

No live speech, protected installation, privileged diagnostics or disruptive
computer-control validation was performed by adding this register.
