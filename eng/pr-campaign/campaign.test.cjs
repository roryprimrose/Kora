const { test } = require('node:test');
const assert = require('node:assert/strict');
const { createHash } = require('node:crypto');
const { Reader, execute } = require('./github.cjs');
const { SCHEMA, QUALIFY, ObservationError, digest, newReceipt, evaluate, validateReceipt, supersede,
  summarizeRun, validatePull, ACTIONS } = require('./campaign.cjs');
const { parseArgs, createReader, scratchPath, observe } = require('./cli.cjs');
const { WINDOWS_12_JOBS, GATE, title } = require('../merge-queue/policy.cjs');
const { manifestFor, digest: manifestDigest } = require('../merge-queue/batch-policy.cjs');
const schema = require('./receipt.schema.json');

const BASE = 'a'.repeat(40);
const HEAD = 'b'.repeat(40);
const COMBINED = 'c'.repeat(40);
const MERGE = 'd'.repeat(40);
const TICKET = '01234567-89ab-cdef-0123-456789abcdef';
const OTHER_TICKET = '11234567-89ab-cdef-0123-456789abcdef';
const NOW = '2026-11-04T00:00:00.000Z';
const REPO = 'owner/repo';
const URL = `https://github.com/${REPO}`;
const campaign = () => ({ repository: REPO, owner: 'delivery-1', targets: [1, 2], expectedPrincipal: 'publisher' });
const observer = () => ({ source: HEAD, digest: 'e'.repeat(64) });
const pull = number => ({ number, state: 'OPEN', head: HEAD, base: 'main', draft: false,
  mergeable: 'MERGEABLE', review: null, url: `${URL}/pull/${number}`, mergedAt: null, merge: null,
  author: 'publisher', repository: REPO, enrollment: NOW, method: 'SQUASH', labels: [] });
const references = () => ({ queue: { branch: 'automation/merge-queue-state', commit: BASE, blob: HEAD },
  main: { sha: MERGE, fetchedAt: NOW } });
const state = () => ({ version: 2, profile: 'batch-v1', active: null, blocked: [],
  completedTip: null, singletonRetries: [] });
const snapshot = () => ({ principal: 'publisher', profile: 'batch-v1', state: state(),
  pulls: [pull(1), pull(2)], checks: {}, reachable: {}, references: references(),
  candidate: null, qualification: null, duplicates: [], controllers: [] });
const receipt = () => newReceipt(campaign(), new Date(NOW), observer());
test('machine schema version, required envelope and problem codes match maintained receipt construction', () => {
  assert.equal(schema.properties.schema.const, SCHEMA);
  assert.deepEqual([...schema.required].sort(), Object.keys(receipt()).sort());
  assert.deepEqual(schema.properties.blockers.items.properties.code.enum.sort(), Object.keys(ACTIONS).sort());
  const example = require('./receipt.example.json');
  assert.equal(validateReceipt(example, example.campaign), example);
});
const run = (overrides = {}) => ({ id: 10, run_attempt: 1, head_sha: BASE, head_branch: 'main',
  event: 'workflow_dispatch', status: 'completed', conclusion: 'success', display_title: 'CI main',
  html_url: `${URL}/actions/runs/10`, ...overrides });
const jobs = (item, overrides = {}) => [...WINDOWS_12_JOBS, QUALIFY].map((name, index) => ({
  id: 100 + index, name, run_id: item.id, run_attempt: item.run_attempt, head_sha: item.head_sha,
  status: 'completed', conclusion: 'success', html_url: `${URL}/actions/runs/${item.id}/job/${100 + index}`,
  ...overrides,
}));
function evidence(item = run(), kind = 'candidate', source = HEAD, ticket = TICKET, jobList = jobs(item)) {
  return summarizeRun(item, jobList, { blob: HEAD, schema: 'windows-12-v1', jobs: [...WINDOWS_12_JOBS] },
    REPO, kind, source, ticket);
}
function activeBatch() {
  const active = { kind: 'batch', ticket: TICKET, base: BASE, head: COMBINED, created: NOW,
    phase: 'dispatched', dispatchRecorded: true,
    members: [1, 2].map(number => ({ number, head: HEAD, enrollment: NOW, review: null })),
    prefixes: [{ sha: HEAD, tree: BASE }, { sha: COMBINED, tree: COMBINED }],
    inventory: { schema: 'windows-12-v1', blob: HEAD, jobs: [...WINDOWS_12_JOBS] } };
  active.digest = manifestDigest(manifestFor(active));
  return active;
}
function merged(item) { return { ...item, state: 'MERGED', merge: MERGE, mergedAt: NOW, enrollment: null, method: null }; }

test('all fixed targets need actual merged state and fresh main ancestry', () => {
  const data = snapshot();
  data.pulls = data.pulls.map(merged);
  data.reachable = { 1: true, 2: true };
  const result = evaluate(receipt(), data);
  assert.equal(result.outcome, 'MERGED_VERIFIED');
  assert.ok(result.pulls.every(item => item.reachableFrom === MERGE && item.merge === MERGE));
  assert.equal(validateReceipt(result, campaign()), result);
  data.pulls[1] = { ...data.pulls[1], state: 'CLOSED' };
  assert.equal(evaluate(receipt(), data).blockers[0].code, 'closed-unmerged');
  data.pulls[1] = merged(pull(2));
  data.reachable[2] = false;
  assert.equal(evaluate(receipt(), data).blockers[0].code, 'merge-unreachable');
});
test('completed batch member source/admitted/merge identities survive retirement without inventing solo-head proof', () => {
  const data = snapshot();
  data.state.completedTip = { sha: MERGE, tree: HEAD, ticket: TICKET, combined: COMBINED,
    manifest: 'e'.repeat(64), members: [{ number: 1, source: HEAD, admittedHead: BASE, merged: MERGE }] };
  const result = evaluate(receipt(), data);
  assert.deepEqual(result.queue.completedTip.members,
    [{ number: 1, source: HEAD, admittedHead: BASE, merged: MERGE }]);
  assert.equal(result.pulls[0].proofKind, 'none');
  assert.equal(validateReceipt(result, campaign()), result);
  delete data.state.completedTip.members;
  assert.equal(evaluate(receipt(), data).queue.completedTip.members, null, 'historical inventory is unavailable, not empty proof');
});
test('malformed optional completed-tip source or member identities fail before receipt projection', () => {
  for (const change of [
    tip => { tip.combined = 'unknown'; }, tip => { tip.manifest = 'unknown'; },
    tip => { tip.members = null; }, tip => { tip.members = []; },
    tip => { tip.members[0].source = 'unknown'; }, tip => { tip.members[0].admittedHead = null; },
    tip => { tip.members[0].merged = BASE; }, tip => { tip.members.push(tip.members[0]); },
  ]) {
    const data = snapshot();
    data.state.completedTip = { sha: MERGE, tree: HEAD, ticket: TICKET, combined: COMBINED,
      manifest: 'e'.repeat(64), members: [{ number: 1, source: HEAD, admittedHead: BASE, merged: MERGE }] };
    change(data.state.completedTip);
    const output = receipt();
    assert.throws(() => evaluate(output, data), /completed-tip/);
    assert.equal(output.queue, null);
  }
});
test('missing targets and missing merged identities fail closed', () => {
  const data = snapshot();
  data.pulls.pop();
  assert.throws(() => evaluate(receipt(), data), /PR metadata/);
  assert.throws(() => validatePull({ ...merged(pull(1)), merge: null }, 1, REPO), /actual merged identity/);
});
for (const [field, value, code] of [
  ['mergeable', 'CONFLICTING', 'conflict'], ['mergeable', 'UNKNOWN', 'unknown-metadata'],
  ['review', 'CHANGES_REQUESTED', 'review-blocked'], ['review', 'REVIEW_REQUIRED', 'review-blocked'],
  ['draft', true, 'not-enrolled'], ['method', null, 'not-enrolled'], ['base', 'other', 'not-enrolled'],
  ['repository', 'other/repo', 'not-enrolled'], ['labels', ['queue:priority-mystery'], 'priority-invalid'],
  ['labels', ['queue:priority-high', 'queue:priority-low'], 'priority-invalid'],
]) test(`current ${field}=${JSON.stringify(value)} has actionable ${code}`, () => {
  const data = snapshot();
  data.pulls[0][field] = value;
  const result = evaluate(receipt(), data);
  assert.equal(result.outcome, 'blocked');
  assert.ok(result.blockers.some(item => item.target === 1 && item.code === code && item.action.length));
});
test('batch proof is combined-source evidence, never individual-head testing', () => {
  const data = snapshot();
  data.state.active = activeBatch();
  data.candidate = evidence(run(), 'candidate', COMBINED);
  const result = evaluate(receipt(), data);
  assert.equal(result.pulls[0].source, COMBINED);
  assert.equal(result.pulls[0].head, HEAD);
  assert.equal(result.pulls[0].proofKind, 'combined-batch-not-solo-head');
  assert.equal(result.outcome, 'observing');
});
test('recorded App branch update retains original source and admitted head distinctly', () => {
  const data = snapshot();
  const active = activeBatch();
  active.members[0].updateIntent = { base: BASE, head: HEAD, created: NOW };
  active.members[0].updatedHead = MERGE;
  data.state.active = active;
  data.pulls[0].head = MERGE;
  const result = evaluate(receipt(), data);
  assert.equal(result.queue.active.members[0].source, HEAD);
  assert.equal(result.queue.active.members[0].admittedHead, MERGE);
  assert.equal(result.pulls[0].source, COMBINED);
});
test('head or enrollment change invalidates all batch proof and classifies old failure as superseded', () => {
  for (const field of ['head', 'enrollment']) {
    const data = snapshot();
    data.state.active = activeBatch();
    data.candidate = evidence(run({ conclusion: 'failure' }), 'candidate', COMBINED,
      TICKET, jobs(run(), { conclusion: 'failure' }));
    data.pulls[1][field] = field === 'head' ? MERGE : '2026-11-04T00:01:00Z';
    const result = evaluate(receipt(), data);
    assert.ok(result.blockers.some(item => item.code === 'source-changed'));
    assert.equal(result.runs[0].classification, 'superseded-source');
    assert.equal(result.pulls[0].proofKind, 'none');
    assert.ok(!result.blockers.some(item => item.code === 'required-producer-failed'));
  }
});
test('required producer failure is distinct from waiting/skipped/missing jobs', () => {
  const item = run();
  assert.equal(evidence(item, 'candidate', HEAD, TICKET,
    jobs(item, { status: 'in_progress', conclusion: null })).outcome, 'incomplete-jobs');
  assert.equal(evidence(run({ status: 'in_progress', conclusion: null }), 'candidate', HEAD, TICKET,
    jobs(item, { status: 'in_progress', conclusion: null })).outcome, 'waiting-or-skipped');
  assert.equal(evidence(item, 'candidate', HEAD, TICKET, jobs(item, { conclusion: 'skipped' })).outcome,
    'incomplete-jobs');
  assert.equal(evidence(item, 'candidate', HEAD, TICKET, jobs(item, { conclusion: 'failure' })).outcome,
    'required-producer-failed');
  assert.equal(evidence(item, 'candidate', HEAD, TICKET, []).outcome, 'incomplete-jobs');
});
test('wrong run/attempt/source, duplicate IDs and untrusted reference URLs cannot become proof', () => {
  for (const change of [{ run_id: 999 }, { run_attempt: 2 }, { head_sha: MERGE }])
    assert.throws(() => evidence(run(), 'candidate', HEAD, TICKET, jobs(run(), change)), /exact run/);
  const duplicate = jobs(run());
  duplicate[1].id = duplicate[0].id;
  assert.throws(() => evidence(run(), 'candidate', HEAD, TICKET, duplicate), /exact run/);
  assert.throws(() => evidence(run({ html_url: 'https://evil.example/token' })), /reference URL/);
});
test('failed duplicate cannot override exact durable actual-tip receipt; prefer controller handoff', () => {
  const data = snapshot();
  const item = run({ head_sha: MERGE });
  const proof = evidence(item, 'qualification', MERGE);
  data.state.completedTip = { sha: MERGE, tree: HEAD, ticket: TICKET,
    qualification: { run: item.id, source: MERGE, job: proof.jobs.find(job => job.name === QUALIFY).id } };
  data.qualification = proof;
  data.duplicates = [evidence(run({ id: 11, head_sha: MERGE, conclusion: 'failure' }),
    'qualification', MERGE, TICKET, jobs(run({ id: 11, head_sha: MERGE })).map(job =>
      job.name === QUALIFY ? { ...job, conclusion: 'failure' } : job))];
  data.controllers = [{ id: 12, url: `${URL}/actions/runs/12` }];
  const result = evaluate(receipt(), data);
  assert.equal(result.outcome, 'observing');
  assert.equal(result.runs[0].classification, 'authoritative-tip-receipt');
  assert.equal(result.runs[1].classification, 'non-authoritative-duplicate-after-qualified-tip');
  assert.equal(result.runs[1].diagnostic, 'publication-guard-cause-unconfirmed');
  assert.equal(result.following.kind, 'controller-handoff');
  assert.equal(result.following.run, 12);
});
test('before qualification follow oldest running first-attempt tip, then hand off after observed join without inventing a receipt', () => {
  const data = snapshot();
  data.state.completedTip = { sha: MERGE, tree: HEAD, ticket: TICKET };
  const older = run({ head_sha: MERGE, status: 'in_progress', conclusion: null });
  const newer = run({ id: 11, head_sha: MERGE, status: 'queued', conclusion: null });
  data.duplicates = [evidence(newer, 'qualification', MERGE), evidence(older, 'qualification', MERGE)];
  data.controllers = [{ id: 12, url: `${URL}/actions/runs/12` }];
  const pending = evaluate(receipt(), data);
  assert.equal(pending.following.kind, 'tip-qualification');
  assert.equal(pending.following.run, 10);
  data.duplicates[1] = evidence(run({ head_sha: MERGE }), 'qualification', MERGE);
  const joined = evaluate(receipt(), data);
  assert.equal(joined.following.kind, 'controller-handoff');
  assert.equal(joined.queue.completedTip.qualification, null);
  assert.ok(joined.runs.every(item => item.classification === 'unreceipted-tip-observation'));
});
test('skipped historical tip runs are not full proof or producer failures while actual tip work is pending', () => {
  const data = snapshot();
  data.state.completedTip = { sha: MERGE, tree: HEAD, ticket: TICKET };
  const skipped = run({ head_sha: MERGE });
  const pending = run({ id: 11, head_sha: MERGE, status: 'in_progress', conclusion: null });
  data.duplicates = [evidence(skipped, 'qualification', MERGE, TICKET,
    jobs(skipped, { conclusion: 'skipped' })), evidence(pending, 'qualification', MERGE)];
  const progressing = evaluate(receipt(), data);
  assert.equal(progressing.outcome, 'observing');
  assert.equal(progressing.following.run, 11);
  assert.equal(progressing.runs[0].classification, 'skipped-or-incomplete-not-qualification');
  assert.equal(progressing.runs[0].outcome, 'incomplete-jobs');
  assert.equal(progressing.queue.completedTip.qualification, null);
  data.duplicates.pop();
  assert.equal(evaluate(receipt(), data).blockers[0].code, 'incomplete-jobs', 'no actual pending/join evidence');
  data.duplicates.push(evidence(run({ id: 12, head_sha: MERGE, conclusion: 'failure' }), 'qualification', MERGE,
    TICKET, jobs(run({ id: 12, head_sha: MERGE }), { conclusion: 'failure' })));
  assert.ok(evaluate(receipt(), data).blockers.some(item => item.code === 'required-producer-failed'));
});
test('stale qualification source or changed proof job never clears failure', () => {
  for (const field of ['source', 'job', 'run']) {
    const data = snapshot();
    const item = evidence(run({ head_sha: MERGE }), 'qualification', MERGE);
    data.qualification = item;
    data.state.completedTip = { sha: MERGE, tree: HEAD, ticket: TICKET,
      qualification: { run: item.id, source: MERGE, job: item.jobs.find(job => job.name === QUALIFY).id } };
    if (field === 'source') item.workflowSource = BASE;
    else data.state.completedTip.qualification[field] += 1;
    assert.ok(evaluate(receipt(), data).blockers.some(blocker => blocker.code === 'qualification-invalid'));
  }
});
test('exact blocked revision only; recovery and main-progress blockers remain explicit', () => {
  const data = snapshot();
  data.state.blocked = [{ number: 1, head: MERGE, enrollment: NOW, reason: 'unsafe raw text not copied' }];
  assert.equal(evaluate(receipt(), data).blockers.length, 0);
  data.state.blocked[0].head = HEAD;
  assert.equal(evaluate(receipt(), data).blockers[0].code, 'revision-blocked');
  data.state.blocked = [];
  data.state.active = activeBatch();
  data.state.active.phase = 'recovery-required';
  assert.equal(evaluate(receipt(), data).blockers[0].code, 'recovery-required');
  data.state.active = null;
  data.state.completedTip = { sha: BASE, tree: HEAD, ticket: TICKET };
  assert.equal(evaluate(receipt(), data).blockers[0].code, 'main-moved-before-qualification');
});
test('old-ticket failed App checks remain retained historical evidence, not current failure authority', () => {
  const data = snapshot();
  data.state.active = activeBatch();
  data.checks[1] = [{ id: 33, app: 42, name: GATE, head: HEAD, ticket: OTHER_TICKET,
    status: 'completed', conclusion: 'failure', url: `${URL}/runs/33` }];
  const old = evaluate(receipt(), data);
  assert.equal(old.pulls[0].checks[0].classification, 'historical-not-current-authority');
  assert.equal(old.blockers.length, 0);
  data.checks[1][0].ticket = TICKET;
  const current = evaluate(receipt(), data);
  assert.equal(current.pulls[0].checks[0].classification, 'current-ticket');
  assert.equal(current.blockers[0].code, 'required-producer-failed');
});
test('old handoff is explicitly superseded by head/ticket/source/enrollment changes without modifying history', () => {
  const data = snapshot();
  data.state.active = activeBatch();
  const old = evaluate(receipt(), data);
  const before = JSON.stringify(old);
  data.state.active.ticket = OTHER_TICKET;
  data.state.active.members[0].head = MERGE;
  data.state.active.digest = manifestDigest(manifestFor(data.state.active));
  data.pulls[0].head = MERGE;
  const current = supersede(evaluate(receipt(), data), old);
  assert.equal(JSON.stringify(old), before);
  assert.equal(current.superseded[0].previousReceipt, digest(old));
  assert.ok(current.superseded[0].changes.includes('head'));
  assert.ok(current.superseded[0].changes.includes('ticket'));
  assert.equal(current.superseded.length, 2);
});
test('unknown/corrupt previous schema, owner, principal, targets, proof and references fail closed', () => {
  const previous = evaluate(receipt(), snapshot());
  for (const change of [
    item => { item.schema = 'old-unknown'; }, item => { item.campaign.owner = 'other-owner'; },
    item => { item.campaign.targets = [1]; }, item => { item.pulls[0].head = 'unknown'; },
    item => { item.references.main.sha = null; }, item => { item.outcome = 'MERGED_VERIFIED'; },
  ]) {
    const item = structuredClone(previous);
    change(item);
    assert.throws(() => validateReceipt(item, campaign()));
  }
  previous.principal = 'other';
  assert.throws(() => supersede(evaluate(receipt(), snapshot()), previous), /principal/);
});
test('changed observer source or bytes explicitly supersede old validation identity', () => {
  const previous = evaluate(receipt(), snapshot());
  const current = evaluate(receipt(), snapshot());
  current.observer.source = MERGE;
  current.observer.digest = 'f'.repeat(64);
  const result = supersede(current, previous);
  assert.deepEqual(result.superseded[0].changes, ['observer.source', 'observer.digest']);
});
test('CLI requires explicit unique fixed targets, rejects unknown switches and bounds watch', () => {
  const args = ['--repository', REPO, '--prs', '2,1', '--owner', 'delivery-1', '--output', '.scratch\\campaign'];
  assert.deepEqual(parseArgs(args).campaign.targets, [1, 2]);
  assert.equal(createReader(parseArgs(args)).repository, REPO, 'actual CLI reader receives parsed repository');
  for (const invalid of [
    [...args, '--observations', '0'], [...args, '--observations', '2'], [...args, '--observations', '1001'],
    [...args, '--command-seconds', '301'],
    [...args, '--budget-seconds', '86401'], [...args, '--unknown', 'x'],
    [...args, '--prs', '3'], args.map(value => value === '2,1' ? '1,1' : value),
    args.map(value => value === '2,1' ? '1;2' : value),
  ]) assert.throws(() => parseArgs(invalid));
});
test('receipt output rejects paths outside the owning scratch tree', async () => {
  await assert.rejects(() => scratchPath(process.cwd(), require('node:path').join(process.cwd(), 'eng', 'bad.json')),
    /owning scratch path/);
});
test('workflow failure without failed required producers remains distinct and blocking', () => {
  const data = snapshot();
  data.state.active = activeBatch();
  data.candidate = evidence(run({ conclusion: 'failure' }), 'candidate', COMBINED);
  assert.equal(data.candidate.outcome, 'run-failed-without-producer-failure');
  assert.equal(evaluate(receipt(), data).blockers[0].code, 'run-failed-without-producer-failure');
});

function fakeObservation(overrides = {}) {
  const receipts = [];
  const lines = [];
  const options = { campaign: campaign(), observations: 1, intervalSeconds: 30 };
  const reader = { deadline: Date.parse(NOW) + 60_000, reconciliations: [],
    preflight: async expected => { assert.equal(expected, 'publisher'); return 'publisher'; },
    snapshot: async () => snapshot(), ...overrides };
  return { options, reader, receipts, dependencies: { reader, observer: observer(),
    now: () => new Date(NOW), save: async item => receipts.push(item), stdout: line => lines.push(line),
    progress: () => {}, wait: async () => { throw new Error('one-shot must not wait'); } }, lines };
}
test('one shot has nonzero actionable budget outcome and machine JSON, not implicit success', async () => {
  const fixture = fakeObservation();
  assert.equal(await observe(fixture.options, fixture.dependencies), 3);
  assert.equal(fixture.receipts[0].blockers[0].code, 'budget-exhausted');
  assert.equal(JSON.parse(fixture.lines[0]).schema, SCHEMA);
});
test('bounded observation finishes all target merges without sleeping or spawning background work', async () => {
  const data = snapshot();
  data.pulls = data.pulls.map(merged);
  data.reachable = { 1: true, 2: true };
  const fixture = fakeObservation({ snapshot: async () => data });
  assert.equal(await observe(fixture.options, fixture.dependencies), 0);
  assert.equal(fixture.receipts[0].outcome, 'MERGED_VERIFIED');
});
for (const code of ['transport-error', 'authentication-error', 'command-timeout', 'cancelled', 'budget-exhausted'])
  test(`finite ${code} persists explicit failure without stale success fallback`, async () => {
    const fixture = fakeObservation({ snapshot: async () => { throw new ObservationError(code, 'fake boundary'); } });
    assert.equal(await observe(fixture.options, fixture.dependencies), 2);
    assert.equal(fixture.receipts[0].blockers[0].code, code);
    assert.equal(fixture.receipts[0].pulls.length, 0);
  });
test('cancel during bounded interval records a terminal receipt; no next command starts', async () => {
  const fixture = fakeObservation();
  fixture.options.observations = 2;
  fixture.dependencies.wait = async () => { throw Object.assign(new Error(), { name: 'AbortError' }); };
  assert.equal(await observe(fixture.options, fixture.dependencies), 2);
  assert.equal(fixture.receipts.length, 2);
  assert.equal(fixture.receipts[1].blockers[0].code, 'cancelled');
});

function content(text) {
  const bytes = Buffer.from(text);
  return { type: 'file', encoding: 'base64', content: bytes.toString('base64'),
    sha: createHash('sha1').update(`blob ${bytes.length}\0`).update(bytes).digest('hex') };
}
function graphPull(item) {
  return { number: item.number, state: item.state, headRefOid: item.head, baseRefName: item.base,
    isDraft: item.draft, mergeable: item.mergeable, reviewDecision: item.review, url: item.url,
    mergedAt: item.mergedAt, mergeCommit: item.merge ? { oid: item.merge } : null,
    author: { login: item.author }, headRepository: { nameWithOwner: item.repository },
    autoMergeRequest: item.enrollment ? { enabledAt: item.enrollment, mergeMethod: item.method } : null,
    labels: { totalCount: item.labels.length, pageInfo: { hasNextPage: false },
      nodes: item.labels.map(name => ({ name })) } };
}
function fakeCli({ data = snapshot(), transform = (endpoint, result) => result, fault = () => null,
  clock = () => Date.parse(NOW) } = {}) {
  const calls = [];
  const runner = async (file, args, settings) => {
    calls.push({ file, args, settings });
    const failure = fault(file, args, calls.length);
    if (failure) throw failure;
    if (file === 'git') {
      if (args[0] === 'remote') return `https://github.com/${REPO}.git`;
      if (args[0] === 'fetch') return '';
      if (args[0] === 'rev-parse') return MERGE;
      if (args[0] === 'merge-base') return '';
      throw new Error('Unexpected Git operation');
    }
    assert.equal(file, 'gh');
    assert.equal(args[0], 'api');
    const endpoint = args[5];
    assert.ok(args[4] === 'GET' || endpoint === 'graphql' && args[4] === 'POST'
      && args[7].startsWith('query=query {'), 'Only REST GET or GraphQL query is allowed.');
    let result;
    if (endpoint === 'user') result = { login: 'publisher' };
    else if (endpoint === `repos/${REPO}`) result = { full_name: REPO, permissions: { pull: true } };
    else if (endpoint === 'graphql') result = { data: { repository: Object.fromEntries(data.pulls.map(item =>
      [`p${item.number}`, graphPull(item)])) } };
    else if (endpoint.includes('actions/variables?')) result = { total_count: 3, variables: [
      { name: 'KORA_MERGE_QUEUE_MODE', value: 'enabled' }, { name: 'KORA_MERGE_QUEUE_PROFILE', value: 'batch-v1' },
      { name: 'KORA_MERGE_QUEUE_APP_ID', value: '42' }] };
    else if (endpoint.endsWith('git/ref/heads/automation/merge-queue-state'))
      result = { object: { sha: BASE, type: 'commit' } };
    else if (endpoint.includes('contents/queue.json?')) result = content(JSON.stringify(data.state));
    else if (endpoint.includes('contents/.github/workflows/ci.yml?'))
      result = content('# merge-queue-job-schema: windows-12-v1\n');
    else if (endpoint.includes('/check-runs?')) result = { total_count: 0, check_runs: [] };
    else if (endpoint.includes('/attempts/1/jobs?')) result = { total_count: jobs(run()).length, jobs: jobs(run()) };
    else if (endpoint.endsWith('/actions/runs/10')) result = run();
    else if (endpoint.includes('/runs?')) result = { total_count: 0, workflow_runs: [] };
    else throw new Error(`Unexpected read boundary ${endpoint}`);
    return JSON.stringify(transform(endpoint, result));
  };
  return { calls, reader: new Reader({ repository: REPO, worktree: process.cwd(), deadline: Date.parse(NOW) + 120_000,
    run: runner, now: clock }) };
}
test('fake gh/git snapshot only reads explicit PRs and current context, fetches fresh main, records publisher', async () => {
  const fixture = fakeCli();
  const principal = await fixture.reader.preflight('publisher');
  const data = await fixture.reader.snapshot(campaign(), principal);
  assert.equal(data.principal, 'publisher');
  assert.equal(data.references.main.sha, MERGE);
  assert.equal(data.references.queue.commit, BASE);
  assert.equal(fixture.calls.filter(call => call.file === 'git' && call.args[0] === 'fetch').length, 1);
  assert.equal(fixture.calls.filter(call => call.args.includes('graphql')).length, 2);
});
test('ambiguous CLI transport reconciles by exact successful read, never log text or gh run watch exit', async () => {
  let first = true;
  const fixture = fakeCli({ fault: file => {
    if (file === 'gh' && first) { first = false; return new ObservationError('transport-error', 'gh'); }
    return null;
  } });
  assert.equal(await fixture.reader.preflight('publisher'), 'publisher');
  assert.deepEqual(fixture.calls[1].args, fixture.calls[2].args);
  assert.equal(fixture.reader.reconciliations[0].classification, 'transport-error-reconciled-by-exact-read');
});
test('auth denial has no fallback identity or transport; failed exact read never becomes success', async () => {
  for (const code of ['authentication-error', 'transport-error']) {
    const fixture = fakeCli({ fault: file => file === 'gh' ? new ObservationError(code, 'gh') : null });
    await assert.rejects(() => fixture.reader.preflight('publisher'), new RegExp(code));
    assert.equal(fixture.calls.filter(call => call.file === 'gh').length, code === 'transport-error' ? 2 : 1);
  }
  await assert.rejects(() => fakeCli().reader.preflight('other'), /authentication-error/);
});
for (const kind of ['partial-graphql', 'missing-pr', 'missing-page', 'inconsistent-page', 'bad-blob', 'unknown-schema',
  'unknown-mode', 'labels-page', 'changed-queue', 'changed-head', 'malformed-merge']) test(`${kind} cannot establish campaign evidence`, async () => {
  let refs = 0;
  let graphs = 0;
  const fixture = fakeCli({ transform: (endpoint, result) => {
    if (endpoint === 'graphql') {
      graphs++;
      if (kind === 'partial-graphql') return { ...result, errors: [{ message: 'not copied into receipt' }] };
      if (kind === 'missing-pr') result.data.repository.p2 = null;
      if (kind === 'labels-page') result.data.repository.p1.labels.pageInfo.hasNextPage = true;
      if (kind === 'malformed-merge') result.data.repository.p1.mergeCommit = {};
      if (kind === 'changed-head' && graphs > 1) result.data.repository.p1.headRefOid = COMBINED;
    }
    if (endpoint.includes('actions/variables?')) {
      if (kind === 'missing-page') return { total_count: 4, variables: [] };
      if (kind === 'inconsistent-page') return { total_count: endpoint.includes('page=1') ? 4 : 5,
        variables: [{ name: 'one', value: '' }] };
      if (kind === 'unknown-mode') result.variables[0].value = 'unknown';
    }
    if (endpoint.includes('contents/queue.json?')) {
      if (kind === 'bad-blob') result.sha = MERGE;
      if (kind === 'unknown-schema') return content('{"version":99}');
    }
    if (endpoint.includes('git/ref/heads/')) {
      refs++;
      if (kind === 'changed-queue' && refs > 1) result.object.sha = MERGE;
    }
    return result;
  } });
  await assert.rejects(() => fixture.reader.snapshot(campaign(), 'publisher'));
});
test('exact current-ticket run is read by identity and attempt, not latest unrelated main run', async () => {
  const data = snapshot();
  data.state.active = { number: 1, head: HEAD, base: BASE, ticket: TICKET, phase: 'dispatched',
    dispatchRecorded: true, created: NOW, enrollment: NOW, checks: {} };
  const item = run({ display_title: title(data.state.active) });
  const fixture = fakeCli({ data, transform: (endpoint, result) => {
    if (endpoint.includes('/ci.yml/runs?')) return { total_count: 2,
      workflow_runs: [run({ id: 11 }), item] };
    if (endpoint.endsWith('/actions/runs/10')) return item;
    return result;
  } });
  const result = await fixture.reader.snapshot(campaign(), 'publisher');
  assert.equal(result.candidate.id, 10);
  assert.equal(result.candidate.source, HEAD);
  assert.ok(fixture.calls.some(call => call.args.some(arg => arg.includes('/attempts/1/jobs?'))));
});
test('PR-triggered controller handoff preserves trigger head without claiming it is tested workflow source', async () => {
  const fixture = fakeCli({ transform: (endpoint, result) => endpoint.includes('merge-queue.yml/runs?status=pending')
    ? { total_count: 1, workflow_runs: [run({ id: 44, head_sha: HEAD, head_branch: 'feature',
      status: 'pending', conclusion: null, event: 'pull_request_target', html_url: `${URL}/actions/runs/44` })] }
    : result });
  const data = await fixture.reader.snapshot(campaign(), 'publisher');
  const result = evaluate(receipt(), data);
  assert.equal(result.following.run, 44);
  assert.equal(result.following.triggerHead, HEAD);
  assert.equal(result.following.classification, 'trigger-context-not-validation-source');
  assert.equal(result.pulls[0].proofKind, 'none');
});
test('active exact candidate observation does not query unconsumed controller listings', async () => {
  const data = snapshot();
  data.state.active = { number: 1, head: HEAD, base: BASE, ticket: TICKET, phase: 'dispatched',
    dispatchRecorded: true, created: NOW, enrollment: NOW, checks: {} };
  const item = run({ status: 'in_progress', conclusion: null, display_title: title(data.state.active) });
  const fixture = fakeCli({ data, transform: (endpoint, result) => {
    assert.ok(!endpoint.includes('merge-queue.yml/runs?'), 'controller listing is not consumed before handoff');
    if (endpoint.includes('/ci.yml/runs?')) return { total_count: 1, workflow_runs: [item] };
    if (endpoint.endsWith('/actions/runs/10')) return item;
    return result;
  } });
  const observed = await fixture.reader.snapshot(campaign(), 'publisher');
  assert.equal(evaluate(receipt(), observed).following.kind, 'candidate');
  assert.equal(observed.controllers.length, 0);
});
test('pending actual-tip producer does not query unconsumed controller listings before its join', async () => {
  const data = snapshot();
  data.state.completedTip = { sha: MERGE, tree: HEAD, ticket: TICKET };
  const item = run({ head_sha: MERGE, status: 'in_progress', conclusion: null });
  const fixture = fakeCli({ data, transform: (endpoint, result) => {
    assert.ok(!endpoint.includes('merge-queue.yml/runs?'), 'controller is not consumed before tip join');
    if (endpoint.includes('/ci.yml/runs?')) return { total_count: 1, workflow_runs: [item] };
    if (endpoint.endsWith('/actions/runs/10')) return item;
    if (endpoint.includes('/attempts/1/jobs?')) return { total_count: jobs(item).length, jobs: jobs(item) };
    return result;
  } });
  const observed = await fixture.reader.snapshot(campaign(), 'publisher');
  assert.equal(evaluate(receipt(), observed).following.kind, 'tip-qualification');
  assert.equal(observed.controllers.length, 0);
});
test('malformed current or retained App ticket identity is not silently treated as historical evidence', async () => {
  const check = { id: 44, app: { id: 42 }, name: GATE, head_sha: HEAD, status: 'completed',
    conclusion: 'failure', external_id: '-'.repeat(36), html_url: `${URL}/runs/44` };
  const fixture = fakeCli({ transform: (endpoint, result) => endpoint.includes('/check-runs?')
    ? { total_count: 1, check_runs: [check] } : result });
  await assert.rejects(() => fixture.reader.snapshot(campaign(), 'publisher'), /candidate-head App check/);
  const retained = evaluate(receipt(), snapshot());
  retained.pulls[0].checks = [{ id: 44, app: 42, name: GATE, head: HEAD, ticket: '-'.repeat(36),
    status: 'completed', conclusion: 'failure', url: `${URL}/runs/44` }];
  assert.throws(() => validateReceipt(retained, campaign()), /previous PR identities/);
});
test('Git ancestry exit 1 differs from fatal Git/transport failure', async () => {
  const data = snapshot();
  data.pulls = data.pulls.map(merged);
  for (const exitCode of [1, 128]) {
    const fixture = fakeCli({ data, fault: (file, args) => file === 'git' && args[0] === 'merge-base'
      ? Object.assign(new ObservationError('transport-error', 'git'), { exitCode }) : null });
    if (exitCode === 1) assert.deepEqual((await fixture.reader.snapshot(campaign(), 'publisher')).reachable,
      { 1: false, 2: false });
    else await assert.rejects(() => fixture.reader.snapshot(campaign(), 'publisher'), /transport-error/);
  }
});
test('clock budget/cancellation prevents any CLI process from starting', async () => {
  let starts = 0;
  const controller = new AbortController();
  const reader = new Reader({ repository: REPO, worktree: '.', deadline: 0, now: () => 1,
    run: async () => { starts++; return ''; }, signal: controller.signal });
  await assert.rejects(() => reader.command('gh', []), /budget-exhausted/);
  controller.abort();
  await assert.rejects(() => reader.command('gh', []), /cancelled/);
  assert.equal(starts, 0);
});
test('native child cancellation/timeout waits for close; arbitrary stderr is not disclosed', async () => {
  await assert.rejects(() => execute(process.execPath, ['-e', 'process.stderr.write("PRIVATE");process.exit(1)'],
    { timeout: 5000 }), error => error.message === `transport-error: ${process.execPath}` && error.exitCode === 1);
  await assert.rejects(() => execute(process.execPath, ['-e', 'setInterval(()=>{},1000)'], { timeout: 5 }),
    /command-timeout/);
  const controller = new AbortController();
  controller.abort();
  await assert.rejects(() => execute(process.execPath, ['-e', 'setInterval(()=>{},1000)'],
    { timeout: 5000, signal: controller.signal }), /cancelled/);
});
