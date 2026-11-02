const { createHash } = require('node:crypto');
const { SHA, TICKET, GATE, invariant, readinessIssue, priority } = require('./policy.cjs');

const PROFILE = 'batch-v1';
const SCHEMA = 'batch-manifest-v1';
const MAX_MEMBERS = 5;
const PHASES = ['constructing', 'reserved', 'dispatched', 'proven', 'updating', 'merging', 'retiring', 'recovery-required'];
const CONTROL_PATH = /^(?:\.github\/workflows\/|eng\/merge-queue\/|eng\/(?:.*Release.*|.*BuildVersion.*|.*SourceTools.*)|GitVersion\.yml$)/i;
const digest = value => createHash('sha256').update(JSON.stringify(value)).digest('hex');
class BatchUpdateError extends Error {}
const timestamp = value => typeof value === 'string' && Number.isFinite(Date.parse(value))
  && new Date(value).toISOString() === value;

function profile(value = '') {
  invariant(['', 'singleton-v1', PROFILE].includes(value), 'Unknown merge queue profile.');
  return value || 'singleton-v1';
}

function memberValid(member) {
  return member && Number.isSafeInteger(member.number) && member.number > 0 && SHA.test(member.head)
    && typeof member.enrollment === 'string' && Number.isFinite(Date.parse(member.enrollment))
    && [null, 'APPROVED'].includes(member.review);
}

function manifestFor(active) {
  return { schema: SCHEMA, ticket: active.ticket, base: active.base, inventory: active.inventory,
    members: active.members.map(({ number, head, enrollment, review }) => ({ number, head, enrollment, review })),
    prefixes: active.prefixes, combined: active.head };
}

function validateBatchState(state) {
  invariant(state.profile === PROFILE && Array.isArray(state.singletonRetries)
    && state.singletonRetries.every(memberValid), 'Invalid batch profile or singleton fallback revisions.');
  invariant(state.completedTip === null || state.completedTip && SHA.test(state.completedTip.sha)
    && SHA.test(state.completedTip.tree) && TICKET.test(state.completedTip.ticket),
  'Invalid completed batch tip.');
  if (state.completedTip?.qualification !== undefined) {
    const proof = state.completedTip.qualification;
    invariant(proof && Number.isSafeInteger(proof.run) && proof.run > 0
      && Number.isSafeInteger(proof.job) && proof.job > 0 && proof.source === state.completedTip.sha,
    'Invalid completed-tip qualification evidence.');
  }
  if (state.completedTip?.qualificationRequested !== undefined)
    invariant(typeof state.completedTip.qualificationRequested === 'boolean', 'Invalid tip dispatch intent.');
  const active = state.active;
  if (active === null) return;
  invariant(active && (active.kind === undefined || active.kind === 'batch'), 'Unknown active queue protocol.');
  if (active.kind !== 'batch') {
    require('./policy.cjs').validateState({ version: 1, active, blocked: state.blocked });
    return;
  }
  invariant(active.kind === 'batch' && TICKET.test(active.ticket) && SHA.test(active.base)
    && timestamp(active.created) && PHASES.includes(active.phase) && typeof active.dispatchRecorded === 'boolean'
    && Array.isArray(active.members) && active.members.length > 0 && active.members.length <= MAX_MEMBERS
    && active.members.every(memberValid)
    && new Set(active.members.map(member => member.number)).size === active.members.length
    && Array.isArray(active.prefixes) && active.prefixes.length <= active.members.length
    && active.prefixes.every(item => SHA.test(item.sha) && SHA.test(item.tree))
    && active.inventory?.schema === 'windows-12-v1' && SHA.test(active.inventory.blob)
    && Array.isArray(active.inventory.jobs) && active.inventory.jobs.length > 0
    && JSON.stringify(active.inventory.jobs) === JSON.stringify(require('./policy.cjs').WINDOWS_12_JOBS),
  'Invalid active batch.');
  let unmerged = false;
  for (const [index, member] of active.members.entries()) {
    const expectedBase = index ? active.members[index - 1].merged : active.base;
    if (!member.merged) unmerged = true;
    else invariant(!unmerged && SHA.test(member.merged) && member.mergeIntent, 'Invalid merged batch prefix.');
    if (member.updateIntent) invariant(member.updateIntent.base === expectedBase && SHA.test(expectedBase)
      && member.updateIntent.head === member.head
      && timestamp(member.updateIntent.created),
      'Invalid branch-update intent.');
    if (member.updatedHead) invariant(member.updateIntent && SHA.test(member.updatedHead), 'Invalid updated member head.');
    if (member.check !== undefined) invariant(Number.isSafeInteger(member.check) && member.check > 0,
      'Invalid batch admission check.');
    if (member.mergeIntent) invariant(member.mergeIntent.base === expectedBase && SHA.test(expectedBase)
      && member.mergeIntent.head === (member.updatedHead || member.head)
      && timestamp(member.mergeIntent.created) && member.check, 'Invalid batch merge intent.');
  }
  if (active.jobObservationStarted !== undefined)
    invariant(timestamp(active.jobObservationStarted)
      && Date.parse(active.jobObservationStarted) >= Date.parse(active.created),
    'Invalid batch job observation timestamp.');
  if (active.phase !== 'constructing' && (active.head !== undefined
    || !['retiring', 'recovery-required'].includes(active.phase))) {
    invariant(active.prefixes.length === active.members.length && SHA.test(active.head)
      && active.head === active.prefixes.at(-1).sha && active.digest === digest(manifestFor(active)),
    'Batch manifest changed or is incomplete.');
  }
  invariant(!active.dispatchRecorded || active.phase !== 'constructing', 'Unconstructed batch was dispatched.');
}

function liveMember(pr, member, repository, enrollment, review) {
  return readinessIssue(pr, repository) === null && pr.mergeable === true
    && enrollment === member.enrollment && review === member.review
    && pr.head.sha === (member.updatedHead || member.head);
}

function ordered(prs, repository) {
  return prs.filter(pr => readinessIssue(pr, repository) === null && priority(pr) !== null)
    .sort((a, b) => priority(a) - priority(b) || a.number - b.number);
}

function gateMessage(active, index, run) {
  return `Combined commit ${active.head} (tree ${active.prefixes.at(-1).tree}) tested by CI run ${run}; `
    + `manifest ${active.digest}; PR #${active.members[index].number} admitted as prefix ${index + 1}/${active.members.length} `
    + `under ${PROFILE}. Individual head and intermediate main were NOT independently tested. `
    + 'Release remains blocked until the verified completed tip is qualified at its actual SHA.';
}

module.exports = { PROFILE, SCHEMA, MAX_MEMBERS, CONTROL_PATH, PHASES, digest, profile, timestamp, BatchUpdateError,
  validateBatchState, manifestFor, liveMember, ordered, gateMessage, GATE };
