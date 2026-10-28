# Opt-in terminal transport compatibility

[Invoke-Coordinator.ps1](Invoke-Coordinator.ps1) remains strict by default. Its
`Consume -RecoverEvidenceTerminalMetadata` entry adapter recognizes only the
schema-1 `evidence-completed` assessment profile: all eighteen terminal fields
plus **exactly** `epoch`, `dispatchRevision`, `sourceHead`, `workspaceHead`,
`assessmentPath`, `assessmentSha256`, `pointerPath`, and `completedUtc`.
That profile is unchanged. No arbitrary legacy/bootstrap normalization or
publication correction is supported by it.

The original two closed published-terminal profiles are available only with
`Consume -PublishedTerminalWireProfile UtcTimestampV1` or
`Consume -PublishedTerminalWireProfile HostedDeliveryV1`. They cannot be combined
with `-RecoverEvidenceTerminalMetadata`. Neither changes strict default parsing,
the existing core publication correction, or core consumption.

Additional profiles below are separate opt-ins. Select exactly one profile
per call; the original profiles and their immutable replay proofs are unchanged.

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
artifact and digest, or use the strictly lossless profile below when applicable.

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

## Lossless UTC representation only

`Consume -PublishedTerminalWireProfile UtcRepresentationV1` takes exactly:

```json
{
  "receiptPath": "<absolute original registered-owner terminal JSON path>",
  "receiptSha256": "<raw original file SHA256>"
}
```

This accepts only the eighteen strict fields, stopped/joined/no-running/null-held
publication, and exactly `autoMerge: { method: SQUASH, enabledAt: <UTC-Z> }`.
The UTC spelling has the same exact zero-to-seven-fraction grammar as above.
Only that spelling changes to UTC roundtrip format; no identity, source,
revision, method, policy, work-state or decision is corrected. The unchanged
strict core validates every other original fact and remains the only consumer.
As with baseline strict consumption, publication facts are the original actor's
claims, not a new CLI verification. No archived readback is required or invented.
In particular, an App-updated PR557 head must not replace the original head.

```powershell
.\eng\Invoke-Coordinator.ps1 -RepositoryPath $VerifiedOwnWorktree -Action Consume `
  -Session $CurrentLeader -ExpectedEpoch $CurrentEpoch -ExpectedRevision $CurrentRevision `
  -RequestPath $PinnedOriginalRequest -PublishedTerminalWireProfile UtcRepresentationV1
```

## Closed literal-false equivalent absence

`Consume -PublishedTerminalWireProfile NoHeldDecisionV1` additionally requires
separate original delivery proof, prerequisite-ready stop proof and archived
raw CLI publication readback, all in the same registered owner directory:

```json
{
  "receiptPath": "<absolute original terminal JSON path>",
  "receiptSha256": "<raw terminal SHA256>",
  "readbackPath": "<absolute archived gh-pr-view JSON path>",
  "readbackSha256": "<raw CLI SHA256>",
  "publisher": "<dataset pinned publisher>",
  "deliveryProofPath": "<absolute original delivery-proof JSON path>",
  "deliveryProofSha256": "<raw delivery SHA256>",
  "stopProofPath": "<absolute original prerequisite-ready JSON path>",
  "stopProofSha256": "<raw stop SHA256>"
}
```

The closed original PR558 delivery/prerequisite shapes are supported, not generic
narrative proofs. All three original artifacts must explicitly contain Boolean
`finiteCommandsJoined: true`, array `runningCommands: []`, and Boolean
`heldDecision: false`. Literal false is the explicit absence flag; null, numeric
zero, empty values, strings, objects, arrays and true are not this profile.
Dataset/owner/execution/native, policy, publisher, branch/worktree/head/tree,
repository, PR and original filenames must agree. Distinct file paths are
mandatory. The existing `UtcTimestampV1` archived-readback validation is reused
without weakening it. Delivery command text is inert, not executed or parsed as
authority. Only this corroborated false-to-null mapping and same-instant UTC
spelling change. There is no idle/age/PR-based absence inference.

## Current original-dispatch completed sources

Use exactly one of `-CurrentOriginalDispatchProfile MissingNativeEvidenceV1`
or `-CurrentOriginalDispatchProfile MissingLaunchBlockerV1` with `Consume`.
Both take exactly:

```json
{
  "receiptPath": "<absolute original registered-owner terminal or blocker JSON path>",
  "receiptSha256": "<raw original SHA256>",
  "hostLaunchProofPath": "<absolute captured original host-launch proof JSON at dataset root>",
  "hostLaunchProofSha256": "<raw captured proof SHA256>",
  "sourceHead": "<exact original unchanged source commit>"
}
```

```powershell
.\eng\Invoke-Coordinator.ps1 -RepositoryPath $VerifiedOwnWorktree -Action Consume `
  -Session $CurrentOriginalDispatcher -ExpectedEpoch $CurrentEpoch -ExpectedRevision $CurrentRevision `
  -RequestPath $PinnedOriginalLaunchRequest -CurrentOriginalDispatchProfile MissingNativeEvidenceV1
```

These are not legacy retirement, launch, rebind or ownership-transfer APIs.
The leader must still be the execution's **original dispatcher in the current
epoch**, with the existing accepted grant and canonical native/branch/worktree
binding. Imported executions are unavailable. A missing identity derives only
from the successful joined original host creation link already accepted there.
A non-null claimed identity is never replaced, even if it matches.

The closed captured-host proof has `version`, `datasetId`, `coordinator`,
`source`, `sourceBytesObserved`, `observedUtc`, and `launches`. It is the
stream-captured SDK proof shape used by
`native-launch-proof-f6-5397-df16fb09.json`, with the exact source classification
`Original host-managed current-dispatcher SDK events; exact paired native create_session start/completion; no title or shared-parent inference`.
Every launch has exactly `ownerId`, `executionId`, `dispatcher`, `toolName`,
`toolCallId`, `startEventId`, `completionEventId`, `startedUtc`, `completedUtc`,
`nativeIdentity`, `originalCallSucceeded`, `originalDispatcherCallJoined`,
`initialHeaderSha256`. All entries must match current original grants/canonical
links; success/join are Boolean true. Tool-call IDs, event IDs, owners,
executions, header digests and child links cannot alias; start/completion/observation chronology
must agree. Exactly one entry names the source execution. Unknown, contradictory,
ambiguous, different-dispatcher or different-epoch proof rejects. In particular,
old unrelated execution5027 is not eligible.

This capture is trusted same-user engineering evidence provided and raw-pinned
by the current original dispatcher, not a signed host attestation. The helper
does not scan host storage, authenticate a fabricated capture, or reconstruct
missing event payloads from titles. An untrusted or unavailable capture is not
sufficient evidence and must not be submitted. Its original path/bytes/hash,
paired IDs, header digest and canonical grant are retained in helper provenance.

`MissingNativeEvidenceV1` accepts only an eighteen-field, fully stopped/joined,
no-running/null-held, nonpublished `evidence-completed` original for an evidence
owner, with **null** native identity. Every other original field is retained.
`MissingLaunchBlockerV1` accepts only the closed original
`receiptKind: missing-launch-mapping` blocker with matching clean preflight
branch/worktree/head, pinned publisher/repository/policy, and all seven effects
explicitly Boolean false. Its stopped/joined/no-running/null-held facts are
mandatory, not inferred from a missing launch file or lack of a PR. It derives
`blocked-quiescent`, revision 1 and a deterministic **helper** event ID bound to
the dataset/authority/grant and original raw hash. It does not claim the actor
wrote a strict terminal.

For both profiles the source must still be a canonical, non-reparse, clean Git
worktree at the exact original branch/head and canonical HTTPS origin. Read-only
checks run before staging and again immediately before strict consumption.
Changed or unavailable source fails closed; these checks do not lock Git against
a hostile same-user writer. Original immutable joined facts, current original
grant, trusted captured proof and the fenced core transaction are all required.
Other source states require a separately justified profile, not a fallback.

All new profiles use the existing immutable atomic staging, pinned read-only
handles, adapter lock and sole fenced core. Their helper proofs explicitly name
each derivation and preserve raw original/supporting bytes and request pins.
They do not change the existing assessment/publication proofs or replay keys.

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
