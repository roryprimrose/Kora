---
title: Supplied physical-laptop presence results
description: Provenance, verified comparisons and limits of the original benchmark laptop run.
date: 2026-10-15
---

## Provenance

These four records were copied byte-for-byte from the user-supplied `summary.zip`. Its SHA256 is `6C3519E57D289AF58C27E4F5F6FA5CDAEF56E16986B237A61B2B51D861DA62B2`. The [manifest](manifest.json) identifies benchmark commit `5d16773f2843141f2b51640580ec9b74cb85230a`; these are not reruns of the subsequent DIB verification changes.

The [manifest](manifest.json) reports AMD Ryzen AI MAX 385, eight cores/16 logical processors, Radeon 8050S graphics, and a 2880 x 1800 display. Each [run](runs.json) used a topmost transparent window, input polling, one-second warmup and five measured seconds. There are 36 runs: three passes of twelve active cases, with 180.07 measured seconds in total.

## Reported application CPU

All values below are medians of three runs, in CPU-ms/s. These are process measurements of renderer fixtures, not before/after Kora application measurements or whole-machine energy savings. Source: [summary](summary.csv).

| Scenario | Baseline default backend | Software compositor | Native DIB | Native below baseline | Native below compositor |
|---|---:|---:|---:|---:|---:|
| Default | 256.1 | 112.3 | 40.6 | 84.2% | 63.9% |
| Maximum | 281.1 | 109.4 | 40.6 | 85.6% | 62.9% |
| Reactive, first five seconds | 344.6 | 196.3 | 50.0 | 85.5% | 74.5% |

Maximum-load working-set medians were 108.0 MiB for baseline, 62.7 MiB for compositor, and 41.1 MiB for native DIB. Native median submissions were 29.98/s versus 24.79 baseline draw callbacks/s. The advantage is not explained by a lower counted native frame rate ([summary](summary.csv)).

## Interpretation limits

- Five-second maximum native samples ranged from 34.4 to 84.3 CPU-ms/s. These medians and percentage reductions are indicative, not precision estimates ([summary](summary.csv)).
- Five measured reactive seconds do not cover the entire seven-state sequence, the first seven-second theme change, or a sustained speech-off period. See the [workload timing](../../Program.cs) and [run durations](runs.json).
- The laptop native maximum median approximately matches the [original remote result](../summary.csv); this does not establish universal hardware independence. Baseline/compositor workloads and cadence differed across the two hosts.
- Shared DWM counters cover the full child lifetime, not an isolated matched steady-state interval. Maximum DWM medians were about 230 CPU-ms/s for native, 202 for baseline and 183 for compositor. Do not add them to the five-second process numbers to claim precise whole-machine savings ([summary](summary.csv), [counter scope](runs.json)).
- The original [fidelity records](fidelity.json) label direct-Skia rows `skia/native-dib`, but those rows do not inspect actual DIB memory. The original suite does not prove native pixel-format/layout or post-composition fidelity. The subsequent [DIB repair evidence](../dib-byte-validation/README.md) is separate.
