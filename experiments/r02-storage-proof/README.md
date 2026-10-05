# R02 storage/key feasibility supporting D-009

Status: **draft evidence; not a closed R02 storage/key gate or D-009 decision**.
The approved R01 merge (`7d5e6a3`, PR #19) was verified before starting.
Actual second-user Windows protection was deliberately left blocked with the
owner's approval. No mock identity is counted as Windows evidence.

This is a standalone, synthetic, executable experiment. It does not reference
Kora projects, open `%LOCALAPPDATA%\Kora`, change preferences, implement grants,
or define the future R04/R05 session/task schema. All new files, dependency
versions, lock files, scripts and recommendations live in this directory.
Production manifests, composition, solution, CI and canonical contracts are
unchanged.

**Supported OS scope: Windows only**, as confirmed by the owner on 2026-10-05.
Linux runtime support and Linux-host cross-build validation are outside this
proof, not blocked acceptance gates. Existing CI running on a Linux build host
does not imply Linux product support and is unchanged.

## Contracts and baseline inspected

- [D-009](../../Design/Decision_Register.md#d-009-session-persistence-and-retention)
- [Storage architecture](../../Design/Architecture.md#storage-and-processes)
- [Persistent sessions, lifecycle, deletion and restart](../../Design/Interaction_And_Sessions.md)
- [Retention, keys and security boundary](../../Design/Security_Data_Flows.md#retention-defaults)
- [Task recovery](../../Design/Task_Lifecycle.md)
- [Persistence acceptance](../../Design/Acceptance_Criteria.md#persistence-configurable-lifecycle-and-deletion)
- [R02/R04 dependencies](../../Design/Implementation_Roadmap.md#ordered-outstanding-work)

The inspected [bootstrap SQLite probe](../../src/Kora.Core/Dependencies/SqliteDependencyProbe.cs)
opens unkeyed SQLite, checks integrity, and transactionally creates a v1
`setup_tasks` table. The [directory probe](../../src/Kora.Core/Dependencies/StorageDependencyProbe.cs)
creates local directories and the roaming declarative-skill directory. These
were **read as source only**, never invoked. This proof's v1/v99 and opaque
`samples` table are private test fixtures, not production migration numbers.

## Reproduce

Requirements: Windows with the repository's .NET SDK (10.0.401), an ordinary
unlocked user profile, and NuGet connectivity for the initial locked restore.
No elevation, accounts, services or production databases are needed.

From the repository root in PowerShell:

```powershell
.\experiments\r02-storage-proof\Run-Proof.ps1 -PublishAssets -AllowBlockedCrossUser
```

The explicit switch acknowledges the blocked second-user gate; it does not
turn it into acceptance. Without the switch the script stops on that gate.
Direct runner exit codes: `0` for a successful handoff operation, `1` for an
assertion/operational failure, **`2` for the automated subset passing while
actual cross-user validation is blocked**.

Each run creates a new GUID directory under [`.scratch`](./.gitignore), restricts
its inherited ACL to the actual Windows user SID, redirects its process-local
TMP/TEMP there, and uses random 256-bit keys. The proof kills only its own
specific child process trees. Successful automated runs remove their own
scratch directory. Failures leave that exact synthetic directory for
investigation; never bulk-delete another run or the scratch root. No raw key,
DPAPI wrapper, database or handoff is committed.

Generated `evidence\latest.json` contains assertion results, observed file
names and measurements; `evidence\latest-native.json` contains published
native-asset hashes and architectures. Both are ignored. The checked-in
[Windows run](evidence/windows-run.json) and
[asset report](evidence/native-assets.json) are synthetic measured snapshots,
not runtime guarantees. See the [measured summary](MEASUREMENTS.md).
Re-run after changing the native package or platform.

The runner is deliberately a small console assertion harness, not a new
production test-runner dependency. Any unexpected exception fails visibly.
It logs reason categories and exception type/stack, not record payloads,
SQL parameters, passwords or connection strings.

## Approaches exercised

| Candidate | Actual experiment | Main trade-off |
|---|---|---|
| SQLCipher page encryption | Microsoft.Data.Sqlite.Core + isolated `bundle_e_sqlcipher`; real keyed databases, FTS5, WAL and rollback journals, explicitly keyed online backups and rekey | Encrypts content-bearing pages and FTS without custom SQL storage serialization; native provenance/maintenance, temporary stores and key/backup lifecycle remain critical |
| Application envelopes | AES-256-GCM BLOBs in unkeyed SQLite; fresh 96-bit nonce, 128-bit tag, version and location/role AAD | Portable managed content authentication; every content-bearing field must be enveloped; schema/row count/size/order and index access patterns remain visible |
| Equality index | Domain-prefixed HMAC-SHA256 token for one synthetic search term | No literal token in SQLite, but equal words and hit frequencies are linkable; no substring/range/ranking/semantic search proof |
| Managed artifacts | Authenticated encrypted files, flushed staging, rename before committing a reference, interrupted orphan/reference recovery | SQLite and filesystem publication are not one atomic transaction; needs a recovery/GC protocol and bounded memory/size admission |
| Windows key custody | Real CurrentUser DPAPI wrapping, owner-only scratch ACL, real same-SID child unwrap, tamper rejection and lost-key errors | Protects at rest across the intended OS boundary, not against same-user code, administrators, compromised profiles or exported copies |

Both SQLite candidates use **the same native engine** to keep this experiment
bounded; the unkeyed run is not a benchmark of production's `e_sqlite3`.
The hex encoding of the random database key is passed as a password through
the provider, so SQLCipher's normal 256,000-iteration KDF is measured. A raw
key API could change startup cost but is not measured here.
The provider password is an immutable managed string and cannot be reliably
zeroed; clearing key byte arrays does not clear all copies of decrypted data.
Process memory, paging and third-party/OS dumps are outside this proof.

## Failure and leakage evidence

- AES-GCM rejects changes to version, nonce, tag and ciphertext, truncation,
  wrong keys, row substitution and artifact identity substitution.
- SQLCipher rejects a changed authenticated page and an incorrect key. Wrong
  key and future-version refusals are checked against the existing file hash.
- Four actual kill/reopen database boundaries per candidate: uncommitted WAL,
  committed WAL, durable synthetic intent without receipt, and a live rollback
  journal with cache spill. Integrity and exact row counts are checked.
- Three actual artifact kill/reopen boundaries per candidate: flushed staging,
  published orphan, and committed reference. Recovered files authenticate and
  no committed fixture reference is dangling.
- Injected DDL/content/version migration failure rolls back all three. A failed
  copy-conversion preserves the synthetic legacy source byte-for-byte; a
  successful candidate is read back before any replacement. Applying a key
  to an existing plaintext database is explicitly shown **not** to migrate it.
- `max_page_count` induces a real SQLite capacity error without replacing
  existing rows. This is not an OS disk-full/power-loss hardware trial.
- Live/closed databases, WAL, rollback journals, backups, artifact staging and
  structured proof diagnostics are scanned for the synthetic UTF-8 and UTF-16
  marker. A deliberately unsafe plaintext FTS index is a **positive control**:
  the marker really is found in its WAL. It is then removed from this run.
- `temp_store=MEMORY` is asserted; a content-bearing temporary table is exercised.
  No file-backed SQLite temporary store is authorized by this recommendation.
  Ephemeral/deleted-open files and OS paging are not exhaustively observed.
- Live `-shm` WAL-index files are inventoried but not read because Windows
  SQLite byte locks reject reading those regions. SHM contains WAL-index
  metadata; this is an explicit scanning exclusion, not an encryption claim.
- Targeted deletion preserves another record. Index entries disappear after
  full deletion. Backups still recover deleted records; artifact unlink still
  leaves a copied artifact readable. Rekey does not rotate a prior backup,
  and deleting a DPAPI key file does not revoke a copied wrapper.

Absence of the marker is a scoped sampling result, **not proof that all
plaintext, secrets, logs, memory, paging or crash dumps are absent**.
The SQLCipher temporary-file caveat is documented by
[its security design](https://www.zetetic.net/sqlcipher/design/).

The "intent without receipt" fixture demonstrates preserved unknown evidence,
not actual action execution, authorization, session restoration or no-replay
enforcement. Those remain R04/R05/R12 host responsibilities. The harness
contains no effect-dispatch path at all.

## Recommended strategy

**Prefer maintained, authenticated whole-database encryption for content-bearing
events/metadata/search plus AES-GCM for out-of-database artifacts, with random
keys wrapped by CurrentUser DPAPI and restricted local ACLs.** Select a modern,
proven native distribution only after the deployment gates below. Do not ship
this experiment's unofficial old native engine.

1. Keep all content-bearing FTS/summaries/indexes inside the keyed store.
   Force in-memory temporary storage on every connection; fail initialization
   if the expected engine/encryption configuration is absent. Ban unkeyed
   backups, diagnostic SQL tracing and exports of decrypted temp files.
2. Use SQLite transactions for ordered evidence/revision commits with FULL
   durability. Persist required intent before an effect and receipts afterward.
   Recovery reads evidence and marks interrupted/unknown work; it never dispatches
   automatically. This proof does not prescribe the final schema.
3. Stage/authenticate/flush artifacts, publish before reference commit, and
   reconcile staged/orphan/missing files on recovery. Bound artifact sizes.
   The immutable digest can itself leak equality/content identity: consider
   opaque names or keyed digests for sensitive artifacts in the final design.
4. Treat key wrappers as versioned recoverable data. Missing/invalid keys stop
   writes and dispatch dependent on durable evidence, never create a new
   database or fresh key over existing content. Plan rotation and wrapper
   publication together with every retained backup. DPAPI entropy here is a
   public domain separator, not a password or second security boundary.
5. Define a deletion ownership inventory for current rows, FTS, artifacts,
   caches, staging, journals and every **managed** backup. Purge/rewrite owned
   recoverable copies; do not promise per-record cryptographic erasure with a
   shared database key. Per-owner content keys may help only if every wrapped
   copy, plaintext index and historical backup is also handled; that design is
   not proven here.
6. Explicitly disclose lack of forensic erasure: SSD wear levelling, filesystem
   snapshots, OS paging/dumps, user exports and provider copies survive local
   unlink. `secure_delete`, checkpoint and VACUUM are hygiene, not guarantees.

If maintained page encryption cannot be admitted, the measured envelope
approach is a viable **narrower fallback** for content records/artifacts, with
in-memory search or explicitly accepted equality-token leakage. It must
envelope all retained content-bearing metadata and derived indexes; plaintext
FTS is not an acceptable shortcut. Key separation, keyed-index rotation,
nonce budgets, replay/rollback resistance, per-owner deletion and large-store
search costs still need design and tests before exposure.

AES-GCM/page MACs detect corruption and tampering, but neither proves complete
history, detects deletion of whole valid records, nor prevents restoring a
valid older database/backup. D-008 tamper-evident audit is a separate gate;
this proof does not implement it or any grant store.

## Licensing, native assets and Windows-only scope

See [the native/licensing assessment](NATIVE-ASSETS.md). RID publication for
win-x64 and win-x86 is exercised on Windows. Published x86 binaries are not
execution evidence. The runner does not publish Linux assets; neither Linux
execution nor Linux-host builds are requirements for this proof.

## Remaining gates / D-009 boundary

| Gate | Status |
|---|---|
| Synthetic encryption/authentication, transactions, migration/capacity failure, backups, deletion limits and benchmark | Measured; see checked-in run |
| Actual current-user DPAPI/ACL and same-user process restart | Measured on Windows x64 |
| Actual different-user DPAPI/ACL denial | **Blocked by explicit owner choice; draft PR** |
| Maintained native engine, provenance/notices, installed Windows x86/x64 clean-machine load | Open; this package is not production-admitted |
| Power cut/fsync hardware guarantees, rekey/wrapper crash recovery, ephemeral-file observation, large-scale artifacts/indexing | Not proven by these bounded experiments |
| Final identities/schema, source/account revocation, lifecycle timer/startup/access, apply-now retention, live/unknown work holds, append-versus-delete coordination, grants and dispatch recovery | Deferred to R04/R05/R12; D-009 is not closed |

The canonical defaults remain 24-hour archive and 30-day deletion from the
same meaningful-activity clock, with configurable durations and no browsing
refresh. This storage proof neither implements nor changes them. Independent
perpetual grants and content-minimising diagnostics/security evidence must not
be deleted with conversation content; no prototype grant implementation exists.

## Approved real cross-user handoff protocol

Do this only with two existing approved Windows account sessions, **not** a
mock SID, new account, credential request or unrestricted ACL change.

Owner account, from this experiment directory:

```powershell
dotnet run --project StorageProof.csproj -c Release --no-build -- --prepare-cross-user
```

This verifies the owner's unwrap, writes a synthetic DPAPI key and handoff in its
own protected scratch directory, and prints its SHA256. The owner explicitly
copies only that file to an approved second-account-readable location and
independently confirms the digest to the tester. Keep owner scratch ACLs intact.

Actual different Windows account, from a readable copy of this experiment
(no user data is shared):

```powershell
dotnet run --project StorageProof.csproj -c Release --no-build -- `
  --verify-cross-user '<approved handoff path>' '<owner-confirmed SHA256>'
```

The verifier checks the owner-confirmed digest and real process SID, refuses
the same SID, and requires Windows DPAPI to reject the **unchanged** owner blob.
Capture both account trials with timestamps/OS and different SID hashes.
Also have that account try opening the owner's exact synthetic key file at
its original protected location and record the real ACL denial; copying a
handoff intentionally bypasses the file ACL to test DPAPI separately.
The verifier alone does not attest that separate ACL trial.

Do not commit raw SIDs, handoffs or key wrappers. Remove only the named handoff
and its exact owned run after readback. A future real result needs review and
does not automatically promote this PR or close the remaining gates.
