const { test } = require('node:test');
const assert = require('node:assert/strict');
const { bootstrap, BRANCH, CHECKPOINT, RUN, EXPECTED } = require('./bootstrap-retirement.cjs');
const { REJECTED_ADMISSION_JOBS, terminalRejectedAdmission, title } = require('./policy.cjs');

const SOURCE = 'f'.repeat(40);
const COMMITTED = 'e'.repeat(40);
const clone = value => structuredClone(value);
const options = () => ({ context: { repo: { owner: 'roryprimrose', repo: 'Kora' },
  actor: 'administrator', eventName: 'workflow_dispatch', ref: `refs/heads/${BRANCH}`,
  sha: SOURCE, runId: 123 }, inputs: { acknowledge: true, lock_ruleset: '456' }, attempt: 1 });
function fixture() {
  const caller = { id: 123, run_attempt: 1, event: 'workflow_dispatch', head_branch: BRANCH,
    head_sha: SOURCE, path: '.github/workflows/merge-queue.yml', status: 'in_progress',
    repository: { full_name: 'roryprimrose/Kora' }, head_repository: { full_name: 'roryprimrose/Kora' },
    actor: { login: 'administrator' } };
  const run = { ...caller, id: RUN, head_branch: 'main', head_sha: EXPECTED.active.base,
    path: '.github/workflows/ci.yml', status: 'completed', conclusion: 'failure',
    workflow_id: 373584582, display_title: title(EXPECTED.active) };
  const api = {
    repository: 'roryprimrose/Kora', repo: options().context.repo, appId: 99, commit: CHECKPOINT,
    state: clone(EXPECTED), caller, run, listed: [clone(run)], source: SOURCE,
    base: EXPECTED.active.base, admin: true, blob: '97e22a8f88bcdc9bbaef32cf9e7200dd6e5c43cd',
    ruleset: { id: 456, target: 'branch', enforcement: 'active', bypass_actors: [],
      conditions: { ref_name: { exclude: [], include: [`refs/heads/${BRANCH}`] } },
      rules: ['creation', 'update', 'deletion'].map(type => ({ type })) },
    policies: { total_count: 2, branch_policies: ['main', BRANCH].map(name => ({ name, type: 'branch' })) },
    jobList: REJECTED_ADMISSION_JOBS.map((name, index) => ({ id: index + 1, name,
      run_id: RUN, run_attempt: 1, head_sha: EXPECTED.active.base, status: 'completed',
      conclusion: name === 'Admit CI execution' ? 'failure' : 'skipped' })),
    saves: [], wakes: 0, loads: 0, hook() {},
    async verifyProtection() { this.hook('protection'); },
    async administrator() { this.hook('administrator'); return this.admin; },
    async main() { return this.base; },
    async runs() { return clone(this.listed); },
    async jobs() { return clone(this.jobList); },
    async rejectedAdmission(source, value, jobs) {
      return source === value.head_sha && terminalRejectedAdmission(value, jobs, this.blob);
    },
    async load() { this.loads++; this.hook('load'); return clone(this.state); },
    async save(state, message) {
      this.saves.push({ state: clone(state), message }); this.state = clone(state); this.commit = COMMITTED;
      this.hook('save');
    },
    async wake() { this.wakes++; },
    info() {},
  };
  api.github = {
    rest: {
      git: { async getRef() { return { data: { object: { sha: api.source } } }; } },
      actions: { async getWorkflowRun({ run_id }) { return { data: clone(run_id === RUN ? api.run : api.caller) }; } },
      checks: { async get({ check_run_id }) {
        return { data: { id: check_run_id, name: Object.entries(EXPECTED.active.checks)
          .find(([, id]) => id === check_run_id)[0], app: { id: api.appId },
        head_sha: EXPECTED.active.head, status: 'completed', conclusion: 'failure', ...api.checkChange } };
      } },
    },
    async request(route) { return { data: clone(route.includes('rulesets') ? api.ruleset : api.policies) }; },
  };
  return api;
}

test('one-time App bootstrap clears only active and preserves every failed check and blocked revision', async () => {
  const api = fixture();
  assert.equal(await bootstrap(api, options()), 'recovered');
  assert.deepEqual(api.state, { ...EXPECTED, active: null });
  assert.equal(api.saves.length, 1);
  assert.equal(api.wakes, 1);
  assert.match(api.saves[0].message, new RegExp(CHECKPOINT));
  assert.match(api.saves[0].message, new RegExp(String(RUN)));
  assert.match(api.saves[0].message, new RegExp(SOURCE));
  await assert.rejects(bootstrap(api, options()), /checkpoint or state changed/);
  assert.equal(api.saves.length, 1);
});

for (const [name, change] of Object.entries({
  'checkpoint drift': api => { api.commit = COMMITTED; },
  'changed blocked entries': api => { api.state.blocked.pop(); },
  'merge uncertainty': api => { api.state.active.mergeStarted = '2026-11-02T18:00:00.000Z'; },
  'main movement': api => { api.base = COMMITTED; },
  'administrator revoked': api => { api.admin = false; },
  'branch movement': api => { api.source = COMMITTED; },
  'lock disabled': api => { api.ruleset.enforcement = 'disabled'; },
  'lock bypass': api => { api.ruleset.bypass_actors.push({ actor_type: 'Integration', actor_id: 99 }); },
  'incomplete lock': api => { api.ruleset.rules.pop(); },
  'wildcard lock': api => { api.ruleset.conditions.ref_name.include = ['refs/heads/agents/*']; },
  'additional environment branch': api => { api.policies.total_count = 3; },
  'wrong caller source': api => { api.caller.head_sha = COMMITTED; },
  'retried caller': api => { api.caller.run_attempt = 2; },
  'duplicate ticket': api => { api.listed.push(clone(api.run)); },
  'missing ticket': api => { api.listed = []; },
  'retried CI run': api => { api.run.run_attempt = 2; },
  'running CI': api => { api.run.status = 'in_progress'; },
  'changed CI source': api => { api.run.head_sha = COMMITTED; },
  'unknown workflow blob': api => { api.blob = COMMITTED; },
  'missing job': api => { api.jobList.pop(); },
  'nonterminal job': api => { api.jobList[0].status = 'in_progress'; },
  'candidate execution': api => { api.jobList[2].conclusion = 'success'; },
  'modified failed check': api => { api.checkChange = { conclusion: 'success' }; },
  'foreign check publisher': api => { api.checkChange = { app: { id: 100 } }; },
  'foreign repository': api => { api.repository = 'other/repository'; },
})) {
  test(`bootstrap rejects ${name} without writes or wakeups`, async () => {
    const api = fixture(); change(api);
    await assert.rejects(bootstrap(api, options()));
    assert.equal(api.saves.length, 0);
    assert.equal(api.wakes, 0);
  });
}

test('fresh boundary observes authority, jobs and checkpoint changes on the second pass', async () => {
  for (const change of [
    api => { api.admin = false; },
    api => { api.jobList[0].status = 'in_progress'; },
    api => { api.commit = COMMITTED; },
  ]) {
    const api = fixture();
    api.hook = event => { if (event === 'load' && api.loads === 1) change(api); };
    await assert.rejects(bootstrap(api, options()));
    assert.equal(api.saves.length, 0);
    assert.equal(api.wakes, 0);
  }
});

test('ambiguous writes are not retried and never dispatch a candidate or controller', async () => {
  const api = fixture();
  api.hook = event => { if (event === 'save') throw new Error('Ambiguous transport after write'); };
  await assert.rejects(bootstrap(api, options()), /Ambiguous transport/);
  assert.equal(api.saves.length, 1);
  assert.equal(api.wakes, 0);
  await assert.rejects(bootstrap(api, options()), /checkpoint or state changed/);
  assert.equal(api.saves.length, 1);
});

test('unknown acknowledgement, event, branch, attempt, repository and ruleset fail before writes', async () => {
  for (const change of [
    input => { input.inputs.acknowledge = false; },
    input => { input.inputs.acknowledge = 'true'; },
    input => { input.inputs.lock_ruleset = '456x'; },
    input => { input.attempt = 2; },
    input => { input.context.eventName = 'push'; },
    input => { input.context.ref = 'refs/heads/main'; },
  ]) {
    const api = fixture(); const input = options(); change(input);
    await assert.rejects(bootstrap(api, input));
    assert.equal(api.saves.length, 0);
  }
});
