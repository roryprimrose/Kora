const { test } = require('node:test');
const assert = require('node:assert/strict');
const { reconcile } = require('./controller.cjs');
const { validateState, WINDOWS_12_JOBS, GATE, title } = require('./policy.cjs');
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
  async syntheticPrefix(active, index) {
    this.calls.push(['synthetic', index]);
    if (this.conflict) return null;
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
    this.workflowJobs = WINDOWS_12_JOBS.map((name, index) => ({ id: index + 1, name,
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
  async tipQualification() { return this.qualified ? { run: 10, job: 11, source: this.base } : null; }
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
  const api = new BatchFake();
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
});

test('synthetic conflicts integrate nothing and retire without falsely quarantining individual members', async () => {
  const api = new BatchFake();
  api.conflict = true;
  assert.equal(await step(api), 'retiring');
  assert.equal(await step(api), 'retired');
  assert.equal(count(api, 'merge'), 0);
  assert.equal(count(api, 'dispatch'), 0);
  assert.equal(api.state.blocked.length, 0);
  assert.equal(api.state.singletonRetries.length, 5);
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
      assert.equal(api.state.active.phase, prefix || change === 'main' ? 'recovery-required' : 'retiring');
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
      assert.equal(await step(api), 'retiring');
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
