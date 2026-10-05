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
| D-001 | Copilot adapter/control-point viability | Runtime engineering lead | Slice A0 implementation | Open, release-blocking | Pinned SDK/version; context/tool/egress interception; streaming; cancellation; independent session behavior; unsupported built-ins disabled |
| D-002 | Local wake detector, endpointing, and playback rejection | Speech engineering lead | Slice A1 implementation | Open, release-blocking | Candidate benchmark, redistribution/licence review, packaged Windows assets, recall/false activation, CPU/memory, immediate-command preservation, self-activation tests |
| D-003 | Local inference baseline | Runtime engineering lead | Slice A2 acceptance | Open, release-blocking; R02 identity/licence metadata and unavailable-path evidence recorded; candidate unqualified | [R02 local-inference outcomes and plan](Local_Inference.md): pinned candidate, distribution review, agreed budgets, actual CPU-floor quality/performance/context/cancellation and network-blocked successful answering; owner-reviewed selection |
| D-004 | Management inference provider envelope | Runtime engineering lead | Slice A3 implementation | Open, release-blocking for model-assisted management | Independent-session permission, SDK/account tier, terms, quota/rate limit, cost estimate, 32 KiB/4 KiB bounds, 15-second deadline, deterministic fallback |
| D-005 | Unsigned Windows package and notify-only maintenance | Release engineering lead | First public binary candidate | Open, release-blocking | NSIS proof, Linux build, final-byte hash/provenance, Unknown Publisher/SmartScreen UX, no install-capable updater, external Windows evidence; future signed-metadata root design separately gated |
| D-006 | Optional frequent-speaker learning and verifier | Security and speech leads | Before advertising learned-speaker/owner-aware capability | Accepted optional direction; engine/privacy proof open | Separate consent, local protected per-SID/device learning, predominant-speaker/drift/playback quality, reset/delete and privacy evidence; separately enrolled verifier FAR/FRR/anti-spoof proof and protected OS workflow |
| D-007 | Supported Windows/reference hardware matrix | Product and test leads | Slice A1 acceptance; inference qualification before A2 | Open, release-blocking; R02 development inventory is not floor qualification | Windows versions, named reference CPU/RAM, microphones/headsets, accessibility baseline, test machine ownership and reproducible environment; [R02-L1/L3](Implementation_Roadmap.md#r02-local-inference-continuation) supported CPU-only inference-floor evidence |
| D-008 | Approval/grant implementation and audit model | Security engineering lead | Before general side-effecting execution | Accepted scopes/lifetimes; schema/enforcement proof open, release-blocking; initial model grants only | Single-use consumption, operation-bound durable session grants, perpetual grants without retention/eviction, applicability/provenance after chat deletion, native explicit edit/removal, intent lineage, audit tamper evidence, fatigue/race acceptance tests |
| D-009 | Durable session storage, lifecycle and deletion | Storage and security leads | Revised Slice A3 implementation | Lifecycle and Windows encryption direction recorded; synthetic R02 evidence measured; native/key integration and schema/deletion acceptance open, release-blocking | Maintained authenticated SQLite and AES-GCM artifacts, CurrentUser/profile-path/effective-ACL integration and installed Windows evidence, event ordering, crash/migration/key/backup recovery, configurable 24-hour/30-day inactivity policies, journal/cache/backup deletion, no action/grant replay |
| D-010 | Concurrent sessions and resource coordination | Runtime engineering lead | Revised Slice A3 implementation | Accepted bounded concurrency; isolation/budget proof open, release-blocking | Pinned SDK/provider isolation and concurrency budgets, proposed two-slot baseline, one task per session, canonical shared/exclusive resource leases, outside-change revalidation, fair scheduling, cancellation/unknown-effect races |
| D-011 | Shared interaction and session routing/history tools | Product and application leads | Revised Slice A3/A4 implementation | Accepted UX direction; protocol/integration proof open, release-blocking | Voice/UI/mixed structured questions and exact grants, compact interaction/list-plus-conversation workspace/separate detail surfaces, minimal Active-session routing context, bounded paginated tools, provenance/egress, foreground voice versus addressed UI races |
| D-012 | Windows-session trust model and accepted voice boundary | Product and security leads | Design acceptance; enforcement before associated capability release | Accepted direction; implementation evidence outstanding | Enabled verbal input trusts the active unlocked profile, not speaker identity; no compulsory biometrics/PTT/UI for ordinary voice; scoped grants, call origin/reuse gates, intent/content separation, containment and truthful recovery tests |
| D-013 | Windows worker and protected-deployment containment | Security and Windows engineering leads | R11 execution admission; R17 protected-deployment acceptance | Open, release-blocking; partial AppContainer proof, Job-only restriction rejected | Attributable OS network denial, executable dependency admission, effective app/worker identities and protected-root ACLs, aliases/TOCTOU, fixed-control feasibility, truthful receipts and descendant/crash shutdown |

The [Internal Model Tool Catalogue](Internal_Model_Tools.md) is the exposure inventory for D-001/D-008/D-011.
Registry/schema/lane coverage, unavailable-tool exclusion, and host-only boundaries are release evidence, not implied by an SDK's native tool support.

## Roadmap Dependencies

R01's initial-release policy reconciliation was approved on 2026-10-05 and is
recorded below. This closes design discrepancies, not runtime or release proof.
The deferred standalone-application rollback question is due before R27, not
before ordinary read-only tools or fixed bundled actions.

| Decisions | Primary roadmap packages and closure evidence |
|---|---|
| D-001/D-004 | R02 actual SDK/provider control and budget proofs, R06/R08 mediated local/remote loop, R13 independent management/concurrency |
| D-002/D-003/D-007 | R02 candidate/licence/hardware selection, with [R02-L1-L5](Implementation_Roadmap.md#r02-local-inference-continuation) local-inference qualification and L6 handoff; R03/R07/R09 actual privacy/local-answer/wake trials, R19 integrated acceptance |
| D-005 | R02/R17 protected Linux-built distribution/installed Windows evidence, R18 notify-only maintenance; no install-capable updater implied |
| D-006 | Optional R24 only; do not make learning or verification a baseline voice prerequisite |
| D-008/D-012 | R01 resolved authority/origin rules, R03/R05 common privacy/grant gateway, R11/R15/R16 real containment/call/power enforcement |
| D-009/D-010 | R02 storage/key native and real Windows admission gates, R04 durable identity/encrypted storage, R12 lifecycle/deletion/queue, R13 isolated scheduler/resource budgets |
| D-011 | R05 shared questions/grants, R06 registry, R12/R13 addressing/ledger, R14/R18 coordinated workspace/proactive interaction |
| D-013 | R02 staged worker/deployment continuation, R11 admitted fixed execution, R16 actual controlled effects and R17 installed identity/ACL/runtime proof; none implied by experiment or package assembly |

R19 and R23 compile the applicable release results; a merged implementation or
fake-only test does not close a decision that requires real boundary evidence.

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
Speech/containment/distribution approvals and observations are not inference
consent or evidence; their outstanding decisions remain independent.

## D-004 Management Inference Envelope

Model-assisted management is enabled only where the pinned provider permits and can sustain an independent management session.
The deterministic local path remains the product fallback and is tested first.
Record provider/version/account tier, terms constraints, concurrency behavior, quotas, rate limits, cost assumptions, and the date evidence was checked.

## D-005 Unsigned Packaging and Maintenance

Prototype NSIS first because it matches the single-setup-EXE and Linux-packaging direction.
The initial application performs notify-only update discovery and cannot download, stage, execute, mutate source, or activate a replacement.
Compare packaging options against protected installation, unsigned disclosure, and Windows-recovery gates without introducing an updater.
Any future install-capable updater is a separate decision requiring independently signed metadata with a protected offline/root trust anchor, threshold/key rotation, expiry, rollback/freeze protection, exact host-owned voice/UI approval, and mandatory OS checks.
Authenticode remains a later separate decision.

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

## D-009 Session Persistence and Retention

### R02 Windows Storage Outcome - 2026-10-05

The investigation now drives the [canonical Windows storage direction](Architecture.md#windows-durable-storage-direction):
maintained authenticated whole-database encryption for content-bearing events/metadata/indexes, AES-GCM managed artifacts, and random keys wrapped by CurrentUser DPAPI with restricted local ACLs.
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

Prove atomic intent/decision/event recording, readable recovery with interrupted/unknown work, key protection, source-revocation handling, and full permitted history without raw audio/secrets.
Both archive and deletion durations are configurable; the same meaningful-activity clock controls them and passive browsing does not refresh it.
Test timer/startup/access expiry, live-work holds, changed-policy apply-now confirmation, deletion completeness and disclosed independent audit/provider/export limitations.

## D-010 Concurrent Work Sessions

Record provider/version/account-tier and actual supported concurrency, context isolation, cancellation, hardware/cost limits, and the configured/effective budget.
Verify simultaneous independent-resource writes, conflicting read/write exclusion, unknown-effect handling, dependency holds, and fairness.
An isolated UI chat or an SDK-created session is not evidence of independent safe execution.

## D-011 Shared Interaction and Model Tools

The accepted [window structure](UI_Workspace_And_Windows.md) is compact latest interaction, a Sessions workspace with list beside full conversation/history, and separate immutable detail/script viewing.
Settings/setup/permissions/guide and optional caption/web surfaces remain coordinated supporting roles, not alternative hidden interaction systems.
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
  executable-dependency/fixed-control feasibility before R11 dispatch admission.
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

## Decision Completion

For every completed decision:

1. Link the evidence or reproducible test result.
2. Record the selected option and material rejected alternatives.
3. Update all affected design and acceptance documents.
4. Record residual risks and an explicit reconsideration trigger.
5. Change status only after the accountable owner signs off.
