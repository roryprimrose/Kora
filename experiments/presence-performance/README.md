---
title: Presence performance evaluation
description: Automated, presence-only Windows renderer comparison and reproduction instructions.
date: 2026-10-15
---

## Outcome

Read the [evaluation report](REPORT.md), [topmost summary](results/summary.csv), and [raw topmost measurements](results/runs.json). The reusable native bitmap renderer was the best measured CPU/memory prototype on this remote host; this is not a production renderer change or physical-GPU qualification.

## Scope and safety

This opt-in executable creates synthetic transparent windows, links the unchanged production presence control and animation rules, and never starts Kora's application composition, microphone, model, synthesis provider, preferences, or session/approval services. Speech levels and state changes are synthetic. Images contain only the fixture's own graphics, not desktop captures. See [the entry
point](Program.cs), [native prototype](NativeBitmapWindow.cs), and [pixel verification](FidelityVerification.cs).

The benchmark links production files instead of maintaining an edited baseline. The [PR #247 snapshot](snapshots/PresenceControl.cs) is pinned to `e5e46148d9dec9bc6a73266cbffc3f6e639748a3`, with its namespace changed to permit side-by-side compilation. That snapshot is an experimental input, not production source.

Every measured case runs in a fresh child process, with a warmup period, explicit completion/error outcomes, and a bounded lifetime watchdog. The runner only terminates its own timed-out child tree. It refuses to overwrite existing per-case measurements. See [the runner](Invoke-PresenceBenchmark.ps1).

## Build and validate

Run from the repository root. Apply any machine-local feed routing through command-line source overrides; do not change repository feed configuration for this experiment.

```powershell
dotnet restore .\experiments\presence-performance-tests\Presence.Performance.Tests.csproj --locked-mode
dotnet build .\experiments\presence-performance-tests\Presence.Performance.Tests.csproj --configuration Release --no-restore
dotnet test --project .\experiments\presence-performance-tests\Presence.Performance.Tests.csproj --configuration Release --no-build -- --report-trx --results-directory .\artifacts\presence-tests
```

The [tests](../presence-performance-tests/ParticleSceneTests.cs) cover particle selection, cache bounds, settling, speech spread versus dot radius, and seamless trajectory positions. The separate native-window verification compares cached/direct/atlas renderings against production pixels across density, size, dot-size and motion-frame cases.

## Reproduce the final topmost comparison

The defaults preserve topmost, transparent, non-activating windows and native input polling. No interactive approval or device access is needed by this synthetic fixture. See [window configuration](Program.cs) and [native window styles](NativeBitmapWindow.cs).

```powershell
$output = Join-Path $env:TEMP ('KoraPresence-' + [Guid]::NewGuid().ToString('N'))
$runner = '.\experiments\presence-performance\Invoke-PresenceBenchmark.ps1'

& $runner -OutputDirectory $output -Suite Qualifying -Repetitions 3 -Seconds 15 -WarmupSeconds 3
& $runner -OutputDirectory $output -Suite TopmostLifecycle -Repetitions 1 -Seconds 15 -WarmupSeconds 3
& $runner -OutputDirectory $output -Suite Soak -Repetitions 1 -Seconds 180 -WarmupSeconds 3

& .\experiments\presence-performance\bin\Release\net10.0-windows10.0.19041.0\Presence.Performance.exe --variant verify --output "$output\fidelity.json"

.\experiments\presence-performance\Export-PresenceResults.ps1 -InputDirectory $output -OutputDirectory .\artifacts\presence-summary -ExpectedRuns 46
```

`Qualifying` is only a suite name, not a product enablement/RC qualification decision. These commands produce 36 active, eight lifecycle, and two soak runs. The [exporter](Export-PresenceResults.ps1) checks run counts, measured durations, native submission cadence, and forced-render cadence before producing summaries.

## Additional experiments

Use `Active` for the wider baseline/cached/Skia/compositor/native comparison, `Lifecycle` for three-pass lifecycle comparisons including PR #247, and `Offscreen` for simulation-cache/bitmap-cache diagnostics. `Offscreen` forces 30 additional raster draws per second while the ordinary window remains active; it is not a single-output production CPU benchmark. See [the suite
definitions](Invoke-PresenceBenchmark.ps1).

An individual run accepts `--variant`, `--scenario`, `--backend`, `--seconds`, `--warmup`, `--output`, `--input`, `--layered`, `--offscreen`, and `--topmost`; names/ranges are validated in [the options parser](Program.cs). Native DIB rendering is always raster regardless of the Avalonia backend option. Composition custom visuals cannot be measured through `RenderTargetBitmap` and reject
offscreen mode.

The initial non-topmost stage and screenings are retained under [non-topmost results](results/non-topmost/manifest.json); they must not be combined with the final topmost series as if they were the same experiment.
