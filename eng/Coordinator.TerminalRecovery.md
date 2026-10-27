# Opt-in assessment terminal compatibility

[Invoke-Coordinator.ps1](Invoke-Coordinator.ps1) remains strict by default. Its
`Consume -RecoverEvidenceTerminalMetadata` entry adapter recognizes only the
schema-1 `evidence-completed` assessment profile: all eighteen terminal fields
plus **exactly** `epoch`, `dispatchRevision`, `sourceHead`, `workspaceHead`,
`assessmentPath`, `assessmentSha256`, `pointerPath`, and `completedUtc`.
No arbitrary legacy/bootstrap normalization or publication correction is supported.

This repairs evidence scheduling, not product scope or native execution.
The [core consumer](Coordinator.Common.ps1) is unchanged and remains the only
locked/fenced transactional writer, including identity, authority, lifecycle,
revision, event deduplication and once-only release validation.

## Supported command

The current leader writes an ordinary request containing only `receiptPath`
(the absolute **original** registered owner's terminal JSON path). After reviewing
the original receipt and helper proof, use the actual current fence:

```powershell
.\eng\Invoke-Coordinator.ps1 -RepositoryPath $VerifiedOwnWorktree -Action Consume `
  -Session $CurrentLeader -ExpectedEpoch $CurrentEpoch -ExpectedRevision $CurrentRevision `
  -RequestPath $OriginalReceiptRequest -RecoverEvidenceTerminalMetadata
```

Do not invoke this on a live dataset as a test. Do not consume the generated
projection directly: retries use the original path and the same opt-in, with a
fresh fence obtained through `Read`. A blocked result is not a release.
No command text belongs in an owner's strict terminal receipt.

## Validation and provenance

- The owner must be registered as evidence with an accepted execution (or its
  already consumed replay). All additive fields are required,
  typed and checked. Epoch binds to the original registered execution, **not**
  the current leadership epoch. Dispatch revision is positive, no later than
  the snapshot revision, and matches both assessment and pointer; the core
  does not retain historical dispatch revisions, so this is documentary
  consistency, not a reconstructed dispatch authorization.
- Source head equals terminal head; workspace head is a separate valid SHA.
  The existing assessment's raw SHA256 must match. Assessment and pointer bind
  version/dataset/owner/execution/epoch/dispatch/source, and assessment workspace
  head. Pointer revision, kind, candidate count and assessment path/digest must
  agree. Completion time uses the existing UTC roundtrip parser.
- Paths must be canonical absolute JSON files directly in that registered
  owner directory, without links, reparse ancestors, traversal or stream names.
  Original, assessment and pointer must be distinct. JSON is bounded, valid
  UTF-8, and rejects duplicate top-level properties.
- A nonblocking stable `terminal-recovery.lock` serializes adapters only; it
  never substitutes for or breaks `writer.lock`. An intervening coordinator
  transition still fails the original expected fence at core consumption.
- A helper-owned immutable `terminal-recovery\<path-key>.proof.json` retains
  original path, raw bytes (base64), raw SHA256, assessment/pointer raw digests,
  projection path/raw digest/canonical digest, dataset and authority ID. Its
  explicit producer is the helper, **not** the original actor.
- The proof is flushed, atomically renamed without replacement and read back
  **before** the strict projection and request are published. The projection
  is `owners\<owner>\terminal-recovery-<path-key>.json`: every authoritative
  terminal value is copied unchanged; metadata is retained in the original
  and proof, not silently discarded. Original and supporting files, proof,
  projection and request remain open read-only without write/delete sharing
  through the unchanged core `Consume` call.
- A crash leaves immutable staged evidence, not a claimed release. Identical
  retry completes staging and core recovery/deduplication. Changed originals,
  supporting evidence, projections or proofs fail closed, even after release.
  Once consumption is recorded, missing helper evidence also fails closed;
  an existing projection/request without its preceding proof is never adopted.
  No counters, execution identities, branches, heads, policies or unrelated
  source ownership are rewritten. No remote authority or native launch occurs.

The same-user local filesystem is the existing trust boundary, not a defense
against malicious processes that can rewrite the entire dataset. Preserve all
evidence while referenced. A proof mismatch requires investigation, not deletion
or overwriting to make a retry pass.

## Offline process regressions

Run in new ignored owning-workspace output directories:

```powershell
.\eng\Test-CoordinatorTerminalRecovery.ps1 -OutputDirectory (Join-Path $PWD '.scratch\terminal-recovery-local')
.\eng\Test-Coordinator.ps1 -OutputDirectory (Join-Path $PWD '.scratch\coordinator-contracts-local')
```

Both use real entry-point processes and isolated Git fixture datasets. The
focused summary retains the successful fixture command, exact proof pointer
and process results. These are engineering process checks, not live native
session, provider or merge qualification.
