const { test } = require('node:test');
const assert = require('node:assert/strict');
const { reconcile } = require('./controller.cjs');
const { validateState, WINDOWS_12_JOBS, DELIVERY_JOBS, GATE, title,
  REJECTED_ADMISSION_JOBS, terminalRejectedAdmission, terminalRejectedPreflight } = require('./policy.cjs');
const { profile, CONTROL_PATH, digest, manifestFor } = require('./batch-policy.cjs');
const { changeBatchState } = require('./recovery.cjs');
const sha = n => n.toString(16).padStart(40, '0');
const BASE = sha(100);
const TICKET = '01234567-89ab-cdef-0123-456789abcdef';
const NOW = new Date('2026-01-01T12:00:00Z');
const ENROLLED = '2026-01-01T11:00:00.000Z';
const clone = value => structuredClone(value);

class BatchFake {
  repository = 'owner/repo';
  state = { version: 2, profile: 'batch-v1', active: null, blocked: [], completedTip: null, singletonRetries: [] };
  base = BASE;
  prs = Array.from({ length: 7 }, (_, index) => ({ number: index + 1, state: 'open', draft: false,
    merged: false, mergeable: true, labels: [], head: { sha: sha(index + 1), repo: { full_name: 'owner/repo' } },
    base: { ref: 'main' }, auto_merge: { merge_method: 'squash' }, enrollment: ENROLLED, review: null }));
  commits = new Map([[BASE, { tree: sha(200), parents: [] }]]);
  calls = [];
  workflowRuns = [];
  workflowJobs = [];
  inventory = { schema: 'windows-12-v1', blob: sha(999), jobs: [...WINDOWS_12_JOBS] };
  qualified = false;
  conflict = false;
  conflicts = new Set();
  qualificationObservation = null;
  commit = sha(800);
  administratorAllowed = true;
  quiet = true;
  info() {}
  notice(message) { this.calls.push(['notice', message]); }
  async verifyProtection(value) { this.calls.push(['protection', value]); }
  async load() { return clone(this.state); }
  async save(state) { validateState(state); this.state = clone(state); this.commit = sha(Number.parseInt(this.commit, 16) + 1); }
  async main() { return this.base; }
  async pulls() { return clone(this.prs.filter(pr => pr.state === 'open')); }
  async pull(number) { return clone(this.prs.find(pr => pr.number === number)); }
  async enrollment(number) { return this.prs.find(pr => pr.number === number).enrollment; }
  async reviewState(number) { return this.prs.find(pr => pr.number === number).review; }
  async reviewReady(number) { return [null, 'APPROVED'].includes(await this.reviewState(number)); }
  async files() { return ['src/Kora.Core/Feature.cs']; }
  async trustedInventory() { return clone(this.inventory); }
  async expectedJobs() { return [...WINDOWS_12_JOBS]; }
  async rejectedAdmission(source, run, jobs) {
    return run.head_sha === source && (terminalRejectedAdmission(run, jobs, this.inventory.blob)
      || terminalRejectedPreflight(run, jobs, this.inventory.blob));
  }
  async syntheticPrefix(active, index) {
    this.calls.push(['synthetic', index]);
    if (this.conflict || this.conflicts.has(active.members[index].number)) return null;
    const prefix = { sha: sha(300 + index), tree: sha(400 + index) };
    this.commits.set(prefix.sha, { tree: prefix.tree,
      parents: [index ? active.prefixes[index - 1].sha : active.base, active.members[index].head] });
    if (index === 0) this.commits.set(active.members[0].head, { tree: prefix.tree, parents: [BASE] });
    return prefix;
  }
  async dispatch(active) {
    this.calls.push(['dispatch', clone(active)]);
    this.workflowRuns = [{ id: 9, run_attempt: 1, head_sha: active.base, status: 'completed',
      conclusion: 'success', event: 'workflow_dispatch', head_branch: 'main', display_title: title(active) }];
    this.workflowJobs = this.inventory.jobs.map((name, index) => ({ id: index + 1, name,
      run_id: 9, run_attempt: 1, head_sha: active.base, status: 'completed', conclusion: 'success' }));
  }
  async runs() { return clone(this.workflowRuns); }
  async jobs() { return clone(this.workflowJobs); }
  async containsBase(base, head) {
    return head === base || this.commits.get(head)?.parents.includes(base) === true;
  }
  async memberTree(head, tree) { return this.commits.get(head)?.tree === tree; }
  async commitInfo(head) { return clone(this.commits.get(head)); }
  async createCheck(active, name) { this.calls.push(['check', active.head, name]); return this.calls.length + 100; }
  async finishCheck(id, conclusion, message) { this.calls.push(['finish', id, conclusion, message]); }
  async updateBranch(active) {
    this.calls.push(['update', clone(active)]);
    const pr = this.prs.find(pr => pr.number === active.number);
    const index = this.state.active.members.findIndex(member => member.number === active.number);
    pr.head.sha = sha(500 + index);
    this.commits.set(pr.head.sha, { tree: this.state.active.prefixes[index].tree,
      parents: [active.head, this.base], author: 'queue[bot]', verified: true });
  }
  async expectedUpdate(member, base, tree) {
    const pr = await this.pull(member.number);
    if (pr.head.sha === member.head) return null;
    const commit = await this.commitInfo(pr.head.sha);
    assert.deepEqual(commit.parents, [member.head, base]);
    assert.equal(commit.tree, tree);
    assert.equal(commit.author, 'queue[bot]');
    assert.equal(commit.verified, true);
    return pr.head.sha;
  }
  async merge(active) {
    this.calls.push(['merge', clone(active)]);
    const pr = this.prs.find(pr => pr.number === active.number);
    assert.equal(pr.head.sha, active.head);
    const commit = sha(600 + active.number);
    this.commits.set(commit, { tree: this.commits.get(active.head).tree, parents: [this.base] });
    this.base = commit;
    Object.assign(pr, { merged: true, state: 'closed', merge_commit_sha: commit, enrollment: null, auto_merge: null });
    return { merged: true };
  }
  async wake() { this.calls.push(['wake']); }
  async cancel(id) { this.calls.push(['cancel', id]); }
  async tipQualification() {
    return this.qualified ? { owner: { run: 10, source: this.base, attempt: 1 },
      qualification: { run: 10, job: 11, source: this.base } } : clone(this.qualificationObservation);
  }
  async dispatchQualification(tip) { this.calls.push(['qualification', tip.sha]); }
  async administrator() { return this.administratorAllowed; }
  async ciQuiescent() { return this.quiet; }
}
const step = (api, extra = {}) => reconcile(api, { profile: 'batch-v1', now: NOW, ticket: TICKET, ...extra });
const count = (api, name) => api.calls.filter(call => call[0] === name).length;
async function until(api, target, limit = 40) {
  for (let index = 0; index < limit; index++) if (await step(api) === target) return;
  assert.fail(`Did not reach ${target}`);
}

test('independent source completion admits another candidate while its main run is running or has failed', async () => {
  for (const mainStatus of ['running', 'failed']) {
    const api = new BatchFake();
    api.independent = true;
    api.inventory = { schema: 'windows-12-delivery-v1', blob: sha(999), jobs: [...DELIVERY_JOBS] };
    await until(api, 'batch-completed');
    const tip = clone(api.state.completedTip);
    assert.equal(tip.delivery, 'independent-v1');
    assert.equal(tip.validation.run, 9);
    assert.equal(api.state.deliveryProfile, 'independent-v1');
    api.qualificationObservation = { owner: { run: 42, source: tip.sha, attempt: 1 },
      qualification: null, provisional: false, terminal: mainStatus === 'failed' };
    await step(api);
    assert.ok(api.state.active);
    assert.equal(api.state.completedTip.qualification, undefined);
    assert.equal(api.state.completedTip.qualificationOwner.run, 42);
    assert.equal(count(api, 'qualification'), 0);
    assert.equal(count(api, 'cancel'), 0);
  }
});

test('independent delivery still waits for definite source-run admission before moving main', async () => {
  const api = new BatchFake();
  api.independent = true;
  api.inventory = { schema: 'windows-12-delivery-v1', blob: sha(999), jobs: [...DELIVERY_JOBS] };
  await until(api, 'batch-completed');
  api.qualificationObservation = { owner: { run: 42, source: api.base, attempt: 1 }, provisional: true };
  assert.equal(await step(api), 'waiting-for-tip-qualification');
  assert.equal(api.state.active, null);
  assert.equal(count(api, 'qualification'), 0);
  api.qualificationObservation = null;
  assert.equal(await step(api), 'waiting-for-tip-qualification');
  assert.equal(count(api, 'qualification'), 1);
  assert.equal(await step(api), 'waiting-for-tip-qualification');
  assert.equal(count(api, 'qualification'), 1);
});

test('exact reviewed failed preflight retires batch only after complete terminal unexpanded matrix evidence', async () => {
  for (const change of ['terminal', 'running', 'missing', 'unknown', 'blob', 'version-skipped']) {
    const api = new BatchFake();
    api.inventory.blob = 'c75173721c4131cf29d45482ea2edb92934070f1';
    await step(api);
    api.workflowRuns[0].conclusion = 'failure';
    api.workflowJobs = REJECTED_ADMISSION_JOBS.map((name, index) => ({ id: index + 1, name,
      run_id: 9, run_attempt: 1, head_sha: BASE, status: 'completed',
      conclusion: name === 'Admit CI execution' ? 'success'
        : name === 'Resolve build version' || name === WINDOWS_12_JOBS[2] || name === 'Windows integration tests'
          || name.startsWith('Windows integration shard (') ? 'failure' : 'skipped' }));
    if (change === 'running') {
      api.workflowRuns[0].status = 'in_progress';
      Object.assign(api.workflowJobs.at(-1), { status: 'in_progress', conclusion: null });
    }
    if (change === 'missing') api.workflowJobs.pop();
    if (change === 'unknown') api.workflowJobs.at(-1).conclusion = null;
    if (change === 'blob') api.inventory.blob = sha(999);
    if (change === 'version-skipped') api.workflowJobs.find(job => job.name === 'Resolve build version').conclusion = 'skipped';
    assert.equal(await step(api), 'retiring', change);
    assert.equal(api.state.active.ticket, TICKET);
    assert.equal(count(api, 'merge'), 0);
    assert.equal(api.calls.some(call => call[0] === 'finish' && call[2] === 'success'), false);
    assert.equal(await step(api), change === 'terminal' ? 'retired' : 'retiring', change);
    if (change === 'running') {
      api.workflowRuns[0].status = 'completed';
      assert.equal(await step(api), 'retiring');
      Object.assign(api.workflowJobs.at(-1), { status: 'completed', conclusion: 'skipped' });
      assert.equal(await step(api), 'retired');
    }
    if (['terminal', 'running'].includes(change)) {
      assert.equal(api.state.active, null);
      assert.equal(api.state.singletonRetries.length, 5);
    } else assert.equal(api.state.active.ticket, TICKET);
    assert.equal(count(api, 'dispatch'), 1);
  }
});

test('an admission-rejected batch can retire but never attest proof or merge a member', async () => {
  for (const incomplete of [false, true]) {
    const api = new BatchFake();
    api.inventory.blob = '97e22a8f88bcdc9bbaef32cf9e7200dd6e5c43cd';
    await reconcile(api, { profile: 'batch-v1', now: NOW, ticket: TICKET });
    api.state.active.phase = 'retiring';
    api.state.active.reason = 'Admission rejected before execution.';
    api.workflowRuns[0].conclusion = 'failure';
    api.workflowJobs = REJECTED_ADMISSION_JOBS.map((name, index) => ({ id: index + 1, name, run_id: 9,
      run_attempt: 1, head_sha: BASE, status: 'completed',
      conclusion: name === 'Admit CI execution' ? 'failure' : 'skipped' }));
    if (incomplete) api.workflowJobs.pop();
    assert.equal(await reconcile(api, { profile: 'batch-v1', now: NOW, ticket: TICKET }),
      incomplete ? 'retiring' : 'retired');
    assert.equal(api.calls.some(call => call[0] === 'merge'), false);
    assert.equal(api.calls.some(call => call[0] === 'finish' && call[2] === 'success'), false);
    if (incomplete) {
      assert.equal(api.state.active.ticket, TICKET);
      assert.equal(api.state.singletonRetries.length, 0);
    } else {
      assert.equal(api.state.active, null);
      assert.equal(api.state.singletonRetries.length, 5);
      assert.equal(api.state.completedTip, null);
    }
  }
});

test('batch profile is explicit, bounded and never implicitly migrates legacy state', async () => {
  assert.equal(profile(), 'singleton-v1');
  for (const value of ['future', 'enabled', null]) assert.throws(() => profile(value));
  const api = new BatchFake();
  api.state = { version: 1, active: null, blocked: [] };
  await assert.rejects(step(api), /explicitly migrated/);
  assert.equal(count(api, 'dispatch'), 0);
});

test('up to five already-waiting members follow priority then number, and active work cannot be interrupted', async () => {
  const api = new BatchFake();
  api.prs[5].labels = ['queue:priority-high'];
  api.prs[0].labels = ['queue:priority-low'];
  // The chosen first source is made current by the synthetic fake.
  await step(api);
  assert.deepEqual(api.state.active.members.map(member => member.number), [6, 2, 3, 4, 5]);
  api.prs[6].labels = ['queue:priority-high'];
  await step(api);
  assert.deepEqual(api.state.active.members.map(member => member.number), [6, 2, 3, 4, 5]);
  assert.equal(count(api, 'dispatch'), 1);
});

test('all original PRs merge individually with prefix receipts, exact combined final tree and no copied CI checks', async () => {
  const api = new BatchFake();
  await until(api, 'batch-completed');
  assert.equal(count(api, 'dispatch'), 1);
  assert.equal(count(api, 'merge'), 5);
  assert.equal(count(api, 'update'), 4);
  assert.ok(api.calls.filter(call => call[0] === 'check').every(call => call[2] === GATE));
  assert.ok(api.calls.filter(call => call[0] === 'finish' && call[2] === 'success')
    .every(call => call[3].includes('NOT independently tested') && call[3].includes(sha(304))));
  assert.deepEqual(api.prs.filter(pr => pr.merged).map(pr => pr.number), [1, 2, 3, 4, 5]);
  assert.equal(api.state.completedTip.tree, sha(404));
  assert.equal(api.state.completedTip.sha, api.base);
  assert.notEqual(api.state.completedTip.sha, api.state.completedTip.combined);
  assert.equal(api.state.completedTip.members.length, 5);
  assert.equal(await step(api), 'waiting-for-tip-qualification');
  assert.equal(await step(api), 'waiting-for-tip-qualification');
  assert.equal(count(api, 'qualification'), 1);
  assert.equal(count(api, 'dispatch'), 1);
  api.qualified = true;
  await step(api);
  assert.equal(count(api, 'dispatch'), 2);
});

test('manifest source/base/enrollment/review/prefix/inventory mutations fail closed', async () => {
  const original = new BatchFake();
  await step(original);
  for (const mutate of [
    active => { active.base = sha(999); }, active => { active.members[0].head = sha(999); },
    active => { active.members[0].enrollment = NOW.toISOString(); },
    active => { active.members[0].review = 'APPROVED'; }, active => { active.prefixes[0].tree = sha(999); },
    active => { active.inventory.jobs.pop(); }, active => { active.head = sha(999); },
  ]) {
    const state = clone(original.state);
    mutate(state.active);
    assert.throws(() => validateState(state));
  }
});

test('unknown mergeability, reviews, enrollment and conflicting priority labels are not eligible', async () => {
  const api = new BatchFake();
  api.prs[0].mergeable = null;
  api.prs[1].review = undefined;
  api.prs[2].enrollment = null;
  api.prs[3].labels = ['queue:priority-high', 'queue:priority-low'];
  api.prs[4].draft = true;
  await step(api);
  assert.deepEqual(api.state.active.members.map(member => member.number), [6, 7]);
});

test('control surfaces and group-failure retries use the existing fully validated singleton path', async () => {
  for (const path of ['.github/workflows/ci.yml', 'eng/merge-queue/controller.cjs',
    'eng/Publish-GitHubRelease.ps1', 'eng/GitHubRelease.Common.ps1', 'eng/Get-BuildVersion.ps1', 'GitVersion.yml'])
    assert.ok(CONTROL_PATH.test(path), path);
  for (const retry of [false, true]) {
    const api = new BatchFake();
    api.commits.set(sha(1), { tree: sha(400), parents: [BASE] });
    if (retry) api.state.singletonRetries = [{ number: 1, head: sha(1), enrollment: ENROLLED, review: null }];
    else api.files = async () => ['.github/workflows/ci.yml'];
    await step(api);
    assert.equal(api.state.active.kind, undefined);
    assert.equal(count(api, 'synthetic'), 0);
    assert.equal(count(api, 'check'), 4);
    assert.equal(count(api, 'dispatch'), 1);
    assert.equal(api.calls.find(call => call[0] === 'dispatch')[1].head, sha(1));
  }
});

test('batch-profile control singleton retains complete exact-head proof and records actual merged-tip qualification', async () => {
  for (const independent of [false, true]) {
    const api = new BatchFake();
    api.independent = independent;
    if (independent) {
      api.inventory = { schema: 'windows-12-delivery-v1', blob: sha(999), jobs: [...DELIVERY_JOBS] };
      api.expectedJobs = async () => [...DELIVERY_JOBS];
    }
    api.prs = [api.prs[0]];
    api.files = async () => ['.github/workflows/ci.yml'];
    api.commits.set(sha(1), { tree: sha(400), parents: [BASE] });
    assert.equal(await step(api), 'dispatched');
    assert.equal(api.state.active.kind, undefined);
    assert.equal(await step(api), 'merged');
    assert.equal(api.state.active, null);
    assert.equal(api.state.completedTip.sha, api.base);
    assert.equal(api.state.completedTip.tree, sha(400));
    assert.equal(count(api, 'check'), 4);
    assert.equal(count(api, 'dispatch'), 1);
    assert.equal(count(api, 'merge'), 1);
    assert.equal(await step(api), 'waiting-for-tip-qualification');
    if (independent) {
      assert.equal(api.state.completedTip.delivery, 'independent-v1');
      assert.equal(api.state.completedTip.validation.source, sha(1));
    }
  }
});

test('a first-member construction conflict isolates only the oldest member without quarantining its peers', async () => {
  const api = new BatchFake();
  api.conflict = true;
  assert.equal(await step(api), 'retiring');
  assert.equal(count(api, 'wake'), 1);
  assert.equal(api.state.active.phase, 'retiring');
  assert.equal(api.state.active.dispatchRecorded, false);
  assert.equal(await step(api), 'retired');
  assert.equal(count(api, 'wake'), 2);
  assert.equal(count(api, 'merge'), 0);
  assert.equal(count(api, 'dispatch'), 0);
  assert.equal(api.state.blocked.length, 0);
  assert.deepEqual(api.state.singletonRetries.map(member => member.number), [1]);
});

test('construction seals a new complete manifest for the compatible prefix, never skipping an older conflict', async () => {
  const api = new BatchFake();
  api.conflicts.add(3);
  assert.equal(await step(api), 'dispatched');
  const active = api.state.active;
  assert.deepEqual(active.members.map(member => member.number), [1, 2]);
  assert.equal(active.prefixes.length, 2);
  assert.equal(active.head, active.prefixes[1].sha);
  assert.equal(active.digest, digest(manifestFor(active)));
  assert.deepEqual(api.state.singletonRetries.map(member => member.number), [3]);
  assert.deepEqual(api.calls.filter(call => call[0] === 'synthetic').map(call => call[1]), [0, 1, 2]);
  const frozen = clone(manifestFor(active));
  await until(api, 'batch-completed');
  assert.equal(count(api, 'merge'), 2);
  assert.deepEqual(api.state.completedTip.members.map(member => member.number), [1, 2]);
  assert.equal(api.state.completedTip.manifest, digest(frozen));
  api.qualified = true;
  api.commits.set(sha(3), { tree: sha(700), parents: [api.base] });
  assert.equal(await step(api), 'dispatched');
  assert.equal(api.state.active.kind, undefined);
  assert.equal(api.state.active.number, 3, 'oldest conflicting member must not be repeatedly skipped');
});

test('every conflict boundary preserves priority order and freezes only the admitted prefix', async () => {
  for (let boundary = 1; boundary < 5; boundary++) {
    const api = new BatchFake();
    api.prs[5].labels = ['queue:priority-high'];
    const order = [6, 1, 2, 3, 4];
    api.conflicts.add(order[boundary]);
    await step(api);
    assert.deepEqual(api.state.active.members.map(member => member.number), order.slice(0, boundary));
    assert.equal(api.state.active.digest, digest(manifestFor(api.state.active)));
    assert.deepEqual(api.state.singletonRetries.map(member => member.number), [order[boundary]]);
    assert.equal(count(api, 'dispatch'), 1);
    assert.equal(count(api, 'merge'), 0);
    assert.equal(count(api, 'synthetic'), boundary + 1);
  }
});

test('a compatible prefix survives restart before its final subset checkpoint without replaying dispatch', async () => {
  const api = new BatchFake();
  api.conflicts.add(3);
  const save = api.save.bind(api);
  let interrupted = false;
  api.save = async state => {
    if (!interrupted && state.active?.phase === 'reserved') {
      interrupted = true;
      throw new Error('subset checkpoint unavailable');
    }
    return save(state);
  };
  await assert.rejects(step(api), /subset checkpoint unavailable/);
  assert.equal(api.state.active.phase, 'constructing');
  assert.equal(count(api, 'dispatch'), 0);
  api.save = save;
  assert.equal(await step(api), 'dispatched');
  assert.deepEqual(api.state.active.members.map(member => member.number), [1, 2]);
  assert.equal(api.state.singletonRetries.filter(member => member.number === 3).length, 1);
  assert.equal(count(api, 'dispatch'), 1);
});

test('running failed or stale batch work is cancelled promptly but keeps its exact lane until all jobs are terminal', async () => {
  for (const change of ['failure', 'duplicate', 'head', 'base', 'enrollment', 'review', 'priority']) {
    const api = new BatchFake();
    await step(api);
    api.workflowRuns[0].status = 'in_progress';
    api.workflowRuns[0].conclusion = null;
    Object.assign(api.workflowJobs.at(-1), { status: 'in_progress', conclusion: null });
    if (change === 'failure') api.workflowJobs[0].conclusion = 'failure';
    if (change === 'duplicate') api.workflowJobs.push({ ...api.workflowJobs[0], id: 999 });
    if (change === 'head') api.prs[0].head.sha = sha(999);
    if (change === 'base') api.base = sha(999);
    if (change === 'enrollment') api.prs[0].enrollment = null;
    if (change === 'review') api.prs[0].review = 'CHANGES_REQUESTED';
    if (change === 'priority') api.prs[0].labels = ['queue:priority-low'];
    assert.equal(await step(api), 'retiring', change);
    assert.equal(count(api, 'cancel'), 1, change);
    assert.equal(api.state.active.ticket, TICKET);
    assert.equal(count(api, 'wake'), 1, 'halt wakeup must survive the prompt cancellation change');
    api.workflowRuns[0].status = 'completed';
    api.workflowRuns[0].conclusion = 'cancelled';
    assert.equal(await step(api), 'retiring');
    assert.equal(count(api, 'merge'), 0);
    assert.equal(count(api, 'dispatch'), 1);
    Object.assign(api.workflowJobs.at(-1), { status: 'completed', conclusion: 'cancelled' });
    assert.equal(await step(api), change === 'duplicate' ? 'retiring' : 'retired');
    if (change === 'duplicate') assert.ok(api.state.active, 'duplicate evidence never proves physical quiescence');
  }
});

test('missing evidence or failed authority reads do not invent a failure or cancel unrelated work', async () => {
  const api = new BatchFake();
  await step(api);
  api.workflowRuns[0].status = 'in_progress';
  api.workflowRuns[0].conclusion = null;
  api.workflowJobs = [];
  assert.equal(await step(api), 'validating');
  assert.equal(count(api, 'cancel'), 0);
  api.reviewState = async () => { throw new Error('authority unavailable'); };
  await assert.rejects(step(api), /authority unavailable/);
  assert.equal(count(api, 'cancel'), 0);
  assert.equal(api.state.active.phase, 'dispatched');
});

test('incomplete live authority metadata is not conclusive staleness and cannot cancel candidate work', async () => {
  for (const change of ['state', 'draft', 'base', 'head', 'repository', 'intent', 'mergeability', 'enrollment', 'review']) {
    const api = new BatchFake();
    await step(api);
    api.workflowRuns[0].status = 'in_progress';
    api.workflowRuns[0].conclusion = null;
    const pr = api.prs[0];
    if (change === 'state') pr.state = undefined;
    if (change === 'draft') pr.draft = undefined;
    if (change === 'base') pr.base.ref = undefined;
    if (change === 'head') pr.head.sha = undefined;
    if (change === 'repository') pr.head.repo = undefined;
    if (change === 'intent') pr.auto_merge = undefined;
    if (change === 'mergeability') pr.mergeable = null;
    if (change === 'enrollment') pr.enrollment = undefined;
    if (change === 'review') pr.review = undefined;
    await assert.rejects(step(api), /unavailable/, change);
    assert.equal(api.state.active.phase, 'dispatched', change);
    assert.equal(count(api, 'cancel'), 0, change);
    assert.equal(count(api, 'merge'), 0, change);
    assert.equal(count(api, 'dispatch'), 1, change);
  }
});

test('cancellation and continuation errors preserve the doomed checkpoint and never release or redispatch its lane', async () => {
  for (const boundary of ['cancel', 'wake']) {
    const api = new BatchFake();
    await step(api);
    api.workflowRuns[0].status = 'in_progress';
    api.workflowRuns[0].conclusion = null;
    api.workflowJobs[0].conclusion = 'failure';
    const original = api[boundary].bind(api);
    api[boundary] = async () => { throw new Error(`${boundary} unavailable`); };
    await assert.rejects(step(api), /unavailable/);
    assert.equal(api.state.active.phase, 'retiring');
    assert.equal(api.state.active.ticket, TICKET);
    api[boundary] = original;
    assert.equal(await step(api), 'retiring');
    assert.equal(count(api, 'dispatch'), 1);
    assert.equal(count(api, 'merge'), 0);
    assert.equal(api.state.singletonRetries.length, 0);
  }
});

test('completed-tip ownership reconciles running work before dispatch and persists the exact first attempt', async () => {
  const api = new BatchFake();
  await until(api, 'batch-completed');
  const owner = { run: 42, source: api.base, attempt: 1 };
  api.qualificationObservation = { owner, qualification: null };
  assert.equal(await step(api), 'waiting-for-tip-qualification');
  assert.deepEqual(api.state.completedTip.qualificationOwner, owner);
  assert.equal(count(api, 'qualification'), 0);
  assert.equal(await step(api), 'waiting-for-tip-qualification');
  assert.equal(count(api, 'qualification'), 0);
  api.qualificationObservation.qualification = { run: 42, source: api.base, job: 43 };
  await step(api);
  assert.equal(api.state.completedTip.qualification.run, 42);
  assert.equal(count(api, 'dispatch'), 2);
});

test('a provisional ordinary push delays dispatch without claiming a qualification owner or consuming dispatch intent', async () => {
  const api = new BatchFake();
  await until(api, 'batch-completed');
  api.qualificationObservation = { owner: { run: 42, source: api.base, attempt: 1 },
    qualification: null, provisional: true };
  assert.equal(await step(api), 'waiting-for-tip-qualification');
  assert.equal(api.state.completedTip.qualificationOwner, undefined);
  assert.equal(api.state.completedTip.qualificationRequested, undefined);
  assert.equal(count(api, 'qualification'), 0);
  api.qualificationObservation = null;
  assert.equal(await step(api), 'waiting-for-tip-qualification');
  assert.equal(count(api, 'qualification'), 1);
});

test('qualification owner state is bounded and compatible with existing unowned receipts and dispatch intents', async () => {
  const api = new BatchFake();
  api.state.completedTip = { sha: BASE, tree: sha(200), ticket: TICKET, qualificationRequested: true };
  assert.doesNotThrow(() => validateState(api.state));
  api.state.completedTip.qualification = { run: 42, source: BASE, job: 43 };
  assert.doesNotThrow(() => validateState(api.state));
  api.state.completedTip.qualificationOwner = { run: 42, source: BASE, attempt: 1 };
  assert.doesNotThrow(() => validateState(api.state));
  for (const owner of [{ run: 0, source: BASE, attempt: 1 }, { run: 42, source: sha(999), attempt: 1 },
    { run: 42, source: BASE, attempt: 2 }, { run: 44, source: BASE, attempt: 1 }]) {
    const state = clone(api.state);
    state.completedTip.qualificationOwner = owner;
    assert.throws(() => validateState(state));
  }
});

test('ambiguous completed-tip dispatch is reconciled to its owner without redispatch across restart', async () => {
  const api = new BatchFake();
  await until(api, 'batch-completed');
  api.dispatchQualification = async tip => {
    api.calls.push(['qualification', tip.sha]);
    api.qualificationObservation = { owner: { run: 42, source: tip.sha, attempt: 1 }, qualification: null };
    throw new Error('ambiguous qualification dispatch');
  };
  await assert.rejects(step(api), /ambiguous qualification dispatch/);
  assert.equal(api.state.completedTip.qualificationRequested, true);
  assert.equal(await step(api), 'waiting-for-tip-qualification');
  assert.equal(api.state.completedTip.qualificationOwner.run, 42);
  assert.equal(count(api, 'qualification'), 1);
});

test('failed terminal validation requests a retirement pass before normal isolation resumes', async () => {
  const api = new BatchFake();
  await step(api);
  api.workflowRuns[0].conclusion = 'failure';
  api.workflowJobs[0].conclusion = 'failure';
  assert.equal(await step(api), 'retiring');
  assert.equal(count(api, 'wake'), 1);
  assert.equal(api.state.active.phase, 'retiring');
  assert.equal(count(api, 'dispatch'), 1);
  assert.equal(count(api, 'merge'), 0);
  assert.equal(await step(api), 'retired');
  assert.equal(count(api, 'wake'), 2);
  assert.equal(api.state.singletonRetries.length, 5);
});

test('a failed retirement continuation retains its checkpoint and later reconciliation recovers without replay', async () => {
  const api = new BatchFake();
  api.conflict = true;
  const wake = api.wake.bind(api);
  api.wake = async () => { throw new Error('continuation unavailable'); };
  await assert.rejects(step(api), /continuation unavailable/);
  assert.equal(api.state.active.phase, 'retiring');
  assert.equal(api.state.active.dispatchRecorded, false);
  assert.equal(count(api, 'synthetic'), 1);
  api.wake = wake;
  assert.equal(await step(api), 'retired');
  assert.equal(api.state.active, null);
  assert.equal(count(api, 'synthetic'), 1);
  assert.equal(count(api, 'dispatch'), 0);
  assert.equal(count(api, 'merge'), 0);
  assert.equal(count(api, 'wake'), 1);
});

test('each missing, duplicate, skipped or nonterminal producer denies proof and retains uncertain work', async () => {
  for (const name of WINDOWS_12_JOBS) {
    for (const kind of ['missing', 'duplicate', 'skipped', 'pending']) {
      const api = new BatchFake();
      await step(api);
      const job = api.workflowJobs.find(job => job.name === name);
      if (kind === 'missing') api.workflowJobs = WINDOWS_12_JOBS.filter(item => item !== name)
        .map((item, index) => ({ id: index + 1, name: item, run_id: 9, run_attempt: 1, head_sha: BASE,
          status: 'completed', conclusion: 'success' }));
      if (kind === 'duplicate') api.workflowJobs.push({ ...job, id: 999 });
      if (kind === 'skipped') job.conclusion = 'skipped';
      if (kind === 'pending') { job.status = 'in_progress'; job.conclusion = null; }
      const result = await step(api);
      assert.equal(result, ['missing', 'pending'].includes(kind) ? 'waiting-for-jobs' : 'retiring', `${name}/${kind}`);
      if (result === 'waiting-for-jobs') await step(api, { now: new Date(NOW.valueOf() + 600_000) });
      await step(api);
      assert.equal(count(api, 'merge'), 0);
      if (kind !== 'skipped') assert.ok(api.state.active, `${name}/${kind} released uncertain jobs`);
    }
  }
});

test('head/enrollment/review withdrawal is re-read before every merge boundary, including the suffix', async () => {
  for (let prefix = 0; prefix < 5; prefix++) {
    for (const change of ['head', 'enrollment', 'review', 'main']) {
      const api = new BatchFake();
      await step(api);
      for (let index = 0; index < prefix; index++) await until(api, 'prefix-confirmed');
      if (change === 'head') api.prs[4].head.sha = sha(999);
      if (change === 'enrollment') api.prs[4].enrollment = null;
      if (change === 'review') api.prs[4].review = 'CHANGES_REQUESTED';
      if (change === 'main') api.base = sha(999);
      await step(api);
      assert.equal(count(api, 'merge'), prefix);
      assert.equal(api.state.active.phase, prefix ? 'recovery-required' : 'retiring');
    }
  }
});

test('ambiguous merge at every original-PR boundary is observed, never blindly retried', async () => {
  for (let boundary = 0; boundary < 5; boundary++) {
    for (const applied of [false, true]) {
      const api = new BatchFake();
      await step(api);
      for (let index = 0; index < boundary; index++) await until(api, 'prefix-confirmed');
      const original = api.merge.bind(api);
      api.merge = async active => {
        if (applied) await original(active);
        else api.calls.push(['merge', clone(active)]);
        throw new Error('Ambiguous transport');
      };
      await assert.rejects(until(api, 'merge-pending'), /Ambiguous transport/);
      api.merge = original;
      assert.equal(await step(api), applied ? boundary === 4 ? 'batch-completed' : 'prefix-confirmed' : 'merge-pending');
      assert.equal(count(api, 'merge'), boundary + 1);
      if (!applied) {
        assert.equal(await step(api, { now: new Date(NOW.valueOf() + 600_000) }), 'recovery-required');
        assert.equal(count(api, 'merge'), boundary + 1);
      }
    }
  }
});

test('authority withdrawal across successful check publication withholds the explicit merge request', async () => {
  for (const change of ['enrollment', 'head', 'reviews', 'main']) {
    const api = new BatchFake();
    const finish = api.finishCheck.bind(api);
    api.finishCheck = async (...args) => {
      await finish(...args);
      if (args[1] !== 'success') return;
      if (change === 'enrollment') api.prs[0].enrollment = null;
      if (change === 'head') api.prs[0].head.sha = sha(999);
      if (change === 'reviews') api.prs[0].review = 'CHANGES_REQUESTED';
      if (change === 'main') api.base = sha(999);
    };
    await until(api, 'recovery-required');
    assert.equal(count(api, 'merge'), 0);
    assert.equal(api.calls.at(-2)[2], 'failure');
  }
});

test('native Auto-merge after attestation is observed without duplicating the protected API call', async () => {
  const api = new BatchFake();
  const finish = api.finishCheck.bind(api);
  api.finishCheck = async (...args) => {
    await finish(...args);
    if (args[1] === 'success') {
      const active = api.state.active;
      const member = active.members.find(member => member.mergeIntent && !member.merged);
      await api.merge({ number: member.number, head: member.mergeIntent.head });
    }
  };
  await until(api, 'batch-completed');
  assert.equal(count(api, 'merge'), 5);
  assert.equal(count(api, 'dispatch'), 1);
});

test('affirmative merge response alone, wrong parent/tree and unexpected main never establish completion', async () => {
  for (const kind of ['unobserved', 'parent', 'tree', 'main']) {
    const api = new BatchFake();
    await step(api);
    const merge = api.merge.bind(api);
    api.merge = async active => {
      if (kind === 'unobserved') return { merged: true };
      const result = await merge(active);
      if (kind === 'parent') api.commits.get(api.base).parents = [sha(999)];
      if (kind === 'tree') api.commits.get(api.base).tree = sha(999);
      if (kind === 'main') api.base = sha(999);
      return result;
    };
    await until(api, 'merge-pending');
    assert.equal(await step(api), kind === 'unobserved' ? 'merge-pending' : 'recovery-required');
    assert.equal(api.state.completedTip, null);
  }
});

test('author changes cannot masquerade as an expected App update', async () => {
  const api = new BatchFake();
  await until(api, 'prefix-confirmed');
  assert.equal(await step(api), 'updating');
  const updated = api.prs[1].head.sha;
  api.commits.get(updated).author = 'author';
  await assert.rejects(step(api), /queue/);
  assert.equal(count(api, 'merge'), 1);
  assert.equal(count(api, 'update'), 1);
});

test('no observable dispatch, duplicate runs, wrong attempts and stale job metadata never grant merge authority', async () => {
  for (const kind of ['absent', 'duplicate', 'attempt', 'source', 'job-source', 'job-identity']) {
    const api = new BatchFake();
    await step(api);
    if (kind === 'absent') api.workflowRuns = [];
    if (kind === 'duplicate') api.workflowRuns.push(clone(api.workflowRuns[0]));
    if (kind === 'attempt') api.workflowRuns[0].run_attempt = 2;
    if (kind === 'source') api.workflowRuns[0].head_sha = sha(999);
    if (kind === 'job-source') api.workflowJobs[0].head_sha = sha(999);
    if (kind === 'job-identity') api.workflowJobs[0].id = api.workflowJobs[1].id;
    if (kind === 'absent') {
      assert.equal(await step(api, { now: new Date(NOW.valueOf() + 600_000) }), 'retiring');
      assert.equal(count(api, 'wake'), 1);
      assert.equal(await step(api), 'retiring');
      assert.equal(count(api, 'wake'), 1, 'uncertain owned work must not create a self-dispatch loop');
    } else await assert.rejects(step(api));
    assert.equal(count(api, 'merge'), 0);
    assert.ok(api.state.active);
  }
});

test('restart at every checkpoint or irreversible call retains the lane and never duplicates merge/update/dispatch', async () => {
  for (const operation of ['save', 'dispatch', 'updateBranch', 'finishCheck', 'merge']) {
    for (const after of [false, true]) {
      const api = new BatchFake();
      if (operation === 'updateBranch') await until(api, 'prefix-confirmed');
      const original = api[operation].bind(api);
      let failed = false;
      api[operation] = async (...args) => {
        if (failed) return original(...args);
        failed = true;
        if (after) await original(...args);
        throw new Error('Injected interruption');
      };
      await assert.rejects(until(api, 'batch-completed'), /Injected interruption/);
      api[operation] = original;
      for (let index = 0; index < 25 && api.state.active; index++) await step(api);
      const merges = api.calls.filter(call => call[0] === 'merge').map(call => call[1].number);
      assert.equal(new Set(merges).size, merges.length, `${operation}/${after}`);
      assert.ok(count(api, 'dispatch') <= 1);
      assert.ok(count(api, 'update') <= 4);
    }
  }
});

test('reviewed migration, rollback and partial repair are checkpointed, authorized and quiescent', async () => {
  const api = new BatchFake();
  api.state = { version: 1, active: null, blocked: [] };
  const options = operation => ({ context: { eventName: 'workflow_dispatch', ref: 'refs/heads/main',
    runId: 123, actor: 'operator' }, inputs: { operation, recovery_checkpoint: api.commit,
    acknowledge_batch_change: true, recovery_ticket: api.state.active?.ticket }, attempt: 1, now: NOW });
  assert.equal(await changeBatchState(api, options('migrate-batch-v1')), 'migrate-batch-v1');
  assert.equal(api.state.version, 2);
  assert.equal(count(api, 'dispatch'), 0);
  await assert.rejects(changeBatchState(api, options('rollback-batch-v1')), /qualified/);
  api.state.completedTip.qualification = { run: 1, source: BASE, job: 2 };
  assert.equal(await changeBatchState(api, options('rollback-batch-v1')), 'rollback-batch-v1');
  await changeBatchState(api, options('migrate-batch-v1'));
  api.state.completedTip = null;
  await until(api, 'prefix-confirmed');
  api.prs[1].enrollment = null;
  assert.equal(await step(api), 'recovery-required');
  api.quiet = false;
  await assert.rejects(changeBatchState(api, options('repair-batch')), /quiescent/);
  api.quiet = true;
  api.administratorAllowed = false;
  await assert.rejects(changeBatchState(api, options('repair-batch')), /administrator/);
  api.administratorAllowed = true;
  const repair = options('repair-batch');
  assert.equal(await changeBatchState(api, repair), 'repair-batch');
  assert.equal(api.state.history[0].batch.members[0].merged, api.base);
  assert.equal(api.state.active, null);
  assert.equal(api.state.completedTip, null);
  await assert.rejects(changeBatchState(api, repair), /checkpoint/);
  await step(api);
  assert.ok(!api.state.active.members.some(member => member.number === 2));
  assert.equal(api.state.active.base, api.base);
});

test('reviewed batch recovery accepts exact terminal preflight rejection but never missing or unknown work', async () => {
  for (const change of ['terminal', 'missing', 'unknown', 'source', 'blob']) {
    const api = new BatchFake();
    api.inventory.blob = 'c75173721c4131cf29d45482ea2edb92934070f1';
    await step(api);
    api.state.active.phase = 'recovery-required';
    api.state.active.reason = 'Reviewed preflight-failure recovery.';
    api.workflowRuns[0].conclusion = 'failure';
    api.workflowJobs = REJECTED_ADMISSION_JOBS.map((name, index) => ({ id: index + 1, name,
      run_id: 9, run_attempt: 1, head_sha: BASE, status: 'completed',
      conclusion: name === 'Admit CI execution' ? 'success' : name === 'Resolve build version' ? 'failure' : 'skipped' }));
    if (change === 'missing') api.workflowJobs.pop();
    if (change === 'unknown') api.workflowJobs.at(-1).conclusion = null;
    if (change === 'source') api.workflowJobs[0].head_sha = sha(999);
    if (change === 'blob') api.inventory.blob = sha(999);
    const options = { context: { eventName: 'workflow_dispatch', ref: 'refs/heads/main',
      runId: 123, actor: 'operator' }, inputs: { operation: 'repair-batch',
      recovery_checkpoint: api.commit, acknowledge_batch_change: true, recovery_ticket: TICKET },
      attempt: 1, now: NOW };
    if (change === 'terminal') {
      assert.equal(await changeBatchState(api, options), 'repair-batch');
      assert.equal(api.state.active, null);
    } else {
      await assert.rejects(changeBatchState(api, options), /missing, duplicate or nonterminal/);
      assert.equal(api.state.active.phase, 'recovery-required');
    }
    assert.equal(count(api, 'merge'), 0);
    assert.equal(count(api, 'dispatch'), 1);
    assert.equal(api.state.completedTip, null);
  }
});

test('every durable checkpoint and side-effect boundary survives before/after interruption without duplicate authority', async () => {
  for (const operation of ['save', 'createCheck', 'finishCheck', 'updateBranch', 'merge', 'dispatch']) {
    const baseline = new BatchFake();
    const baselineOperation = baseline[operation].bind(baseline);
    let total = 0;
    baseline[operation] = async (...args) => { total++; return baselineOperation(...args); };
    await until(baseline, 'batch-completed');
    for (let occurrence = 1; occurrence <= total; occurrence++) {
      for (const after of [false, true]) {
        const api = new BatchFake();
        const original = api[operation].bind(api);
        let calls = 0;
        api[operation] = async (...args) => {
          if (++calls !== occurrence) return original(...args);
          if (after) await original(...args);
          throw new Error('Exact boundary interruption');
        };
        await assert.rejects(until(api, 'batch-completed'), /Exact boundary interruption/);
        api[operation] = original;
        for (let index = 0; index < 40 && api.state.active; index++) await step(api);
        const identities = api.calls.filter(call => call[0] === 'merge').map(call => call[1].number);
        assert.equal(new Set(identities).size, identities.length, `${operation}/${occurrence}/${after}`);
        assert.ok(count(api, 'dispatch') <= 1, `${operation}/${occurrence}/${after}`);
        const updates = api.calls.filter(call => call[0] === 'update').map(call => call[1].number);
        assert.equal(new Set(updates).size, updates.length, `${operation}/${occurrence}/${after}`);
        if (api.state.completedTip) {
          assert.equal(api.state.completedTip.sha, api.base);
          assert.equal(api.state.completedTip.tree, sha(404));
        } else if (!api.state.active) {
          assert.equal(count(api, 'merge'), 0);
        }
      }
    }
  }
});

test('reviewed resume preserves only unchanged authorized suffix and cannot erase a pending ambiguous merge', async () => {
  const api = new BatchFake();
  await until(api, 'prefix-confirmed');
  api.prs[1].review = 'CHANGES_REQUESTED';
  assert.equal(await step(api), 'recovery-required');
  const options = () => ({ context: { eventName: 'workflow_dispatch', ref: 'refs/heads/main',
    runId: 123, actor: 'operator' }, inputs: { operation: 'resume-batch', recovery_checkpoint: api.commit,
    recovery_ticket: TICKET, acknowledge_batch_change: true }, attempt: 1, now: NOW });
  await assert.rejects(changeBatchState(api, options()), /still-authorized/);
  api.prs[1].review = null;
  assert.equal(await changeBatchState(api, options()), 'resume-batch');
  assert.equal(api.state.active.members[0].merged, api.base);
  assert.equal(count(api, 'dispatch'), 1);
  await until(api, 'batch-completed');
  assert.equal(count(api, 'dispatch'), 1);
  assert.equal(count(api, 'merge'), 5);

  const pending = new BatchFake();
  pending.merge = async () => { throw new Error('Uncertain merge'); };
  await assert.rejects(until(pending, 'merge-pending'), /Uncertain/);
  assert.equal(await step(pending, { now: new Date(NOW.valueOf() + 600_000) }), 'recovery-required');
  const blocked = options();
  blocked.inputs.recovery_checkpoint = pending.commit;
  await assert.rejects(changeBatchState(pending, blocked), /unresolved merge/);
});

test('explicit exact-main repair preserves unrelated advances and requires new complete validation', async () => {
  const api = new BatchFake();
  await until(api, 'prefix-confirmed');
  const recorded = api.base;
  api.base = sha(900);
  api.commits.set(api.base, { tree: sha(901), parents: [recorded] });
  assert.equal(await step(api), 'recovery-required');
  const options = { context: { eventName: 'workflow_dispatch', ref: 'refs/heads/main',
    runId: 123, actor: 'operator' }, inputs: { operation: 'repair-batch', recovery_checkpoint: api.commit,
    recovery_ticket: TICKET, acknowledge_batch_change: true }, attempt: 1, now: NOW };
  await assert.rejects(changeBatchState(api, options), /exact-main/);
  options.inputs.recovery_main = sha(999);
  await assert.rejects(changeBatchState(api, options), /exact-main/);
  options.inputs.recovery_main = api.base;
  await changeBatchState(api, options);
  assert.equal(api.state.completedTip, null);
  assert.equal(api.state.history[0].actualMain, sha(900));
  assert.equal(api.state.history[0].batch.members[0].merged, recorded);
  assert.equal(api.base, sha(900));
  await step(api);
  assert.equal(api.state.active.base, sha(900));
  assert.equal(count(api, 'dispatch'), 2);
});

test('late merge visibility records actual prefix but retains explicit recovery before suffix continuation', async () => {
  const api = new BatchFake();
  const merge = api.merge.bind(api);
  api.merge = async () => { throw new Error('Lost merge'); };
  await assert.rejects(until(api, 'merge-pending'), /Lost merge/);
  await step(api, { now: new Date(NOW.valueOf() + 600_000) });
  const member = api.state.active.members[0];
  await merge({ number: member.number, head: member.mergeIntent.head });
  assert.equal(await step(api), 'recovery-required');
  assert.equal(api.state.active.members[0].merged, api.base);
  assert.equal(count(api, 'merge'), 1);
  assert.equal(api.state.completedTip, null);
});

test('frozen manifests include original enrollment/reviews and every precomputed prefix, not runtime receipts', async () => {
  const api = new BatchFake();
  await step(api);
  const active = api.state.active;
  assert.equal(active.digest, digest(manifestFor(active)));
  active.members[0].check = 123;
  assert.equal(active.digest, digest(manifestFor(active)));
});
