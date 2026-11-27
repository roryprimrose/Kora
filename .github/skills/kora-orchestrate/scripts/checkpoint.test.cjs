'use strict';

const assert = require('node:assert/strict');
const { test } = require('node:test');
const path = require('node:path');
const fs = require('node:fs');
const { checkpoint } = require('./checkpoint.cjs');
const { applyEvent, digest, heldReservations } = require('./registry.cjs');
const { incidentFixture, recoveryEvent, recoveryEvidence, renewalEvent, renewalEvidence,
  continuation } = require('./incident-recovery-fixture.cjs');
const { validateRecoveryContinuation } = require('./registry.cjs');
const { refinementFixture, refinementEvent, tamperedStates,
  candidateFixture, candidateEvent, supersessionFixture, supersessionEvent,
  supersessionBeneficiaryFixture, fencedExtensionFixture, fencedExtensionEvent,
  fencedExtensionContext, fencedSupersessionEvent, fencedSupersessionContext } = require('./refinement-fixture.cjs');
const { createLifecycleState, lifecycleProjection } = require('./registry.cjs');
const { lifecycleInput } = require('./refinement-fixture.cjs');

test('v10 checkpoint binds complete event/host/action history and rejects stale writes, locks and atomic failure', () => {
  const f = fixture();
  f.files.set(f.file, JSON.stringify({ version: 1, revision: 1, events: [], actions: [],
    state: createLifecycleState('synthetic-coordinator', '1'.repeat(40), 'delivery') }));
  function run(command) {
    const value = f.run({ operation: 'read' });
    const input = lifecycleInput(value.state, command);
    return f.run({ operation: 'apply', expectedRevision: value.revision, ...input });
  }
  run({ kind: 'register', key: 'a', outcome: 'SYNTHETIC checkpoint lifecycle', pool: 'ready',
    workKind: 'feature', pr: null, evidence: 'SYNTHETIC admitted outcome.' });
  run({ kind: 'allocate', key: 'a', grantId: 'a', scopes: ['src\\a.cs'], transfers: [] });
  const dispatched = run({ kind: 'dispatch', grantId: 'a' });
  assert.equal(dispatched.actions.length, 1);
  assert.deepEqual(dispatched.actions[0].command,
    { type: 'launch', key: 'a', pool: 'ready', grantId: 'a', grantRevision: 1 });
  assert.deepEqual(f.run({ operation: 'read' }), dispatched);
  const original = f.files.get(f.file);
  const input = lifecycleInput(dispatched.state, { kind: 'ambiguous', grantId: 'a', reason: 'SYNTHETIC unknown send.' });
  assert.throws(() => f.run({ operation: 'apply', expectedRevision: dispatched.revision - 1, ...input }),
    /Stale checkpoint/);
  f.files.set(`${f.file}.lock`, 'other writer');
  assert.throws(() => f.run({ operation: 'apply', expectedRevision: dispatched.revision, ...input }), /locked/);
  f.files.delete(`${f.file}.lock`);
  assert.equal(f.files.get(f.file), original);
  for (const change of [
    v => { v.events.pop(); },
    v => { v.events.at(-1).commands = []; },
    v => { v.actions = []; },
    v => { v.actions[0].command.grantRevision = 0; },
    v => { v.state.history[0].hostEvidence.ledgerAuthorityVerified = false; },
    v => { v.state.projection = { reservations: [] }; },
    v => { v.state.history[0].event.command.evidence = 'rewritten'; }
  ]) {
    const changed = structuredClone(dispatched); change(changed);
    f.files.set(f.file, JSON.stringify(changed));
    assert.throws(() => f.run({ operation: 'read' }));
  }
  f.files.set(f.file, original);
  f.failReplacement();
  assert.throws(() => f.run({ operation: 'apply', expectedRevision: dispatched.revision, ...input }),
    /Replacement failed/);
  assert.equal(f.files.get(f.file), original);
  assert.equal([...f.files.keys()].some(k => k.endsWith('.lock') || k.endsWith('.tmp')), false);
});

test('v10 checkpoint adoption is explicit, preview never writes and the legacy action journal remains unchanged', () => {
  const f = fixture();
  const original = f.files.get(f.file), old = f.run({ operation: 'read' });
  const preview = f.run({ operation: 'preview-lifecycle' });
  assert.equal(preview.activating, false);
  assert.equal(f.files.get(f.file), original);
  const shadow = { version: 10, origin: { coordinator: old.state.coordinator, head: old.state.head,
    mode: old.state.mode, legacy: old.state, legacyDigest: digest(old.state) }, history: [] };
  const input = lifecycleInput(shadow, { kind: 'adopt', legacyDigest: digest(old.state) });
  const adopted = f.run({ operation: 'adopt-lifecycle', expectedRevision: old.revision, ...input });
  assert.deepEqual(adopted.state.origin.legacy, old.state);
  assert.deepEqual(adopted.actions, old.actions);
  assert.deepEqual(f.run({ operation: 'read' }), adopted);
  assert.equal(lifecycleProjection(adopted.state).grants.length, 0);
  const bytes = f.files.get(f.file);
  assert.throws(() => f.run({ operation: 'apply', expectedRevision: adopted.revision, ...input }),
    /cannot be replayed/);
  assert.equal(f.files.get(f.file), bytes);
});

test('v10 checkpoint explicitly initializes without silently changing default legacy initialization', () => {
  const f = fixture();
  assert.equal(f.run({ operation: 'read' }).state.version, 1);
  f.files.delete(f.file);
  const value = f.run({ operation: 'init', expectedRevision: 0, lifecycleVersion: 10,
    coordinator: 'synthetic-new', head: '1'.repeat(40), mode: 'delivery' });
  assert.equal(value.state.version, 10);
  assert.deepEqual(value.events, []);
  assert.deepEqual(value.actions, []);
});

test('v10 explicit unexecuted custody disposition retains atomic failure and no-action journal boundaries', () => {
  const f = fixture();
  function legacy(event) {
    const before = f.run({ operation: 'read' });
    return f.run({ operation: 'apply', expectedRevision: before.revision, event });
  }
  legacy({ id: 'local-main', type: 'refresh', head: '1'.repeat(40) });
  legacy({ id: 'local-holder', type: 'adopt', key: 'prior', owner: 'synthetic-prior',
    outcome: 'SYNTHETIC prior', pool: 'ready', kind: 'feature', head: '1'.repeat(40),
    scopes: ['src\\local.cs'], evidence: 'SYNTHETIC scope.' });
  legacy({ id: 'local-planner', type: 'planner', owner: 'synthetic-planner' });
  let before = legacy({ id: 'local-candidate', type: 'candidate', owner: 'synthetic-planner', pass: 1,
    key: 'successor', outcome: 'SYNTHETIC successor', pool: 'ready', kind: 'maintenance',
    head: '1'.repeat(40), scopes: ['src\\local.cs'], evidence: 'SYNTHETIC operation.' });
  before = legacy(supersessionEvent(before.state, before.revision));
  const shadow = { version: 10, origin: { coordinator: before.state.coordinator, head: before.state.head,
    mode: before.state.mode, legacy: before.state, legacyDigest: digest(before.state) }, history: [] };
  const adoption = lifecycleInput(shadow, { kind: 'adopt', legacyDigest: digest(before.state) });
  const adopted = f.run({ operation: 'adopt-lifecycle', expectedRevision: before.revision, ...adoption });
  const input = lifecycleInput(adopted.state, { kind: 'release-unexecuted', legacyDigest: digest(before.state),
    releases: [{ reservationId: 'legacy:successor', paths: ['src\\local.cs'] }] });
  const bytes = f.files.get(f.file);
  f.failReplacement();
  assert.throws(() => f.run({ operation: 'apply', expectedRevision: adopted.revision, ...input }), /Replacement failed/);
  assert.equal(f.files.get(f.file), bytes);
  f.failReplacement(false);
  const missing = structuredClone(input); missing.lifecycleEvidence.unexecutedCustodyVerified = [];
  assert.throws(() => f.run({ operation: 'apply', expectedRevision: adopted.revision, ...missing }),
    /custody\/preservation readback/);
  assert.equal(f.files.get(f.file), bytes);
  const accepted = f.run({ operation: 'apply', expectedRevision: adopted.revision, ...input });
  assert.deepEqual(accepted.actions, adopted.actions);
  assert.deepEqual(accepted.events.at(-1).commands, []);
  assert.deepEqual(f.run({ operation: 'read' }), accepted);
  const finalBytes = f.files.get(f.file);
  assert.throws(() => f.run({ operation: 'apply', expectedRevision: accepted.revision, ...input }), /cannot be replayed/);
  assert.equal(f.files.get(f.file), finalBytes);
});

test('v10 adoption preserves nonempty legacy publications and pending journal actions byte-for-byte', () => {
  const f = fixture();
  function legacy(event) {
    const old = f.run({ operation: 'read' });
    return f.run({ operation: 'apply', expectedRevision: old.revision, event });
  }
  legacy({ id: 'legacy-planner', type: 'planner', owner: 'synthetic-planner' });
  legacy({ id: 'legacy-main', type: 'refresh', head: '1'.repeat(40) });
  legacy({ id: 'legacy-owner', type: 'adopt', key: 'a', owner: 'synthetic-owner',
    outcome: 'SYNTHETIC legacy operation', pool: 'ready', kind: 'feature', head: '1'.repeat(40),
    scopes: ['src\\a.cs'], evidence: 'SYNTHETIC exclusive source.' });
  const old = legacy({ id: 'legacy-published', type: 'published', key: 'a', owner: 'synthetic-owner',
    proof: { pushedHead: '2'.repeat(40), remoteHead: '2'.repeat(40), pr: 94, ready: true, enrolled: true } });
  assert.ok(old.actions.some(a => a.status === 'pending'));
  const shadow = { version: 10, origin: { coordinator: old.state.coordinator, head: old.state.head,
    mode: old.state.mode, legacy: old.state, legacyDigest: digest(old.state) }, history: [] };
  const input = lifecycleInput(shadow, { kind: 'adopt', legacyDigest: digest(old.state) });
  const bytes = f.files.get(f.file);
  const missing = { ...input, lifecycleEvidence: undefined };
  assert.throws(() => f.run({ operation: 'adopt-lifecycle', expectedRevision: old.revision, ...missing }));
  assert.equal(f.files.get(f.file), bytes);
  const adopted = f.run({ operation: 'adopt-lifecycle', expectedRevision: old.revision, ...input });
  assert.equal(JSON.stringify(adopted.state.origin.legacy), JSON.stringify(old.state));
  assert.equal(JSON.stringify(adopted.events.slice(0, -1)), JSON.stringify(old.events));
  assert.equal(JSON.stringify(adopted.actions), JSON.stringify(old.actions));
  assert.equal(lifecycleProjection(adopted.state).grants[0].status, 'unresolved');
  assert.deepEqual(f.run({ operation: 'read' }), adopted);
});

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
  return { files, file, run, failReplacement: (fail = true) => { failRename = fail; } };
}

function candidateCheckpoint(released = true) {
  const f = fixture();
  const state = candidateFixture(released);
  f.files.set(f.file, JSON.stringify({ version: 1, revision: 107, state, actions: [],
    events: [...state.refinements.map(record => ({ id: record.eventId, type: 'refine-scopes' })),
      ...state.amendments.map(record => ({ id: record.eventId, type: 'extend-scopes' }))] }));
  return f;
}

function replacementInput(value, type) {
  const { owner, pass } = value.state.planner;
  const event = { id: type, type, oldOwner: owner, pass, checkpointRevision: value.revision,
    oldProof: { owner, pass, status: 'error', stoppedWriting: true, pendingInstructions: 0,
      receipt: 'SYNTHETIC stopped planner and consumed authority.' } };
  if (type === 'reserve-planner-replacement') {
    event.authorization = { id: 'decision', actor: 'SYNTHETIC user', userAuthorized: true,
      oldOwner: owner, pass, checkpointRevision: value.revision, receipt: 'SYNTHETIC explicit authorization.' };
  } else {
    Object.assign(event, { owner: 'fixture-replacement', reservationId: 'reserve-planner-replacement',
      standbyProof: { owner: 'fixture-replacement', reservationId: 'reserve-planner-replacement',
        scanning: false, productionWrites: false, pendingInstructions: 0, receipt: 'SYNTHETIC standby.' } });
  }
  return { operation: 'apply', expectedRevision: value.revision, event,
    plannerReplacementEvidence: { kind: 'host-verified-planner-replacement', eventDigest: digest(event),
      coordinator: value.state.coordinator, ledgerAuthorityVerified: true, authorityReconciled: true,
      userAuthorizationVerified: true, oldPlannerVerified: true, standbyVerified: true,
      receipt: 'SYNTHETIC independent host proof.' } };
}

test('checkpoint planner replacement reconciles pending old actions and standby send, binds history and preserves atomic replay', () => {
  const f = fixture();
  let value = f.run({ operation: 'apply', expectedRevision: 1,
    event: { id: 'planner', type: 'planner', owner: 'fixture-planner' } });
  let bytes = f.files.get(f.file);
  assert.throws(() => f.run(replacementInput(value, 'reserve-planner-replacement')), /pending old-planner/);
  assert.equal(f.files.get(f.file), bytes);
  value = f.run({ operation: 'ack', expectedRevision: value.revision, actionId: 'planner:0',
    status: 'blocked', evidence: 'SYNTHETIC verified terminal rejection, no pending authority.' });
  const reserve = replacementInput(value, 'reserve-planner-replacement');
  f.failReplacement();
  bytes = f.files.get(f.file);
  assert.throws(() => f.run(reserve), /Replacement failed/);
  assert.equal(f.files.get(f.file), bytes);
  f.failReplacement(false);
  value = f.run(reserve);
  assert.deepEqual(f.run({ ...reserve, expectedRevision: value.revision }), value);
  bytes = f.files.get(f.file);
  assert.throws(() => f.run(replacementInput(value, 'replace-planner')), /initialization action/);
  assert.equal(f.files.get(f.file), bytes);
  value = f.run({ operation: 'ack', expectedRevision: value.revision,
    actionId: 'reserve-planner-replacement:0', status: 'done', evidence: 'SYNTHETIC exact standby host readback.' });
  const attach = replacementInput(value, 'replace-planner');
  bytes = f.files.get(f.file);
  const pending = structuredClone(value);
  pending.actions.push({ id: 'old-pause:0', status: 'pending',
    command: { type: 'pause', owner: 'fixture-planner' } });
  f.files.set(f.file, JSON.stringify(pending));
  assert.throws(() => f.run(attach), /pending old-planner/);
  f.files.set(f.file, bytes);
  assert.throws(() => f.run({ ...attach, expectedRevision: value.revision - 1 }), /Stale checkpoint/);
  f.files.set(`${f.file}.lock`, 'other writer');
  assert.throws(() => f.run(attach), /locked/);
  f.files.delete(`${f.file}.lock`);
  value = f.run(attach);
  assert.deepEqual(value.actions.at(-1).command, { type: 'discover', owner: 'fixture-replacement',
    capacity: { ready: 10, repair: 4 }, examine: ['ready', 'repair'], head: null, pass: 2 });
  bytes = f.files.get(f.file);
  assert.deepEqual(f.run({ ...attach, expectedRevision: value.revision }), value);
  assert.equal(f.files.get(f.file), bytes);
  for (const mutate of [
    v => { v.events.pop(); },
    v => { v.events.at(-1).digest = 'wrong'; },
    v => { v.actions.pop(); },
    v => { v.actions.at(-1).command.pass = 1; },
    v => { v.events.at(-1).commands[0].owner = 'wrong'; },
    v => { v.events.at(-1).commands[0].head = '9'.repeat(40); v.actions.at(-1).command.head = '9'.repeat(40); },
    v => { delete v.state.planner.replacements; },
    v => { v.state.planner.replacements[0].replacement.hostEvidence.oldPlannerVerified = false; }
  ]) {
    const changed = structuredClone(value); mutate(changed);
    f.files.set(f.file, JSON.stringify(changed));
    assert.throws(() => f.run({ operation: 'read' }));
  }
  f.files.set(f.file, bytes);
  assert.deepEqual(f.run({ operation: 'read' }), value);
});

test('checkpoint persists terminal frontier and queued actions; invalid or duplicate reports preserve bytes', () => {
  const f = fixture();
  const attached = f.run({ operation: 'apply', expectedRevision: 1,
    event: { id: 'planner', type: 'planner', owner: 'fixture-planner' } });
  const event = { id: 'end', type: 'pass-ended', owner: 'fixture-planner', pass: 1,
    readyExamined: true, repairExamined: true, remaining: true,
    inventoryComplete: true, remainingFrontier: ['session conversation'] };
  const bytes = f.files.get(f.file);
  assert.throws(() => f.run({ operation: 'apply', expectedRevision: attached.revision,
    event: { ...event, remaining: false } }), /Remaining/);
  assert.equal(f.files.get(f.file), bytes);
  const ended = f.run({ operation: 'apply', expectedRevision: attached.revision, event });
  assert.equal(ended.version, 1);
  assert.equal(ended.state.version, 1);
  assert.deepEqual(ended.state.planner.frontier,
    { inventoryComplete: true, remainingFrontier: ['session conversation'] });
  assert.deepEqual(ended.actions.at(-1).command.frontier, ended.state.planner.frontier);
  assert.deepEqual(f.run({ operation: 'read' }), ended);
  const persisted = f.files.get(f.file);
  assert.deepEqual(f.run({ operation: 'apply', expectedRevision: ended.revision, event }), ended);
  assert.equal(f.files.get(f.file), persisted);
  assert.equal([...f.files.keys()].some(file => file.endsWith('.lock') || file.endsWith('.tmp')), false);
});

test('saved candidate-readmission checkpoint reads and replays seen terminal events without rewriting', () => {
  const f = candidateCheckpoint();
  const bytes = f.files.get(f.file), original = f.run({ operation: 'read' });
  assert.equal(Object.hasOwn(original.state.planner, 'frontier'), false);
  for (const id of original.state.seen.filter(id => id.startsWith('planner-pass-ended-'))) {
    assert.deepEqual(f.run({ operation: 'apply', expectedRevision: original.revision,
      event: { id, type: 'pass-ended', owner: original.state.planner.owner, pass: 1,
        readyExamined: true, repairExamined: true, remaining: false } }), original);
    assert.equal(f.files.get(f.file), bytes);
  }
});

function supersessionCheckpoint() {
  const f = fixture();
  f.files.set(f.file, JSON.stringify({ version: 1, revision: 41,
    state: supersessionFixture(), actions: [], events: [] }));
  return f;
}

function incidentCheckpoint() {
  const f = fixture();
  f.files.set(f.file, JSON.stringify({ version: 1, revision: 41,
    state: incidentFixture(), actions: [], events: [] }));
  return f;
}

test('UNKNOWN holder handoff roundtrips complete history and no-action journal; tamper/replay never replaces bytes', () => {
  const f = fixture();
  f.files.set(f.file, JSON.stringify({ version: 1, revision: 41,
    state: supersessionFixture(), events: [], actions: [] }));
  const event = fencedSupersessionEvent(f.run({ operation: 'read' }).state);
  const evidence = fencedSupersessionContext(event).supersessionEvidence;
  const accepted = f.run({ operation: 'apply', expectedRevision: 41, event, supersessionEvidence: evidence });
  assert.equal(accepted.revision, 42);
  assert.equal(accepted.state.version, 9);
  assert.deepEqual(accepted.actions, []);
  assert.deepEqual(accepted.events.at(-1).commands, []);
  assert.equal(accepted.events.at(-1).digest, digest(accepted.state.supersessions[0]));
  assert.deepEqual(f.run({ operation: 'read' }), accepted);
  const original = f.files.get(f.file);
  assert.throws(() => f.run({ operation: 'apply', expectedRevision: 42, event,
    supersessionEvidence: evidence }), /cannot be replayed/);
  assert.equal(f.files.get(f.file), original);
  for (const change of [
    v => { v.state.version = 8; },
    v => { v.state.supersessions[0].before.items[0].publication.remoteHead = '0'.repeat(40); },
    v => { delete v.state.supersessions[0].hostEvidence; },
    v => { v.state.supersessions[0].hostEvidence.holders[1].commandsJoinedVerified = false; },
    v => { v.state.supersessions[0].event.proof.holders[0].reconciliation.pendingInstructions = 0; },
    v => { v.events[0].digest = '0'.repeat(64); },
    v => { v.events = []; },
    v => { delete v.events[0].commands; },
    v => {
      const command = { type: 'launch', key: 'successor', pool: 'ready' };
      v.events[0].commands = [command];
      v.state.supersessions[0].commands = [command];
      v.actions.push({ id: `${event.id}:0`, status: 'pending', command });
    },
    v => { v.state.supersessions = []; }
  ]) {
    const altered = structuredClone(accepted); change(altered);
    const bytes = JSON.stringify(altered); f.files.set(f.file, bytes);
    assert.throws(() => f.run({ operation: 'read' }));
    assert.throws(() => f.run({ operation: 'apply', expectedRevision: 42, event: { id: 'later', type: 'pause' } }));
    assert.equal(f.files.get(f.file), bytes);
  }
});

test('UNKNOWN handoff stale writer, missing independent proof, contention and failed replacement retain original bytes', () => {
  for (const failure of ['revision', 'evidence', 'lock', 'replace']) {
    const f = fixture();
    f.files.set(f.file, JSON.stringify({ version: 1, revision: 41,
      state: supersessionFixture(), events: [], actions: [] }));
    const event = fencedSupersessionEvent(f.run({ operation: 'read' }).state);
    const original = f.files.get(f.file);
    if (failure === 'lock') f.files.set(`${f.file}.lock`, 'other-writer');
    if (failure === 'replace') f.failReplacement();
    assert.throws(() => f.run({ operation: 'apply', expectedRevision: failure === 'revision' ? 40 : 41,
      event, ...(failure === 'evidence' ? {} : {
        supersessionEvidence: fencedSupersessionContext(event).supersessionEvidence }) }));
    assert.equal(f.files.get(f.file), original);
    assert.deepEqual([...f.files.keys()].sort(),
      (failure === 'lock' ? [f.file, `${f.file}.lock`] : [f.file]).sort());
  }
});

test('fenced mutable checkpoint binds actual authority history and no actions, rejects replay and journal tampering', () => {
  const f = fixture();
  f.files.set(f.file, JSON.stringify({ version: 1, revision: 41,
    state: fencedExtensionFixture(), events: [], actions: [] }));
  const event = fencedExtensionEvent(f.run({ operation: 'read' }).state);
  const accepted = f.run({ operation: 'apply', expectedRevision: 41, event,
    extensionEvidence: fencedExtensionContext(event).extensionEvidence });
  assert.equal(accepted.revision, 42);
  assert.deepEqual(accepted.actions, []);
  assert.equal(accepted.events.at(-1).digest, digest(accepted.state.items[0].scopeAmendments[0]));
  assert.deepEqual(accepted.events.at(-1).commands, []);
  const original = f.files.get(f.file);
  assert.throws(() => f.run({ operation: 'apply', expectedRevision: 42, event,
    extensionEvidence: fencedExtensionContext(event).extensionEvidence }), /cannot be replayed/);
  assert.equal(f.files.get(f.file), original);
  for (const change of [
    v => { v.events[0].digest = '0'.repeat(64); }, v => { v.events[0].commands = [{ type: 'resume' }]; },
    v => { v.actions.push({ id: `${event.id}:0`, status: 'pending', command: { type: 'resume' } }); },
    v => { v.state.version = 7; }, v => { v.state.items[0].scopeAmendments[0].authorization.id = 'forged'; },
    v => { v.state.items[0].scopeAmendments[0].hostEvidence.instructionAuthorityVerified = false; },
    v => { v.events = []; }
  ]) {
    const altered = structuredClone(accepted); change(altered); const bytes = JSON.stringify(altered);
    f.files.set(f.file, bytes);
    assert.throws(() => f.run({ operation: 'read' }));
    assert.throws(() => f.run({ operation: 'apply', expectedRevision: 42, event: { id: 'later', type: 'pause' } }));
    assert.equal(f.files.get(f.file), bytes);
  }
});

test('fenced extension stale writer, missing readback, contention and failed replacement retain exact original bytes', () => {
  for (const failure of ['revision', 'lock', 'evidence', 'replace']) {
    const f = fixture();
    f.files.set(f.file, JSON.stringify({ version: 1, revision: 41,
      state: fencedExtensionFixture(), events: [], actions: [] }));
    const event = fencedExtensionEvent(f.run({ operation: 'read' }).state);
    const original = f.files.get(f.file);
    if (failure === 'lock') f.files.set(`${f.file}.lock`, 'other-writer');
    if (failure === 'replace') f.failReplacement();
    assert.throws(() => f.run({ operation: 'apply', expectedRevision: failure === 'revision' ? 40 : 41,
      event, ...(failure === 'evidence' ? {} : { extensionEvidence: fencedExtensionContext(event).extensionEvidence }) }));
    assert.equal(f.files.get(f.file), original);
    assert.deepEqual([...f.files.keys()].sort(),
      (failure === 'lock' ? [f.file, `${f.file}.lock`] : [f.file]).sort());
  }
});

function renewalCheckpoint() {
  const f = incidentCheckpoint();
  const event = recoveryEvent(f.run({ operation: 'read' }).state);
  f.run({ operation: 'apply', expectedRevision: 41, event, recoveryEvidence: recoveryEvidence(event) });
  f.run({ operation: 'apply', expectedRevision: 42, event: {
    id: 'renewal-source-refresh', type: 'refresh', head: '2'.repeat(40) } });
  return f;
}

test('checkpoint renews only after current host/owner compatibility proof and durably binds no-action history', () => {
  const f = renewalCheckpoint();
  const original = f.run({ operation: 'read' });
  const event = renewalEvent(original.state);
  const value = f.run({ operation: 'apply', expectedRevision: 43, event,
    renewalEvidence: renewalEvidence(event) });
  assert.equal(value.revision, 44);
  assert.deepEqual(value.state.items, original.state.items);
  assert.deepEqual(value.state.recoveries, original.state.recoveries);
  assert.deepEqual(value.actions, original.actions);
  assert.equal(value.events.at(-1).digest, digest(value.state.recoveryRenewals[0]));
  assert.deepEqual(value.events.at(-1).commands, []);
  assert.equal(validateRecoveryContinuation(value.state, continuation(value.state, 44, true),
    { revision: 44 }).sourceExecutionAuthorized, false);
  const bytes = f.files.get(f.file);
  for (const replay of [event, { ...event, type: 'pause' }]) {
    assert.throws(() => f.run({ operation: 'apply', expectedRevision: 44, event: replay,
      renewalEvidence: renewalEvidence(event) }), /cannot be replayed/);
    assert.equal(f.files.get(f.file), bytes);
  }
});

test('renewal stale writer, missing host compatibility, lock and atomic replacement failures preserve original bytes', () => {
  for (const failure of ['lock', 'revision', 'proof-revision', 'host-evidence', 'replace']) {
    const f = renewalCheckpoint();
    const event = renewalEvent(f.run({ operation: 'read' }).state, failure === 'proof-revision' ? 42 : 43);
    const before = f.files.get(f.file);
    if (failure === 'lock') f.files.set(`${f.file}.lock`, 'different owner');
    if (failure === 'replace') f.failReplacement();
    assert.throws(() => f.run({ operation: 'apply', expectedRevision: failure === 'revision' ? 42 : 43,
      event, ...(failure === 'host-evidence' ? {} : { renewalEvidence: renewalEvidence(event) }) }));
    assert.equal(f.files.get(f.file), before);
    assert.deepEqual([...f.files.keys()].sort(),
      (failure === 'lock' ? [f.file, `${f.file}.lock`] : [f.file]).sort());
  }
});

test('renewal checkpoint refuses journal tamper, omitted/downgraded history and fabricated continuation actions', () => {
  for (const change of [
    v => { v.events.pop(); },
    v => { v.events.at(-1).digest = '0'.repeat(64); },
    v => { v.events.at(-1).commands = [{ type: 'resume' }]; },
    v => { v.actions.push({ id: 'synthetic-renewal-43:0', status: 'pending', command: { type: 'resume' } }); },
    v => { v.state.version = 6; },
    v => { v.state.recoveryRenewals = []; },
    v => { v.state.recoveryRenewals[0].hostEvidence.compatibilityVerified = false; },
    v => { v.state.recoveryRenewals[0].event.proof.source.head = '3'.repeat(40); },
    v => { v.state.recoveryRenewals[0].event.acknowledgment.receipt = 'forged'; },
    v => { v.state.recoveries[0].event.proof.head = '3'.repeat(40); }
  ]) {
    const f = renewalCheckpoint();
    const event = renewalEvent(f.run({ operation: 'read' }).state);
    const value = f.run({ operation: 'apply', expectedRevision: 43, event,
      renewalEvidence: renewalEvidence(event) });
    change(value);
    f.files.set(f.file, JSON.stringify(value));
    const before = f.files.get(f.file);
    assert.throws(() => f.run({ operation: 'read' }));
    assert.throws(() => f.run({ operation: 'apply', expectedRevision: 44,
      event: { id: 'pause-corrupt-renewal', type: 'pause' } }));
    assert.equal(f.files.get(f.file), before);
  }
});

test('incident recovery durably records whole event and host evidence, no actions, exact replay only', () => {
  const f = incidentCheckpoint();
  const event = recoveryEvent(f.run({ operation: 'read' }).state);
  const value = f.run({ operation: 'apply', expectedRevision: 41, event,
    recoveryEvidence: recoveryEvidence(event) });
  assert.equal(value.revision, 42);
  assert.deepEqual(value.actions, []);
  assert.deepEqual(value.events[0].commands, []);
  assert.equal(value.events[0].digest, digest(value.state.recoveries[0]));
  const bytes = f.files.get(f.file);
  f.run({ operation: 'apply', expectedRevision: 42, event });
  assert.equal(f.files.get(f.file), bytes);
  assert.throws(() => f.run({ operation: 'apply', expectedRevision: 42,
    event: { ...event, type: 'pause' } }), /replay/);
  assert.equal(f.files.get(f.file), bytes);
});

test('incident lock contention, stale writer/proof, host readback and failed replacement preserve exact bytes', () => {
  for (const failure of ['lock', 'revision', 'proof-revision', 'host-evidence', 'replace']) {
    const f = incidentCheckpoint();
    const event = recoveryEvent(f.run({ operation: 'read' }).state,
      failure === 'proof-revision' ? 40 : 41);
    const before = f.files.get(f.file);
    if (failure === 'lock') f.files.set(`${f.file}.lock`, 'other live writer');
    if (failure === 'replace') f.failReplacement();
    assert.throws(() => f.run({ operation: 'apply', expectedRevision: failure === 'revision' ? 40 : 41,
      event, ...(failure === 'host-evidence' ? {} : { recoveryEvidence: recoveryEvidence(event) }) }));
    assert.equal(f.files.get(f.file), before);
    assert.deepEqual([...f.files.keys()].sort(),
      (failure === 'lock' ? [f.file, `${f.file}.lock`] : [f.file]).sort());
  }
});

test('incident checkpoint rejects omitted or forged event, history, downgrade and implicit continuation journal', () => {
  for (const change of [
    v => { v.events = []; },
    v => { v.events[0].digest = '0'.repeat(64); },
    v => { v.events[0].commands = [{ type: 'resume' }]; },
    v => { v.actions.push({ id: 'recover-synthetic-incident:0', status: 'pending', command: { type: 'resume' } }); },
    v => { v.state.version = 5; },
    v => { v.state.items[0].scopeRecoveries = []; },
    v => { v.state.recoveries[0].event.proof.incident.command.command = 'altered'; },
    v => { v.state.recoveries[0].hostEvidence.humanDecisionId = 'forged'; },
    v => { v.state.recoveries = []; }
  ]) {
    const f = incidentCheckpoint();
    const event = recoveryEvent(f.run({ operation: 'read' }).state);
    const value = f.run({ operation: 'apply', expectedRevision: 41, event,
      recoveryEvidence: recoveryEvidence(event) });
    change(value);
    f.files.set(f.file, JSON.stringify(value));
    const before = f.files.get(f.file);
    assert.throws(() => f.run({ operation: 'read' }));
    assert.throws(() => f.run({ operation: 'apply', expectedRevision: 42,
      event: { id: 'pause-invalid', type: 'pause' } }));
    assert.equal(f.files.get(f.file), before);
  }
});

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
