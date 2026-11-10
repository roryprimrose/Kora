'use strict';

const assert = require('node:assert/strict');
const { test } = require('node:test');
const path = require('node:path');
const fs = require('node:fs');
const { checkpoint } = require('./checkpoint.cjs');
const { applyEvent, digest, heldReservations } = require('./registry.cjs');
const { refinementFixture, refinementEvent, tamperedStates,
  candidateFixture, candidateEvent, supersessionFixture, supersessionEvent,
  supersessionBeneficiaryFixture } = require('./refinement-fixture.cjs');

function fixture() {
  const files = new Map();
  let failRename = false;
  const io = {
    mkdirSync() {},
    existsSync: file => files.has(file),
    openSync(file) {
      if (files.has(file)) throw new Error('EEXIST: checkpoint locked');
      files.set(file, '');
      return file;
    },
    closeSync() {},
    readFileSync: file => {
      if (!files.has(file)) throw new Error('ENOENT');
      return files.get(file);
    },
    writeFileSync: (file, value) => {
      assert.ok(!files.has(file));
      files.set(file, value);
    },
    renameSync: (from, to) => {
      if (failRename) throw new Error('Replacement failed');
      files.set(to, files.get(from));
      files.delete(from);
    },
    unlinkSync: file => files.delete(file)
  };
  const file = path.resolve('.scratch', 'fixture', 'checkpoint.json');
  const run = input => checkpoint(file, input, io);
  run({ operation: 'init', expectedRevision: 0, coordinator: 'fixture-coordinator', head: null,
    mode: 'delivery' });
  return { files, file, run, failReplacement: () => { failRename = true; } };
}

function candidateCheckpoint(released = true) {
  const f = fixture();
  const state = candidateFixture(released);
  f.files.set(f.file, JSON.stringify({ version: 1, revision: 107, state, actions: [],
    events: [...state.refinements.map(record => ({ id: record.eventId, type: 'refine-scopes' })),
      ...state.amendments.map(record => ({ id: record.eventId, type: 'extend-scopes' }))] }));
  return f;
}

function supersessionCheckpoint() {
  const f = fixture();
  f.files.set(f.file, JSON.stringify({ version: 1, revision: 41,
    state: supersessionFixture(), actions: [], events: [] }));
  return f;
}

test('supersession persists full journal binding before launch and exact replay is byte-idempotent', () => {
  const f = supersessionCheckpoint();
  const event = supersessionEvent(f.run({ operation: 'read' }).state);
  const value = f.run({ operation: 'apply', expectedRevision: 41, event });
  assert.equal(value.revision, 42);
  assert.equal(value.events.at(-1).digest, digest(value.state.supersessions[0]));
  assert.deepEqual(value.actions.map(action => action.command),
    [{ type: 'launch', key: 'successor', pool: 'ready' }]);
  const before = f.files.get(f.file);
  f.run({ operation: 'apply', expectedRevision: 42, event });
  assert.equal(f.files.get(f.file), before);
  assert.throws(() => f.run({ operation: 'apply', expectedRevision: 42,
    event: { ...event, type: 'pause' } }), /replay/);
  assert.equal(f.files.get(f.file), before);
});

test('supersession lock, stale revision, changed holder and atomic replacement failures preserve exact bytes', () => {
  for (const failure of ['lock', 'revision', 'proof-revision', 'holder', 'replace']) {
    const f = supersessionCheckpoint();
    const state = f.run({ operation: 'read' }).state;
    const event = supersessionEvent(state, failure === 'proof-revision' ? 40 : 41);
    if (failure === 'holder') event.proof.localReservations[0].blocker = 'changed holder';
    const before = f.files.get(f.file);
    if (failure === 'lock') f.files.set(`${f.file}.lock`, 'other writer');
    if (failure === 'replace') f.failReplacement();
    assert.throws(() => f.run({ operation: 'apply', expectedRevision: failure === 'revision' ? 40 : 41,
      event }));
    assert.equal(f.files.get(f.file), before);
    assert.deepEqual([...f.files.keys()].sort(),
      (failure === 'lock' ? [f.file, `${f.file}.lock`] : [f.file]).sort());
  }
});

test('supersession checkpoint rejects journal removal, altered history, launch omission and changed launch', () => {
  for (const change of [
    v => { v.events.pop(); },
    v => { v.events.at(-1).digest = '0'.repeat(64); },
    v => { v.state.supersessions[0].event.proof.head = '0'.repeat(40); },
    v => { v.actions = []; },
    v => { v.actions[0].command.key = 'prior-0'; },
    v => {
      v.actions[0].command.key = 'prior-0';
      v.events.at(-1).commands[0].key = 'prior-0';
    },
    v => { v.actions.push(structuredClone(v.actions[0])); }
  ]) {
    const f = supersessionCheckpoint();
    const state = f.run({ operation: 'read' }).state;
    const value = f.run({ operation: 'apply', expectedRevision: 41, event: supersessionEvent(state) });
    change(value);
    f.files.set(f.file, JSON.stringify(value));
    const before = f.files.get(f.file);
    assert.throws(() => f.run({ operation: 'read' }));
    assert.throws(() => f.run({ operation: 'apply', expectedRevision: 42,
      event: { id: 'pause-corrupt', type: 'pause' } }));
    assert.equal(f.files.get(f.file), before);
  }
});

test('durable beneficiary cleanup releases its future fence only after accepted complete proof', () => {
  const f = supersessionCheckpoint();
  const { completion, published } = supersessionBeneficiaryFixture();
  const before = f.run({ operation: 'read' });
  f.run({ operation: 'apply', expectedRevision: 41, event: supersessionEvent(before.state) });
  f.run({ operation: 'apply', expectedRevision: 42, event: { id: 'review-successor-attached',
    type: 'attached', key: 'successor', owner: 'review-successor' } });
  const held = f.run({ operation: 'apply', expectedRevision: 43, event: {
    id: 'review-successor-published', type: 'published', key: 'successor', owner: 'review-successor',
    proof: published.items.find(item => item.key === 'successor').publication } });
  const oldBytes = f.files.get(f.file);
  assert.throws(() => f.run({ operation: 'apply', expectedRevision: 44,
    event: { ...completion, proof: { ...completion.proof, branchAbsent: false } } }), /cleanup/);
  assert.equal(f.files.get(f.file), oldBytes);
  assert.ok(heldReservations(f.run({ operation: 'read' }).state, 'later-feature')
    .some(item => item.key === 'supersession:supersede-once:successor'));
  const completed = f.run({ operation: 'apply', expectedRevision: 44, event: completion });
  assert.deepEqual(completed.state.supersessions, held.state.supersessions);
  assert.equal(heldReservations(f.run({ operation: 'read' }).state, 'later-feature')
    .some(item => item.key === 'supersession:supersede-once:successor'), false);
  const later = f.run({ operation: 'apply', expectedRevision: 45, event: {
    id: 'review-later-adopt', type: 'adopt', key: 'later-feature', owner: 'review-later-owner',
    outcome: 'later independent outcome', head: before.state.head, scopes: ['src\\shared.cs'],
    pool: 'ready', kind: 'feature', evidence: 'Independent admission after complete beneficiary cleanup.' } });
  assert.equal(later.state.items.at(-1).phase, 'running');
  assert.deepEqual(later.state.items.slice(0, 2), held.state.items.slice(0, 2));
});

test('atomic candidate readmission persists exact old-item/source binding and only normal launch actions', () => {
  const f = candidateCheckpoint();
  const old = f.run({ operation: 'read' });
  const event = candidateEvent(old.state);
  const result = f.run({ operation: 'apply', expectedRevision: 107, event });
  assert.equal(result.revision, 108);
  assert.equal(result.state.version, 4);
  assert.deepEqual(result.state.refinements, old.state.refinements);
  assert.deepEqual(result.state.amendments, old.state.amendments);
  assert.equal(result.events.at(-1).id, event.id);
  assert.deepEqual(result.actions.map(action => action.command),
    [{ type: 'launch', key: event.key, pool: 'ready' }]);
  const bytes = f.files.get(f.file);
  const repeated = f.run({ operation: 'apply', expectedRevision: 108, event });
  assert.equal(repeated.revision, 108);
  assert.equal(f.files.get(f.file), bytes);
  assert.throws(() => f.run({ operation: 'apply', expectedRevision: 108,
    event: { ...event, id: 'new-id-replay' } }), /unowned unreserved/);
  assert.equal(f.files.get(f.file), bytes);
});

test('readmission stale checkpoint/source/item, held guards, lock and replacement failures preserve exact bytes', () => {
  for (const failure of ['revision', 'source', 'item', 'lock', 'replace', 'guard']) {
    const f = candidateCheckpoint(failure !== 'guard');
    const state = f.run({ operation: 'read' }).state;
    const event = candidateEvent(state);
    const before = f.files.get(f.file);
    if (failure === 'source') event.head = '0'.repeat(40);
    if (failure === 'item') event.expectedItemDigest = '0'.repeat(64);
    if (failure === 'lock') f.files.set(`${f.file}.lock`, 'other writer');
    if (failure === 'replace') f.failReplacement();
    assert.throws(() => f.run({ operation: 'apply',
      expectedRevision: failure === 'revision' ? 106 : 107, event }));
    assert.equal(f.files.get(f.file), before);
    assert.deepEqual([...f.files.keys()].sort(),
      (failure === 'lock' ? [f.file, `${f.file}.lock`] : [f.file]).sort());
  }
});

test('checkpoint read/apply reject omitted admission journal/history or saved schema downgrade', () => {
  const changes = [
    value => { value.events.pop(); },
    value => { value.events.push({ id: 'forged-journal', type: 'revalidate-candidate' }); },
    value => { value.events.at(-1).type = 'candidate'; },
    value => { value.state.readmissions = []; },
    value => { value.state.version = 3; },
    value => { value.state.items.find(item => item.admissionHistory.length).admissionHistory = []; },
    value => { value.state.items.find(item => item.admissionHistory.length).admissionHistory[0].event.head = '0'.repeat(40); }
  ];
  for (const change of changes) {
    const f = candidateCheckpoint();
    const event = candidateEvent(f.run({ operation: 'read' }).state);
    const saved = f.run({ operation: 'apply', expectedRevision: 107, event });
    change(saved);
    f.files.set(f.file, JSON.stringify(saved));
    const before = f.files.get(f.file);
    assert.throws(() => f.run({ operation: 'read' }));
    assert.throws(() => f.run({ operation: 'apply', expectedRevision: 108,
      event: { id: 'refresh-invalid-admission', type: 'refresh', head: saved.state.head } }));
    assert.equal(f.files.get(f.file), before);
  }
});

test('checkpoint records actions before sending and requires evidence to acknowledge', () => {
  const f = fixture();
  const applied = f.run({ operation: 'apply', expectedRevision: 1,
    event: { id: 'planner', type: 'planner', owner: 'fixture-planner' } });
  assert.equal(applied.revision, 2);
  assert.equal(applied.actions[0].status, 'pending');
  assert.equal(applied.actions[0].command.type, 'discover');
  assert.throws(() => f.run({ operation: 'ack', expectedRevision: 2,
    actionId: 'planner:0', status: 'done', evidence: '' }), /concrete evidence/);
  const acknowledged = f.run({ operation: 'ack', expectedRevision: 2,
    actionId: 'planner:0', status: 'done', evidence: 'child chat and accepted message ID' });
  assert.equal(acknowledged.actions[0].status, 'done');
  assert.equal(f.run({ operation: 'read' }).revision, 3);
});

test('duplicate events do not emit another action or mutate a persisted checkpoint', () => {
  const f = fixture();
  const event = { id: 'planner', type: 'planner', owner: 'fixture-planner' };
  f.run({ operation: 'apply', expectedRevision: 1, event });
  const before = f.files.get(f.file);
  assert.equal(f.run({ operation: 'apply', expectedRevision: 2, event }).revision, 2);
  assert.equal(f.files.get(f.file), before);
});

test('stale writers, malformed state, and existing locks fail rather than overwriting', () => {
  const f = fixture();
  assert.throws(() => f.run({ operation: 'apply', expectedRevision: 0,
    event: { id: 'pause', type: 'pause' } }), /Stale/);
  f.files.set(`${f.file}.lock`, 'another owner');
  assert.throws(() => f.run({ operation: 'apply', expectedRevision: 1,
    event: { id: 'pause', type: 'pause' } }), /locked/);
  assert.equal(f.files.get(`${f.file}.lock`), 'another owner');
  f.files.delete(`${f.file}.lock`);
  f.files.set(f.file, '{"version":0}');
  assert.throws(() => f.run({ operation: 'read' }), /Malformed checkpoint/);
});

test('failed replacement preserves old checkpoint and removes only owned lock/temporary file', () => {
  const f = fixture();
  const before = f.files.get(f.file);
  f.failReplacement();
  assert.throws(() => f.run({ operation: 'apply', expectedRevision: 1,
    event: { id: 'pause', type: 'pause' } }), /Replacement failed/);
  assert.equal(f.files.get(f.file), before);
  assert.deepEqual([...f.files.keys()], [f.file]);
});

test('checkpoint rejects relative and non-scratch destinations', () => {
  assert.throws(() => checkpoint('checkpoint.json', { operation: 'read' }), /absolute canonical/);
  assert.throws(() => checkpoint(path.resolve('checkpoint.json'), { operation: 'read' }),
    /absolute canonical/);
});

test('durable refinement rejects stale writers, resumes proof and binds checkpoint event schema', () => {
  const f = fixture();
  const legacy = JSON.parse(f.files.get(f.file));
  legacy.state = refinementFixture(true);
  f.files.set(f.file, JSON.stringify(legacy));
  assert.equal(f.run({ operation: 'read' }).state.version, 1);
  const event = refinementEvent(legacy.state);
  const before = f.files.get(f.file);
  assert.throws(() => f.run({ operation: 'apply', expectedRevision: 0, event }), /Stale/);
  assert.equal(f.files.get(f.file), before);
  const value = f.run({ operation: 'apply', expectedRevision: 1, event });
  assert.equal(value.state.version, 2);
  assert.equal(value.revision, 2);
  assert.equal(value.events[0].type, 'refine-scopes');
  assert.deepEqual(f.run({ operation: 'read' }), value);
  const saved = f.files.get(f.file);
  assert.equal(f.run({ operation: 'apply', expectedRevision: 2, event }).revision, 2);
  assert.equal(f.files.get(f.file), saved);
  assert.throws(() => f.run({ operation: 'apply', expectedRevision: 2,
    event: { ...event, id: 'stale-proof' } }), /Stale scope/);
  assert.equal(f.files.get(f.file), saved);
  for (const state of tamperedStates(value.state)) {
    f.files.set(f.file, JSON.stringify({ ...value, state }));
    assert.throws(() => f.run({ operation: 'read' }));
    assert.throws(() => f.run({ operation: 'apply', expectedRevision: 2,
      event: { id: 'pause', type: 'pause' } }));
  }
  const downgraded = structuredClone(legacy);
  downgraded.events = value.events;
  f.files.set(f.file, JSON.stringify(downgraded));
  assert.throws(() => f.run({ operation: 'read' }), /event\/schema/);
  f.files.set(f.file, JSON.stringify({ ...value, events: [] }));
  assert.throws(() => f.run({ operation: 'read' }), /event\/schema/);
});

test('failed refinement replacement retains original broad reservation with no eligibility journal', () => {
  const f = fixture();
  const value = JSON.parse(f.files.get(f.file));
  value.state = refinementFixture();
  f.files.set(f.file, JSON.stringify(value));
  const before = f.files.get(f.file);
  f.failReplacement();
  assert.throws(() => f.run({ operation: 'apply', expectedRevision: 1,
    event: refinementEvent(value.state) }), /Replacement failed/);
  assert.equal(f.files.get(f.file), before);
  assert.deepEqual([...f.files.keys()], [f.file]);
  assert.deepEqual(f.run({ operation: 'read' }).state.items[0].scopes, ['src', 'tests']);
});

function amendmentCheckpointFixture(refined = false) {
  const f = fixture();
  const proof = JSON.parse([...fs.readFileSync(path.resolve(__dirname, '..', 'README.md'), 'utf8')
    .matchAll(/```json\r?\n([\s\S]*?)\r?\n```/g)][1][1]);
  let state = refinementFixture();
  if (refined) state = applyEvent(state, refinementEvent(state)).state;
  else state.items[0].scopes = ['unrelated\\owned.cs'];
  state = applyEvent(state, { id: 'adopt-doc-owner', type: 'adopt', key: proof.key,
    owner: proof.owner, ...proof.reservation, scopes: proof.fromScopes,
    evidence: 'Synthetic mutable documentation input.' }).state;
  proof.exclusivity.reservations = heldReservations(state, proof.key);
  const event = { id: 'extend-doc-owner', type: 'extend-scopes', key: proof.key, owner: proof.owner,
    expectedScopeRevision: proof.scopeRevision, proof,
    acknowledgment: { id: 'ack-doc-owner', receipt: 'Synthetic fresh owner response', owner: proof.owner,
      key: proof.key, scopeRevision: proof.scopeRevision, proofDigest: digest(proof) } };
  const value = JSON.parse(f.files.get(f.file));
  value.state = state;
  value.events = (state.refinements ?? []).map(record => ({ id: record.eventId, type: 'refine-scopes' }));
  f.files.set(f.file, JSON.stringify(value));
  return { ...f, event };
}

for (const refined of [false, true]) {
  test(`extension atomically migrates legacy v${refined ? 2 : 1}, preserves original shrink and requires current writer/lock`, () => {
    const f = amendmentCheckpointFixture(refined);
    const before = f.files.get(f.file);
    const legacy = f.run({ operation: 'read' });
    assert.equal(legacy.state.version, refined ? 2 : 1);
    assert.throws(() => f.run({ operation: 'apply', expectedRevision: 0, event: f.event }), /Stale/);
    f.files.set(`${f.file}.lock`, 'other writer');
    assert.throws(() => f.run({ operation: 'apply', expectedRevision: 1, event: f.event }), /locked/);
    assert.equal(f.files.get(`${f.file}.lock`), 'other writer');
    f.files.delete(`${f.file}.lock`);
    assert.equal(f.files.get(f.file), before);
    const result = f.run({ operation: 'apply', expectedRevision: 1, event: f.event });
    assert.equal(result.revision, 2);
    assert.equal(result.state.version, 3);
    const item = result.state.items.find(value => value.key === f.event.key);
    assert.equal(item.scopeRevision, 1);
    assert.equal(item.sourceWritesRetired, false);
    assert.deepEqual(item.scopes, f.event.proof.toScopes);
    assert.deepEqual(item.scopeHistory, []);
    assert.deepEqual(result.state.items[0].scopeHistory, legacy.state.items[0].scopeHistory ?? []);
    assert.deepEqual(result.state.refinements, legacy.state.refinements ?? []);
    assert.deepEqual(result.state.seen.filter(id => !id.startsWith('scope-amendment:') && id !== f.event.id),
      legacy.state.seen);
    assert.deepEqual(result.actions, legacy.actions);
    assert.deepEqual(f.run({ operation: 'read' }), result);
    const saved = f.files.get(f.file);
    assert.deepEqual(f.run({ operation: 'apply', expectedRevision: 2, event: f.event }), result);
    assert.equal(f.files.get(f.file), saved);
    assert.throws(() => f.run({ operation: 'apply', expectedRevision: 2,
      event: { ...f.event, id: 'new-stale-id' } }), /Stale/);
    assert.equal(f.files.get(f.file), saved);
  });
}

test('amendment checkpoint read/apply reject omission, tamper, replay, downgrade and journal mismatch', () => {
  const f = amendmentCheckpointFixture(true);
  const result = f.run({ operation: 'apply', expectedRevision: 1, event: f.event });
  const changes = [
    v => { delete v.state.amendments; }, v => { v.state.amendments = []; },
    v => { delete v.state.items[1].scopeAmendments; },
    v => { v.state.items[1].scopeAmendments = []; },
    v => { v.state.items[1].scopeAmendments[0].proof.quiescence.paused = false; },
    v => { v.state.items[1].scopeAmendments[0].proof.manifest.actual = []; },
    v => { v.state.items[1].scopeAmendments[0].proof.toScopes.pop(); },
    v => { v.state.items[1].scopeAmendments[0].acknowledgment.id = 'altered'; },
    v => { v.state.items[1].scopeRevision = 0; },
    v => { v.state.items[1].sourceWritesRetired = true; },
    v => { v.state.items[1].scopes.pop(); },
    v => { v.state.version = 2; },
    v => {
      v.state.version = 2;
      delete v.state.amendments;
      for (const item of v.state.items) delete item.scopeAmendments;
    },
    v => { v.events = v.events.filter(event => event.type !== 'extend-scopes'); },
    v => { v.state.seen = v.state.seen.filter(id => id !== f.event.id); },
    v => { v.events.push(v.events.at(-1)); },
    v => { v.state.items[1].scopeAmendments[0].acknowledgment.id =
      v.state.items[0].scopeHistory[0].acknowledgment.id; }
  ];
  for (const change of changes) {
    const invalid = structuredClone(result);
    change(invalid);
    f.files.set(f.file, JSON.stringify(invalid));
    const saved = f.files.get(f.file);
    assert.throws(() => f.run({ operation: 'read' }));
    assert.throws(() => f.run({ operation: 'apply', expectedRevision: 2,
      event: { id: 'pause', type: 'pause' } }));
    assert.equal(f.files.get(f.file), saved);
  }
});

test('failed extension replacement retains original scopes/dirty evidence and emits no continuation', () => {
  const f = amendmentCheckpointFixture();
  const before = f.files.get(f.file);
  f.failReplacement();
  assert.throws(() => f.run({ operation: 'apply', expectedRevision: 1, event: f.event }), /Replacement failed/);
  assert.equal(f.files.get(f.file), before);
  assert.deepEqual([...f.files.keys()], [f.file]);
  const value = f.run({ operation: 'read' });
  assert.equal(value.state.version, 1);
  assert.deepEqual(value.state.items[1].scopes, f.event.proof.fromScopes);
});
