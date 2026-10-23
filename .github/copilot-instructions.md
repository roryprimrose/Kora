# Kora repository instructions

## Product and architecture

- Kora is a Windows-only, local-first Avalonia desktop assistant targeting .NET 10.
- Preserve the dependency direction: `Kora.Core` contains portable contracts and domain rules; `Kora.Tools` contains host-owned C# actions; `Kora.Definitions` contains bundled behavior definitions/resources; `Kora.Application` contains orchestration and presentation state; `Kora.Windows` contains Windows integrations; `Kora` composes the desktop. Tools and Definitions reference Core, never Application/Windows/desktop. Keep Windows APIs, handles, registry/device APIs and OS-specific exceptions out of Core, Tools, Definitions and Application.
- Before proposing or starting a feature, apply [product admission](../Design/MVP_Scope.md#product-admission-and-realignment): name the approved outcome, concrete scenario, host-versus-extension rationale and complete interaction. Source layout, dependencies, tests and spare capacity are not product approval. Repairs, accessibility, reliability and proportionate usability within approved outcomes do not need a new decision; material expansion or retired-work revival does.
- Prefer bounded increments toward approved voice/model interaction, shared settings proposals, governed tools/MCP/skills/agents/instructions, presence/readable results and permissioned session conversations. An unavailable provider is not permission to invent unrelated utilities. Do not recreate retired features or fill slots with variants; report a shortage of approved work.
- Completion means the approved operation, not navigation or field preparation. Preserve equivalent voice/UI review where permitted and mandatory authentication/privacy/channel exceptions. Keep identity/revision/digest mechanics internal unless explicitly needed for diagnostics.
- Unsent conversation text is per-session composer state, not a saved-document product: preserve legacy-data recovery, privacy and retention; do not add disk autosave or confuse it with question/definition-editing drafts.

## Implementation and scoped guidance

- Use small cohesive owners, strong types and nullable analysis. Keep policy/serialization authoritative in one place; reuse domain validation and shared admitted actions. Surface failures with repository-standard diagnostics and recovery, never silent defaults or success-shaped fallbacks.
- Use asynchronous I/O/lifecycle APIs and deterministic disposal. Allow `async void` only at framework event or `ICommand` boundaries with explicit failure handling.
- Follow the applicable scoped guidance for matching edits and cross-cutting work:
  - [Architecture and clean implementation](instructions/architecture.instructions.md): pragmatic SOLID, readable code, layers, ownership, shared routes and lifecycle.
  - [Behavioral testing](instructions/testing.instructions.md): deterministic regressions, typed outcomes and forbidden effects.
  - [Security boundaries](instructions/security.instructions.md): authority, revalidation, audit and resource quiescence.
  - [Automation evidence](instructions/automation.instructions.md): complete module/shard evidence, analyzers and CI.
- Use the [design topic owners](../Design/README.md#topic-owners) and [extension placement guide](../EXTENDING-KORA.md) for canonical contracts. Instructions guide agents; maintained automated gates enforce requirements. Do not claim a guard exists or a client loaded instructions without evidence.

## Safety, persistence and retention

- Treat privacy, consent, instance ownership, approval, audit and resource quiescence as security boundaries. Unknown session, device, permission or ownership state fails closed; do not weaken a boundary to simplify work.
- Preserve [security/data-flow policy](../Design/Security_Data_Flows.md), including [independent retention domains](../Design/Security_Data_Flows.md#retention-defaults). Session history, diagnostics, audit, grants and definitions have different lifetimes; retained content is not renewed authority. Never put raw audio, transcripts, response/speech content, credentials or secrets in diagnostic/audit sinks.
- Store device-local preferences under `IApplicationDataPaths` using the shared atomic preference store. Domain types own validation/format semantics; the store owns paths, directory creation, unique temporary files, replacement and cleanup. Reject malformed saved values with `InvalidDataException`, not default reinterpretation. Security/privacy/model-approval/device writes retain their audit outcomes.

## Delivery, readiness and proof

- Apply the [I - Implementation / E - Enablement / Q - RC policy](../Design/Implementation_Roadmap.md#delivery-and-qualification-dependency-policy). Determine the minimum consumed implementation subset; an Rxx label or its remaining completion gap is not a monolithic prerequisite.
- Retired, historical, optional, physical, lab, hardware, acoustic, provider, containment, installed-native, accessibility or experiment evidence is not an implementation prerequisite. Require it only when enabling, advertising, packaging or materially changing the exact capability/profile and its owning contract requires it.
- E/Q gates do not block bounded fail-closed implementation, deterministic tests, PR creation or merge. Missing/unknown runtime readiness remains explicitly disabled or unavailable, with deterministic fakes at the platform seam; no speculative enablement, success fallback, real hardware/network substitute or open-ended proof harness.
- Claim implementation-blocked only for a concrete missing consumed production contract/service, directly applicable failing maintained check, unresolved material security/consent/authority decision or conflicting live ownership. Cite the exact symbol, test, decision or owner. Delegated owners retain implementation, tests, documentation, PR, CI/review repair and merge without waiting for unrelated proofs.

## Instrumentation and correlation

- Follow [contributor logging conventions](../CONTRIBUTING.md#logging-conventions): source-generated `[LoggerMessage]` partial methods only, in sibling `{ClassName}.Logging.cs` files with matching namespace/partial class, including logging helpers. No `Log*` extensions or `LoggerMessage.Define`. Preserve event IDs, levels, templates, typed property names, exceptions and callers; no interpolation or rendered-text parsing.
- Follow [activity tracing and evidence correlation](../Design/Architecture.md#activity-tracing-and-evidence-correlation): versioned layer `ActivitySource`, W3C IDs, stable low-cardinality operation names, truthful terminal status and host-owned correlation. No content/secrets in activity names/tags or Kora identities/content in `Activity.Baggage`.
- Preserve async parentage; capture admitted context at enqueue and use `ActivityLink` for deferred/fan-out/reconciliation/restart/multiple-cause work, not fabricated parents. Capture `Activity.Current` at log-call time before buffering. Activities are causal operations, not durable session identity.
- Use the trusted typed audit path for correlated request/terminal outcomes in the authoritative audit table and file sink. Lookalike `SecurityAudit=true` properties, provider fields, scopes and incoming trace headers are not authority. Session work/audit commits require valid host-resolved context; only explicitly classified pre-host bootstrap diagnostics may be uncorrelated.

## Validation and documentation

- Identify the changed invariant and owning contract. Supply focused behavioral regressions, exact commands/results and unrun checks; distinguish inspection, executed tests, implementation readiness and enablement/qualification. Never weaken tests, analyzers, 100% portable line/branch coverage or security boundaries to obtain green checks.
- Use [contributor development checks](../CONTRIBUTING.md#development-checks) for pinned SDK, locked restore, build, MTP/xUnit v3/AwesomeAssertions and scheduling commands. Run the smallest covering checks; documentation-only work needs links/headings, exact examples and availability checks, not unnecessary builds or device/provider trials.
- Do not modify `experiments` as production code or treat experimental evidence as a production contract unless explicitly tasked. Markdown directly under `docs` is embedded end-user guidance; keep developer/CI guidance beside engineering scripts or in contributor documentation.

## Pull requests and merge admission

- Follow the [account-safe PR skill](skills/kora-github-pr/SKILL.md) and [merge admission client protocol](../eng/merge-queue/README.md#copilot-and-other-pr-clients). Verify publisher, owned worktree/branch/head, repository and live queue mode before writes; failed/unknown reads confer no authority.
- Keep one owned same-repository PR to main per ready feature, preserving an explicit draft request. Protected squash Auto-merge needs user/workflow merge authorization, not merely PR-creation authority.
- When hosted mode is enabled, GitHub owns candidate selection, branch updates, dispatch and stale-work cancellation. No main-chasing rebases/force-pushes, manual candidate dispatch or old-ticket reruns. When disabled, retain ordinary protected CI/integration.
- Inspect current candidate-head App checks and PR/review state; skipped waiting jobs or successful main checks are not admission. Repair the same PR, incorporating App updates. Verify actual merged state and reachability before claiming completion. Never claim unsupported background monitoring or change App installation, secrets, environments, protection, queue mode or durable queue state without separate administrator authorization.
