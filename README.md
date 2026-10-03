# Kora

A Windows voice-first, local-first extensible assistant.

This repository currently contains the first runnable bootstrap: an Avalonia desktop shell using the constellation design, deterministic built-in command routing, Windows microphone discovery, local Windows speech recognition, dependency readiness probes, and unit tests.

## Requirements

- Windows 10 build 19041 or later; Windows 11 is recommended.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) matching `global.json`.
- An English Windows speech-recognition language.
- A microphone capture device.

No model, cloud account, or network connection is used to recognize built-in commands.

## Run

```powershell
dotnet restore .\Kora.slnx --locked-mode
dotnet run --project .\src\Kora.Desktop\Kora.Desktop.csproj
```

On first launch:

1. Review the readiness results.
2. Select a detected microphone.
3. Choose **Enable listening** to provide explicit capture consent.
4. Say **“Kora, what can you do?”** or another phrase in the built-in catalogue.
5. Observe the transcript, matched action, and constellation state.

The typed command field drives the same router and can prove command behavior when microphone or speech-language support is unavailable.

Kora also keeps a system tray icon available while the process is running. Its context menu provides **Show Kora**, **Detect microphone**, and **Exit**. Detecting microphones refreshes readiness without enabling capture; Exit releases listening before closing the application.

## Implemented voice proof

- Application actions: show, hide, exit, restart, settings, setup, help, version, and status.
- Task controls: cancel task and stop speaking.
- Bundled session skill: lock the current Windows session.
- Protected power requests: recognize, display, inspect, and cancel shutdown/restart proposals.

Shutdown and computer restart execution is intentionally disabled in this bootstrap. Recognition produces a visible non-destructive proposal and never sends an operating-system power request. Locking is a real local action and disables microphone capture before calling Windows.

The initial recognizer uses the installed English Windows speech engine and a fixed host-owned grammar. It is a command proof, not the final wake-word, endpointing, playback-rejection, or transcription implementation described by the design documents.

## Build and test

```powershell
dotnet restore .\Kora.slnx --locked-mode
dotnet build .\Kora.slnx --configuration Release --no-restore
dotnet test .\Kora.slnx --configuration Release --no-build
```

Tests use xUnit and AwesomeAssertions. The GitHub Actions workflow restores locked dependencies, builds and tests on Linux, and cross-publishes framework-dependent Windows x64 and x86 artifacts.

NuGet versions are owned centrally by `Directory.Packages.props`. Central package
management also enables transitive version pinning and rejects project-level
version overrides. After intentionally changing a package version, refresh the
lock files with:

```powershell
dotnet restore .\Kora.slnx --force-evaluate
```

Builds treat compiler and analyzer warnings as errors. The baseline combines
the latest recommended .NET analyzers, enforced code-style diagnostics,
Meziantou.Analyzer, Visual Studio threading analyzers, and the xUnit analyzers
provided by the test framework. Narrow `.editorconfig` exceptions document
framework or test patterns where a rule is not applicable.

The automated test boundary is:

- `Kora.Core.UnitTests` and `Kora.Application.UnitTests` are portable unit tests. Linux CI collects coverage for `Kora.Core` and `Kora.Application`, requires 100% line and branch coverage, uploads the Cobertura report, and posts the Markdown summary to pull requests.
- `Kora.Windows.IntegrationTests` runs on a GitHub-hosted Windows runner. It covers native dependency probing, safe microphone enumeration/error boundaries, and the audio stream adapter without opening a real capture session or invoking session lock.
- Physical microphone capture, recognition quality, device removal during capture, Windows session-lock notification, and real lock behavior require a controlled Windows machine and remain manual/end-to-end acceptance evidence. CI must never lock or restart its runner.

## Project layout

- `src/Kora.Core` — portable command, dependency, voice, and platform contracts.
- `src/Kora.Application` — portable application orchestration and view models.
- `src/Kora.Windows` — Windows microphone, speech-recognition, readiness, and session integrations.
- `src/Kora.Desktop` — Avalonia composition root and constellation interface.
- `tests/Kora.Core.UnitTests` and `tests/Kora.Application.UnitTests` — portable unit tests and CI coverage.
- `tests/Kora.Windows.IntegrationTests` — non-destructive Windows integration tests.
- `Design` — product, architecture, safety, and interaction specifications.
