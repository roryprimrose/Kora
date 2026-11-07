'use strict';

const assert = require('node:assert/strict');
const { test } = require('node:test');
const { createState, applyEvent } = require('./registry.cjs');
const { workerLaunch } = require('./worker-launch.cjs');

const head = '1'.repeat(40);
const settings = {
  repository: 'roryprimrose/Kora', publicationAuthorized: true,
  controlWorkspace: 'Q:\\Repos\\Kora.worktrees\\control',
  productionWorkspace: 'Q:\\Repos\\Kora',
  scratch: 'Q:\\Repos\\Kora.worktrees\\control\\.scratch\\run'
};

function fixture(pool = 'ready') {
  let state = createState('agent-host-session://coordinator?chat=exact', head, 'delivery');
  state = applyEvent(state, { id: 'planner', type: 'planner', owner: 'planner' }).state;
  const result = applyEvent(state, { id: 'feature', type: 'candidate', owner: 'planner', pass: 1,
    key: 'one', outcome: 'one', pool, kind: pool === 'repair' ? 'repair' : 'feature',
    pr: pool === 'repair' ? 42 : null, head, scopes: ['src\\One.cs'],
    evidence: 'Approved complete operation with source and failure evidence.' });
  return { state: result.state, action: result.actions[0] };
}

test('reserved worker launch is asynchronous, isolated and addresses the exact coordinator', () => {
  const f = fixture();
  const launch = workerLaunch(f.state, f.action, settings);
  assert.equal(launch.relationship, 'currentSession');
  assert.equal(launch.worktree, true);
  assert.equal(launch.workspace, settings.productionWorkspace);
  assert.equal(launch.model, 'gpt-6.1-sol');
  assert.match(launch.prompt, /agent-host-session:\/\/coordinator\?chat=exact/);
  assert.match(launch.prompt, /src\\one.cs/);
  assert.ok(launch.prompt.includes(`${settings.controlWorkspace}\\.github\\skills\\kora-orchestrate\\worker.md`));
  assert.match(launch.prompt, /effective effort UNVERIFIED/);
  assert.match(launch.prompt, /CLEANUP_BLOCKED/);
  assert.equal(Object.hasOwn(launch, 'autopilot'), false);
});

test('concurrent disjoint reservations create distinct bounded requests without shared writes', () => {
  const first = fixture();
  const result = applyEvent(first.state, { id: 'two', type: 'candidate', owner: 'planner', pass: 1,
    key: 'two', outcome: 'two', pool: 'ready', kind: 'feature', head,
    scopes: ['src\\Two.cs'], evidence: 'Second admitted independent complete operation.' });
  const one = workerLaunch(result.state, first.action, settings);
  const two = workerLaunch(result.state, result.actions[0], settings);
  assert.notEqual(one.title, two.title);
  assert.notEqual(one.prompt, two.prompt);
  assert.equal(one.worktree && two.worktree, true);
  assert.equal(result.state.items.filter(item => item.phase === 'reserved').length, 2);
});

test('repair launch preserves one existing PR and expected publisher', () => {
  const f = fixture('repair');
  const launch = workerLaunch(f.state, f.action, { ...settings, expectedPublisher: 'verified-principal' });
  assert.match(launch.prompt, /Repair exactly existing PR 42/);
  assert.match(launch.prompt, /expected repository gh publisher=verified-principal/);
});

test('missing publication authority, unsafe paths, or control-workspace use fail closed', () => {
  const f = fixture();
  for (const replacement of [
    { publicationAuthorized: false }, { repository: 'other/repo' },
    { productionWorkspace: settings.controlWorkspace },
    { scratch: 'Q:\\Elsewhere\\.scratch\\run' }, { scratch: 'relative' },
    { controlWorkspace: 'Q:\\Repos\\..\\Control' }, { expectedPublisher: '' }
  ]) {
    assert.throws(() => workerLaunch(f.state, f.action, { ...settings, ...replacement }));
  }
});

test('already-attached, blocked, stale or observation-only workers cannot relaunch', () => {
  const f = fixture();
  const attached = applyEvent(f.state, { id: 'attached', type: 'attached', key: 'one', owner: 'worker' }).state;
  assert.throws(() => workerLaunch(attached, f.action, settings), /reservation/);
  const blocked = applyEvent(f.state, { id: 'blocked', type: 'launch-blocked',
    key: 'one', reason: 'Unknown host launch result.' }).state;
  assert.throws(() => workerLaunch(blocked, f.action, settings), /reservation/);
  const stale = applyEvent(f.state, { id: 'refresh', type: 'refresh', head: '2'.repeat(40) }).state;
  assert.throws(() => workerLaunch(stale, f.action, settings), /reservation/);
  assert.throws(() => workerLaunch({ ...f.state, mode: 'observe' }, f.action, settings), /authority/);
});
