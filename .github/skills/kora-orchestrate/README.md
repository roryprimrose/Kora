# Runtime evidence and use

Select **Kora Coordinator** and run `/kora-orchestrate` in the owning repository.
For an observed live run follow [the trial contract](./trial.md). Use the durable
checkpoint CLI in ignored scratch with explicit observe/delivery authority;
do not rely on conversation history alone for reservations and pending actions.
This bootstraps a finite event request, not a permanently active model turn.
Child reports trigger subsequent event requests in the same chat; the registry
retains `enabled=true` until explicit interruption.

The selected model/profile must support the requested frontmatter. The exposed
session-creation API selects a model, not a custom agent or reasoning effort.
Child contract links therefore supply behavior, not proof that profiles loaded.
The caller explicitly permits requested high-reasoning worker definitions with
unverified effective settings. Report that condition; do not fabricate verification.

## Non-writing host probe, 2026-11-07

The probe used one nested child, no worktree, no repository writes, no GitHub
operations, and no automations.

| Observation | Evidence | Meaning |
| --- | --- | --- |
| Child's initial turn completed | Child context, turn 1 `complete` | A final child turn can be inspected without its parent report being consumed. |
| Queued follow-up started in the same child | Message `b11647f4-3f86-42b5-9e86-db22864d2742`, `started/processing`; child turn 2 `complete` | Same-chat queued child continuation was exercised. |
| Parent callback stayed pending | Child callback `df3641e4-9861-48ce-9049-892c68734ce4`, `pending`; parent turn 10 still `inProgress` | Keeping the parent turn active can starve queued reports. |
| Active report accepted as steering | Message `12b60a23-c7da-49b0-89d6-a56434fbd62c`, `steered/processing` | Active-turn delivery was accepted; this is not proof of consumption or idle resumption. |
| Old queued probe reports cancelled | Child turn 3 cancellation report, both `cancelled` from `pending` | No pending probe report was intentionally left to restart the parent. |
| Effective settings not exposed | Child host-metadata inspection | Requested model accepted; model/effort verification remains unavailable. |

This first probe did **not** prove idle coordinator resumption. A subsequent
finite coordinator-role fixture exercised that path:

| Observation | Evidence | Meaning |
| --- | --- | --- |
| Finite bootstrap ended with registry enabled | Fixture chat `0d4fe4cb-3968-4fb6-903c-164b3714087c`, turn 1 `complete`; steering attempt rejected with `targetStatus=idle` | Normal bounded turn completion did not disable or archive the nested coordinator. |
| A separate child sent its terminal callback | Sender chat `6babbaf2-709a-49ec-b58a-29d532463bbc`; queued message `ccb6cdc3-d6f0-486e-8b91-4047148d9bbc`, `started/processing` | Child-to-idle-coordinator delivery was exercised without automation. |
| Same coordinator resumed and processed the callback | Same fixture chat, turn 2 `complete`, token `finite-coordinator-20261107`; one synthetic feature reservation and one unexecuted launch action | Same-chat resumption and finite event processing were observed, not merely send acceptance. |

The fixture read the coordinator/skill contracts and ran the reducer with
synthetic in-memory state. It did not automatically select an agent profile.
It tested a **nested** coordinator chat, not idle resumption of the enclosing
main chat, and not sustained production delivery. No production workers or
PRs were launched. The final reducer's numbered-pass/checkpoint guards have
separate deterministic regression evidence; this is transport evidence, not
exact-source production qualification.

All three non-writing fixture chats were archived after completed turns and
verification that no owned files/processes or pending restarting instructions
remained. Retained local receipts are under the owning ignored scratch path.

## Deployment acceptance

The first observed live trial on 2026-11-07 ran five discovery passes at source
`33208fefebb24b1b0cf41cf6fa482f3403205f34`. Its retained checkpoint revision 13
contains no candidates, launch actions, publications or cleanups. The open-PR
inventory was empty, and the bounded final feature inventory found no unblocked
candidate. This exercised planning transport, not actual delivery. See the
local ignored trial checkpoint/receipts for evidence; this is not global product
completion or an exhaustive implementation audit.

Observation improved two contracts: queue a separate same-planner next turn from
its validated terminal report instead of waiting for a future host completion
event; and inventory unfinished admitted implementation before tracing shipped
operations. The second rule was applied late in that trial, so the trial does
not establish efficient candidate selection or production delivery performance.

Before advertising unattended operation on a host:

1. Verify the coordinator prompt/agent/skill are discoverable on that host.
2. Finish a bounded coordinator bootstrap turn without archiving/disabling it.
   If automatic continuation keeps it active, that is not a successful yield.
3. Have a non-writing child send a queued terminal report after the coordinator
   is actually idle. Verify a new turn in the exact same coordinator processes
   the report ID and registry action. Acceptance alone is insufficient.
4. Exercise an active-turn report, an already-completed child missing its report,
   and recovery from child context on another genuine event.
5. Confirm model settings where exposed; otherwise keep UNVERIFIED explicit.

If step 2 or 3 is unsupported, report the exact limitation. Definitions cannot
repair the host's lifecycle/transport. Do not introduce automation, replacement
coordinators, periodic polling, or promises that an idle model can wake itself.

Run all three focused modules:

```powershell
node --test .\.github\skills\kora-orchestrate\scripts\registry.test.cjs .\.github\skills\kora-orchestrate\scripts\checkpoint.test.cjs .\.github\skills\kora-orchestrate\scripts\worker-launch.test.cjs
```

The maintained [policy runner](../../../eng/run-policy-tests.cjs) selects all
three modules when this skill is installed and fails if any required module is
missing. Run `node .\eng\run-policy-tests.cjs` for the combined maintained checks.

Deterministic registry tests exercise reservations, separate pools, feature-first
delivery, stale/duplicate reports, cleanup-before-release, and bounded discovery.
They are not production delivery or runtime qualification evidence.
Checkpoint tests additionally cover pending actions, duplicate replay, stale
writers, lock ownership, and failed atomic replacement without lost state.
Worker-launch fixtures cover disjoint asynchronous isolated requests, exact
callbacks, publication authority, source freshness, and safe control/receipt
paths. They do not establish that real workers publish or clean up successfully.

## Shrink-only reservation protocol

`refine-scopes` contracts an attached **stopped** running or published reservation
in enabled delivery mode. It never releases a slot, changes owner/key/outcome,
kind/pool/source/PR/phase/publication/blocker/cleanup, archives a chat, removes
files or authorizes new source writes. The retained owner has
`sourceWritesRetired=true` permanently. Running/cleanup-blocked owners remain
blocked; independently admitted genuinely disjoint candidates can dispatch.
No new product or indexed-source feature is implemented by this protocol.

### Exact consumed input

Apply through the existing [checkpoint CLI](./scripts/checkpoint.cjs) with the
current checkpoint `expectedRevision`. The event has exactly these fields:
`id`, `type="refine-scopes"`, `owner`, `key`, `expectedScopeRevision`, `proof`,
`acknowledgment`. Unrefined legacy items have effective scope revision **0**.
All proof/acknowledgment/nested object fields below are mandatory; unknown
fields reject. Paths are unique canonical lowercase repository-relative Windows
paths, without glob, absolute, traversal, control characters, trailing dots or
spaces. Scope/path lists are case-sensitive canonical data, not glob patterns.

Illustrative **synthetic published-owner** proof (not host evidence):

```json
{
  "version": 1,
  "owner": "exact-stopped-worker-chat",
  "key": "held-outcome",
  "reservation": {
    "outcome": "Original admitted outcome",
    "kind": "feature",
    "pool": "ready",
    "head": "1111111111111111111111111111111111111111",
    "pr": 869
  },
  "publication": {
    "pushedHead": "2222222222222222222222222222222222222222",
    "remoteHead": "2222222222222222222222222222222222222222",
    "pr": 869,
    "ready": true,
    "enrolled": true
  },
  "scopeRevision": 0,
  "fromScopes": ["src", "tests"],
  "toScopes": ["src\\owned.cs", "tests\\owned.test.cs"],
  "phase": "published",
  "publicationHead": "2222222222222222222222222222222222222222",
  "manifest": {
    "complete": true,
    "actual": ["src\\owned.cs", "tests\\owned.test.cs"],
    "planned": []
  },
  "preservation": {
    "complete": true,
    "commits": [{
      "head": "2222222222222222222222222222222222222222",
      "paths": ["src\\owned.cs"],
      "receipt": "Verified original committed diff and preserved local ref"
    }],
    "retained": [{
      "path": "tests\\owned.test.cs",
      "receipt": "Verified retained local write"
    }],
    "unpreserved": []
  },
  "stoppedWriting": true,
  "expansionFrozen": true,
  "pendingInstructions": 0,
  "receipt": "Fresh exact host/owner evidence; actual cleanup still blocked"
}
```

`reservation` copies the item's original outcome/kind/pool/head/pr.
`publication` copies its complete original publication object, not a new remote
query. For running owners both `publication` and `publicationHead` are `null`;
assigned repair PRs remain bound. A later publication may fill an originally
null running PR, but cannot change any previously assigned PR.
Published proof must preserve its original publication head in `commits`,
including a retained ref/commit receipt even if that commit's path list is empty.
`commits[].paths` lists every relevant original committed write; use additional
commit records for other retained heads. `retained` and `unpreserved` have the
same `{path,receipt}` shape and unique local paths. Their union with committed
paths must equal `manifest.actual`; all actual **and planned** paths must be
covered by the new scopes. Removed/committed files remain actual writes.
Unpreserved writes remain reserved with their existing blockers; they are not
treated as preserved or safe to delete.

The acknowledgment has exactly `id`, `receipt`, `owner`, `key`, `scopeRevision`,
`proofDigest`. It repeats the proof's identity/revision and binds **all** proof
fields, including old/proposed scopes, manifests and preservation evidence.
`id` is a fresh concrete owner-response identity, unique across the registry.
`receipt` identifies the actual response/evidence; an invented receipt or a
digest computed by the coordinator without an owner response is not consent.
Use exported `digest(proof)` from [registry.cjs](./scripts/registry.cjs):
SHA-256 of UTF-8 JSON with recursively sorted object keys and unchanged array
order. This is consistency binding, **not authentication**.

From the repository root, calculate the digest of an already prepared proof-only
scratch JSON. Module resolution uses the runtime's native filesystem paths;
the proof's canonical Windows scope/path data is unchanged:

```powershell
Get-Content -LiteralPath $ProofPath -Raw |
    node -e 'const fs = require("node:fs"); const path = require("node:path"); const { digest } = require(path.resolve(".github", "skills", "kora-orchestrate", "scripts", "registry.cjs")); console.log(digest(JSON.parse(fs.readFileSync(0, "utf8"))));'
```

### Client sequencing and durable compatibility

1. Read the exact checkpoint without mutation; reconcile pending actions and
   identify the exact holding owner/key/revision/current scopes.
2. Derive original committed/local-retained/unpreserved writes from Git and
   filesystem receipts, not just clean status or current remote equality.
   A merged PR whose remote branch is absent keeps its original publication
   evidence and independent local preservation. Host locks remain cleanup
   blockers. Neither a deleted branch nor idle status proves stopped writing.
3. Verify source quiescence, permanent frozen expansion and zero queued **and
   processing** broad instructions. Obtain a concrete fresh owner response
   acknowledging the entire final proof, digest, old/new scopes and revision.
   Unknown host facts fail closed; the reducer does not discover these facts.
4. Apply once with current `expectedRevision` and `expectedScopeRevision`.
   Use the existing stdin example in [the skill](./SKILL.md); persist returned
   actions before sends. Stale/invalid input has no effects; same event ID is
   idempotent. Reusing an acknowledgment under a new event ID rejects.
5. Only accepted proper contraction requests/coalesces eligibility through
   existing discover/steer/dispatch machinery. Written paths still conflict;
   no slot is released. Carry retired-owner exclusions into planner/worker
   instructions. Do not activate source writes for the old owner.
6. Keep original PR/cleanup evidence and history. Completion still needs the
   separate original cleanup proof; refinement does not fix a failed cleanup
   gate or authorize remote-proof fabrication. Later repair requires allocation.

The first accepted event migrates registry `version:1` to **2** atomically,
leaving untouched legacy version-1 states readable and launchable. Version 2
requires every item to have `scopeRevision`, `scopeHistory` and
`sourceWritesRetired`; unrefined/new items have `0`, `[]`, `false`. Each accepted
record is `{eventId,proof,acknowledgment}`. Revisions are consecutive, current
scopes equal the final proposal, and successive proofs retain all prior actual
and planned paths. Top-level `refinements` stores `{key,eventId,digest(record)}`
and `seen` retains the event plus a reserved `scope-refinement:v1:<digest>`
marker. No normal input event may use that marker prefix.

Checkpoint reads additionally bind `refine-scopes` event journal entries to
the registry index. Missing/altered history, omitted retirement, stale scope
revision, proof/acknowledgment replay, event/index mismatch and schema downgrade
reject on checkpoint read/apply and worker-launch validation. Later legitimate
publication, blocking, pause/resume and proven completion retain the history.
Deleting every binding and coherently forging the entire store is outside this
deterministic guard: local hashes do not authenticate a checkpoint. The trusted
coordinator must verify real host/owner/Git evidence. No real reservation is
refined merely by shipping this implementation or running synthetic tests.
