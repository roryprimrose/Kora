# W2 unattended file-only preparation — 2026-10-09

**Result: PreparedOnly, exit 0. Qualification: Not run.** This is a new bounded
receipt, not a repeat of the historical live trials or production admission.
The original [measured evidence](../README.md#final-safe-run) and its exact
qualification distinctions are unchanged.

## Source and tool identity

- Actual HEAD: `e0692f438a058de0a20021b3420a981849706dbf`.
- Actual branch: `proof/unattended-20261009-w2`.
- Source was **dirty**, with the new preparation script and documentation
  changes. [preparation.json](preparation.json) records the source status and
  exact selected source/lock hashes at execution, not a fictitious clean commit.
  This result page/export was added after execution; it is not part of that
  source snapshot. SDK/build-tool binaries are not fully attested by these hashes.
- Installed SDK **10.0.401**, PowerShell **7.6.6**, Windows x64.
- First successful run: **2026-10-09 11:02:41–11:03:15 UTC**.
- No package declarations or lock files were changed. The existing reconciled
  test lock was used as found, without repeating the earlier reconciliation.

## Commands and observed exits

Paths below are repository-relative; `$EvidenceDirectory` substitutes a new
private evidence directory. Full command arguments and exits are in the receipt.

| Command | Exit / result |
|---|---|
| Initial `dotnet build …\tests\W2Tests.csproj --configuration Release --no-restore --verbosity quiet` | **1**, `NETSDK1004`: missing test assets |
| `dotnet restore …\tests\W2Tests.csproj --locked-mode --verbosity quiet` | **0** |
| `dotnet restore …\payload\Payload.csproj --locked-mode --verbosity quiet` | **0** |
| `dotnet build …\tests\W2Tests.csproj --configuration Release --no-restore --verbosity quiet` | **0**, zero warnings/errors |
| `dotnet test --project …\tests\W2Tests.csproj --configuration Release --no-build -- --report-trx --results-directory $EvidenceDirectory\tests` | **0**, **97/97**, zero failed/skipped |
| `dotnet build …\payload\Payload.csproj --configuration Release --no-restore --verbosity quiet -p:PayloadVariant=Declared --output $EvidenceDirectory\declared` | **0**, zero warnings/errors |
| Same payload build with `PayloadVariant=Undeclared` and `…\undeclared` output | **0**, zero warnings/errors |

The full suite consists of **27 Admission**, **60 NetworkCollection**,
**6 InvocationPolicy**, and **4 DescendantObservations** test cases. The linked
network/consent/descendant tests exercise deterministic parsing and policy
functions only: they do not collect network events or launch owned trials.
Their passing results do not qualify W1 or validate OS containment.

Both payload DLLs were built but **never loaded or executed**. Their SHA-256
identities differ:

- Declared: `737869F0694EA88F70AB4991161BF9BB4B49ADDE0E138AAF72CBF854CAEEE4F4`.
- Undeclared: `26407D5A35D2066CCE3C5E71B322A60C8E916F2DCA4F9996CA0D911E4ABD3640`.

The apphost EXEs have identical hashes. As in the original experiment, an
apphost hash alone does not identify the managed dependency closure. Byte
differences are fixture preparation evidence, not observations of values 11/22.

## Preparation entry-point validation

- A second fresh-output run with existing assets: **exit 0**, **97/97**, both
  payload builds successful, **zero restore commands**.
- Reusing the first evidence directory: **exit 1**, every existing file hash
  unchanged. Prior evidence is not overwritten.
- Supplying the unsupported `-ConsentOwnedScratch` parameter: **exit 1** before
  creating output. It remains a separate live-run scope, not unattended consent.
- Historical `evidence\measured`, dependency locks and linked containment source
  compare unchanged against the baseline.

## Qualification and blockers

| Boundary | Current meaning |
|---|---|
| Deterministic test suite and payload compilation | Prepared/validated only |
| Historical exact dependency ACL/no-child candidates | Still **Rejected**; not rerun or reclassified |
| Historical embedded helper/marker result | Still **Feasible, not admitted**; no new marker observation |
| Native synthetic loading | **Blocked / Not run**; compiler acquisition/elevation prohibited; prior compiler prerequisite not rechecked |
| Protected installed runtime/ownership/ACL checks | **Blocked / Not run**, separately scoped lab work required |
| Restricted-token/AppContainer/live workers | **Not run**; no profiles, jobs or trial ACLs created |
| Windows lock/shutdown/restart/control APIs | **Not run**, no real effects |
| W1 network and W4 production lifecycle/host-death | **Not run**, independently unqualified |

The owner decision permitting best-effort transitive script dependency tracking
remains unchanged. Exact declared bytes, known-change revocation, protected
runtime identity, privacy, ownership, network, approval and receipt requirements
are not waived. A successful preparation exit is not the historical live
runner's exit 2 or any production-pass exit.

## Artifact preservation and publication

Private session evidence retains both complete runs, raw logs/TRX files,
built payloads, initial missing-assets failure, and guard-validation records.
These artifacts are outside the isolated worktree and survive its removal.
No machine-specific absolute paths, binaries, tokens or secrets are published.
No runtime copy, installation, elevation, compiler execution, live proof, native
loader, control action or cleanup of unrelated resources occurred.

The committed JSON preserves the generated receipt's fields, with output
arguments already replaced by `$EvidenceDirectory` and repository-relative
source names. Only the published copy's line endings are normalized to LF.
Logs and TRX reports referenced by it remain private; this is not an export of
the raw test report. Raw evidence is not rewritten.

- Raw receipt SHA-256:
  `086FDB8C45C1CF665582CD4311590A023C4DAB6D5CF9CB85C8315667647B194C`.
- Published LF receipt SHA-256:
  `3CBC192BC9C9250DD11351D3A815B637FDED7A44B775A5D680BA71540A0C7A01`.
