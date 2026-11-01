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
const SPLIT_JOBS = Object.freeze([...JOBS,
  'Portable build, tests, and coverage', 'Publish and inspect exact-source payloads',
]);
const WINDOWS_12_JOBS = Object.freeze([...SPLIT_JOBS, 'Build exact-source Windows test modules',
  ...['storage-heavy', 'storage-other', 'ui-definitions', 'ui-definition-dialogs', 'ui-conversations',
    'ui-conversation-core', 'ui-conversation-actions', 'native-ux', 'ui-platform', 'ui-editors',
    'ui-workflows', 'ui-presentation'].map(partition => `Windows test partition (${partition})`),
]);
// Unversioned workflows are recognized only by their exact trusted Contents API blob identity.
const PREDECESSOR_WORKFLOWS = Object.freeze({
  '854e26b112e57ff003daedb0c6a2183e7355ff54': JOBS,
  'd7d28f15a59261722b8a87e57a41966aff87fd29': JOBS,
  'a9c7e87106cdef9398fb508a1181927d95819e72': JOBS,
  '65f091f1585b5a25c9c6e325f3a97e025e24bafb': JOBS,
  '033686e2be70445f8801b93ad034efda176edc8f': JOBS,
  '9f19d4298cde21e313481cec9042138d6378b7ba': JOBS,
  'd8bfcc6eceeb3b68bf993c94ee0139d58cf15a8c': SPLIT_JOBS,
  '86656bbb4151eac2f50bb4cd532f83d5667e3bba': WINDOWS_12_JOBS,
});
const STATE_BRANCH = 'automation/merge-queue-state';
const SHA = /^[0-9a-f]{40}$/;
const TICKET = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const PRIORITY_LABELS = Object.freeze(['queue:priority-high', 'queue:priority-normal', 'queue:priority-low']);
const MERGE_PHASES = Object.freeze(['merging', 'merge-refused', 'merge-unconfirmed']);
const MERGE_CONFIRMATION_MS = 10 * 60_000;
const JOB_OBSERVATION_MS = MERGE_CONFIRMATION_MS;
const JOB_CONCLUSIONS = Object.freeze([
  'success', 'failure', 'cancelled', 'timed_out', 'neutral', 'skipped', 'action_required', 'stale',
]);

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
    if (active.jobObservationStarted !== undefined) {
      invariant(['dispatched', 'retiring'].includes(active.phase) && active.mergeStarted === undefined
        && typeof active.jobObservationStarted === 'string'
        && Number.isFinite(Date.parse(active.jobObservationStarted))
        && new Date(Date.parse(active.jobObservationStarted)).toISOString() === active.jobObservationStarted
        && Date.parse(active.jobObservationStarted) >= Date.parse(active.created),
        'Invalid required-job observation timestamp.');
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

function jobOutcome(job) {
  if (job.status !== 'completed' || !JOB_CONCLUSIONS.includes(job.conclusion)) return 'pending';
  return job.conclusion === 'success' ? 'success' : 'failure';
}

function workflowJobs(source, blob) {
  const markers = source.replaceAll('\r\n', '\n').split('\n')
    .filter(line => line.includes('merge-queue-job-schema:'));
  if (markers.length !== 0)
    return markers.length === 1 && markers[0] === '# merge-queue-job-schema: windows-12-v1'
      ? WINDOWS_12_JOBS : null;
  return Object.hasOwn(PREDECESSOR_WORKFLOWS, blob) ? PREDECESSOR_WORKFLOWS[blob] : null;
}

function requiredJobsState(jobs, expected = JOBS) {
  if (!Array.isArray(expected) || expected.length === 0) return 'pending';
  const outcomes = expected.map(name => {
    const matches = jobs.filter(job => job.name === name);
    if (matches.length > 1) return 'failure';
    return matches.length === 0 ? 'pending' : jobOutcome(matches[0]);
  });
  return outcomes.includes('failure') ? 'failure' : outcomes.includes('pending') ? 'pending' : 'success';
}

function terminalJobs(jobs, expected = JOBS) {
  return Array.isArray(expected) && expected.length > 0
    && expected.every(name => jobs.filter(job => job.name === name).length === 1)
    && jobs.every(job => jobOutcome(job) !== 'pending');
}

function successfulJobs(jobs, expected = JOBS) {
  return requiredJobsState(jobs, expected) === 'success';
}

module.exports = { CHECKS, GATE, JOBS, SPLIT_JOBS, WINDOWS_12_JOBS, PREDECESSOR_WORKFLOWS, workflowJobs,
  STATE_BRANCH, SHA, TICKET, MERGE_PHASES, MERGE_CONFIRMATION_MS, JOB_OBSERVATION_MS,
  invariant, mode, execution,
  validateState, ready, readinessIssue, priority, title, matchingRuns, requiredJobsState, terminalJobs, successfulJobs };
