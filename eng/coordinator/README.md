# Finite coordinator decisions

The [roadmap coordinator](../../.github/agents/roadmap-coordinator.agent.md#required-executable-decision-path)
must use [Invoke-Coordinator.ps1](../Invoke-Coordinator.ps1) for a helper-managed
dataset. The [implementation](../Coordinator.Common.ps1) reuses repository
[path safety](../Distribution.Common.ps1) and local PowerShell/Git rather than
installing a service, database or dependency. PowerShell 7.5+ is required.

This is a finite local decision tool, not a native session launcher or background
scheduler. It never evaluates shell text from requests/receipts, contacts GitHub,
sets host preferences or assumes mailbox writes wake a chat. A successful
`dispatch` result reserves intent; it is not evidence that a child exists.

## Fresh session and canonical locator

From any verified native worktree, start with:

```powershell
.\eng\Invoke-Coordinator.ps1 -RepositoryPath $PWD -Action Read
```

The [entry point](../Invoke-Coordinator.ps1) uses the first registered Git worktree
(the primary workspace), its canonical common-Git identity and canonical origin.
Its one locator is `<primary-workspace>\.scratch\roadmap-coordinator`.
The directory must be ignored already, local and free of reparse/link ancestors.
All that repository's native worktrees discover the same locator; a different
clone is a different identity and cannot silently join.

Missing, corrupt, incompatible or inaccessible state is an error, not a fresh
empty ledger. Existing legacy datasets outside this locator remain authoritative
under their existing writer. Resolve their recorded locator and execution
disposition first; do not copy, import or migrate live writers. The bootstrap
receipt folder is evidence, not another registry. No invocation in the offline
tests initializes the user's real dataset.

Only after an authorized absence check proves that no prior ledger/admitted
execution is being abandoned, `Initialize` accepts this explicit request:

```json
{
  "legacyAbsenceConfirmed": true,
  "authorityId": "recorded-scoped-user-approval",
  "publicationPolicy": "ready-auto",
  "publisher": "verified-repository-cli-principal"
}
```

Write approved request data to an ignored owning-workspace file, then:

```powershell
.\eng\Invoke-Coordinator.ps1 -RepositoryPath $PWD -Action Initialize `
  -Session $ExactNativeCoordinator -RequestPath $ApprovedInitializationRequest
```

The [initializer](../Coordinator.Common.ps1) refuses existing data, including
unknown files, rather than resetting counters. The boolean records the trusted
caller's authorization/evidence; it cannot itself discover lost processes or
prove absence. Legacy adoption is deliberately unsupported in this slice.

## State and trust boundary

The [schema validator and transaction runner](../Coordinator.Common.ps1) own:

| File | Owner and role |
| --- | --- |
| `dataset.json` | Immutable schema 1, dataset ID and repository/common-Git/workspace identity |
| `writer.lock` | Stable nonblocking exclusive OS file handle, never removed to break contention |
| `registry.json` | Current validated snapshot: revision, fenced leader/lease, authority, owners, executions, deduplication and scoped recovery |
| `pending-transition.json` | One redo record: previous digest, complete next snapshot and next digest, flushed before snapshot replacement |
| `owners\<id>\*.json` | Registered owner's terminal evidence; only an exact registered-directory receipt can be consumed |

Each [mutation](../Coordinator.Common.ps1) holds `FileShare.None` across validation,
expected revision/epoch and three-minute lease checks, operation, redo publication
and atomic snapshot replacement/readback. A restart replays a contiguous pending
transition or verifies it already committed. A gap/digest/version/identity failure
stops recovery; it never becomes an empty registry. Interrupted initialization
needs explicit inspection/restore, not another `Initialize`. The redo slot is a
bounded recovery checkpoint, not an append-only audit history.

Local request files are trusted **coordinator operations**, not instructions
accepted from model/PR text. Registered owner receipts are data, never execution
or publication grants. The same-user filesystem is not protection from a hostile
local user. Approval/source certification, PR completeness, native lifecycle and
actual GitHub readbacks still require truthful evidence from their owning actors.
The helper validates their registered identities/schema and enforces transitions;
it does not authenticate native hosts or turn an assertion into proof.

The single persisted policy is `ready-auto`, `pr-only` or `local-only` for the
recorded authority scope. There is no request/receipt operation that broadens it.
The verified repository publisher is pinned alongside it and returned on dispatch;
fresh-session recovery never silently repins to another account. Only local-only
initialization may use a null publisher when no repository publication is admitted.
Different authority scopes, live legacy import and policy changes require a
separately designed authorized compatibility transition, not a second registry.
State is capped at 1 MiB and each index at 1,000 records; requests/receipts at
32 KiB. Full indexes stop rather than prune live deduplication/ownership.

## Finite operations

`Read` returns locator/state without claiming leadership. For each mutation pass
the exact native coordinator session and the last returned epoch/revision:

```powershell
.\eng\Invoke-Coordinator.ps1 -RepositoryPath $PWD -Action Next `
  -Session $ExactNativeCoordinator -ExpectedEpoch $Epoch -ExpectedRevision $Revision
```

Supply `-RequestPath` for structured operations. Unknown keys/types fail closed.
The supported [operations](../Coordinator.Common.ps1) are:

| Action | Request / result |
| --- | --- |
| `Claim` | Empty request. Same leader renews; another claims only an expired lease and advances the epoch. No child allocation is released. |
| `Register` | `ownerId`, semantic `operation`, repository-relative `scopes` array, `kind` (`feature`, `repair`, `evidence`, `historical`), `allocation` (`none` or `uncertain`), `evidence`. Explicit registration is not dispatch. A proved unadmitted historical label is ownership-only; a started/ambiguous legacy command must register `uncertain`. |
| `Certify` | `ownerId`, `repair` boolean, `certification` as below. Same-owner repair is prioritized; no new owner or PR is invented. |
| `Next` | Empty request. Reserve one certified nonconflicting candidate, concrete repair first; successive calls fill available slots without waiting for feature/assessment batches. If none is safe, return exact-intent reconciliation (at most three), once-only scoped recovery (at most three), or a blocker with constraints/missing inputs. |
| `Reconcile` | `executionId`, `outcome` (`accepted`, `uncertain`, `not-dispatched`), `evidence`, `dispatcherJoined` boolean, `nativeIdentity`, `branch`, `worktree`. Accepted binds actual launcher readback. Non-dispatch releases only with explicit original-dispatcher join/proved non-dispatch evidence, never missing-ID inference. It cannot undo accepted work. |
| `Recover` | `ownerId`, `reason`. Record exhausted assessment/unknown-owner/changed-input evidence, invalidating that owner's ready certification only. `Next` emits one targeted action, not repeated fleet discovery. |
| `Consume` | Absolute `receiptPath` in the registered owner's direct directory. Validate exact terminal evidence and release only its execution once, preserving source/PR ownership. |
| `ReleaseSource` | `ownerId`, exact `mergeSha`, `freshMainSha`, `reachabilityEvidence` from the authorized verifier. Requires PR delivery and no unreleased execution; never automatic from merge/age/absence. This releases collision scope, not cleanup authority. |

`recover-scoped` and `reconcile-dispatch` are bounded control-plane proposals,
not new worker grants. Any actual evidence/repair execution must itself be
registered, certified and reserved within the same ten-allocation ceiling.
`ReleaseSource` persists its exact merge/main/evidence provenance for restart.

Certification is a compact independently returned result, not an assessment-batch
completion flag:

```json
{
  "ownerId": "approved-operation-owner",
  "repair": false,
  "certification": {
    "sourceHead": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    "inputDigest": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
    "prObservation": "complete-repository-pr-observation-receipt",
    "observedUtc": "2026-10-25T07:00:00.0000000+00:00",
    "approvedOutcome": "Exact approved complete operation and source evidence",
    "placement": "Host-versus-extension rationale"
  }
}
```

The [selector](../Coordinator.Common.ps1) requires the PR observation within sixty
seconds and not in the future. Refresh that candidate's observation using the
owning bounded evidence/delta workflow; do not infer complete PR coverage from
its registry alone. Retained source scopes and semantic operation keys prevent
collisions even across disjoint paths. Evidence allocations count within ten,
with at most one active evidence owner. Unknown execution stays charged; merely
historical capability/source names do not invent running work.

Before each native dispatch the caller verifies its current epoch/admission and
host authority, then invokes the native launcher **once**. Intent is committed
before that call. If the caller crashes before native dispatch or before recording
its result, a restart returns reconciliation, never another dispatch for that
intent. No filesystem transaction can make the external native call exactly-once:
preserve ambiguity until exact producer/runner evidence or an authorized
cancellation-and-join resolves it. Do not reinterpret that gap as safe to retry.

## Exact terminal receipt

The [consumer](../Coordinator.Common.ps1) accepts only these fields. The owner
publishes complete UTF-8 JSON via a unique sibling/atomic rename and readback in
its registered directory; the leader consumes that exact path:

```json
{
  "version": 1,
  "datasetId": "actual-registered-dataset-id",
  "eventId": "unique-owner-terminal-event",
  "ownerId": "approved-operation-owner",
  "executionId": "actual-admitted-execution-id",
  "revision": 1,
  "nativeIdentity": "actual-launch-mapped-native-child",
  "branch": "actual-owned-branch",
  "worktree": "Q:\\actual\\owned-worktree",
  "head": "cccccccccccccccccccccccccccccccccccccccc",
  "kind": "pr-enrolled-ci-wait",
  "workState": "stopped",
  "finiteCommandsJoined": true,
  "runningCommands": [],
  "heldDecision": null,
  "publicationPolicy": "ready-auto",
  "prUrl": "https://github.com/roryprimrose/Kora/pull/485",
  "autoMerge": {
    "method": "SQUASH",
    "enabledAt": "2026-10-25T07:00:00.0000000+00:00"
  }
}
```

Those are schema examples, not actual admission or new enrollment evidence.
`pr-only` requires null Auto; `local-only` instead uses `kind: completed` with null
PR/Auto. A genuine terminal blocker may use `blocked-quiescent` with null PR/Auto,
but still must prove stopped/joined work and preserve ownership. It is not a
phase-release mechanism for incomplete publication.

The [consumer](../Coordinator.Common.ps1) persists event digest/revision and exact
publication evidence. Exact replay is a duplicate; reused IDs with changed
content, older revisions, different owners/executions, unjoined commands and
already released transitions cannot release again. CI pending does not consume
execution after a valid handoff, but neither merge nor missing chat proves that
an unjoined command terminated.

## Validation and unverified native acceptance

Run the [maintained process check](../Test-Coordinator.ps1) in a new output path:

```powershell
.\eng\Test-Coordinator.ps1 -OutputDirectory (Join-Path $PWD '.scratch\coordinator-contracts-local')
```

It invokes the actual entry point in independent PowerShell processes with local
Git fixtures, including another native worktree, concurrent last-slot reservations,
fenced takeover, redo/snapshot crash boundaries, independent partial candidates
and hostile/malformed receipts. Per-process argv/exit/stdout/stderr and `tests.json`
are retained; no feature/host session, provider or GitHub operation is called.
The [normal CI route](../../.github/workflows/ci.yml) runs it in portable validation
and a Windows shard; it does not lower portable coverage or protected merge gates.

This proves finite offline state-machine behavior, not tomorrow's actual native
session selection, original-host lost-command recovery, model selection, delivery,
automatic wakeup or continuation. A fresh native coordinator must actually read
the same locator, consume real supported native outcomes and record any re-entry
mechanism before claiming those. Unavailable host lifecycle evidence remains a
precise operational blocker. No installed automation or live dataset migration is
part of these tests or this helper.
