const CHECKS = Object.freeze([
  'Portable build, tests, coverage, and package',
  'Windows integration tests',
  'Build WiX MSI and custom Burn setup',
]);
const GATE = 'Merge admission';
const JOBS = Object.freeze([
  'Admit CI execution', 'Resolve build version', ...CHECKS,
  ...['storage-heavy', 'storage-other', 'ui-definitions', 'native-ux', 'ui-platform']
    .map(shard => `Windows integration shard (${shard})`),
]);
const STATE_BRANCH = 'automation/merge-queue-state';
const SHA = /^[0-9a-f]{40}$/;
const TICKET = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const PRIORITY_LABELS = Object.freeze(['queue:priority-high', 'queue:priority-normal', 'queue:priority-low']);
const MERGE_PHASES = Object.freeze(['merging', 'merge-refused', 'merge-unconfirmed']);
const MERGE_CONFIRMATION_MS = 10 * 60_000;

function invariant(condition, message) {
  if (!condition) throw new Error(message);
}

function mode(value) {
  invariant(['', 'disabled', 'enabled'].includes(value), 'Unknown merge queue mode.');
  return value === 'enabled';
}

function execution({ modeValue, eventName, ref, sha, inputs }) {
  const enabled = mode(modeValue);
  const candidate = ['queue_pr', 'queue_head', 'queue_base', 'queue_ticket'].some(key => inputs[key]);
  if (candidate) invariant(enabled && eventName === 'workflow_dispatch' && ref === 'refs/heads/main',
    'Candidate dispatch requires the enabled queue on trusted main.');
  const comparison = eventName === 'workflow_dispatch'
    && (inputs.compare_portable_tests === true || inputs.compare_native_evidence === true);
  invariant(!(candidate && comparison), 'Candidate validation cannot also be a timing comparison.');
  return { candidate, source: sha, full: !enabled || candidate
    || eventName !== 'pull_request' && (ref === 'refs/heads/main' || ref.startsWith('refs/tags/') || comparison) };
}

function validateState(state) {
  invariant(state?.version === 1 && Array.isArray(state.blocked), 'Invalid merge queue state version.');
  invariant(state.blocked.length <= 1000, 'Merge queue blocked revision limit exceeded.');
  for (const item of state.blocked) {
    invariant(Number.isSafeInteger(item.number) && item.number > 0 && SHA.test(item.head)
      && typeof item.enrollment === 'string' && Number.isFinite(Date.parse(item.enrollment))
      && typeof item.reason === 'string', 'Invalid blocked revision.');
  }
  const active = state.active;
  if (active !== null) {
    invariant(active && Number.isSafeInteger(active.number) && active.number > 0
      && SHA.test(active.head) && SHA.test(active.base) && TICKET.test(active.ticket)
      && ['updating', 'reserved', 'dispatched', 'retiring', ...MERGE_PHASES].includes(active.phase)
      && typeof active.dispatchRecorded === 'boolean'
      && (active.phase === 'retiring'
        || active.dispatchRecorded === (active.phase === 'dispatched' || MERGE_PHASES.includes(active.phase)))
      && typeof active.created === 'string' && Number.isFinite(Date.parse(active.created))
      && typeof active.enrollment === 'string' && Number.isFinite(Date.parse(active.enrollment)) && active.checks
      && typeof active.checks === 'object' && !Array.isArray(active.checks), 'Invalid active candidate.');
    for (const [name, id] of Object.entries(active.checks)) {
      invariant([...CHECKS, GATE].includes(name) && Number.isSafeInteger(id) && id > 0,
        'Invalid candidate check identity.');
    }
    if (MERGE_PHASES.includes(active.phase) || active.mergeStarted !== undefined) {
      invariant((MERGE_PHASES.includes(active.phase) || active.phase === 'retiring')
        && active.dispatchRecorded === true
        && typeof active.mergeStarted === 'string' && Number.isFinite(Date.parse(active.mergeStarted))
        && new Date(Date.parse(active.mergeStarted)).toISOString() === active.mergeStarted
        && Date.parse(active.mergeStarted) >= Date.parse(active.created)
        && [...CHECKS, GATE].every(name => active.checks[name]),
        'Invalid merge reconciliation identity or timestamp.');
    }
  }
  return state;
}

function ready(pr, repository) {
  return readinessIssue(pr, repository) === null;
}

function readinessIssue(pr, repository) {
  if (pr.state !== 'open') return 'PR is not open';
  if (pr.draft !== false) return 'PR is a draft or its draft state is unknown';
  if (pr.base.ref !== 'main') return 'PR does not target main';
  if (pr.head.repo?.full_name !== repository) return 'PR is not from this repository';
  if (pr.auto_merge?.merge_method !== 'squash') return 'squash Auto-merge is not enabled';
  return null;
}

function priority(pr) {
  invariant(Array.isArray(pr.labels), 'PR labels are unavailable for queue priority.');
  const labels = pr.labels.map(label => {
    const name = typeof label === 'string' ? label : label?.name;
    invariant(typeof name === 'string', 'Invalid PR label metadata.');
    return name.toLowerCase();
  }).filter(name => name.startsWith('queue:priority-'));
  const ranks = new Set(labels.map(name => PRIORITY_LABELS.indexOf(name)));
  if (ranks.has(-1) || ranks.size > 1) return null;
  return ranks.size === 0 ? 1 : [...ranks][0];
}

function title(active) {
  return `Merge queue PR #${active.number} [${active.ticket}]`;
}

function matchingRuns(runs, active) {
  return runs.filter(run => run.display_title === title(active)
    && run.event === 'workflow_dispatch' && run.head_branch === 'main');
}

function successfulJobs(jobs) {
  return JOBS.every(name => {
    const matches = jobs.filter(job => job.name === name);
    return matches.length === 1 && matches[0].conclusion === 'success';
  });
}

module.exports = { CHECKS, GATE, JOBS, STATE_BRANCH, SHA, TICKET, MERGE_PHASES, MERGE_CONFIRMATION_MS,
  invariant, mode, execution,
  validateState, ready, readinessIssue, priority, title, matchingRuns, successfulJobs };
