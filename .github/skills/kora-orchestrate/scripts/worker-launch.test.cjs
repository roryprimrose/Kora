'use strict';

const assert = require('node:assert/strict');
const { test } = require('node:test');
const { createState, applyEvent, digest, heldReservations } = require('./registry.cjs');
const fs = require('node:fs');
const path = require('node:path');
const { workerLaunch } = require('./worker-launch.cjs');
const { refinementFixture, refinementEvent, tamperedStates,
  candidateFixture, candidateEvent, supersessionFixture, supersessionEvent,
  supersessionBeneficiaryFixture } = require('./refinement-fixture.cjs');

const head = '1'.repeat(40);
const settings = {
  repository: 'roryprimrose/Kora', publicationAuthorized: true,
  controlWorkspace: 'Q:\\Repos\\Kora.worktrees\\control',
  productionWorkspace: 'Q:\\Repos\\Kora',
  scratch: 'Q:\\Repos\\Kora.worktrees\\control\\.scratch\\run'
};

test('supersession launch consumes effective exclusions and separately preserves immutable historical scope', () => {
  const state = supersessionFixture();
  const result = applyEvent(state, supersessionEvent(state), { revision: 41 });
  const prompt = workerLaunch(result.state, result.actions[0], settings).prompt;
  assert.match(prompt, /Exclusive repository-relative write scopes: src\\shared.cs; tests\\shared.test.cjs/);
  assert.match(prompt, /Retained exclusion key=prior-0, owner=owner-0, scopes=docs\\retained.md/);
  assert.match(prompt, /Historical reservation key=prior-0/);
  assert.match(prompt, /Prior source writes permanently retired by accepted supersession/);
  assert.doesNotMatch(prompt, /Retained exclusion key=prior-0, owner=owner-0, scopes=src\\shared.cs/);
  for (const change of [
    s => { delete s.supersessions; },
    s => { s.version = 4; },
    s => { s.items.at(-1).scopes.push('src\\unauthorized.cs'); },
    s => { s.supersessions[0].event.proof.holders.pop(); }
  ]) {
    const altered = structuredClone(result.state);
    change(altered);
    assert.throws(() => workerLaunch(altered, result.actions[0], settings));
  }
});

test('prepared external proposal never authorizes launch or adoption even with a fabricated action', () => {
  const state = supersessionFixture();
  const external = supersessionFixture();
  external.coordinator = 'external-coordinator';
  const event = supersessionEvent(state, 41, 'prepare-external',
    [{ repository: settings.repository, revision: 122, state: external,
      stateDigest: digest(external), receipt: 'Synthetic exact external snapshot.' }]);
  const result = applyEvent(state, event, { revision: 41 });
  const action = { type: 'launch', key: 'successor', pool: 'ready' };
  assert.throws(() => workerLaunch(result.state, action, settings), /reservation|prepared/);
  const forged = structuredClone(result.state);
  forged.items.at(-1).phase = 'reserved';
  assert.throws(() => workerLaunch(forged, action, settings), /prepared|conflict/i);
  assert.throws(() => applyEvent(result.state, { id: 'adopt-external', type: 'adopt', owner: 'new-owner',
    key: 'different-key', outcome: 'different outcome', head, scopes: ['src\\shared.cs'],
    pool: 'ready', kind: 'feature', evidence: 'Cannot bypass prepared external guard.' }), /conflict/i);
});

test('completed beneficiary permits later same-path launch while prior retirement and retained cleanup remain', () => {
  const { complete } = supersessionBeneficiaryFixture();
  const result = applyEvent(complete, { id: 'review-later-launch', type: 'candidate',
    owner: 'planner', pass: 1, key: 'later-feature', outcome: 'later launch outcome',
    head: complete.head, scopes: ['src\\shared.cs'], pool: 'ready', kind: 'feature',
    evidence: 'Fresh independent admission after complete beneficiary cleanup.' });
  assert.deepEqual(result.actions, [{ type: 'launch', key: 'later-feature', pool: 'ready' }]);
  const prompt = workerLaunch(result.state, result.actions[0], settings).prompt;
  assert.match(prompt, /Exclusive repository-relative write scopes: src\\shared.cs/);
  assert.match(prompt, /Retained exclusion key=prior-0, owner=owner-0, scopes=docs\\retained.md/);
  assert.match(prompt, /Prior source writes permanently retired by accepted supersession/);
  assert.doesNotMatch(prompt, /Retained exclusion key=(?:successor|supersession:)/);
  assert.doesNotMatch(prompt, /transferred to this assignment/);
  assert.deepEqual(result.state.supersessions, complete.supersessions);
});

test('launch consumes exact v4 revalidated scopes and all old proofs, never held-conflicting candidate authority', () => {
  const held = candidateFixture();
  const event = candidateEvent(held);
  assert.throws(() => applyEvent(held, event), /conflicts with held/);
  assert.throws(() => workerLaunch(held,
    { type: 'launch', key: event.key, pool: 'ready' }, settings), /reservation/);
  const state = candidateFixture(true);
  const result = applyEvent(state, candidateEvent(state));
  const launch = workerLaunch(result.state, result.actions[0], settings);
  for (const value of candidateEvent(state).scopes) assert.ok(launch.prompt.includes(value));
  assert.equal(result.state.amendments.length, 4);
  assert.equal(result.state.refinements.length, 1);
  for (const change of [
    value => { delete value.readmissions; }, value => { value.version = 3; },
    value => { value.items.find(item => item.admissionHistory.length).admissionHistory = []; },
    value => { value.items.find(item => item.admissionHistory.length).scopes.pop(); },
    value => { value.items.find(item => item.admissionHistory.length).admissionHistory[0].event.expectedItemDigest = '0'.repeat(64); }
  ]) {
    const altered = structuredClone(result.state);
    change(altered);
    assert.throws(() => workerLaunch(altered, result.actions[0], settings));
  }
});

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

test('launch consumes refined schema, allows disjoint work and rejects proof omission/downgrade', () => {
  let state = refinementFixture(true);
  state = applyEvent(state, { id: 'disjoint', type: 'candidate', owner: 'planner', pass: 1,
    key: 'one', outcome: 'independent outcome', pool: 'ready', kind: 'feature', head,
    scopes: ['src\\other.cs'], evidence: 'Independent admitted complete interaction.' }).state;
  const result = applyEvent(state, refinementEvent(state));
  const action = result.actions.find(value => value.type === 'launch');
  assert.equal(action.key, 'one');
  const prompt = workerLaunch(result.state, action, settings).prompt;
  assert.match(prompt, /src\\other.cs/);
  assert.match(prompt, /Retained exclusion key=holding, owner=stopped-owner/);
  assert.match(prompt, /src\\written.cs; tests\\written.test.cs, sourceWritesRetired=true/);
  assert.equal(result.state.items[0].phase, 'published');
  assert.equal(result.state.items[0].pr, 869);
  for (const tampered of tamperedStates(result.state)) {
    assert.throws(() => workerLaunch(tampered, action, settings));
  }
  assert.throws(() => workerLaunch(result.state,
    { type: 'launch', key: 'holding', pool: 'ready' }, settings), /reservation/);
  assert.equal(fixture().state.version, 1);
  assert.equal(workerLaunch(fixture().state, fixture().action, settings).worktree, true);
});

test('launch validates documented mutable amendment and retained union but never relaunches attached owner', () => {
  const proof = JSON.parse([...fs.readFileSync(path.resolve(__dirname, '..', 'README.md'), 'utf8')
    .matchAll(/```json\r?\n([\s\S]*?)\r?\n```/g)][1][1]);
  const f = fixture();
  let state = applyEvent(f.state, { id: 'adopt-doc', type: 'adopt', key: proof.key,
    owner: proof.owner, ...proof.reservation, scopes: proof.fromScopes,
    evidence: 'Synthetic documentation mutable owner.' }).state;
  proof.exclusivity.reservations = heldReservations(state, proof.key);
  const event = { id: 'extend-doc', type: 'extend-scopes', key: proof.key, owner: proof.owner,
    expectedScopeRevision: proof.scopeRevision, proof,
    acknowledgment: { id: 'fresh-doc-ack', receipt: 'Synthetic exact owner response', key: proof.key,
      owner: proof.owner, scopeRevision: proof.scopeRevision, proofDigest: digest(proof) } };
  state = applyEvent(state, event).state;
  const prompt = workerLaunch(state, f.action, settings).prompt;
  assert.match(prompt, /src\\owned.cs; tests\\owned.test.cs, sourceWritesRetired=false/);
  assert.match(prompt, /separate coordinator continuation/);
  assert.throws(() => workerLaunch(state,
    { type: 'launch', key: proof.key, pool: 'ready' }, settings), /reservation/);
  const mutations = [
    v => { delete v.amendments; }, v => { v.amendments = []; },
    v => { delete v.items[1].scopeAmendments; }, v => { v.items[1].scopeAmendments = []; },
    v => { v.items[1].scopeAmendments[0].proof.receipt = 'altered'; },
    v => { v.items[1].scopeAmendments[0].acknowledgment.id = 'altered'; },
    v => { v.items[1].scopeRevision = 0; }, v => { v.items[1].sourceWritesRetired = true; },
    v => { v.items[1].scopes.pop(); }, v => { v.version = 2; },
    v => { v.version = 1; },
    v => { v.seen = v.seen.filter(id => id !== event.id); }
  ];
  for (const mutate of mutations) {
    const invalid = structuredClone(state);
    mutate(invalid);
    assert.throws(() => workerLaunch(invalid, f.action, settings));
  }
});
