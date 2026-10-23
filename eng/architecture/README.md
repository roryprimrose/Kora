# Maintained architecture guard

The policy protects the existing
[platform boundary contract](../../Design/Architecture.md#platform-boundaries-and-support),
not a new platform or feature. Run from the repository root after the ordinary
locked solution restore and Release build:

```powershell
pwsh -NoProfile -File .\eng\Test-Architecture.ps1
```

CI runs this beside the scheduling-contract checks. No devices, native calls,
providers, application settings or qualification evidence are used.

## Evaluated dependencies: KORAARCH001

`Architecture.targets` is the executable dependency allow-list. It admits only
the canonical production edges: Core has none; Tools/Definitions -> Core;
Application -> Core/Tools; Windows -> Core; desktop -> Core/Definitions/
Application/Windows. Unclassified production projects and platform-targeted
portable projects fail closed.

The runner discovers production projects and asks MSBuild for their declared
configurations, platforms and runtime identifiers. It then evaluates
each combination, including no RID, for both `Windows_NT` and `Unix` conditions.
The target framework is resolved within each profile, not pinned from the
default evaluation; multi-target projects also evaluate every inner framework.
MSBuild, not XML text or a namespace search, resolves imports, conditional
references, item removals and normalized reference paths. This is a finite
supported-build matrix, not a claim to solve arbitrary custom MSBuild property
conditions. New supported build dimensions must extend this matrix.

## Semantic API boundary: KORAARCH002

The focused compiler analyzer uses the Roslyn assemblies from the pinned .NET
SDK; it adds no dependency-analysis package. The runner recompiles the portable
layers in Debug and Release with this analyzer, including generated C#. It
rejects resolved Windows-platform API requirements, Windows integration/API
namespaces and native interop/COM imports, handles, loading and marshalling
mechanisms. Aliases, static imports, runtime OS guards and diagnostic
suppression do not grant an architectural exception. These APIs belong behind
the Windows seam even when a platform analyzer would permit a guarded call.

Windows-named domain interfaces, string literals, OS capability predicates and
portable `StructLayout` data are allowed. Catching an adapter's managed
`Win32Exception` does not call an OS mechanism and remains allowed; constructing
one in a portable layer is rejected. This preserves the existing error
transport without moving exception creation/native error access into portable
services.

Real compiler fixtures also prove that the MSBuild injection loads the analyzer
and rejects forbidden calls, even with a guarded call or project-level analyzer
disablement. The maintained runner forces analyzer execution and recompilation.

The check is deliberately bounded, not a general whole-source rule engine or a
security sandbox. It does not inspect third-party implementation IL, infer
reflection targets or qualify native/provider behavior. Conditional source is
compiled for the ordinary Debug/Release profiles; RID/host-specific project
edges are additionally evaluated by the graph check.

## Regression fixtures

`tests/Kora.Architecture.Tests` uses xUnit v3 and AwesomeAssertions. Semantic
fixtures must first compile successfully, then assert exact architecture errors
or an empty diagnostic set. They include aliases, static calls, guarded calls,
native imports, generated source, suppression, portable layout and Windows-
named contracts. No forbidden operation is executed.

MSBuild fixtures use temporary projects with imported conditional references
(Release, Windows RID and Windows host), allowed normalized paths, forbidden
layer directions, unknown layers and Windows target frameworks. A failed
fixture must contain the architecture diagnostic, not merely a process error.
Temporary projects are removed and child processes have bounded lifetimes.

For a focused fixture rerun after building:

```powershell
dotnet test --project .\tests\Kora.Architecture.Tests\Kora.Architecture.Tests.csproj --configuration Release --no-build
```

The engineering fixture project is intentionally outside the four production
coverage modules; the normal portable coverage selection remains unchanged.
