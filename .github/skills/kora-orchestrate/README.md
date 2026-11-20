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

## Opt-in version10 lifecycle

The [registry](./scripts/registry.cjs) exports a version10 engineering interface, distinct from legacy versions1-9 below. Loading or merging it performs no migration, adoption, release, host action or source execution. Legacy validation and histories retain their original meanings. All examples in this section are synthetic, not evidence of live consent, quiescence or OS enforcement.

### Public boundary

Use the CommonJS exports in [registry.cjs](./scripts/registry.cjs), [checkpoint.cjs](./scripts/checkpoint.cjs) and [worker-launch.cjs](./scripts/worker-launch.cjs). Missing/malformed/stale authority throws `Error`; CLIs write the error to stderr and exit1. No dependency installation is needed.

| Export | Signature and result |
| --- | --- |
| `createLifecycleState` | `(coordinator, head: SHA40|null, mode='observe') -> LifecycleState`; explicit version10, empty history, no grants. |
| `previewLifecycleMigration` | `(acceptedLegacyState) -> {version:10, legacyDigest, projection, activating:false}`; read-only, every unresolved old exclusion remains `HELD`. |
| `adoptLifecycle` | `(acceptedLegacyState, adoptionEvent, {lifecycleEvidence}) -> {state, actions:[]}`; complete fresh human/holder/host evidence required. |
| `lifecycleProjection` | `(state) -> {version:10, coordinator, enabled, mode, head, sourceRevision, authorityRevision, works, grants, reservations, capacity}`; semantic history replay, no trusted cached projection. |
| `lifecycleRevisions` | `(state, command) -> {sourceRevision, authorityRevision, works, grants, reservations}`; maps of affected IDs to complete canonical digests, absent work/grant IDs to null. |
| `lifecycleProofDigest` | `(event) -> SHA256`; omits only human/holder `proofDigest` leaves to avoid circularity. All other event fields are bound. |
| `applyEvent` | `(state, event, {lifecycleEvidence}) -> {state, actions, duplicate}`; version10 uses only lifecycle commands; legacy events cannot bypass the projection. |
| `validateSourceGrant` | `(state, {grantId,grantRevision,owner,source,head,paths}, {sourceAuthorityVerified:true,receipt}) -> {valid:true,executionAuthorized:false,grantId,grantRevision}`. |

`LifecycleState` has exactly `{version:10, origin:{coordinator,head,mode,legacy,legacyDigest}, history}`. The complete unchanged legacy state is stored once, or both legacy fields are null. Each history record is `{event,hostEvidence,digest}`; its digest binds the previous record digest, event and host evidence. New records contain no recursive before-state or embedded source copies.

The shared projection derives work/PR/publication history, delivery capacity, grant generations and current custody. Work status is `candidate|open|delivered|cancelled|abandoned`.
Each grant has `{id,key,generation,revision,scopes,status,owner,source,head,sourceRevision}`; status is `allocated|reserved|ambiguous|active|revoked|unresolved`. Each reservation has `{id,key,grantId,scopes,revision,status:'HELD'}`.

New publications have `{pushedHead,remoteHead,pr,ready:true,enrolled:true,receipt}`. Imported publications/cleanup retain their original legacy evidence shape. Work fields are `{key,outcome,pool,kind,evidence,pr,status,publications,cleanup}`.
Capacity counts open deliveries, not retained directories. Grants are execution identities, not chat identities; a fresh explicitly bounded grant never resurrects an old grant.

### Events and independent evidence

Every event has exactly `{id,type:'lifecycle',command,expected,evidence}`. Obtain `expected` from `lifecycleRevisions` for the final command; do not invent revisions. `evidence` has `{human,holders,receipt}`.
Authority operations require a fresh exact `{id,actor,proofDigest,receipt}` human decision. Bookkeeping (`register,publish,refresh,ambiguous,pause`) requires `human:null`; pause still requires every live holder's actual stopped/joined proof.

Supply independent context `{lifecycleEvidence:{kind:'host-verified-lifecycle',eventDigest,coordinator,ledgerAuthorityVerified:true,humanAuthorizationVerified,holdersVerified,sourceOwnershipVerified,deliveryVerified,preservationVerified,receipt}}`.
The digest binds the complete final event, not an earlier draft. All flags are explicit booleans. Host facts must be derived independently; a supplied digest or receipt does not authenticate them.

For `attach`, `sourceOwnershipVerified` includes actual child/worktree/branch/head ownership and compatibility with the admitted base/PR. For `compatible`, it includes exact old/current main and source-head/scopes compatibility, not disjoint-path inference. For `cleanup`, it includes actual absence of all affected paths/refs. `deliveryVerified` includes current execution's delivery.

Each required holder entry has exactly `{grantId,owner,writingStopped:true,commandsRunning:false,workJoined:true,pendingInstructions,processingInstructions,fence,preservation,acknowledgment}`. Counts are verified `0` or literal `'UNKNOWN'`, never defaulted.
Both modes require a fresh established cooperative fence `{epoch,nonce,established:true,revokesEarlierSourceAuthority:true}` and acknowledgment `{id,proofDigest,receipt}`.

Preservation has `{actual,planned,committed,restored,retained,unpreserved,commits,bytesPreserved:true,refsPreserved:true,historyPreserved:true,receipt}`. Every path list is canonical/unique.
Unpreserved must be empty; all actual/committed/restored bytes must be retained, and commits require `{head,ref}` anchors. Known earlier actual/restored/committed/planned evidence cannot disappear. Ordinary amendment cannot cover an already unauthorized write.

Holders are sorted by grant ID: transfer/release holders; all grants of an allocated/repaired/closed/physically-cleaned work; named grant for revoke/amend/compatible; all live/unresolved grants for adopt/pause/recover. Retirement covers the entire grant, not only transferred paths. Untransferred scopes remain held. External guards stay nonactivating and cannot be disposed through a local transfer.

| Command `kind` | Other exact fields | Behavior |
| --- | --- | --- |
| `adopt` | `legacyDigest` | First event only, explicitly adopts one complete validated controlling legacy ledger; imports no active execution grant. |
| `register` | `key,outcome,pool,workKind,evidence,pr` | Registers one admitted operation. `pr` is explicit null or positive number; same PR uses repair, not another work. |
| `allocate` | `key,grantId,scopes,transfers` | Fresh identity/current custody, no launch. Retires all affected old source grants. |
| `dispatch` | `grantId` | Separately authorized once-only launch action `{type:'launch',key,pool,grantId,grantRevision}`. |
| `attach` | `grantId,owner,source` | Resolves reserved/ambiguous launch with actual exclusive source/child evidence. Source is `{worktree,branch,head}`. |
| `ambiguous` | `grantId,reason` | Retains unresolved launch custody and capacity; no retry or replacement. |
| `revoke` | `grantId,reason` | Actual stop/join/fence/preservation permanently terminates that grant; holds remain. |
| `publish` | `grantId,publication` | Records verified ready publication; does not release authority, custody or capacity. |
| `amend` | `grantId,scopes` | Current active source, complete superset/exclusive union, actual temporary pause/join/fence and new revision; no automatic continuation. |
| `refresh` | `head` | New verified source increments source revision; active grants cannot continue until compatible. |
| `compatible` | `grantId,head`, optional `source` | Fresh independent compatibility and exact holder/human evidence; increments grant revision. Explicit source-head advancement keeps the exact worktree/branch. |
| `close` | `key,outcome,releases` | Outcome `delivered|cancelled|abandoned`; every grant terminated/joined, bytes/refs/history preserved, delivered requires latest execution's publication. Releases only explicit exact paths; omitted paths remain held. |
| `release` | `releases` | Explicitly disposes retired remainder custody, not active writers or external snapshots. |
| `repair` | `key,pr,grantId,scopes,transfers` | Same work/PR in repair capacity, fresh generation, actual prior-source termination/preservation, no old-grant revival. Supports retained open or closed deliveries. |
| `pause` | `reason` | Stops dispatch, joins/revokes live grants and retains all work/custody. |
| `recover` | `coordinator` | Paused, joined old authority only; stays paused and invalidates source freshness. |
| `resume` | none | Explicit exact current human/host authority; does not revive grants or establish missing source. |
| `cleanup` | `key,worktreeAbsent:true,branchAbsent:true,receipt` | Separate optional observed physical cleanup of closed work; never automatically deletes/archives anything. |

`transfers` and `releases` are explicit arrays of `{reservationId,paths}`. Only exact held path tokens can move; no ancestor subtraction. `allocate` and `repair` require no remaining overlapping reservation, including retained/external guards. Original item/history bytes are never edited to represent a transfer.

Only the projection drives dispatch, exclusion, amendment, source continuation, repair, recovery, inspection, closure and optional cleanup. No historical-participant ban applies to a fresh version10 grant. Legacy retired grants remain rejected. No expiry reclamation, queue daemon, scheduling, OS isolation or host authentication is supplied.

### Checkpoint and legacy construction

The [checkpoint wrapper](./scripts/checkpoint.cjs) keeps its version1 envelope and global expected revision/lock/atomic replacement. Every write, including unrelated acknowledgments, still requires the actual current checkpoint revision. Authority proofs bind affected source/authority/grant/work/custody revisions, so unrelated bookkeeping does not itself force new approval.

- Default `init` remains legacy version1. Explicit `lifecycleVersion:10` creates a new version10 ledger; it never replaces an existing checkpoint.
- `read` validates all history and action journals without writes.
- `preview-lifecycle` reads a legacy checkpoint and returns `previewLifecycleMigration(state)` without writes/revision changes.
- `adopt-lifecycle` requires `expectedRevision,event,lifecycleEvidence`, validates complete legacy history, and appends the adoption journal without rewriting old events/actions.
- `apply` forwards `lifecycleEvidence`; only `dispatch` may produce a launch action. `ack` retains its existing concrete-result semantics.
- Authority replay fails; read accepted state instead. Exact bookkeeping replay returns `duplicate:true` with no actions or persistence. Changed same-ID replay always fails.
- Missing evidence, stale affected/global revisions, lock contention, malformed projection/history/journal or failed replacement preserve the original checkpoint bytes and actions.

Construct a legacy input using existing `createState` and the applicable version1-9 events below, or use a complete accepted checkpoint's state. Do not assign a larger version number to incomplete fields. Preview first.
For explicit adoption construct a shadow `{version:10,origin:{coordinator:old.coordinator,head:old.head,mode:old.mode,legacy:old,legacyDigest:digest(old)},history:[]}` solely to obtain `lifecycleRevisions(shadow,{kind:'adopt',legacyDigest:digest(old)})`.

Then obtain actual fresh decision/holder/host evidence for that event and call `adoptLifecycle(old,event,context)` or the checkpoint adoption operation. This is an engineering construction route, not permission to adopt any live ledger. Original retirements, publication, supersession, amendments, recovery/renewal, cleanup and external guards remain intact. Unresolved old grants cannot execute.

### Synthetic retained-work example

Run this example with Node from the repository root. Every human/holder/host fact is intentionally synthetic; no checkpoint, host session, filesystem ownership, consent or live grant is created.

```javascript
const r = require('./.github/skills/kora-orchestrate/scripts/registry.cjs');
let state = r.createLifecycleState('SYNTHETIC coordinator', '1'.repeat(40), 'delivery');
const epochs = new Map();
function apply(command, holderIds = []) {
  const p = r.lifecycleProjection(state), id = `synthetic-${state.history.length}`;
  const human = !['register', 'publish', 'refresh', 'ambiguous', 'pause'].includes(command.kind);
  const event = { id, type: 'lifecycle', command, expected: r.lifecycleRevisions(state, command),
    evidence: { human: human ? { id: `${id}-human`, actor: 'SYNTHETIC human',
      proofDigest: '', receipt: 'SYNTHETIC decision' } : null,
    holders: holderIds.sort().map(grantId => {
      const g = p.grants.find(g => g.id === grantId), epoch = (epochs.get(grantId) ?? 0) + 1;
      epochs.set(grantId, epoch);
      return { grantId, owner: g.owner, writingStopped: true, commandsRunning: false,
        workJoined: true, pendingInstructions: 'UNKNOWN', processingInstructions: 'UNKNOWN',
        fence: { epoch, nonce: `${id}-${grantId}`, established: true, revokesEarlierSourceAuthority: true },
        preservation: { actual: g.scopes, planned: g.scopes, committed: g.scopes, restored: [],
          retained: g.scopes, unpreserved: [], commits: [{ head: g.head, ref: `refs/heads/${grantId}` }],
          bytesPreserved: true, refsPreserved: true, historyPreserved: true, receipt: 'SYNTHETIC preservation' },
        acknowledgment: { id: `${id}-${grantId}-ack`, proofDigest: '', receipt: 'SYNTHETIC response' } };
    }), receipt: 'SYNTHETIC complete evidence' } };
  const binding = r.lifecycleProofDigest(event);
  if (event.evidence.human) event.evidence.human.proofDigest = binding;
  for (const h of event.evidence.holders) h.acknowledgment.proofDigest = binding;
  const result = r.applyEvent(state, event, { lifecycleEvidence: {
    kind: 'host-verified-lifecycle', eventDigest: r.digest(event), coordinator: p.coordinator,
    ledgerAuthorityVerified: true, humanAuthorizationVerified: human, holdersVerified: holderIds,
    sourceOwnershipVerified: true, deliveryVerified: true, preservationVerified: true,
    receipt: 'SYNTHETIC independent readback, not authentication' } });
  state = result.state;
  return result.actions;
}
for (const [index, key] of ['a', 'b', 'c'].entries()) {
  apply({ kind: 'register', key, outcome: `SYNTHETIC ${key}`, pool: 'ready',
    workKind: 'feature', pr: null, evidence: 'SYNTHETIC complete operation' });
  const prior = ['a', 'b'][index - 1], scopes = ['src\\synthetic.cs'];
  apply({ kind: 'allocate', key, grantId: key, scopes,
    transfers: prior ? [{ reservationId: `grant:${prior}`, paths: scopes }] : [] }, prior ? [prior] : []);
  apply({ kind: 'dispatch', grantId: key });
  apply({ kind: 'attach', grantId: key, owner: `SYNTHETIC ${key}`,
    source: { worktree: `Q:\\Synthetic\\${key}`, branch: `synthetic/${key}`, head: '1'.repeat(40) } });
  apply({ kind: 'publish', grantId: key, publication: { pushedHead: '2'.repeat(40),
    remoteHead: '2'.repeat(40), pr: 100 + index, ready: true, enrolled: true, receipt: 'SYNTHETIC PR' } });
  apply({ kind: 'revoke', grantId: key, reason: 'SYNTHETIC actual stop/join' }, [key]);
  apply({ kind: 'close', key, outcome: 'delivered', releases: [] }, [key]);
}
const p = r.lifecycleProjection(state);
console.log(JSON.stringify({ closed: p.works.map(w => w.status), capacity: p.capacity,
  retained: p.works.every(w => w.cleanup === null), custody: p.reservations.map(h => h.key),
  grants: p.grants.map(g => g.status) }));
```

The [runtime regressions](./scripts/registry.test.cjs) execute this synthetic example and separately assert stale-grant rejection, partial/multi-holder custody, same-PR repair and fail-closed history/authority behavior. The [checkpoint regressions](./scripts/checkpoint.test.cjs) cover the corresponding persistence boundary; this evidence does not establish live deployment or consent.

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

## Explicit human-authorized scope-incident recovery

Use the separately typed `recover-scope-incident` event only for an attached running, nonretired same owner with a prior incidental out-of-reservation write. Ordinary extension still rejects that write and still requires pending zero; recovery is not a flag on extension, refinement, readmission or supersession ([verifiers](./scripts/registry.cjs)).

### Authority and host sequencing

1. Stop source writes, join current work and owned commands, and retain every dirty file and touched/restored-path receipt. Independently inventory complete actual/planned paths, preserved commits/local files, restored effects and every held guard. Do not derive actual history from dirty status alone ([proof validation](./scripts/registry.cjs)).
2. Read current repository, coordinator, verified source head, exact item digest, original reservation and durable checkpoint revision. Bind the exact additions and complete original union to the full effective held-reservation snapshot, including passive, published, blocked and superseded guards ([heldReservations](./scripts/registry.cjs)).
3. Establish one explicit reconciliation mode below. Obtain a fresh exact-owner acknowledgment of the entire final proof, including live joined work and rejection of earlier broad source authority. Idle status, implementation approval, a proposed union or an old incident receipt is insufficient ([recovery proof](./scripts/registry.cjs)).
4. Obtain a **new actual human decision** approving only prospective coverage over the final proof **and acknowledgment**, including any fence. Independently read back that decision, owner response, live pause/join and established instruction authority through the host. Populate `recoveryEvidence` only from those observations ([host evidence contract](./scripts/registry.cjs)).
5. Apply once through the [checkpoint CLI](./scripts/checkpoint.cjs) at the current revision. Acceptance reserves paths and persists immutable unauthorized provenance and host-supplied evidence. It emits **no actions**, including unrelated dispatch, launch, resume or blocker clearance. Keep ownership, PR/slot/publication/cleanup obligations and unrelated blockers ([reducer](./scripts/registry.cjs)).
6. Separately reconcile any incident-specific blocker under its own authority, preserving the original blocker in recovery history and all unrelated blockers. No recovery blocker-resolution operation is supplied here. Issue a separately authorized exact continuation only after live authority checks and `validateRecoveryContinuation` below. If pause/join/fence cannot actually be established, preserve the incident and withhold continuation ([verifiers](./scripts/registry.cjs)).

These are deterministic **consistency verifiers**, not host consent authentication, queue enumeration, filesystem enforcement or a network/session client. A caller able to fabricate every supplied observation is not authenticated by a digest or receipt. The host owns real human identity/decision, command and write inventory, exact-owner response, pause/join, instruction revocation and execution enforcement; never label synthetic inputs as those observations ([implementation](./scripts/registry.cjs), [synthetic fixture](./scripts/incident-recovery-fixture.cjs)).

### Pending zero versus UNKNOWN reconciliation

- `enumerated-pending-zero` requires separately observed `pendingInstructions=0` **and** `processingInstructions=0`, with concrete enumeration evidence. Source pause, no running commands and joined current work remain independently required ([validation](./scripts/registry.cjs)).
- `human-approved-source-fence` retains both counts as the literal string `"UNKNOWN"`. Require an actually established same-owner source-authority revocation fence: a positive monotonic `epoch`, fresh `nonce`, `revokesEarlierSourceAuthority=true`, `established=true` and concrete evidence. The final owner acknowledgment and human decision bind that fence; it is not an automatic missing-count fallback ([validation](./scripts/registry.cjs)).
- Every earlier broad source instruction loses source-write authority under the fence. The same owner must enforce rejection before any write, even if an old instruction later starts processing. The host must enforce that protocol; the reducer cannot cancel a queued message or prove a drain. A later recovery cannot drop an established fence, reuse its nonce or lower its epoch ([validation](./scripts/registry.cjs)).
- This branch changes no pending-zero requirement for ordinary extension, shrink, supersession, handoff or cleanup. UNKNOWN is never cleanup evidence, and temporary pause/fencing never retires, replaces or duplicates the owner ([legacy and recovery regressions](./scripts/registry.test.cjs)).

### Exact input and synthetic execution

The [synthetic fixture generator](./scripts/incident-recovery-fixture.cjs) emits a complete exact `apply` input with `expectedRevision=41`, `event` and separately supplied `recoveryEvidence`. It uses nine touched paths, eight dirty files and one restored incidental lock; tests also exercise nine retained dirty files. **Do not pipe this fixture into a live checkpoint** ([behavioral tests](./scripts/registry.test.cjs)).

```powershell
node .\.github\skills\kora-orchestrate\scripts\incident-recovery-fixture.cjs
```

All objects have exactly the following fields; unknown/missing fields reject. Paths are unique canonical lowercase repository-relative Windows paths, not glob/ancestor subtraction. Array order is part of each digest ([schema](./scripts/registry.cjs)):

| Object | Exact fields |
| --- | --- |
| event | `id`, `type="recover-scope-incident"`, `proof`, `acknowledgment`, `authorization` |
| proof | `version=1`, `repository`, `coordinator`, `checkpointRevision`, verified main `head`, `source`, `owner`, `key`, `expectedItemDigest`, `reservation`, `scopeRevision`, `fromScopes`, `additionalScopes`, `toScopes`, `exactFiles`, `incident`, `manifest`, `preservation`, `workingTree`, `quiescence`, `reconciliation`, `exclusivity` |
| source | same `owner`, absolute canonical Windows `worktree`, `branch`, current source `head` (original reservation or a preserved commit), actual identity `receipt` |
| reservation | `outcome`, `kind`, `pool`, original `head`, `pr` |
| exactFiles | `complete=true`, `paths` equal to exact additional files, `receipt` for verified file kinds |
| incident | `id`, `owner`, `key`, `command`, `originalHead`, `originalScopes`, `unauthorizedPaths`, `receipt` |
| incident.command | `id`, literal `command`, original command `receipt` |
| manifest | `complete=true`, all `actual` including restored paths, all `planned` |
| preservation | `complete=true`, `commits`, `retained`, `unpreserved`; same commit/path receipt shapes as [refinement](./README.md#exact-consumed-input) |
| workingTree | `complete=true`, `dirty`, `restored`, `receipt`; dirty paths must be retained, restored effects must remain explicitly unpreserved in history |
| quiescence | `sourcePaused=true`, `commandsRunning=false`, `workJoined=true`, live `receipt` |
| reconciliation, zero | `kind="enumerated-pending-zero"`, `pendingInstructions=0`, `processingInstructions=0`, `receipt` |
| reconciliation, UNKNOWN | `kind="human-approved-source-fence"`, `pendingInstructions="UNKNOWN"`, `processingInstructions="UNKNOWN"`, `fence`, `receipt` |
| fence | `epoch`, `nonce`, `revokesEarlierSourceAuthority=true`, `established=true`, `receipt` |
| exclusivity | `complete=true`, exact current `reservations=heldReservations(state,key)`, `receipt` |
| acknowledgment | fresh `id`, `receipt`, exact `owner`, `key`, `scopeRevision`, `proofDigest=digest(proof)`, `workJoined=true`, `staleAuthorityRejected=true`, `fence` (exact fence or `null` for zero) |
| authorization | `kind="explicit-human-recovery-decision"`, fresh `id`, human `actor`, `decision="prospective-coverage-only"`, `subjectDigest=digest({proof,acknowledgment})`, actual decision `receipt` |
| recoveryEvidence | `kind="host-verified-scope-incident"`, `coordinator`, `checkpointRevision`, `proofDigest`, `ownerResponseId`, `humanDecisionId`, `humanDecisionDigest=digest(authorization)`, `sourceIdentityVerified=true`, `sourcePauseVerified=true`, `commandsJoinedVerified=true`, `instructionAuthorityVerified=true`, independent host readback `receipt` |

`toScopes` is exactly `fromScopes + additionalScopes`, without overlap. Each addition names an explicitly planned exact file. `incident.unauthorizedPaths` equals every actual path outside the original reservation; acceptance never changes that historical classification. Preservation union must equal all actual writes; prior actual/restored and planned history cannot disappear in later scope transactions ([validation](./scripts/registry.cjs)).

Run this exact synthetic in-memory JavaScript with Node's eval option from the repository root. It tests both reconciliation modes without a filesystem checkpoint, host call, dependency command or source continuation ([fixture](./scripts/incident-recovery-fixture.cjs)):

```javascript
const path = require('node:path');
const root = path.resolve('.github', 'skills', 'kora-orchestrate', 'scripts');
const { applyEvent, validateRecoveryContinuation } = require(path.join(root, 'registry.cjs'));
const { incidentFixture, recoveryEvent, recoveryContext, continuation } =
  require(path.join(root, 'incident-recovery-fixture.cjs'));
for (const mode of ['enumerated-pending-zero', 'human-approved-source-fence']) {
  const before = incidentFixture();
  const event = recoveryEvent(before, 41, mode);
  const result = applyEvent(before, event, recoveryContext(event));
  const checked = validateRecoveryContinuation(result.state, continuation(result.state), { revision: 42 });
  console.log(JSON.stringify({ mode, actions: result.actions, checked }));
}
```

Expected output for each mode includes `actions:[]`, `coverageVerified:true`, the retained blocker and `sourceExecutionAuthorized:false`. This validates only supplied coverage/fence bindings; it deliberately does not resolve a blocker or authorize execution ([example regression](./scripts/registry.test.cjs)).

### Separate exact continuation and persistence

`validateRecoveryContinuation(state, request, {revision})` is read-only. Its request has exactly `version=1`, `kind="exact-recovered-owner-continuation"`, current `coordinator`, main `head`, accepted `sourceHead`, `checkpointRevision`, `key`, same `owner`, complete current `expectedItemDigest`, current `scopeRevision`, `grantDigest=digest(acceptedRecoveryRecord)`, current `fence`, and fresh `acknowledgment` ([function](./scripts/registry.cjs)).

That acknowledgment has exactly `id`, `owner`, `requestDigest=recoveryContinuationDigest(request)`, `grantDigest`, `fence`, `workJoined=true`, `staleAuthorityRejected=true`, `receipt`. The digest excludes the acknowledgment itself, binding the complete request including current revision/item. Reused recovery acknowledgment, stale source/coordinator/revision/item/grant/fence or retired/superseded owner rejects. The host still needs actual separate execution authority and blocker reconciliation ([validation](./scripts/registry.cjs)).

Version 1 retains exact same-main behavior. If unrelated compatible main advancement invalidates it, use the explicit [same-owner continuation renewal](./README.md#same-owner-continuation-renewal), not a rewritten grant, rebase of retained dirty work or an ignore-main option.

Acceptance explicitly migrates legacy versions 1-5 to version 6 without discarding existing refinement/amendment/readmission/supersession proofs. Full recovery records retain the original item and schema, complete event, independent supplied host evidence and an empty command journal; per-item indexes and reserved seen/digest markers bind them. Checkpoint event digests and empty action bindings detect omission, altered replay and downgrade. Old valid states remain accepted without recovery fields ([migration tests](./scripts/registry.test.cjs), [atomic checkpoint tests](./scripts/checkpoint.test.cjs)).

### Dependency closure lesson

Before any dependency command, inspect and reserve the entire affected tracked manifest/lock/license-notice closure. Include all solution-project locks restored by the command even when their source is not edited; direct project references alone do not establish this closure. Withhold dependency commands until accepted coverage. This tooling protocol itself requires no restore, dependency, lock or notice change ([worker contract](./worker.md#implementation-and-publication), [launch guidance](./scripts/worker-launch.cjs)).

### Same-owner continuation renewal

`renew-recovery-continuation` explicitly revalidates an accepted recovery for compatible unrelated main advancement. It never grants paths, rewrites the original recovery/main/source base, changes the item/scopes/fence/blockers/history, resumes work or emits any actions. The original owner must still be attached, running and nonretired/nonsuperseded ([verifier](./scripts/registry.cjs)).

Collect and apply in this order ([checkpoint](./scripts/checkpoint.cjs), [worker](./worker.md#explicit-scope-incident-recovery-acknowledgment)):

1. Reconcile source through ordinary `refresh`, then read the current checkpoint. Capture the complete current item/digest, scope revision/scopes, latest original recovery record/digest, latest prior renewal/digest (or `null`) and `heldReservations(state,key)`. Do not reuse the original recovery checkpoint revision.
2. Independently reverify the same owner's unchanged worktree/branch/source HEAD, original recovered scopes and current fence. Obtain actual fresh live source pause, joined work/commands and instruction-authority observations. UNKNOWN remains UNKNOWN; enumeration-zero owners still require actual current zero.
3. Inventory the **entire** original-grant-main to current-main change, including deleted paths and both rename sides; verify forward ancestry from the previous renewal head, or original main for the first renewal. Reject any overlap with recovered scopes. Independently review relevant source/dependency/contracts against the preserved source base; disjoint paths alone are not compatibility.
4. Bind this complete proof to a fresh exact-owner acknowledgment. Independently read back the owner's response and compatibility/source/pause/join/instruction facts through the host into `renewalEvidence`. A supplied receipt/digest or an unchanged source HEAD alone does not establish those facts.
5. Apply once through checkpoint `apply` at the captured current revision. On changed evidence/item/source/guards or a stale revision, recollect; do not rehash stale acknowledgments. Renewal replay, even the same payload/ID, rejects without replacement: read the accepted checkpoint to reconcile an ambiguous result.
6. Read back the renewal and empty command journal, then obtain a **separate** fresh exact continuation acknowledgment. Version 2 continuation must be checked at the immediately post-renewal checkpoint revision against the latest renewal, current item/main/source/scopes/fence and held guards. Any intervening checkpoint mutation requires a fresh renewal, not reuse.

The host remains responsible for actual compatibility/identity verification, consent and source-write enforcement. Renewal retains the original human-authorized fence and existing execution-authority limits; it is not a new human permission, generic readmission, incident admission for clean omissions or blocker resolution. Failed/unknown compatibility withholds continuation ([authority limits](./scripts/registry.cjs)).

#### Exact renewal input

Use the existing absolute ignored scratch checkpoint path. All nested fields below are mandatory; no bypass or optional compatibility flag exists. `source` retains original `{owner,worktree,branch,head}` with a fresh actual `receipt`; `scopes` must equal the original recovery's `toScopes`, with scope revision exactly original `scopeRevision+1` ([schema](./scripts/registry.cjs)).

| Object | Exact fields |
| --- | --- |
| checkpoint apply | `operation="apply"`, current numeric `expectedRevision`, `event`, independent supplied `renewalEvidence` |
| event | fresh `id`, `type="renew-recovery-continuation"`, `proof`, `acknowledgment` |
| proof | `version=1`, `repository`, `coordinator`, `checkpointRevision`, current main `head`, `key`, `owner`, `grantDigest`, `previousRenewalDigest`, `expectedItemDigest`, `scopeRevision`, `scopes`, `source`, `fence`, `quiescence`, `exclusivity`, `compatibility` |
| quiescence | `sourcePaused=true`, `commandsRunning=false`, `workJoined=true`, `pendingInstructions`, `processingInstructions`, fresh live `receipt`; counts equal the original reconciliation (`"UNKNOWN"` stays unknown) |
| exclusivity | `complete=true`, exact current `reservations=heldReservations(state,key)`, actual current inventory `receipt` |
| compatibility | `baseHead` original grant main, `fromHead` previous renewal main or original main, `toHead` current main, `ancestorVerified=true`, `complete=true`, canonical `changedPaths` for the whole base-to-current diff, `scopeReviewCompatible=true`, `sourceReviewCompatible=true`, actual review `receipt` |
| acknowledgment | fresh `id`, `receipt`, exact `owner`, `key`, current `scopeRevision`, `proofDigest=digest(proof)`, `workJoined=true`, `staleAuthorityRejected=true`, exact unchanged `fence` |
| renewalEvidence | `kind="host-verified-recovery-renewal"`, `coordinator`, `checkpointRevision`, `proofDigest`, actual `ownerResponseId`, `compatibilityDigest=digest(proof.compatibility)`, `sourceIdentityVerified=true`, `sourcePauseVerified=true`, `commandsJoinedVerified=true`, `instructionAuthorityVerified=true`, `compatibilityVerified=true`, independent actual readback `receipt` |

`grantDigest=digest(completeAcceptedRecoveryRecord)` preserves the original authority/history. `previousRenewalDigest` is `null` only for the first renewal of that grant; successive renewals bind `digest(completePreviousRenewalRecord)`. Each renewal record is exactly `{event,before,hostEvidence,commands:[]}`. Version 7 adds `state.recoveryRenewals` and reserved seen/digest markers without mutating any existing items or proofs ([persistence](./scripts/registry.cjs)).

Use exported `applyEvent(state,event,{revision,renewalEvidence})` only for deterministic verification; live persistence uses the checkpoint wrapper. The registry stdin CLI has no verified context and cannot apply renewal. Placeholder input outline (not a grant, not executable until independently observed typed fields replace every placeholder):

```text
{
  "operation": "apply",
  "expectedRevision": "<CURRENT_INTEGER>",
  "event": {
    "id": "<FRESH_RENEWAL_EVENT_ID>",
    "type": "renew-recovery-continuation",
    "proof": "<EXPAND_EXACT_PROOF_SCHEMA_ABOVE_WITH_CURRENT_OBSERVATIONS>",
    "acknowledgment": "<ACTUAL_FRESH_WHOLE_PROOF_OWNER_ACKNOWLEDGMENT>"
  },
  "renewalEvidence": "<INDEPENDENT_ACTUAL_HOST_READBACK_USING_EXACT_SCHEMA_ABOVE>"
}
```

```powershell
Get-Content -LiteralPath $RenewalInputPath -Raw |
    node $MergedCheckpointCli $CheckpointPath
```

Version 2 `validateRecoveryContinuation(state,request,{revision})` uses the exact version-1 request fields plus `renewalDigest=digest(latestAcceptedRenewalRecord)` and `version=2`. It binds the original `grantDigest` and unchanged source/fence, the current main/item and post-renewal revision. Its separate acknowledgment binds `recoveryContinuationDigest(request)` including `renewalDigest`; original recovery or any renewal acknowledgment cannot be reused ([function](./scripts/registry.cjs)).

Validation still returns `{coverageVerified:true,blockers:<current blocker>,sourceExecutionAuthorized:false}`. It neither consumes a host instruction nor authorizes/sends one; repeated read-only validation is not execution or a queue-drain claim. The host must deduplicate actual continuation delivery/consumption by exact message identity and revalidate the current grant/fence before writes ([transport](./SKILL.md#bootstrap-and-transport)).

#### Runnable synthetic renewal example

This exact in-memory example uses only [synthetic observations](./scripts/incident-recovery-fixture.cjs); it performs no host calls, Git commands, checkpoint writes or source continuation:

```javascript
const path = require('node:path');
const root = path.resolve('.github', 'skills', 'kora-orchestrate', 'scripts');
const { applyEvent, validateRecoveryContinuation } = require(path.join(root, 'registry.cjs'));
const { renewalFixture, renewalEvent, renewalContext, continuation } =
  require(path.join(root, 'incident-recovery-fixture.cjs'));
const before = renewalFixture();
const event = renewalEvent(before, 43);
const result = applyEvent(before, event, renewalContext(event));
const checked = validateRecoveryContinuation(result.state, continuation(result.state, 44, true), { revision: 44 });
console.log(JSON.stringify({ actions: result.actions, checked }));
```

Expected output includes `actions:[]`, `coverageVerified:true`, retained blockers and `sourceExecutionAuthorized:false`. Missing/foreign/stale compatibility, acknowledgment, source, scope, fence or exclusive guards reject. Original versions 1-6 and same-head behavior remain valid; failed renewal leaves the original incident/grant and file bytes unchanged ([regressions](./scripts/registry.test.cjs), [checkpoint tests](./scripts/checkpoint.test.cjs)).

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

## Active mutable-owner scope extension

`extend-scopes` is separate from permanent shrink/retirement. It applies only
to an attached **running, nonretired** owner in enabled delivery. Acceptance
reserves the exact additional union; it does not resume the worker, clear a
blocker, qualify runtime/effects, change any existing permission or authorize
a new outcome. The coordinator issues continuation **separately** after
reading the accepted grant. A retired owner can never use this transaction.
See the implementation in [registry.cjs](./scripts/registry.cjs), persistence
in [checkpoint.cjs](./scripts/checkpoint.cjs), and behavioral cases in
[registry.test.cjs](./scripts/registry.test.cjs).

### Exact extension input and acknowledgment

The checkpoint envelope remains
`{operation:"apply",expectedRevision:<current checkpoint revision>,event:<below>}`.
The version-1 event has exactly `id`, `type="extend-scopes"`, `owner`, `key`,
`expectedScopeRevision`, `proof`, `acknowledgment`. Every nested field is
required; unknown fields reject. This is a synthetic proof example, not an
actual host acknowledgment:

```json
{
  "version": 1,
  "owner": "exact-mutable-worker-chat",
  "key": "mutable-outcome",
  "reservation": {
    "outcome": "Original admitted mutable outcome",
    "kind": "feature",
    "pool": "ready",
    "head": "1111111111111111111111111111111111111111",
    "pr": null
  },
  "scopeRevision": 0,
  "fromScopes": ["src\\owned.cs"],
  "additionalScopes": ["tests\\owned.test.cs"],
  "toScopes": ["src\\owned.cs", "tests\\owned.test.cs"],
  "phase": "running",
  "sourceWritesRetired": false,
  "manifest": {
    "complete": true,
    "actual": ["src\\owned.cs"],
    "planned": ["tests\\owned.test.cs"]
  },
  "preservation": {
    "complete": true,
    "commits": [],
    "retained": [],
    "unpreserved": [{
      "path": "src\\owned.cs",
      "receipt": "Verified original dirty file retained in owned worktree"
    }]
  },
  "quiescence": {
    "paused": true,
    "pendingInstructions": 0,
    "receipt": "Fresh temporary source pause and instruction reconciliation"
  },
  "unionFrozen": true,
  "exclusivity": {
    "complete": true,
    "reservations": [],
    "receipt": "Verified exact current snapshot: no other holding owners"
  },
  "receipt": "Complete current owner/Git/host evidence, no permanent retirement"
}
```

`reservation` copies original item outcome/kind/pool/head/pr; `fromScopes`
copies the **live** scope list and revision. `additionalScopes` must be a
nonempty canonical unique list, disjoint from every original scope and each
other (no already-covered/ancestor/redundant additions). `toScopes` is exactly
`[...fromScopes,...additionalScopes]`, retaining every original scope and order.
Use the same Windows domain path validator and native digest CLI documented
above; platform-native module loading does not change scope semantics.

`manifest` and `preservation` use the shrink proof's exact nested shapes.
Their complete actual-write union must match, including committed, retained,
unpreserved and touched-then-restored paths. **Actual writes must already be
covered by fromScopes**; extension cannot legalize unauthorized writes.
All actual/planned paths must be covered by toScopes, and each additional scope
must cover an explicit planned write. Subsequent amendments
retain prior actual and planned manifests (planned paths may become actual).
Unpreserved dirty files remain retained, not permission to delete.

Version-1 `quiescence` requires a concrete temporary paused-source receipt and verified
zero queued/processing broad instructions. It is not `stoppedWriting` or
permanent retirement. `unionFrozen=true` acknowledges no writes beyond the
accepted union. The host/owner must prove those facts; unknown pending counts,
idle status or a historical pause cannot substitute for a fresh acknowledgment.

`exclusivity.reservations` must exactly equal exported
`heldReservations(state,key)` from [registry.cjs](./scripts/registry.cjs):
other slot-holding items, sorted by key, each
`{key,owner,scopes,scopeRevision,sourceWritesRetired}`. Legacy revision/retirement
are `0/false`; unattached owners have `owner:null`. Include published,
cleanup-blocked, retired and passive guarded scopes. Every held scope remains
conflict-protected. The reducer checks snapshot consistency and conflicts;
coordinator receipts must establish actual exclusive ownership.

`acknowledgment` has exactly `id`, `receipt`, `owner`, `key`, `scopeRevision`,
`proofDigest`, with the same whole-proof `digest(proof)` and actual fresh
owner-response requirements as shrink. IDs cannot replay across **either**
protocol. A stale checkpoint writer, owner, revision, current scopes/PR or
exclusivity snapshot rejects with no effects. Version-1 event replay remains idempotent.

### Explicit human-approved UNKNOWN extension

For a **non-incident** mutable closure omission, proof version 2 permits
UNKNOWN queue counts only through an explicitly established same-owner source
authority fence. All actual writes must still be covered by original scopes:
this is not incident recovery or retroactive authorization. Ordinary version-1
pending-zero behavior, shrink, cleanup, handoff and supersession remain unchanged
([verifiers](./scripts/registry.cjs), [regressions](./scripts/registry.test.cjs)).

The host must independently observe current source identity and preservation,
temporary pause, no running source commands, joined work and effective rejection
of all earlier broad source instructions. Sender-only pending lists, bounded
transcripts, idle state, message acceptance, fixture strings and digests do not
establish these facts or authenticate human consent. If actual enforcement or
pause/join cannot be established, withhold acknowledgment and continuation;
never infer a complete queue drain ([worker obligations](./worker.md#mutable-owner-extension-acknowledgment)).

The event retains every version-1 field and adds `authorization`. Proof version 2
retains the original proof fields except the replaced `quiescence` shape below;
each row lists exact required nested fields. Unknown fields reject
([implementation](./scripts/registry.cjs)):

| Object | Exact fields and constraints |
| --- | --- |
| Additional proof fields | `repository="roryprimrose/Kora"`, exact `coordinator`, current safe-integer `checkpointRevision`, current main `head`, `source`, `expectedItemDigest=digest(current complete item)`, `reconciliation` |
| `source` | `owner`, canonical absolute Windows `worktree`, `branch`, current source `head`, `receipt`; same owner, source head is original reservation head or a preserved commit. Source base remains distinct from current main. |
| Replaced `quiescence` | `paused=true`, `commandsRunning=false`, `workJoined=true`, `receipt`; independently observed, not inferred from queue counts. |
| `reconciliation` | `kind="human-approved-source-fence"`, `pendingInstructions="UNKNOWN"`, `processingInstructions="UNKNOWN"`, `fence`, `receipt` |
| `fence` | Positive integer `epoch`, fresh `nonce`, `revokesEarlierSourceAuthority=true`, `established=true`, `receipt` |
| `acknowledgment` | Original `id,receipt,owner,key,scopeRevision,proofDigest=digest(proof)`, plus `workJoined=true,staleAuthorityRejected=true,fence`; exact fresh owner response over the complete frozen proof and matching fence. |
| `authorization` | `kind="explicit-human-scope-extension-decision"`, fresh `id`, authenticated `actor`, `decision="prospective-coverage-only"`, `subjectDigest=digest({proof,acknowledgment})`, `receipt`; actual new human decision, not implementation approval. |
| Independent `extensionEvidence` | `kind="host-verified-fenced-scope-extension"`, matching `coordinator,checkpointRevision,proofDigest,ownerResponseId,humanDecisionId,humanDecisionDigest=digest(authorization)`, observed `sourceIdentityVerified=true,sourcePauseVerified=true,commandsJoinedVerified=true,instructionAuthorityVerified=true`, `receipt` |

`extensionEvidence` is a sibling of `event` in the checkpoint apply input,
not part of the event or an owner-authored substitute for host observation.
Reducer callers pass it separately as `{revision,extensionEvidence}`.
The stdin registry CLI has no independent evidence adapter and cannot accept
this transaction by itself. After independently collecting real facts, the
checkpoint CLI accepts this exact envelope
([checkpoint implementation](./scripts/checkpoint.cjs)):

```text
{operation:"apply",expectedRevision:<current>,event:<complete version-2 event>,
 extensionEvidence:<independent current host readback>}
```

```powershell
Get-Content -LiteralPath $ApprovedInputPath -Raw |
    node .\.github\skills\kora-orchestrate\scripts\checkpoint.cjs $CheckpointPath
```

Before that command, read the current checkpoint/complete item and all held
guards; inventory actual/planned/committed/retained/unpreserved/restored paths;
pause/join the exact source owner; establish the fresh revocation fence; freeze
the exact old-plus-additional union and full proof; obtain its exact-owner
acknowledgment; obtain the new human decision over both; independently read back
all observations. A changed source, item, revision, union, owner or exclusivity
snapshot requires fresh proof, acknowledgment and human authority. Never replace
unknown facts with `true` to fill this input
([validation and atomic replacement](./scripts/checkpoint.cjs)).

Acceptance migrates valid v1-v7 stores to **v8**, preserving all existing
history, publication, blockers, slots, cleanup and retired-owner guards.
The existing amendment index and `scope-amendment:v1:` digest marker now bind
the version-2 record
`{eventId,proof,acknowledgment,authorization,hostEvidence,before,fromVersion,commands:[]}`.
`before` is the complete prior item. Checkpoint event digest binds this entire
record, the immediately prior proof revision and empty commands; fabricated
actions, omitted history, schema downgrade and altered replay reject before
replacement. Version-2 event replay always rejects, even with unchanged payload.
There is **no** launch, resume, discovery action or blocker clearance. Later
extensions or recoveries cannot drop an accepted mutable fence with zero-count
claims: same source owner/worktree/branch, strictly increasing epochs and fresh
nonces/human decisions remain required
([persisted validators](./scripts/registry.cjs), [checkpoint tests](./scripts/checkpoint.test.cjs)).

#### Separate fenced-extension continuation

After durable acceptance, independently read current state and obtain a fresh
exact-owner whole-request acknowledgment. Exported
`validateScopeExtensionContinuation(state,request,{revision})` is read-only;
its exact request fields are:

```text
version:1, kind:"exact-extended-owner-continuation", coordinator, head,
source:{owner,worktree,branch,head}, checkpointRevision, key, owner,
expectedItemDigest, scopeRevision, coverageDigest, fence,
acknowledgment:{id,owner,requestDigest,coverageDigest,fence,
               workJoined:true,staleAuthorityRejected:true,receipt}
```

`coverageDigest=digest(complete latest version-2 amendment record)`;
`requestDigest=recoveryContinuationDigest(request)` excludes acknowledgment.
It binds current checkpoint revision, complete item, latest exact accepted scopes,
main/source identity, unchanged full held guards and accepted fence. Any later
scope change or newer fence invalidates the old continuation, including an old
incident-recovery continuation. Main/guard drift fails closed; this path adds no
renewal mechanism. Success returns
`{coverageVerified:true,blockers:<retained blocker>,sourceExecutionAuthorized:false}`.
The host must independently authenticate actual consent/owner response, enforce
the fence before every source operation, deduplicate actual delivery and issue
the separate narrowly authorized continuation. Repeated read-only validation
does not consume or authenticate an execution grant
([verifiers](./scripts/registry.cjs), [launch consumer](./scripts/worker-launch.cjs)).

This runnable example uses only synthetic in-memory facts. It prints exact input
shapes, not live proof; it does not touch any checkpoint, host owner or source
([fixture](./scripts/refinement-fixture.cjs)):

```javascript
const path = require('node:path');
const base = path.resolve('.github', 'skills', 'kora-orchestrate', 'scripts');
const { applyEvent, validateScopeExtensionContinuation } = require(path.join(base, 'registry.cjs'));
const { fencedExtensionFixture, fencedExtensionEvent, fencedExtensionContext,
  fencedExtensionContinuation } = require(path.join(base, 'refinement-fixture.cjs'));
const state = fencedExtensionFixture(), event = fencedExtensionEvent(state);
const context = fencedExtensionContext(event);
const accepted = applyEvent(state, event, context);
const request = fencedExtensionContinuation(accepted.state);
const result = validateScopeExtensionContinuation(accepted.state, request, { revision: 42 });
console.log(JSON.stringify({ operation: 'apply', expectedRevision: 41, event,
  extensionEvidence: context.extensionEvidence, actions: accepted.actions, request, result }));
```

### Mutable migration, sequencing and retained source

1. Read checkpoint and exact owner/current scope revision, preserve original
   publication/cleanup and all dirty files. Never edit checkpoint JSON manually.
2. Obtain complete original actual/planned/preservation evidence and temporarily
   pause source work; reconcile broad instructions. Derive the exact conflict-free
   additional union and held-owner snapshot. Obtain a fresh acknowledgment bound
   to the entire final proof, including union, identity, revision and digest.
3. Apply through the atomic checkpoint with current expected checkpoint/scope
   revisions. Preserve returned state before sends. Acceptance changes only
   scopes, scope revision and amendment metadata; no new resume/discovery/archive
   action is emitted by extension. Existing unrelated dispatch behavior remains.
4. Read the accepted grant, then issue explicit continuation to the exact same
   owner for the admitted outcome/union. Retain scope blockers until independently
   resolved. Do not infer effect permissions, runtime qualification or permanent
   retirement reversal. No additional feature writes happen in synthetic tests.

First accepted extension migrates valid registry **v1 or v2 to v3** atomically,
without changing existing shrink records, digests, markers, retirement or
publication evidence. Each item additionally requires `scopeAmendments:[]`;
top-level `amendments` indexes `{key,eventId,digest(record)}`. Each amendment
record is `{eventId,proof,acknowledgment}` with proof version 1.
`seen` retains its event and reserved `scope-amendment:v1:<digest(record)>`
marker; normal event IDs cannot use either scope marker prefix.

Live `scopeRevision` counts amendments **plus** shrink records. Amendments are
consecutive from 0, followed only by any later permanent shrink records.
`sourceWritesRetired` derives solely from nonempty shrink history, never from
mutable revisions. Valid v1/v2 states remain readable/launchable unchanged.
Later publication, cleanup, pause/resume and source refresh preserve amendments;
later shrink must retain all their actual/planned writes and retires source
permanently. No extension follows permanent retirement.

Checkpoint read/apply binds extension journal to amendment index in addition
to old shrink binding. Worker-launch validates the entire v3 store before
producing arguments, preserves expanded exclusions and cannot relaunch an
attached owner. Omitted/altered history, missing retirement/index/event markers,
schema downgrade, stale revisions, replay and inconsistency reject. Existing
exclusive locks and failed atomic replacement retain old coverage.
As before, deterministic hashes do not authenticate a coherently forged whole
store or prove host quiescence. Use only verified immutable source modules and
retain their source SHA/digest when a resumable coordinator loads this protocol;
shipping, loading or shadow migration is not a live grant.

## Same-key unowned candidate revalidation

`refresh` changes verified source freshness, not cached candidate scopes or
evidence. Do not replay `candidate`, invent a new key/outcome, amend an attached
owner, or overwrite saved JSON to repair an incomplete old admission.
`revalidate-candidate` instead replaces only the existing unowned candidate's
head, complete scopes and evidence. Key/outcome/kind/pool/PR and all unrelated
owners, writes, slots, blockers, cleanup/publication and scope histories remain.

Only enabled delivery with the exact **active assigned planner owner/pass**
can accept this event. The target must be `phase=candidate`, `owner=null`,
unpublished, unreserved, nonretired and without any scope-write history or
cleanup/source-write authority. Current source must equal the event head.
Every original scope remains covered by the canonical nonempty unique final
manifest. The coordinator freshly verifies the **whole** complete operation,
required production/test wiring and evidence against that source; old cached
evidence or a source refresh alone does not establish completeness.

Any overlap with **any held owner**, including a permanently retired owner's
passive guards, or the same held PR rejects without mutation or new action.
Stopped source work, empty roots, failed cleanup and spare capacity do not
release a guard. Ordinary lifecycle completion or separately authorized
[exact-path supersession](./README.md#exact-path-write-reservation-supersession) is required
to change effective exclusion; supersession never releases historical ownership.
No variant, renamed outcome, new worker or live event is
authorized by a published protocol repair.

### Exact atomic client input

Read the current checkpoint using `{"operation":"read"}`. Pin its revision,
verified source, exact active planner/pass and complete existing item.
Compute `expectedItemDigest` using the documented native `node:path` digest
program above on that **entire item**, including all saved metadata and any
`admissionHistory`, not a key/scopes projection. Supply fresh full scopes and
evidence. The event has exactly the fields in this synthetic example; replace
the illustrative digest/source/identity/manifest with verified current inputs:

```json
{
  "operation": "apply",
  "expectedRevision": 107,
  "event": {
    "id": "revalidate-final-manifest-once",
    "type": "revalidate-candidate",
    "owner": "fixture-planner",
    "pass": 1,
    "key": "fixture-candidate",
    "expectedItemDigest": "0000000000000000000000000000000000000000000000000000000000000000",
    "head": "1111111111111111111111111111111111111111",
    "scopes": ["src\\fixture.cs", "tests\\fixture.test.cs"],
    "evidence": "Fresh verified whole operation and complete production/test write manifest.",
    "manifestComplete": true
  }
}
```

Pass the approved absolute scratch input/checkpoint paths through the existing
PowerShell client, with no separate wrapper or direct checkpoint edits:

```powershell
Get-Content -LiteralPath $InputPath -Raw |
    node .\.github\skills\kora-orchestrate\scripts\checkpoint.cjs $CheckpointPath
```

The exclusive lock and current `expectedRevision` bind source/item inspection
to atomic replacement. Wrong/stale source/item/planner/pass, unknown fields,
incomplete/noncanonical/duplicate/traversing/glob/empty scopes or no-op input
reject. A failed write/rename preserves the previous bytes and guards.
An exact accepted event replay is idempotent with no revision/history/action
change, even after attachment. Changed payload under that ID, an old candidate
event ID, or a new-ID replay with the old digest rejects. A genuine successive
revalidation needs the new complete item digest and current planner/source.

Successful replacement runs only existing guarded dispatch: a disjoint
feature may reserve one available slot; maintenance still waits for pass end,
and blockers/capacity still apply. Persist and reconcile the resulting pending
launch action before ordinary worker attachment. Revalidation itself supplies
no owner source authority, runtime qualification, effect grant, cleanup proof
or retired-source resurrection; there is no unowned worker to acknowledge.

### Versioned persistence and compatibility

First acceptance atomically migrates valid **v1/v2/v3 to v4**. Untouched older
stores remain readable without migration. Every v4 item requires
`admissionHistory:[]`; top-level `readmissions` indexes `{key,eventId,digest}`.
Each record is `{event,before,fromVersion}`: `before` is the exact old item
without `admissionHistory`, whose prior prefix is reconstructed for v4
complete-item digest validation. First-record legacy shape follows
`fromVersion`; successive records require v4 or v5. No original shrink/amendment
record, marker, acknowledgment, revision or retirement changes.

`seen` binds the accepted ID and reserved
`candidate-revalidation:v1:<digest(record)>` marker. Registry read/apply and
worker launch reject missing/altered history, index, digests, markers, replay,
schema downgrade or inconsistent current admission. Checkpoint read/apply also
binds each event journal entry to the index. Later ordinary attachment,
publication, scope transactions and cleanup retain admission history.

These deterministic hashes establish shape/consistency, not fresh host/Git
facts or authentication of a coherently forged whole store. The frozen seven
final-manifest/v3-history fixtures are data-only shadow evidence. Their
synthetic successful cleanup does **not** prove any live owner's physical
closure. Load a pinned verified canonical module and obtain separately
authorized fresh discovery/source/cleanup inputs before any future live apply.

## Exact-path write-reservation supersession

The [registry reducer](./scripts/registry.cjs) accepts `supersede-write-reservations` in enabled delivery mode through the [durable checkpoint client](./scripts/checkpoint.cjs).
It separates future exact-file write exclusion from immutable historical scope/publication/source-retirement records and retained slots/cleanup. It does not implement external authority transfer, release capacity or perform host operations.

### Supported activation and nonactivating preparation

- `mode=activate-local`: all overlapping holders must be in the controlling local ledger, with single-ledger authority, complete preservation, stopped-work/instruction reconciliation and exact holder acknowledgments or authorized revocation receipts. The [reducer](./scripts/registry.cjs) derives retirement without changing the old flag. No external snapshot may activate.
- `mode=prepare-external`: retain complete external snapshots and every overlapping holder. Unknown host evidence remains null, never inferred pending-zero or acknowledgment. The [reducer](./scripts/registry.cjs) persists the proposal but never reserves/attaches/launches its candidate. External guards remain; this is not resolved feature ownership.
- External activation requires a separately supported authoritative single-ledger handoff protocol. There is no promotion, two-checkpoint commit, live migration or prepared-proposal activation route in this version. Preparing, publishing or testing this code does not execute such a protocol ([checkpoint client](./scripts/checkpoint.cjs)).

### Exact input contract

Use the existing absolute ignored scratch checkpoint client with `operation=apply` and current `expectedRevision`. The event has exactly `{id,type,proof,authorization}`, where `type=supersede-write-reservations`. Pin the complete source modules and verify facts before constructing any input; the [validator](./scripts/registry.cjs) checks the following exact field sets.

| Object | Exact fields and requirements |
| --- | --- |
| Version-1 `proof` | `version=1`, `repository="roryprimrose/Kora"`, exact `coordinator`, `checkpointRevision`, current verified `head`, `mode`, `authority`, `exactFiles`, complete `candidate`, `expectedItemDigest`, `manifestComplete=true`, `localReservations`, `externalCheckpoints`, `holders`. |
| `authority` | `singleLedger=true` only for local activation; false for external preparation. Nonempty `receipt` identifies independently verified controlling source authority, not a snapshot-based claim to it. |
| `exactFiles` | `complete=true`, canonical unique `paths` equal the first-occurrence union of all holder paths, and `receipt` establishing these are exact files, not directories. Filesystem kinds are a trusted-host fact, not inferred from extensions. |
| `candidate` | Entire current unowned, unreserved, unpublished, nonretired candidate item, including all metadata/history. `expectedItemDigest=digest(candidate)`, head equals proof head and blocker is null. Independent product/effect admission remains required. |
| `localReservations` | Entire current ordered array of all slot-holding items, not a projection. Acceptance compares it exactly; ownership, publication anchors, scopes, history and blockers are not rewritten. Unrelated unattached reservations are retained; an overlapping unattached reservation blocks acceptance. |
| `externalCheckpoints[]` | Exactly `repository`, positive `revision`, complete validated `state`, `stateDigest=digest(state)`, and `receipt`. Coordinators must be distinct from each other/local. Nested external authority imports reject; local-only v5 histories in an external snapshot retain their shared effective exclusions. No external file is written. |
| `holders[]` | Exactly `coordinator`, `key`, `owner`, complete original `itemDigest`, `scopeRevision`, exact `paths`, `manifest`, `preservation`, `stopped`, `acknowledgment`. List every overlap in local snapshot order, then external snapshot/item order, including multiple ledgers holding the same path. Missing, duplicate, reordered or mismatched holders reject. |
| `manifest` / `preservation` | Existing complete actual/planned and commit/retained/unpreserved structures from the shrink-only protocol; every historical actual/planned path remains accounted for, original publication is preserved, and `unpreserved` must be empty for activation. Receipt-backed retained local bytes may be preserved without inventing publication. |
| `stopped` | Exactly `writingStopped=true`, `pendingInstructions=0`, `sourceInstructionsCleared=true`, and independent concrete `receipt`. Reconcile source-authorizing queued and processing instructions; idle/archived/merged/empty-directory states are not proof. |
| `acknowledgment` | Exactly `kind=owner` or `revocation`, unique fresh `id`, concrete `receipt`, and `proofDigest=supersessionAcknowledgmentDigest(proof,holder)`. |
| Unknown external evidence | In preparation only, `manifest`, `preservation`, `stopped` and `acknowledgment` may all be explicit `null`. Partial/unknown activation rejects. Otherwise complete proof is validated even though preparation remains nonactivating. |
| `authorization` | Exactly `humanAuthorized=true`, concrete explicit human authority `receipt`, and `proofDigest=digest(proof)`. Bind the exact complete multi-holder operation, not a blanket ancestor grant or approval of this design. |

The [acknowledgment helper](./scripts/registry.cjs) binds the entire proof, excluding all acknowledgment objects to avoid a circular digest, plus the exact holder identity. Changes to any holder, snapshot, source, authority or candidate invalidate every acknowledgment. Revocation requires explicit authority plus independent quiescence, never an unavailable owner's fabricated response.

`paths` must equal both an effective prior scope token and a candidate token, with no overlapping ancestor/descendant token. The reducer never carves a file out of a directory reservation or interprets a directory grant as a set of transferred descendants. The host must verify `exactFiles` kinds; canonical strings/digests cannot do that ([scope derivation](./scripts/registry.cjs)).

The pure reducer requires a host-supplied third argument `{revision}` for this event; ordinary two-argument routes remain unchanged. Production clients use `checkpoint(...,{operation:"apply",expectedRevision,event})` with the locked revision. The registry JSON CLI cannot apply supersession without that durable context ([clients](./scripts/checkpoint.cjs)).

### History, dispatch and retained lifecycle

First acceptance upgrades valid v1-v4 to v5, preserving prior schemas/history with their existing empty migration metadata. Unaccepted older stores remain unchanged.
Each `supersessions[]` record stores `{event,commands}`; `seen` binds the event and `write-supersession:v1:<digest(record)>`. The [checkpoint journal](./scripts/checkpoint.cjs) binds the full record and exact generated commands before returning any action.

Version-1 exact accepted replay at the current checkpoint revision changes no bytes, actions or revision. Changed payload/type, stale item/snapshot/source/revision, missing history/journal/commands, lock contention or replacement failure rejects without replacement. Hashes detect inconsistent edits, not a coherently forged whole store ([regressions](./scripts/checkpoint.test.cjs)).

Shared `heldReservations(state,key)` includes effective local exclusions, nonactivating external snapshots and a beneficiary-specific transferred-file fence. Empty effective scopes retain their slot/owner.
Only the exact beneficiary can acquire transferred writes even if it waits for capacity, a rival appears first, or the prior holder completes cleanup. Validation, dispatch, adoption, revalidation, extension and [worker launch](./scripts/worker-launch.cjs) consume this derivation.

The fence remains while the beneficiary is waiting, reserved, running, published or blocked. Only its valid accepted `completed` cleanup ends that future fence, allowing independently admitted later same-path work.
Local and external snapshot consumers use the same release rule; historical transfer records and permanent prior-writer retirement remain unchanged ([cleanup regressions](./scripts/registry.test.cjs)).

Supersession freezes participant source identity/scopes/history; `refine-scopes`, `extend-scopes` and candidate revalidation cannot mutate it or revive a prior writer. A changed beneficiary/source requires fresh supported authority; this version has no participant amendment route.
Ordinary truthful later publication, explicit blockers and actual cleanup remain supported; they never erase supersession history or restore prior source writes ([lifecycle regressions](./scripts/registry.test.cjs)).

Generated launch prompts distinguish historical reservations from future exclusions and permanent derived retirement. External preparation emits no beneficiary launch; fabricated actions and adoption under a different key cannot bypass its guards.
Tests establish these local contracts, not external inventory freshness, consent, quiescence, transport delivery, global enforcement or host configuration ([launch regressions](./scripts/worker-launch.test.cjs)).

### Executable synthetic example

This example uses only in-memory test data and makes no live checkpoint, session, source or authority changes. It exercises the [shared fixture](./scripts/refinement-fixture.cjs), not a ready-to-apply human receipt.

```javascript
const path = require('node:path');
const scripts = path.resolve('.github', 'skills', 'kora-orchestrate', 'scripts');
const { applyEvent, heldReservations } = require(path.join(scripts, 'registry.cjs'));
const { supersessionFixture, supersessionEvent } = require(path.join(scripts, 'refinement-fixture.cjs'));
const before = supersessionFixture();
const event = supersessionEvent(before, 41);
const result = applyEvent(before, event, { revision: 41 });
console.log(JSON.stringify({
  version: result.state.version,
  launch: result.actions.filter(action => action.type === 'launch'),
  retained: heldReservations(result.state, 'successor').map(item => item.scopes)
}));
```

Run the three focused modules and the combined maintained policy selector under [deployment acceptance](./README.md#deployment-acceptance). Tests exercise synthetic complete proofs and explicit unknowns; they cannot authenticate supplied receipt strings or establish an unavailable external protocol.

### Human-fenced local holder handoff

Proof version 2 is an explicit opt-in alternative for retained **published** local holders when complete queue enumeration is unavailable. It never coerces UNKNOWN to zero, invents `sourceInstructionsCleared`, fabricates an incident or authorizes past effects.
Version-1 verified-zero activation/revocation and nonactivating external preparation remain unchanged ([validator](./scripts/registry.cjs)).

Only one independently verified controlling local ledger may activate this path. Every overlapping holder must cooperatively stop its entire source identity, join owned commands and establish a fresh authority-revocation fence rejecting **all** earlier broad source grants. Require each actual whole-proof owner response; version 2 adds no UNKNOWN revocation route ([worker contract](./worker.md)).

The host must independently authenticate the new human whole-proof decision and read back each holder's exact source, complete preservation, stopped/joined work and effective stale-authority rejection.
Idle/merged/UI/sender-pending state, accepted transport, fixtures, hashes and implementation approval prove none of these facts. Missing observations withhold handoff; no queue-drain or OS-enforcement claim is made ([verifier](./scripts/registry.cjs)).

Keep the existing `{id,type:"supersede-write-reservations",proof,authorization}` event. All version-1 proof fields remain, with `version=2`, `mode="activate-local"`, `authority.singleLedger=true` and `externalCheckpoints=[]`; the following exact additions/replacements apply ([implementation](./scripts/registry.cjs)):

| Object | Exact version-2 fields and constraints |
| --- | --- |
| Additional proof fields | `expectedStateDigest=digest(complete current state)`, `effectiveReservations=heldReservations(state,candidate.key)`, `beneficiaryManifest` |
| `beneficiaryManifest` | `complete=true`, `actual=[]`, `planned` exactly equals complete candidate `scopes` in order. Entire admitted operation/evidence, current-source head and metadata remain in `candidate`. |
| Each holder, additions | `source`, `reconciliation`; all original holder identity/digest/revision/exact paths/manifest/preservation remain required, without dropping restored or historical actual/planned paths. |
| `source` | `owner`, canonical absolute Windows `worktree`, `branch`, current preserved source `head`, `receipt`; head is original item head or a preserved commit. It is not necessarily current main. |
| Replaced `stopped` | Exactly `writingStopped=true`, `commandsRunning=false`, `workJoined=true`, `receipt`; no `pendingInstructions` or `sourceInstructionsCleared` property. |
| `reconciliation` | Exactly `kind="human-approved-source-fence"`, `pendingInstructions="UNKNOWN"`, `processingInstructions="UNKNOWN"`, `fence`, `receipt` |
| `fence` | Exactly positive integer `epoch`, fresh `nonce`, `revokesEarlierSourceAuthority=true`, `established=true`, `receipt`; newer than any prior same-owner fence, same known worktree/branch identity, globally fresh nonce. |
| Replaced holder acknowledgment | Exactly `kind="owner"`, fresh `id`, `receipt`, `proofDigest=supersessionAcknowledgmentDigest(proof,holder)`, exact `owner`, `workJoined=true`, `staleAuthorityRejected=true`, matching `fence` |
| Extended human `authorization` | Existing `humanAuthorized=true,receipt,proofDigest=digest(proof)`, plus `kind="explicit-human-reservation-handoff-decision"`, fresh `id`, authenticated `actor`, `decision="retire-prior-writers-for-exact-beneficiary"` |

Every owner acknowledgment binds the complete multi-holder proof, excluding all acknowledgment objects through the existing digest helper. The human decision binds the final proof **including all holder acknowledgments**.
Any changed union, holder, source, preservation, fence, candidate, revision or guard requires fresh complete responses and human authority ([digest implementation](./scripts/registry.cjs)).

Independent `supersessionEvidence` is a sibling of `event` in checkpoint input, not owner-authored event data. Its exact required fields are ([checkpoint client](./scripts/checkpoint.cjs)):

```text
kind:"host-verified-reservation-handoff", coordinator, checkpointRevision,
proofDigest, humanDecisionId, humanDecisionDigest:digest(authorization),
humanAuthorizationVerified:true, ledgerAuthorityVerified:true,
beneficiarySourceVerified:true, holders, receipt
```

`holders` has exactly one observed entry per proof holder, in the same order, with these exact fields ([host-readback validation](./scripts/registry.cjs)):

```text
coordinator, key, owner, itemDigest, ownerResponseId,
sourceDigest:digest(holder.source),
preservationDigest:digest({manifest:holder.manifest,preservation:holder.preservation}),
fenceDigest:digest(holder.reconciliation.fence), sourceIdentityVerified:true,
preservationVerified:true, sourceStoppedVerified:true, commandsJoinedVerified:true,
instructionAuthorityVerified:true, receipt
```

Collect current checkpoint/source/beneficiary/full item-history/effective-guard inventories, verify preservation, actually stop/join and establish all cooperative holder fences, freeze the proof, obtain every whole-proof owner response, obtain the separate exact live human decision, then independently read back all facts.
A supplied receipt or `true` flag alone authenticates nothing ([authority boundary](./scripts/registry.cjs)).

For actual separately approved operation only, use the native checkpoint CLI with the complete input below. Reducer callers pass `{revision,supersessionEvidence}` separately; the stdin registry CLI has no host-evidence adapter and cannot apply version 2 ([client](./scripts/checkpoint.cjs)):

```text
{operation:"apply",expectedRevision:<current>,event:<complete version-2 event>,
 supersessionEvidence:<independent current host observations>}
```

```powershell
Get-Content -LiteralPath $ApprovedInputPath -Raw |
    node .\.github\skills\kora-orchestrate\scripts\checkpoint.cjs $CheckpointPath
```

Acceptance migrates valid v1-v8 stores to **v9**, preserving all prior protocols and v8 mutable fences. The new record is exactly `{event,commands:[],before,hostEvidence}`, where `before` is the complete validated pre-transaction state; existing event/index/marker and atomic checkpoint journal bind the full record.
Version-2 replay, altered history, downgrade or fabricated actions reject before replacement ([tests](./scripts/checkpoint.test.cjs)).

Acceptance emits **no actions**, leaves the beneficiary an unowned candidate and retains planner state. Only its exact future paths are reserved through the existing beneficiary-specific guard. It grants no source execution, clears no blocker and releases no slot/cleanup or remaining exclusion.
Retire each entire prior source identity permanently, not only transferred paths or an old HEAD ([shared derivation](./scripts/registry.cjs)).

After reading accepted state, separate current execution authority must precede an ordinary guarded dispatch event. For example, a separately authorized `refresh` with freshly verified current head can produce the existing recorded launch action.
The host then validates it through `workerLaunch`, creates/attaches the exact new owner and verifies actual worktree/source/ownership before writes; neither helper calls the host automatically ([launch consumer](./scripts/worker-launch.cjs)).

Old mutable/recovery continuations cannot revive retired holders. Historical scopes, publication, retained bytes, blockers, slots, cleanup and all old amendment/recovery/fence records survive.
Only ordinary verified-zero beneficiary cleanup ends its future guard; prior-writer retirement and untransferred exclusions survive. External preparation is never this UNKNOWN alternative ([regressions](./scripts/registry.test.cjs)).

This runnable example uses only in-memory synthetic data; it prints an input shape, never a live human grant or physical-enforcement receipt ([fixture](./scripts/refinement-fixture.cjs)):

```javascript
const path = require('node:path');
const scripts = path.resolve('.github', 'skills', 'kora-orchestrate', 'scripts');
const { applyEvent } = require(path.join(scripts, 'registry.cjs'));
const { supersessionFixture, fencedSupersessionEvent,
  fencedSupersessionContext } = require(path.join(scripts, 'refinement-fixture.cjs'));
const before = supersessionFixture(), event = fencedSupersessionEvent(before);
const context = fencedSupersessionContext(event);
const accepted = applyEvent(before, event, context);
console.log(JSON.stringify({ version: accepted.state.version, actions: accepted.actions,
  beneficiaryPhase: accepted.state.items.find(item => item.key === 'successor').phase,
  input: { operation: 'apply', expectedRevision: 41, event,
    supersessionEvidence: context.supersessionEvidence } }));
```
