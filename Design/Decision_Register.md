# Architecture and Delivery Decision Register

Status: active proposed-design register.

Related: [MVP Scope](MVP_Scope.md), [Architecture](Architecture.md), [Acceptance Criteria](Acceptance_Criteria.md).

This register records accepted product/UX direction separately from unresolved technical choices and implementation evidence that can block or materially change delivery.
The named owner is a role until an individual is assigned.
A decision is not complete because an implementation was started; its evidence and affected documents must be updated together.
An Accepted direction status records the agreed behavior, not completed contracts, tested enforcement, or release readiness. Remaining schema/integration/proof work stays open until its acceptance evidence passes.

| ID | Decision | Owner | Due before | Status | Required evidence |
|---|---|---|---|---|---|
| D-001 | Copilot adapter/control-point viability | Runtime engineering lead | Slice A0 implementation | Open, release-blocking | Pinned SDK/version; context/tool/egress interception; streaming; cancellation; independent session behavior; unsupported built-ins disabled |
| D-002 | Local wake detector, endpointing, and playback rejection | Speech engineering lead | Slice A1 implementation | Open, release-blocking | Candidate benchmark, redistribution/licence review, packaged Windows assets, recall/false activation, CPU/memory, immediate-command preservation, self-activation tests |
| D-003 | Local inference baseline | Runtime engineering lead | Slice A2 acceptance | Open, release-blocking; bootstrap candidate pinned | Pinned Ollama/runtime versions, selected model, licence, download size, reference hardware floor, answer/cancellation quality, offline proof |
| D-004 | Management inference provider envelope | Runtime engineering lead | Slice A3 implementation | Open, release-blocking for model-assisted management | Independent-session permission, SDK/account tier, terms, quota/rate limit, cost estimate, 32 KiB/4 KiB bounds, 15-second deadline, deterministic fallback |
| D-005 | Unsigned Windows package and notify-only maintenance | Release engineering lead | First public binary candidate | Open, release-blocking | NSIS proof, Linux build, final-byte hash/provenance, Unknown Publisher/SmartScreen UX, no install-capable updater, external Windows evidence; future signed-metadata root design separately gated |
| D-006 | Optional frequent-speaker learning and verifier | Security and speech leads | Before advertising learned-speaker/owner-aware capability | Accepted optional direction; engine/privacy proof open | Separate consent, local protected per-SID/device learning, predominant-speaker/drift/playback quality, reset/delete and privacy evidence; separately enrolled verifier FAR/FRR/anti-spoof proof and protected OS workflow |
| D-007 | Supported Windows/reference hardware matrix | Product and test leads | Slice A1 acceptance | Open, release-blocking | Windows versions, CPU/RAM, microphones/headsets, accessibility baseline, test machine ownership and reproducible environment |
| D-008 | Approval/grant implementation and audit model | Security engineering lead | Before general side-effecting execution | Accepted scopes/lifetimes; schema/enforcement proof open, release-blocking; initial model grants only | Single-use consumption, operation-bound durable session grants, perpetual grants without retention/eviction, applicability/provenance after chat deletion, native explicit edit/removal, intent lineage, audit tamper evidence, fatigue/race acceptance tests |
| D-009 | Durable session storage, lifecycle and deletion | Storage and security leads | Revised Slice A3 implementation | Accepted lifecycle direction; storage/schema/deletion proof open, release-blocking | Encrypted SQLite/artifact/index integration, OS-protected keys, event ordering, crash/migration recovery, configurable 24-hour/30-day inactivity policies, journal/cache/backup deletion, no action/grant replay |
| D-010 | Concurrent sessions and resource coordination | Runtime engineering lead | Revised Slice A3 implementation | Accepted bounded concurrency; isolation/budget proof open, release-blocking | Pinned SDK/provider isolation and concurrency budgets, proposed two-slot baseline, one task per session, canonical shared/exclusive resource leases, outside-change revalidation, fair scheduling, cancellation/unknown-effect races |
| D-011 | Shared interaction and session routing/history tools | Product and application leads | Revised Slice A3/A4 implementation | Accepted UX direction; protocol/integration proof open, release-blocking | Voice/UI/mixed structured questions and exact grants, compact interaction/list-plus-conversation workspace/separate detail surfaces, minimal Active-session routing context, bounded paginated tools, provenance/egress, foreground voice versus addressed UI races |
| D-012 | Windows-session trust model and accepted voice boundary | Product and security leads | Design acceptance; enforcement before associated capability release | Accepted direction; implementation evidence outstanding | Enabled verbal input trusts the active unlocked profile, not speaker identity; no compulsory biometrics/PTT/UI for ordinary voice; scoped grants, call origin/reuse gates, intent/content separation, containment and truthful recovery tests |

The [Internal Model Tool Catalogue](Internal_Model_Tools.md) is the exposure inventory for D-001/D-008/D-011.
Registry/schema/lane coverage, unavailable-tool exclusion, and host-only boundaries are release evidence, not implied by an SDK's native tool support.

## D-001 Copilot Adapter Control Proof

Stop if the adapter can transmit unreviewed context, invoke unmediated tools, retain undisclosed memory, or cannot cancel truthfully.
An alternative runtime needs a new recorded decision and the same gates.

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

## D-009 Session Persistence and Retention

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

## Decision Completion

For every completed decision:

1. Link the evidence or reproducible test result.
2. Record the selected option and material rejected alternatives.
3. Update all affected design and acceptance documents.
4. Record residual risks and an explicit reconsideration trigger.
5. Change status only after the accountable owner signs off.
