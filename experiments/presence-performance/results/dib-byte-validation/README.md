---
title: Actual native DIB-byte validation
description: Scope and gates for the shared DIB-backed rendering verification repair.
date: 2026-10-15
---

## Covered path

The [native benchmark](../../NativeBitmapWindow.cs) and [verifier](../../FidelityVerification.cs) now use the same [NativeDibBuffer](../../NativeDibBuffer.cs). It allocates a Windows DC and DIB section, validates the GDI dimensions/stride/format, wraps the DIB pointer with a BGRA premultiplied Skia surface, clears and draws into it, and exposes the same selected DC for `UpdateLayeredWindow`.

Verification copies bytes directly from the `CreateDIBSection` allocation; it does not substitute a Skia snapshot as the DIB reference. Production reference pixels and the independent direct-Skia reference are explicitly converted to premultiplied BGRA for comparison ([verifier](../../FidelityVerification.cs), [byte comparator](../../BgraPixelComparison.cs)).

## Gates and results

The [297 comparison records](fidelity.json) comprise the original 189 raster comparisons and 108 actual-DIB comparisons across density 25/100/200%, size 240/360/600, dot size 50/100/200%, and initial/60-step motion. Only required cached/direct/DIB paths are acceptance gates; atlas and PR-prefix rows remain diagnostic.

- The 54 actual-DIB versus direct-Skia cases require zero differing bytes, and all passed exactly.
- The 54 actual-DIB versus production cases require at most 1% normalized alpha error, at most 1% normalized total BGRA-channel error, and no RGB byte greater than its alpha. Normalization divides total absolute error by total reference coverage/channel values, not the entire image area.
- Observed production-relative maxima were 0.0176% alpha error and 0.0161% BGRA-channel error. At most three pixels differed per case; the largest individual channel difference was eight. Production and direct Skia are therefore not claimed to be byte-identical in every case.

The thresholds and source hashes are in the [manifest](manifest.json); the individual errors are in [fidelity data](fidelity.json).

The [32 passing tests](tests.trx) include odd-width buffers, asymmetric top/bottom sentinel colours checked through both raw memory and GDI coordinate reads, premultiplied alpha, clearing reused buffers, disposal, and release across 100 allocations. Mutation tests detect channel swaps, bottom-up order, straight-alpha bytes, and RGB corruption in transparent pixels ([native
tests](../../../presence-performance-tests/NativeDibBufferTests.cs), [comparison tests](../../../presence-performance-tests/BgraPixelComparisonTests.cs)).

The [five-second native smoke run](native-smoke.json) completed approximately 29.98 successful submissions/s with a non-empty buffer after the extraction. It is a functionality check, not a new comparative performance series.

## Explicitly not covered

These checks stop at the DIB/DC input to presentation. They do not compare pixels after `UpdateLayeredWindow` or validate DWM blending, final visible-window orientation, per-monitor scaling, HDR/colour management, scanout, or physical display latency. GDI coordinate reads verify the selected bitmap's interpretation, not a screenshot of the composed window.

This repair does not change the application, add a helper process, or retroactively expand the fidelity coverage of the [historical remote](../fidelity.json) or [laptop](../laptop-2026-10-15/fidelity.json) datasets.
