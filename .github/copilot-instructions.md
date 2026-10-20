# Kora repository instructions

## Product and architecture

- Kora is a Windows-only, local-first Avalonia desktop assistant targeting .NET 10.
- Preserve the dependency direction: `Kora.Core` contains portable contracts and domain rules; `Kora.Tools` contains host-owned C# actions; `Kora.Definitions` contains bundled behavior definitions/resources; `Kora.Application` contains application orchestration and presentation state; `Kora.Windows` contains Windows integrations; `Kora` is the composition and desktop presentation layer. Tools and Definitions reference Core, never Application/Windows/desktop.
- Keep Windows APIs, native handles, registry access, device APIs, and OS-specific exceptions out of `Kora.Core`, `Kora.Tools`, `Kora.Definitions` and `Kora.Application`.
- Treat privacy, consent, instance ownership, approval, audit, and resource-quiescence checks as security boundaries. Do not weaken or bypass them to simplify a workflow.
- Unknown session, device, permission, or ownership state must fail closed.

## Design and implementation

- Follow SOLID and DRY pragmatically. Prefer small, cohesive services and consumer-focused interfaces over broad managers, god objects, or speculative abstractions.
- Put each policy or serialization rule in one authoritative location. Reuse existing domain validation rather than duplicating limits or parsing rules.
- Keep view models focused on presentation state and delegation. Move device operations, persistence, setup workflows, and policy decisions into injected collaborators.
- Implement built-in tool actions in `Kora.Tools/<CapabilityGroup>/<Action>.cs`, with matching `Kora.Tools.<CapabilityGroup>` namespaces and one class per registered action, not a broad class with a method per tool. Share cohesive policy/broker services; keep contracts/domain rules in Core, native mechanisms in Windows, and presentation in Application/desktop. All admitted command/UI/model routes call the same action. See [built-in tool implementation guidance](../Design/Commands_Tools_And_Skills.md#built-in-tool-source-layout-and-implementation).
- Keep bundled skills, prompt templates, shared instructions and future agent profiles in one `Kora.Definitions` project, grouped by content kind; keep each skill's own instructions/fixtures/scripts together. Preserve explicit resource IDs, exact bytes and package digests when relocating content. Definitions are not execution, approval, egress or session authority; agent profiles remain gated until qualified runtime/host support exists. See [definition and agent guidance](../Design/Commands_Tools_And_Skills.md#bundled-definitions-and-agent-profiles).
- Add abstractions only at real seams: platform integration, persistence, time/process/network dependencies, or a cohesive workflow with independent tests.
- Preserve strong types and nullable analysis. Do not use `dynamic`, `object`, or unsafe casts to avoid defining the correct contract.
- Surface failures explicitly with repository-standard logging and user-facing recovery. Do not silently fall back after invalid persisted state or failed security-sensitive work.
- Use asynchronous APIs for I/O and lifecycle work. `async void` is allowed only at framework event or `ICommand` boundaries, where failures must be routed to an explicit error handler.
- Dispose owned native, stream, timer, cancellation, and synchronization resources deterministically.
- Keep comments for non-obvious invariants, privacy guarantees, concurrency rules, or platform constraints; prefer clear names for ordinary behavior.

## Delivery, readiness, and proof policy

- Apply the [three-tier delivery policy](../Design/Implementation_Roadmap.md#delivery-and-qualification-dependency-policy) before declaring work blocked. Determine the minimum consumed **I - Implementation** subset for the requested behavior; a broad Rxx package or its remaining completion gap is not a monolithic prerequisite.
- Retired, historical, optional, physical, lab, hardware, acoustic, provider, containment, installed-native, accessibility, or experiment evidence is not an implementation prerequisite. Do not create, rerun, or wait for that evidence unless the change is enabling, advertising, packaging, or materially changing the exact affected capability/profile and its owning contract requires the evidence.
- **E - Enablement** and **Q - RC** gates do not block implementation, deterministic validation, pull-request creation, or merge of a bounded fail-closed feature. Keep the unqualified capability disabled or explicitly unavailable and document that boundary truthfully.
- A feature may be classified as implementation-blocked only by a concrete missing consumed production contract/service, a directly applicable failing maintained check, an unresolved material security/consent/authority decision, or conflicting live ownership. Cite the exact symbol, test, decision, or owner; do not cite an Rxx label, experiment, future proof, or qualification category alone.
- When runtime readiness is missing or unknown, implement the production path with explicit unavailable/fail-closed behavior and deterministic fakes at the platform seam. Do not substitute success-shaped fallbacks, speculative enablement, real hardware/network calls, or an open-ended proof harness.
- Include this distinction in delegated peer prompts and reviews. A peer owns implementation through deterministic tests, documentation, PR, CI/review repair and merge without waiting for unrelated or retired proofs.

## Instrumentation and activity correlation

- Always define `ILogger<T>` diagnostic messages with source-generated `[LoggerMessage]` partial methods; do not use `LogDebug`, `LogInformation`, other `Log*` extension methods, or `LoggerMessage.Define` delegates.
- Put all `[LoggerMessage]` declarations in a `{ClassName}.Logging.cs` file beside the class's other source files, using the same namespace and a matching partial class declaration. Logging-only helper classes also use this filename convention; reuse the existing layer helpers for shared events.
- When moving logging declarations, preserve event IDs, levels, templates, structured property names, exception parameters, and callers.
- Use structured `ILogger<T>` templates and typed named properties; do not use string interpolation or rendered-message parsing for query, correlation, outcome, or authorization data. Keep events compatible with both the daily JSON and SQLite providers.
- Emit security audit events through the trusted typed audit path. `SecurityAudit=true` routes to the dedicated authoritative audit table as well as the file sink; an arbitrary property or lookalike message must never acquire audit authority. Preserve correlated request and terminal outcomes around consequential operations.
- Use the layer's versioned `System.Diagnostics.ActivitySource` and W3C IDs at meaningful request, policy, runtime, tool, storage, presentation, evidence, retention, and recovery boundaries. Use stable low-cardinality activity names, end activities with truthful status, and do not instrument every method.
- Activities represent causal operations, not durable business identity. A session spans many traces. Add host-owned session, task, invocation, approval, and audit correlation IDs to admitted activity tags and logging scopes; never put user text, paths, titles, model output, credentials, or secrets in activity names or tags, and do not propagate Kora identities or content through `Activity.Baggage`.
- Preserve normal parentage through asynchronous calls. Capture admitted context when queueing work and use `ActivityLink` for deferred work, fan-out, reconciliation, restart-related work, or multiple causes rather than reusing a completed activity or fabricating a parent. Logging providers must capture `Activity.Current` when the log call occurs, before asynchronous sink buffering.
- Treat incoming trace headers and model/provider correlation fields as untrusted correlation only. They cannot select a Kora session or establish identity, intent, permission, approval, or authority. Session-bound work and audit commits require valid host-resolved activity/session context; only explicitly classified pre-host bootstrap diagnostics may be uncorrelated.
- Add focused tests for trace propagation and termination, queue/link behavior, cross-session isolation, hostile incoming context, structured log/audit correlation, and missing or expired trace segments.

## Persistence and configuration

- Store device-local preferences under the application data paths supplied by `IApplicationDataPaths`; never hard-code user profile paths.
- Use the shared atomic preference store for preference files. Domain preference classes own validation and format semantics, while the store owns paths, directory creation, unique temporary files, replacement, and cleanup.
- Reject malformed saved values with `InvalidDataException`; do not reinterpret corrupted or unknown values as defaults.
- Configuration writes that affect security, privacy, model approvals, or device behavior must retain their audit outcomes.

## Testing and validation

- Add focused tests for behavior changes, failure paths, cancellation, invalid persisted data, and privacy/resource-lifecycle invariants.
- Use xUnit v3 and AwesomeAssertions, following nearby test naming and fixture patterns.
- Keep tests deterministic and independent of installed devices, user settings, network access, or timing where a fake can model the boundary.
- Restore with locked dependencies and validate from the repository root:

```powershell
dotnet restore .\Kora.slnx --locked-mode
dotnet build .\Kora.slnx --configuration Release --no-restore
dotnet test --project .\tests\Kora.Core.UnitTests\Kora.Core.UnitTests.csproj --configuration Release --no-build
dotnet test --project .\tests\Kora.Application.UnitTests\Kora.Application.UnitTests.csproj --configuration Release --no-build
dotnet test --project .\tests\Kora.Tools.UnitTests\Kora.Tools.UnitTests.csproj --configuration Release --no-build
dotnet test --project .\tests\Kora.Definitions.UnitTests\Kora.Definitions.UnitTests.csproj --configuration Release --no-build
dotnet test --project .\tests\Kora.Windows.IntegrationTests\Kora.Windows.IntegrationTests.csproj --configuration Release --no-build
```

- Treat warnings as errors and keep analyzer suppressions narrowly scoped with a concrete justification.
- Do not modify files under `experiments` as production code or treat experiment evidence as a production contract unless the task explicitly targets an experiment.

## Pull requests and merge admission

- Follow the [merge admission guide](../docs/merge-queue.md#copilot-and-other-pr-clients) before publishing, enrolling, or monitoring a PR. Confirm the live queue mode; a failed lookup or unknown value is not permission to assume either mode.
- Create or update one owned, same-repository PR targeting main for each ready feature. Preserve an explicit draft request, and enable protected squash Auto-merge only when the user or owning workflow authorizes merging, not merely PR creation.
- When the hosted queue is enabled, let GitHub select the candidate, update its branch, dispatch validation, and cancel stale work. Do not chase main with repeated rebases/force-pushes, launch candidate workflows, or rerun an old ticket. When disabled, retain the ordinary protected CI/integration workflow.
- Monitor current candidate-head App checks and actual PR/review state; skipped waiting-PR jobs or successful main workflow checks are not candidate admission. Repair concrete failures on the same PR, incorporating any App branch updates before pushing, and verify the actual merge and its reachability before claiming completion.
- Copilot monitoring supports review and repair; it does not keep the hosted queue running. Never claim background monitoring without a supported continuation, or change App installation, secrets, environments, protection, queue mode, or durable queue state without separate administrator authorization.
