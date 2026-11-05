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
    && [null, 'APPROVED'].includes(member.review)
    && (member.priority === undefined || [0, 1, 2].includes(member.priority));
}

function manifestFor(active) {
  return { schema: SCHEMA, ticket: active.ticket, base: active.base, inventory: active.inventory,
    members: active.members.map(({ number, head, enrollment, review, priority }) =>
      ({ number, head, enrollment, review, ...(priority === undefined ? {} : { priority }) })),
    prefixes: active.prefixes, combined: active.head };
}

function validateBatchState(state) {
  invariant(state.deliveryProfile === undefined || state.deliveryProfile === 'independent-v1',
    'Unknown durable delivery profile.');
  if (state.deliveryTransition !== undefined) {
    const change = state.deliveryTransition;
    invariant(change && TICKET.test(change.ticket) && SHA.test(change.checkpoint)
      && typeof change.actor === 'string' && /^[a-z0-9][a-z0-9-]{0,38}$/i.test(change.actor)
      && Number.isSafeInteger(change.run) && change.run > 0 && timestamp(change.created)
      && ['draining', 'ready', 'completed', 'aborted'].includes(change.phase),
    'Invalid independent delivery transition.');
    if (['ready', 'completed'].includes(change.phase)) {
      invariant(SHA.test(change.source) && SHA.test(change.tree) && timestamp(change.prepared)
        && Number.isSafeInteger(change.prepareRun) && change.prepareRun > 0
        && change.qualification?.source === change.source
        && Number.isSafeInteger(change.qualification.run) && change.qualification.run > 0
        && Number.isSafeInteger(change.qualification.job) && change.qualification.job > 0,
      'Invalid prepared independent delivery boundary.');
    }
    if (change.phase === 'ready')
      invariant(state.active === null, 'A prepared delivery boundary cannot own an active candidate.');
    if (change.phase === 'completed')
      invariant(state.deliveryProfile === 'independent-v1' && timestamp(change.completed)
        && Number.isSafeInteger(change.resumeRun) && change.resumeRun > 0,
      'Completed delivery transition requires durable independent adoption.');
    if (change.phase === 'aborted')
      invariant(state.deliveryProfile === undefined && timestamp(change.aborted)
        && Number.isSafeInteger(change.abortRun) && change.abortRun > 0,
      'An aborted transition cannot revoke durable independent delivery.');
  }
  if (state.completedTip?.delivery !== undefined)
    require('./delivery.cjs').releaseRecord(state.completedTip);
  invariant(state.profile === PROFILE && Array.isArray(state.singletonRetries)
    && state.singletonRetries.every(memberValid), 'Invalid batch profile or singleton fallback revisions.');
  invariant(state.completedTip === null || state.completedTip && SHA.test(state.completedTip.sha)
    && SHA.test(state.completedTip.tree) && TICKET.test(state.completedTip.ticket),
  'Invalid completed batch tip.');
  if (state.completedTip?.qualification !== undefined) {
    const proof = state.completedTip.qualification;
    invariant(proof && Number.isSafeInteger(proof.run) && proof.run > 0
      && Number.isSafeInteger(proof.job) && proof.job > 0 && proof.source === state.completedTip.sha
      && (!state.completedTip.qualificationOwner || proof.run === state.completedTip.qualificationOwner.run),
    'Invalid completed-tip qualification evidence.');
  }
  if (state.completedTip?.qualificationOwner !== undefined) {
    const owner = state.completedTip.qualificationOwner;
    invariant(owner && Number.isSafeInteger(owner.run) && owner.run > 0 && owner.attempt === 1
      && owner.source === state.completedTip.sha, 'Invalid completed-tip qualification owner.');
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
    && ['windows-12-v1', 'windows-12-delivery-v1'].includes(active.inventory?.schema) && SHA.test(active.inventory.blob)
    && Array.isArray(active.inventory.jobs) && active.inventory.jobs.length > 0
    && JSON.stringify(active.inventory.jobs) === JSON.stringify(active.inventory.schema === 'windows-12-v1'
      ? require('./policy.cjs').WINDOWS_12_JOBS : require('./policy.cjs').DELIVERY_JOBS),
  'Invalid active batch.');
  if (active.failureKind !== undefined)
    invariant(['construction-conflict', 'validation-failure', 'stale', 'unavailable'].includes(active.failureKind),
      'Invalid batch retirement classification.');
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
    && (member.priority === undefined || priority(pr) === member.priority)
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
