const { test } = require('node:test');
const assert = require('node:assert/strict');
const { recover, changeMainBuildState } = require('./recovery.cjs');
const { controllerOperation, validateState, title, DISPATCH_OBSERVATION_MS } = require('./policy.cjs');

const CHECKPOINT = 'a'.repeat(40);
const NEXT = 'b'.repeat(40);
const TICKET = '01234567-89ab-cdef-0123-456789abcdef';
const NOW = new Date('2026-01-01T12:00:00Z');
const options = () => ({
  context: { eventName: 'workflow_dispatch', ref: 'refs/heads/main', actor: 'operator', runId: 123 },
  attempt: 1, now: NOW,
  inputs: { operation: 'recover-unobservable-dispatch', recovery_checkpoint: CHECKPOINT,
    recovery_ticket: TICKET, acknowledge_uncertain_dispatch: true },
});

class Fake {
  commit = CHECKPOINT;
  state = { version: 1, blocked: [{ number: 2, head: NEXT, enrollment: NOW.toISOString(), reason: 'Existing failure.' }],
    active: { number: 1, head: NEXT, base: CHECKPOINT, ticket: TICKET, phase: 'retiring', dispatchRecorded: true,
      created: new Date(NOW - DISPATCH_OBSERVATION_MS).toISOString(), enrollment: NOW.toISOString(), checks: {} } };
  calls = [];
  async verifyProtection() { this.calls.push(['protection']); }
  async administrator(actor) { this.calls.push(['administrator', actor]); return true; }
  async load() { return structuredClone(this.state); }
  async runs() { this.calls.push(['runs']); return []; }
  async save(state, message) {
    validateState(state);
    this.state = structuredClone(state);
    this.commit = NEXT;
    this.calls.push(['save', structuredClone(state), message]);
  }
  async wake() { this.calls.push(['wake']); }
  info(message) { this.calls.push(['info', message]); }
}

const observable = (active, overrides = {}) => ({ id: 9, display_title: title(active),
  event: 'workflow_dispatch', head_branch: 'main', status: 'completed', ...overrides });
const writes = api => api.calls.filter(call => ['save', 'wake'].includes(call[0]));

class MainSchedulingFake extends Fake {
  independent = true;
  mainBuildProfile = 'per-merge';
  state = { version: 2, profile: 'batch-v1', active: null, blocked: [], singletonRetries: [],
    deliveryProfile: 'independent-v1', completedTip: { sha: CHECKPOINT, tree: 'c'.repeat(40),
      ticket: TICKET, delivery: 'independent-v1', base: NEXT, combined: 'd'.repeat(40),
      validation: { run: 7, attempt: 1, source: 'd'.repeat(40), workflow: NEXT,
        inventory: 'e'.repeat(40), schema: 'parallel-pr-v1' } } };
  saves = 0;
  async main() { return CHECKPOINT; }
  async releaseSource(source) { return { source, tree: this.state.completedTip.tree }; }
  async supportsHourlyMain() { return true; }
  async commitInfo(source) { return { sha: source, tree: this.state.completedTip.tree }; }
  async ciQuiescent() { return true; }
  async tipQualification(tip, options) {
    assert.equal(options.allowScheduled, true);
    return { owner: { run: 31, source: tip.sha, attempt: 1 },
      qualification: { run: 31, job: 32, source: tip.sha }, terminal: true, provisional: false };
  }
  async save(state, message) {
    validateState(state);
    this.state = structuredClone(state);
    this.commit = (++this.saves + 20).toString(16).padStart(40, '0');
    this.calls.push(['save', structuredClone(state), message]);
  }
}

const mainOptions = (api, operation, target = 'hourly-v1') => ({
  context: { eventName: 'workflow_dispatch', ref: 'refs/heads/main', actor: 'operator', runId: 123 },
  profile: 'batch-v1', attempt: 1, now: NOW,
  inputs: { operation, main_build_profile: target, recovery_checkpoint: api.commit,
    ...(operation === 'hold-main-builds' ? {} : { recovery_ticket: api.state.mainSchedulingTransition.ticket }),
    acknowledge_batch_change: true },
});

test('hourly main activation and rollback require a guarded qualified drained boundary', async () => {
  const api = new MainSchedulingFake();
  for (const target of ['hourly-v1', 'per-merge']) {
    assert.equal(await changeMainBuildState(api, mainOptions(api, 'hold-main-builds', target)), 'main-builds-draining');
    assert.equal(await changeMainBuildState(api, mainOptions(api, 'prepare-main-builds', target)), 'main-builds-ready');
    api.mainBuildProfile = target;
    assert.equal(await changeMainBuildState(api, mainOptions(api, 'resume-main-builds', target)), 'main-builds-resumed');
    assert.equal(api.state.mainBuildProfile ?? 'per-merge', target);
    if (target === 'hourly-v1') {
      assert.equal(api.state.hourlyMain.baseline.source, CHECKPOINT);
      assert.equal(api.state.hourlyMain.baseline.qualification.run, 31);
    } else assert.equal(api.state.hourlyMain, undefined);
  }
  assert.equal(api.calls.filter(call => call[0] === 'save').length, 6);
  assert.ok(api.calls.filter(call => call[0] === 'save').every(call => call[2].includes('Administrator: operator')));
});

test('main scheduling input, administrator and configuration failures never write a hold', async () => {
  for (const mutate of [
    (api, value) => { value.attempt = 2; },
    (api, value) => { value.context.eventName = 'pull_request_target'; },
    (api, value) => { value.context.ref = 'refs/heads/topic'; },
    (api, value) => { value.inputs.acknowledge_batch_change = false; },
    (api, value) => { value.inputs.main_build_profile = 'unknown'; },
    (api, value) => { value.inputs.recovery_main = NEXT; },
    api => { api.independent = false; },
    api => { api.mainBuildProfile = 'hourly-v1'; },
    api => { api.administrator = async () => false; },
    api => { api.administrator = async () => { throw new Error('HTTP 403'); }; },
  ]) {
    const api = new MainSchedulingFake();
    const value = mainOptions(api, 'hold-main-builds');
    mutate(api, value);
    await assert.rejects(changeMainBuildState(api, value));
    assert.deepEqual(writes(api), []);
  }
});

test('failed, skipped, pending, wrong-tree and nonquiescent main cannot prepare hourly activation', async () => {
  for (const failure of ['failed', 'skipped', 'pending', 'tree', 'record', 'workflow', 'quiescence', 'checkpoint']) {
    const api = new MainSchedulingFake();
    await changeMainBuildState(api, mainOptions(api, 'hold-main-builds'));
    const value = mainOptions(api, 'prepare-main-builds');
    if (failure === 'failed' || failure === 'skipped')
      api.tipQualification = async () => ({ terminal: true, provisional: false, qualification: null });
    if (failure === 'pending') api.tipQualification = async () => ({ terminal: false, provisional: true });
    if (failure === 'tree') api.commitInfo = async () => ({ tree: NEXT });
    if (failure === 'record') api.releaseSource = async () => ({ source: NEXT, tree: NEXT });
    if (failure === 'workflow') api.supportsHourlyMain = async () => false;
    if (failure === 'quiescence') api.ciQuiescent = async () => false;
    if (failure === 'checkpoint') api.commit = NEXT;
    await assert.rejects(changeMainBuildState(api, value));
    assert.equal(writes(api).length, 1);
    assert.equal(api.state.mainSchedulingTransition.phase, 'draining');
  }
});

test('main scheduling hold ownership, stale source and late authority are revalidated before mutation', async () => {
  const api = new MainSchedulingFake();
  await changeMainBuildState(api, mainOptions(api, 'hold-main-builds'));
  const wrongOwner = mainOptions(api, 'prepare-main-builds');
  wrongOwner.context.actor = 'other-admin';
  await assert.rejects(changeMainBuildState(api, wrongOwner));
  await changeMainBuildState(api, mainOptions(api, 'prepare-main-builds'));
  api.mainBuildProfile = 'hourly-v1';
  api.main = async () => NEXT;
  await assert.rejects(changeMainBuildState(api, mainOptions(api, 'resume-main-builds')));
  assert.equal(api.state.mainBuildProfile, undefined);
  api.main = async () => CHECKPOINT;
  api.mainBuildProfile = 'per-merge';
  let checks = 0;
  api.administrator = async () => ++checks === 1;
  await assert.rejects(changeMainBuildState(api, mainOptions(api, 'abort-main-builds')));
  assert.equal(api.state.mainSchedulingTransition.phase, 'ready');
  api.administrator = async () => true;
  assert.equal(await changeMainBuildState(api, mainOptions(api, 'abort-main-builds')), 'main-builds-aborted');
  assert.equal(api.state.mainBuildProfile, undefined);
});

test('ordinary wakeups cannot accidentally request recovery and unknown or mixed operations fail explicitly', () => {
  for (const inputs of [{}, { operation: 'reconcile' }, { operation: 'reconcile',
    recovery_checkpoint: '', recovery_ticket: '', acknowledge_uncertain_dispatch: false }]) {
    assert.equal(controllerOperation(inputs), 'reconcile');
  }
  assert.equal(controllerOperation(options().inputs), 'recover-unobservable-dispatch');
  for (const inputs of [null, [], { operation: 'unknown' }, { recovery_checkpoint: CHECKPOINT },
    { recovery_ticket: TICKET }, { acknowledge_uncertain_dispatch: true },
    { acknowledge_uncertain_dispatch: 'true' }]) {
    assert.throws(() => controllerOperation(inputs));
  }
});

test('reviewed recovery preserves failures, records the administrator and exact receipt, and only wakes ordinary reconciliation', async () => {
  const api = new Fake();
  const before = structuredClone(api.state);
  assert.equal(await recover(api, options()), 'recovered');
  assert.equal(api.state.active, null);
  assert.deepEqual(api.state.blocked[0], before.blocked[0]);
  assert.equal(api.state.blocked[1].head, before.active.head);
  assert.equal(api.state.blocked[1].enrollment, before.active.enrollment);
  assert.match(api.state.blocked[1].reason, new RegExp(TICKET));
  assert.match(api.calls.find(call => call[0] === 'save')[2],
    new RegExp(`Administrator: operator\\nWorkflow run: 123\\nTicket: ${TICKET}\\nPrevious checkpoint: ${CHECKPOINT}`));
  assert.equal(api.calls.filter(call => call[0] === 'runs').length, 2);
  assert.equal(api.calls.filter(call => call[0] === 'administrator').length, 2);
  assert.equal(api.calls.at(-1)[0], 'wake');
  assert.deepEqual(writes(api).map(call => call[0]), ['save', 'wake']);
});

test('recovery requires exact acknowledgement, identity, trusted manual invocation and a fresh first attempt', async () => {
  for (const change of [
    value => { value.inputs.operation = 'reconcile'; },
    value => { value.inputs.acknowledge_uncertain_dispatch = false; },
    value => { delete value.inputs.acknowledge_uncertain_dispatch; },
    value => { value.inputs.recovery_checkpoint = 'invalid'; },
    value => { value.inputs.recovery_ticket = 'invalid'; },
    value => { value.context.eventName = 'pull_request_target'; },
    value => { value.context.ref = 'refs/heads/topic'; },
    value => { value.context.runId = 0; },
    value => { value.attempt = 2; },
    value => { value.attempt = undefined; },
  ]) {
    const api = new Fake();
    const value = options();
    change(value);
    await assert.rejects(recover(api, value));
    assert.deepEqual(writes(api), []);
  }
});

test('nonadministrator, revoked authority and failed live authority or protection reads never repair state', async () => {
  for (const scenario of ['denied', 'revoked', 'error', 'protection']) {
    const api = new Fake();
    let checks = 0;
    api.administrator = async () => {
      if (scenario === 'error') throw new Error('Authority HTTP 403');
      return scenario !== 'denied' && (scenario !== 'revoked' || ++checks === 1);
    };
    if (scenario === 'protection') api.verifyProtection = async () => { throw new Error('Protection changed'); };
    await assert.rejects(recover(api, options()), /administrator|authority|Authority|Protection/);
    assert.deepEqual(writes(api), []);
  }
});

test('changed checkpoints, other phases, merge uncertainty and unelapsed budgets cannot be recovered', async () => {
  for (const change of [
    api => { api.commit = NEXT; },
    api => { api.state.active = null; },
    api => { api.state.active.ticket = 'ffffffff-ffff-ffff-ffff-ffffffffffff'; },
    api => { api.state.active.phase = 'dispatched'; },
    api => { api.state.active.phase = 'reserved'; api.state.active.dispatchRecorded = false; },
    api => { api.state.active.mergeStarted = NOW.toISOString(); },
    api => { api.state.active.created = new Date(NOW - DISPATCH_OBSERVATION_MS + 1).toISOString(); },
  ]) {
    const api = new Fake();
    change(api);
    await assert.rejects(recover(api, options()));
    assert.deepEqual(writes(api), []);
  }
  const api = new Fake();
  await assert.rejects(recover(api, { ...options(), now: new Date('invalid') }), /budget/);
  assert.deepEqual(writes(api), []);
});

test('observable, nonterminal, duplicate, malformed and newly appearing runs forbid recovery', async () => {
  for (const scenario of ['completed', 'running', 'unrelated-running', 'duplicate', 'invalid', 'late', 'error']) {
    const api = new Fake();
    let reads = 0;
    api.runs = async () => {
      if (scenario === 'error') throw new Error('Run listing HTTP 500');
      if (scenario === 'late' && ++reads === 1) return [];
      if (scenario === 'invalid') return [{ id: 'unknown' }];
      const run = observable(api.state.active);
      if (scenario === 'running') run.status = 'in_progress';
      if (scenario === 'unrelated-running') Object.assign(run, { display_title: 'Other dispatch', status: 'queued' });
      if (scenario === 'duplicate') {
        run.display_title = 'Other dispatch';
        return [run, run];
      }
      return [run];
    };
    await assert.rejects(recover(api, options()), /run|work|Run|observations/);
    assert.deepEqual(writes(api), []);
  }
  const api = new Fake();
  api.runs = async () => [observable(api.state.active, { display_title: 'Unrelated completed dispatch' })];
  assert.equal(await recover(api, options()), 'recovered');
});

test('an existing quarantine is retained without duplication and a full blocked table fails before commit', async () => {
  const api = new Fake();
  const active = api.state.active;
  const blocked = { number: active.number, head: active.head, enrollment: active.enrollment, reason: 'Original failure.' };
  api.state.blocked.push(blocked);
  await recover(api, options());
  assert.equal(api.state.blocked.length, 2);
  assert.deepEqual(api.state.blocked[1], blocked);
  const full = new Fake();
  full.state.blocked = Array.from({ length: 1000 }, () => structuredClone(full.state.blocked[0]));
  await assert.rejects(recover(full, options()), /limit/);
  assert.deepEqual(writes(full), []);
});

test('save and readback uncertainty stops before waking and does not claim successful recovery', async () => {
  for (const scenario of ['conflict', 'readback', 'commit-readback']) {
    const api = new Fake();
    if (scenario === 'conflict') api.save = async () => { throw new Error('State changed outside the controller'); };
    else {
      const load = api.load.bind(api);
      api.load = async () => {
        const state = await load();
        if (api.commit === NEXT) {
          if (scenario === 'readback') state.blocked = [];
          else api.commit = CHECKPOINT;
        }
        return state;
      };
    }
    await assert.rejects(recover(api, options()), /outside|readback/);
    assert.equal(api.calls.some(call => call[0] === 'wake'), false);
  }
});

test('a failed continuation preserves the recovery checkpoint and old inputs cannot replay the operation', async () => {
  const api = new Fake();
  api.wake = async () => { throw new Error('Wakeup response lost'); };
  await assert.rejects(recover(api, options()), /Wakeup/);
  assert.equal(api.state.active, null);
  assert.equal(api.calls.filter(call => call[0] === 'save').length, 1);
  await assert.rejects(recover(api, options()), /checkpoint changed/);
  assert.equal(api.calls.filter(call => call[0] === 'save').length, 1);
});
