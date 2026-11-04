const { test } = require('node:test');
const assert = require('node:assert/strict');
const { createHash } = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const { GitHubQueue, admit } = require('./github.cjs');
const { admitBatchTip } = require('./github.cjs');
const { manifestFor, digest } = require('./batch-policy.cjs');
const { CHECKS, GATE, STATE_BRANCH, WINDOWS_12_JOBS, DELIVERY_JOBS, REJECTED_ADMISSION_JOBS } = require('./policy.cjs');
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
function batchState() {
  const active = { kind: 'batch', ticket: TICKET, base: BASE, head: OTHER, created: new Date(ENROLLED).toISOString(),
    phase: 'dispatched', dispatchRecorded: true, members: [{ number: 1, head: HEAD, enrollment: ENROLLED, review: null }],
    prefixes: [{ sha: OTHER, tree: HEAD }],
    inventory: { schema: 'windows-12-v1', blob: workflowContent().sha, jobs: [...WINDOWS_12_JOBS] } };
  active.digest = digest(manifestFor(active));
  return { version: 2, profile: 'batch-v1', active, blocked: [], completedTip: null, singletonRetries: [] };
}
function workflowContent(source = '# merge-queue-job-schema: windows-12-v1\n') {
  const bytes = Buffer.from(source);
  return { type: 'file', encoding: 'base64', content: bytes.toString('base64'),
    sha: createHash('sha1').update(`blob ${bytes.length}\0`).update(bytes).digest('hex') };
}

function fixture(overrides = {}) {
  const calls = [];
  const responses = {
    getRef: ({ ref }) => ({ object: { sha: ref === 'heads/main' ? BASE : OTHER } }),
    getContent: () => ({ type: 'file', encoding: 'base64', content: Buffer.from(JSON.stringify(state())).toString('base64') }),
    getBranchProtection: () => ({ enforce_admins: { enabled: true },
      required_status_checks: { strict: true, checks: [...CHECKS, GATE].map(name => ({ context: name, app_id: 42 })) } }),
    compareCommits: () => ({ status: 'ahead' }),
    getCommit: ({ ref }) => ({ sha: ref, commit: { tree: { sha: OTHER } } }),
    get: () => pr(),
    create: () => ({ id: 77, app: { id: 42 } }),
    createBlob: () => ({ sha: 'blob' }), createTree: () => ({ sha: 'tree' }),
    createCommit: () => ({ sha: 'newcommit' }), merge: () => ({ merged: true }),
    getWorkflowRun: () => ({ status: 'completed' }),
    ...overrides,
  };
  const groups = {
    git: ['getRef', 'createBlob', 'createTree', 'createCommit', 'updateRef', 'createRef'],
    repos: ['getContent', 'getBranchProtection', 'compareCommits', 'getCollaboratorPermissionLevel', 'getCommit', 'merge'],
    pulls: ['list', 'get', 'updateBranch', 'merge', 'listFiles'],
    checks: ['create', 'update', 'listForRef'],
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

test('completed-source records are saved atomically with state and preserved by subsequent ordinary saves', async () => {
  const tip = { delivery: 'independent-v1', sha: BASE, tree: HEAD, base: OTHER, combined: HEAD,
    ticket: TICKET, validation: { run: 42, attempt: 1, source: HEAD, workflow: OTHER,
      inventory: BASE, schema: 'windows-12-delivery-v1' } };
  const queue = { version: 2, profile: 'batch-v1', active: null, blocked: [],
    completedTip: tip, deliveryProfile: 'independent-v1', singletonRetries: [] };
  const record = require('./delivery.cjs').releaseRecord(tip);
  let stored;
  const value = fixture({ createCommit: () => ({ sha: 'd'.repeat(40) }), getContent: ({ path }) => {
    if (path === 'queue.json') return workflowContent(JSON.stringify(queue));
    if (!stored) throw error(404);
    return workflowContent(JSON.stringify(stored));
  } });
  const loaded = await value.api.load();
  loaded.completedTip.qualificationRequested = true;
  await value.api.save(loaded);
  const first = value.calls.find(call => call[0] === 'createTree')[1];
  assert.equal(first.base_tree, OTHER);
  assert.deepEqual(first.tree.map(entry => entry.path), ['queue.json', `releases/${BASE}.json`]);
  assert.ok(value.calls.find(call => call[0] === 'createBlob' && call[1].content === `${JSON.stringify(record)}\n`));
  stored = record;
  value.responses.getRef = ({ ref }) => ({ object: { sha: ref === 'heads/main' ? BASE : 'd'.repeat(40) } });
  loaded.completedTip.qualificationOwner = { run: 99, source: BASE, attempt: 1 };
  await value.api.save(loaded);
  assert.deepEqual(value.calls.filter(call => call[0] === 'createTree').at(-1)[1].tree.map(entry => entry.path), ['queue.json']);
  stored = { ...record, ticket: '11111111-1111-1111-1111-111111111111' };
  loaded.completedTip.qualification = { run: 99, job: 100, source: BASE };
  const writes = value.calls.filter(call => call[0] === 'updateRef').length;
  await assert.rejects(value.api.save(loaded), /Immutable/);
  assert.equal(value.calls.filter(call => call[0] === 'updateRef').length, writes);
});

test('independent main admission accepts a recorded reachable final source despite a new active candidate, not an intermediate prefix', async () => {
  const record = { schema: 'completed-source-v1', source: BASE, tree: HEAD, base: OTHER, combined: HEAD,
    ticket: TICKET, validation: { run: 42, attempt: 1, source: HEAD, workflow: OTHER,
      inventory: BASE, schema: 'windows-12-delivery-v1' } };
  const queue = batchState();
  const value = fixture({ getContent: ({ path }) => path === 'queue.json'
    ? workflowContent(JSON.stringify(queue)) : workflowContent(JSON.stringify(record)) });
  assert.equal(await admitBatchTip({ github: value.github, context, appId: 42, deliveryProfile: 'independent-v1' }), true);
  value.responses.getContent = ({ path }) => {
    if (path !== 'queue.json') throw error(404);
    return workflowContent(JSON.stringify(queue));
  };
  assert.equal(await admitBatchTip({ github: value.github, context, appId: 42, deliveryProfile: 'independent-v1' }), false);
  assert.ok(!value.calls.some(call => ['updateRef', 'createWorkflowDispatch', 'merge'].includes(call[0])));
});

test('candidate packaging diff must prove exact admitted ancestry and never treats truncated impact as ordinary', async () => {
  const value = fixture();
  for (const data of [{}, { status: 'ahead', files: [{ filename: 'src/X.cs', status: 'modified' }] },
    { status: 'diverged', base_commit: { sha: BASE }, merge_base_commit: { sha: BASE }, files: [] }]) {
    value.responses.compareCommits = () => data;
    assert.equal((await value.api.candidatePackaging(BASE, HEAD)).build, true);
  }
  value.responses.compareCommits = () => ({ status: 'ahead', base_commit: { sha: BASE },
    merge_base_commit: { sha: BASE }, files: [{ filename: 'src/X.cs', status: 'modified' }] });
  assert.equal((await value.api.candidatePackaging(BASE, HEAD)).build, false);
});

test('independent main early failure is negative terminal evidence, not successful qualification or a permanent lane dependency', async () => {
  const { REJECTED_DELIVERY_JOBS } = require('./policy.cjs');
  const workflow = fs.readFileSync(path.join(__dirname, '..', '..', '.github', 'workflows', 'ci.yml'), 'utf8').replaceAll('\r\n', '\n');
  const run = { id: 42, run_attempt: 1, head_sha: BASE, head_branch: 'main', event: 'push',
    display_title: 'CI main', status: 'completed', conclusion: 'failure' };
  const jobs = REJECTED_DELIVERY_JOBS.map((name, index) => ({ id: index + 1, name,
    run_id: 42, run_attempt: 1, head_sha: BASE, status: 'completed',
    conclusion: name === 'Admit CI execution' ? 'failure' : 'skipped' }));
  const value = fixture({ getContent: () => workflowContent(workflow),
    listWorkflowRuns: () => ({ total_count: 1, workflow_runs: [run] }),
    listJobsForWorkflowRunAttempt: () => ({ total_count: jobs.length, jobs }) });
  value.api.independent = true;
  const observation = await value.api.tipQualification({ sha: BASE });
  assert.equal(observation.provisional, false);
  assert.equal(observation.terminal, true);
  assert.equal(observation.qualification, null);
  jobs.pop();
  assert.equal((await value.api.tipQualification({ sha: BASE })).provisional, true);
  assert.ok(!value.calls.some(call => ['updateRef', 'createWorkflowDispatch', 'merge'].includes(call[0])));
});

  test('batch protection is a separate explicit App-owned admission-only profile; current four-check default is unchanged', async () => {
    const value = fixture();
    await value.api.verifyProtection();
    await assert.rejects(value.api.verifyProtection('batch-v1'), /separately authorized/);
    const protection = value.responses.getBranchProtection();
    protection.required_status_checks.checks = [{ context: 'Merge admission', app_id: 42 }];
    value.responses.getBranchProtection = () => protection;
    await value.api.verifyProtection('batch-v1');
    await assert.rejects(value.api.verifyProtection(), /missing/);
    protection.required_status_checks.checks[0].app_id = 99;
    await assert.rejects(value.api.verifyProtection('batch-v1'), /missing/);
  });

  test('synthetic adapter uses only trusted GitHub merge/ref operations and reconciles ambiguous responses by exact parents', async () => {
    for (const ambiguous of [false, true]) {
      let ref = null;
      let requests = 0;
      const value = fixture({
        getRef: () => { if (!ref) throw error(404); return { object: { sha: ref } }; },
        createRef: args => { ref = args.sha; return {}; },
        merge: () => { requests++; ref = OTHER; if (ambiguous) throw error(502); return {}; },
        getCommit: ({ ref: source }) => ({ sha: source, commit: { tree: { sha: HEAD } },
          parents: [{ sha: BASE }, { sha: HEAD }] }),
      });
      const active = batchState().active;
      if (ambiguous) await assert.rejects(value.api.syntheticPrefix(active, 0), /502/);
      assert.deepEqual(await value.api.syntheticPrefix(active, 0), { sha: OTHER, tree: HEAD });
      assert.equal(requests, 1);
      assert.deepEqual(value.calls.filter(call => call[0] === 'createRef').map(call => call[1]), [{
        owner: 'owner', repo: 'repo', ref: `refs/heads/automation/merge-queue-batch/${TICKET}/1`, sha: BASE }]);
      assert.ok(!value.calls.some(call => ['updateRef', 'createBlob', 'createTree', 'createCommit', 'dispatch'].includes(call[0])));
      value.responses.getCommit = ({ ref: source }) => ({ sha: source, commit: { tree: { sha: HEAD } },
        parents: [{ sha: HEAD }, { sha: BASE }] });
      await assert.rejects(value.api.syntheticPrefix(active, 0), /parent identities/);
    }
  });

  test('synthetic conflicts and unknown commit metadata cannot manufacture candidate trees', async () => {
    const value = fixture({ getRef: () => ({ object: { sha: BASE } }), merge: () => { throw error(409); } });
    assert.equal(await value.api.syntheticPrefix(batchState().active, 0), null);
    for (const commit of [{}, { sha: OTHER }, { sha: OTHER, commit: { tree: { sha: HEAD } }, parents: [{}] }]) {
      value.responses.getCommit = () => commit;
      await assert.rejects(value.api.commitInfo(OTHER), /Incomplete/);
    }
  });

  test('expected base updates require exact parents/tree, verified commit and the configured App bot, not an author change', async () => {
    const member = batchState().active.members[0];
    const value = fixture({
      get: () => ({ ...pr(), head: { ...pr().head, sha: OTHER } }),
      getCommit: () => ({ sha: OTHER, commit: { tree: { sha: HEAD }, verification: { verified: true } },
        parents: [{ sha: HEAD }, { sha: BASE }], author: { login: 'queue[bot]' } }),
      listForRef: () => [{ app: { id: 42, slug: 'queue' } }],
    });
    assert.equal(await value.api.expectedUpdate(member, BASE, HEAD), OTHER);
    for (const change of ['parents', 'tree', 'verified', 'author', 'app']) {
      const data = { sha: OTHER, commit: { tree: { sha: HEAD }, verification: { verified: true } },
        parents: [{ sha: HEAD }, { sha: BASE }], author: { login: 'queue[bot]' } };
      if (change === 'parents') data.parents.reverse();
      if (change === 'tree') data.commit.tree.sha = BASE;
      if (change === 'verified') data.commit.verification.verified = false;
      if (change === 'author') data.author.login = 'author';
      value.responses.getCommit = () => data;
      value.responses.listForRef = () => [{ app: { id: change === 'app' ? 99 : 42, slug: 'queue' } }];
      await assert.rejects(value.api.expectedUpdate(member, BASE, HEAD), /expected queue-App/);
    }
  });

  test('batch changed-file inventory is complete and bounded; truncation and duplicates cannot hide control changes', async () => {
    const value = fixture({ get: () => ({ ...pr(), changed_files: 2 }),
      listFiles: () => [{ filename: 'src/feature.cs' }, { filename: '.github/workflows/ci.yml' }] });
    assert.deepEqual(await value.api.files(1), ['src/feature.cs', '.github/workflows/ci.yml']);
    value.responses.listFiles = () => [{ filename: 'src/feature.cs' },
      { filename: 'docs/old-policy.txt', previous_filename: 'eng/merge-queue/policy.cjs' }];
    assert.deepEqual(await value.api.files(1), ['src/feature.cs', 'docs/old-policy.txt', 'eng/merge-queue/policy.cjs']);
    for (const count of [undefined, 0, 3000, 3001]) {
      value.responses.get = () => ({ ...pr(), changed_files: count });
      await assert.rejects(value.api.files(1), /inventory/);
    }
    value.responses.get = () => ({ ...pr(), changed_files: 2 });
    for (const files of [[{ filename: 'one' }], [{ filename: 'one' }, { filename: 'one' }]]) {
      value.responses.listFiles = () => files;
      await assert.rejects(value.api.files(1), /Incomplete or duplicate/);
    }
  });

  test('batch dispatch admits only the frozen live manifest and returns combined source, never an individual source', async () => {
    const state = batchState();
    const value = fixture({
      getContent: ({ path: file }) => file === '.github/workflows/ci.yml' ? workflowContent()
        : { type: 'file', encoding: 'base64', content: Buffer.from(JSON.stringify(state)).toString('base64') },
      get: () => ({ ...pr(), mergeable: true }),
      getCommit: () => ({ sha: OTHER, commit: { tree: { sha: HEAD } }, parents: [] }),
    });
    const dispatch = { ...inputs(), queue_head: OTHER, queue_batch: state.active.digest, profile: 'batch-v1' };
    assert.equal(await admit({ github: value.github, context, inputs: dispatch }), OTHER);
    assert.equal(value.calls.some(call => ['merge', 'createRef', 'updateBranch'].includes(call[0])), false);
    for (const change of ['head', 'base', 'ticket', 'manifest', 'pr', 'profile']) {
      const changed = { ...dispatch };
      changed[{ head: 'queue_head', base: 'queue_base', ticket: 'queue_ticket',
        manifest: 'queue_batch', pr: 'queue_pr', profile: 'profile' }[change]] = change === 'pr' ? '2' : 'unknown';
      await assert.rejects(admit({ github: value.github, context, inputs: changed }));
    }
    for (const change of ['enrollment', 'review', 'head', 'mergeability', 'workflow']) {
      value.github.graphql = async () => ({ repository: { pullRequest: { reviewDecision: change === 'review'
        ? 'CHANGES_REQUESTED' : null, autoMergeRequest: change === 'enrollment' ? null : { enabledAt: ENROLLED } } } });
      value.responses.get = () => ({ ...pr(), mergeable: change === 'mergeability' ? null : true,
        head: { ...pr().head, sha: change === 'head' ? OTHER : HEAD } });
      value.responses.getContent = ({ path: file }) => file === '.github/workflows/ci.yml'
        ? workflowContent(change === 'workflow' ? '# merge-queue-job-schema: unknown\n' : undefined)
        : { type: 'file', encoding: 'base64', content: Buffer.from(JSON.stringify(state)).toString('base64') };
      await assert.rejects(admit({ github: value.github, context, inputs: dispatch }));
    }
  });

  test('main/tag/manual admission suppresses every active prefix and unknown main while accepting only the completed actual tip', async () => {
    const state = batchState();
    const value = fixture({ getContent: () => ({ type: 'file', encoding: 'base64',
      content: Buffer.from(JSON.stringify(state)).toString('base64') }) });
    const options = { github: value.github, context, appId: 42 };
    assert.equal(await admitBatchTip(options), false);
    state.active = null;
    assert.equal(await admitBatchTip(options), false);
    state.completedTip = { sha: BASE, tree: HEAD, ticket: TICKET };
    assert.equal(await admitBatchTip(options), true);
    assert.equal(await admitBatchTip({ ...options, context: { ...context, sha: HEAD } }), false);
    value.responses.getRef = ({ ref }) => ({ object: { sha: ref === 'heads/main' ? OTHER : HEAD } });
    assert.equal(await admitBatchTip(options), false);
  });

  test('tip qualification requires complete actual-source jobs and the unique successful publication join', async () => {
    const run = { id: 9, run_attempt: 1, head_sha: BASE, status: 'completed', conclusion: 'success',
      event: 'push', head_branch: 'main', display_title: 'CI main' };
    const jobs = [...WINDOWS_12_JOBS, 'Qualify completed batch tip'].map((name, index) => ({
      id: index + 1, name, status: 'completed', conclusion: 'success', run_id: 9, run_attempt: 1, head_sha: BASE }));
    const value = fixture({ getContent: () => workflowContent(),
      listWorkflowRuns: () => ({ total_count: 1, workflow_runs: [run] }),
      listJobsForWorkflowRunAttempt: () => ({ total_count: jobs.length, jobs }) });
    assert.deepEqual((await value.api.tipQualification({ sha: BASE }))?.qualification,
      { run: 9, source: BASE, job: jobs.length });
    for (const name of [...WINDOWS_12_JOBS, 'Qualify completed batch tip']) {
      const target = jobs.find(job => job.name === name);
      target.conclusion = 'skipped';
      assert.equal((await value.api.tipQualification({ sha: BASE }))?.qualification ?? null, null, name);
      target.conclusion = 'success';
    }
    jobs.push({ ...jobs.at(-1), id: 999 });
    assert.equal((await value.api.tipQualification({ sha: BASE }))?.qualification ?? null, null);
  });

  test('qualification observes queued first attempts and reconciles missing owner listings without dispatch', async () => {
    const run = { id: 9, run_attempt: 1, head_sha: BASE, status: 'queued', conclusion: null,
      event: 'workflow_dispatch', head_branch: 'main', display_title: 'CI main' };
    const value = fixture({ getContent: () => workflowContent(),
      listWorkflowRuns: () => ({ total_count: 1, workflow_runs: [run] }),
      listJobsForWorkflowRunAttempt: () => ({ total_count: 0, jobs: [] }),
      getWorkflowRun: () => run });
    const observation = await value.api.tipQualification({ sha: BASE });
    assert.deepEqual(observation.owner, { run: 9, source: BASE, attempt: 1 });
    assert.equal(observation.qualification, null);
    assert.equal(observation.terminal, false);
    assert.equal(value.calls.find(call => call[0] === 'listWorkflowRuns')[1].status, undefined);
    value.responses.listWorkflowRuns = () => ({ total_count: 0, workflow_runs: [] });
    const tip = { sha: BASE, qualificationOwner: observation.owner };
    assert.deepEqual(await value.api.tipQualification(tip), observation);
    for (const change of ['source', 'attempt', 'branch', 'event', 'title', 'id']) {
      value.responses.getWorkflowRun = () => ({ ...run, ...{
        source: { head_sha: OTHER }, attempt: { run_attempt: 2 }, branch: { head_branch: 'tag' },
        event: { event: 'pull_request' }, title: { display_title: 'Merge queue batch [ticket]' }, id: { id: 10 },
      }[change] });
      await assert.rejects(value.api.tipQualification(tip), /owner/);
    }
    assert.equal(value.calls.some(call => ['createWorkflowDispatch', 'cancelWorkflowRun'].includes(call[0])), false);
  });

  test('qualification holds a failed owner until exact terminal jobs and adopts only observed fresh first-attempt work', async () => {
    const run = { id: 9, run_attempt: 1, head_sha: BASE, status: 'completed', conclusion: 'failure',
      event: 'push', head_branch: 'main', display_title: 'CI main' };
    const jobs = WINDOWS_12_JOBS.map((name, index) => ({ id: index + 1, name,
      status: 'completed', conclusion: 'failure', run_id: 9, run_attempt: 1, head_sha: BASE }));
    jobs.at(-1).status = 'in_progress';
    const replacement = { ...run, id: 10, event: 'workflow_dispatch', status: 'queued', conclusion: null };
    const value = fixture({ getContent: () => workflowContent(),
      listWorkflowRuns: () => ({ total_count: 2, workflow_runs: [replacement, run] }),
      listJobsForWorkflowRunAttempt: ({ run_id }) => {
        const observed = run_id === 9 ? jobs : [{ id: 100, name: 'Resolve build version',
          status: 'in_progress', conclusion: null, run_id: 10, run_attempt: 1, head_sha: BASE }];
        return { total_count: observed.length, jobs: observed };
      } });
    const tip = { sha: BASE, qualificationOwner: { run: 9, source: BASE, attempt: 1 } };
    assert.equal((await value.api.tipQualification(tip)).owner.run, 9);
    jobs.at(-1).status = 'completed';
    assert.equal((await value.api.tipQualification(tip)).owner.run, 10);
    replacement.run_attempt = 2;
    assert.equal((await value.api.tipQualification(tip)).owner.run, 9);
    replacement.run_attempt = 1;
    value.responses.listWorkflowRuns = () => { throw error(503); };
    await assert.rejects(value.api.tipQualification(tip), /503/);
    assert.equal(value.calls.some(call => ['createWorkflowDispatch', 'cancelWorkflowRun'].includes(call[0])), false);
  });

  test('ordinary push suppression never owns qualification; missing source jobs wait without pretending to be suppression', async () => {
    const run = { id: 9, run_attempt: 1, head_sha: BASE, status: 'in_progress', conclusion: null,
      event: 'push', head_branch: 'main', display_title: 'CI main' };
    const jobs = ['Admit CI execution', 'Resolve build version', 'Qualify completed batch tip']
      .map((name, index) => ({ id: index + 1, name, status: 'completed',
        conclusion: index ? 'skipped' : 'success', run_id: 9, run_attempt: 1, head_sha: BASE }));
    const value = fixture({ getContent: () => workflowContent(),
      listWorkflowRuns: () => ({ total_count: 1, workflow_runs: [run] }),
      listJobsForWorkflowRunAttempt: () => ({ total_count: jobs.length, jobs }) });
    assert.equal((await value.api.tipQualification({ sha: BASE })).provisional, true);
    Object.assign(run, { status: 'completed', conclusion: 'success' });
    assert.equal(await value.api.tipQualification({ sha: BASE }), null);
    jobs.pop();
    assert.equal((await value.api.tipQualification({ sha: BASE })).provisional, true);
    jobs[1].conclusion = 'success';
    const full = await value.api.tipQualification({ sha: BASE });
    assert.equal(full.provisional, false);
    assert.equal(full.qualification, null);
    assert.equal(full.terminal, false);
    assert.equal(value.calls.some(call => ['createWorkflowDispatch', 'cancelWorkflowRun'].includes(call[0])), false);
  });
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

test('reviewed recovery commit metadata preserves the existing linear state update mechanism', async () => {
  const value = fixture();
  const loaded = await value.api.load();
  loaded.active = null;
  await value.api.save(loaded, 'ci: reviewed recovery\n\nAdministrator: operator\nTicket: synthetic');
  assert.equal(value.calls.find(call => call[0] === 'createCommit')[1].message,
    'ci: reviewed recovery\n\nAdministrator: operator\nTicket: synthetic');
  assert.equal(value.calls.find(call => call[0] === 'updateRef')[1].force, false);
});

test('recovery actor authority is read live and fails closed on missing, mismatched or unavailable metadata', async () => {
  for (const permission of ['admin', 'maintain', 'write', 'read', 'none']) {
    const value = fixture({ getCollaboratorPermissionLevel: () => ({ permission, user: { login: 'Operator' } }) });
    assert.equal(await value.api.administrator('operator'), permission === 'admin');
    assert.deepEqual(value.calls.at(-1)[1], { owner: 'owner', repo: 'repo', username: 'operator' });
  }
  for (const data of [{}, { permission: 'admin' }, { permission: 'admin', user: { login: 'other' } },
    { permission: 'unknown', user: { login: 'operator' } }]) {
    await assert.rejects(fixture({ getCollaboratorPermissionLevel: () => data }).api.administrator('operator'),
      /authority/);
  }
  for (const actor of ['', undefined, 'queue[bot]', '../operator']) {
    await assert.rejects(fixture().api.administrator(actor), /actor/);
  }
  for (const status of [401, 403, 404, 500]) {
    await assert.rejects(fixture({ getCollaboratorPermissionLevel: () => { throw error(status); } })
      .api.administrator('operator'), /HTTP/);
  }
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
    list: () => [pr()], listWorkflowRuns: () => ({ total_count: 1, workflow_runs: [{ id: 1 }] }),
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

test('workflow inventory reads and validates exact source bytes, never main, candidate code or event fields', async () => {
  const value = fixture({ getContent: () => workflowContent() });
  assert.deepEqual(await value.api.expectedJobs(BASE), WINDOWS_12_JOBS);
  assert.deepEqual(value.calls, [['getContent', { owner: 'owner', repo: 'repo',
    ref: BASE, path: '.github/workflows/ci.yml' }]]);
  for (const source of [null, undefined, 'main', HEAD.toUpperCase(), 'topic']) {
    await assert.rejects(value.api.expectedJobs(source), /workflow source/);
  }
  assert.equal(value.calls.length, 1);
});

test('admission rejection retirement authenticates an exact historical workflow blob, not its schema marker alone', async () => {
  const source = fs.readFileSync(path.join(__dirname, 'fixtures', 'ci-97e22a8f.yml'), 'utf8')
    .replaceAll('\r\n', '\n');
  assert.equal(workflowContent(source).sha, '97e22a8f88bcdc9bbaef32cf9e7200dd6e5c43cd');
  const run = { id: 9, run_attempt: 1, head_sha: BASE, head_branch: 'main', event: 'workflow_dispatch',
    status: 'completed', conclusion: 'failure' };
  const jobs = REJECTED_ADMISSION_JOBS.map((name, index) => ({ id: index + 1, name, run_id: 9,
    run_attempt: 1, head_sha: BASE, status: 'completed',
    conclusion: name === 'Admit CI execution' ? 'failure' : 'skipped' }));
  const value = fixture({ getContent: () => workflowContent(source) });
  assert.equal(await value.api.rejectedAdmission(BASE, run, jobs), true);
  assert.equal(await value.api.rejectedAdmission(OTHER, run, jobs), false);
  assert.equal(value.calls.length, 1);
  assert.equal(value.calls[0][1].ref, BASE);
  for (const text of [source + '\n# changed execution contract\n', '# merge-queue-job-schema: windows-12-v1\n',
    'name: CI\n', '# merge-queue-job-schema: future-v2\n']) {
    assert.equal(await fixture({ getContent: () => workflowContent(text) }).api.rejectedAdmission(BASE, run, jobs), false);
  }
  for (const response of [{ ...workflowContent(source), sha: OTHER }, { ...workflowContent(source), encoding: 'utf-8' }]) {
    assert.equal(await fixture({ getContent: () => response }).api.rejectedAdmission(BASE, run, jobs), false);
  }
  assert.equal(await fixture({ getContent: () => { throw error(404); } }).api.rejectedAdmission(BASE, run, jobs), false);
  for (const status of [401, 403, 429, 500]) {
    await assert.rejects(fixture({ getContent: () => { throw error(status); } }).api.rejectedAdmission(BASE, run, jobs), /HTTP/);
  }
  assert.equal(await value.api.rejectedAdmission(BASE, { ...run, run_attempt: 2 }, jobs), false);
  assert.equal(value.calls.some(call => ['update', 'merge', 'createWorkflowDispatch'].includes(call[0])), false);
});

test('preflight rejection retirement authenticates the exact reviewed workflow bytes and failed-version job plan', async () => {
  const source = fs.readFileSync(path.join(__dirname, 'fixtures', 'ci-c7517372.yml'), 'utf8')
    .replaceAll('\r\n', '\n');
  assert.equal(workflowContent(source).sha, 'c75173721c4131cf29d45482ea2edb92934070f1');
  const run = { id: 9, run_attempt: 1, head_sha: BASE, head_branch: 'main', event: 'workflow_dispatch',
    status: 'completed', conclusion: 'failure' };
  const jobs = REJECTED_ADMISSION_JOBS.map((name, index) => ({ id: index + 1, name, run_id: 9,
    run_attempt: 1, head_sha: BASE, status: 'completed',
    conclusion: name === 'Admit CI execution' ? 'success' : name === 'Resolve build version' ? 'failure' : 'skipped' }));
  const value = fixture({ getContent: () => workflowContent(source) });
  assert.equal(await value.api.rejectedAdmission(BASE, run, jobs), true);
  assert.equal(await value.api.rejectedAdmission(OTHER, run, jobs), false);
  for (const text of [source + '\n# changed fanout authority\n', '# merge-queue-job-schema: windows-12-v1\n',
    fs.readFileSync(path.join(__dirname, 'fixtures', 'ci-97e22a8f.yml'), 'utf8').replaceAll('\r\n', '\n')]) {
    assert.equal(await fixture({ getContent: () => workflowContent(text) }).api.rejectedAdmission(BASE, run, jobs), false);
  }
  for (const response of [{ ...workflowContent(source), sha: OTHER },
    { ...workflowContent(source), encoding: 'utf-8' }]) {
    assert.equal(await fixture({ getContent: () => response }).api.rejectedAdmission(BASE, run, jobs), false);
  }
  for (const status of [401, 403, 429, 500]) {
    await assert.rejects(fixture({ getContent: () => { throw error(status); } }).api.rejectedAdmission(BASE, run, jobs), /HTTP/);
  }
  assert.equal(value.calls.some(call => ['merge', 'createWorkflowDispatch', 'updateRef'].includes(call[0])), false);
});

test('real schema-declared workflow resolves complete inventory; new unversioned source fails closed', async () => {
  const source = fs.readFileSync(path.join(__dirname, '..', '..', '.github', 'workflows', 'ci.yml'), 'utf8')
    .replaceAll('\r\n', '\n');
  const unversioned = source.replace('# merge-queue-job-schema: windows-12-v1\n', '');
  assert.equal(await fixture({ getContent: () => workflowContent(unversioned) }).api.expectedJobs(BASE), null);
  for (const workflow of [source]) {
    const value = fixture({ getContent: () => workflowContent(workflow) });
    assert.deepEqual(await value.api.expectedJobs(BASE), DELIVERY_JOBS);
    assert.deepEqual(value.calls, [['getContent', { owner: 'owner', repo: 'repo',
      ref: BASE, path: '.github/workflows/ci.yml' }]]);
  }
});

test('missing, unknown or malformed workflow source explicitly denies a quiescence inventory', async () => {
  for (const response of [null, {}, { type: 'dir' }, { ...workflowContent(), encoding: 'utf-8' },
    { ...workflowContent(), sha: undefined }, { ...workflowContent(), content: undefined },
    { ...workflowContent(), content: '' }, { ...workflowContent(), content: 'not base64' },
    { ...workflowContent(), sha: OTHER }, workflowContent('name: CI\n'),
    workflowContent('# merge-queue-job-schema: future-v2\n'),
    workflowContent('# merge-queue-job-schema: windows-12-v1\n# merge-queue-job-schema: windows-12-v1\n')]) {
    const value = fixture({ getContent: () => response });
    // A null API response is normalized to an empty object by this existing fake.
    assert.equal(await value.api.expectedJobs(BASE), null);
    assert.equal(value.calls.filter(call => call[0] === 'warning').length, 1);
    assert.match(value.calls.at(-1)[1], /physical quiescence is unknown/);
    assert.equal(value.calls.some(call => ['update', 'merge', 'createWorkflowDispatch'].includes(call[0])), false);
  }
  const missing = fixture({ getContent: () => { throw error(404); } });
  assert.equal(await missing.api.expectedJobs(BASE), null);
  assert.match(missing.calls.at(-1)[1], /source is unavailable/);
  for (const status of [401, 403, 429, 500]) {
    const value = fixture({ getContent: () => { throw error(status); } });
    await assert.rejects(value.api.expectedJobs(BASE), new RegExp(`HTTP ${status}`));
    assert.equal(value.calls.length, 1);
  }
});

test('run observations require complete consistent pages and never accept API errors as an empty listing', async () => {
  for (const scenario of ['complete', 'partial', 'changed-count', '401', '403', '404', '429', '500']) {
    const value = fixture();
    value.github.paginate = async (method, args, transform) => {
      assert.equal(method, value.github.rest.actions.listWorkflowRuns);
      assert.equal(args.workflow_id, 'ci.yml');
      assert.equal(args.event, 'workflow_dispatch');
      assert.equal(args.branch, 'main');
      const first = Object.assign([{ id: 1 }], { total_count: 2 });
      const records = [...transform({ data: first })];
      if (scenario === 'partial') return records;
      if (/^\d+$/.test(scenario)) throw error(Number(scenario));
      const second = Object.assign([{ id: 2 }], { total_count: scenario === 'changed-count' ? 3 : 2 });
      return [...records, ...transform({ data: second })];
    };
    if (scenario === 'complete') assert.deepEqual(await value.api.runs(candidate()), [{ id: 1 }, { id: 2 }]);
    else await assert.rejects(value.api.runs(candidate()), /pagination|HTTP/);
  }
  for (const total_count of [undefined, null, -1, '0', 1]) {
    await assert.rejects(fixture({ listWorkflowRuns: () => ({ total_count, workflow_runs: [] }) })
      .api.runs(candidate()), /candidate run pagination/);
  }
});

test('a late recovered ticket cannot admit expensive candidate work after its reservation is removed or replaced', async () => {
  for (const active of [null, { ...candidate(), ticket: 'ffffffff-ffff-ffff-ffff-ffffffffffff' }]) {
    const value = fixture({ getContent: () => ({ type: 'file', encoding: 'base64',
      content: Buffer.from(JSON.stringify({ ...state(), active })).toString('base64') }) });
    await assert.rejects(admit({ github: value.github, context, inputs: inputs() }), /stale or was not admitted/);
    assert.equal(value.calls.some(call => ['get', 'compareCommits', 'createWorkflowDispatch'].includes(call[0])), false);
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
