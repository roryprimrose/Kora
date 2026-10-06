# Architecture and Delivery Decision Register

Status: active proposed-design register.

Related: [MVP Scope](MVP_Scope.md), [Architecture](Architecture.md), [Acceptance Criteria](Acceptance_Criteria.md).

The [Implementation Status and Delivery Roadmap](Implementation_Roadmap.md)
is the source-backed delivery baseline. It separates accepted direction from
implemented behavior and required proof, and assigns dependencies/priority to
remaining work without closing the decisions below.

This register records accepted product/UX direction separately from unresolved technical choices and implementation evidence that can block or materially change delivery.
The named owner is a role until an individual is assigned.
A decision is not complete because an implementation was started; its evidence and affected documents must be updated together.
An Accepted direction status records the agreed behavior, not completed contracts, tested enforcement, or release readiness. Remaining schema/integration/proof work stays open until its acceptance evidence passes.

| ID | Decision | Owner | Due before | Status | Required evidence |
|---|---|---|---|---|---|
| D-001 | Copilot adapter/control-point viability | Runtime engineering lead | Slice A0 implementation | Open, release-blocking; historical source-built RT1 and separately approved released-profile RT1 regressions pass; bounded RT2 passes but all-path gate Blocked; hook-only rejected | Preserve both distinct artifact pins and [RT2 handoff](../experiments/r02-runtime-lifecycle-proof/EVIDENCE.md#handoffs). MG1's initial local source-built reproduction blocker remains recorded; RT2's final source reproduction is separate evidence, not released-byte equivalence. Released SDK 1.0.16 repeated all 45 RT1 controls in MG1-owned fixture. Native helper/transient-write observations do not establish all-destination/storage/diagnostic attribution or prevention. Dedicated-host tracing needs separate approval; qualify applicable changed-profile lifecycle paths and approved execution PV1 before production admission. |
| D-002 | Local wake detector, endpointing, and playback rejection | Speech engineering lead | Slice A1 implementation | Open, release-blocking | Candidate benchmark, redistribution/licence review, packaged Windows assets, recall/false activation, CPU/memory, immediate-command preservation, self-activation tests |
| D-003 | Local inference baseline | Runtime engineering lead | Slice A2 acceptance | Open, release-blocking; R02 identity/licence metadata and unavailable-path evidence recorded; candidate unqualified | [R02 local-inference outcomes and plan](Local_Inference.md): pinned candidate, distribution review, agreed budgets, actual CPU-floor quality/performance/context/cancellation and network-blocked successful answering; owner-reviewed selection |
| D-004 | Management inference provider envelope | Runtime engineering lead | Slice A3 implementation | Open, release-blocking for production model-assisted management; released-profile .NET MG1 envelope passes, historical witnesses unchanged | Carry MG1 bytes/deadline/admission/no-retry/Unknown quarantine to R13 using R04 host contracts. Applicable RT2 and management PV1 still require approved account/tier/terms, actual concurrency, quota/rate limit and defensible billed-cost evidence. Byte limits do not bound billed reasoning; deterministic fallback is independent. |
| D-005 | Unsigned Windows package and notify-only maintenance | Release engineering lead | First production-accepted binary candidate | WiX MSI/custom Burn binary packaging and POC release automation implemented; managed-source/installed/protection acceptance open, production release-blocking; historical NSIS receipts retained | Approved unsigned beta/stable POC publication is not D-005 closure. CI gates, per-release native/redistribution review, lifecycle/logon/upgrades, actual runtime/identity/protection trials and source delivery remain required; follow R02-D01 through R17-D03 below. No install-capable updater; future signed-metadata trust is separately gated. |
| D-006 | Optional frequent-speaker learning and verifier | Security and speech leads | Before advertising learned-speaker/owner-aware capability | Accepted optional direction; engine/privacy proof open | Separate consent, local protected per-SID/device learning, predominant-speaker/drift/playback quality, reset/delete and privacy evidence; separately enrolled verifier FAR/FRR/anti-spoof proof and protected OS workflow |
| D-007 | Supported Windows/reference hardware matrix | Product and test leads | Slice A1 acceptance; inference qualification before A2 | Open, release-blocking; R02 development inventory is not floor qualification | Windows versions, named reference CPU/RAM, microphones/headsets, accessibility baseline, test machine ownership and reproducible environment; [R02-L1/L3](Implementation_Roadmap.md#r02-local-inference-continuation) supported CPU-only inference-floor evidence |
| D-008 | Approval/grant implementation and audit model | Security engineering lead | Before general side-effecting execution | Accepted scopes/lifetimes; schema/enforcement proof open, release-blocking; initial model grants only | Single-use consumption, operation-bound durable session grants, perpetual grants without retention/eviction, applicability/provenance after chat deletion, native explicit edit/removal, intent lineage, audit tamper evidence, fatigue/race acceptance tests |
| D-009 | Durable session/evidence storage, lifecycle and deletion | Storage and security leads | Revised Slice A3 implementation | Owner-approved standard SQLite/private profile baseline; encryption/key/rekey prerequisites superseded; bounded actual task store delivered, composed evidence/schema/lifecycle acceptance partial | Supplied LocalApplicationData and effective ACLs, pinned standard SQLite, versioned schemas, ordered durable events, W3C/call-time correlation, independent log/audit/span/link persistence and retention, explicit recovery/gaps, copy disclosure, lifecycle/deletion and no replay; D-008 audit authority remains separate |
| D-010 | Concurrent sessions and resource coordination | Runtime engineering lead | Revised Slice A3 implementation | Accepted bounded concurrency; Node and scoped .NET RT1 conversation topology measured, production isolation/budget proof open | Approved provider concurrency budgets, proposed two-slot baseline, one task per session, canonical shared/exclusive resource leases, outside-change revalidation, fair scheduling, cancellation/unknown-effect races; loopback conversations are not execution-slot proof |
| D-011 | Shared interaction, session routing/history and evidence tools | Product and application leads | Revised Slice A3/A4 implementation | Accepted UX direction; protocol/integration proof open, release-blocking | Voice/UI/mixed structured questions and exact grants, compact interaction/list-plus-conversation workspace/separate detail surfaces, minimal Active-session routing context, per-session Logs/Audit/All Evidence across traces, trace-tree/link navigation, bounded cited search/reasoning and gap status, provenance/egress, foreground voice versus addressed UI races |
| D-012 | Windows-session trust model and accepted voice boundary | Product and security leads | Design acceptance; enforcement before associated capability release | Accepted direction; implementation evidence outstanding | Enabled verbal input trusts the active unlocked profile, not speaker identity; no compulsory biometrics/PTT/UI for ordinary voice; scoped grants, call origin/reuse gates, intent/content separation, containment and truthful recovery tests |
| D-013 | Windows worker and protected-deployment containment | Security and Windows engineering leads | R11 execution admission; R17 protected-deployment acceptance | Best-effort transitive tracking/review direction accepted 2026-10-06; technical admission open, release-blocking; strict ACL/no-child profiles rejected | Exact manifest-listed script review/grants, honest transitive gaps/user responsibility, attributable OS network denial, effective app/worker identities and protected-root/runtime ACLs, aliases/TOCTOU, fixed-control feasibility, truthful receipts and descendant/crash shutdown |

The [Internal Model Tool Catalogue](Internal_Model_Tools.md) is the exposure inventory for D-001/D-008/D-011.
Registry/schema/lane coverage, unavailable-tool exclusion, and host-only boundaries are release evidence, not implied by an SDK's native tool support.

## Roadmap Dependencies

R01's initial-release policy reconciliation was approved on 2026-10-05 and is
recorded below. This closes design discrepancies, not runtime or release proof.
The deferred standalone-application rollback question is due before R27, not
before ordinary read-only tools or fixed bundled actions.

| Decisions | Primary roadmap packages and closure evidence |
|---|---|
| D-001/D-004 | [R02-RT1/RT2/MG1/PV1](Implementation_Roadmap.md#r02-runtimeprovider-follow-up-gates) complete .NET, observation and account/budget evidence; R06/R08 integrate mediated local/remote loop, R13 deterministic core then independently gated model-assisted management |
| D-002/D-003/D-007 | R02 candidate/licence/hardware selection, with [R02-L1-L5](Implementation_Roadmap.md#r02-local-inference-continuation) local-inference qualification and L6 handoff; R03/R07/R09 actual privacy/local-answer/wake trials, R19 integrated acceptance |
| D-005 | R02/R17 protected Linux-first distribution with Windows WiX packaging and installed Windows evidence, R18 notify-only maintenance; no install-capable updater implied |
| D-006 | Optional R24 only; do not make learning or verification a baseline voice prerequisite |
| D-008/D-012 | R01 resolved authority/origin rules, R03/R05 common privacy/grant gateway, R11/R15/R16 real containment/call/power enforcement |
| D-009/D-010 | Approved standard SQLite/private-profile integration, R04 durable identities/evidence, R17 ordinary packaged loading, R12 lifecycle/deletion/queue and R13 isolated scheduler/resource budgets; encrypted-key/native admission superseded |
| D-011 | R05 shared questions/grants, R06 registry, R12/R13 addressing/ledger, R14/R18 coordinated workspace/proactive interaction |
| D-013 | R02 staged worker/deployment continuation, R11 admitted fixed execution, R16 actual controlled effects and R17 installed identity/ACL/runtime proof; none implied by experiment or package assembly |

R19 and R23 compile the applicable release results; a merged implementation or
fake-only test does not close a decision that requires real boundary evidence.

## R02 Runtime/Provider Feasibility Outcomes

The 2026-10-05 [recorded experiment](../experiments/r02-runtime-proof/EVIDENCE.md)
tested Node SDK 1.0.16 / runtime 1.0.90 through synthetic loopback providers:
13 PASS, 1 FAIL and 3 BLOCKED outcomes, with 16 passing conformance tests.
This is candidate evidence, not selection approval or decision closure.

- **Reject:** hook-only result mediation. Failed results do not traverse the
  successful-result hook. Require host sanitization and supported final
  serialized-request gating before every send.
- **Measured .NET RT1:** the separate
  [actual .NET fixture](../experiments/r02-dotnet-control-proof/EVIDENCE.md)
  passes 45/45 tests for an explicitly approved exact-tag source-built
  v1.0.16/runtime 1.0.90 minimal HTTP/stdio profile. Final full-request,
  pre-effect denial, volatile session I/O, result/history/exception,
  streaming/error/cancellation and execution/management boundaries are
  measured. Hook-only FAIL remains. No Node bridge or production adapter.
- **Carry forward:** the host byte/deadline envelope, explicit no-retry
  boundary and truthful cancellation/unknown effects; none proves a hosted
  account allowance or rollback.
- **Keep open/blocked:** released NuGet artifact byte parity (TLS acquisition
  failed; source build explicitly approved), complete lifecycle
  destination/diagnostic/storage observation (RT2), full .NET management
  envelope (MG1) and hosted-account/auth/terms/usage/concurrency evidence (PV1).
  RT1 unblocks RT2/MG1 trials for those exact tested bytes, not Gate 0.

- **Measured bounded RT2, not closure:** the independent
  [lifecycle fixture](../experiments/r02-runtime-lifecycle-proof/EVIDENCE.md)
  passes 20/20 tests and two locale contracts, with exact final source-byte
  reproduction and all 45 unchanged staged RT1 regressions. Native PowerShell/
  console-host helpers and transient policy-test writes are observed.
  All-path attribution/native prevention remains **Blocked**; the user
  retained fail-closed runtime privacy and deferred privileged tracing to a
  dedicated host. W2 best-effort transitive dependency tracking is a separate
  policy, not runtime-egress consent. D-004 still needs MG1/account envelope;
  D-010 still needs integrated leases/scheduler/unknown-effect evidence.

The hook-only failure rejects an option, not every Copilot integration.
Closure evaluates the selected final-request-gated profile and its remaining
required controls; it does not require reclassifying the historical failure.

[Runtime and Provider Feasibility](Runtime_Provider_Feasibility.md) owns the
technical path and unsupported-candidate stop condition. The
[named follow-up gates](Implementation_Roadmap.md#r02-runtimeprovider-follow-up-gates)
assign runtime/security/provider owners and measurable go/no-go exits.
D-001 closes only on the relevant actual runtime/provider evidence; D-004
additionally requires the management envelope/account trial. D-010 still
requires integrated resource leases and scheduler/unknown-effect evidence.
Unapproved provider trials do not block the deterministic local core.

**2026-10-06 scoped follow-up:** [MG1](../experiments/r02-dotnet-management-proof/EVIDENCE.md)
records separate user approval for released SDK 1.0.16 after the original
source-built artifact failed reproduction. Released RT1 conformance is
45/45, with the rejected hook-only FAIL retained, followed by 22 host and
16 actual SDK/native management tests. This supersedes the *outstanding MG1*
work above only for that released minimal profile; it does not revise the
historical source-built record, claim byte equivalence, close RT2/PV1 or
establish a production protocol. D-001/D-004/D-010 and R04 storage/authority
admission remain open.

## R01 Accepted Policy Reconciliation

Product approval in the R01 session resolved the initial-release policy choices:

| Contract | Accepted decision and canonical owner | Implementation / evidence still required |
|---|---|---|
| Management power authority | M/E may submit typed proposal-only shutdown/restart requests. The deterministic host lifecycle controller owns all-session review, approval, countdown and dispatch through the admitted execution gateway/worker; neither model lane approves or executes. [Security contract](Security_Data_Flows.md#management-power-proposal-authority) | R05/R06/R13/R16 lane, grant, quiescence, cancellation and real worker/OS proofs |
| Microphone consent and enablement | Explicit first-launch ongoing consent; saved consent permits ordinary safe automatic launch/restart. Unlock/resume/manual disablement/permission or device loss require explicit recovery within the run. Run-scoped holds do not survive ordinary restart; consent withdrawal does. [Canonical matrix](Security_Data_Flows.md#microphone-consent-and-enablement-matrix) | R03/R09/R17 actual Windows/audio ownership, event and stale-generation trials; bootstrap auto-listening is not this complete policy |
| Optional private speech | Owner-confidence fallback applies only while owner-aware protection is enabled; its failure never silently disables it. Baseline voice trusts the active unlocked profile and normal output/privacy/call policy without compulsory verification. Learning is not enrollment or authority. [Output privacy](Security_Data_Flows.md#explicit-verification-and-output-privacy) | R09/R15 normal output gates; R24 verifier/learning claims only if separately delivered |
| Standalone lock Session binding | The deterministic host creates a new durable Active control work session for an unaddressed standalone lock. Commit and present its identity/lineage before approval/dispatch; offer Session only after binding. Explicitly addressed Active sessions remain valid targets; window selection is never authority. [Binding rule](Built_In_Skills.md#standalone-lock-work-session-binding) | R04/R05/R11 persistence failures, targeting, priority-path grant/receipt and lifetime evidence |
| Acceptance placement | Ollama/offline answering remains required A2 evidence; fixed bundled containment and concurrent independent tasks remain required A3 evidence. [Additional capability evidence](Acceptance_Criteria.md#additional-capability-evidence) is already correctly classified | Actual provider, hardware, containment and concurrency evidence remains open |
| Standalone application rollback | No policy chosen for restoring old standalone application bytes. This is deferred R27 work, not a prerequisite for initial fixed capabilities. [Execution-grant boundary](../docs/skill-and-task-execution-design.md#execution-grants) | Record the rollback/applicability decision before R27 admission; never weaken permanent bundled-content revocation |

No required R01 product question remains open. D-001 through D-011 technical
choices/enforcement evidence retain their existing status and due checkpoints;
R01 does not claim their closure or acceptance of A0-A4.
R02's independent feasibility branches and R03's existing-host
ownership/privacy foundation may now start against these contracts.
R03 need not wait for the Copilot/provider decision, but production wake
exposure still needs its relevant R02/R09 proof.

## D-001 Copilot Adapter Control Proof

Stop if the adapter can transmit unreviewed context, invoke unmediated tools, retain undisclosed memory, or cannot cancel truthfully.
An alternative runtime needs a new recorded decision and the same gates.
The proof includes admitted-tool/enabled-skill discovery, typed proposals,
host-bound approvals, correlated result feedback, and continued reasoning
under [the interaction contract](Commands_Tools_And_Skills.md). Advertising a
catalogue or returning an action name alone does not prove a mediated tool loop.

## D-002 Wake and Endpointing Engine

Benchmark at least:

- `openWakeWord`
- `sherpa-onnx` keyword spotting
- Picovoice Porcupine

Candidate inclusion is not approval.
Verify code and model licences separately, including redistribution and commercial-use terms.
Run the packaged acceptance corpus on the reference Windows hardware before selecting an engine.
Record the selected engine/model/version, rejected alternatives, threshold rationale, and known accents/noise limitations.

## D-003 Local Inference Baseline

Select one Ollama-backed model that can complete the Slice A clipboard explanation on the reference CPU-only floor.
Record model identity/digest, licence, download/storage requirements, context limit, measured first-token/completion latency, cancellation behavior, and answer-quality fixtures.
If no candidate meets the floor, change the documented hardware floor or local-first product claim before implementation continues.
PR #13 pins consented per-user Ollama 0.35.1 and `qwen3:1.7b` by digest and
tests a completed inference response on loopback. This is a bootstrap/readiness
candidate, not reference-hardware performance, answer-quality, cancellation,
licence, or network-blocked offline acceptance evidence. Keep D-003 open.

The 2026-10-05 R02 experiment adds verified public candidate identity/licence
metadata, download-size evidence, 31 passing deterministic proof tests and a
real missing-endpoint/unavailable response without fallback. It produced no
real model answers, floor measurements or network-blocked success. Runtime
absence is a provisioning blocker, not rejection of the candidate.

[Local Inference Qualification and Technical Plan](Local_Inference.md) records
the outcomes' design consequences: separate total provisioning from the model
storage guard, test runtime compatibility rather than infer it from readiness,
budget the full context envelope rather than characters alone, distinguish
buffered production from experimental streaming, and verify cancellation and
egress at actual boundaries. Retain the pinned candidate for the first
consented trial; final selection and numeric UX/resource budgets are unresolved.
Follow [R02-L1-L5](Implementation_Roadmap.md#r02-local-inference-continuation)
before qualifying the local runtime; L6 carries evidence into integration.
Close D-003 only with owner-reviewed applicable proofs, not this document or
the harness PR.

Future interactive inference trials are recorded in the
[shared deferred-validation register](Deferred_Validation.md) and
[LI01-LI07 checklist](../experiments/r02-local-inference-proof/README.md#deferred-inference-trials).
Publication of partial evidence is separate from those qualification gates.
Speech/storage/containment/distribution approvals and observations are not inference
consent or evidence; their outstanding decisions remain independent.

## D-004 Management Inference Envelope

Model-assisted management is enabled only where the pinned provider permits and can sustain an independent management session.
The deterministic local path remains the product fallback and is tested first.
Record provider/version/account tier, terms constraints, concurrency behavior, quotas, rate limits, cost assumptions, and the date evidence was checked.

## D-005 Unsigned Packaging and Maintenance

Select WiX MSI + Burn as the production Windows installer direction, using
Linux builds/cross-publishing wherever feasible and Windows packaging where
needed. Preserve NSIS receipts as historical evidence; acquisition/build code
and the unexecuted Linux recipe are retired. Retained source/native inspection
checks have not been superseded by binary packaging alone.
The initial application performs notify-only update discovery and cannot download, stage, execute, mutate source, or activate a replacement.
Compare packaging options against protected installation, unsigned disclosure, and Windows-recovery gates without introducing an updater.
Any future install-capable updater is a separate decision requiring independently signed metadata with a protected offline/root trust anchor, threshold/key rotation, expiry, rollback/freeze protection, exact host-owned voice/UI approval, and mandatory OS checks.
Authenticode remains a later separate decision.

The 2026-10-05 R02 distribution proof records successful Ubuntu win-x64
cross-publishing, a Windows-assembled unsigned NSIS 3.13 setup, static
runtime/native/resource/licence inspection, and exact-revision managed-source
publishing with non-destructive reruns. This does not establish WiX
implementation acceptance or close D-005. The deployed application remains
Windows-only; portable architecture and Linux build infrastructure do not
promise another runtime platform.

The [canonical distribution outcomes](Distribution_And_Updates.md#r02-distribution-outcomes-and-direction)
own the technical consequences, prerequisite baseline and protection
assumptions. The
[distribution follow-up roadmap](Implementation_Roadmap.md#r02-distribution-follow-up-and-r17-delivery)
assigns the closure sequence:

- R02-D01: apply the repository's chosen licence/NuGet-notice controls, review
  external/native assets and declare launch inputs; no redistribution before clearance.
- R02-D02: WiX MSI + Burn direction selected; retire the standalone Linux NSIS
  gate and hand off packaging/lifecycle implementation to R17. Review/pin
  build tools and their terms before use; no separate WiX feasibility project.
- R02-D03: prove the independent deployment boundary on an approved Windows
  lab under actual application identities; coordinate worker requirements
  without treating absent workers as tested.
- R17-D01/D02: binary MSI/custom Burn packaging, scoped startup/completion
  options and Linux-first release/provenance automation are implemented.
  Production managed-source delivery and installed qualification remain open;
  do not promote lab-only scripts as the production bootstrap.
- R17-D03: integrated runtime-only Windows acceptance against the exact final
  artifact and implemented resources/workers before production sign-off.

The owner approved unsigned beta/stable POC releases with front-loaded/ad-hoc/
risk-based installed validation, not an exhaustive manual trial for every MSI.
Full CI licence/test/coverage/payload/MSI ICE gates still apply; configured
publication is not an actual release receipt. Silent related-bundle upgrades
and numeric-version beta ordering remain unsupported; see
[installer limits](../installer/README.md#package-behavior-and-limits).
Windows installed/protection trials remain outstanding. Build-tool/external
asset review and future resources remain separate requirements, not waived
gates. Preserve the independently gated future
signed-metadata trust design and unsigned-phase notify-only maintenance.

Installer-assisted dependency setup is an optional convenience, independent
of the packaging choice. The application must retain detection and
installation/configuration, and users may decline all optional providers and
use the dependency-qualified deterministic command subset. See the
[shared optional-dependency contract](Environment_Setup.md#optional-dependencies-and-built-in-command-only-operation).
The WiX direction selected above does not demonstrate installer assistance
or waive required product-level provider/resource acceptance.

## D-006 Optional Speaker Verification

No verifier or frequent-speaker profile is required for general voice use or baseline explicit voice approval in an unlocked Windows session.
The separately consented [frequent-speaker learning](Security_Data_Flows.md#optional-local-frequent-speaker-learning) direction is accepted as non-authorizing personalization, not authenticated-owner enrollment.
Select and benchmark local attribution/recognition improvements, drift/correction/replacement policy, bounded samples, protected storage and deletion before advertising it; leave this optional capability open until measured.
Do not expose owner-aware claims until the selected local verifier, anti-spoof behavior, protected enrollment/storage, and acceptance thresholds pass.
Failure leaves confidence `Unavailable` and preserves visual privacy fallback.

## D-008 Approval and Grant Implementation

Implement the host-owned risk taxonomy and grant types before enabling action approvals.
Prove exact/non-inherited scope, owner-presence fallback, native review/edit/revoke flows, immediate revocation for new dispatch, truthful in-flight handling, content-minimising audit evidence, and intent lineage against hostile content using already-existing grants.
No model, skill, provider, or tool may classify its own risk or approve/create/extend a grant without host-mediated direct user confirmation.
The current model path validates registered action/grant proposals and offers
once/session/always model-action approval, including model-suggested lock.
Exact direct lock does not yet pass the same gate; executable/script hash-bound
grants, embedded `.ps1` execution, and the full risk/audit proof are future work.
Keep D-008 open and reconcile both routes under
[the execution design](../docs/skill-and-task-execution-design.md).
Use [Interaction and Sessions](Interaction_And_Sessions.md#approval-and-risk-session-trust-not-mouse-superiority): voice and UI express equivalent intent; risk affects exact review/confirmation, not obligatory clicks or blanket Windows Hello.
Preserve mandatory OS/provider verification and prohibited effects. Verify risk against effects, scope, reversibility, environment, exposure, privileges, and enforced constraints, not script prose.
The same evidence must cover UI task invocations and skill workflows, not just
exact phrases and model proposals. Skill enablement/selection, clarification,
and script review remain separate from the exact task execution grant.

**2026-10-06 bounded R05 foundation:** portable typed questions and exact
operation proposals, revision/scope checks, atomic Once use, explicit
remove/edit and permanent observed-content revocation now have concrete
core/application services and tests. Original caller origin and protected-call
reusable-grant rules remain enforced; legacy action-name preferences are not
authority for these bindings. See [R05 delivery/pending gates](Implementation_Roadmap.md#r05-bounded-authorizationquestion-foundation).
The adapter is an explicit atomic storage/audit contract with a test-only
implementation. Production durable approval storage, native trusted input/
review, actual dispatch composition and the verified R04 handoff remain
pending. D-008 stays open; D-009's standard SQLite baseline and D-013's
exact-declared/best-effort-transitive and containment requirements are unchanged.

## D-009 Session Persistence and Retention

### Approved Profile-Secured SQLite Baseline - 2026-10-06

The owner explicitly approved **standard SQLite under the supplied
LocalApplicationData path with verified private profile permissions**.
This supersedes the mandatory page-encryption, database DPAPI-key,
encrypted-native admission and rekey requirements in the historical R02
outcome below. No SQLCipher/SQLite3MC package, commercial codec or owned
native build is required. Use the existing pinned Microsoft.Data.Sqlite /
SQLitePCLRaw `e_sqlite3` distribution; SQLite remains bundled infrastructure,
not an optional download or an ambient system-library dependency.

This is an account-boundary threat model, not protection against same-user
malware, administrators or offline/copied-file access. Database, journal,
artifact, staging and managed backup files remain inside a verified private
local partition. Copies exported or removed from that boundary are readable;
first-use/export disclosure must say so. Credentials/secrets remain in
Windows-protected credential storage and are not ordinary database content.
Profile location alone does not establish correct permissions.

Transactions, versioned schema validation, corruption/access failure,
ordered intent/receipts, truthful Interrupted/Unknown recovery, no automatic
replay, retention and managed-copy deletion remain required. D-008 audit
authority/tamper evidence and D-013 worker containment are separate gates;
ordinary SQLite or ACLs do not close them. Existing crypto primitives and
experiments are historical/optional evidence, not prerequisites for this store.

The owner also requested **optional database encryption as a future enhancement
if a suitable supported provider becomes available**. Track this as
[R30](Implementation_Roadmap.md#optional-and-deferred-work), not a restored
R04/initial-release prerequisite. Revisit a maintained, compatible,
redistributable packaged provider without assuming a custom native build;
any eventual opt-in feature needs safe migration/key recovery and accurate
journal/backup coverage. No provider is selected or newly admitted now.

The current bounded [SQLite task store](../src/Kora.Windows/Storage/WindowsSqliteHostTaskStore.cs)
implements private-folder/file checks, a distinct version-1 host schema,
FULL-synchronous transactions and revision-checked event/state commits.
It has no database key or automatic schema/data replacement. The next bounded
R04 milestone composes exact typed/activated-voice version queries with the
private task store and independent typed SQLite diagnostic/audit/span/link
projections. Required evidence admission failures prevent dispatch or terminal
success; startup recovery commits intent-only Interrupted and dispatched/
unverified Unknown without replay. First-use greeting, settings and version
response disclose that removed/copied files are readable and same-user/admin
access is not prevented. This is not complete R04/D-009 acceptance: managed
backups/artifact publication, content/session lifecycle and deletion, audit
checkpoints/pruning, broader migrations and installed/power-loss evidence
remain open under the R04 inventory and deferred register.

The next [R04/R05 interaction slice](Implementation_Roadmap.md#r04r05-durable-interaction-and-minimal-session-authority---2026-10-06)
uses this same baseline, not a new provider decision. Its private version-1
interaction schema atomically persists typed question/grant/session authority
and its own typed audit. Matching committed task intent is validated while
holding the task lease through the interaction commit; independent diagnostic
projections cannot substitute for admission. Active generation survives restart,
ended/resumed scope never revives, and independent Perpetual records survive
authority removal. No encrypted-native/key prerequisite, legacy grant
migration, general content store or effect dispatch is introduced. Authority
row removal is not recoverable-copy deletion; full R12 retention/deletion,
audit anchors/pruning/whole-store rollback and installed/power-loss acceptance
remain open.

### R02 Windows Storage Outcome - 2026-10-05

**Historical, superseded encryption direction:** the investigation proposed:
maintained authenticated whole-database encryption for content-bearing session,
diagnostic and audit events/metadata/indexes, AES-GCM managed artifacts, and
random keys wrapped by CurrentUser DPAPI with restricted local ACLs.
The retained-event policy is also selected: diagnostic database records default
to 30 days under their independent configurable setting; audit records default
to 90 days and are configurable from 30 through 365 days. Session deletion does
not delete content-minimising audit rows, and perpetual grants remain outside
audit expiry.
No shipping native package or final session/task schema is selected.
Windows-only product support is confirmed; Linux runtime and local Linux-host validation are not storage-readiness blockers.
This does not change the existing Linux-hosted Windows build/distribution pipeline.

The [measured R02 snapshots](../experiments/r02-storage-proof/MEASUREMENTS.md) record passing automated assertions, 14 actual process-kill/reopen trials and Windows x64/x86 asset publication, with historical results preserved.
It demonstrates synthetic authentication failure, transactional rollback/recovery, source preservation on failed conversion, same-user DPAPI custody and the limits of logical deletion/rekey.
Point-read/write timings support small-workload feasibility, not product performance thresholds.
No second-account denial trial was performed. On reassessment, it is optional OS-boundary corroboration for profile-local storage, not an application admission or publication gate.
Required integration evidence is [CurrentUser scope, profile-local copies and effective ACLs](Architecture.md#profile-boundary-and-validation-responsibility); x86 publication is not x86 execution or clean-installed deployment evidence.

| Choice / finding | Design consequence |
|---|---|
| Authenticated page encryption supports content-bearing FTS without plaintext index persistence | Preferred strategy; keep temporary stores in memory and explicitly key backups |
| Measured unofficial package embeds old SQLCipher/SQLite/crypto versions | Reject it for production; admit a maintained distribution only after provenance/licence and real Windows deployment review |
| Envelopes authenticate content but reveal metadata; keyed equality reveals linkability/frequency | Conditional fallback only after revisiting D-009 and explicitly accepting narrower search/leakage; no silent plaintext FTS shortcut |
| SQLite and artifact publication are separate durability domains | Require staged/orphan/missing-reference recovery, bounded admission and authenticated identity-bound artifacts |
| Rekey/unlink/wrapper deletion leave old backups or copied wrappers recoverable | Coordinate key/backup generations and inventory deletion ownership; do not claim per-session cryptographic or forensic erasure |
| Valid-record removal and old valid backups are not detected by content authentication | Keep D-008 tamper-evident audit and host recovery/replay controls separate |

Remaining **R02 storage/key admission gates**: maintained native selection and installed Windows x64/x86 loading/protection.
Publish/merge the documented partial outcome through normal repository checks without claiming these capability gates are closed.
Do not admit production persistence based on synthetic evidence alone.
R04 must implement ordered commits, versioned key/backup recovery, verified legacy conversion and artifact reconciliation on the admitted engine; test rekey/wrapper publication interruption and relevant durability/failure boundaries.
It must verify profile-local managed paths/copies, CurrentUser use without machine-scope fallback and actual directory/key-file permissions. Profile location alone is not evidence of correct integration.
R12 owns integrated lifecycle, source revocation, append-versus-delete races and managed-copy cleanup.
Neither the prototype's "intent without receipt" fixture nor absence of a sentinel proves real dispatch recovery or complete absence of plaintext.

Reconsider this strategy if a maintained codec cannot pass Windows admission, key recovery/deletion requirements cannot be met, or representative history/artifact/index workloads miss product budgets.
Changing the native engine, crypto provider, .NET runtime or backup/key format requires rerunning the affected proof and updating the canonical requirements and evidence together.
D-009 remains open until the storage/security owners accept its remaining implementation and real-boundary evidence.
The [deferred-validation register](Deferred_Validation.md#storage-admission-follow-up) separates mergeable research from these application/deployment gates and records when multi-account trials would become relevant.

### Lifecycle and Integration Closure

The encrypted-route findings below are retained history, not current
standard-SQLite prerequisites; the approved baseline above owns current scope.

R04's 2026-10-06 isolated implementation is **partial, not D-009 closure**.
The [delivery inventory](Implementation_Roadmap.md#r04-foundation-delivery)
records portable host identities, ordered state/recovery contracts, versioned
W3C sources, bounded structured diagnostic/typed audit envelopes, independent
sink failure/gap handling and retention calculations. Content persistence and
the encrypted evidence projection remain unavailable. No unapproved native
engine, commercial dependency, plaintext fallback or envelope-based SQLite
substitute is admitted. Daily-file typed audit copies and in-memory recovery
tests cannot establish authoritative durable receipts, audit tamper evidence
or installed protection. Native selection and S1-S4 gates remain open.

On 2026-10-06 the owner approved **evaluation, not production admission**, of
maintainer-owned
[SQLite3MC.PCLRaw 2.4.0](https://github.com/utelle/SQLite3MultipleCiphers-NuGet/tree/v2.4.0)
as a noncommercial route. Its tagged source declares SQLite3MC 2.4.0 /
SQLite 3.53.4, a netstandard2.0 provider using SQLitePCLRaw.core 3.0.2,
and both Windows native RIDs. The
[authenticated ChaCha20-Poly1305 page configuration](https://utelle.github.io/SQLite3MultipleCiphers/docs/ciphers/cipher_chacha20/)
requires authentication checking to remain enabled; selecting an
unauthenticated cipher or disabling `hmac_check` is not admitted.
MIT engine/wrapper metadata does not discharge embedded-component notice
requirements (including SHA2 BSD and the applicable Argon2/Aegis notices).

Public research found active upstream maintenance but a NuGet/native release
lag (packaged 2.4.0 versus observed native 2.5.1). Certificate-validated
acquisition of the exact provider/lib packages failed with Schannel
`SEC_E_ILLEGAL_MESSAGE` and PowerShell TLS errors; one bundle receipt was
observed, but payloads were not retained and exact package closure is not
verified. Actual nuspec/RID/signature/native-byte/notice inspection, servicing
ownership, .NET 10 authentication/WAL/recovery tests and installed x64/x86
acceptance remain S1 requirements. No production dependency or notice was
changed. Official SQLCipher commercial binaries were not approved; an owned
SQLCipher Community build remains a possible separately owned alternative,
not an implicitly selected fallback.

The subsequent 2026-10-06 continuation resolved package acquisition through
the certificate-validated official NuGet v2 endpoint. Bundle/provider/lib
2.4.0 and SQLitePCLRaw.core 3.0.2 were retained, and `dotnet nuget verify
--all` passed NuGet.org **repository**, not publisher/author, signatures.
Actual x64/x86 native hashes match the maintainer's cross-builder checksum
metadata; both DLLs are Authenticode-unsigned and import only KERNEL32.dll.
The cross-builder variants are not byte-identical to the separately built/
signed main-project release DLLs. An owned synthetic x64/.NET 10 fixture
passed 44 assertions covering exact native identity/version, authenticated
ChaCha20 with `hmac_check=1`, encrypted database/WAL, reopen and missing-key,
wrong-key and page-tamper rejection. This is not installed acceptance or
complete wrapper/recovery qualification; a separate ADO wrapper probe was
blocked at temporary compilation by CS1701 framework-reference warnings.

**SQLite3MC 2.4.0 is not suitable for production admission despite those
basic passes.** Its source and packaged cross-builder amalgamation contain
the read-error handling defect corrected by
[upstream PR 272](https://github.com/utelle/SQLite3MultipleCiphers/pull/272)
(`b4a88cfcd0bb50fee1c477c45a0f638d8b1b2372`): failed statement-journal reads
can be overwritten with successful-looking outcomes and cause silent
corruption during rollback. Upstream's
[temporary-file fix](https://github.com/utelle/SQLite3MultipleCiphers/commit/f25f25aad60b59209b95be80a481268e0eae6ee5)
also identifies previously plaintext temporary/statement-journal paths;
these changes postdate the evaluated release. A happy-path encrypted
database/WAL test cannot waive either defect.

The package's MIT expression also does not describe all compiled obligations:
the generated provider/Core require Apache-2.0/NOTICE attribution, SHA2
requires BSD-3-Clause binary notices, and the native source has unresolved
Rijndael documentation and enabled extension-permission ambiguities.
No production manifests, native binaries or redistribution notices were
changed. Choosing an owned official-release SQLCipher Community build or
a reviewed patched/reduced SQLite3MC build requires an explicit owner
decision about candidate, build provenance, licences and servicing; neither
is silently selected. Production persistence remains unavailable.

Prove atomic intent/decision/event recording, readable recovery with interrupted/unknown work, key protection, source-revocation handling, and full permitted history without raw audio/secrets.
Both archive and deletion durations are configurable; the same meaningful-activity clock controls them and passive browsing does not refresh it.
Test timer/startup/access expiry, live-work holds, changed-policy apply-now confirmation, deletion completeness and disclosed independent audit/provider/export limitations.

## D-010 Concurrent Work Sessions

Record provider/version/account-tier and actual supported concurrency, context isolation, cancellation, hardware/cost limits, and the configured/effective budget.
Verify simultaneous independent-resource writes, conflicting read/write exclusion, unknown-effect handling, dependency holds, and fairness.
An isolated UI chat or an SDK-created session is not evidence of independent safe execution.

## D-011 Shared Interaction and Model Tools

The accepted [window structure](UI_Workspace_And_Windows.md) is compact latest interaction, a Sessions workspace with list beside full conversation/history, and separate immutable detail/script viewing.
The detail design reuses passive Markdown/static-HTML/source presenters and
host-owned provenance chrome. Session, skill, settings and permission
management remain purpose-built native workspaces; rendered content cannot own
mutation, approval, dirty state or workflow authority. Settings/setup/guide
and optional caption/web surfaces remain coordinated supporting roles, not
alternative hidden interaction systems.
The accepted voice interaction uses the activation name for unsolicited turns
and permits prefix-free schema-valid answers only inside one timed
host-opened conversational turn bound to the unique foreground
question/revision. Timeout or target/gate change returns to wake activation;
the exception never weakens confirmation or approval policy.
Define versioned question/draft/reply and presentation contracts with stable session/task/proposal identity.
Demonstrate pure voice, pure UI, and mixed workflows; historical/rendered content cannot answer itself or grant authority.
Verify relatedness routing only to clear Active matches, explicit targeting precedence, archived read versus resume, and bounded evidence-cited history queries under local-only/remote-egress policy.
Prove shared per-session drafts, multi-session attention, no background focus theft, deterministic voice-target transitions, independently bound detail references, and workspace/compact selection during concurrent streaming.

## D-012 Accepted Trust Boundary and Verification

The user controls the desktop and chooses enabled verbal instructions; who speaks an activated command is not itself an authorization requirement.
Kora does not secure a computer the user no longer controls. Residual indistinguishable external speech/playback is accepted without making absent speaker/system-output verification a blanket voice release blocker.
Keep self-output rejection, supported playback discrimination, exact action/scope checks, untrusted-content isolation, safe interruption, and the explicit call-specific restrictions.
The [accepted controls](Security_Data_Flows.md#accepted-controls-and-verification-boundary) resolve the design-level concerns within this threat model, not all possible risks or runtime implementation.
Do not mark the corresponding capabilities release-ready until adversarial, race, persistence/restart, call-transition and failure evidence demonstrates the host controls.

## D-013 Windows Worker and Deployment Containment

The [R02 measured proof](../experiments/r02-containment-proof/evidence/README.md)
met 63/71 OS assertions with eight network-denial assertions unproven.
The [canonical outcomes and continuation gates](Security_Data_Flows.md#r02-windows-containment-outcomes)
record what this changes technically:

- Reject same-user PowerShell plus Job Objects as restricted execution.
- Retain capability-free AppContainer plus a non-breakaway kill-on-close job
  as a partial filesystem/credential/lifetime candidate, not a production
  profile or executable allowlist.
- Next establish attributable network denial on a supported OS and resolve
  reviewed-script/fixed-control feasibility before R11 dispatch admission.
  A typed native broker is an alternative requiring an explicit decision and
  contract reconciliation, not an implicitly selected workaround.
- Independently validate normal-host and worker deployment rights under
  [the R17 identity/alias requirements](Security_Data_Flows.md#protected-deployment-identity-and-validation).
  Build/inspect-only distribution work cannot satisfy real installation trials.
- Preserve Unknown for effects without verifiable receipts; cancellation or
  process termination does not roll back effects or permit automatic replay.

Keep this decision open until the accountable owners sign off on the applicable
real-boundary evidence. Reconsider the mechanism if attributable network denial,
protected identities/dependencies or required fixed controls cannot be enforced
without broadening ambient authority. Other R02 branches may close independently.

**2026-10-06 accepted dependency/review amendment:** after the
[W2 real-boundary trials](../experiments/r02-w2-dependency-proof/README.md),
the owner selected best-effort tracking of other scripts/modules/binaries
for both bundled and future user scripts. The user granting permission is
responsible for the script's overall actions within its admitted scope.
The user can view/approve internal scripts: every manifest-listed file is
available via named read-only tabs. The complete declared script-set hash,
definition identity and observed-content-change revocation rules remain exact.
Discovery gaps and possibly undetected transitive changes must be disclosed.
See [the authoritative rule](Built_In_Skills.md#best-effort-transitive-dependency-tracking).

ACL projection and no-child mitigation are rejected as **exact** dependency
mechanisms: native denials coexist with undeclared byte/module/script execution,
and no-child denies declared helpers too. The amendment removes universal
dynamic-dependency denial as a default script-grant condition; it does not
convert these trials into strict enforcement success. Separate in-memory
helper/entry execution and an owned fixed effect were observed; native fixture
loading is blocked by absent compiler, and real Windows controls were not run.
No typed broker/adapter substitution was selected. W1/W3/W4, protected runtime
resolution, R11/R16 and installed R17 evidence remain outstanding; no ambient
execution, general user-script exposure or production profile is authorized.

## Decision Completion

For every completed decision:

1. Link the evidence or reproducible test result.
2. Record the selected option and material rejected alternatives.
3. Update all affected design and acceptance documents.
4. Record residual risks and an explicit reconsideration trigger.
5. Change status only after the accountable owner signs off.
