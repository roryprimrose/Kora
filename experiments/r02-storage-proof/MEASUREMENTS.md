# Measured Windows Snapshots

## Unattended Rerun - 2026-10-09

**152 automated assertions passed, zero failed or blocked; runner exit 0.**
The unchanged harness performed **14 actual owned-child kill/recovery trials**
(four database and three artifact boundaries per candidate), followed by
successful framework-dependent win-x64 and win-x86 publications with native
PE architecture checks. This is a rerun of the historical synthetic comparison,
not production admission or a revived encryption prerequisite.

New, separate receipts:
[assertions/measurements](evidence/unattended-20261009-run.json) and
[native publication](evidence/unattended-20261009-native-assets.json).
All four original/historical receipts remain byte-for-byte unchanged.
The native report's unchanged `latest.json` reference denotes this rerun's
assertion receipt; it is not a reference to the older historical snapshots.

### Provenance and commands

- Measured revision: `e0692f438a058de0a20021b3420a981849706dbf` from `origin/main`.
- Branch at execution: `proof/unattended-20261009-storage`.
- Experiment tree at execution: `652eb7a36840399111cf5bba52b9262d4eb09a31`.
- `git status --porcelain=v1 --untracked-files=all` was empty before and after
  execution. Ignored build outputs and generated evidence are not source edits.
  Receipt/documentation deliverables were added **after** the measurement;
  their delivery commit is not the measured source revision.
- SDK `10.0.401`; PowerShell `7.6.6`; runtime `.NET 10.0.12`;
  Windows `10.0.26300`, X64, 16 logical processors.
- Assertion receipt timestamp: `2026-10-09T11:02:33.8624507+00:00`;
  native receipt timestamp: `2026-10-09T11:02:39.975827+00:00`.

Commands below are relative to the isolated repository root. Absolute worktree
and raw-output destinations are deliberately omitted from committed receipts.

```powershell
dotnet --version
$PSVersionTable.PSVersion.ToString()
dotnet build .\experiments\r02-storage-proof\StorageProof.csproj -c Release --no-restore
pwsh -NoLogo -NoProfile -NonInteractive -File .\experiments\r02-storage-proof\Run-Proof.ps1 -PublishAssets
```

The initial no-restore build exited **1** with `NETSDK1004` because the isolated
worktree had no `obj\project.assets.json` (0 warnings, 1 error). Only after that
missing-dependency failure was the existing runner invoked. It performed:

```powershell
# From experiments\r02-storage-proof, as implemented by Run-Proof.ps1:
dotnet restore StorageProof.csproj --locked-mode
dotnet build StorageProof.csproj -c Release --no-restore
dotnet run --project StorageProof.csproj -c Release --no-build
dotnet publish StorageProof.csproj -c Release -r win-x64 --self-contained false --no-restore
dotnet publish StorageProof.csproj -c Release -r win-x86 --self-contained false --no-restore
```

All five runner commands succeeded. The Release build reported **0 warnings,
0 errors**; packages, lock file, source, and runner were not changed. There was
no second restore or machine setup. No temporary code was created.

| Receipt/pin | SHA256 |
|---|---|
| `unattended-20261009-run.json` | `72E4AE3D4D7FB359AA3560C20127707BEC362A985D003CC01676891EB75B5FA0` |
| `unattended-20261009-native-assets.json` | `C2674EEE0DC26306E91AB1BD2C99FAB16B31B855FEB8AFB73C7989DB2300DB30` |
| Unchanged `packages.lock.json` | `339AB7FE3082CA0BE434D91555778F93A81D195643797CD4A6CFA3809247AADF` |

### Observed workload

Per candidate: 2,000 batched inserts, 100 individual autocommit inserts,
2,000 verified point reads and one exact fixture-count query; 4,133 synthetic
bytes per record. FULL-synchronous WAL, disabled pooling and disabled automatic
checkpointing match the historical workload.

| Metric | AES-GCM + equality token | SQLCipher + FTS5 |
|---|---:|---:|
| Initial open/schema, ms | 6.8529 | 1,505.0977 |
| 2,000-record transaction, ms | 221.5723 | 1,331.9262 |
| Batched records/second | 9,026.40 | 1,501.58 |
| Individual write p50 / p95, ms | 1.0182 / 2.7620 | 6.6042 / 35.7973 |
| Point read p50 / p95, ms | 0.0508 / 0.1294 | 0.1043 / 1.0170 |
| Exact fixture search/count, ms | 0.4417 | 0.8681 |
| Live main database bytes | 4,096 | 4,096 |
| Live WAL bytes | 11,185,832 | 25,164,992 |

**Other proofs were running concurrently on the shared development machine.**
These observations neither establish a regression against the older snapshots
nor qualify reference hardware, a performance budget or statistical significance.
The candidates have different search semantics, and both use the same old
SQLCipher native engine. Observed versions remain SQLite `3.39.2`, SQLCipher
`4.5.2 community`, LibTomCrypt `1.18.2`, 4,096-byte cipher pages, KDF 256,000
iterations and HMAC enabled; memory-only temporary storage was asserted.
Native hashes and sizes match the historical reports. X64 native loading was
exercised; x86 was published **but not executed**.

### Boundaries and cleanup

Only synthetic experiment databases, artifacts, keys and owned child processes
were used. No production database, second-user session, elevation, service,
account or machine configuration was accessed or changed. CurrentUser DPAPI
and actual owner-only directory/key-file ACL assertions passed. The runner
removed its own GUID scratch directory; the scratch root contained no run
directories afterward. Raw logs, original generated receipts and both published
output directories were retained outside git for worktree cleanup by the
coordinator. No keys, wrappers, binary assets or machine-specific paths were
added to the deliverables.

No operational blocker remains for this bounded automated run. Different-user
denial remains **unperformed**, not passed. Live SHM byte-locked regions remain
excluded from scans; marker absence is not general plaintext-absence evidence.
Installed/clean-machine x86/x64 native load, provenance/admission, physical
power-loss/fsync guarantees, wrapper/rekey crash recovery, broad temporary-file
observation and integrated production artifact/backup deletion remain unproven.
Copied wrappers/backups still preserve the documented deletion/rekey limits.
The synthetic intent-without-receipt assertion has no effect-dispatch path and
does not establish production no-replay, approval, grants or lifecycle behavior.
Existing maintained-production equivalence/disposition assessments below and
the canonical standard-SQLite decision are unchanged.

## Revised Profile-Integration Run

After rebasing onto the merged speech and containment outcomes, the Windows
proof passed **152 automated assertions, zero failed or blocked**, with the
same 14 actual process-kill/recovery trials and both Windows RID publications.
See the distinct [profile-integration run](evidence/profile-integration-run.json)
and [native-asset report](evidence/profile-integration-native-assets.json).
The original evidence files below were not overwritten.

Two new assertions verify application-controlled integration: the scope
actually passed by the DPAPI wrapper is CurrentUser, and the synthetic key
file's effective ACL inherits only the current user's full-control rule.
The directory ACL, real DPAPI roundtrip/corruption checks and same-user child
recovery remain exercised. This is not a claim of a different-user denial
trial or production profile-path integration.

The blanket cross-user gate was reassessed: Windows supplies per-profile
isolation, while Kora must verify its own scope, paths/copies and permissions.
The optional second-account trial remains unperformed rather than being
relabeled as passed. The runner now returns success for its bounded automated
proof; native admission and S1-S4 production work remain separate.
Safe scratch reruns need a loaded Windows profile, not an unlocked console;
this run did not instrument or certify the physical desktop's lock state.

| Metric | AES-GCM + equality token | SQLCipher + FTS5 |
|---|---:|---:|
| Initial open/schema, ms | 6.60 | 473.12 |
| 2,000-record transaction, ms | 52.90 | 254.73 |
| Batched records/second | 37,806 | 7,852 |
| Individual write p50 / p95, ms | 0.519 / 0.666 | 1.582 / 2.530 |
| Point read p50 / p95, ms | 0.013 / 0.025 | 0.041 / 0.080 |
| Exact fixture search/count, ms | 0.223 | 0.931 |

Workload, engine versions and observation limitations match the original
snapshot below. These are shared-machine measurements, not performance
acceptance budgets. The [deferred-validation register](../../Design/Deferred_Validation.md#storage-admission-follow-up)
records the remaining application-specific evidence and conditional
multi-account trigger.

## Original Windows-Only Snapshot

Original Windows-only proof run after checking upstream freshness: 2026-10-05,
Windows 10.0.26300,
x64, .NET runtime 10.0.12, SDK 10.0.401, 16 logical processors.
The shared development machine was not isolated or idle.

**150 automated assertions passed, 0 failed, 1 actual cross-user gate blocked under the original gate definition.**
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
