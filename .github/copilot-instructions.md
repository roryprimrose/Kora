# Kora repository instructions

## Product and architecture

- Kora is a Windows-only, local-first Avalonia desktop assistant targeting .NET 10.
- Preserve the dependency direction: `Kora.Core` contains portable contracts and domain rules; `Kora.Application` contains application orchestration and presentation state; `Kora.Windows` contains Windows integrations; `Kora` is the composition and desktop presentation layer.
- Keep Windows APIs, native handles, registry access, device APIs, and OS-specific exceptions out of `Kora.Core` and `Kora.Application`.
- Treat privacy, consent, instance ownership, approval, audit, and resource-quiescence checks as security boundaries. Do not weaken or bypass them to simplify a workflow.
- Unknown session, device, permission, or ownership state must fail closed.

## Design and implementation

- Follow SOLID and DRY pragmatically. Prefer small, cohesive services and consumer-focused interfaces over broad managers, god objects, or speculative abstractions.
- Put each policy or serialization rule in one authoritative location. Reuse existing domain validation rather than duplicating limits or parsing rules.
- Keep view models focused on presentation state and delegation. Move device operations, persistence, setup workflows, and policy decisions into injected collaborators.
- Add abstractions only at real seams: platform integration, persistence, time/process/network dependencies, or a cohesive workflow with independent tests.
- Preserve strong types and nullable analysis. Do not use `dynamic`, `object`, or unsafe casts to avoid defining the correct contract.
- Surface failures explicitly with repository-standard logging and user-facing recovery. Do not silently fall back after invalid persisted state or failed security-sensitive work.
- Use asynchronous APIs for I/O and lifecycle work. `async void` is allowed only at framework event or `ICommand` boundaries, where failures must be routed to an explicit error handler.
- Dispose owned native, stream, timer, cancellation, and synchronization resources deterministically.
- Keep comments for non-obvious invariants, privacy guarantees, concurrency rules, or platform constraints; prefer clear names for ordinary behavior.

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
dotnet test --project .\tests\Kora.Windows.IntegrationTests\Kora.Windows.IntegrationTests.csproj --configuration Release --no-build
```

- Treat warnings as errors and keep analyzer suppressions narrowly scoped with a concrete justification.
- Do not modify files under `experiments` as production code or treat experiment evidence as a production contract unless the task explicitly targets an experiment.
