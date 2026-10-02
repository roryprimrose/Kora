# Architecture and Delivery Decision Register

Status: active proposed-design register.

Related: [MVP Scope](MVP_Scope.md), [Architecture](Architecture.md), [Acceptance Criteria](Acceptance_Criteria.md).

This register owns unresolved choices that can block or materially change delivery.
The named owner is a role until an individual is assigned.
A decision is not complete because an implementation was started; its evidence and affected documents must be updated together.

| ID | Decision | Owner | Due before | Status | Required evidence |
|---|---|---|---|---|---|
| D-001 | Copilot adapter/control-point viability | Runtime engineering lead | Slice A0 implementation | Open, release-blocking | Pinned SDK/version; context/tool/egress interception; streaming; cancellation; independent session behavior; unsupported built-ins disabled |
| D-002 | Local wake detector, endpointing, and playback rejection | Speech engineering lead | Slice A1 implementation | Open, release-blocking | Candidate benchmark, redistribution/licence review, packaged Windows assets, recall/false activation, CPU/memory, immediate-command preservation, self-activation tests |
| D-003 | Local inference baseline | Runtime engineering lead | Slice A2 implementation | Open, release-blocking | Pinned Ollama/runtime versions, selected model, licence, download size, reference hardware floor, answer/cancellation quality, offline proof |
| D-004 | Management inference provider envelope | Runtime engineering lead | Slice A3 implementation | Open, release-blocking for model-assisted management | Independent-session permission, SDK/account tier, terms, quota/rate limit, cost estimate, 32 KiB/4 KiB bounds, 15-second deadline, deterministic fallback |
| D-005 | Unsigned Windows package and maintenance technology | Release engineering lead | First public binary candidate | Open, release-blocking | NSIS and Velopack comparison, Linux build proof, final-byte hash/provenance, Unknown Publisher/SmartScreen UX, protected install/update rights, external Windows evidence |
| D-006 | Optional speaker verifier | Security and speech leads | Before advertising owner-aware speech | Open, optional capability | Verifier/anti-spoof candidate, licence, local packaging, FAR/FRR targets, replay/cloned-speech tests, protected per-SID storage, Windows Hello enrollment |
| D-007 | Supported Windows/reference hardware matrix | Product and test leads | Slice A1 acceptance | Open, release-blocking | Windows versions, CPU/RAM, microphones/headsets, accessibility baseline, test machine ownership and reproducible environment |

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

## D-004 Management Inference Envelope

Model-assisted management is enabled only where the pinned provider permits and can sustain an independent management session.
The deterministic local path remains the product fallback and is tested first.
Record provider/version/account tier, terms constraints, concurrency behavior, quotas, rate limits, cost assumptions, and the date evidence was checked.

## D-005 Unsigned Packaging and Maintenance

Prototype NSIS first because it matches the single-setup-EXE and Linux-packaging direction.
Compare Velopack only against the same protected-installation, explicit-update-approval, unsigned-disclosure, and Windows-recovery gates.
If neither passes, allow a documented release-boundary revision rather than silently weakening installation protection.
Code signing remains a later separate decision.

## D-006 Optional Speaker Verification

No verifier is required for general voice use.
Do not expose owner-aware claims until the selected local verifier, anti-spoof behavior, protected enrollment/storage, and acceptance thresholds pass.
Failure leaves confidence `Unavailable` and preserves visual privacy fallback.

## Decision Completion

For every completed decision:

1. Link the evidence or reproducible test result.
2. Record the selected option and material rejected alternatives.
3. Update all affected design and acceptance documents.
4. Record residual risks and an explicit reconsideration trigger.
5. Change status only after the accountable owner signs off.
