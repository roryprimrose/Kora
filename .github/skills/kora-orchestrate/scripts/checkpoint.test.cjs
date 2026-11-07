'use strict';

const assert = require('node:assert/strict');
const { test } = require('node:test');
const path = require('node:path');
const { checkpoint } = require('./checkpoint.cjs');
const { refinementFixture, refinementEvent, tamperedStates } = require('./refinement-fixture.cjs');

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
