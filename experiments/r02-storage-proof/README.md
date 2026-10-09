# R02 storage/key feasibility supporting D-009

Status: **partial measured evidence and canonical design outcome; not production admission or a closed D-009 decision**.

**Current disposition:** migrate applicable recovery, migration/source
preservation, capacity, retention/deletion and artifact/backup-copy integrity
assertions to maintained production/integration tests on approved standard
SQLite before executable archival. Existing equivalence maps are partial;
shared crypto/native/candidate consumers prohibit whole-harness deletion.
Superseded encryption/rekey comparisons are historical unless R30 is pursued,
not revived R04 prerequisites. The
[three-tier policy](../../Design/Acceptance_Criteria.md#three-tier-qualification-policy)
and [disposition inventory](../../Design/Implementation_Roadmap.md#experiment-disposition-inventory)
own scope and archive triggers; this harness is not a default-CI or unrelated
merge gate. No code or original evidence is retired here.

## Maintained minimal Sessions equivalence assessment - 2026-10-07

The generic lifecycle-adjacent intent/no-receipt and kill/reopen atomicity
assertions are now exercised against actual production private standard
SQLite by maintained
[guarded Done/resume interruption tests](../../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionLifecycleInterruptionTests.cs)
and [session workspace tests](../../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionWorkspaceTests.cs).
They cover before/after authoritative COMMIT, restart generation/audit
agreement, Interrupted control intent with no replay, Unknown/live/question
blockers, revisions, cross-session reads and independent Perpetual preservation.
These are maintained production semantics, not relabelled synthetic evidence.

No executable experiment file is retired: the opaque sample harness has no
Active/Done session schema, and its intertwined WAL/rollback engine comparison,
SQLCipher/envelope cryptography, authenticated artifact stages, keyed backups/
rekey, capacity/migration, leakage, DPAPI and native-provider evidence are not
equivalent to this minimal standard-SQLite lifecycle slice. Historical receipts
and provenance remain unchanged. General retention/deletion and complete
artifact/backup disposal acceptance remain open.

The subsequent bounded metadata slice adds maintained production
[name/revision/migration preservation tests](../../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionMetadataTests.cs)
and extends the owned-process interruption harness to Create, Rename and
v1-to-v2 migration. Generic schema-version refusal, transaction atomicity and
reopen expectations are now maintained against exact production semantics.
This does not supersede the proof's opaque capacity/migration payloads,
encrypted engines, artifacts, managed backups/rekey, DPAPI, leakage or
native-provider cases. No executable or historical evidence is removed;
retaining the intertwined harness preserves those unique measurements.

The approved R01 merge (`7d5e6a3`, PR #19) was verified before starting.
An actual second-user Windows trial was initially left blocked with the
owner's approval. Reassessment distinguishes OS isolation from application
integration: that trial is optional for profile-local storage, not a merge gate.
No cross-user denial result or mock-identity Windows evidence is claimed.

This is a standalone, synthetic, executable experiment. It does not reference
Kora projects, open `%LOCALAPPDATA%\Kora`, change preferences, implement grants,
or define the future R04/R05 session/task schema. All new files, dependency
versions, lock files, scripts and measured evidence live in this directory.
Its outcome now updates the canonical architecture, decision, security/session
and acceptance contracts and roadmap. Production manifests, composition,
solution, persistence/preferences and CI are unchanged.

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

Requirements: Windows with the repository's .NET SDK (10.0.401), a loaded
ordinary user profile, and NuGet connectivity for the initial locked restore.
The file/database-only proof can run remotely while the physical console is
locked; it requires neither local desktop input nor microphone access.
No elevation, accounts, services or production databases are needed.

From the repository root in PowerShell:

```powershell
.\experiments\r02-storage-proof\Run-Proof.ps1 -PublishAssets
```

Direct runner exit codes: `0` for the automated proof passing or a successful
optional handoff operation, `1` for an assertion/operational failure.
Success covers only the bounded prototype, not native admission, final host
integration or production acceptance. Historical runs with a blocked
second-account gate remain unmodified; they used exit code 2 and an explicit
acknowledgement switch which the revised runner no longer needs.

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
[original Windows run](evidence/windows-run.json) and
[original asset report](evidence/native-assets.json) remain historical snapshots.
The [revised profile-integration run](evidence/profile-integration-run.json)
and [revised asset report](evidence/profile-integration-native-assets.json)
record post-rebase validation, not runtime guarantees.
See the [measured summary](MEASUREMENTS.md).
Re-run after changing the native package or platform.

The [2026-10-09 unattended rerun](MEASUREMENTS.md#unattended-rerun---2026-10-09)
adds separate [assertion/measurement](evidence/unattended-20261009-run.json)
and [native-publication](evidence/unattended-20261009-native-assets.json)
receipts from clean baseline `e0692f4`. It passed the existing automated scope
without executable or dependency changes. Historical receipts remain untouched;
the rerun does not reopen the superseded encrypted-production selection or
qualify reference hardware.

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

## Outcome in Canonical Contracts

The [Windows durable-storage direction](../../Design/Architecture.md#windows-durable-storage-direction)
now owns the implementation requirements: maintained authenticated
whole-database encryption, AES-GCM managed artifacts and CurrentUser DPAPI keys.
The [D-009 record](../../Design/Decision_Register.md#d-009-session-persistence-and-retention)
owns choices/rejections, remaining evidence and reconsideration triggers.
The [roadmap](../../Design/Implementation_Roadmap.md#r02-storagekey-outcome-and-follow-on-work)
assigns native/key admission to R02, durable migration/key/artifact recovery to
R04, and integrated lifecycle/managed-copy deletion to R12.

The measured old native engine is not production-admitted. An envelope fallback
requires revisiting D-009; the prototype does not silently select it or define
future host types. Detailed requirements belong in those canonical contracts,
not a second experimental specification. This directory preserves the runnable
evidence needed to verify or revisit them.

## Proof code lifecycle

Migrate applicable R04/R12 assertions into maintained production native-load,
storage-recovery and lifecycle tests. Retain the executable while applicable
equivalence is incomplete or shared candidate consumers remain. Archival/removal
requires exact maintained equivalence, explicit disposition of superseded
comparisons and passed consumer/reference checks; preserve reviewed historical
receipts. The current roadmap inventory above owns this lifecycle, not the
superseded encrypted-native selection or blanket retention until R30 exists.

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
| Application-controlled key scope and actual scratch/key-file ACLs | Measured; CurrentUser argument, directory/key-file rules and same-user recovery checked; production profile-path/copy integration remains R04 work |
| Actual different-user DPAPI/ACL denial | Not performed; optional Windows-boundary corroboration for profile-local storage, not a routine application or PR-merge gate |
| Maintained native engine, provenance/notices, installed Windows x86/x64 clean-machine load | Open; this package is not production-admitted |
| Power cut/fsync hardware guarantees, rekey/wrapper crash recovery, ephemeral-file observation, large-scale artifacts/indexing | Not proven by these bounded experiments |
| Final identities/schema, source/account revocation, lifecycle timer/startup/access, apply-now retention, live/unknown work holds, append-versus-delete coordination, grants and dispatch recovery | Deferred to R04/R05/R12; D-009 is not closed |

The canonical defaults remain 24-hour archive and 30-day deletion from the
same meaningful-activity clock, with configurable durations and no browsing
refresh. This storage proof neither implements nor changes them. Independent
perpetual grants and content-minimising diagnostics/security evidence must not
be deleted with conversation content; no prototype grant implementation exists.

## Production Recovery Migration and Retention - 2026-10-07

The approved baseline is now standard SQLite/private-profile permissions;
this experiment's candidate engine/encryption remains historical comparison,
not production admission. Applicable interruption assertions have moved into
maintained tests using actual production adapters and exact production schemas:

| Experiment assertion/path | Maintained production equivalent | Specific disposition |
|---|---|---|
| `CrashTests` / `CrashChild`: uncommitted/committed transaction, exact rows and journal recovery | [Task writes](../../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteTaskInterruptionTests.cs), [diagnostic/audit/span-link writes](../../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteEvidenceInterruptionTests.cs), [interaction approval/Once-use/Done](../../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteInteractionInterruptionTests.cs). Actual PERSIST/FULL adapter writes, hot header/pager flush, exact old state, all-or-none commit and private ACL preservation are checked. | Production recovery no longer depends on the prototype or a raw-SQL proxy. Retain these shared candidate paths because their SQLCipher/envelope WAL/journal and content-authentication comparisons use different engine/mode/key contracts; standard PERSIST tests do not replace those unique proofs. |
| Synthetic intent without receipt, no executable behavior | [Audited Interrupted/Unknown recovery](../../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteTaskInterruptionTests.cs), including interrupted recovery, repeated reopening and preserved committed terminal receipts; [composed runner](../../tests/Kora.Windows.IntegrationTests/Storage/DurableStorageCompositionTests.cs) checks no second invocation. | Prototype classification is not the production task contract. Retain its candidate encrypted-content restart check; production distinguishes intent-only Interrupted from dispatched/unverified Unknown. |
| Schema/corruption/access/capacity/migration negatives | Production task/evidence/interaction tests maintain exact schema, missing/permissive journal, corrupt rows, held ownership/access, rollback/cancellation and stale authority checks. | Retain encrypted-page/key failure, WAL capacity, failed DDL/copy conversion, source preservation and encryption migration comparisons: no equivalent production migration is newly claimed. |
| Artifact interruption, DPAPI, envelope/SQLCipher authentication, backup/rekey/deletion/native receipts | Outside this bounded production-store slice. | Retain `Crypto.cs`, `ScratchStore.cs`, shared `Program.cs`, runner/project/locked native dependencies and all historical receipts/provenance. Artifact/copy recovery and physical power-loss/installed gates remain unaddressed here. |

**No executable file is retired in this slice.** `Program.cs` and its
`CrashChild`/`CrashTests` routes remain consumers of those unique candidate
and artifact proofs; removing the shared harness would discard unaddressed
evidence. This is a specific retained-proof disposition, not blanket retention
until encryption becomes mandatory again. Retire a candidate path only after
all of its applicable behaviors have maintained equivalents and no consumer
remains. Checked-in receipts are unchanged, not relabelled as current
production results. No experiment was rerun as part of these production tests.

## Bounded Ordinary Retention Equivalence Assessment - 2026-10-07

[Maintained ordinary retention tests](../../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteDiagnosticRetentionTests.cs)
exercise actual production standard SQLite: effective due boundaries, bounded
log/span/link removal, atomic rollback/cancellation, concurrent writer/reader
admission, restart/backlog, citation gaps and unchanged task/interaction/audit/
Perpetual authority. The existing
[owned-process interruption tests](../../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteEvidenceInterruptionTests.cs)
also exercise before/after retention COMMIT and hot-journal reopening.

No experiment executable is retired. `Program.cs` deletion routes in
`StorageTests` still compare encrypted/enveloped samples, SQLCipher FTS,
`secure_delete`/checkpoint/VACUUM and a backup that deliberately recovers
deleted content. They are consumed by both candidate runs and share the
cryptography, rekey, artifact, native-provider and backup runner paths.
Ordinary production log/span pruning supplies no maintained equivalence for
those key/content/copy/engine behaviors. `Crypto.cs`, `ScratchStore.cs`, runner/
project/locked dependencies and original encryption/rekey/native/backup/deletion
receipts remain unchanged. None were rerun, relabelled or promoted to current
production evidence; physical power-loss and forensic erasure are not claimed.

## Optional Real Cross-User Handoff Protocol

The [canonical profile-boundary contract](../../Design/Architecture.md#profile-boundary-and-validation-responsibility)
requires validation of Kora's CurrentUser/profile-path/permission integration,
not routine re-proving of Windows cross-profile isolation. Use this optional
protocol only to corroborate that boundary or investigate a changed/shared
identity/storage design. It does not supply same-user worker containment.
See [S1-S4 in the deferred register](../../Design/Deferred_Validation.md#storage-admission-follow-up)
for the application/deployment work still required before production admission.

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
does not close the application/deployment gates.
