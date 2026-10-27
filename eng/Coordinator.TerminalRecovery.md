# Opt-in terminal transport compatibility

[Invoke-Coordinator.ps1](Invoke-Coordinator.ps1) remains strict by default. Its
`Consume -RecoverEvidenceTerminalMetadata` entry adapter recognizes only the
schema-1 `evidence-completed` assessment profile: all eighteen terminal fields
plus **exactly** `epoch`, `dispatchRevision`, `sourceHead`, `workspaceHead`,
`assessmentPath`, `assessmentSha256`, `pointerPath`, and `completedUtc`.
That profile is unchanged. No arbitrary legacy/bootstrap normalization or
publication correction is supported by it.

Two separate closed published-terminal profiles are available only with
`Consume -PublishedTerminalWireProfile UtcTimestampV1` or
`Consume -PublishedTerminalWireProfile HostedDeliveryV1`. They cannot be combined
with `-RecoverEvidenceTerminalMetadata`. Neither changes strict default parsing,
the existing core publication correction, or core consumption.

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

## Published-terminal wire requests

The timestamp-only profile accepts exactly the eighteen strict terminal fields,
`kind: pr-enrolled-ci-wait`, `workState: stopped`, ready-auto authority and
`autoMerge: { method: SQUASH, enabledAt: <explicit-UTC-Z> }`.
The hosted-delivery profile accepts exactly the same fields, but requires
`workState: ci-wait` and the GraphQL alias `mergeMethod` instead of `method`.
It does not accept a mixture of keys, other methods, unknown states or missing
quiescence facts. In **both** profiles, the original must explicitly affirm a
Boolean `finiteCommandsJoined: true`, array `runningCommands: []`, and
`heldDecision: null`.

The request for `UtcTimestampV1` has exactly these fields (SHA256 pins are raw
file-byte hashes, not canonical JSON digests):

```json
{
  "receiptPath": "<absolute original registered-owner JSON path>",
  "receiptSha256": "<64 hex digits>",
  "readbackPath": "<absolute archived gh-pr-view JSON path in the same owner directory>",
  "readbackSha256": "<64 hex digits>",
  "publisher": "<dataset pinned publisher>"
}
```

`HostedDeliveryV1` additionally requires `publicationProofPath` and
`publicationProofSha256`. This supplemental **original** publication proof uses
the closed `proof-publication` shape: version, timestamp, publisher, repository,
ownerId, executionId, nativeIdentity, branch, worktree, testedHead,
currentRemoteHead, prUrl, state, mergeStateStatus, reviewDecision, autoMerge,
enrollmentCommand, enrollmentResult, currentRequiredAppChecks, checkMeaning,
implementationProof, prReadback, checkReadback, finiteCommandsJoined,
runningCommands and heldDecision. Identity/source values must match the original;
`autoMerge` has exactly `mergeMethod` and `enabledAt`. Its exact protected
`gh pr merge <number> --repo github.com/roryprimrose/Kora --auto --squash
--match-head-commit <original-head>` command must have `enrollmentResult:
passed and read back`, with an explicit same-source joined/no-running/no-held
affirmation. The readback basename must match `prReadback`. Other proof
shapes, including narrative summaries, are unavailable rather than guessed.

The raw archived CLI readback must contain URL/number, main base, original
branch/head, OPEN/non-draft state, publisher author and SQUASH Auto enabled by
that same publisher, at the exact same instant as the original. Its optional
same-repository fields are also checked when present. Only the known CLI
metadata keys are allowed. The PR URL pins the repository; no remote query,
head refresh, enrollment or hidden fallback occurs. An archived original-head
readback proves historical publication only, **not** current candidate success.
A summary proof without that raw readback (including the supplied PR557 summary)
cannot alone satisfy this profile; provide the matching original archived CLI
artifact and digest, or leave recovery blocked.

Both timestamps must use ASCII `yyyy-MM-ddTHH:mm:ss[.fffffff]Z`, with zero to
seven fractional digits and a valid Gregorian date/time. Parsing is exact UTC;
the helper emits the identical instant with
`.ToUniversalTime().ToString('o')`. Naive times, numeric offsets (including
`+00:00` in this wire profile), lower-case suffixes, ambiguous dates, leap seconds
and overprecision fail closed. Existing strict roundtrip receipts still use
ordinary strict consumption. Fraction padding never changes the instant.

```powershell
.\eng\Invoke-Coordinator.ps1 -RepositoryPath $VerifiedOwnWorktree -Action Consume `
  -Session $CurrentLeader -ExpectedEpoch $CurrentEpoch -ExpectedRevision $CurrentRevision `
  -RequestPath $PinnedPublicationRequest -PublishedTerminalWireProfile UtcTimestampV1
```

Only the selected timestamp spelling, proven field alias, and explicitly
supported hosted label are projected. The latter is a **helper-owned semantic
projection of local execution quiescence**, not a new stopped receipt attributed
to the original actor. Original IDs/native/branch/head/revision/policy/PR/method
and actual time instant are retained. The separate helper proof records the
profile, request pins, original and readback raw bytes/path/digest, supplemental
proof bytes/digest when required, explicit original/normalized value mapping,
projection and semantic provenance. Request field ordering is canonicalized;
raw original and supporting evidence bytes must still remain identical.

All inputs and generated artifacts use the existing adapter lock, original-byte
evidence staging, atomic readback and read-only no-write/no-delete handles through
the unchanged fenced core transaction. Published input JSON rejects duplicate
properties recursively; request, terminal, Auto and authority-bearing proof
objects use closed field sets. Unknown/contradictory inputs never imply stopped,
SQUASH, publication authority or a timestamp. Changed or missing evidence fails
even on replay; changing a request pin cannot replace immutable provenance.
Same-user local files remain trusted engineering evidence, not cryptographic
host authentication. Never edit originals, fabricate the original writer, or
apply these fixture tests to the live ledger.

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

For the assessment profile, the same-user local filesystem is the existing trust boundary, not a defense
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
