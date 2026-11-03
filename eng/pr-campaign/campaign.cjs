const { createHash } = require('node:crypto');
const { SHA, TICKET, GATE, invariant, validateState, requiredJobsState, readinessIssue, priority } =
  require('../merge-queue/policy.cjs');
const { profile } = require('../merge-queue/batch-policy.cjs');

const SCHEMA = 'kora-pr-campaign-v1';
const QUALIFY = 'Qualify completed batch tip';
const ACTIONS = Object.freeze({
  'transport-error': 'Restore CLI/API connectivity, then read the same identities again; do not replay writes.',
  'authentication-error': 'Restore the recorded CLI principal/access; do not switch accounts automatically.',
  'command-timeout': 'Inspect CLI connectivity and retry a bounded read; timeout is not producer failure.',
  cancelled: 'Observation cancelled; resume explicitly. No worker or GitHub mutation is scheduled.',
  'budget-exhausted': 'Resume with an explicit observation/time budget; no completion is inferred.',
  'invalid-evidence': 'Inspect the named metadata boundary; obtain complete, current authoritative evidence.',
  'source-changed': 'Use the current head/enrollment. Older validation and handoffs do not transfer.',
  'closed-unmerged': 'Resolve the closed-unmerged target with its owner; closure is not delivery.',
  'merge-unreachable': 'Investigate the actual merge SHA against fresh main; do not claim delivery.',
  'conflict': 'Have the source owner repair the conflicting PR without bypassing admission.',
  'review-blocked': 'Have the source owner address the current review requirement.',
  'not-enrolled': 'Have the authorized source owner review draft/base/repository and squash enrollment.',
  'priority-invalid': 'Have the source owner resolve conflicting or unknown queue priority labels.',
  'revision-blocked': 'Inspect the controller outcome for this exact head/enrollment; repair through its owner.',
  'recovery-required': 'Request authorized queue-owner investigation; this observer cannot repair durable state.',
  'required-producer-failed': 'Inspect the exact run/attempt/job and repair through its source owner.',
  'run-failed-without-producer-failure': 'Inspect non-producer workflow/qualification failures at this exact run; no success is inferred.',
  'incomplete-jobs': 'Inspect missing/duplicate/skipped required jobs; waiting/skipped is not a pass.',
  'unknown-metadata': 'Re-read current mergeability/check metadata; unknown state is not readiness.',
  'qualification-invalid': 'Inspect the exact durable qualification run/job/source; no other run substitutes.',
  'main-moved-before-qualification': 'Ask the queue owner to reconcile completed-tip/main identities.',
});
const id = value => Number.isSafeInteger(value) && value > 0;
const timestamp = value => typeof value === 'string' && Number.isFinite(Date.parse(value));
const digest = value => createHash('sha256').update(JSON.stringify(value)).digest('hex');
const login = value => typeof value === 'string' && /^[a-z0-9][a-z0-9-]{0,38}$/i.test(value);
const ownerName = value => typeof value === 'string' && /^[a-z0-9][a-z0-9._-]{0,79}$/i.test(value);
const repositoryName = value => typeof value === 'string'
  && /^[a-z0-9][a-z0-9-]{0,38}\/[a-z0-9._-]+$/i.test(value)
  && !['.', '..'].includes(value.split('/')[1]);
function problem(code, target = null, reference = null) {
  invariant(Object.hasOwn(ACTIONS, code), 'Unknown campaign problem code.');
  return { code, target, reference, action: ACTIONS[code] };
}
class ObservationError extends Error {
  constructor(code, boundary) {
    super(`${code}: ${boundary}`);
    this.code = code;
    this.boundary = boundary;
  }
}
function boundary(condition, name) {
  if (!condition) throw new ObservationError('invalid-evidence', name);
}
function validateCampaign(campaign) {
  boundary(repositoryName(campaign?.repository) && ownerName(campaign.owner)
    && Array.isArray(campaign.targets) && campaign.targets.length > 0 && campaign.targets.length <= 100
    && campaign.targets.every(id) && new Set(campaign.targets).size === campaign.targets.length
    && (campaign.expectedPrincipal === null || login(campaign.expectedPrincipal)), 'campaign identity');
  return campaign;
}
function safeUrl(url, repository) {
  boundary(typeof url === 'string' && url.startsWith(`https://github.com/${repository}/`)
    && !/[\s<>"\\]/.test(url), 'GitHub reference URL');
  return url;
}
function validatePull(pull, number, repository) {
  boundary(pull && pull.number === number && ['OPEN', 'CLOSED', 'MERGED'].includes(pull.state)
    && SHA.test(pull.head) && ['MERGEABLE', 'CONFLICTING', 'UNKNOWN'].includes(pull.mergeable)
    && [null, 'APPROVED', 'REVIEW_REQUIRED', 'CHANGES_REQUESTED'].includes(pull.review)
    && typeof pull.draft === 'boolean' && typeof pull.base === 'string'
    && repositoryName(pull.repository) && (login(pull.author)
      || typeof pull.author === 'string' && /^[a-z0-9][a-z0-9-]{0,38}\[bot\]$/i.test(pull.author))
    && Array.isArray(pull.labels) && pull.labels.every(label => typeof label === 'string')
    && (pull.enrollment === null || timestamp(pull.enrollment))
    && [null, 'SQUASH', 'MERGE', 'REBASE'].includes(pull.method)
    && (pull.merge === null || SHA.test(pull.merge))
    && (pull.mergedAt === null || timestamp(pull.mergedAt)), 'PR metadata');
  safeUrl(pull.url, repository);
  boundary(pull.state !== 'MERGED' || SHA.test(pull.merge) && timestamp(pull.mergedAt), 'actual merged identity');
  return pull;
}
function summarizeRun(run, jobs, inventory, repository, kind, source, ticket = null) {
  boundary(run && id(run.id) && id(run.run_attempt) && SHA.test(run.head_sha)
    && typeof run.display_title === 'string'
    && typeof run.event === 'string' && typeof run.head_branch === 'string'
    && ['completed', 'queued', 'in_progress', 'waiting', 'pending', 'requested'].includes(run.status)
    && (run.conclusion === null || ['success', 'failure', 'cancelled', 'timed_out', 'neutral', 'skipped',
      'action_required', 'stale', 'startup_failure'].includes(run.conclusion)), 'workflow run identity');
  boundary(inventory && SHA.test(inventory.blob) && Array.isArray(inventory.jobs)
    && inventory.jobs.length > 0 && Array.isArray(jobs)
    && jobs.every(job => id(job.id) && job.run_id === run.id && job.run_attempt === run.run_attempt
      && job.head_sha === run.head_sha && typeof job.name === 'string'
      && ['completed', 'queued', 'in_progress', 'waiting', 'pending', 'requested'].includes(job.status)
      && (job.conclusion === null || ['success', 'failure', 'cancelled', 'timed_out', 'neutral',
        'skipped', 'action_required', 'stale'].includes(job.conclusion)))
    && new Set(jobs.map(job => job.id)).size === jobs.length, 'exact run/attempt/job/source');
  const expected = [...inventory.jobs, ...(kind === 'qualification' ? [QUALIFY] : [])];
  const selected = jobs.filter(job => expected.includes(job.name)).map(job => ({
    id: job.id, name: job.name, status: job.status, conclusion: job.conclusion,
    url: safeUrl(job.html_url, repository),
  }));
  const required = requiredJobsState(jobs, inventory.jobs);
  const failed = selected.filter(job => job.status === 'completed'
    && ['failure', 'cancelled', 'timed_out', 'action_required', 'stale'].includes(job.conclusion));
  const outcome = failed.length ? 'required-producer-failed'
    : run.status === 'completed' && !['success', 'skipped', null].includes(run.conclusion)
      ? 'run-failed-without-producer-failure'
    : required === 'success' ? 'required-jobs-success'
      : run.status === 'completed' ? 'incomplete-jobs' : 'waiting-or-skipped';
  return { id: run.id, attempt: run.run_attempt, workflowSource: run.head_sha,
    source, ticket, kind, event: run.event, branch: run.head_branch,
    status: run.status, conclusion: run.conclusion, url: safeUrl(run.html_url, repository),
    inventory: { blob: inventory.blob, schema: inventory.schema, jobs: inventory.jobs },
    jobs: selected, observedJobCount: jobs.length, outcome };
}
function newReceipt(campaign, now, observer) {
  validateCampaign(campaign);
  return { schema: SCHEMA, campaign, observedAt: now.toISOString(), observer,
    principal: null, references: null, pulls: [], queue: null, runs: [],
    following: null, reconciliations: [], superseded: [], blockers: [], outcome: 'blocked' };
}
function evaluate(receipt, snapshot) {
  const { campaign } = receipt;
  const { state, pulls, checks, candidate, qualification, duplicates, controllers } = snapshot;
  validateState(state);
  boundary(profile(snapshot.profile) === (state.profile || 'singleton-v1'), 'live queue profile/state');
  receipt.principal = snapshot.principal;
  receipt.references = snapshot.references;
  const active = state.active;
  receipt.queue = { version: state.version, profile: snapshot.profile,
    active: active === null ? null : { ticket: active.ticket, phase: active.phase,
      kind: active.kind || 'singleton', source: active.head || null, base: active.base,
      manifest: active.digest || null,
      members: active.kind === 'batch' ? active.members.map(member => ({
        number: member.number, source: member.head, admittedHead: member.updatedHead || member.head,
        enrollment: member.enrollment,
      })) : [{ number: active.number, source: active.head, admittedHead: active.head, enrollment: active.enrollment }] },
    completedTip: state.completedTip ? { sha: state.completedTip.sha, ticket: state.completedTip.ticket,
      combined: state.completedTip.combined || null, manifest: state.completedTip.manifest || null,
      qualification: state.completedTip.qualification || null } : null };
  if (active && ['recovery-required', 'retiring', 'merge-refused', 'merge-unconfirmed'].includes(active.phase))
    receipt.blockers.push(problem('recovery-required', null, receipt.references.queue.commit));
  let candidateCurrent = true;
  for (const member of receipt.queue.active?.members || []) {
    const current = pulls.find(pull => pull.number === member.number);
    if (!current || current.state === 'OPEN'
      && (current.head !== member.admittedHead || current.enrollment !== member.enrollment)) {
      candidateCurrent = false;
      receipt.blockers.push(problem('source-changed', member.number));
    }
  }
  for (const number of campaign.targets) {
    const pull = validatePull(pulls.find(item => item.number === number), number, campaign.repository);
    const member = receipt.queue.active?.members.find(item => item.number === number);
    const currentChecks = checks[number] || [];
    const result = { number, url: pull.url, author: pull.author, state: pull.state, head: pull.head,
      enrollment: pull.enrollment, merge: pull.merge, mergedAt: pull.mergedAt, reachableFrom: null,
      ticket: member?.admittedHead === pull.head ? active.ticket : null,
      source: member && candidateCurrent ? active.head || member.source : pull.head,
      proofKind: member && candidateCurrent ? active.kind === 'batch' ? 'combined-batch-not-solo-head' : 'singleton'
        : 'none', checks: [], outcome: 'waiting' };
    result.checks = currentChecks.map(check => ({ ...check,
      classification: result.ticket && check.ticket === result.ticket
        ? 'current-ticket' : 'historical-not-current-authority' }));
    if (pull.state === 'MERGED') {
      if (snapshot.reachable[number] === true) {
        result.reachableFrom = receipt.references.main.sha;
        result.outcome = 'MERGED_VERIFIED';
      } else receipt.blockers.push(problem('merge-unreachable', number, pull.url));
    } else if (pull.state === 'CLOSED') receipt.blockers.push(problem('closed-unmerged', number, pull.url));
    else {
      if (pull.mergeable === 'CONFLICTING') receipt.blockers.push(problem('conflict', number, pull.url));
      if (pull.mergeable === 'UNKNOWN') receipt.blockers.push(problem('unknown-metadata', number, pull.url));
      if (['REVIEW_REQUIRED', 'CHANGES_REQUESTED'].includes(pull.review))
        receipt.blockers.push(problem('review-blocked', number, pull.url));
      if (readinessIssue({ state: 'open', draft: pull.draft, base: { ref: pull.base },
        head: { repo: { full_name: pull.repository } },
        auto_merge: { merge_method: pull.method?.toLowerCase() } }, campaign.repository))
        receipt.blockers.push(problem('not-enrolled', number, pull.url));
      if (priority(pull) === null) receipt.blockers.push(problem('priority-invalid', number, pull.url));
      if (state.blocked.some(item => item.number === number && item.head === pull.head
        && item.enrollment === pull.enrollment))
        receipt.blockers.push(problem('revision-blocked', number, pull.url));
      if (member && candidateCurrent) result.outcome = candidate?.outcome || 'waiting-for-candidate-run';
      for (const check of currentChecks) {
        if (check.name === GATE && check.ticket === result.ticket && check.status === 'completed'
          && ['failure', 'cancelled', 'timed_out', 'action_required'].includes(check.conclusion))
          receipt.blockers.push(problem('required-producer-failed', number, check.url));
      }
    }
    receipt.pulls.push(result);
  }
  if (candidate) {
    receipt.runs.push(candidate);
    if (!candidateCurrent) candidate.classification = 'superseded-source';
    else if (['required-producer-failed', 'run-failed-without-producer-failure', 'incomplete-jobs'].includes(candidate.outcome))
      receipt.blockers.push(problem(candidate.outcome, null, candidate.url));
  }
  let qualified = false;
  if (qualification) {
    receipt.runs.push(qualification);
    const proof = state.completedTip.qualification;
    qualified = qualification.id === proof?.run && qualification.attempt === 1
      && qualification.source === proof.source && qualification.workflowSource === proof.source
      && qualification.branch === 'main' && ['push', 'workflow_dispatch'].includes(qualification.event)
      && qualification.status === 'completed' && qualification.conclusion === 'success'
      && qualification.outcome === 'required-jobs-success'
      && qualification.jobs.filter(job => job.name === QUALIFY && job.id === proof.job
        && job.status === 'completed' && job.conclusion === 'success').length === 1;
    qualification.classification = qualified ? 'authoritative-tip-receipt' : 'invalid-tip-receipt';
    if (!qualified) receipt.blockers.push(problem('qualification-invalid', null, qualification.url));
  } else if (state.completedTip?.qualification)
    receipt.blockers.push(problem('qualification-invalid', null, receipt.references.queue.commit));
  for (const run of duplicates) {
    run.classification = qualified ? 'non-authoritative-duplicate-after-qualified-tip'
      : 'unreceipted-tip-observation';
    // Job metadata cannot prove why a duplicate's publication/qualification guard failed.
    if (qualified && run.outcome !== 'required-jobs-success') run.diagnostic = 'publication-guard-cause-unconfirmed';
    receipt.runs.push(run);
    if (!qualified && !active
      && ['required-producer-failed', 'run-failed-without-producer-failure', 'incomplete-jobs'].includes(run.outcome))
      receipt.blockers.push(problem(run.outcome, null, run.url));
  }
  if (!active && state.completedTip && !qualified && state.completedTip.sha !== receipt.references.main.sha)
    receipt.blockers.push(problem('main-moved-before-qualification'));
  receipt.following = candidateCurrent && candidate && candidate.status !== 'completed'
    ? { kind: 'candidate', run: candidate.id, url: candidate.url }
    : controllers.length ? { kind: 'controller-handoff', run: controllers[0].id, url: controllers[0].url,
      triggerHead: controllers[0].triggerHead, event: controllers[0].event, status: controllers[0].status,
      classification: 'trigger-context-not-validation-source' }
      : !qualified && !active && duplicates.find(run => run.status !== 'completed')
        ? { kind: 'tip-qualification', run: duplicates.find(run => run.status !== 'completed').id,
          url: duplicates.find(run => run.status !== 'completed').url } : null;
  receipt.outcome = receipt.blockers.length ? 'blocked'
    : receipt.pulls.every(pull => pull.outcome === 'MERGED_VERIFIED') ? 'MERGED_VERIFIED' : 'observing';
  return receipt;
}
function validateReceipt(receipt, campaign) {
  boundary(receipt?.schema === SCHEMA, 'previous receipt schema');
  validateCampaign(receipt.campaign);
  boundary(digest(receipt.campaign) === digest(campaign) && timestamp(receipt.observedAt)
    && receipt.observer && SHA.test(receipt.observer.source)
    && /^[a-f0-9]{64}$/.test(receipt.observer.digest)
    && (receipt.principal === null || login(receipt.principal))
    && ['blocked', 'observing', 'MERGED_VERIFIED'].includes(receipt.outcome)
    && Array.isArray(receipt.blockers) && receipt.blockers.every(item => Object.hasOwn(ACTIONS, item.code))
    && Array.isArray(receipt.pulls) && Array.isArray(receipt.runs)
    && Array.isArray(receipt.superseded) && Array.isArray(receipt.reconciliations), 'previous receipt envelope');
  boundary(receipt.pulls.length <= campaign.targets.length
    && (receipt.outcome === 'blocked' || receipt.pulls.length === campaign.targets.length)
    && new Set(receipt.pulls.map(pull => pull.number)).size === receipt.pulls.length
    && receipt.pulls.every(pull => campaign.targets.includes(pull.number) && SHA.test(pull.head)
      && (pull.ticket === null || TICKET.test(pull.ticket))
      && SHA.test(pull.source) && (pull.enrollment === null || timestamp(pull.enrollment))
      && (pull.merge === null || SHA.test(pull.merge))
      && ['OPEN', 'CLOSED', 'MERGED'].includes(pull.state)
      && (pull.mergedAt === null || timestamp(pull.mergedAt))
      && typeof pull.outcome === 'string' && Array.isArray(pull.checks)), 'previous PR identities');
  if (receipt.references !== null) boundary(SHA.test(receipt.references?.main?.sha)
    && timestamp(receipt.references.main.fetchedAt) && SHA.test(receipt.references.queue?.commit)
    && SHA.test(receipt.references.queue.blob)
    && receipt.references.queue.branch === 'automation/merge-queue-state', 'previous reference identities');
  if (receipt.queue !== null) {
    boundary([1, 2].includes(receipt.queue?.version)
      && ['singleton-v1', 'batch-v1'].includes(receipt.queue.profile), 'previous queue profile');
    const active = receipt.queue.active;
    if (active !== null) boundary(active && TICKET.test(active.ticket) && SHA.test(active.base)
      && (active.source === null || SHA.test(active.source)) && typeof active.phase === 'string'
      && ['singleton', 'batch'].includes(active.kind) && Array.isArray(active.members)
      && active.members.length > 0 && active.members.every(member => id(member.number)
        && SHA.test(member.source) && SHA.test(member.admittedHead) && timestamp(member.enrollment)),
    'previous ticket/member source');
    const tip = receipt.queue.completedTip;
    if (tip !== null) {
      boundary(tip && SHA.test(tip.sha) && TICKET.test(tip.ticket)
        && (tip.combined === null || SHA.test(tip.combined))
        && (tip.manifest === null || /^[a-f0-9]{64}$/.test(tip.manifest)), 'previous completed-tip source');
      if (tip.qualification !== null) boundary(tip.qualification && id(tip.qualification.run)
        && id(tip.qualification.job) && tip.qualification.source === tip.sha, 'previous qualification run/job/source');
    }
  }
  boundary(receipt.runs.every(run => id(run.id) && id(run.attempt) && SHA.test(run.source)
    && SHA.test(run.workflowSource) && (run.ticket === null || TICKET.test(run.ticket))
    && SHA.test(run.inventory?.blob) && Array.isArray(run.inventory.jobs) && run.inventory.jobs.length > 0
    && Array.isArray(run.jobs) && new Set(run.jobs.map(job => job.id)).size === run.jobs.length
    && run.jobs.every(job => id(job.id) && typeof job.name === 'string'
      && ['completed', 'queued', 'in_progress', 'waiting', 'pending', 'requested'].includes(job.status))),
  'previous run identities');
  boundary(receipt.outcome !== 'MERGED_VERIFIED' || receipt.blockers.length === 0
    && receipt.pulls.length === campaign.targets.length && receipt.pulls.every(pull =>
      pull.state === 'MERGED' && SHA.test(pull.merge) && pull.outcome === 'MERGED_VERIFIED'
      && pull.reachableFrom === receipt.references?.main.sha), 'previous merge proof');
  return receipt;
}
function supersede(receipt, previous) {
  if (!previous) return receipt;
  validateReceipt(previous, receipt.campaign);
  boundary(!previous.principal || !receipt.principal || previous.principal === receipt.principal,
    'recorded receipt principal');
  const previousId = digest(previous);
  const observerChanges = ['source', 'digest'].filter(key => previous.observer[key] !== receipt.observer[key]);
  if (observerChanges.length)
    receipt.superseded.push({ previousReceipt: previousId, target: null,
      classification: 'superseded-observer-validation-not-history-deletion',
      changes: observerChanges.map(key => `observer.${key}`) });
  for (const pull of receipt.pulls) {
    const old = previous.pulls.find(item => item.number === pull.number);
    if (!old) continue;
    const changes = ['head', 'ticket', 'enrollment', 'source'].filter(key => old[key] !== pull[key]);
    if (changes.length) receipt.superseded.push({ previousReceipt: previousId, target: pull.number,
      classification: 'superseded-observation-not-history-deletion', changes,
      previous: { head: old.head, ticket: old.ticket, source: old.source, enrollment: old.enrollment },
      current: { head: pull.head, ticket: pull.ticket, source: pull.source, enrollment: pull.enrollment } });
  }
  if (previous.queue?.completedTip?.qualification
    && digest(previous.queue.completedTip) !== digest(receipt.queue?.completedTip || null))
    receipt.superseded.push({ previousReceipt: previousId, target: null,
      classification: 'superseded-tip-context-not-history-deletion', changes: ['completedTip'] });
  return receipt;
}

module.exports = { SCHEMA, QUALIFY, ACTIONS, ObservationError, problem, boundary, id, login, timestamp,
  digest, validateCampaign, safeUrl, validatePull, summarizeRun, newReceipt, evaluate, validateReceipt, supersede };
