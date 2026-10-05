# Measured Windows snapshot

Latest Windows-only proof run after checking upstream freshness: 2026-10-05,
Windows 10.0.26300,
x64, .NET runtime 10.0.12, SDK 10.0.401, 16 logical processors.
The shared development machine was not isolated or idle.

**150 automated assertions passed, 0 failed, 1 actual cross-user gate blocked.**
The run intentionally returns code 2 for that blocked gate; the documented
script was run with `-AllowBlockedCrossUser`. Two Windows RID publications passed.
See [raw assertions/measurements](evidence/windows-run.json) and
[native sizes, hashes and architectures](evidence/native-assets.json).

## Small retained-event workload

Per candidate: 2,000 inserts in one FULL-synchronous WAL transaction, then 100
individual autocommit inserts, 2,000 verified point reads and one indexed count
query. Payload: 4,133 synthetic bytes per record. Pooling is disabled and
automatic WAL checkpointing is disabled to permit observation.

| Metric | AES-GCM + equality token | SQLCipher + FTS5 |
|---|---:|---:|
| Initial open/schema, ms | 6.79 | 479.33 |
| 2,000-record transaction, ms | 46.04 | 268.10 |
| Batched records/second | 43,440 | 7,460 |
| Individual write p50 / p95, ms | 0.503 / 0.671 | 1.639 / 2.266 |
| Point read p50 / p95, ms | 0.012 / 0.017 | 0.043 / 0.082 |
| Exact fixture search/count, ms | 0.234 | 0.537 |
| Live WAL bytes | 11,185,832 | 25,164,992 |

Both databases have only 4,096 bytes in the main file at this observation:
most committed work is still in WAL. That is **not** a compact final database
size. SQLCipher additionally stores tokenized content in FTS; the candidates
do not provide equivalent search functionality. The equality benchmark uses
one deliberately repetitive term and measures lookup, not realistic ranking.

These are warm, single-process feasibility timings, not product performance
acceptance thresholds. Open includes a password KDF for SQLCipher; per-record
transactions include content and FTS writes on that path. Device I/O contention,
background activity, cold caches, multiprocess contention, heterogeneous
content, different retained-history sizes and a maintained engine can change
the results. No statistical significance or reference CPU-floor claim is made.
Large artifacts/embeddings, memory pressure, key rotation throughput and
long-history search are not benchmarked by the 4 KiB artifact fixture.

## Actual failure observations

- Four database kill points and three artifact publication kill points per
  candidate: **14 actual terminated child processes**, not exception-only
  crash simulations. Committed records survived; uncommitted records did not;
  every recovered database passed `integrity_check`.
- DDL/version/content migration rollback preserved v1, 40 original records and
  original values. Wrong-key and future-version refusals preserved file hashes.
  A failed encrypted copy-conversion preserved the synthetic legacy file hash.
- SQLite capacity limits caused actual errors while preserving baseline records.
  This is not a hardware filesystem/disk-full test.
- A real plaintext FTS positive control leaked the fixture into WAL.
  Encrypted WAL/journals, managed backups, staging and proof logs did not show
  the UTF-8/UTF-16 fixture in the observed snapshots.
- Live SHM regions were explicitly excluded after Windows byte-lock reads
  failed. Memory-only temporary storage was asserted and used; no claim is made
  about observing every deleted-open temporary handle, OS dump or paging file.
- SQLCipher rekey changed the live database key but left the prior backup
  readable with its old key. Logical deletion/unlink left independent backup
  contents recoverable. DPAPI wrapper deletion did not revoke a copied wrapper.

Earlier experiment failures led to scoped corrections, not production edits:
requesting Windows ownership change was unnecessary and required rights not
available in the ordinary session; use owner-created directory DACL changes
instead. Live WAL reads need compatible share flags; live SHM has byte locks.
SQLite PRAGMA does not accept key parameters, so the proof uses the provider's
supported `Password` builder option. The final snapshot is from the corrected
implementation.

## What remains unmeasured

No approved different-user trial was performed. No mock identities were used.
The handoff CLI was separately exercised with real owner DPAPI roundtrip:
the exact same-SID refusal (`InvalidOperationException`) and a deliberately
wrong owner-confirmed digest refusal (`InvalidDataException`) were verified.
Those negative CLI checks are **not** different-user protection evidence.
Linux runtime support and Linux-host cross-build validation are outside the
owner-confirmed Windows-only scope, not readiness blockers.
Windows x86 assets were published but not executed; clean installed
native loading, permissions and runtime prerequisites still require trials.
No conclusions about production persistence, grants, source revocation,
retention schedulers or final session/task schemas follow from these counts.
