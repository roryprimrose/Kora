---
title: Kora presence performance evaluation
description: Automated remote-host benchmarks, fidelity checks, resource accounting and ranked renderer options.
date: 2026-10-15
---

## Decision

**The best measured prototype on this host is a reusable raster buffer presented through a small native layered window, still using C# and the existing animation rules.** At maximum settings it used a median **40.6 CPU-ms/s**, **41.2 MiB working set**, and approximately **30 successful submissions/s**. The best retained-Avalonia candidate at comparable cadence used **73.9 CPU-ms/s** and **64.6
MiB**. These are renderer-fixture results, not whole-Kora savings ([topmost summary](results/summary.csv)).

Recommendation: change the rendering/presentation path before changing language, adding another application process, or baking full-colour videos. Keep scalar colour, visibility and speech-envelope calculations live. Preserve a simple cached-Avalonia option as the lower-integration-cost alternative. This recommendation is based on [the measured comparisons](results/summary.csv) and [fidelity
checks](results/fidelity.json); no production renderer was enabled.

## Evidence and scope

The final series contains **46 topmost runs**: three 15-second passes of twelve active cases, eight 15-second lifecycle checks, and two 180-second reactive soaks. Every case had three seconds of warmup. Actual measured time was **1,020.4 seconds**, excluding **138 seconds** of warmup. Earlier exploration contains another **101 timed runs** plus **32 short screenings**; five subsequent topmost
probes are retained separately ([final manifest](results/manifest.json), [exploration manifest](results/non-topmost/manifest.json), [screenings](results/non-topmost/screening.json), [topmost probes](results/topmost-screening.json)).

The host was a Windows VM reporting AMD EPYC 7763, eight cores/16 logical processors, .NET 10.0.13, and Hyper-V/Remote Display adapters. The fixture observed DPI scaling 1.0. This is not physical-GPU, battery, or energy evidence ([machine metadata](results/manifest.json), [per-run backend observations](results/runs.json)).

The baseline is the unchanged control from repository commit `df75e4b1f1be51c0990a3b4f61e69c022a8c57cf`. The benchmark links it and the production animation/settings/input rules directly. It does not start Kora's full application or execute its real speech-playback/caption callback. Consequently, this evaluation cannot establish the cause of another session's full-app, one-core observation
([source hashes](results/manifest.json), [fixture entry point](Program.cs), [production frame subscriber](../../src/Kora/MainWindow.axaml.cs)).

### Workloads and metrics

| Workload | Configuration |
|---|---|
| Default | 360-pixel footprint, 150 particles, 100% dot size and movement, Calculating, no speech |
| Maximum | 600-pixel footprint, 300 particles, 200% dot size and movement, Calculating, no speech |
| Reactive | Maximum geometry; all seven visible states, synthetic speech on/off and varying envelope, light/dark theme changes |
| Static | Failure state after transition settling |
| Hidden | Hide after warmup; production baseline and PR/candidate lifecycle behavior measured separately |
| Soak | Three minutes of the reactive sequence |

Configurations are defined in [the fixture](Program.cs), [native equivalent](NativeBitmapWindow.cs), and [suite runner](Invoke-PresenceBenchmark.ps1). The native prototype's motion is elapsed-time-based, as is the compositor candidate; cached UI-control candidates preserve the original per-tick motion unless explicitly labelled adaptive. Pixel-equivalence tests compare the same simulation steps,
not a claim of identical wall-clock trajectories at different frame rates.

CPU is process CPU milliseconds per elapsed second: **1,000 CPU-ms/s means one saturated logical core**. Allocations are process-wide managed allocated bytes, not total native allocation traffic. Working set includes each standalone fixture process, not incremental memory inside the existing Kora process. Draw counters report render callbacks or successful native submissions, **not physical
display presentation** ([measurement implementation](Program.cs), [native implementation](NativeBitmapWindow.cs), [render probe](RenderProbe.cs)).

## Corrected topmost results

All rows below use the corrected topmost fixture. Values are medians of three active runs; the full min/max range is shown for maximum-workload CPU. Source: [summary CSV](results/summary.csv), with complete counter records in [runs JSON](results/runs.json).

| Renderer | Default CPU-ms/s | Maximum CPU-ms/s [min, max] | Reactive CPU-ms/s | Maximum draws/s | Maximum working set |
|---|---:|---:|---:|---:|---:|
| Unchanged Avalonia, default backend | 85.4 | 136.4 [94.7, 147.8] | 108.3 | 3.93 | 101.7 MiB |
| Unchanged Avalonia, forced software | 56.2 | 83.3 [70.8, 94.7] | 103.1 | 23.32 | 67.9 MiB |
| Composition custom visual, software circles | 38.5 | 73.9 [68.7, 87.5] | 77.1 | 30.33 | 64.6 MiB |
| Native reusable DIB, software circles | 21.9 | 40.6 [33.3, 49.9] | 32.3 | 30.04 | 41.2 MiB |

The native maximum-workload median is about **45% below the software compositor's application CPU** at comparable 30-frame cadence. Relative to the unchanged default-backend fixture it is about **70% lower**, but that latter comparison is not equal-cadence: the default backend delivered only about four draw callbacks/s on this remote host ([summary CSV](results/summary.csv)).

At maximum settings, managed allocation medians were approximately 104 KiB/s for the default-backend baseline, 407 KiB/s for software baseline, 79 KiB/s for the software compositor, and 0.83 KiB/s for native DIB. Software baseline reactive redraws exceeded its animation updates because bound speech/state properties also invalidate rendering; counting animation ticks alone would have concealed
this work ([raw counters](results/runs.json), [baseline invalidation registration](../../src/Kora/Controls/PresenceControl.cs), [summary CSV](results/summary.csv)).

### Remote-window limitation and methodology correction

The initial fixture omitted production's topmost setting. That was identified, preserved as a separate exploratory dataset, corrected, and followed by all final comparisons above. Enabling topmost did **not** restore default-backend cadence in the short probes or final series ([production window](../../src/Kora/MainWindow.axaml), [topmost probes](results/topmost-screening.json), [final
results](results/runs.json)).

An actual Skia GPU context was observed in the default-backend draw probe. That does not identify a physical adapter, prove GPU execution efficiency, or diagnose why cadence was limited. **Hypothesis, unconfirmed:** this remote display/composition path limits redraw cadence. Neither a blanket "software fallback caused it" diagnosis nor a physical-GPU performance conclusion is justified by these
measurements ([backend observations](results/runs.json), [probe implementation](RenderProbe.cs)).

## Physical-laptop corroboration

The user supplied a separate 36-run series from benchmark commit `5d16773f2843141f2b51640580ec9b74cb85230a`. It reports Ryzen AI MAX 385/Radeon 8050S hardware, three repetitions, five measured seconds and one warmup second per case. It is preserved unchanged, not pooled with the VM measurements ([provenance](results/laptop-2026-10-15/README.md),
[manifest](results/laptop-2026-10-15/manifest.json)).

| Laptop scenario | Baseline CPU-ms/s | Compositor CPU-ms/s | Native DIB CPU-ms/s | Native below baseline | Native below compositor |
|---|---:|---:|---:|---:|---:|
| Default | 256.1 | 112.3 | 40.6 | 84.2% | 63.9% |
| Maximum | 281.1 | 109.4 | 40.6 | 85.6% | 62.9% |
| Reactive, first five seconds | 344.6 | 196.3 | 50.0 | 85.5% | 74.5% |

The native maximum median reproduces the remote result approximately, but its three laptop samples range from 34.4 to 84.3 CPU-ms/s. Native submissions were about 30/s versus baseline draws about 24.8/s. Maximum working sets were 41.1 MiB native, 62.7 MiB compositor and 108.0 MiB baseline ([laptop summary](results/laptop-2026-10-15/summary.csv)).

Five-second reactive samples do not cover the complete seven-state/theme cycle or a sustained speech-off period. Shared DWM intervals include startup and warmup and are not exclusively attributable. These results corroborate the candidate ranking, not exact whole-machine savings or integrated application performance ([limits](results/laptop-2026-10-15/README.md), [timing](Program.cs)).

## Which ideas were tested

| Option or combination | Outcome | Evidence |
|---|---|---|
| Cache brushes, selected-particle depth order and drawing inputs | Strong allocation reduction, but CPU savings inconsistent; default exploratory CPU was not better than baseline software. Useful low-risk cleanup, not a proven drastic fix alone. | [exploration summary](results/non-topmost/summary.csv), [cached implementation](ParticleScene.cs) |
| Direct Skia recording instead of hundreds of retained ellipse operations | Preserved reference pixels and reduced CPU in several exploratory workloads, but still incurred UI-frame recording and allocation. | [exploration summary](results/non-topmost/summary.csv), [pixel results](results/fidelity.json), [implementation](CandidateControl.cs) |
| Lower motion frequency to roughly 15 requested fps | Lost cadence without a decisive CPU improvement in screening. Not selected as the main strategy; speech responsiveness must not be sacrificed. | [screenings](results/non-topmost/screening.json) |
| Precompute compact periodic trajectories, retain live colour and spread | A 300-particle, 600-sample cache used 1,440,000 bytes. It did not decisively outperform direct simulation and changed the motion model. | [cache diagnostics](results/non-topmost/summary.csv), [trajectory implementation](ParticleScene.cs) |
| Pre-render 120 raster frames, tint and scale | Maximum: 172,800,000 cache bytes, 234.3 MiB working set, slower than live drawing. Whole-bitmap speech scaling changes dot radii. Rejected. | [data](results/non-topmost/summary.csv), [code](CandidateControl.cs), [radius tests](../presence-performance-tests/ParticleSceneTests.cs) |
| Batch dots through a single sprite atlas | Looked promising on CPU, but failed fidelity: up to 72.3% normalized alpha error for small dots. Faster inaccurate images are not accepted. | [pixel results](results/fidelity.json), [screenings](results/non-topmost/screening.json) |
| Compositor-thread immediate drawing with cached resources | Good in-stack option. Replacing reset-on-each-frame scheduling with an accumulated deadline restored roughly 30 software updates/s in short comparisons. | [clock screenings](results/non-topmost/screening.json), [scheduler](CompositionControl.cs), [topmost summary](results/summary.csv) |
| Minimal native HWND plus a reusable BGRA DIB/Skia surface | Best measured application CPU, standalone memory and managed allocations on this host, while retaining reference circle drawing and independent speech spread. | [topmost summary](results/summary.csv), [native implementation](NativeBitmapWindow.cs), [pixel verification](results/fidelity.json) |
| Default versus forced-software graphics | Default-backend GPU-context availability did not imply good cadence on this VM. Software was materially more useful here. This does not justify changing the entire production app's backend without evaluating its other windows. | [topmost summary](results/summary.csv), [backend setup](Program.cs) |
| Remove layered style/input setup | An exploratory probe did not restore default-backend cadence. It also disabled fixture input polling, so it cannot isolate the causal effect of the style alone. | [screenings](results/non-topmost/screening.json), [fixture setup](Program.cs) |
| Separate process or different language | Every fixture already ran in a fresh process. The measured native improvement came from its renderer/presentation path, not evidence that process isolation or C++/Rust itself saves CPU. IPC and incremental full-app memory were not benchmarked. | [runner](Invoke-PresenceBenchmark.ps1), [native path](NativeBitmapWindow.cs) |

The repeated cache diagnostics forced **30 additional offscreen raster draws/s while ordinary window rendering remained active**. They compare caching choices under that dual-output workload; their CPU values must not be presented as an isolated, single-output 30-fps production renderer. Full raster replay used 162.2 CPU-ms/s versus 130.2 for baseline and 99.9 for cached live ellipses in that
diagnostic ([exploration summary](results/non-topmost/summary.csv), [forced drawing implementation](Program.cs)).

## Hidden, static and longer-run behavior

The topmost lifecycle checks are one 15-second run per case, not three-sample performance estimates. Earlier non-topmost lifecycle checks contain three repetitions. An isolated final render callback around hide is distinguished from recurring animation updates ([topmost lifecycle rows](results/summary.csv), [exploration lifecycle rows](results/non-topmost/summary.csv)).

| Renderer | Hidden CPU-ms/s | Hidden animation updates/s | Static CPU-ms/s | Static animation updates/s |
|---|---:|---:|---:|---:|
| Original baseline | 22.9 | 21.31 | 12.5 | 21.39 |
| PR #247 renderer/lifecycle snapshot | 11.5 | 0 | 16.7 | 21.46 |
| Software compositor candidate | 7.3 | 0 | 11.4 | 0 |
| Native DIB candidate | 5.2 | 0 | 6.2 | 0 |

Both candidates still have process/input/watchdog overhead while quiescent; zero animation updates is not zero process CPU. Native DIB still has a timer and does not yet suppress every redundant frame in a mixed reactive sequence. Production hidden/settled scheduling improvements remain worthwhile independently of a renderer change ([native scheduler](NativeBitmapWindow.cs), [composition
scheduler](CompositionControl.cs), [measurements](results/runs.json)).

In the corrected three-minute reactive soaks, native DIB used **40.1 CPU-ms/s**, submitted **30.00 frames/s**, and working set changed **41.55 to 41.48 MiB**. GDI objects stayed **2 to 2**, USER objects **5 to 5**, and no GC collections occurred. The compositor used **76.9 CPU-ms/s**, drew **25.53 frames/s**, and working set changed **64.19 to 74.78 MiB**, with one Gen0 collection. That
working-set increase is not, by itself, proof of a leak ([soak counter records](results/runs.json)).

The reactive compositor deliberately stops updates when a state settles, so lower average draw counts in a mixed-state soak must not all be labelled dropped frames. The fixture does not separately attribute every missing presentation, deadline miss, or static interval. Its short active cadence and long-soak cadence are reported independently ([scheduler](CompositionControl.cs), [soak and active
counters](results/runs.json)).

### Machine-resource accounting

Shared-session DWM counters were collected around each full child lifetime, including startup and warmup. In the topmost soaks they showed **22.9 CPU-ms/s for the native run versus 13.0 for the compositor run**. Thus the application-only CPU benefit is partly offset by higher observed compositor cost. These windows are not matched steady-state intervals or exclusive attribution, so summing them
into a precise whole-machine savings percentage would be misleading ([shared DWM records](results/runs.json), [counter collection](Invoke-PresenceBenchmark.ps1)).

The native path also transfers a full premultiplied buffer: payload arithmetic is 600 x 600 x 4 x 30 = **43.2 MB/s** at maximum settings. This is a nominal pixel payload, not a measured memory-bus/GPU-transfer counter. Energy, physical GPU utilization, remote-display transport, and the benchmark observer's resource use were not measured as production costs ([DIB/presentation
code](NativeBitmapWindow.cs), [host metadata](results/manifest.json)).

## Fidelity and PR #247

There were **189 deterministic image comparisons**: 54 cached-versus-production, 54 direct-Skia-versus-production, 54 atlas comparisons, and 27 PR-snapshot comparisons. Required cached and direct-Skia paths passed across density 25/100/200%, footprint 240/360/600, dot size 50/100/200%, and initial/60-step motion. Pure tests additionally verify that speech changes particle spread without changing
dot radius ([fidelity data](results/fidelity.json), [verifier](FidelityVerification.cs), [13-test TRX](results/tests.trx)).

**Coverage correction:** the original "native-window verification" description was too broad. The 189 historical comparisons use an independent in-memory Skia raster; rows labelled `skia/native-dib` do not test actual DIB bytes. The original native snapshot only proves a non-empty buffer, and successful native calls do not prove the final image ([historical data](results/fidelity.json), [native
path](NativeBitmapWindow.cs)).

The repaired verifier adds **108 actual-DIB comparisons**, using the same extracted operation as the native benchmark: 54 against production output at explicit normalized colour/alpha limits, and 54 byte-for-byte against direct Skia. The actual DIB allocation, BGRA premultiplication, row layout and clearing are now covered; **post-`UpdateLayeredWindow`/DWM display fidelity is not** ([repair
evidence](results/dib-byte-validation/README.md)).

The [PR #247](https://github.com/roryprimrose/Kora/pull/247) renderer was pinned at `e5e46148d9dec9bc6a73266cbffc3f6e639748a3`. Its sorting change selects a different seeded subset below maximum density: it sorts all particles before taking the requested count. The original takes the seeded prefix and then sorts it. The image comparisons detect that difference; the cached candidate preserves the
original selection order. This observation was sent to the other session; this evaluation does not change that PR ([snapshot](snapshots/PresenceControl.cs), [original](../../src/Kora/Controls/PresenceControl.cs), [pixel results](results/fidelity.json)).

PR #247 also contains app-wide responsiveness work outside the renderer. Its full app, branch integration, and dispatcher/evidence/privacy feedback changes were not exercised by this presence-only benchmark; the renderer snapshot/lifecycle numbers are not a verdict on the whole PR ([PR scope](https://github.com/roryprimrose/Kora/pull/247), [fixture scope](Program.cs)).

## Unmeasured ideas and adoption boundary

Two additional designs remain unranked, not performance claims:

- Retained Windows composition visuals/keyframes could remove recurring pixel-buffer submission. That is a candidate for physical-GPU/power evaluation, not a demonstrated improvement over this host's DIB result.
- Instanced analytic circle rendering or a coverage-correct atlas could batch particles without the tested atlas's minification error. A replacement must pass the same pixel/spread checks before its CPU numbers count.

The prototypes are **not production-ready window hosts**. In particular, the native experiment does not provide production Ctrl-drag placement, live footprint/density rebuilding, per-monitor DPI changes, production privacy/ownership integration, or complete hide/reveal lifecycle integration. Preserve those contracts rather than transplanting this experiment wholesale. A helper process would
additionally require host-owned lifecycle, fail-closed disconnect behavior, bounded state messages and measured IPC overhead ([native prototype](NativeBitmapWindow.cs), [production window](../../src/Kora/MainWindow.axaml.cs), [production visibility rules](../../Design/Ambient_UI.md)).

For a bounded production increment, retain one authoritative animation/settings model, cache stable resources, stop hidden/static recurring work, and evaluate a platform-owned reusable-buffer presenter against the retained-Avalonia implementation. Do not precompile combinatorial colour/theme/speech variants. These measurements do not authorize changing the default renderer.

The branch now includes a separately gated production integration, described in [Ambient UI](../../Design/Ambient_UI.md#opt-in-native-presence-renderer). It is not the standalone prototype measured here. The historical baseline is frozen in [the original snapshot](snapshots/baseline/PresenceControl.cs), so consuming #250 cannot silently redefine the committed comparison.

## Validation and reproducibility

Locked restore succeeded for the benchmark/test graph. The final Release build completed with zero warnings/errors, the targeted xUnit run passed all 13 tests, the final pixel verifier completed 189 comparisons with the required exact paths passing, and exports validated 46 corrected runs plus 101 separately labelled exploratory runs ([test TRX](results/tests.trx), [fidelity
data](results/fidelity.json), [final manifest](results/manifest.json), [exploration manifest](results/non-topmost/manifest.json)).

Build evidence from the executed command:

```text
dotnet build .\experiments\presence-performance-tests\Presence.Performance.Tests.csproj --configuration Release --no-restore
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

Use [the reproduction instructions](README.md) for the same final workloads, watchdog, pixel checks and export gates. Source hashes and actual elapsed denominators are retained with the data. No full-product test-suite, physical-display presentation, real speech-device, or production enablement claim is made.

The subsequent DIB repair passed **32 tests** and **297 raster/buffer comparisons** for required paths; diagnostic atlas rows remain intentionally unqualified. A native smoke run exercised the extracted buffer through successful layered-window submissions. This adds verification evidence only; it does not revise historical CPU results or change production code ([repair
manifest](results/dib-byte-validation/manifest.json), [test evidence](results/dib-byte-validation/tests.trx), [scope](results/dib-byte-validation/README.md)).
