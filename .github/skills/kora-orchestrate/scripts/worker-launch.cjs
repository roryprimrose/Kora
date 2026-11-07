'use strict';

const path = require('node:path');
const { validate } = require('./registry.cjs');

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
  requireValue(state.enabled && state.mode === 'delivery', 'Delivery authority is not active.');
  requireValue(action?.type === 'launch', 'Only a recorded launch action can create a worker.');
  const item = state.items.find(value => value.key === action.key);
  requireValue(item?.phase === 'reserved' && item.owner === null && !item.blocker &&
    item.pool === action.pool && item.head === state.head, 'Worker reservation is stale, blocked or already attached.');
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
      `Exact coordinator return chat: ${state.coordinator}.`,
      `Read and apply the absolute worker contract: ${contract}.`,
      `Admitted evidence and complete bounded operation: ${item.evidence}`,
      `Exclusive repository-relative write scopes: ${item.scopes.join('; ')}.`,
      ...state.items.filter(other => other.key !== item.key &&
        !['candidate', 'complete'].includes(other.phase)).map(other =>
        `Retained exclusion key=${other.key}, owner=${other.owner ?? 'unattached'}, scopes=${other.scopes.join('; ')}, sourceWritesRetired=${other.sourceWritesRetired === true}. Ownership/PR/slot/cleanup blockers remain; never write these paths or restart that owner.`),
      `Expected source head at reservation: ${item.head}. Branch your exclusively owned isolated checkout from current origin/main after pinned source/owner preflight.`,
      `Receipts outside disposable worker worktree: ${scratch}. Verify control scratch ignore and access before writing receipts.`,
      `Repository=${settings.repository}. Publication explicitly authorized: ${authority}`,
      publisher === undefined
        ? 'Discover and pin the authenticated repository gh publisher without inferring it from Git author or Copilot login.'
        : `Preserve and revalidate expected repository gh publisher=${publisher}; stop on mismatch.`,
      'Verify actual host-provided worktree path, branch and exclusive ownership before any writes. Never edit the original source workspace, coordinator control checkout, or another owner.',
      'This is an isolated asynchronous chat, not proof of a separate standalone host session. Requested GPT-6.1 Sol/high profile; effective effort UNVERIFIED unless the host exposes proof. Do not fabricate an effort selector or change host settings.',
      'Send STARTED immediately with exact child chat, worktree/branch/head and key. Reports go explicitly to the coordinator before ending: steer an active target; queue when idle. A final reply alone is not a callback.',
      'Run required focused validation, account-safe commit/push, actual remote/PR/enrollment verification, then stop implementation and clean up only the owned worktree/local branch with full evidence.',
      'Report host-owned worktree cleanup restrictions as CLEANUP_BLOCKED and preserve work; do not force-remove dirty or locked worktrees or delete remote branches.',
      'Do not create workers, expand scope, invent product approval, publish the control branch, wait for unrelated proofs or refill slots. No automation, timers, polling or protection/authentication bypass.',
      'End this assigned task after explicit publication and cleanup report or a concrete blocker. No routine continuation questions; unknown authority fails closed.'
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
