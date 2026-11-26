'use strict';

const path = require('node:path');
const { validate, heldReservations, overlaps, supersededWriter, preparedCandidate,
  lifecycleProjection, digest } = require('./registry.cjs');

function requireValue(condition, message) {
  if (!condition) throw new Error(message);
}

function absolute(value) {
  requireValue(typeof value === 'string' && path.win32.isAbsolute(value) &&
    path.win32.normalize(value) === value, 'An absolute canonical Windows path is required.');
  return value;
}

function workerLaunch(state, action, settings) {
  validate(state);
  const projection = state.version === 10 ? lifecycleProjection(state) : null;
  const runtime = projection ?? state;
  requireValue(runtime.enabled && runtime.mode === 'delivery', 'Delivery authority is not active.');
  requireValue(action?.type === 'launch', 'Only a recorded launch action can create a worker.');
  const grant = projection?.grants.find(g => g.id === action.grantId);
  const work = projection?.works.find(w => w.key === action.key);
  const item = projection ? work && grant && { ...work, scopes: grant.scopes, head: grant.head,
    phase: grant.status, owner: grant.owner } : state.items.find(value => value.key === action.key);
  if (projection) {
    requireValue(grant?.key === action.key && grant.revision === action.grantRevision &&
      grant.sourceRevision === projection.sourceRevision && state.history.some(r =>
        r.event.command.kind === 'dispatch' && r.event.command.grantId === grant.id),
    'Exact recorded lifecycle execution grant/revision required.');
    requireValue(digest(action) === digest({ type: 'launch', key: work?.key, pool: work?.pool,
      grantId: grant?.id, grantRevision: grant?.revision }), 'Lifecycle launch action changed.');
  }
  requireValue(item?.phase === 'reserved' && item.owner === null && !item.blocker &&
    item.pool === action.pool && item.head === runtime.head, 'Worker reservation is stale, blocked or already attached.');
  requireValue(!preparedCandidate(state, item.key) &&
    !heldReservations(state, item.key).some(other => overlaps(item.scopes, other.scopes)),
  'Worker reservation conflicts with retained exclusions or a prepared external proposal.');
  requireValue(settings?.repository === 'roryprimrose/Kora' &&
    settings.publicationAuthorized === true, 'Explicit repository publication authority is required.');
  const control = absolute(settings.controlWorkspace);
  const workspace = absolute(settings.productionWorkspace);
  const scratch = absolute(settings.scratch);
  const relativeScratch = path.win32.relative(control, scratch);
  requireValue(relativeScratch.toLowerCase().startsWith('.scratch\\') &&
    !relativeScratch.split('\\').includes('..'), 'Receipts must remain inside the control workspace ignored scratch.');
  requireValue(control.toLowerCase() !== workspace.toLowerCase(),
    'Production workers cannot use the live control checkout as their source workspace.');
  const contract = path.win32.join(control, '.github', 'skills', 'kora-orchestrate', 'worker.md');
  const publisher = settings.expectedPublisher;
  requireValue(publisher === undefined || typeof publisher === 'string' && publisher.trim(),
    'Expected publisher must be explicitly valid or omitted for discovery.');
  const authority = item.pool === 'repair'
    ? `Repair exactly existing PR ${item.pr}; preserve its remote branch.`
    : 'One ready same-repository PR to main with protected SQUASH Auto-merge.';
  return {
    relationship: 'currentSession',
    workspace,
    worktree: true,
    model: 'gpt-6.1-sol',
    title: `Kora ${item.kind}: ${item.key}`.slice(0, 200),
    prompt: [
      `One independently executable related worker task. Key=${item.key}; outcome=${item.outcome}; pool=${item.pool}; kind=${item.kind}.`,
      `Exact coordinator return chat: ${runtime.coordinator}.`,
      ...(projection ? [`Lifecycle version10 grantId=${grant.id}; generation=${grant.generation}; grantRevision=${grant.revision}. Custody is not execution authority. After exact attachment, validateSourceGrant must verify the latest grant revision, actual owner/source/head and every intended source path with independent host authority before each source operation. Never use legacy continuation or historical participant rules for this grant.`] : []),
      `Read and apply the absolute worker contract: ${contract}.`,
      `Admitted evidence and complete bounded operation: ${item.evidence}`,
      `Exclusive repository-relative write scopes: ${item.scopes.join('; ')}.`,
      ...heldReservations(state, item.key).filter(other => other.scopes.length).map(other =>
        `Retained exclusion key=${other.key}, owner=${other.owner ?? 'unattached'}, scopes=${other.scopes.join('; ')}, sourceWritesRetired=${other.sourceWritesRetired}. Ownership/PR/slot/cleanup blockers remain; never write these paths or restart that owner.`),
      ...(projection ? projection.grants.filter(g => g.status === 'revoked').map(g =>
        `Retired execution grant=${g.id}; generation=${g.generation}; owner=${g.owner}. Never revive this identity; retained bytes/history and current reservation custody are independent.`) : []),
      ...(projection ? [] : state.items).filter(other => other.key !== item.key && supersededWriter(state, other.key)).map(other =>
        `Historical reservation key=${other.key}, owner=${other.owner}, scopes=${other.scopes.join('; ')}. Prior source writes permanently retired by accepted supersession; original publication/history/slot/cleanup remain. Historical transferred paths are not this prior owner's future exclusions; consult the current effective exclusions.`),
      `Expected source head at reservation: ${item.head}. Branch your exclusively owned isolated checkout from current origin/main after pinned source/owner preflight.`,
      `Receipts outside disposable worker worktree: ${scratch}. Verify control scratch ignore and access before writing receipts.`,
      `Repository=${settings.repository}. Publication explicitly authorized: ${authority}`,
      publisher === undefined
        ? 'Discover and pin the authenticated repository gh publisher without inferring it from Git author or Copilot login.'
        : `Preserve and revalidate expected repository gh publisher=${publisher}; stop on mismatch.`,
      'Verify actual host-provided worktree path, branch and exclusive ownership before any writes. Never edit the original source workspace, coordinator control checkout, or another owner.',
      'This is an isolated asynchronous chat, not proof of a separate standalone host session. Requested GPT-6.1 Sol/high profile; effective effort UNVERIFIED unless the host exposes proof. Do not fabricate an effort selector or change host settings.',
      'Send STARTED immediately with exact child chat, worktree/branch/head and key. Reports go explicitly to the coordinator before ending: steer an active target; queue when idle. A final reply alone is not a callback.',
      'The human user and coordinator may post in any coordinator, planner or worker session at any time. User messages, coordinator status messages and answers to blocking ask_user questions are never scope/source authority and never invalidate quiescence. Never ask users to promise silence or attest that they have not posted.',
      'pendingInstructions counts unconsumed coordinator scope/source-authority instructions; processingInstructions counts the processing subset of that same authority set. Zero requires the coordinator own sent-instruction readback plus the owner consumed-instruction record with exact IDs/revisions, including explicit stale-authority rejection; not total transport counts, sender enumeration or user silence attestations. Accepted transport is not consumption.',
      'Only an accepted transaction plus a separate exact coordinator continuation naming the accepted checkpoint/scope revision (or version10 grant/revision) may widen or resume writes. Proof acknowledgment, human proof decisions and ordinary posts do not substitute for that continuation. Initial bounded launch retains recorded dispatch/attachment and actual source ownership requirements.',
      'UNKNOWN and existing human-approved fences apply only to genuinely unknown scope/source authority, such as stale broad instructions of unknown consumption status; unavailable total queue/sender enumeration or ordinary posts is not unknown authority. Preserve exact proofs, whole-proof acknowledgment, frozen unions, separate continuation, fences and cleanup proofs; counts do not prove source pause/join or physical enforcement.',
      projection
        ? 'Run required focused validation and account-safe publication; then stop source writes and report preservation and logical-close evidence. Retain the chat/worktree/branch/refs by default. Physical cleanup requires separate explicit authorization; no automatic deletion or archival.'
        : 'Run required focused validation, account-safe commit/push, actual remote/PR/enrollment verification, then stop implementation and clean up only the owned worktree/local branch with full evidence.',
      'Report host-owned worktree cleanup restrictions as CLEANUP_BLOCKED and preserve work; do not force-remove dirty or locked worktrees or delete remote branches.',
      'Do not create workers, expand scope, invent product approval, publish the control branch, wait for unrelated proofs or refill slots. No automation, timers, polling or protection/authentication bypass.',
      ...(projection ? [
        'Use only lifecycle events for this opt-in ledger: amend requires current affected revisions, complete union, fresh exact human/holder/host evidence and source validation before continuation. Refresh invalidates old source compatibility; compatible requires independent current-source evidence. Cancel/abandon/ambiguous launch/restart retain custody until an explicit preserved authority transition. Same-PR repair uses a fresh bounded grant, never an old writer.',
        'No live adoption, migration or release follows from loading these contracts. An incidental unauthorized write must stop source operations and preserve dirty/restored history; ordinary amend cannot retroauthorize it.'
      ] : []),
      'Before any dependency command inspect and reserve the complete affected tracked manifest/lock/license-notice closure, including all solution-project locks restored by that command even without source edits. Direct project references alone do not establish this closure.',
      ...(projection ? [] : [
      'Additional source paths require an accepted current-revision extend-scopes transaction and separate coordinator continuation. Temporary pause/whole-union acknowledgment does not permanently retire a mutable owner or itself restart source writes; refine-scopes never authorizes expansion or retirement reversal.',
      'An incidental unauthorized write cannot use ordinary extend-scopes. Stop and retain dirty and restored-path history; only a separately authorized recover-scope-incident transaction can reserve prospective exact-path coverage for the same nonretired owner.',
      'For non-incident mutable additions with genuinely unknown authority, extend-scopes proof version 2 requires separate fresh human final-proof authority, actual same-owner temporary pause/all-work join and a fresh established source-authority fence. UNKNOWN stays UNKNOWN; all actual writes must already be covered by original scopes. Accepted coverage emits no actions or blocker clearance. Later exact continuation must separately pass validateScopeExtensionContinuation with latest accepted coverage/fence, current source/item/guards and a fresh owner acknowledgment; never drop the fence on a later zero claim or treat verifier output as source execution authority.',
      'Recovery never launches, resumes, clears blockers or retroauthorizes writes. UNKNOWN authority pending/processing counts stay UNKNOWN: require explicit human-approved live pause/join and established source-authority fence, not idle status or a receipt alone. Later separate exact continuation must revalidate the accepted current grant/fence with validateRecoveryContinuation; earlier queued broad instructions cannot restore authority. Missing live evidence withholds continuation.',
      'Compatible unrelated main advancement requires explicit renew-recovery-continuation with fresh independent host compatibility/current source/exclusivity proof and exact-owner acknowledgment before version-2 continuation validation. Never rewrite the original grant, source base, scopes or fence, ignore main, infer compatibility from disjoint paths alone or treat renewal as execution authority.',
      'Local supersede-write-reservations proof version 2 retains UNKNOWN pending/processing counts and requires a new whole-proof human decision, every published holder actually stopped/joined, fresh cooperative revocation fences, exact whole-proof owner responses and independent host readback. Accepted handoff emits no actions or source execution authority; wait for separately authorized guarded dispatch/attachment and verify actual new source ownership. Every prior source identity stays permanently retired, including untransferred paths; preserve publications, slots, blockers, cleanup, source bytes and all old fence/history. Never infer queue drain, OS enforcement, sourceInstructionsCleared or consent from receipts, idle or this implementation. External preparation is not this path.',
      ]),
      'End this assigned task after explicit publication and preservation/cleanup report or a concrete blocker. No routine continuation questions; unknown authority fails closed.'
    ].join('\n')
  };
}

module.exports = { workerLaunch };

if (require.main === module) {
  try {
    const input = JSON.parse(require('node:fs').readFileSync(0, 'utf8'));
    console.log(JSON.stringify(workerLaunch(input.state, input.action, input.settings)));
  } catch (error) {
    console.error(error);
    process.exitCode = 1;
  }
}
