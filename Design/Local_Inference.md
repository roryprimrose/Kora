# Local Inference Disposition and Future Qualification

Status: the evaluated Ollama 0.35.1/qwen3:1.7b profile is rejected. The
Ollama 0.40.2 comparison is also rejected. Automated quality failures are
terminal for those candidates; no human scoring, reference-floor run or
offline lab can qualify them.

Production remains fail closed: local inference is unavailable unless the
pinned runtime/model identity is present and healthy, and there is no automatic
remote fallback. Provider-neutral contracts, UI, orchestration and replacement
candidate work continue without reopening these results.

Related: [Decision Register](Decision_Register.md#d-003-local-inference-baseline),
[Architecture](Architecture.md#local-inference-qualification),
[Setup](Environment_Setup.md#local-inference-provisioning-budget),
[Acceptance](Acceptance_Criteria.md#local-inference-evidence),
[Roadmap](Implementation_Roadmap.md#r02-local-inference-continuation), and
[Deferred Validation](Deferred_Validation.md).

## Unattended Provisioning Admission - 2026-10-09

The original absent-runtime and installer-admission observations are preserved
in the [validation archive](Archive/Deferred_Validation_2026-10-11.md) and
[experiment results](../experiments/r02-local-inference-proof/results/).
They are historical context, not current work or ongoing consent.

Later isolated evaluation used the official portable Ollama 0.35.1 bytes on a
dedicated loopback endpoint without changing the shared runtime. That candidate
failed automated answer-quality gates. Ollama 0.40.2 failed comparison gates.
Production pins were not changed.

## Three-Tier Admission Policy

1. **Development:** deterministic production boundaries and unrelated features
   continue. No local-inference qualification evidence is required.
2. **Capability:** a materially changed runtime/model/profile must pass the
   applicable identity, quality, bounds, cancellation and offline gates before
   it can be enabled or advertised.
3. **Release candidate:** an RC including local inference must identify the
   qualified profile and evidence. An RC explicitly excluding local inference
   is not blocked by rejected or unperformed candidate trials.

Environment evidence never grants installation, process, network, clipboard,
audio or tool authority. Each consequential trial retains its own approval and
ownership boundary.

## Outcomes and Their Consequences

| Outcome | Engineering consequence |
|---|---|
| Pinned 0.35.1/qwen3:1.7b failed automated quality gates | Keep unavailable; do not spend operator time on semantic scoring or hardware qualification |
| 0.40.2 comparison failed automated quality gates | Do not change the production pin to this version |
| Exact request/response, pinned identity, cancellation and timeout behavior have maintained production tests | Continue provider-neutral and replacement-candidate development under normal CI |
| Advertised model context, physical server cessation and independently blocked egress were not qualified | Reassess only for a future candidate that first passes automated quality gates |
| No candidate is accepted | D-003/local-inference D-007 and A2 remain unavailable-profile decisions, not general feature-development blockers |

## Future Candidate Gate

A proposed replacement must:

1. define exact runtime/model/options and licence/installation ownership;
2. pass automated expected-answer, authority, unavailable-path and recovery
   gates before human review;
3. establish the effective prompt/output envelope and explicit oversize
   behavior;
4. pass repeated cancellation/timeout/late-output recovery;
5. meet pre-agreed reference-hardware latency/resource budgets; and
6. prove offline success under independently attributable egress denial if an
   offline profile is claimed.

Use the retained
[experiment procedure](../experiments/r02-local-inference-proof/README.md#outstanding-testing-checklist)
only after a materially changed candidate is selected for enablement. Do not
repeat it for the rejected candidates.

## Platform Boundary Gate

Shared domain, policy, task, skill and configuration code remains portable.
Windows runtime/process/device mechanisms stay in Windows integration and
composition. Missing, malformed or changed runtime/model identity fails closed.
