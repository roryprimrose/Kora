const { test } = require('node:test');
const assert = require('node:assert/strict');
const { reconcile } = require('./controller.cjs');
const { CHECKS, GATE, JOBS, mode, validateState, ready, title, successfulJobs } = require('./policy.cjs');

const BASE = 'a'.repeat(40);
const HEAD = 'b'.repeat(40);
const OTHER = 'c'.repeat(40);
const TICKET = '01234567-89ab-cdef-0123-456789abcdef';
const NOW = new Date('2026-01-01T12:00:00Z');
const ENROLLED = '2026-01-01T11:00:00Z';
const clone = value => structuredClone(value);
function pr(number = 1, overrides = {}) {
  return { number, state: 'open', draft: false, merged: false, mergeable: true,
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
  async cancel(id) { this.calls.push(['cancel', id]); }
  async updateBranch(value) { this.calls.push(['update', clone(value)]); }
  async merge(value) { this.calls.push(['merge', clone(value)]); return { merged: true }; }
  notice(message) { this.calls.push(['notice', message]); }
}
const step = (api, options = {}) => reconcile(api, { now: NOW, ticket: TICKET, ...options });
const count = (api, action) => api.calls.filter(call => call[0] === action).length;

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

test('review changes and protected merge refusal fail explicitly', async () => {
  for (const condition of ['review', 'merge']) {
    const api = new Fake();
    api.state.active = active();
    api.workflowRuns = [run()];
    if (condition === 'review') api.approved = false;
    else api.merge = async () => ({ merged: false });
    assert.equal(await step(api), 'blocked');
    assert.equal(api.state.active, null);
    assert.equal(api.calls.filter(call => call[0] === 'finish').at(-1)[2], 'failure');
  }
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

test('protection and state persistence errors never turn into success or a second dispatch', async () => {
  for (const boundary of ['verifyProtection', 'save', 'createCheck']) {
    const api = new Fake();
    api[boundary] = async () => { throw new Error(`Failed ${boundary}`); };
    await assert.rejects(step(api), new RegExp(boundary));
    assert.equal(count(api, 'dispatch'), 0);
    assert.equal(count(api, 'merge'), 0);
  }
});
