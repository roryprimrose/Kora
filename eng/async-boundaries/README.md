# Async-void engineering check

`VSTHRD100` is enabled for all C# and promoted to a build error by the existing
warnings-as-errors policy. Ordinary application services return `Task`; they do
not inherit an exception for UI code. An unavoidable framework event or
`ICommand.Execute` adapter may use a **method-local** `SuppressMessage` with the
specific void contract and its explicit error route in the justification.
Do not suppress the rule for a project, directory, type, or all C# files.

From the repository root, after the normal locked restore, run:

```powershell
pwsh -NoProfile -File .\eng\Test-AsyncVoidAnalysis.ps1
dotnet build .\tests\Kora.Application.UnitTests\Kora.Application.UnitTests.csproj --configuration Release --no-restore
dotnet .\tests\Kora.Application.UnitTests\bin\Release\net10.0\Kora.Application.UnitTests.dll -method '*AsyncCommandTests.*'
```

The check first rebuilds the real desktop and application event/command adapters.
It then imports [Probe.targets](Probe.targets) for one deliberately failing
application compilation, using the real project, repository `.editorconfig`,
warnings-as-errors policy and already restored threading analyzer. The target
adds [UnjustifiedAsyncVoidService.cs](UnjustifiedAsyncVoidService.cs) only when
explicitly requested; the fixture is not included in normal production builds.
The check requires a nonzero exit and `error VSTHRD100` at that fixture, rejects
unrelated compiler errors, and rebuilds the production application without the
probe. Restoring a blanket suppression makes this check fail.

No extra analyzer package, dependency manifest, native device, provider,
installation, or qualification run is required. Normal builds enforce the rule
for every newly compiled service. The command tests cover synchronous and
deferred faults/cancellation through the actual `ICommand` adapter, including
availability recovery. Application event tests cover transcript dispatch and
call-policy failure routing; headless desktop checks cover the native error
adapters without invoking hardware.
