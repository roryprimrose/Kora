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
