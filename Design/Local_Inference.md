# Local Inference Qualification and Technical Plan

Status: R02 partial evidence and proposed continuation plan, reviewed 2026-10-05.
D-003 and the local-inference part of D-007 remain open. No runtime/model
replacement, hardware-floor revision or A2 release approval is established.

Continuation preparation reviewed 2026-10-08: the
[sanitized receipt](../experiments/r02-local-inference-proof/results/continuation-2026-10-08/preparation.json)
records 55 current deterministic checks and six worktree-path regression
checks. The supported per-user executable, PATH command, port-11434 listener
and default pinned-model manifest were absent. No runtime/model acquisition,
startup, generation, residency change or network isolation was performed.
Other installation locations were not searched; an absent supported location
does not establish that the machine contains no runtime anywhere.

### Unattended Provisioning Admission - 2026-10-09

The operator conditionally approved this machine for exact pinned provisioning
and a bounded, non-exclusive synthetic observation **only if fully unattended**.
The [sanitized preflight receipt](../experiments/r02-local-inference-proof/results/unattended-preflight-2026-10-09/admission.json)
and [refreshed public metadata](../experiments/r02-local-inference-proof/results/unattended-preflight-2026-10-09/candidate-metadata.json)
record the resulting **Blocked before installation** decision.

The supported executable, PATH command, listener and selected default model
manifest remain absent. Official release/winget metadata agree on the pinned
installer digest; the registry manifest and licence blob match the unchanged
model pin. These are metadata/source observations, not installed-byte or
packaged-native licence verification.

The repository's delivered acquisition path is per-user winget/Inno setup.
The exact upstream `v0.35.1` installer source declares an HKCU environment
`Path` update, post-install launch of `ollama app.exe`, and pre-install/
uninstall `taskkill /im ... /f /t` hooks. Those effects are incompatible with
the approved no-broad-PATH, no-UI and no-unrelated-process scope, regardless of
winget's `--disable-interactivity` flag. `PrivilegesRequired=lowest` is not a
guarantee of absence of those other effects. Tagged source is not a binary
execution observation, and no unsupported installer override was attempted.

No installer/weight bytes were acquired, winget/setup/runtime was not
executed, and no synthetic live request or teardown was needed. An official
portable ZIP is advertised, but it is not this repository's approved
provisioning/ownership/teardown path; it was neither downloaded nor silently
substituted. The next decision must explicitly select and approve a reviewed
isolated acquisition/lifecycle path, or stop. The current conditional approval
does not admit exclusive residency/resource measurements, independently
blocked egress/capture or later L3/L4/L6 acceptance.

### Three-Tier Admission Policy

1. **Development:** deterministic/file-only proof preparation and unrelated
   feature development continue while local-inference qualification is open.
   Merge of a research receipt does not certify a capability.
2. **Capability:** these outstanding gates block local-inference enablement
   and advertising of the affected envelope, not unrelated capabilities.
   Historical bounded bootstrap observations are not final qualification or
   permission for a new trial.
3. **Release candidate:** an RC manifest that includes local inference requires
   its applicable qualified/integrated evidence. An RC that explicitly omits
   the capability is not blocked solely by this proof.

This policy changes neither approval/ownership boundaries nor production
runtime/model pins. The next live step requires an operator-selected approved
environment and separately scoped acquisition/startup, exclusive residency,
resource and isolation decisions. Previous one-shot trial approvals are not
ongoing consent on this host.

Related: [Decision Register](Decision_Register.md#d-003-local-inference-baseline),
[Architecture](Architecture.md#local-inference-qualification),
[Setup](Environment_Setup.md#local-inference-provisioning-budget),
[Acceptance](Acceptance_Criteria.md#local-inference-evidence),
[Roadmap](Implementation_Roadmap.md#r02-local-inference-continuation),
[Deferred Validation](Deferred_Validation.md).

This is the canonical technical interpretation and path forward for the R02
local-inference branch. The [experiment](../experiments/r02-local-inference-proof/README.md)
owns reproducible commands/fixtures; its
[results](../experiments/r02-local-inference-proof/RESULTS.md) and
[post-rebase validation](../experiments/r02-local-inference-proof/results/final-validation/validation.json)
retain observations, not production capability claims.

## Outcomes and Their Consequences

The evidence was collected against unchanged production code on 2026-10-05.
The source-linked Release proof built with zero warnings/errors and passed
31 deterministic tests, including simulated measurement orchestration.
Those tests validate the harness and negative paths, not real model answers.

| Observed outcome | Technical/design consequence | Remaining evidence |
|---|---|---|
| Public manifest matches the existing `qwen3:1.7b` digest; manifest-linked Apache-2.0 licence blob verified; tagged Ollama 0.35.1 source is MIT | Keep the existing candidate as the first comparison baseline; there is no evidence to justify replacing it | Installed executable/weight integrity, packaged native-dependency licences/notices and distribution review |
| Real loopback probe reports `Missing`; synthetic reasoning reports `Unavailable`; only version/tags calls occurred and no remote fallback | Preserve truthful unavailable behavior and deterministic recovery; absence is an environment blocker, not evidence the model fails quality/performance | Successful real answers and unhealthy/missing-model/timeout trials under independently verified network isolation |
| No installed/listening candidate was available and no provisioning/isolation consent was supplied | Next work begins with an owned, approved test environment; do not silently install assets, change firewall rules or choose cloud inference | Test owner, provisioning consent, supported OS, isolation plan and captures |
| Development host has 16 logical cores and about 23.78 GiB visible RAM; no real inference ran | The proposed floor is neither validated nor disproved; a faster development host cannot qualify it | Named CPU on supported Windows 11 x64, 8-logical-core/16-GiB floor trial, SSD, power/UI/service contention and CPU-only residency |
| Production sends one buffered JSON generation with `num_predict=512`, a two-minute deadline and a 4,096-character request limit | Treat streaming as R08 implementation work and the current bounds as bootstrap behavior, not qualified token/capacity budgets | Real production-default answer quality and completion; separate streamed first-token measurements, total-envelope token accounting and output/thinking behavior |
| Model card advertises 32,768 tokens; no effective-window trial ran | Do not advertise that entire window as usable by Kora; selected context must fit the tested runtime envelope | Prompt/system/history/tool-result/output budgeting and explicit truncation/oversize behavior |
| Published setup is 1,580,352,416 bytes; model blobs total 1,359,293,444 bytes | Plan roughly 2.94 GB fresh transfer before overhead, not just the 1.36 GB model; the existing 2 GB guard is model-volume-only | Expanded runtime, peak installer/pull/staging storage, per-volume free space and RAM/KV-cache measurements |
| Mocks cover cancellation and fail-closed errors; real model cancellation was unavailable | Client cancellation alone cannot prove server work stopped or guarantee recovery capacity | Server/runner cessation, late-output rejection, completion/cancel races, actual timeout and subsequent quality/recovery |
| Loopback transport disallows remote providers, redirects and proxies | Retain this adapter boundary, but never equate it with OS egress containment of the environment/runtime | Whole-environment IPv4/IPv6 denial plus independent process-correlated evidence during successful and unavailable trials |

Exact identities, sizes, sources and verification limits are retained in
[candidate metadata](../experiments/r02-local-inference-proof/results/candidate-metadata.json).
Do not confuse a published installer hash or manifest hash with locally
verified installed binaries/weight bytes. No alternative was benchmarked.

## Qualification Contract

### Candidate and Compatibility

The first trial uses the existing Ollama `0.35.1` / `qwen3:1.7b` production
pins without changing them. The model manifest digest is
`sha256:8F68893C685C3DDFF2AA3FFFCE2AA60A30BB2DA65CA488B61FFF134A4D1730E7`.
Candidate retention is a trial recommendation, not final selection.

The bootstrap setup/probe identifies a responding runtime and verifies the
model digest; it does not enforce an exact runtime version. The experiment
requires `0.35.1` in its strict measurement preflight. R06/R10 must reconcile
this distinction with a host-owned, **tested** compatibility envelope:
record actual version/capabilities, reject unsupported capability claims,
and explain incompatibility without replacing a user's healthy installation.
A newer identified version is not automatically qualified by an older proof.
No compatibility-policy implementation or change of production pins is
delivered by this documentation.

### Resource and Context Budgets

Before performance trials, product/runtime/test leads must record numerical
cold/warm first-token and completion budgets, memory/CPU headroom under
UI/service contention, and cancellation/recovery deadlines. These local-answer
budgets are currently **unresolved**; the bootstrap two-minute timeout is a
failure bound, not an approved UX latency target. Agree the budgets before
seeing results, then compare every trial and p50/p95/max against them.

Reference-machine selection must include a named CPU and power profile, not
only a logical-core count. Retain the proposed supported-Windows-11-x64,
8-logical-core/16-GiB/SSD floor until the responsible owners approve a change.
A constrained VM can aid isolation but does not automatically represent a
physical reference CPU, memory pressure, speech concurrency or thermals.

The production 4,096-character request cap is not a token budget. R06/R07/R08
must account for the complete approved envelope, including system/catalogue,
selected snapshot, history, tool results, output reservation and any thinking
tokens. Test the selected window and oversize boundaries before increasing
limits; use explicit bounded selection or rejection, not silent truncation.
Do not turn experimental `num_ctx`, sampling or CPU overrides into production
defaults without conformance and hardware evidence.

### Answer, Cancellation and Offline Evidence

Use the experiment's expected-answer fixtures and its
[automated/human rubric](../experiments/r02-local-inference-proof/README.md#answer-quality-rubric)
as the R02 evaluation baseline. Human review must establish factual grounding,
uncertainty, relevance and instruction separation, not merely lexical matches
or nonempty output. Require every critical safety fixture to pass, no action
dispatch/authority from quoted content, and record individual failures.

Measure at least 30 unloaded-model cold and 30 paired warm trials with raw
timings, output, runtime counters and process resource observations. Separate
production-default buffered results from experimental streamed results.
Report generated/thinking-token versus response-token timing distinctly;
neither a JSON brace nor thinking output is a displayed semantic answer.

Extend the real trial beyond the harness's single scheduled cancellation:
exercise repeated completion/cancellation races and the actual timeout,
observe server/runner computation stopping or disclose bounded residual work,
reject late output and verify a subsequent answer. A cancelled HTTP await
alone does not pass cancellation/resource containment.

The test owner must approve provisioning and **whole-environment** remote
network blocking while retaining loopback. Assets are verified before the
test interval; both Kora and Ollama/runner must be covered. Independently
verify denied IPv4/IPv6/DNS/proxy/NAT paths and retain start/end times, controls
and process-correlated capture. Run successful answering, cancellation/recovery
and missing/unhealthy trials under the same block. Local-only tasks acquire no
assets and never use remote-provider fallback.

The [offline procedure](../experiments/r02-local-inference-proof/README.md#approved-network-blocked-proof-procedure)
is an operator runbook, not authorization to alter a shared machine.
Missing/blocked evidence remains explicit; zero sampled resources when no model
ran must not be reported as an efficient model.

## Path Forward and Decision Gate

The [R02-L1 through R02-L6 roadmap steps](Implementation_Roadmap.md#r02-local-inference-continuation)
own order, dependencies, accountable roles and completion receipts:

1. Name/approve the reference and isolated test environments and agree budgets.
2. Separately consent to provision exact pinned assets; capture identity,
   licences and per-volume provisioning/storage evidence.
3. Run and review actual CPU-floor quality, timing, context, cancellation,
   errors and recovery, retaining failures as well as distributions.
4. Repeat relevant trials with independently verified remote-network denial.
5. Record owner-reviewed selection or rejection, supported envelope and
   unresolved capabilities in D-003/D-007 and dependent contracts.
6. Carry the qualified envelope into R06/R07/R08/R10; repeat the proofs on the
   actual integrated host before A2/R19 sign-off.

L1-L5 qualify the candidate, not the production tool loop or A2 clipboard
workflow. L6 is the integration handoff, completed by the dependent packages.
R03 ownership/privacy and unrelated R02 branches may continue independently;
local runtime exposure cannot inherit approval from harness tests.

The [shared deferred-validation register](Deferred_Validation.md) and
[LI01-LI07 checklist](../experiments/r02-local-inference-proof/README.md#outstanding-testing-checklist)
are the interactive-session handoff. They separate deterministic reruns from
real endpoint generation, separately approved provisioning/residency/isolation,
and rows requiring independent instrumentation or later host implementation.
The merged speech/storage/containment proofs do not qualify inference or supply consent
for it; whole-environment inference isolation does not resolve the worker's
attributable network-denial gate. Safe standalone inference measurements remain
distinct from UI/voice acceptance with the required R03/R09 controls.

Partial research, truthful outcomes and this continuation plan may merge under
normal checks/reviews while the interactive trials remain outstanding. They
are qualification/capability blockers, not merge prerequisites for that scope.
Historical snapshots retain their original revision, draft status and results.

If the candidate fails agreed quality, resource, context, cancellation or
offline gates, retain failed evidence and request a consented alternative
comparison or an explicit hardware/product-scope decision. A changed runtime,
model digest, inference options, compatibility range or approved envelope
requires affected proofs to be rerun. Never substitute a remote provider,
raise the hardware floor silently or close D-003 because the experiment merged.
