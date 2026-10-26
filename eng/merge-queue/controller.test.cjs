const { test } = require('node:test');
const assert = require('node:assert/strict');
const { reconcile } = require('./controller.cjs');
const { CHECKS, GATE, JOBS, MERGE_PHASES, MERGE_CONFIRMATION_MS,
  mode, validateState, ready, priority, title, successfulJobs } = require('./policy.cjs');

const BASE = 'a'.repeat(40);
const HEAD = 'b'.repeat(40);
const OTHER = 'c'.repeat(40);
const TICKET = '01234567-89ab-cdef-0123-456789abcdef';
const NOW = new Date('2026-01-01T12:00:00Z');
const ENROLLED = '2026-01-01T11:00:00Z';
const clone = value => structuredClone(value);
function pr(number = 1, overrides = {}) {
  return { number, state: 'open', draft: false, merged: false, mergeable: true, labels: [],
    base: { ref: 'main' }, head: { sha: HEAD, repo: { full_name: 'owner/repo' } },
    auto_merge: { merge_method: 'squash' }, ...overrides };
}
function active(overrides = {}) {
  return { number: 1, head: HEAD, base: BASE, ticket: TICKET, phase: 'dispatched',
    checks: Object.fromEntries([...CHECKS, GATE].map((name, index) => [name, index + 1])),
    created: NOW.toISOString(), enrollment: ENROLLED, dispatchRecorded: true, ...overrides };
}
function run(overrides = {}) {
  return { id: 9, display_title: title(active()), event: 'workflow_dispatch', head_branch: 'main',
    head_sha: BASE, status: 'completed', conclusion: 'success', run_attempt: 1, ...overrides };
}
class Fake {
  repository = 'owner/repo';
  state = { version: 1, active: null, blocked: [] };
  base = BASE;
  prs = [pr()];
  workflowRuns = [];
  workflowJobs = JOBS.map(name => ({ name, conclusion: 'success' }));
  contains = true;
  approved = true;
  enrolled = ENROLLED;
  calls = [];
  messages = [];
  info(message) { this.messages.push(message); }
  async verifyProtection() { this.calls.push(['protection']); }
  async load() { return clone(this.state); }
  async save(state) { this.state = clone(state); this.calls.push(['save', clone(state)]); }
  async main() { return this.base; }
  async pulls() { return clone(this.prs); }
  async pull(number) { return clone(this.prs.find(item => item.number === number)); }
  async containsBase() { return this.contains; }
  async reviewReady() { return this.approved; }
  async enrollment() { return this.enrolled; }
  async runs() { return clone(this.workflowRuns); }
  async jobs() { return clone(this.workflowJobs); }
  async createCheck(value, name) { this.calls.push(['check', value.head, name]); return this.calls.length + 100; }
  async finishCheck(id, conclusion, message) { this.calls.push(['finish', id, conclusion, message]); }
  async dispatch(value) { this.calls.push(['dispatch', clone(value)]); }
  async wake() { this.calls.push(['wake']); }
  async cancel(id) { this.calls.push(['cancel', id]); }
  async updateBranch(value) { this.calls.push(['update', clone(value)]); }
  async merge(value) { this.calls.push(['merge', clone(value)]); return { merged: true }; }
  notice(message) { this.calls.push(['notice', message]); }
}
const step = (api, options = {}) => reconcile(api, { now: NOW, ticket: TICKET, ...options });
const count = (api, action) => api.calls.filter(call => call[0] === action).length;
const labels = (...names) => names.map(name => ({ name }));

test('idle feedback explains an empty queue without dispatching work', async () => {
  const api = new Fake();
  api.prs = [];
  assert.equal(await step(api), 'idle');
  const trace = api.messages.join('\n');
  assert.match(trace, /branch protection verified/);
  assert.match(trace, /no active candidate; 0 blocked revision/);
  assert.match(trace, new RegExp(`Current main: ${BASE}`));
  assert.match(trace, /Inspected 0 open PR\(s\); 0 express/);
  assert.match(trace, /Selection order \(0 candidate\(s\).*none/);
  assert.match(trace, /Selection exhausted; no runnable candidate/);
  assert.match(trace, /Reconciliation finished: idle/);
  assert.equal(count(api, 'dispatch'), 0);
  assert.equal(count(api, 'wake'), 0);
});

test('selection feedback explains every exclusion and reservation without echoing PR content', async () => {
  const api = new Fake();
  const untrusted = 'UNTRUSTED-CONTENT\n::error::not-a-controller-message';
  api.prs = [
    pr(1, { draft: true }), pr(2, { auto_merge: null }),
    pr(3, { head: { sha: HEAD, repo: { full_name: 'fork/repo' } } }),
    pr(4), pr(5), pr(6, { mergeable: null }), pr(7),
    pr(8, { labels: labels(`queue:priority-${untrusted}`) }),
    pr(9, { mergeable: false }), pr(10, { title: untrusted, body: untrusted,
      labels: labels(untrusted), head: { sha: HEAD, ref: untrusted, repo: { full_name: 'owner/repo' } } }),
  ];
  api.state.blocked = [{ number: 5, head: HEAD, enrollment: ENROLLED, reason: untrusted }];
  api.enrollment = async number => number === 4 ? null : ENROLLED;
  api.reviewReady = async number => number !== 7;
  assert.equal(await step(api), 'dispatched');
  const trace = [...api.messages, ...api.calls.filter(call => call[0] === 'notice').map(call => call[1])].join('\n');
  for (const message of [
    /PR #1: skipped; PR is a draft/, /PR #2: skipped; squash Auto-merge is not enabled/,
    /PR #3: skipped; PR is not from this repository/, /Inspected 10 open PR\(s\); 7 express/,
    /PR #4: skipped; no current Auto-merge enrollment/, /PR #5: skipped; head .*quarantined/,
    /PR #6: skipped; mergeability is unknown/, /PR #7: skipped; required reviews are not satisfied/,
    /PR #8 has invalid or conflicting/, /PR #9: quarantining head .*Selected PR conflicts/,
    /Selection order.*#4 \(normal\), #5 \(normal\), #6 \(normal\), #7 \(normal\), #9 \(normal\), #10 \(normal\)/,
    /PR #10: reserved the candidate lane/, /dispatch intent persisted/, /candidate CI dispatch accepted/,
    /Reconciliation finished: dispatched/,
  ]) assert.match(trace, message);
  assert.equal(trace.includes(untrusted), false);
  assert.equal(api.state.active.number, 10);
  assert.equal(count(api, 'dispatch'), 1);
  assert.equal(count(api, 'merge'), 0);
});

test('live PR reread feedback reports readiness changes instead of reserving stale intent', async () => {
  const api = new Fake();
  api.pull = async () => pr(1, { auto_merge: null });
  assert.equal(await step(api), 'idle');
  assert.match(api.messages.join('\n'), /PR #1: skipped on live reread; squash Auto-merge is not enabled/);
  assert.equal(count(api, 'dispatch'), 0);
});

test('active feedback distinguishes waiting, validation, branch updates and cancellation lane holds', async () => {
  for (const scenario of ['waiting-for-run', 'validating', 'updating', 'retiring', 'unobservable']) {
    const api = new Fake();
    api.state.active = active();
    if (scenario === 'validating' || scenario === 'retiring') api.workflowRuns = [run({ status: 'in_progress', conclusion: null })];
    if (scenario === 'updating') {
      api.state.active = active({ phase: 'updating', dispatchRecorded: false, checks: {} });
      api.contains = false;
    }
    if (scenario === 'retiring' || scenario === 'unobservable') api.prs[0].auto_merge = null;
    assert.equal(await step(api), scenario === 'unobservable' ? 'retiring' : scenario);
    const trace = api.messages.join('\n');
    assert.match(trace, new RegExp(`Reconciling active PR #1;.*ticket ${TICKET}`));
    assert.match(trace, /Waiting PRs cannot preempt this lane/);
    const expected = {
      'waiting-for-run': /no matching workflow run is visible yet; waiting without redispatch/,
      validating: /matching run 9; status in_progress; conclusion pending; attempt 1/,
      updating: /branch update is not yet visible; waiting without repeating/,
      retiring: /requesting cancellation of matching run 9/,
      unobservable: /recorded dispatch has no observable matching run; lane held/,
    };
    assert.match(trace, expected[scenario]);
    if (scenario === 'retiring') assert.match(trace, /lane held until matching runs are observed terminal/);
    assert.equal(count(api, 'dispatch'), 0);
    assert.equal(count(api, 'merge'), 0);
    assert.equal(count(api, 'wake'), 0);
  }
});

test('terminal validation feedback identifies missing and duplicate required jobs before quarantine', async () => {
  for (const duplicate of [false, true]) {
    const api = new Fake();
    api.state.active = active();
    api.workflowRuns = [run()];
    const job = api.workflowJobs.shift();
    if (duplicate) api.workflowJobs.push(job, clone(job));
    assert.equal(await step(api), 'blocked');
    const trace = api.messages.join('\n');
    assert.ok(trace.includes(`required job "${job.name}"; ${duplicate ? 2 : 0} match(es); conclusion(s): ${duplicate ? 'success, success' : 'missing'}.`));
    for (const name of JOBS) assert.ok(trace.includes(`required job "${name}"`));
    assert.match(trace, /quarantining head .*Candidate validation failed/);
    assert.match(trace, /Decision: blocked; requesting another controller reconciliation/);
    assert.match(trace, /Controller continuation requested/);
    assert.equal(count(api, 'merge'), 0);
  }
});

test('successful feedback correlates dispatch and protected merge with exact source evidence', async () => {
  const api = new Fake();
  api.contains = false;
  assert.equal(await step(api), 'updating');
  assert.match(api.messages.join('\n'), /head does not contain current main; requesting GitHub branch update/);
  assert.match(api.messages.join('\n'), /branch update request accepted; retaining the lane/);
  api.contains = true;
  assert.equal(await step(api), 'dispatched');
  assert.match(api.messages.join('\n'), /branch update observed; head .* contains current main/);
  api.workflowRuns = [run()];
  assert.equal(await step(api), 'merged');
  const trace = api.messages.join('\n');
  assert.match(trace, /exact required jobs and final head\/base\/enrollment\/review\/ancestry checks passed; merge intent persisted/);
  assert.match(trace, new RegExp(`requesting protected squash merge with expected head ${HEAD}`));
  assert.match(trace, /merge confirmed; candidate lane released/);
  assert.match(trace, /Reconciliation finished: merged/);
  assert.equal(count(api, 'update'), 1);
  assert.equal(count(api, 'dispatch'), 1);
  assert.equal(count(api, 'merge'), 1);
});

test('failed operations leave truthful partial feedback, not a successful terminal log', async () => {
  for (const operation of ['verifyProtection', 'save', 'dispatch', 'wake']) {
    const api = new Fake();
    if (operation === 'wake') {
      api.state.active = active();
      api.workflowRuns = [run()];
    }
    api[operation] = async () => { throw new Error(`Synthetic ${operation} failure`); };
    await assert.rejects(step(api), new RegExp(`Synthetic ${operation} failure`));
    const trace = api.messages.join('\n');
    assert.match(trace, /Starting reconciliation/);
    assert.doesNotMatch(trace, /Reconciliation finished/);
    if (operation === 'dispatch') {
      assert.match(trace, /dispatch intent persisted; requesting CI/);
      assert.doesNotMatch(trace, /candidate CI dispatch accepted/);
      assert.equal(api.state.active.dispatchRecorded, true);
    }
    if (operation === 'wake') {
      assert.match(trace, /requesting another controller reconciliation/);
      assert.doesNotMatch(trace, /Controller continuation requested/);
    }
  }
});

test('mode is explicitly disabled by default and rejects misspelled configuration', () => {
  for (const value of ['', 'disabled']) assert.equal(mode(value), false);
  assert.equal(mode('enabled'), true);
  for (const value of ['true', 'ENABLED', undefined, null]) assert.throws(() => mode(value));
});

test('missing, corrupt and unknown durable state fails closed', () => {
  for (const state of [null, {}, { version: 2, blocked: [], active: null },
    { version: 1, blocked: [], active: {} }, { version: 1, blocked: [{}], active: null },
    { version: 1, blocked: [], active: active({ checks: { forged: 1 } }) },
    { version: 1, blocked: [], active: active({ created: 'unknown' }) },
    { version: 1, blocked: [], active: active({ enrollment: 2026 }) },
    { version: 1, blocked: [], active: active({ created: 2026 }) }]) {
    assert.throws(() => validateState(state));
  }
});

test('only open same-repository squash auto-merge PRs express queue intent', () => {
  assert.equal(ready(pr(), 'owner/repo'), true);
  for (const changes of [{ draft: true }, { draft: undefined }, { draft: null }, { state: 'closed' }, { auto_merge: null },
    { auto_merge: undefined }, { auto_merge: { merge_method: 'merge' } }, { base: { ref: 'other' } },
    { head: { sha: HEAD, repo: null } }, { head: { sha: HEAD, repo: { full_name: 'fork/repo' } } }]) {
    assert.equal(ready(pr(1, changes), 'owner/repo'), false);
  }
});

test('oldest eligible PR gets the only full-build ticket and SHA-bound checks', async () => {
  const api = new Fake();
  api.prs = [pr(3), pr(2), pr(1, { draft: true })];
  assert.equal(await step(api), 'dispatched');
  assert.equal(api.state.active.number, 2);
  assert.equal(api.state.active.head, HEAD);
  assert.equal(api.state.active.base, BASE);
  assert.equal(count(api, 'dispatch'), 1);
  assert.equal(count(api, 'check'), 4);
  const dispatchIndex = api.calls.findIndex(call => call[0] === 'dispatch');
  assert.equal(api.calls[dispatchIndex - 1][1].active.phase, 'dispatched');
});

test('priority defaults to normal and recognizes only one supported priority, case-insensitively', () => {
  for (const names of [[], ['bug'], ['queue:other'], ['queue:priority-normal'], ['QUEUE:PRIORITY-NORMAL']]) {
    assert.equal(priority(pr(1, { labels: labels(...names) })), 1);
  }
  assert.equal(priority(pr(1, { labels: labels('queue:priority-high', 'bug') })), 0);
  assert.equal(priority(pr(1, { labels: labels('queue:priority-low') })), 2);
  assert.equal(priority(pr(1, { labels: ['queue:priority-high'] })), 0);
  assert.equal(priority(pr(1, { labels: labels('queue:priority-high', 'QUEUE:PRIORITY-HIGH') })), 0);
  for (const names of [
    ['queue:priority-high', 'queue:priority-low'],
    ['queue:priority-normal', 'queue:priority-high'],
    ['queue:priority-normal', 'queue:priority-low'],
    ['queue:priority-urgent'],
    ['queue:priority-high', 'queue:priority-urgent'],
  ]) {
    assert.equal(priority(pr(1, { labels: labels(...names) })), null);
  }
  for (const invalid of [undefined, null, {}, [null], [{}], [42], [{ name: null }]]) {
    assert.throws(() => priority(pr(1, { labels: invalid })), /label/i);
  }
});

test('high then normal then low priority selects by ascending PR number within each tier', async () => {
  const api = new Fake();
  api.prs = [
    pr(1, { labels: labels('queue:priority-low') }),
    pr(4, { labels: labels('queue:priority-normal') }),
    pr(2),
    pr(8, { labels: labels('queue:priority-high') }),
    pr(6, { labels: labels('queue:priority-high') }),
  ];
  for (const expected of [6, 8, 2, 4, 1]) {
    assert.equal(await step(api), 'dispatched');
    assert.equal(api.state.active.number, expected);
    api.state.active = null;
    api.prs = api.prs.filter(item => item.number !== expected);
  }
  assert.equal(count(api, 'dispatch'), 5);
});

test('conflicting and unsupported priority labels are explicit and do not occupy or quarantine the lane', async () => {
  for (const names of [['queue:priority-high', 'queue:priority-low'], ['queue:priority-urgent']]) {
    const api = new Fake();
    api.prs = [pr(1, { labels: labels(...names) }), pr(2)];
    assert.equal(await step(api), 'dispatched');
    assert.equal(api.state.active.number, 2);
    assert.deepEqual(api.state.blocked, []);
    assert.match(api.calls.find(call => call[0] === 'notice')[1], /PR #1.*invalid or conflicting/);
    assert.equal(count(api, 'dispatch'), 1);
  }
  const api = new Fake();
  api.prs[0].labels = labels('queue:priority-urgent');
  assert.equal(await step(api), 'idle');
  assert.equal(count(api, 'notice'), 1);
  assert.equal(count(api, 'wake'), 0);
  api.prs[0].labels = labels('queue:priority-high');
  assert.equal(await step(api), 'dispatched');
});

test('priority changes reorganize waiting candidates without a new commit or enrollment', async () => {
  const api = new Fake();
  api.prs = [pr(1, { mergeable: null }), pr(2, { mergeable: null })];
  assert.equal(await step(api), 'idle');
  api.prs.forEach(item => { item.mergeable = true; });
  api.prs[1].labels = labels('queue:priority-high');
  assert.equal(await step(api), 'dispatched');
  assert.equal(api.state.active.number, 2);
  assert.equal(api.state.active.enrollment, ENROLLED);
  assert.equal(api.state.active.head, HEAD);
});

test('removing a high-priority label restores the unlabeled normal-tier PR-number order', async () => {
  const api = new Fake();
  api.prs = [pr(1, { mergeable: null }), pr(2, { labels: labels('queue:priority-high'), mergeable: null })];
  assert.equal(await step(api), 'idle');
  api.prs.forEach(item => { item.mergeable = true; });
  api.prs[1].labels = [];
  assert.equal(await step(api), 'dispatched');
  assert.equal(api.state.active.number, 1);
});

test('a fresh priority change during selection refreshes order before reserving a lane', async () => {
  const api = new Fake();
  api.prs.push(pr(2));
  api.pull = async number => {
    const observed = clone(api.prs.find(item => item.number === number));
    if (number === 1) observed.labels = labels('queue:priority-low');
    return observed;
  };
  assert.equal(await step(api), 'continuing');
  assert.equal(api.state.active, null);
  assert.equal(count(api, 'check'), 0);
  assert.equal(count(api, 'dispatch'), 0);
  assert.equal(count(api, 'wake'), 1);
  assert.match(api.calls.find(call => call[0] === 'notice')[1], /priority changed/);
});

test('priority conflicts discovered on the fresh PR read skip it explicitly without granting admission', async () => {
  const api = new Fake();
  api.prs.push(pr(2));
  api.pull = async number => pr(number, number === 1
    ? { labels: labels('queue:priority-high', 'queue:priority-low') } : {});
  assert.equal(await step(api), 'dispatched');
  assert.equal(api.state.active.number, 2);
  assert.equal(count(api, 'notice'), 1);
});

test('missing priority metadata fails explicitly before any candidate is reserved', async () => {
  const api = new Fake();
  delete api.prs[0].labels;
  await assert.rejects(step(api), /labels are unavailable/);
  assert.equal(api.state.active, null);
  assert.equal(count(api, 'check'), 0);
  assert.equal(count(api, 'dispatch'), 0);
  assert.equal(count(api, 'wake'), 0);
});

test('priority labels do not enroll drafts, foreign repositories or PRs without auto-merge', async () => {
  for (const changes of [
    { auto_merge: null }, { draft: true },
    { head: { sha: HEAD, repo: { full_name: 'fork/repo' } } },
  ]) {
    const api = new Fake();
    api.prs = [pr(1, { ...changes, labels: labels('queue:priority-high') }), pr(2)];
    assert.equal(await step(api), 'dispatched');
    assert.equal(api.state.active.number, 2);
  }
});

test('high priority cannot bypass unknown mergeability or unmet reviews', async () => {
  for (const condition of ['mergeability', 'review']) {
    const api = new Fake();
    api.prs = [pr(2, { labels: labels('queue:priority-high') }), pr(1)];
    if (condition === 'mergeability') api.prs[0].mergeable = null;
    else api.reviewReady = async number => number !== 2;
    assert.equal(await step(api), 'dispatched');
    assert.equal(api.state.active.number, 1);
  }
});

test('high-priority conflicts are quarantined before a normal runnable candidate is admitted', async () => {
  const api = new Fake();
  api.prs = [pr(1), pr(2, { labels: labels('queue:priority-high'), mergeable: false })];
  assert.equal(await step(api), 'dispatched');
  assert.equal(api.state.active.number, 1);
  assert.deepEqual(api.state.blocked.map(item => item.number), [2]);
  assert.equal(count(api, 'dispatch'), 1);
});

test('priority labels cannot retry quarantined heads or erase their failure records', async () => {
  const api = new Fake();
  api.state.blocked = [{ number: 1, head: HEAD, enrollment: ENROLLED, reason: 'failure' }];
  const blocked = clone(api.state.blocked);
  api.prs = [pr(1, { labels: labels('queue:priority-high') }), pr(2)];
  assert.equal(await step(api), 'dispatched');
  assert.equal(api.state.active.number, 2);
  assert.deepEqual(api.state.blocked, blocked);
});

test('priority changes never preempt an updating, reserved or validating candidate', async () => {
  for (const phase of ['updating', 'reserved', 'dispatched']) {
    const api = new Fake();
    api.state.active = active({ phase, dispatchRecorded: phase === 'dispatched' });
    api.prs = [
      pr(1, { labels: labels('queue:priority-low', 'queue:priority-normal') }),
      pr(2, { labels: labels('queue:priority-high') }),
    ];
    if (phase === 'updating') api.contains = false;
    if (phase === 'dispatched') api.workflowRuns = [run({ status: 'in_progress', conclusion: null })];
    assert.equal(await step(api), phase === 'updating' ? 'updating' : phase === 'reserved' ? 'dispatched' : 'validating');
    assert.equal(api.state.active.number, 1);
    assert.equal(count(api, 'cancel'), 0);
    assert.equal(count(api, 'wake'), 0);
  }
});

test('high-priority arrivals cannot release a retiring lane before its matching run terminates', async () => {
  const api = new Fake();
  api.state.active = active({ phase: 'retiring' });
  api.prs.push(pr(2, { labels: labels('queue:priority-high') }));
  api.workflowRuns = [run({ status: 'in_progress', conclusion: null })];
  assert.equal(await step(api), 'retiring');
  assert.equal(api.state.active.number, 1);
  assert.equal(count(api, 'cancel'), 1);
  assert.equal(count(api, 'dispatch'), 0);
  assert.equal(count(api, 'wake'), 0);
});

test('priority labels on a validated candidate cannot invalidate its exact successful merge evidence', async () => {
  const api = new Fake();
  api.state.active = active();
  api.prs[0].labels = labels('queue:priority-low', 'queue:priority-high');
  api.prs.push(pr(2, { labels: labels('queue:priority-high') }));
  api.workflowRuns = [run()];
  assert.equal(await step(api), 'merged');
  assert.equal(count(api, 'merge'), 1);
  assert.equal(count(api, 'dispatch'), 0);
});

test('a running candidate prevents every other PR from dispatching', async () => {
  const api = new Fake();
  api.state.active = active();
  api.prs.push(pr(2));
  api.workflowRuns = [run({ status: 'in_progress', conclusion: null })];
  assert.equal(await step(api), 'validating');
  assert.equal(count(api, 'dispatch'), 0);
  assert.equal(count(api, 'merge'), 0);
});

test('only the selected PR is updated using its expected head before validation', async () => {
  const api = new Fake();
  api.contains = false;
  api.prs.push(pr(2));
  assert.equal(await step(api), 'updating');
  assert.equal(api.state.active.phase, 'updating');
  assert.equal(count(api, 'update'), 1);
  assert.equal(count(api, 'dispatch'), 0);
  api.prs[0].head.sha = OTHER;
  api.contains = true;
  assert.equal(await step(api), 'dispatched');
  assert.equal(api.state.active.head, OTHER);
  assert.equal(count(api, 'update'), 1);
});

test('branch-update reservations recover without repeating the update API and time out explicitly', async () => {
  const api = new Fake();
  api.state.active = active({ phase: 'updating', dispatchRecorded: false, checks: {} });
  api.contains = false;
  assert.equal(await step(api), 'updating');
  assert.equal(count(api, 'update'), 0);
  assert.equal(await step(api, { now: new Date(NOW.valueOf() + 11 * 60_000) }), 'blocked');
  assert.equal(api.state.active, null);
  assert.equal(count(api, 'dispatch'), 0);
  const conflict = new Fake();
  conflict.state.active = active({ phase: 'updating', dispatchRecorded: false, checks: {} });
  conflict.prs[0].mergeable = false;
  assert.equal(await step(conflict), 'blocked');
});

test('an interrupted pre-dispatch reservation resumes only its missing check issuance', async () => {
  const api = new Fake();
  api.state.active = active({ phase: 'reserved', dispatchRecorded: false, checks: { [GATE]: 10 } });
  assert.equal(await step(api), 'dispatched');
  assert.equal(count(api, 'check'), 3);
  assert.equal(count(api, 'dispatch'), 1);
});

for (const mutation of ['base', 'head', 'intent', 'draft', 'closed']) {
  test(`${mutation} change cancels stale validation without releasing an occupied lane`, async () => {
    const api = new Fake();
    api.state.active = active();
    api.workflowRuns = [run({ status: 'in_progress', conclusion: null })];
    if (mutation === 'base') api.base = OTHER;
    if (mutation === 'head') api.prs[0].head.sha = OTHER;
    if (mutation === 'intent') api.prs[0].auto_merge = null;
    if (mutation === 'draft') api.prs[0].draft = true;
    if (mutation === 'closed') api.prs[0].state = 'closed';
    assert.equal(await step(api), 'retiring');
    assert.equal(count(api, 'cancel'), 1);
    assert.equal(count(api, 'dispatch'), 0);
    assert.equal(count(api, 'merge'), 0);
    api.workflowRuns[0].status = 'completed';
    api.workflowRuns[0].conclusion = 'cancelled';
    assert.equal(await step(api), 'retired');
    assert.equal(api.state.active, null);
  });
}

test('a delayed dispatch cannot be mistaken for an empty released lane', async () => {
  const api = new Fake();
  api.state.active = active();
  api.base = OTHER;
  assert.equal(await step(api), 'retiring');
  assert.ok(api.state.active);
  assert.equal(await step(api, { now: new Date(NOW.valueOf() + 11 * 60_000) }), 'retiring');
  api.prs.push(pr(2));
  assert.equal(await step(api, { now: new Date(NOW.valueOf() + 60 * 60_000) }), 'retiring');
  assert.ok(api.state.active);
  assert.equal(count(api, 'dispatch'), 0);
  api.workflowRuns = [run({ status: 'in_progress' })];
  assert.equal(await step(api), 'retiring');
  assert.equal(count(api, 'cancel'), 1);
  api.workflowRuns[0].status = 'completed';
  assert.equal(await step(api), 'retired');
});

test('an invalidated reservation with no recorded dispatch can release the lane immediately', async () => {
  const api = new Fake();
  api.state.active = active({ phase: 'reserved', dispatchRecorded: false });
  api.base = OTHER;
  assert.equal(await step(api), 'retired');
  assert.equal(api.state.active, null);
});

test('all exact required jobs must pass; skipped, missing and duplicate jobs never qualify', () => {
  const jobs = JOBS.map(name => ({ name, conclusion: 'success' }));
  assert.equal(successfulJobs(jobs), true);
  assert.equal(successfulJobs(jobs.slice(1)), false);
  assert.equal(successfulJobs([...jobs, jobs[0]]), false);
  for (const conclusion of ['skipped', 'neutral', 'cancelled', 'failure', null]) {
    assert.equal(successfulJobs([{ ...jobs[0], conclusion }, ...jobs.slice(1)]), false);
  }
});

test('full successful validation is attested before the exact protected squash merge', async () => {
  const api = new Fake();
  api.state.active = active();
  api.workflowRuns = [run()];
  assert.equal(await step(api), 'merged');
  assert.equal(count(api, 'merge'), 1);
  assert.equal(api.calls.filter(call => call[0] === 'finish').length, 4);
  assert.ok(api.calls.filter(call => call[0] === 'finish').every(call => call[2] === 'success'));
  assert.equal(api.calls.find(call => call[0] === 'merge')[1].head, HEAD);
  assert.equal(api.state.active, null);
});

for (const failure of ['failure', 'cancelled', 'skipped', null]) {
  test(`workflow ${failure} blocks that revision and allows a different candidate`, async () => {
    const api = new Fake();
    api.state.active = active();
    api.workflowRuns = [run({ conclusion: failure })];
    assert.equal(await step(api), 'blocked');
    assert.equal(count(api, 'merge'), 0);
    api.prs.push(pr(2));
    api.workflowRuns = [];
    assert.equal(await step(api), 'dispatched');
    assert.equal(api.state.active.number, 2);
  });
}

test('failed heads require a new commit or explicit auto-merge re-enrolment', async () => {
  const api = new Fake();
  api.state.blocked = [{ number: 1, head: HEAD, enrollment: ENROLLED, reason: 'failure' }];
  assert.equal(await step(api), 'idle');
  api.enrolled = NOW.toISOString();
  assert.equal(await step(api), 'dispatched');
});

test('unknown enrollment never creates a reservation and re-enrolment invalidates a running ticket', async () => {
  const api = new Fake();
  api.enrolled = null;
  assert.equal(await step(api), 'idle');
  assert.equal(count(api, 'dispatch'), 0);
  api.state.active = active();
  api.workflowRuns = [run({ status: 'in_progress' })];
  api.enrolled = NOW.toISOString();
  assert.equal(await step(api), 'retiring');
  assert.equal(count(api, 'cancel'), 1);
  assert.equal(count(api, 'merge'), 0);
});

test('new commits and withdrawn queue intent retire obsolete failure records', async () => {
  const api = new Fake();
  api.state.blocked = [{ number: 1, head: OTHER, enrollment: ENROLLED, reason: 'failure' }];
  await step(api);
  assert.deepEqual(api.state.blocked, []);
});

test('unknown mergeability or unmet reviews do not occupy the lane', async () => {
  for (const condition of [null, undefined, 'unknown', 'review']) {
    const api = new Fake();
    if (condition === 'review') api.approved = false;
    else api.prs[0].mergeable = condition;
    assert.equal(await step(api), 'idle');
    assert.equal(count(api, 'dispatch'), 0);
  }
});

test('conflicting candidates produce explicit failure without launching CI', async () => {
  const api = new Fake();
  api.prs[0].mergeable = false;
  assert.equal(await step(api), 'blocked');
  assert.equal(count(api, 'dispatch'), 0);
  assert.equal(api.state.blocked.length, 1);
});

test('conflicts are quarantined in order and a later runnable candidate dispatches in the same reconciliation', async () => {
  const api = new Fake();
  api.prs = [pr(4), pr(3, { mergeable: false }), pr(2, { mergeable: false }), pr(1, { mergeable: false })];
  assert.equal(await step(api), 'dispatched');
  assert.deepEqual(api.state.blocked.map(item => item.number), [1, 2, 3]);
  assert.equal(api.state.active.number, 4);
  assert.equal(count(api, 'dispatch'), 1);
  assert.equal(count(api, 'wake'), 0);
  assert.equal(api.calls.filter(call => call[0] === 'finish' && call[2] === 'failure').length, 12);
});

test('a reconciliation reserves at most ten new candidates and checkpoints before requesting continuation', async () => {
  const api = new Fake();
  api.prs = Array.from({ length: 11 }, (_, index) => pr(index + 1, { mergeable: index === 10 }));
  assert.equal(await step(api), 'continuing');
  assert.deepEqual(api.state.blocked.map(item => item.number), [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
  assert.equal(api.state.active, null);
  assert.equal(count(api, 'check'), 40);
  assert.equal(count(api, 'dispatch'), 0);
  assert.equal(count(api, 'wake'), 1);
  assert.equal(api.calls.at(-2)[0], 'save');
  assert.equal(api.calls.at(-1)[0], 'wake');
  assert.equal(await step(api), 'dispatched');
  assert.equal(api.state.active.number, 11);
  assert.equal(count(api, 'check'), 44);
  assert.equal(count(api, 'dispatch'), 1);
  assert.equal(count(api, 'wake'), 1);
});

test('an entirely conflicting queue stops self-waking after the blocked heads are observed', async () => {
  const api = new Fake();
  api.prs = [pr(1, { mergeable: false }), pr(2, { mergeable: false })];
  assert.equal(await step(api), 'blocked');
  assert.equal(count(api, 'wake'), 1);
  assert.equal(await step(api), 'idle');
  assert.equal(count(api, 'wake'), 1);
  assert.equal(count(api, 'check'), 8);
  assert.equal(count(api, 'dispatch'), 0);
});

test('released candidate lanes request another trusted reconciliation without waiting for the schedule', async () => {
  for (const result of ['blocked', 'merged', 'already-merged', 'retired']) {
    const api = new Fake();
    api.state.active = active();
    api.workflowRuns = [run()];
    if (result === 'blocked') api.workflowRuns[0].conclusion = 'failure';
    if (result === 'already-merged') api.prs[0].merged = true;
    if (result === 'retired') api.prs[0].auto_merge = null;
    assert.equal(await step(api), result);
    assert.equal(api.state.active, null);
    assert.equal(count(api, 'wake'), 1);
    assert.equal(api.calls.findLast(call => call[0] === 'save')[1].active, null);
    assert.equal(api.calls.at(-1)[0], 'wake');
    assert.equal(count(api, 'dispatch'), 0);
  }
});

test('occupied lanes and idle queues do not create self-dispatch loops', async () => {
  for (const result of ['validating', 'waiting-for-run', 'retiring', 'updating', 'idle']) {
    const api = new Fake();
    api.state.active = active();
    api.prs.push(pr(2));
    if (result === 'validating') api.workflowRuns = [run({ status: 'in_progress', conclusion: null })];
    if (result === 'retiring') api.base = OTHER;
    if (result === 'updating') {
      api.state.active = active({ phase: 'updating', dispatchRecorded: false, checks: {} });
      api.contains = false;
    }
    if (result === 'idle') { api.state.active = null; api.prs = []; }
    assert.equal(await step(api), result);
    assert.equal(count(api, 'wake'), 0);
    assert.equal(count(api, 'dispatch'), 0);
  }
});

test('a failed continuation is explicit and cannot erase quarantines or launch duplicate validation', async () => {
  const api = new Fake();
  api.prs[0].mergeable = false;
  api.wake = async () => { throw new Error('Wakeup response lost'); };
  await assert.rejects(step(api), /Wakeup response lost/);
  assert.equal(api.state.active, null);
  assert.equal(api.state.blocked.length, 1);
  assert.equal(count(api, 'dispatch'), 0);
  assert.equal(await step(api), 'idle');
  assert.equal(count(api, 'check'), 4);
});

test('each newly reserved conflicting or runnable candidate receives a separate production ticket', async () => {
  const api = new Fake();
  api.prs = [pr(1, { mergeable: false }), pr(2)];
  assert.equal(await reconcile(api, { now: NOW }), 'dispatched');
  const tickets = new Set(api.calls.filter(call => call[0] === 'save' && call[1].active?.phase === 'reserved')
    .map(call => call[1].active.ticket));
  assert.equal(tickets.size, 2);
});

test('ambiguous dispatch failures are persisted and never retried automatically', async () => {
  const api = new Fake();
  api.dispatch = async () => { throw new Error('Network response lost'); };
  await assert.rejects(step(api), /Network response lost/);
  assert.equal(api.state.active.phase, 'dispatched');
  assert.equal(await step(api), 'waiting-for-run');
  assert.equal(await step(api, { now: new Date(NOW.valueOf() + 11 * 60_000) }), 'retiring');
  assert.equal(api.state.active.phase, 'retiring');
  assert.match(api.state.blocked[0].reason, /No candidate workflow/);
  assert.equal(await step(api, { now: new Date(NOW.valueOf() + 30 * 60_000) }), 'retiring');
  api.workflowRuns = [run({ status: 'completed', conclusion: 'cancelled' })];
  assert.equal(await step(api), 'retired');
  assert.equal(await step(api), 'idle');
});

test('duplicate tickets, unexpected workflow source and reruns fail closed', async () => {
  for (const runs of [[run(), run({ id: 10 })], [run({ head_sha: OTHER })], [run({ run_attempt: 2 })]]) {
    const api = new Fake();
    api.state.active = active();
    api.workflowRuns = runs;
    await assert.rejects(step(api));
    assert.equal(count(api, 'merge'), 0);
  }
});

test('a base advance during terminal validation cannot produce admission success', async () => {
  const api = new Fake();
  api.state.active = active();
  api.workflowRuns = [run()];
  let reads = 0;
  api.main = async () => ++reads === 1 ? BASE : OTHER;
  assert.equal(await step(api), 'retiring');
  assert.equal(count(api, 'finish'), 0);
  assert.equal(count(api, 'merge'), 0);
});

test('re-enrolment during terminal validation invalidates the old ticket even on the same head', async () => {
  const api = new Fake();
  api.state.active = active();
  api.workflowRuns = [run()];
  let reads = 0;
  api.enrollment = async () => ++reads === 1 ? ENROLLED : NOW.toISOString();
  assert.equal(await step(api), 'retiring');
  assert.equal(count(api, 'finish'), 0);
  assert.equal(count(api, 'merge'), 0);
});

test('review changes fail explicitly before attestation', async () => {
  const api = new Fake();
  api.state.active = active();
  api.workflowRuns = [run()];
  api.approved = false;
  assert.equal(await step(api), 'blocked');
  assert.equal(api.state.active, null);
  assert.equal(api.calls.filter(call => call[0] === 'finish').at(-1)[2], 'failure');
});

test('recovery recognises native auto-merge without overwriting successful evidence', async () => {
  const api = new Fake();
  api.state.active = active();
  api.prs[0].merged = true;
  api.workflowRuns = [run()];
  assert.equal(await step(api), 'already-merged');
  assert.equal(api.state.active, null);
  assert.equal(count(api, 'finish'), 0);
  assert.equal(count(api, 'merge'), 0);
});

test('merged recovery cancels an active ticket and waits for terminal evidence before another dispatch', async () => {
  const api = new Fake();
  api.state.active = active();
  api.prs = [pr(1, { state: 'closed', merged: true }), pr(2)];
  api.workflowRuns = [run({ status: 'in_progress', conclusion: null })];
  assert.equal(await step(api), 'retiring');
  assert.equal(api.state.active.phase, 'retiring');
  assert.equal(count(api, 'cancel'), 1);
  assert.equal(count(api, 'finish'), 0);
  assert.equal(count(api, 'dispatch'), 0);
  api.workflowRuns[0] = run({ conclusion: 'cancelled' });
  assert.equal(await step(api), 'already-merged');
  assert.equal(api.state.active, null);
  assert.equal(count(api, 'dispatch'), 0);
  assert.equal(await step(api), 'dispatched');
  assert.equal(api.state.active.number, 2);
});

test('merged recovery never treats an unobservable recorded dispatch as quiescent', async () => {
  for (const elapsed of [0, 11 * 60_000]) {
    const api = new Fake();
    api.state.active = active();
    api.prs = [pr(1, { state: 'closed', merged: true }), pr(2)];
    assert.equal(await step(api, { now: new Date(NOW.getTime() + elapsed) }), 'retiring');
    assert.equal(api.state.active.phase, 'retiring');
    assert.equal(count(api, 'notice'), elapsed === 0 ? 0 : 1);
    assert.equal(count(api, 'finish'), 0);
    assert.equal(count(api, 'dispatch'), 0);
  }
});

test('merged recovery can release a reservation with no recorded dispatch', async () => {
  const api = new Fake();
  api.state.active = active({ phase: 'reserved', dispatchRecorded: false });
  api.prs[0].merged = true;
  assert.equal(await step(api), 'already-merged');
  assert.equal(api.state.active, null);
  assert.equal(count(api, 'finish'), 0);
});

test('merged recovery lookup and cancellation failures preserve the occupied lane', async () => {
  for (const boundary of ['runs', 'cancel']) {
    const api = new Fake();
    api.state.active = active();
    api.prs[0].merged = true;
    api.workflowRuns = [run({ status: 'in_progress', conclusion: null })];
    api[boundary] = async () => { throw new Error(`Failed ${boundary}`); };
    await assert.rejects(step(api), new RegExp(boundary));
    assert.notEqual(api.state.active, null);
    assert.equal(count(api, 'finish'), 0);
    assert.equal(count(api, 'dispatch'), 0);
  }
});

test('a native auto-merge racing the final API response is recognised as an actual merge', async () => {
  const api = new Fake();
  api.state.active = active();
  api.workflowRuns = [run()];
  api.merge = async () => { api.prs[0].merged = true; return { merged: false }; };
  assert.equal(await step(api), 'merged');
  assert.equal(api.state.active, null);
  assert.deepEqual(api.state.blocked, []);
});

test('merge reconciliation validates persisted attempt identity and dispatch state', () => {
  for (const phase of MERGE_PHASES) {
    assert.equal(validateState({ version: 1, blocked: [],
      active: active({ phase, mergeStarted: NOW.toISOString() }) }).active.phase, phase);
    for (const changes of [{ mergeStarted: undefined }, { mergeStarted: null }, { mergeStarted: 'invalid' },
      { mergeStarted: '2026-01-01' }, { mergeStarted: '2026-01-01T12:00:00+00:00' },
      { mergeStarted: '2026-01-01T11:59:59Z' }, { dispatchRecorded: false }, { checks: {} }]) {
      assert.throws(() => validateState({ version: 1, blocked: [],
        active: active({ phase, mergeStarted: NOW.toISOString(), ...changes }) }), /Invalid/);
    }
  }
  assert.throws(() => validateState({ version: 1, blocked: [],
    active: active({ mergeStarted: NOW.toISOString() }) }), /Invalid merge reconciliation/);
  assert.equal(validateState({ version: 1, blocked: [],
    active: active({ phase: 'retiring', mergeStarted: NOW.toISOString() }) }).active.phase, 'retiring');
  assert.throws(() => validateState({ version: 1, blocked: [],
    active: active({ phase: 'retiring', mergeStarted: NOW.toISOString(), dispatchRecorded: false }) }), /Invalid/);
});

test('merge intent is committed before successful checks or a protected merge request', async () => {
  const api = new Fake();
  api.state.active = active();
  api.workflowRuns = [run()];
  api.finishCheck = async (...args) => {
    assert.equal(api.state.active.phase, 'merging');
    assert.equal(api.state.active.mergeStarted, NOW.toISOString());
    api.calls.push(['finish', ...args]);
  };
  api.merge = async () => {
    assert.equal(api.state.active.phase, 'merging');
    assert.equal(count(api, 'finish'), 4);
    return { merged: true };
  };
  assert.equal(await step(api), 'merged');
});

test('one delayed native merge observation preserves all successful checks and recovers without another request', async () => {
  const api = new Fake();
  api.state.active = active();
  api.workflowRuns = [run()];
  const read = api.pull.bind(api);
  let stale = true;
  api.pull = async number => {
    const value = await read(number);
    if (value.merged && stale) { stale = false; return { ...value, merged: false, state: 'open' }; }
    return value;
  };
  api.merge = async value => {
    api.calls.push(['merge', clone(value)]);
    api.prs[0].merged = true;
    api.prs[0].state = 'closed';
    return { merged: false };
  };
  assert.equal(await step(api), 'merge-pending');
  assert.equal(api.state.active.phase, 'merge-refused');
  assert.deepEqual(api.calls.filter(call => call[0] === 'finish').map(call => call[2]),
    ['success', 'success', 'success', 'success']);
  assert.equal(await step(api), 'already-merged');
  assert.equal(api.state.active, null);
  assert.deepEqual(api.state.blocked, []);
  assert.equal(count(api, 'merge'), 1);
  assert.equal(count(api, 'finish'), 4);
});

test('persistent explicit refusal waits to the exact deadline then fails only merge admission', async () => {
  const api = new Fake();
  api.state.active = active();
  api.workflowRuns = [run()];
  api.merge = async value => { api.calls.push(['merge', clone(value)]); return { merged: false }; };
  assert.equal(await step(api), 'merge-pending');
  assert.equal(api.state.active.phase, 'merge-refused');
  for (const elapsed of [0, MERGE_CONFIRMATION_MS - 1]) {
    assert.equal(await step(api, { now: new Date(NOW.getTime() + elapsed) }), 'merge-pending');
  }
  assert.equal(count(api, 'merge'), 1);
  assert.equal(count(api, 'finish'), 4);
  assert.equal(await step(api, { now: new Date(NOW.getTime() + MERGE_CONFIRMATION_MS) }), 'blocked');
  assert.equal(api.state.active, null);
  assert.equal(api.state.blocked.length, 1);
  assert.match(api.state.blocked[0].reason, /no merge was observed/);
  assert.deepEqual(api.calls.filter(call => call[0] === 'finish').at(-1).slice(1, 3), [4, 'failure']);
  assert.equal(count(api, 'finish'), 5);
  assert.equal(count(api, 'merge'), 1);
  assert.equal(await step(api), 'idle');
});

test('only an exact merged observation can confirm a refused request', async () => {
  for (const merged of [undefined, null, 'true', 'false', 1, 0]) {
    const api = new Fake();
    api.state.active = active();
    api.workflowRuns = [run()];
    const read = api.pull.bind(api);
    let attempted = false;
    api.merge = async () => { attempted = true; return { merged: false }; };
    api.pull = async number => {
      const value = await read(number);
      return attempted ? { ...value, merged } : value;
    };
    assert.equal(await step(api), 'merge-pending');
    assert.equal(api.state.active.phase, 'merge-refused');
    assert.equal(count(api, 'finish'), 4);
    assert.deepEqual(api.state.blocked, []);
  }
});

test('lost merge responses are explicit and retain the lane without retry even after the confirmation budget', async () => {
  const api = new Fake();
  api.state.active = active();
  api.prs.push(pr(2));
  api.workflowRuns = [run()];
  api.merge = async value => { api.calls.push(['merge', clone(value)]); throw new Error('Merge response lost'); };
  await assert.rejects(step(api), /Merge response lost/);
  assert.equal(api.state.active.phase, 'merging');
  assert.equal(await step(api), 'merge-pending');
  assert.equal(await step(api, { now: new Date(NOW.getTime() + MERGE_CONFIRMATION_MS) }), 'merge-unconfirmed');
  assert.equal(api.state.active.phase, 'merge-unconfirmed');
  const writes = count(api, 'finish');
  assert.equal(await step(api, { now: new Date(NOW.getTime() + MERGE_CONFIRMATION_MS + 1) }), 'merge-unconfirmed');
  assert.equal(count(api, 'finish'), writes);
  assert.equal(count(api, 'merge'), 1);
  assert.equal(count(api, 'dispatch'), 0);
  assert.deepEqual(api.calls.filter(call => call[0] === 'finish').at(-1).slice(1, 3), [4, 'failure']);
  api.prs[0].merged = true;
  api.prs[0].state = 'closed';
  assert.equal(await step(api), 'already-merged');
  assert.equal(api.state.active, null);
  assert.equal(count(api, 'finish'), writes);
});

test('merge attestation or response lookup failures cannot cause blind merge retries', async () => {
  for (const boundary of ['attestation', 'lookup', 'malformed']) {
    const api = new Fake();
    api.state.active = active();
    api.workflowRuns = [run()];
    let attempted = false;
    api.merge = async value => {
      attempted = true;
      api.calls.push(['merge', clone(value)]);
      return boundary === 'malformed' ? {} : { merged: false };
    };
    if (boundary === 'attestation') api.finishCheck = async () => { throw new Error('Attestation failed'); };
    const read = api.pull.bind(api);
    api.pull = async number => {
      if (boundary === 'lookup' && attempted) { throw new Error('Merge lookup failed'); }
      return read(number);
    };
    await assert.rejects(step(api), /failed|Unknown protected merge response/);
    assert.equal(api.state.active.phase, boundary === 'lookup' ? 'merge-refused' : 'merging');
    api.pull = read;
    api.finishCheck = async (...args) => api.calls.push(['finish', ...args]);
    assert.equal(await step(api), 'merge-pending');
    assert.equal(count(api, 'merge'), boundary === 'attestation' ? 0 : 1);
  }
});

test('unobservable or nonterminal validation after a merge attempt holds the lane rather than admitting another candidate', async () => {
  for (const missing of [true, false]) {
    const api = new Fake();
    api.state.active = active({ phase: 'merge-refused', mergeStarted: NOW.toISOString() });
    api.prs.push(pr(2));
    api.workflowRuns = missing ? [] : [run({ status: 'in_progress', conclusion: null })];
    assert.equal(await step(api), 'merge-pending');
    assert.equal(await step(api, { now: new Date(NOW.getTime() + MERGE_CONFIRMATION_MS) }), 'merge-unconfirmed');
    assert.equal(api.state.active.number, 1);
    assert.equal(count(api, 'merge'), 0);
    assert.equal(count(api, 'dispatch'), 0);
    assert.deepEqual(api.calls.filter(call => call[0] === 'finish').at(-1).slice(1, 3), [4, 'failure']);
    const writes = count(api, 'finish');
    assert.equal(await step(api, { now: new Date(NOW.getTime() + MERGE_CONFIRMATION_MS + 1) }), 'merge-unconfirmed');
    assert.equal(count(api, 'finish'), writes);
  }
});

test('merge reconciliation still enforces source intent and reviews without rewriting validated job checks', async () => {
  for (const boundary of ['head', 'base', 'intent', 'enrollment', 'review']) {
    const api = new Fake();
    api.state.active = active();
    api.workflowRuns = [run()];
    api.merge = async value => { api.calls.push(['merge', clone(value)]); return { merged: false }; };
    assert.equal(await step(api), 'merge-pending');
    if (boundary === 'head') api.prs[0].head.sha = OTHER;
    if (boundary === 'base') api.base = OTHER;
    if (boundary === 'intent') api.prs[0].auto_merge = null;
    if (boundary === 'enrollment') api.enrolled = NOW.toISOString();
    if (boundary === 'review') api.approved = false;
    assert.equal(await step(api), boundary === 'review' ? 'blocked' : 'retired');
    assert.equal(api.state.active, null);
    assert.equal(count(api, 'merge'), 1);
    const writes = api.calls.filter(call => call[0] === 'finish');
    assert.equal(writes.length, 5);
    assert.equal(writes.at(-1)[1], 4);
  }
});

test('merge reconciliation rejects backwards clocks and ambiguous candidate run identity', async () => {
  for (const boundary of ['clock', 'duplicate', 'attempt', 'source']) {
    const api = new Fake();
    api.state.active = active({ phase: 'merging', mergeStarted: NOW.toISOString() });
    api.workflowRuns = [run()];
    if (boundary === 'duplicate') api.workflowRuns.push(run({ id: 10 }));
    if (boundary === 'attempt') api.workflowRuns[0].run_attempt = 2;
    if (boundary === 'source') api.workflowRuns[0].head_sha = OTHER;
    await assert.rejects(step(api, { now: new Date(NOW.getTime() - (boundary === 'clock' ? 1 : 0)) }),
      /clock moved|Multiple candidate runs|source or attempt changed/);
    assert.notEqual(api.state.active, null);
    assert.equal(count(api, 'finish'), 0);
    assert.equal(count(api, 'merge'), 0);
  }
});

test('protection and state persistence errors never turn into success or a second dispatch', async () => {
  for (const boundary of ['verifyProtection', 'save', 'createCheck']) {
    const api = new Fake();
    api[boundary] = async () => { throw new Error(`Failed ${boundary}`); };
    await assert.rejects(step(api), new RegExp(boundary));
    assert.equal(count(api, 'dispatch'), 0);
    assert.equal(count(api, 'merge'), 0);
  }
});
