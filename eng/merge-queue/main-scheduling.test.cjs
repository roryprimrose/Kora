const { test } = require('node:test');
const assert = require('node:assert/strict');
const { mainBuildProfile, validateHourlyState, observeAttempts, admitScheduled } = require('./main-scheduling.cjs');

const sha = n => n.toString(16).repeat(40);
const BASE = sha(1);
const SOURCE = sha(2);
const NEXT = sha(3);
const TREE = sha(4);
const REVISION = sha(10);
const MAIN = sha(15);
const ACTIVATED = '2026-11-05T00:00:00.000Z';
const CREATED = '2026-11-05T01:05:00.000Z';
const repository = 'owner/Kora';
const clone = value => structuredClone(value);

function state() {
  return { mainBuildProfile: 'hourly-v1', hourlyMain: {
    baseline: { source: BASE, tree: TREE, qualification: { run: 1, job: 2, source: BASE } },
    activated: ACTIVATED,
  } };
}

function run(id = 100, source = SOURCE, created = CREATED, extra = {}) {
  return { id, head_sha: source, created_at: created, run_attempt: 1, head_branch: 'main',
    event: 'schedule', display_title: 'CI main', path: '.github/workflows/ci.yml',
    repository: { full_name: repository }, head_repository: { full_name: repository },
    status: 'completed', conclusion: 'success', ...extra };
}

function version(value, extra = {}) {
  return { id: value.id + 1000, run_id: value.id, run_attempt: 1, head_sha: value.head_sha,
    name: 'Resolve build version', status: 'completed', conclusion: 'success', ...extra };
}

function record(source) {
  return { schema: 'completed-source-v1', source, tree: TREE,
    ticket: '01234567-89ab-cdef-0123-456789abcdef', base: sha(5), combined: sha(6),
    validation: { run: 10, attempt: 1, source: sha(6), workflow: sha(5),
      inventory: sha(7), schema: 'unit-pr-v1' } };
}

function fixture(current = run(), history = []) {
  const calls = { pages: [], jobs: [], releases: [], reachable: [], attempts: [], messages: [] };
  const runs = [...history, current];
  const jobs = new Map(runs.map(value => [value.id, [version(value)]]));
  const originals = new Map();
  const records = new Map([BASE, SOURCE, NEXT].map(source => [source, record(source)]));
  const api = {
    commit: REVISION, repo: { owner: 'owner', repo: 'Kora' }, repository,
    github: { rest: { actions: {
      listWorkflowRuns() {},
      async getWorkflowRun(params) {
        assert.deepEqual(params, { ...api.repo, run_id: current.id });
        return { data: clone(current) };
      },
      async getWorkflowRunAttempt(params) {
        calls.attempts.push(params);
        assert.equal(params.attempt_number, 1);
        assert.ok(originals.has(params.run_id), 'Missing exact first-attempt fixture.');
        return { data: clone(originals.get(params.run_id)) };
      },
    } } },
    async completePages(endpoint, params, label) {
      assert.equal(endpoint, api.github.rest.actions.listWorkflowRuns);
      assert.equal(params.workflow_id, 'ci.yml');
      assert.equal(params.branch, 'main');
      assert.equal(label, 'hourly main run');
      assert.equal(Object.keys(params).length, 3);
      calls.pages.push(params);
      return clone(runs);
    },
    async jobs(id, attempt) {
      assert.equal(attempt, 1);
      calls.jobs.push(id);
      assert.ok(jobs.has(id), 'Missing exact run jobs fixture.');
      return clone(jobs.get(id));
    },
    async releaseSource(source, revision) {
      assert.equal(revision, REVISION);
      calls.releases.push(source);
      assert.ok(records.has(source), 'Missing immutable source fixture.');
      return clone(records.get(source));
    },
    async main() { return MAIN; },
    async containsBase(source, main) {
      assert.equal(main, MAIN);
      calls.reachable.push(source);
      return true;
    },
    info(message) { calls.messages.push(message); },
  };
  return { api, calls, runs, jobs, originals, records, state: state(),
    context: { eventName: 'schedule', ref: 'refs/heads/main',
      sha: current.head_sha, runId: current.id, runAttempt: 1 } };
}

function attempted(value) {
  return { run: value.id, source: value.head_sha, created: new Date(value.created_at).toISOString(),
    event: value.event };
}

test('only absent/empty repository values select per-merge; unknown modes fail closed', () => {
  assert.equal(mainBuildProfile(), 'per-merge');
  assert.equal(mainBuildProfile(''), 'per-merge');
  assert.equal(mainBuildProfile('hourly-v1'), 'hourly-v1');
  for (const value of ['per-merge', 'hourly', ' hourly-v1', 'HOURLY-V1', null, false, 1, {}])
    assert.throws(() => mainBuildProfile(value), /Unknown main build profile/);
});

test('durable hourly state is optional, canonical, and separate from parent transition validation', () => {
  assert.deepEqual(validateHourlyState({}), {});
  const value = state();
  value.mainSchedulingTransition = { validatedByParent: true };
  assert.equal(validateHourlyState(value), value);
  value.hourlyMain.lastAttempt = attempted(run());
  value.hourlyMain.observedThrough = { run: 100, created: CREATED };
  assert.equal(validateHourlyState(value), value);
  for (const invalid of [null, [], { hourlyMain: value.hourlyMain },
    { mainBuildProfile: '' }, { mainBuildProfile: 'per-merge' }, { mainBuildProfile: 'future' },
    { mainBuildProfile: 'hourly-v1' }])
    assert.throws(() => validateHourlyState(invalid));
});

test('baseline, qualification, activation, attempt and cursor malformed values are rejected', () => {
  const changes = [
    value => { value.hourlyMain = []; },
    value => { value.hourlyMain.extra = true; },
    value => { value.hourlyMain.activated = '2026-11-05T00:00:00Z'; },
    value => { value.hourlyMain.activated = '2026-02-30T00:00:00.000Z'; },
    value => { value.hourlyMain.baseline.source = [BASE]; },
    value => { value.hourlyMain.baseline.tree = ''; },
    value => { value.hourlyMain.baseline.qualification.source = SOURCE; },
    value => { value.hourlyMain.baseline.qualification.run = 0; },
    value => { value.hourlyMain.baseline.qualification.job = 1.5; },
    value => { value.hourlyMain.baseline.qualification = null; },
    value => { value.hourlyMain.lastAttempt = { ...attempted(run()), event: 'push' }; },
    value => { value.hourlyMain.lastAttempt = { ...attempted(run()), run: '100' }; },
    value => { value.hourlyMain.lastAttempt = { ...attempted(run()), source: 'unknown' }; },
    value => { value.hourlyMain.lastAttempt = { ...attempted(run()), created: '2026-11-04T23:59:59.000Z' }; },
    value => { value.hourlyMain.lastAttempt = { ...attempted(run()), job: 1 }; },
    value => { value.hourlyMain.observedThrough = { run: -1, created: CREATED }; },
    value => { value.hourlyMain.observedThrough = { run: 1, created: '2026-11-05T01:05:00Z' }; },
    value => { value.hourlyMain.observedThrough = { run: 1, created: '2026-11-04T00:00:00.000Z' }; },
    value => {
      value.hourlyMain.lastAttempt = attempted(run());
      value.hourlyMain.observedThrough = { run: 100, created: ACTIVATED };
    },
  ];
  for (const change of changes) {
    const value = state();
    change(value);
    assert.throws(() => validateHourlyState(value), change.toString());
  }
});

test('per-merge observations do no work, but scheduled admission requires durable hourly activation', async () => {
  assert.equal(await observeAttempts({}, {}), false);
  const value = fixture();
  await assert.rejects(admitScheduled(value.api, value.context, {}), /active hourly profile/);
});

test('qualified baseline and last actually attempted source suppress unchanged main', async () => {
  for (const baseline of [true, false]) {
    const value = fixture(run(100, baseline ? BASE : SOURCE));
    if (!baseline) value.state.hourlyMain.lastAttempt = attempted(run(90, SOURCE, ACTIVATED));
    assert.equal(await admitScheduled(value.api, value.context, value.state), false);
    assert.deepEqual(value.calls.releases, [value.context.sha]);
    assert.deepEqual(value.calls.reachable, [value.context.sha]);
    assert.equal(value.calls.pages.length, 0);
    assert.match(value.calls.messages[0], baseline ? /qualified-baseline/ : /unchanged-source/);
  }
});

test('a changed trusted trigger snapshot is admitted read-only even when main advanced afterwards', async () => {
  const value = fixture();
  const before = clone(value.state);
  assert.equal(await admitScheduled(value.api, value.context, value.state), true);
  assert.deepEqual(value.state, before);
  assert.deepEqual(value.calls.jobs, []);
  assert.match(value.calls.messages[0], /changed-source/);
});

test('ready administrator hold suppresses work without interpreting transition metadata', async () => {
  const value = fixture();
  value.state.mainSchedulingTransition = { phase: 'ready' };
  assert.equal(await admitScheduled(value.api, value.context, value.state), false);
  assert.match(value.calls.messages[0], /administrator-hold/);
});

test('actual attempts throttle their UTC trigger slot, not a rolling launch-time hour', async () => {
  const prior = run(90, NEXT, '2026-11-05T01:00:00.000Z');
  const value = fixture();
  value.state.hourlyMain.lastAttempt = attempted(prior);
  assert.equal(await admitScheduled(value.api, value.context, value.state), false);
  const nextHour = fixture(run(101, SOURCE, '2026-11-05T02:00:00.000Z'));
  nextHour.state.hourlyMain.lastAttempt = attempted(run(90, NEXT, '2026-11-05T01:59:59.999Z'));
  assert.equal(await admitScheduled(nextHour.api, nextHour.context, nextHour.state), true);
});

test('newer actual work suppresses delayed older snapshots', async () => {
  const value = fixture();
  value.state.hourlyMain.lastAttempt = attempted(run(101, NEXT, '2026-11-05T02:00:00.000Z'));
  assert.equal(await admitScheduled(value.api, value.context, value.state), false);
  assert.match(value.calls.messages[0], /newer-attempt/);
});

test('context, reruns, wrong source/ref/event and real run identity mismatches fail closed', async () => {
  for (const change of [
    { eventName: 'workflow_dispatch' }, { eventName: 'push' }, { ref: 'refs/tags/v1' },
    { ref: 'refs/heads/feature' }, { sha: NEXT }, { sha: [SOURCE] }, { runId: 99 },
    { runAttempt: 2 }, { runAttempt: '1' },
  ]) {
    const value = fixture();
    await assert.rejects(admitScheduled(value.api, { ...value.context, ...change }, value.state));
  }
  for (const change of [
    { id: 99 }, { run_attempt: 2 }, { head_sha: NEXT }, { event: 'workflow_dispatch' },
    { head_branch: 'other' }, { path: '.github/workflows/other.yml' }, { display_title: 'CI candidate' },
    { repository: { full_name: 'foreign/Kora' } }, { head_repository: { full_name: 'foreign/Kora' } },
    { created_at: '2026-11-04T23:00:00Z' }, { status: 'unknown' }, { conclusion: null },
  ]) {
    const actual = run(100, SOURCE, CREATED, change);
    const value = fixture(actual);
    value.context.runId = 100;
    value.context.sha = SOURCE;
    value.api.github.rest.actions.getWorkflowRun = async () => ({ data: actual });
    await assert.rejects(admitScheduled(value.api, value.context, value.state), JSON.stringify(change));
  }
});

test('invalid pinned state/repository, unavailable main or unreachable source never authorizes work', async () => {
  for (const change of [
    api => { api.commit = null; },
    api => { api.commit = [REVISION]; },
    api => { api.repository = 'foreign/Kora'; },
    api => { api.main = async () => null; },
    api => { api.containsBase = async () => false; },
    api => { api.containsBase = async () => undefined; },
  ]) {
    const value = fixture();
    change(value.api);
    await assert.rejects(admitScheduled(value.api, value.context, value.state));
  }
});

test('immutable release proof is required even for baseline/no-change suppression', async () => {
  for (const source of [BASE, SOURCE]) {
    for (const change of [{ source: NEXT }, { schema: 'unknown' }, { tree: '' },
      { validation: { run: 1, attempt: 2 } }]) {
      const value = fixture(run(100, source));
      Object.assign(value.records.get(source), change);
      await assert.rejects(admitScheduled(value.api, value.context, value.state));
    }
  }
  const value = fixture(run(100, BASE));
  value.records.get(BASE).tree = NEXT;
  await assert.rejects(admitScheduled(value.api, value.context, value.state), /baseline tree changed/);
});

test('full failure/cancellation and queued/started version jobs count as attempts, not qualifications', async () => {
  for (const outcome of ['failure', 'cancelled', 'timed_out', 'success']) {
    const current = run(100, SOURCE, CREATED, { conclusion: outcome });
    const value = fixture(current);
    value.jobs.set(100, [version(current, { conclusion: outcome })]);
    const baseline = clone(value.state.hourlyMain.baseline);
    assert.equal(await observeAttempts(value.api, value.state), true);
    assert.deepEqual(value.state.hourlyMain.lastAttempt, attempted(current));
    assert.deepEqual(value.state.hourlyMain.baseline, baseline);
    assert.deepEqual(value.state.hourlyMain.observedThrough, { run: 100, created: CREATED });
    assert.equal(await observeAttempts(value.api, value.state), false);
    assert.equal(await admitScheduled(value.api, value.context, value.state), false);
  }
  for (const status of ['queued', 'in_progress']) {
    const current = run(100, SOURCE, CREATED, { status, conclusion: null });
    const value = fixture(current);
    value.jobs.set(100, [version(current, { status, conclusion: null })]);
    assert.equal(await observeAttempts(value.api, value.state), true);
    assert.equal(value.state.hourlyMain.lastAttempt.source, SOURCE);
  }
});

test('skipped version jobs are definitive no-work; missing/waiting jobs are not attempts or cursor progress', async () => {
  for (const status of ['skipped', 'missing', 'waiting']) {
    const current = run();
    const value = fixture(current);
    value.jobs.set(100, status === 'missing' ? [] : [version(current,
      status === 'skipped' ? { conclusion: 'skipped' } : { status: 'waiting', conclusion: null })]);
    assert.equal(await observeAttempts(value.api, value.state), status === 'skipped');
    assert.equal(value.state.hourlyMain.lastAttempt, undefined);
    assert.equal(Boolean(value.state.hourlyMain.observedThrough), status === 'skipped');
    assert.equal(value.calls.releases.length, 0);
  }
});

test('candidate, comparison, push, and preactivation runs do not establish main attempts', async () => {
  const current = run(100, SOURCE, CREATED, { display_title: 'CI candidate 42' });
  const history = [
    run(90, NEXT, CREATED, { display_title: 'CI portable comparison' }),
    run(91, NEXT, CREATED, { event: 'push' }),
    run(92, NEXT, '2026-11-04T23:59:59Z'),
  ];
  const value = fixture(current, history);
  assert.equal(await observeAttempts(value.api, value.state), false);
  assert.deepEqual(value.calls.jobs, []);
});

test('observation is chronological, newest attempt wins, cursor never passes an unresolved earlier run', async () => {
  const first = run(90, SOURCE, '2026-11-05T00:01:00Z');
  const missing = run(91, SOURCE, '2026-11-05T00:02:00Z');
  const last = run(100, NEXT, CREATED);
  const value = fixture(last, [missing, first]);
  value.jobs.set(91, []);
  assert.equal(await observeAttempts(value.api, value.state), true);
  assert.deepEqual(value.state.hourlyMain.lastAttempt, attempted(last));
  assert.deepEqual(value.state.hourlyMain.observedThrough, { run: 90, created: attempted(first).created });
  assert.deepEqual(value.calls.jobs, [90, 91, 100]);
  value.calls.jobs.length = 0;
  value.jobs.set(91, [version(missing, { conclusion: 'skipped' })]);
  assert.equal(await observeAttempts(value.api, value.state), true);
  assert.deepEqual(value.calls.jobs, [91, 100]);
  assert.deepEqual(value.state.hourlyMain.observedThrough, { run: 100, created: CREATED });
  assert.equal(value.calls.pages[1].created, '>=2026-11-05T00:01:00.000Z');
});

test('same-timestamp runs use run IDs, and a pending first run holds back all cursor progress', async () => {
  const current = run(100, SOURCE, CREATED);
  const earlier = run(90, NEXT, CREATED);
  const value = fixture(current, [earlier]);
  value.jobs.set(90, []);
  assert.equal(await observeAttempts(value.api, value.state), true);
  assert.equal(value.state.hourlyMain.lastAttempt.run, 100);
  assert.equal(value.state.hourlyMain.observedThrough, undefined);
  value.jobs.set(90, [version(earlier)]);
  assert.equal(await observeAttempts(value.api, value.state), true);
  assert.equal(value.state.hourlyMain.lastAttempt.run, 100);
  assert.equal(value.state.hourlyMain.observedThrough.run, 100);
});

test('older observations never replace newer recorded attempts, including reordered JSON keys', async () => {
  const value = fixture();
  value.state.hourlyMain.lastAttempt = {
    event: 'schedule', created: CREATED, source: SOURCE, run: 100,
  };
  assert.equal(await observeAttempts(value.api, value.state), true);
  assert.equal(value.state.hourlyMain.lastAttempt.run, 100);
  assert.equal(await observeAttempts(value.api, value.state), false);
});

test('earlier full attempts discovered beyond the cursor suppress same source and same-slot duplicates', async () => {
  for (const [source, created] of [
    [SOURCE, '2026-11-05T00:55:00Z'], [NEXT, '2026-11-05T01:01:00Z'],
  ]) {
    for (const event of ['schedule', 'workflow_dispatch']) {
      const earlier = run(90, source, created, { event, conclusion: 'failure' });
      const value = fixture(run(), [earlier]);
      value.jobs.set(90, [version(earlier, { conclusion: 'failure' })]);
      assert.equal(await admitScheduled(value.api, value.context, value.state), false);
      assert.equal(value.state.hourlyMain.lastAttempt, undefined);
    }
  }
});

test('skipped earlier schedules do not consume a slot; an earlier different source in a prior slot permits work', async () => {
  const earlier = run(90, SOURCE, CREATED);
  const value = fixture(run(), [earlier]);
  value.jobs.set(90, [version(earlier, { conclusion: 'skipped' })]);
  assert.equal(await admitScheduled(value.api, value.context, value.state), true);
  const previousHour = fixture(run(), [run(90, NEXT, '2026-11-05T00:59:59Z')]);
  assert.equal(await admitScheduled(previousHour.api, previousHour.context, previousHour.state), true);
});

test('earlier missing metadata blocks a same-slot or same-source queued duplicate without counting work', async () => {
  for (const [source, created] of [
    [NEXT, '2026-11-05T01:01:00Z'], [SOURCE, '2026-11-05T00:59:59Z'],
  ]) {
    const earlier = run(90, source, created, { status: 'queued', conclusion: null });
    const value = fixture(run(), [earlier]);
    value.jobs.set(90, []);
    assert.equal(await admitScheduled(value.api, value.context, value.state), false);
    assert.equal(value.state.hourlyMain.lastAttempt, undefined);
    assert.match(value.calls.messages[0], /earlier-run-unresolved/);
  }
});

test('concurrent-ish schedules elect the earlier trigger; a later actual attempt prevents duplicate work', async () => {
  const earlier = run(90, SOURCE, CREATED, { status: 'in_progress', conclusion: null });
  const later = run(100, SOURCE, CREATED, { status: 'queued', conclusion: null });
  const first = fixture(earlier, [later]);
  first.jobs.set(90, []);
  first.jobs.set(100, []);
  assert.equal(await admitScheduled(first.api, first.context, first.state), true);
  const second = fixture(later, [earlier]);
  second.jobs.set(90, []);
  second.jobs.set(100, []);
  assert.equal(await admitScheduled(second.api, second.context, second.state), false);
  first.jobs.set(100, [version(later, { status: 'queued', conclusion: null })]);
  assert.equal(await admitScheduled(first.api, first.context, first.state), false);
});

test('history reruns resolve exact immutable first-attempt metadata, never rerun job evidence', async () => {
  const original = run(90, SOURCE, '2026-11-05T00:55:00Z', { conclusion: 'failure' });
  const rerun = { ...original, run_attempt: 2, conclusion: 'success' };
  const value = fixture(run(), [rerun]);
  value.originals.set(90, original);
  value.jobs.set(90, [version(original, { conclusion: 'failure' })]);
  assert.equal(await admitScheduled(value.api, value.context, value.state), false);
  assert.equal(value.calls.attempts.length, 1);
  for (const change of [{ run_attempt: 2 }, { head_sha: NEXT }, { id: 91 },
    { created_at: CREATED }, { head_repository: { full_name: 'foreign/Kora' } }]) {
    value.originals.set(90, { ...original, ...change });
    await assert.rejects(admitScheduled(value.api, value.context, value.state));
  }
});

test('observation handles full failed first attempts even when a rerun is now listed', async () => {
  const current = run(100, SOURCE, CREATED, { run_attempt: 3 });
  const value = fixture(current);
  const original = { ...current, run_attempt: 1, conclusion: 'failure' };
  value.originals.set(100, original);
  value.jobs.set(100, [version(original, { conclusion: 'failure' })]);
  assert.equal(await observeAttempts(value.api, value.state), true);
  assert.deepEqual(value.state.hourlyMain.lastAttempt, attempted(original));
});

test('foreign/malformed API run metadata and duplicate run identities never advance state', async () => {
  for (const change of [
    { id: 0 }, { run_attempt: undefined }, { run_attempt: 0 }, { head_sha: '' }, { head_sha: [SOURCE] },
    { event: undefined }, { display_title: undefined }, { head_branch: 'other' },
    { repository: undefined }, { head_repository: { full_name: 'foreign/Kora' } },
    { path: undefined }, { created_at: 'tomorrow' }, { created_at: '2026-02-30T00:00:00Z' },
    { status: undefined }, { conclusion: 'unknown' },
  ]) {
    const value = fixture(run(100, SOURCE, CREATED, change));
    const before = clone(value.state);
    await assert.rejects(observeAttempts(value.api, value.state), JSON.stringify(change));
    assert.deepEqual(value.state, before);
  }
  const value = fixture(run(), [run()]);
  await assert.rejects(observeAttempts(value.api, value.state), /duplicate/);
});

test('job uniqueness, exact run/source/attempt and known statuses are required', async () => {
  for (const change of [
    { id: 0 }, { run_id: 99 }, { run_attempt: undefined }, { run_attempt: 2 },
    { head_sha: NEXT }, { name: undefined }, { status: 'unknown' },
    { conclusion: null }, { status: 'queued', conclusion: 'success' },
  ]) {
    const current = run();
    const value = fixture(current);
    value.jobs.set(100, [version(current, change)]);
    await assert.rejects(observeAttempts(value.api, value.state), JSON.stringify(change));
  }
  for (const duplicateName of [true, false]) {
    const current = run();
    const value = fixture(current);
    value.jobs.set(100, [version(current), version(current,
      duplicateName ? { id: 9999 } : { name: 'Other job' })]);
    await assert.rejects(observeAttempts(value.api, value.state), /Duplicate|duplicate/);
  }
});

test('cursor and recorded attempt immutable identities cannot silently change', async () => {
  const value = fixture();
  value.state.hourlyMain.observedThrough = { run: 100, created: ACTIVATED };
  await assert.rejects(observeAttempts(value.api, value.state), /cursor run identity changed/);
  delete value.state.hourlyMain.observedThrough;
  value.state.hourlyMain.lastAttempt = attempted(run(100, NEXT));
  await assert.rejects(observeAttempts(value.api, value.state), /recorded attempt identity changed/);
});

test('permission failures and incomplete pagination propagate, never becoming no-change success', async () => {
  for (const operation of ['get', 'pages', 'jobs', 'release', 'main', 'reachable', 'attempt']) {
    const earlier = run(90, NEXT, CREATED, { run_attempt: operation === 'attempt' ? 2 : 1 });
    const value = fixture(run(), [earlier]);
    const denied = async () => { const error = new Error('Forbidden'); error.status = 403; throw error; };
    if (operation === 'get') value.api.github.rest.actions.getWorkflowRun = denied;
    if (operation === 'pages') value.api.completePages = denied;
    if (operation === 'jobs') value.api.jobs = denied;
    if (operation === 'release') value.api.releaseSource = denied;
    if (operation === 'main') value.api.main = denied;
    if (operation === 'reachable') value.api.containsBase = denied;
    if (operation === 'attempt') value.api.github.rest.actions.getWorkflowRunAttempt = denied;
    const before = clone(value.state);
    await assert.rejects(admitScheduled(value.api, value.context, value.state), /Forbidden/);
    assert.deepEqual(value.state, before);
  }
  const value = fixture();
  value.api.completePages = async () => { throw new Error('Incomplete or inconsistent hourly main run pagination.'); };
  await assert.rejects(observeAttempts(value.api, value.state), /pagination/);
});

test('observation commits no partial mutations when a later source proof read fails', async () => {
  const value = fixture(run(100, NEXT), [run(90, SOURCE, ACTIVATED)]);
  const before = clone(value.state);
  value.records.delete(NEXT);
  await assert.rejects(observeAttempts(value.api, value.state), /Missing immutable source fixture/);
  assert.deepEqual(value.state, before);
  assert.deepEqual(value.calls.jobs, [90, 100]);
});

test('complete observations have no arbitrary run cap and queries stay cursor-bounded and inclusive', async () => {
  const history = Array.from({ length: 130 }, (_, index) =>
    run(index + 10, SOURCE, new Date(Date.parse(ACTIVATED) + index * 1000).toISOString()));
  const value = fixture(run(1000, NEXT, CREATED), history.reverse());
  assert.equal(await observeAttempts(value.api, value.state), true);
  assert.equal(value.calls.jobs.length, 131);
  assert.equal(value.state.hourlyMain.lastAttempt.run, 1000);
  assert.equal(value.calls.pages[0].created, `>=${ACTIVATED}`);
  assert.equal(await observeAttempts(value.api, value.state), false);
  assert.equal(value.calls.jobs.length, 131);
  assert.equal(value.calls.pages[1].created, `>=${CREATED}`);
});
