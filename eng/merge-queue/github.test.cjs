const { test } = require('node:test');
const assert = require('node:assert/strict');
const { GitHubQueue, admit } = require('./github.cjs');
const { CHECKS, GATE, STATE_BRANCH } = require('./policy.cjs');
const BASE = 'a'.repeat(40);
const HEAD = 'b'.repeat(40);
const OTHER = 'c'.repeat(40);
const TICKET = '01234567-89ab-cdef-0123-456789abcdef';
const ENROLLED = '2026-01-01T00:00:00Z';
const context = { repo: { owner: 'owner', repo: 'repo' }, eventName: 'workflow_dispatch',
  ref: 'refs/heads/main', sha: BASE };
const candidate = () => ({ number: 1, head: HEAD, base: BASE, ticket: TICKET,
  created: '2026-01-01T00:00:00Z', enrollment: ENROLLED, phase: 'dispatched', dispatchRecorded: true, checks: {} });
const state = () => ({ version: 1, active: candidate(), blocked: [] });
const pr = () => ({ state: 'open', draft: false, base: { ref: 'main' },
  head: { sha: HEAD, repo: { full_name: 'owner/repo' } }, auto_merge: { merge_method: 'squash' } });
const inputs = () => ({ queue_pr: '1', queue_head: HEAD, queue_base: BASE, queue_ticket: TICKET, app_id: '42' });
const error = status => Object.assign(new Error(`HTTP ${status}`), { status });

function fixture(overrides = {}) {
  const calls = [];
  const responses = {
    getRef: ({ ref }) => ({ object: { sha: ref === 'heads/main' ? BASE : OTHER } }),
    getContent: () => ({ type: 'file', encoding: 'base64', content: Buffer.from(JSON.stringify(state())).toString('base64') }),
    getBranchProtection: () => ({ enforce_admins: { enabled: true },
      required_status_checks: { strict: true, checks: [...CHECKS, GATE].map(name => ({ context: name, app_id: 42 })) } }),
    compareCommits: () => ({ status: 'ahead' }),
    get: () => pr(),
    create: () => ({ id: 77, app: { id: 42 } }),
    createBlob: () => ({ sha: 'blob' }), createTree: () => ({ sha: 'tree' }),
    createCommit: () => ({ sha: 'newcommit' }), merge: () => ({ merged: true }),
    getWorkflowRun: () => ({ status: 'completed' }),
    ...overrides,
  };
  const groups = {
    git: ['getRef', 'createBlob', 'createTree', 'createCommit', 'updateRef', 'createRef'],
    repos: ['getContent', 'getBranchProtection', 'compareCommits'],
    pulls: ['list', 'get', 'updateBranch', 'merge'],
    checks: ['create', 'update'],
    actions: ['createWorkflowDispatch', 'listWorkflowRuns', 'listJobsForWorkflowRunAttempt', 'cancelWorkflowRun', 'getWorkflowRun'],
  };
  const rest = {};
  for (const [group, names] of Object.entries(groups)) {
    rest[group] = Object.fromEntries(names.map(name => [name, async args => {
      calls.push([name, args]);
      return { data: await (responses[name]?.(args) ?? {}) };
    }]));
  }
  const github = { rest,
    paginate: async (method, args, transform) => {
      const response = await method(args);
      const data = Array.isArray(response.data) ? response.data : response.data.workflow_runs ?? response.data.jobs;
      assert.ok(Array.isArray(data), 'Octokit pagination returns a normalized array.');
      if (!transform) return data;
      const normalized = Object.assign([...data], { total_count: response.data.total_count });
      return [...transform({ ...response, data: normalized })];
    },
    graphql: async (query, args) => {
      calls.push(['graphql', args]);
      return { repository: { pullRequest: { reviewDecision: null, autoMergeRequest: { enabledAt: ENROLLED } } } };
    } };
  const core = { warning: message => calls.push(['warning', message]),
    info: message => calls.push(['info', message]) };
  return { github, core, calls, responses, api: new GitHubQueue(github, context, core, '42') };
}

test('App check protection must include every required identity, strict freshness and administrators', async () => {
  await fixture().api.verifyProtection();
  for (const missing of ['strict', 'admins', ...CHECKS, GATE, 'source']) {
    const value = fixture();
    const protection = value.responses.getBranchProtection();
    if (missing === 'strict') protection.required_status_checks.strict = false;
    else if (missing === 'admins') protection.enforce_admins.enabled = false;
    else if (missing === 'source') protection.required_status_checks.checks[0].app_id = 123;
    else protection.required_status_checks.checks = protection.required_status_checks.checks.filter(item => item.context !== missing);
    value.responses.getBranchProtection = () => protection;
    await assert.rejects(value.api.verifyProtection());
  }
  for (const id of ['', 'unknown', '-1', '0']) assert.throws(() => new GitHubQueue({}, context, {}, id));
});

test('only a genuinely missing state ref bootstraps; access failures and malformed state propagate', async () => {
  const missing = fixture({ getRef: () => { throw error(404); } });
  assert.deepEqual(await missing.api.load(), { version: 1, active: null, blocked: [] });
  for (const status of [401, 403, 500]) await assert.rejects(fixture({ getRef: () => { throw error(status); } }).api.load());
  for (const data of [{ type: 'dir' }, { type: 'file', encoding: 'base64', content: Buffer.from('{}').toString('base64') },
    { type: 'file', encoding: 'base64', content: Buffer.from('{broken').toString('base64') }]) {
    await assert.rejects(fixture({ getContent: () => data }).api.load());
  }
});

test('enrollment comes from durable GitHub intent and unknown timestamp data fails explicitly', async () => {
  const value = fixture();
  assert.equal(await value.api.enrollment(1), ENROLLED);
  for (const request of [null, undefined, {}, { enabledAt: 'unknown' }, { enabledAt: 2026 }]) {
    value.github.graphql = async () => ({ repository: { pullRequest: { autoMergeRequest: request } } });
    if (request === null) assert.equal(await value.api.enrollment(1), null);
    else await assert.rejects(value.api.enrollment(1), /enrollment timestamp/);
  }
  value.github.graphql = async () => ({ repository: { pullRequest: null } });
  await assert.rejects(value.api.enrollment(1), /PR is unavailable/);
});

test('state updates are linear, compare the observed parent and never force-update refs', async () => {
  const value = fixture();
  const loaded = await value.api.load();
  loaded.blocked.push({ number: 2, head: HEAD, enrollment: ENROLLED, reason: 'Synthetic failure.' });
  await value.api.save(loaded);
  const update = value.calls.find(call => call[0] === 'updateRef')[1];
  assert.equal(update.force, false);
  assert.equal(update.ref, `heads/${STATE_BRANCH}`);
  assert.deepEqual(value.calls.find(call => call[0] === 'createCommit')[1].parents, [OTHER]);
  assert.deepEqual(value.calls.find(call => call[0] === 'createTree')[1].tree.map(item => item.path), ['queue.json']);
  assert.equal(value.api.commit, 'newcommit');
  const conflict = fixture();
  await conflict.api.load();
  conflict.responses.getRef = () => ({ object: { sha: HEAD } });
  await assert.rejects(conflict.api.save(loaded), /outside the controller/);
  assert.equal(conflict.calls.some(call => call[0] === 'updateRef'), false);
});

test('unchanged state does not create an idle checkpoint on every scheduled reconciliation', async () => {
  const value = fixture();
  const loaded = await value.api.load();
  await value.api.save(loaded);
  assert.equal(value.calls.some(call => call[0] === 'createCommit'), false);
});

test('cancellation races accept only an independently observed terminal run', async () => {
  const value = fixture({ cancelWorkflowRun: () => { throw error(409); } });
  await value.api.cancel(9);
  assert.equal(value.calls.at(-1)[0], 'warning');
  await assert.rejects(fixture({ cancelWorkflowRun: () => { throw error(409); },
    getWorkflowRun: () => ({ status: 'in_progress' }) }).api.cancel(9));
  await assert.rejects(fixture({ cancelWorkflowRun: () => { throw error(403); } }).api.cancel(9));
});

test('first state commit is isolated from main and does not create an executable workflow', async () => {
  const value = fixture();
  await value.api.save(state());
  assert.deepEqual(value.calls.find(call => call[0] === 'createCommit')[1].parents, []);
  assert.equal(value.calls.find(call => call[0] === 'createRef')[1].ref, `refs/heads/${STATE_BRANCH}`);
});

test('check issuance, dispatch and merge use exact repository and source identities', async () => {
  const value = fixture();
  assert.equal(await value.api.createCheck(candidate(), GATE), 77);
  await value.api.finishCheck(77, 'failure', 'Stale source.');
  await value.api.dispatch(candidate());
  await value.api.updateBranch(candidate());
  await value.api.merge(candidate());
  const check = value.calls.find(call => call[0] === 'create')[1];
  assert.equal(check.head_sha, HEAD);
  assert.equal(check.external_id, TICKET);
  assert.equal(check.status, 'in_progress');
  const dispatch = value.calls.find(call => call[0] === 'createWorkflowDispatch')[1];
  assert.equal(dispatch.ref, 'main');
  assert.equal(dispatch.workflow_id, 'ci.yml');
  assert.equal(dispatch.inputs.queue_base, BASE);
  assert.equal(dispatch.inputs.queue_head, HEAD);
  const merge = value.calls.find(call => call[0] === 'merge')[1];
  assert.equal(merge.sha, HEAD);
  assert.equal(merge.merge_method, 'squash');
  assert.equal(value.calls.find(call => call[0] === 'updateBranch')[1].expected_head_sha, HEAD);
  await assert.rejects(fixture({ create: () => ({ id: 77, app: { id: 99 } }) }).api.createCheck(candidate(), GATE));
});

test('expected protected merge refusals are explicit, infrastructure failures are not hidden', async () => {
  for (const status of [405, 409]) {
    const value = fixture({ merge: () => { throw error(status); } });
    assert.deepEqual(await value.api.merge(candidate()), { merged: false });
    assert.equal(value.calls.at(-1)[0], 'warning');
  }
  await assert.rejects(fixture({ merge: () => { throw error(500); } }).api.merge(candidate()));
});

test('continuation wakes only the trusted controller on main without candidate inputs', async () => {
  const value = fixture();
  await value.api.wake();
  assert.deepEqual(value.calls, [['createWorkflowDispatch',
    { owner: 'owner', repo: 'repo', workflow_id: 'merge-queue.yml', ref: 'main' }]]);
  for (const status of [403, 500]) {
    await assert.rejects(fixture({ createWorkflowDispatch: () => { throw error(status); } }).api.wake());
  }
});

test('candidate admission requires trusted main, the exact ticket and still-current head/base', async () => {
  assert.equal(await admit({ github: fixture().github, context, inputs: inputs() }), HEAD);
  for (const changed of [{ eventName: 'pull_request' }, { ref: 'refs/heads/topic' }, { sha: OTHER }]) {
    await assert.rejects(admit({ github: fixture().github, context: { ...context, ...changed }, inputs: inputs() }));
  }
  for (const changed of [{ queue_pr: '01' }, { queue_pr: 'x' }, { queue_head: OTHER },
    { queue_head: 'main' }, { queue_base: OTHER }, { queue_ticket: 'untrusted' }]) {
    await assert.rejects(admit({ github: fixture().github, context, inputs: { ...inputs(), ...changed } }));
  }
  for (const overrides of [
    { getRef: () => ({ object: { sha: OTHER } }) },
    { get: () => ({ ...pr(), head: { sha: OTHER, repo: { full_name: 'owner/repo' } } }) },
    { get: () => ({ ...pr(), auto_merge: null }) },
    { compareCommits: () => ({ status: 'diverged' }) },
    { getContent: () => ({ type: 'file', encoding: 'base64',
      content: Buffer.from(JSON.stringify({ ...state(), active: null })).toString('base64') }) },
  ]) {
    await assert.rejects(admit({ github: fixture(overrides).github, context, inputs: inputs() }));
  }
});

test('paginated run and job lookups cannot accidentally select an unrelated workflow', async () => {
  const value = fixture({
    list: () => [pr()], listWorkflowRuns: () => ({ workflow_runs: [{ id: 1 }] }),
    listJobsForWorkflowRunAttempt: () => ({ total_count: 1, jobs: [{ id: 2 }] }),
  });
  assert.equal((await value.api.pulls()).length, 1);
  assert.deepEqual(await value.api.runs(candidate()), [{ id: 1 }]);
  assert.deepEqual(await value.api.jobs(1), [{ id: 2 }]);
  assert.equal(value.calls.find(call => call[0] === 'listWorkflowRuns')[1].workflow_id, 'ci.yml');
  assert.deepEqual(value.calls.find(call => call[0] === 'listJobsForWorkflowRunAttempt')[1],
    { owner: 'owner', repo: 'repo', run_id: 1, attempt_number: 1, per_page: 100 });
  assert.equal(await value.api.reviewReady(1), true);
  for (const decision of ['APPROVED', 'REVIEW_REQUIRED', 'CHANGES_REQUESTED']) {
    value.github.graphql = async () => ({ repository: { pullRequest: { reviewDecision: decision } } });
    assert.equal(await value.api.reviewReady(1), decision === 'APPROVED');
  }
  value.github.graphql = async () => ({});
  await assert.rejects(value.api.reviewReady(1));
});

test('normalized pagination preserves empty and multiple workflow run and job records', async () => {
  for (const records of [[], [{ id: 1 }, { id: 2 }], [{ id: 1 }, { id: 1 }]]) {
    const value = fixture({
      listWorkflowRuns: () => ({ total_count: records.length, workflow_runs: records }),
      listJobsForWorkflowRunAttempt: () => ({ total_count: records.length, jobs: records }),
    });
    assert.deepEqual(await value.api.runs(candidate()), records);
    assert.deepEqual(await value.api.jobs(1), records);
  }
});

test('job observations pin the workflow attempt and reject unknown attempts or incomplete page counts', async () => {
  const value = fixture({ listJobsForWorkflowRunAttempt: () => ({ total_count: 1, jobs: [{ id: 2 }] }) });
  assert.deepEqual(await value.api.jobs(9, 2), [{ id: 2 }]);
  assert.equal(value.calls.find(call => call[0] === 'listJobsForWorkflowRunAttempt')[1].attempt_number, 2);
  for (const attempt of [0, -1, null, '1', 1.5]) {
    await assert.rejects(value.api.jobs(9, attempt), /Unknown candidate job attempt/);
  }
  for (const total_count of [undefined, null, -1, '1', 0, 2]) {
    const incomplete = fixture({ listJobsForWorkflowRunAttempt: () => ({ total_count, jobs: [{ id: 2 }] }) });
    await assert.rejects(incomplete.api.jobs(9), /candidate job pagination/);
  }
});

test('job pagination joins every normalized page and propagates later-page API or authentication errors', async () => {
  for (const scenario of ['complete', 'partial', 'changed-count', '401', '403', '404', '429', '500']) {
    const value = fixture();
    value.github.paginate = async (method, args, transform) => {
      assert.equal(method, value.github.rest.actions.listJobsForWorkflowRunAttempt);
      assert.equal(args.run_id, 9);
      assert.equal(args.attempt_number, 1);
      const first = [{ id: 1 }];
      first.total_count = 2;
      const records = [...transform({ data: first })];
      if (scenario === 'partial') return records;
      if (/^\d+$/.test(scenario)) throw error(Number(scenario));
      const second = [{ id: 2 }];
      second.total_count = scenario === 'changed-count' ? 3 : 2;
      return [...records, ...transform({ data: second })];
    };
    if (scenario === 'complete') assert.deepEqual(await value.api.jobs(9), [{ id: 1 }, { id: 2 }]);
    else await assert.rejects(value.api.jobs(9), /pagination|HTTP/);
    assert.equal(value.calls.some(call => ['update', 'merge', 'createWorkflowDispatch'].includes(call[0])), false);
  }
  for (const status of [401, 403, 404, 429, 500]) {
    const value = fixture({ listJobsForWorkflowRunAttempt: () => { throw error(status); } });
    await assert.rejects(value.api.jobs(9), new RegExp(`HTTP ${status}`));
  }
});

test('decision logs and warnings appear in both the console and awaited Actions summary', async () => {
  for (const result of ['idle', 'dispatched', 'failed']) {
    const value = fixture();
    const summaryCalls = [];
    let written = false;
    const summary = {};
    for (const name of ['addHeading', 'addTable', 'addCodeBlock', 'addRaw']) {
      summary[name] = (...args) => { summaryCalls.push([name, ...args]); return summary; };
    }
    summary.write = async () => {
      await Promise.resolve();
      written = true;
    };
    value.core.summary = summary;
    value.api.info('PR #1: skipped; required reviews are not satisfied.');
    value.api.notice('Candidate dispatch has no observable run; lane held.');
    await value.api.summarize(result);
    assert.equal(written, true);
    assert.deepEqual(value.calls, [
      ['info', 'PR #1: skipped; required reviews are not satisfied.'],
      ['warning', 'Candidate dispatch has no observable run; lane held.'],
    ]);
    assert.deepEqual(summaryCalls.find(call => call[0] === 'addTable')[1][1],
      ['owner/repo', 'workflow_dispatch', result]);
    assert.deepEqual(summaryCalls.find(call => call[0] === 'addCodeBlock').slice(1), [
      'PR #1: skipped; required reviews are not satisfied.\nWarning: Candidate dispatch has no observable run; lane held.',
      'text',
    ]);
    assert.equal(summaryCalls.find(call => call[0] === 'addRaw')[1].includes('no successful admission outcome'),
      result === 'failed');
  }
});

test('summary write failures propagate rather than claiming a persisted report', async () => {
  const value = fixture();
  const summary = {};
  for (const name of ['addHeading', 'addTable', 'addCodeBlock', 'addRaw']) summary[name] = () => summary;
  summary.write = async () => { throw new Error('Summary unavailable'); };
  value.core.summary = summary;
  await assert.rejects(value.api.summarize('idle'), /Summary unavailable/);
});
