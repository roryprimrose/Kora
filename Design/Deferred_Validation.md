# Deferred Proof Validation

Status: outstanding validation register, not passed acceptance or permission
to run a test. Partial feasibility evidence and scoped implementation may merge while
unproven capabilities remain gated and their decisions/acceptance gates stay
open. Merge is not certification for production release.

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
| R02 local inference | [Safe deterministic reruns](../experiments/r02-local-inference-proof/README.md#safe-deterministic-reruns); verified public identity/licence/download metadata and real missing-runtime/unavailable behavior, not model quality or performance | [Prepare an interactive inference session](../experiments/r02-local-inference-proof/README.md#before-an-interactive-inference-session), then [LI01-LI07 deferred trials](../experiments/r02-local-inference-proof/README.md#deferred-inference-trials). Assign reference/isolation owners and agree budgets; separately approve exact pinned provisioning, exclusive model residency changes and whole-environment network blocking. Synthetic measurement commands exist; server-cessation/race, attributable egress and integrated-host rows need independent instrumentation or later implementation. | D-003 and inference D-007; [R02-L1-L6](Implementation_Roadmap.md#r02-local-inference-continuation), actual CPU-floor quality/context/cancellation, distribution and offline success; R06/R07/R08/R10 and A2/R19 integration remain gated |
| R02 runtime/provider | [Pinned no-account Node SDK/runtime checks](../experiments/r02-runtime-proof/README.md#reproduce-on-windows); actual loopback mediation, streaming/errors/cancellation, byte/deadline and conversation-isolation evidence; hook-only failure retained | [Runtime/provider follow-up](#runtimeprovider-follow-up): first implement a pinned isolated .NET fixture; then instrument lifecycle paths and reproduce the host envelope. Prepare separately approved account/usage trials for a later interactive session. Existing commands cannot establish .NET parity, full process observation or live service eligibility. | D-001/D-004/D-010; [RT1/RT2/MG1/PV1](Implementation_Roadmap.md#r02-runtimeprovider-follow-up-gates), R08 exposure and model-assisted R13 remain gated; deterministic local management does not wait for hosted trials |
| R02 Windows containment | [Fixed owned-scratch reproduction](../experiments/r02-containment-proof/README.md#reproduce); partial OS denials and lifetime/Unknown receipts already observed | [Containment outstanding-testing checklist](../experiments/r02-containment-proof/README.md#outstanding-testing-checklist): supported-OS repeat, attributable network denial, dependency/control mechanism, independent deployment and aliases, helper contracts, actual controlled effects and host-death/race recovery. Most rows require a new bounded fixture or instrumented implementation; the current runner is not a general executor. | D-013 and [W1-W4](Implementation_Roadmap.md#r02-windows-containment-follow-up); R11/R16/R17 exposure remains gated |
| R02 distribution | [Draft proof #24](https://github.com/roryprimrose/Kora/pull/24); separate build/inspection evidence, not an implementation dependency of the containment proof | Its approved scope is build/inspect only. Linux package construction and actual Windows installation, effective ACL/token protection, native/runtime-only launch and recovery require separate validation. Before any real installation/launch/registry/privileged trial, obtain a new scoped approval and use a disposable lab deployment. Follow the distribution proof's checklist when its documentation is integrated. | D-005/R17; no production worker/catalogue acceptance from package inspection or the absence of those components |
| R03 Windows ownership/audio privacy | [Implementation PR #26](https://github.com/roryprimrose/Kora/pull/26), portable policy/race tests, non-disruptive Windows object/enumeration tests and x64/x86 builds/publishes; not live microphone or lifecycle evidence | [R03 interactive checklist](#r03-windows-ownership-and-audio-privacy). Prepare an instrumented, non-elevated test host and obtain separate approval for each capture/playback, launch/handoff and OS-transition trial. Production wake is not selected or enabled by R03. | R03 real-adapter acceptance; capture release within 500 ms of the observed lock event in every reference trial, takeover/return, hardware/offline ASR, native recovery and cross-architecture evidence remain open |

Each proof-specific checklist owns its detailed procedures; this register
does not replace them or weaken their separate consent requirements. Local
inference isolation proves the answering environment's remote-egress boundary,
not the containment worker's attributable network denial. Speech, storage,
containment and distribution receipts cannot qualify an untested inference runtime or
authorise its provisioning/network changes. The
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

## Runtime/Provider Follow-Up

Status: **RT1/RT2/MG1/PV1 remain open**. The Node experiment has 13 PASS,
1 FAIL and 3 BLOCKED outcomes, not production acceptance. Hook-only
failed-result mediation is rejected; the final-request-gated candidate
continues only through the [technical plan](Runtime_Provider_Feasibility.md).
Merging the experiment and its documented outcomes closes none of these gates.

### Preparation and Approval

- Start with the existing [no-account reproduction](../experiments/r02-runtime-proof/README.md#reproduce-on-windows)
  on the exact pinned Windows/Node/runtime versions. Use only synthetic context,
  credential sentinels and harmless tools. These checks do not need hosted
  credentials or an unlocked interactive console; they are not proof of global
  runtime network/storage containment.
- An interactive session alone cannot run the remaining .NET proofs: RT1
  requires a new isolated .NET fixture and pinned supported APIs first.
  RT2 additionally needs attributable network/file/diagnostic observation;
  MG1 needs the corresponding .NET host envelope. Do not treat the Node
  fixture or its status-only schema as those implementations.
- Record source, SDK/native-runtime hashes, Windows version and fixture/
  instrumentation identity. Agree operator, reference environment, scope,
  deadline tolerances and stop/cleanup procedures before a trial. Obtain
  separate approval for installs, elevation, policy/network changes or
  protected setup; never disrupt unrelated processes or global policy.
- Before live provider inference, name the intended service/model/region,
  supported user authentication, plan/organization restrictions, permitted
  assistant/SDK use, concurrency and enforceable usage/spending budget.
  Obtain explicit account and potentially paid-usage approval. Merge approval
  and an unlocked console authorize neither sign-in nor billable calls.
  Never borrow ambient developer credentials or include tokens, account names,
  raw sensitive payloads or credential-bearing diagnostics in evidence.

### Deferred Trials and Closure Evidence

| Trial / owner | Current state and prerequisite | Required trial and evidence |
|---|---|---|
| RT1 - .NET public control points / runtime lead | Not run; .NET fixture absent | Pin the actual .NET SDK and compatible runtime; prove supported pre-effect tool denial, initial context and every tool-result status/exception at the final serialized-request gate. Observe zero denied-tool effects and zero denied markers forwarded, disabled unmediated tools/collection/persistence, streaming, auth/error behavior, truthful cancellation and execution/management/provider isolation. Missing or ignored APIs stop the candidate and require an explicit D-001 decision, not private patches or a silent Node bridge. |
| RT2 - Full runtime lifecycle / runtime and security leads | Not run; RT1 and attributable instrumentation required | Observe startup/session/auth/error/shutdown network, storage and diagnostics, including runtime initialization metadata and non-model transports. Inventory every destination and content-bearing path; demonstrate prevention/mediation and no denied-marker egress or unauthorized recoverable persistence. Repeat account-specific paths during PV1. An unobservable or uncontrollable path is Failed/Blocked, not assumed compliant. |
| MG1 - .NET management envelope / runtime lead | Not run; RT1; may run alongside RT2 | Prove complete serialized UTF-8 input at 32768/32769 bytes and complete typed output at 4096/4097 bytes, counting framing/history and multi-byte text. Hold real inference across the host 15000 ms dispatch deadline and stall SDK acknowledgements; record actual elapsed timing without extending the configured deadline. Verify one in-flight request, 30 attempts/rolling hour including failures, at most one forwarded inference/no SDK retries, manager responsiveness with two execution conversations held, no lane/context leakage, uncertain-termination quarantine and deterministic fallback. Cancellation acknowledgement must not certify rollback or release an uncertain inference slot. |
| PV1 - Live account/provider / runtime lead, account owner and security/legal review | Not run; no hosted account/usage approval; RT1/RT2, plus MG1 for management | Use the intended user's supported secure authentication; test expiry/denial/throttling/errors, exact destination/content isolation and truthful cancellation. Record approved terms/plan/model scope, actual concurrency/quotas/rates, billed-cost assumptions and hard spending controls. Execution eligibility feeds D-001/R08; management additionally needs actual two-execution-plus-manager capacity/budget evidence for D-004/R13. Incompatible/unapproved or unbounded service stays disabled; do not infer hosted allowance from three loopback conversations or byte counts. |

Keep the hook-only FAIL as a regression witness; accepting a different proved
profile does not require making that rejected approach pass. For each deferred
trial record Pass/Fail/Blocked/Not run, exact tested profile, UTC timestamp,
observer, synthetic counters/receipts and unresolved limitations. Do not replace
failure with a timeout or a successful fake. Retest affected paths whenever the
SDK/runtime, endpoint/model/auth, transport, storage or admitted feature changes.
R08/R13 still need their own integrated host, scheduler and installed-app
acceptance; this checklist does not enable production adapters or tools.

## R03 Windows Ownership and Audio Privacy

Status: **all interactive rows below Not run**. The operator deferred live
trials; no microphone capture/playback, app launch, lock/disconnect/suspend,
takeover/return or crash-recovery trial was performed for PR #26. Tests using
fakes and native object/device enumeration are supplementary only. Publishing
two architectures does not prove cross-build transfer or acoustic behavior.

The scoped implementation has saved device/profile-local microphone consent,
fresh-gated ordinary startup, run-scoped explicit recovery, held PTT, bounded
generation-tagged capture, external privacy observation and native recovery.
It does not implement/select production wake, durable sessions, general grants,
model adapters or script execution. Do not exercise those future capabilities
or infer that the R02 containment experiment is part of this host.

### Preparation and Approval

- Record exact source/artifact hashes, supported Windows servicing build, CPU,
  memory, power profile, physical endpoint IDs, runtime/recognizer versions,
  operator and participants. Use disposable profiles/data and synthetic commands.
- Obtain fresh, bounded test approval covering participants/bystanders,
  microphone/playback endpoint and volume, duration, retention/deletion and a
  visible immediate stop path. Saved microphone consent is not trial approval.
- Obtain separate explicit approval before app launch/ownership transfer,
  process termination or lock/disconnect/suspend. Being unlocked, approving this
  documentation or merging the PR authorizes none of those actions.
- Prepare monotonic timestamp hooks for OS notification, capture handle release,
  buffer clearing, last output sample and dispatch receipts. If hooks, native
  recovery or stable process identities are missing, mark the row Blocked.
  Measure only the owned host; never terminate unrelated applications by name.
- Start capture only as the verified non-elevated owner in an authoritative
  unlocked/connected session. Record RDP/redirection/virtual endpoints separately;
  they do not certify physical headset or speaker/microphone behavior.

### Interactive Trials and Closure Evidence

| ID | Procedure and required result | Closure evidence |
|---|---|---|
| A01 - Consent and ordinary startup | On a clean disposable profile, enumerate without capture; exercise grant/decline, ordinary startup with saved consent, run disable/re-enable and persistent withdrawal/restart. Distinguish armed PTT from actual recording. Permission/device availability alone grants no capture. Debug/release partitions do not copy consent. | Consent/profile/build identities, individual capture-open/close observations and fresh-gate decisions. Withdrawal or persistence failure never keeps capture open. |
| A02 - Explicit command and stale generations | Hold mouse/Space/Enter PTT and use harmless commands such as help/open settings; release or lose focus/close the control. Exercise early result during open, early release, empty speech, maximum duration, failed open and delayed/duplicate callbacks. Verify first command words, bound receipts and exactly one admitted dispatch. | Per-activation generation, sample/queue/transcript bounds and timestamps; no unactivated audio in command transcription and no retired callback dispatch. Hardware/offline Windows ASR behavior is measured, not inferred from fakes. |
| A03 - External session privacy | Separately approve Win+L/idle lock, disconnect, suspend/resume and applicable session transitions while an owned activation/output is active. Capture closes, buffers clear, output stops and sensitive presentation hides. Unlock/reconnect/resume requires explicit recovery and cannot replay audio/approvals. | **Capture released within 500 ms from the observed lock event in every reference trial**. Record OS event-to-notification delay separately, plus each observed-event-to-release duration, buffer clearing, last output sample and zero stale dispatch; no averages/p95 substitution for this target. |
| A04 - Permission and device changes | With approved capture/output fixtures, revoke/restore desktop microphone permission; remove/disable pinned endpoints; change System-default input/output; hot-plug and refresh. Missing pinned endpoints cannot select a same-name substitute. Restored readiness never removes a run hold. | Endpoint/permission revisions, native failure/closure receipts, individual timings, bounded audio clearing, stopped output without replay and explicit recovery. Do not change global privacy settings without separately scoped approval. |
| A05 - Cross-build owner, handoff and return | Launch validated same-build and different-build candidates across approved paths/versions/x64/x86. Verify activation without startup argument dispatch, one assistant owner/tray, inactive candidate, native default-deny approval, active-work refusal and full release before transfer. Exercise decline, expiry, candidate death, lock during approval, abort and explicit exact-original return. | OS-authenticated process/SID/session/creation/content identities, approvals, held-handle/owner epochs, actual desktop/service/capture quiescence and zero simultaneous owners. Return is lifecycle-only before explicit acceptance; no task/grant/audio/consent transfer. |
| A06 - Unclean ownership and failure recovery | In a disposable instrumented host only, separately approve stable-identity process termination and preparation/transfer failure. Unknown/orphaned effects must block automatic crash takeover/return. Changed/elevated/cross-session/unknown identities deny. | Correlated process/job/resource outcomes, continuity marker and explicit blocker/reconciliation receipts; process death alone is not proof of worker quiescence or permission to delete a marker. |
| A07 - Native fallback and private output | Without model/network/optional speech dependencies, exercise tray/settings refresh, revision-bound endpoint selection, enable/disable, PTT, Stop speaking, keyboard/focus/screen-reader paths and locked presentation denial. Test in-flight/queued synthesis and route changes so retired audio cannot start late. | Native UI/accessibility observations and last-output-sample/generation receipts under supported headset and speaker/microphone setups; stale menus do not authorize a substitute. Production wake, acoustic playback rejection and interruption quality remain owned by the separate speech gates. |

For every row, retain approval scope, artifact/revision and machine identities,
trial ID/expected versus actual result, individual timing/dispatch/resource
receipts, positive controls, native errors and cleanup. Keep raw recordings
local under agreed retention; commit only reviewed, content-minimizing/redacted
measurements. Add a new dated run, not a replacement for historical evidence.
Mark Pass/Fail/Blocked/Not run and close only the corresponding acceptance gate
when its complete evidence passes. No claim of overall R03 or A0/A1 acceptance
is made by this handoff.

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

Likewise, local inference may merge as a source-linked harness, truthful partial
results, technical outcomes and an actionable LI01-LI07/R02-L1-L6 handoff after
normal validation/checks/reviews. Missing runtime, operator/hardware trials and
independent offline capture remain capability blockers, not prerequisites for
merging that limited scope. Keep historical observations unchanged and
D-003/D-007 open; no inference pin, production tool loop or remote fallback is
enabled by publication.

On 2026-10-05 the user requested rebasing R03 onto main and adding its deferred
interactive testing alongside the other proofs so the scoped PR can merge.
The authorized merge scope is implementation plus this actionable evidence
handoff, **not completion of interactive acceptance or a production release**.
The live A01-A07 rows remain capability/release blockers, not prerequisites for
merging that limited scope. This does not disable the explicitly consented PTT
implementation or mislabel it as proven wake/audio privacy; deployment/release
acceptance still requires its real-boundary evidence. Production wake remains
unavailable. Normal build/test/100% line-and-branch coverage checks and required
reviews must pass; documenting deferred trials cannot waive CI failures or
authorize disruptive testing. Overall roadmap/decision-register acceptance
consolidation remains with integration review.

No live speech, protected installation, privileged diagnostics or disruptive
computer-control validation was performed by adding this register. Its inference
entry performs no installation/model download, model-residency change,
network-policy mutation or successful real-model trial.
