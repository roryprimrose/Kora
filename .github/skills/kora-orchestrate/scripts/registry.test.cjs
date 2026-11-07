'use strict';

const assert = require('node:assert/strict');
const { test } = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const { createState, applyEvent, validate, digest } = require('./registry.cjs');
const { refinementFixture, refinementEvent, rebind, tamperedStates } = require('./refinement-fixture.cjs');

const sha = number => number.toString(16).padStart(40, '0');
const coordinator = 'agent-host-session://coordinator';
const planner = 'agent-host-session://coordinator?chat=planner';
const owner = key => `agent-host-session://coordinator?chat=${key}`;

function fixture() {
  let state = createState(coordinator, sha(1), 'delivery');
  let sequence = 0;
  const send = event => {
    const result = applyEvent(state, { id: `event-${++sequence}`,
      ...(['candidate', 'pass-ended'].includes(event.type) ? { pass: state.planner.pass } : {}),
      ...event });
    state = result.state;
    return result;
  };
  send({ type: 'planner', owner: planner });
  const offer = (key, pool = 'ready', extra = {}) => send({
    type: 'candidate', owner: planner, key, outcome: key, pool,
    kind: pool === 'repair' ? 'repair' : 'feature', pr: pool === 'repair' ? sequence + 100 : null,
    head: state.head, scopes: [`src\\${key}.cs`], evidence: 'Admitted complete operation; unowned source.',
    ...extra
  });
  const attach = key => send({ type: 'attached', key, owner: owner(key) });
  const publish = (key, extra = {}) => send({
    type: 'published', key, owner: owner(key), proof: {
      pushedHead: sha(2), remoteHead: sha(2), pr: 500,
      ready: true, enrolled: true, ...extra
    }
  });
  const complete = (key, extra = {}) => send({
    type: 'completed', key, owner: owner(key), proof: {
      remoteHead: sha(2), stoppedWriting: true, worktreeAbsent: true,
      branchAbsent: true, pendingInstructions: 0, ...extra
    }
  });
  return { send, offer, attach, publish, complete, state: () => structuredClone(state) };
}

test('bootstrap requests one bounded pass examining both pools', () => {
  const result = applyEvent(createState(coordinator, sha(1)),
    { id: 'planner', type: 'planner', owner: planner });
  assert.deepEqual(result.actions, [{
    type: 'discover', owner: planner, capacity: { repair: 4, ready: 10 },
    examine: ['ready', 'repair'], head: sha(1), pass: 1
  }]);
});

test('unknown source can reserve discovery immediately but cannot admit or launch workers', () => {
  const initial = createState(coordinator, null);
  const attached = applyEvent(initial, { id: 'planner', type: 'planner', owner: planner });
  assert.equal(attached.actions[0].type, 'discover');
  assert.equal(attached.actions[0].head, null);
  assert.equal(attached.state.planner.pass, 1);
  assert.throws(() => applyEvent(attached.state, {
    id: 'candidate', type: 'candidate', owner: planner, pass: 1,
    key: 'feature', outcome: 'feature', pool: 'ready', kind: 'feature',
    head: sha(1), scopes: ['src\\Feature.cs'], evidence: 'Not yet refreshed.'
  }), /source refresh/);
  const refreshed = applyEvent(attached.state, { id: 'source', type: 'refresh', head: sha(1) });
  assert.equal(refreshed.state.head, sha(1));
  assert.ok(refreshed.actions.every(action => action.type !== 'launch'));
  assert.throws(() => createState(coordinator, undefined), /Malformed/);
});

test('blocked repair capacity cannot starve the independent ready pool', () => {
  const f = fixture();
  for (let index = 0; index < 4; index++) {
    const key = `repair-${index}`;
    f.offer(key, 'repair');
    f.attach(key);
    f.send({ type: 'blocked', key, owner: owner(key), reason: 'Owned conflict requires repair.' });
  }
  assert.deepEqual(f.offer('repair-wait', 'repair').actions, []);
  for (let index = 0; index < 10; index++) {
    assert.ok(f.offer(`feature-${index}`).actions.some(action => action.type === 'launch'));
  }
  assert.deepEqual(f.offer('feature-wait').actions, []);
  assert.equal(f.state().items.filter(item => item.phase === 'reserved').length, 10);
});

test('publication keeps reservation; cleanup releases, archives, and refills feature before maintenance', () => {
  const f = fixture();
  for (let index = 0; index < 10; index++) f.offer(`running-${index}`);
  f.attach('running-0');
  f.offer('maintenance', 'ready', { kind: 'maintenance' });
  f.offer('feature');
  f.publish('running-0');
  assert.equal(f.state().items.find(item => item.key === 'feature').phase, 'candidate');
  const result = f.complete('running-0');
  assert.deepEqual(result.actions.slice(0, 2), [
    { type: 'archive', owner: owner('running-0'), key: 'running-0' },
    { type: 'launch', key: 'feature', pool: 'ready' }
  ]);
  assert.ok(result.actions.some(action => action.type === 'steer-planner'));
  assert.equal(result.state.items.find(item => item.key === 'maintenance').phase, 'candidate');
});

test('cleanup flags and source-bound publication are mandatory', () => {
  const f = fixture();
  f.offer('feature');
  f.attach('feature');
  assert.throws(() => f.complete('feature'), /Publication and complete cleanup/);
  for (const bad of [{ remoteHead: sha(3) }, { ready: false }, { enrolled: false }]) {
    assert.throws(() => f.publish('feature', bad), /head|ready PR/);
  }
  f.publish('feature');
  for (const bad of [{ remoteHead: sha(3) }, { stoppedWriting: false },
    { worktreeAbsent: false }, { branchAbsent: false }, { pendingInstructions: 1 }]) {
    assert.throws(() => f.complete('feature', bad), /complete cleanup/);
  }
  assert.equal(f.state().items[0].phase, 'published');
});

test('duplicate reports are idempotent and late phase regression cannot resume completed work', () => {
  const f = fixture();
  f.offer('feature');
  f.attach('feature');
  const event = { id: 'published-once', type: 'published', key: 'feature', owner: owner('feature'),
    proof: { pushedHead: sha(2), remoteHead: sha(2), pr: 500, ready: true, enrolled: true } };
  f.send(event);
  assert.equal(f.send(event).duplicate, true);
  f.complete('feature');
  assert.throws(() => f.publish('feature'), /Unknown or mismatched worker/);
  assert.equal(f.state().items[0].phase, 'complete');
});

test('conflicting scopes remain candidates and another unrelated outcome still dispatches', () => {
  const f = fixture();
  f.offer('directory', 'ready', { scopes: ['src\\Owner'] });
  assert.deepEqual(f.offer('child', 'ready', { scopes: ['SRC\\owner\\Child.cs'] }).actions, []);
  assert.ok(f.offer('sibling', 'ready', { scopes: ['src\\Ownership\\Child.cs'] })
    .actions.some(action => action.type === 'launch'));
  assert.throws(() => f.offer('escape', 'ready', { scopes: ['src\\..\\secret'] }), /Scope/);
  assert.throws(() => f.offer('glob', 'ready', { scopes: ['src\\*.cs'] }), /Scope/);
});

test('duplicate keys, outcomes, owners, PRs and excess adopted capacity are rejected', () => {
  const f = fixture();
  f.offer('one');
  assert.throws(() => f.offer('one'), /Duplicate/);
  assert.throws(() => f.offer('two', 'ready', { outcome: 'one' }), /Duplicate/);
  assert.throws(() => f.attach('one') && f.send({
    type: 'adopt', key: 'adopt', outcome: 'adopt', pool: 'ready', kind: 'feature',
    scopes: ['src\\Adopt.cs'], head: sha(1), evidence: 'Existing exclusive owner.', owner: owner('one')
  }), /Duplicate/);
  const repairs = fixture();
  repairs.offer('repair', 'repair', { pr: 100 });
  assert.throws(() => repairs.offer('same-pr', 'repair', { pr: 100 }), /PR already represented/);
  for (let index = 0; index < 3; index++) repairs.offer(`other-${index}`, 'repair');
  assert.throws(() => repairs.send({
    type: 'adopt', key: 'overflow', outcome: 'overflow', pool: 'repair', kind: 'repair',
    pr: 999, scopes: ['src\\Overflow.cs'], head: sha(1), evidence: 'Existing owner.', owner: owner('overflow')
  }), /pool is full/);
});

test('refresh coalesces, exhausted discovery does not loop, and stale candidates are not launched', () => {
  const f = fixture();
  assert.equal(f.send({ type: 'refresh', head: sha(2) }).actions[0].type, 'steer-planner');
  assert.deepEqual(f.send({ type: 'refresh', head: sha(2) }).actions, []);
  assert.throws(() => f.offer('stale', 'ready', { head: sha(1) }), /source refresh/);
  const end = () => f.send({ type: 'pass-ended', owner: planner,
    readyExamined: true, repairExamined: true, remaining: false });
  assert.equal(end().actions[0].type, 'discover');
  assert.deepEqual(end().actions, []);
  assert.equal(f.state().enabled, true);
  assert.equal(f.state().planner.active, false);
});

test('a pass cannot omit feature discovery, and unexamined scope continues without waiting for workers', () => {
  const f = fixture();
  assert.throws(() => f.send({ type: 'pass-ended', owner: planner,
    readyExamined: false, repairExamined: true, remaining: false }), /both pools/);
  f.offer('running');
  assert.equal(f.send({ type: 'pass-ended', owner: planner,
    readyExamined: true, repairExamined: true, remaining: true }).actions[0].type, 'discover');
});

test('early maintenance proposals cannot consume feature slots before bounded discovery finishes', () => {
  const f = fixture();
  for (let index = 0; index < 10; index++) {
    assert.deepEqual(f.offer(`maintenance-${index}`, 'ready', { kind: 'maintenance' }).actions, []);
  }
  assert.deepEqual(f.offer('feature').actions, [{ type: 'launch', key: 'feature', pool: 'ready' }]);
  const result = f.send({ type: 'pass-ended', owner: planner,
    readyExamined: true, repairExamined: true, remaining: false });
  assert.equal(result.actions.filter(action => action.type === 'launch').length, 9);
});

test('pause retains ownership and never launches queued work or another discovery pass', () => {
  const f = fixture();
  f.offer('feature');
  f.attach('feature');
  assert.deepEqual(f.send({ type: 'pause' }).actions, [
    { type: 'pause', owner: owner('feature'), key: 'feature' },
    { type: 'pause', owner: planner }
  ]);
  assert.deepEqual(f.send({ type: 'refresh', head: sha(2) }).actions, []);
  assert.throws(() => f.offer('new'), /active assigned planner/);
  assert.equal(f.state().items[0].phase, 'running');
});

test('handoff needs a paused workflow and stopped exact owners, then preserves outcome reservations', () => {
  const f = fixture();
  f.offer('feature');
  f.attach('feature');
  const event = { id: 'handoff', type: 'handoff', fromCoordinator: coordinator,
    toCoordinator: 'agent-host-session://new-coordinator',
    proof: { stoppedOwners: [coordinator, planner, owner('feature')], pendingInstructions: 0 } };
  assert.throws(() => f.send(event), /paused workflow/);
  f.send({ type: 'pause' });
  assert.throws(() => f.send({ ...event, proof: { stoppedOwners: [coordinator], pendingInstructions: 0 } }),
    /Every old writer/);
  assert.throws(() => f.send({ ...event, proof: { ...event.proof, pendingInstructions: 1 } }),
    /Every old writer/);
  const result = f.send(event);
  assert.deepEqual(result.actions, []);
  assert.equal(result.state.coordinator, event.toCoordinator);
  assert.equal(result.state.enabled, false);
  assert.equal(result.state.items[0].owner, owner('feature'));
  assert.equal(result.state.items[0].phase, 'running');
  assert.equal(result.state.planner.active, false);
  assert.throws(() => f.send({ type: 'resume', owner: coordinator,
    authorityConfirmed: true, discover: false }), /exact coordinator/);
  const resumed = f.send({ type: 'resume', owner: event.toCoordinator,
    authorityConfirmed: true, discover: true });
  assert.equal(resumed.actions[0].type, 'discover');
  assert.equal(resumed.state.items[0].phase, 'running');
});

test('unknown launch ownership or a worker address cannot become a new coordinator', () => {
  const f = fixture();
  f.offer('feature');
  f.send({ type: 'pause' });
  const event = { type: 'handoff', fromCoordinator: coordinator, toCoordinator: 'new',
    proof: { stoppedOwners: [coordinator, planner], pendingInstructions: 0 } };
  assert.throws(() => f.send(event), /unattached reservation/);
  f.attach('feature');
  assert.throws(() => f.send({ ...event, toCoordinator: owner('feature'),
    proof: { stoppedOwners: [coordinator, planner, owner('feature')], pendingInstructions: 0 } }), /owner/);
});

test('resume cannot dispatch retained candidates before fresh source proof', () => {
  const f = fixture();
  f.offer('occupied', 'ready', { scopes: ['src\\One.cs'] });
  f.attach('occupied');
  f.offer('one', 'ready', { scopes: ['src\\One.cs'] });
  f.send({ type: 'pause' });
  f.publish('occupied');
  f.complete('occupied');
  const resumed = f.send({ type: 'resume', owner: coordinator,
    authorityConfirmed: true, discover: true });
  assert.equal(resumed.state.head, null);
  assert.ok(resumed.actions.every(action => action.type !== 'launch'));
  assert.equal(resumed.state.items.find(item => item.key === 'one').phase, 'candidate');
  const refreshed = f.send({ type: 'refresh', head: sha(1) });
  assert.ok(refreshed.actions.some(action => action.type === 'launch'));
});

test('unknown events and malformed saved state fail without modifying input', () => {
  const f = fixture();
  const state = f.state();
  assert.throws(() => applyEvent(state, { id: 'bad', type: 'invented' }), /Unknown event/);
  assert.deepEqual(state, f.state());
  assert.throws(() => applyEvent({ ...state, version: 0 }, { id: 'x', type: 'pause' }), /Malformed/);
});

test('delayed reports cannot terminate or populate a newer discovery pass', () => {
  const f = fixture();
  f.send({ type: 'pass-ended', owner: planner,
    readyExamined: true, repairExamined: true, remaining: true });
  assert.equal(f.state().planner.pass, 2);
  assert.throws(() => f.send({ type: 'pass-ended', pass: 1, owner: planner,
    readyExamined: true, repairExamined: true, remaining: false }), /active assigned pass/);
  assert.throws(() => f.offer('late', 'ready', { pass: 1 }), /another discovery pass/);
});

test('corrupted completion receipts and noncanonical saved scopes fail closed', () => {
  const f = fixture();
  f.offer('feature');
  f.attach('feature');
  f.publish('feature');
  const state = f.state();
  state.items[0].phase = 'complete';
  assert.throws(() => applyEvent(state, { id: 'x', type: 'refresh', head: sha(1) }), /cleanup evidence/);
  state.items[0].phase = 'published';
  state.items[0].publication.enrolled = false;
  assert.throws(() => applyEvent(state, { id: 'x', type: 'pause' }), /Publication evidence/);
  const casing = f.state();
  casing.items[0].scopes = ['SRC\\Feature.cs'];
  assert.throws(() => applyEvent(casing, { id: 'x', type: 'pause' }), /Noncanonical/);
});

test('CLI consumes one input and exits; malformed input fails without success-shaped output', () => {
  const cli = path.join(__dirname, 'registry.cjs');
  const valid = spawnSync(process.execPath, [cli], { input: JSON.stringify({
    operation: 'init', coordinator, head: sha(1)
  }), encoding: 'utf8' });
  assert.equal(valid.status, 0);
  assert.equal(JSON.parse(valid.stdout).state.enabled, true);
  assert.equal(JSON.parse(valid.stdout).state.mode, 'observe');
  const invalid = spawnSync(process.execPath, [cli], { input: '{broken', encoding: 'utf8' });
  assert.equal(invalid.status, 1);
  assert.equal(invalid.stdout, '');
  assert.match(invalid.stderr, /SyntaxError/);
});

test('discovery-only authority parks candidates without manufacturing worker launch actions', () => {
  let state = createState(coordinator, sha(1));
  state = applyEvent(state, { id: 'planner', type: 'planner', owner: planner }).state;
  const result = applyEvent(state, { id: 'candidate', type: 'candidate', pass: 1,
    owner: planner, key: 'feature', outcome: 'feature', pool: 'ready', kind: 'feature',
    head: sha(1), scopes: ['src\\Feature.cs'], evidence: 'Admitted but no delivery authority.' });
  assert.deepEqual(result.actions, []);
  assert.equal(result.state.items[0].phase, 'candidate');
  assert.throws(() => createState(coordinator, sha(1), 'invented'), /Malformed/);
});

test('first source discovery and unchanged refresh do not restart an exhausted pass', () => {
  let state = createState(coordinator, null);
  state = applyEvent(state, { id: 'planner', type: 'planner', owner: planner }).state;
  state = applyEvent(state, { id: 'source', type: 'refresh', head: sha(1) }).state;
  let result = applyEvent(state, { id: 'end', type: 'pass-ended', pass: 1,
    owner: planner, readyExamined: true, repairExamined: true, remaining: false });
  assert.deepEqual(result.actions, []);
  result = applyEvent(result.state, { id: 'same', type: 'refresh', head: sha(1) });
  assert.deepEqual(result.actions, []);
  result = applyEvent(result.state, { id: 'explicit', type: 'refresh', head: sha(1), rescan: true });
  assert.equal(result.actions[0].type, 'discover');
});

test('failed launch remains explicitly reserved and unrelated deliveries continue', () => {
  const f = fixture();
  f.offer('failed');
  f.send({ type: 'launch-blocked', key: 'failed', reason: 'Host worktree creation denied.' });
  assert.equal(f.state().items[0].phase, 'reserved');
  assert.throws(() => f.attach('failed'), /available reservation/);
  assert.deepEqual(f.offer('other').actions, [{ type: 'launch', key: 'other', pool: 'ready' }]);
});

test('repository customizations reference existing files and request separate profiles', () => {
  const github = path.resolve(__dirname, '..', '..', '..');
  const files = [
    'agents/kora-coordinator.agent.md', 'agents/kora-planner.agent.md',
    'agents/kora-worker.agent.md', 'prompts/kora-orchestrate.prompt.md',
    'skills/kora-orchestrate/SKILL.md', 'skills/kora-orchestrate/planner.md',
    'skills/kora-orchestrate/worker.md', 'skills/kora-orchestrate/README.md'
  ];
  for (const file of files) {
    const absolute = path.join(github, file);
    const content = fs.readFileSync(absolute, 'utf8');
    for (const match of content.matchAll(/\]\(([^)]+)\)/g)) {
      const [target, fragment] = match[1].split('#');
      const linkedFile = path.resolve(path.dirname(absolute), target);
      assert.ok(fs.existsSync(linkedFile), `${file}: ${match[1]}`);
      if (fragment) {
        const headings = [...fs.readFileSync(linkedFile, 'utf8').matchAll(/^#+ (.+)$/gm)]
          .map(heading => heading[1].trim().toLowerCase().replace(/[^\w\s-]/g, '').replace(/\s/g, '-'));
        assert.ok(headings.includes(fragment), `${file}: missing heading ${match[1]}`);
      }
    }
    if (file.endsWith('.agent.md')) {
      assert.match(content, /^---\r?\n/);
      assert.match(content, /model: 'GPT-6\.1 Sol'/);
      assert.match(content, /disable-model-invocation: true/);
      assert.match(content, /agents: \[\]/);
      assert.match(content, new RegExp(`reasoning-effort: ${file.includes('worker') ? 'high' : 'low'}`));
    }
  }
  const coordinatorText = fs.readFileSync(path.join(github, files[0]), 'utf8');
  assert.match(coordinatorText, /each event turn is finite/);
  assert.match(coordinatorText, /steer an active coordinator and queue to an idle/);
  const skillText = fs.readFileSync(path.join(github, 'skills/kora-orchestrate/SKILL.md'), 'utf8');
  assert.match(skillText, /Do not\s+wait for a later host `complete` status/);
  assert.match(skillText, /delivery=queue/);
  assert.match(skillText, /same planner chat/);
  assert.match(skillText, /does not release file\s+ownership/);
});

for (const published of [false, true]) {
  test(`stopped ${published ? 'published cleanup-blocked' : 'running blocked'} contraction enables only disjoint delivery`, () => {
    let state = refinementFixture(published);
    for (const [key, scopes] of [['disjoint', ['src\\other.cs']], ['written', ['src\\written.cs']]]) {
      state = applyEvent(state, { id: key, type: 'candidate', owner: 'planner', pass: 1,
        key, outcome: key, pool: 'ready', kind: 'feature', head: state.head, scopes,
        evidence: 'Independent admitted complete operation.' }).state;
    }
    assert.equal(state.items[1].phase, 'candidate');
    const before = structuredClone(state.items[0]);
    const event = refinementEvent(state);
    const result = applyEvent(state, event);
    const item = result.state.items[0];
    for (const key of Object.keys(before).filter(key => key !== 'scopes')) {
      assert.deepEqual(item[key], before[key], key);
    }
    assert.equal(item.phase, published ? 'published' : 'running');
    assert.equal(item.scopeRevision, 1);
    assert.equal(item.sourceWritesRetired, true);
    assert.deepEqual(item.scopes, event.proof.toScopes);
    assert.deepEqual(item.scopeHistory, [{ eventId: event.id, proof: event.proof,
      acknowledgment: event.acknowledgment }]);
    assert.equal(result.state.items[1].phase, 'reserved');
    assert.equal(result.state.items[2].phase, 'candidate');
    assert.deepEqual(result.actions.map(action => action.type), ['launch', 'steer-planner']);
    assert.equal(result.actions[1].capacity.ready, 8);
    assert.ok(!result.actions.some(action => ['archive', 'pause'].includes(action.type)));
    assert.deepEqual(state.items[0], before);
    assert.deepEqual(applyEvent(result.state, event), {
      state: result.state, actions: [], duplicate: true
    });
  });
}

test('refinement rejects invalid scopes, incomplete evidence and stale bindings without effects', () => {
  const state = refinementFixture(true);
  const invalid = [
    e => { e.owner = 'other'; },
    e => { e.key = 'other'; },
    e => { e.expectedScopeRevision = 1; },
    e => { e.proof.owner = 'other'; },
    e => { e.proof.key = 'other'; },
    e => { e.proof.scopeRevision = 1; },
    e => { e.proof.phase = 'running'; },
    e => { e.proof.fromScopes = ['src']; },
    ...[[], ['src', 'tests'], ['tests', 'src'], ['src', 'tests', 'other'],
      ['src\\written.cs'], ['src\\..\\written.cs', 'tests'], ['src\\.. \\written.cs', 'tests'],
      ['Q:\\src', 'tests'], ['src/*.cs', 'tests'], ['src\\*.cs', 'tests'],
      ['src\\written.cs', 'SRC\\written.cs', 'tests'], ['src\\written.cs', 'src\\written.cs', 'tests']]
      .map(scopes => e => { e.proof.toScopes = scopes; }),
    ...['stoppedWriting', 'expansionFrozen'].flatMap(key =>
      [false, undefined, 'unknown'].map(value => e => { e.proof[key] = value; })),
    ...[1, undefined, '0'].map(value => e => { e.proof.pendingInstructions = value; }),
    e => { e.proof.manifest.complete = false; },
    e => { delete e.proof.manifest.planned; },
    e => { e.proof.manifest.actual.push('src\\other.cs'); },
    e => { e.proof.manifest.actual = []; },
    e => { e.proof.preservation.complete = false; },
    e => { e.proof.preservation.commits = []; },
    e => { e.proof.preservation.retained[0].receipt = ''; },
    e => { e.proof.publicationHead = '2'.repeat(40); },
    e => { e.proof.publication.enrolled = false; },
    e => { e.proof.reservation.pr = 870; },
    e => { e.proof.reservation.outcome = 'different'; },
    e => { e.proof.receipt = ''; },
    e => { e.proof.extra = true; },
    e => { e.acknowledgment.owner = 'other'; },
    e => { e.acknowledgment.key = 'other'; },
    e => { e.acknowledgment.scopeRevision = 1; },
    e => { e.acknowledgment.id = ''; },
    e => { e.acknowledgment.receipt = ''; },
    e => { e.acknowledgment.extra = true; }
  ];
  for (const [index, change] of invalid.entries()) {
    const before = structuredClone(state);
    const event = refinementEvent(state, `invalid-${index}`);
    change(event);
    rebind(event);
    assert.throws(() => applyEvent(state, event), undefined, `case ${index}`);
    assert.deepEqual(state, before);
  }
  for (const key of ['proof', 'acknowledgment']) {
    const event = refinementEvent(state);
    delete event[key];
    assert.throws(() => applyEvent(state, event));
  }
  const event = refinementEvent(state);
  event.acknowledgment.proofDigest = '0'.repeat(64);
  assert.throws(() => applyEvent(state, event), /acknowledgment/);
});

test('paused, observation, unattached, candidate and completed owners cannot refine', () => {
  const state = refinementFixture();
  const event = refinementEvent(state);
  assert.throws(() => applyEvent({ ...state, enabled: false }, event), /enabled delivery/);
  assert.throws(() => applyEvent({ ...state, mode: 'observe' }, event), /enabled delivery/);
  for (const phase of ['candidate', 'reserved']) {
    const input = structuredClone(state);
    input.items[0].phase = phase;
    input.items[0].owner = null;
    assert.throws(() => applyEvent(input, { ...event, owner: null }), /worker|attached/);
  }
  let complete = refinementFixture(true);
  complete = applyEvent(complete, { id: 'complete', type: 'completed', key: 'holding',
    owner: 'stopped-owner', proof: { remoteHead: complete.items[0].publication.remoteHead,
      stoppedWriting: true, worktreeAbsent: true, branchAbsent: true, pendingInstructions: 0 } }).state;
  assert.throws(() => applyEvent(complete, refinementEvent(complete)), /worker/);
});

test('fresh successive acknowledgment is required, retained manifests cannot lose prior writes, and eligibility coalesces', () => {
  const original = refinementFixture();
  const first = refinementEvent(original);
  first.proof.toScopes = ['src\\written.cs', 'tests'];
  rebind(first);
  const state = applyEvent(original, first).state;
  assert.throws(() => applyEvent(state, { ...first, id: 'stale-new-id' }), /Stale scope/);
  const next = refinementEvent(state, 'refine-two');
  next.acknowledgment.id = first.acknowledgment.id;
  assert.throws(() => applyEvent(state, next), /replay/);
  next.acknowledgment.id = 'fresh-ack-two';
  const missing = structuredClone(next);
  missing.proof.manifest.actual = ['src\\written.cs'];
  missing.proof.manifest.planned = [];
  missing.proof.preservation.retained = [];
  assert.throws(() => applyEvent(state, rebind(missing)), /history/);
  const result = applyEvent(state, next);
  assert.equal(result.state.items[0].scopeRevision, 2);
  assert.equal(result.state.items[0].scopeHistory.length, 2);
  assert.deepEqual(result.actions, []);
  validate(result.state);
});

test('refinement schema, proof, history and retirement survive resume and later milestones; tampering rejects', () => {
  const state = refinementFixture();
  let refined = applyEvent(state, refinementEvent(state)).state;
  for (const tampered of tamperedStates(refined)) assert.throws(() => validate(tampered));
  validate(state);
  assert.equal(state.version, 1);
  refined = applyEvent(refined, { id: 'published-later', type: 'published',
    owner: 'stopped-owner', key: 'holding', proof: { pushedHead: state.head, remoteHead: state.head,
      pr: 869, ready: true, enrolled: true } }).state;
  refined = applyEvent(refined, { id: 'blocked-later', type: 'blocked',
    owner: 'stopped-owner', key: 'holding', reason: 'Still host locked.' }).state;
  const history = structuredClone(refined.items[0].scopeHistory);
  assert.throws(() => applyEvent(refined, { id: 'bad-cleanup', type: 'completed',
    owner: 'stopped-owner', key: 'holding', proof: { remoteHead: state.head,
      stoppedWriting: true, worktreeAbsent: false, branchAbsent: true, pendingInstructions: 0 } }));
  refined = applyEvent(refined, { id: 'pause', type: 'pause' }).state;
  refined = applyEvent(refined, { id: 'resume', type: 'resume', owner: 'coordinator',
    authorityConfirmed: true, discover: false }).state;
  assert.equal(refined.head, null);
  refined = applyEvent(refined, { id: 'complete-later', type: 'completed', owner: 'stopped-owner',
    key: 'holding', proof: { remoteHead: state.head, stoppedWriting: true,
      worktreeAbsent: true, branchAbsent: true, pendingInstructions: 0 } }).state;
  assert.deepEqual(refined.items[0].scopeHistory, history);
  assert.equal(refined.items[0].sourceWritesRetired, true);
  validate(refined);
});

test('unpreserved local writes and planned writes stay covered, while exhausted discovery restarts only on acceptance', () => {
  let state = refinementFixture();
  state = applyEvent(state, { id: 'end', type: 'pass-ended', owner: 'planner', pass: 1,
    readyExamined: true, repairExamined: true, remaining: false }).state;
  assert.equal(state.planner.active, false);
  const event = refinementEvent(state);
  event.proof.preservation.unpreserved = event.proof.preservation.retained;
  event.proof.preservation.retained = [];
  event.proof.manifest.planned.push('tests\\future.test.cs');
  assert.throws(() => applyEvent(state, rebind(event)), /uncovered writes/);
  assert.equal(state.planner.active, false);
  event.proof.toScopes = ['src\\written.cs', 'tests'];
  const result = applyEvent(state, rebind(event));
  assert.deepEqual(result.actions.map(action => action.type), ['discover']);
  assert.equal(result.actions[0].pass, 2);
  assert.equal(result.actions[0].capacity.ready, 9);
  assert.deepEqual(result.state.items[0].scopeHistory[0].proof.preservation.unpreserved,
    event.proof.preservation.unpreserved);
  assert.equal(result.state.items[0].blocker, state.items[0].blocker);
});

test('documented proof JSON and digest CLI execute against the exact protocol; skill frontmatter is unchanged', () => {
  const directory = path.resolve(__dirname, '..');
  const readme = fs.readFileSync(path.join(directory, 'README.md'), 'utf8');
  const proof = JSON.parse(readme.match(/```json\r?\n([\s\S]*?)\r?\n```/)[1]);
  let state = createState('coordinator', proof.reservation.head, 'delivery');
  state = applyEvent(state, { id: 'adopt', type: 'adopt', owner: proof.owner, key: proof.key,
    ...proof.reservation, scopes: proof.fromScopes, evidence: 'Synthetic documented example.' }).state;
  state = applyEvent(state, { id: 'published', type: 'published', owner: proof.owner,
    key: proof.key, proof: proof.publication }).state;
  const expression = readme.match(/node -e '([^']+)'/)[1];
  const cli = spawnSync(process.execPath, ['-e', expression], {
    input: JSON.stringify(proof), encoding: 'utf8', cwd: path.resolve(directory, '..', '..', '..')
  });
  assert.equal(cli.status, 0, cli.stderr);
  assert.equal(cli.stdout.trim(), digest(proof));
  const event = { id: 'example', type: 'refine-scopes', owner: proof.owner, key: proof.key,
    expectedScopeRevision: proof.scopeRevision, proof,
    acknowledgment: { id: 'example-ack', receipt: 'Synthetic example only', owner: proof.owner,
      key: proof.key, scopeRevision: proof.scopeRevision, proofDigest: cli.stdout.trim() } };
  assert.equal(applyEvent(state, event).state.items[0].scopeRevision, 1);
  assert.match(fs.readFileSync(path.join(directory, 'SKILL.md'), 'utf8'),
    /^---\r?\nname: kora-orchestrate\r?\ndescription: 'Use for Kora child-chat orchestration, delivery starvation, stalled owners, reservations, and finite message-driven coordinator turns\. No automation\.'\r?\n---/);
});
