'use strict';

const assert = require('node:assert/strict');
const { test } = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const { createState, applyEvent, validate, digest, heldReservations, supersededWriter,
  supersessionAcknowledgmentDigest, validateRecoveryContinuation, recoveryContinuationDigest,
  validateScopeExtensionContinuation } = require('./registry.cjs');
const { incidentFixture, recoveryEvent, rebindRecovery, recoveryContext, continuation,
  paths: incidentPaths, renewalFixture, renewalEvent, renewalContext, rebindRenewal } = require('./incident-recovery-fixture.cjs');
const { refinementFixture, refinementEvent, rebind, tamperedStates,
  candidateFixture, candidateEvent, supersessionFixture, supersessionEvent,
  rebindSupersession,   supersessionBeneficiaryFixture, fencedExtensionFixture, fencedExtensionEvent, rebindFencedExtension,
  fencedExtensionContext, fencedExtensionContinuation, fencedSupersessionEvent,
  rebindFencedSupersession, fencedSupersessionContext } = require('./refinement-fixture.cjs');

const sha = number => number.toString(16).padStart(40, '0');
const coordinator = 'agent-host-session://coordinator';
const planner = 'agent-host-session://coordinator?chat=planner';
const owner = key => `agent-host-session://coordinator?chat=${key}`;

const { createLifecycleState, lifecycleProjection, lifecycleRevisions, lifecycleProofDigest,
  previewLifecycleMigration, adoptLifecycle, validateSourceGrant } = require('./registry.cjs');
const { lifecycleFixture, lifecycleInput } = require('./refinement-fixture.cjs');

function lifecycleRegister(f, key, scopes, transfers = [], start = true) {
  f.run({ kind: 'register', key, outcome: `SYNTHETIC complete ${key}`, pool: 'ready',
    workKind: 'feature', evidence: 'SYNTHETIC admitted complete operation.', pr: null });
  f.run({ kind: 'allocate', key, grantId: key, scopes, transfers });
  if (start) {
    f.run({ kind: 'dispatch', grantId: key });
    f.run({ kind: 'attach', grantId: key, owner: `synthetic-owner-${key}`,
      source: { worktree: `Q:\\Synthetic\\${key}`, branch: `synthetic/${key}`, head: '1'.repeat(40) } });
  }
}

function lifecycleRequest(state, id) {
  const p = lifecycleProjection(state), g = p.grants.find(g => g.id === id);
  return { grantId: id, grantRevision: g.revision, owner: g.owner, source: g.source,
    head: p.head, paths: [g.scopes[0]] };
}

function lifecycleClose(f, id, pr, releases = []) {
  f.run({ kind: 'publish', grantId: id, publication: { pushedHead: '2'.repeat(40),
    remoteHead: '2'.repeat(40), pr, ready: true, enrolled: true, receipt: 'SYNTHETIC remote/PR readback.' } });
  f.run({ kind: 'revoke', grantId: id, reason: 'SYNTHETIC source stop and join.' });
  return f.run({ kind: 'close', key: id, outcome: 'delivered', releases });
}

function lifecycleRebind(input) {
  const binding = lifecycleProofDigest(input.event);
  if (input.event.evidence.human) input.event.evidence.human.proofDigest = binding;
  input.event.evidence.holders.forEach(h => { h.acknowledgment.proofDigest = binding; });
  input.lifecycleEvidence.eventDigest = digest(input.event);
  return input;
}

test('v10 correction binds preserved source/publication refs and rejects newly unadmitted write facts', () => {
  const f = lifecycleFixture();
  lifecycleRegister(f, 'a', ['src\\a.cs']);
  f.run({ kind: 'publish', grantId: 'a', publication: { pushedHead: '2'.repeat(40),
    remoteHead: '2'.repeat(40), pr: 111, ready: true, enrolled: true, receipt: 'SYNTHETIC publication.' } });
  const command = { kind: 'revoke', grantId: 'a', reason: 'SYNTHETIC joined.' };
  const original = JSON.stringify(f.state);
  for (const mutate of [
    m => { m.commits = [{ head: '9'.repeat(40), ref: 'refs/heads/unrelated' }]; },
    m => { m.commits = m.commits.filter(c => c.head !== '2'.repeat(40)); },
    m => { m.commits[0].ref = 'not-a-ref'; },
    m => { m.commits.push({ head: '8'.repeat(40), ref: m.commits[0].ref }); },
    m => { m.actual.push('outside\\unauthorized.cs'); m.retained.push('outside\\unauthorized.cs'); },
    m => { m.planned.push('outside\\unapproved.cs'); }
  ]) {
    const input = lifecycleInput(f.state, command);
    mutate(input.event.evidence.holders[0].preservation);
    assert.throws(() => applyEvent(f.state, lifecycleRebind(input).event, input));
    assert.equal(JSON.stringify(f.state), original);
  }
  const dirty = lifecycleInput(f.state, command, 'preserve-dirty');
  Object.assign(dirty.event.evidence.holders[0].preservation, {
    actual: ['src\\a.cs'], planned: [], committed: [], restored: [], retained: ['src']
  });
  const revoked = applyEvent(f.state, lifecycleRebind(dirty).event, dirty).state;
  const close = lifecycleInput(revoked, { kind: 'close', key: 'a', outcome: 'delivered', releases: [] });
  close.event.evidence.holders[0].preservation.commits[0].ref = 'refs/heads/replacement';
  assert.throws(() => applyEvent(revoked, lifecycleRebind(close).event, close), /anchor/);
  const unwritten = lifecycleFixture();
  lifecycleRegister(unwritten, 'unwritten', ['src\\unwritten.cs'], [], false);
  const stop = lifecycleInput(unwritten.state, { kind: 'revoke', grantId: 'unwritten', reason: 'SYNTHETIC never launched.' });
  Object.assign(stop.event.evidence.holders[0].preservation, {
    actual: [], planned: [], committed: [], restored: [], retained: [], commits: []
  });
  assert.doesNotThrow(() => applyEvent(unwritten.state, lifecycleRebind(stop).event, stop));
});

test('v10 correction releases only independently verified local never-executed custody with real predecessor proof', () => {
  let old = createState('SYNTHETIC local', '1'.repeat(40), 'delivery');
  old = applyEvent(old, { id: 'old-holder', type: 'adopt', owner: 'SYNTHETIC old-holder',
    key: 'prior', outcome: 'SYNTHETIC prior', pool: 'ready', kind: 'feature',
    head: old.head, scopes: ['src\\local.cs'], evidence: 'SYNTHETIC admission.' }).state;
  old = applyEvent(old, { id: 'old-planner', type: 'planner', owner: 'SYNTHETIC planner' }).state;
  old = applyEvent(old, { id: 'old-candidate', type: 'candidate', owner: 'SYNTHETIC planner', pass: 1,
    key: 'successor', outcome: 'SYNTHETIC successor', pool: 'ready', kind: 'maintenance',
    head: old.head, scopes: ['src\\local.cs'], evidence: 'SYNTHETIC complete operation.' }).state;
  old = applyEvent(old, supersessionEvent(old), { revision: 41 }).state;
  const shadow = { version: 10, origin: { coordinator: old.coordinator, head: old.head, mode: old.mode,
    legacy: old, legacyDigest: digest(old) }, history: [] };
  const adoption = lifecycleInput(shadow, { kind: 'adopt', legacyDigest: digest(old) });
  const state = adoptLifecycle(old, adoption.event, adoption).state;
  const command = { kind: 'release-unexecuted', legacyDigest: digest(old),
    releases: [{ reservationId: 'legacy:successor', paths: ['src\\local.cs'] }] };
  const input = lifecycleInput(state, command);
  assert.deepEqual(input.event.evidence.holders.map(h => h.grantId), ['legacy:prior']);
  const result = applyEvent(state, input.event, input);
  assert.deepEqual(result.actions, []);
  assert.equal(lifecycleProjection(result.state).reservations.length, 0);
  assert.deepEqual(lifecycleProjection(result.state).grants, lifecycleProjection(state).grants);
  assert.equal(lifecycleProjection(result.state).works.find(w => w.key === 'successor').status, 'candidate');
  for (const mutate of [
    i => { i.lifecycleEvidence.unexecutedCustodyVerified = []; },
    i => { i.lifecycleEvidence.preservationVerified = false; },
    i => { i.event.evidence.holders = []; i.lifecycleEvidence.holdersVerified = []; },
    i => { i.event.command.legacyDigest = '0'.repeat(64); },
    i => { i.event.command.releases[0].reservationId = 'legacy:prior'; }
  ]) {
    const bad = structuredClone(input); mutate(bad);
    assert.throws(() => applyEvent(state, lifecycleRebind(bad).event, bad));
  }
  assert.throws(() => lifecycleInput(state, { ...command,
    releases: [{ reservationId: 'external::unknown', paths: ['src\\local.cs'] }] }));
});

test('v10 A-to-B-to-C closes retained work without cleanup, transfers custody and rejects every old grant', () => {
  const f = lifecycleFixture(), paths = ['src\\owned.cs'];
  const requests = [];
  for (const [index, key] of ['a', 'b', 'c'].entries()) {
    const transfers = index ? [{ reservationId: `grant:${['a', 'b'][index - 1]}`, paths }] : [];
    lifecycleRegister(f, key, paths, transfers);
    requests.push(lifecycleRequest(f.state, key));
    assert.equal(validateSourceGrant(f.state, requests.at(-1),
      { sourceAuthorityVerified: true, receipt: 'SYNTHETIC exact current host grant.' }).valid, true);
    if (key !== 'c') {
      const before = f.state.history.length;
      const result = lifecycleClose(f, key, 100 + index);
      assert.deepEqual(result.actions, []);
      assert.equal(result.state.history.length, before + 3);
      const p = lifecycleProjection(f.state);
      assert.equal(p.works.find(w => w.key === key).cleanup, null);
      assert.equal(p.capacity.ready, 10);
      assert.deepEqual(p.reservations.map(r => r.scopes), [paths]);
    }
    for (const old of requests.slice(0, -1)) assert.throws(() =>
      validateSourceGrant(f.state, old, { sourceAuthorityVerified: true, receipt: 'SYNTHETIC stale host claim.' }));
  }
  const p = lifecycleProjection(f.state);
  assert.deepEqual(p.grants.map(g => g.status), ['revoked', 'revoked', 'active']);
  assert.deepEqual(p.works.map(w => w.status), ['delivered', 'delivered', 'open']);
  assert.deepEqual(heldReservations(f.state).map(r => r.scopes), [paths]);
  assert.equal(p.capacity.ready, 9);
  assert.equal(f.state.history.some(r => Object.hasOwn(r, 'before')), false);
  assert.equal(f.state.origin.legacy, null);
});

test('v10 partial and multi-holder transfer retires entire source grants and explicitly disposes remainders', () => {
  const f = lifecycleFixture();
  lifecycleRegister(f, 'a', ['src\\a.cs', 'docs\\retained.md']);
  lifecycleRegister(f, 'b', ['src\\b.cs']);
  const a = lifecycleRequest(f.state, 'a'), b = lifecycleRequest(f.state, 'b');
  lifecycleRegister(f, 'c', ['src\\a.cs', 'src\\b.cs'], [
    { reservationId: 'grant:a', paths: ['src\\a.cs'] },
    { reservationId: 'grant:b', paths: ['src\\b.cs'] }
  ]);
  let p = lifecycleProjection(f.state);
  assert.deepEqual(p.reservations.find(r => r.id === 'grant:a').scopes, ['docs\\retained.md']);
  for (const request of [a, b]) assert.throws(() => validateSourceGrant(f.state, request,
    { sourceAuthorityVerified: true, receipt: 'SYNTHETIC stale instruction.' }));
  assert.throws(() => f.run({ kind: 'allocate', key: 'a', grantId: 'a', scopes: ['docs\\retained.md'],
    transfers: [] }), /Fresh bounded/);
  assert.deepEqual(f.run({ kind: 'release',
    releases: [{ reservationId: 'grant:a', paths: ['docs\\retained.md'] }] }).actions, []);
  p = lifecycleProjection(f.state);
  assert.equal(p.reservations.some(r => r.key === 'a'), false);
  assert.equal(p.works.find(w => w.key === 'a').status, 'open');
  assert.equal(p.capacity.ready, 7);
});

test('v10 same-PR repair uses a fresh generation and independent repair capacity, not phase resurrection', () => {
  const f = lifecycleFixture();
  lifecycleRegister(f, 'a', ['src\\a.cs']);
  lifecycleClose(f, 'a', 55, [{ reservationId: 'grant:a', paths: ['src\\a.cs'] }]);
  const original = structuredClone(f.state.history);
  f.run({ kind: 'repair', key: 'a', pr: 55, grantId: 'repair-a', scopes: ['src\\a.cs'], transfers: [] });
  let p = lifecycleProjection(f.state);
  assert.equal(p.capacity.repair, 3);
  assert.equal(p.capacity.ready, 10);
  assert.equal(p.works[0].pr, 55);
  assert.deepEqual(p.grants.map(g => g.generation), [1, 2]);
  assert.deepEqual(f.state.history.slice(0, original.length), original);
  f.run({ kind: 'dispatch', grantId: 'repair-a' });
  f.run({ kind: 'attach', grantId: 'repair-a', owner: 'fresh-repair-owner',
    source: { worktree: 'Q:\\Synthetic\\repair', branch: 'synthetic/repair', head: '1'.repeat(40) } });
  f.run({ kind: 'revoke', grantId: 'repair-a', reason: 'SYNTHETIC stopped without a repair publication.' });
  assert.throws(() => f.run({ kind: 'close', key: 'a', outcome: 'delivered', releases: [] }),
    /Logical closure/);
  p = lifecycleProjection(f.state);
  assert.equal(p.works[0].publications.length, 1);
  assert.equal(p.grants[0].status, 'revoked');
});

test('v10 revisions ignore unrelated bookkeeping, but bind affected scopes/source/custody and exact human evidence', () => {
  const f = lifecycleFixture();
  lifecycleRegister(f, 'a', ['src\\a.cs']);
  const command = { kind: 'amend', grantId: 'a', scopes: ['src\\a.cs', 'src\\new.cs'] };
  const proof = lifecycleInput(f.state, command, 'exact-amend');
  const before = lifecycleRequest(f.state, 'a');
  f.run({ kind: 'register', key: 'unrelated', outcome: 'SYNTHETIC unrelated outcome',
    pool: 'ready', workKind: 'feature', evidence: 'SYNTHETIC other bookkeeping.', pr: null });
  assert.deepEqual(lifecycleRevisions(f.state, command), proof.event.expected);
  const accepted = applyEvent(f.state, proof.event, proof);
  assert.throws(() => validateSourceGrant(accepted.state, before,
    { sourceAuthorityVerified: true, receipt: 'SYNTHETIC obsolete revision.' }));
  assert.throws(() => applyEvent(accepted.state, proof.event, proof), /cannot be replayed/);
  for (const mutate of [
    x => { x.event.evidence.human.receipt = 'different decision'; },
    x => { x.event.evidence.holders[0].acknowledgment.id = 'different response'; },
    x => { x.lifecycleEvidence.humanAuthorizationVerified = false; },
    x => { x.lifecycleEvidence.eventDigest = '0'.repeat(64); },
    x => { x.event.command.scopes.push('src\\unapproved.cs'); }
  ]) {
    const bad = structuredClone(proof); mutate(bad);
    assert.throws(() => applyEvent(f.state, bad.event, bad));
  }
  f.run({ kind: 'refresh', head: '2'.repeat(40) });
  assert.throws(() => applyEvent(f.state, proof.event, proof), /Stale affected/);
  assert.throws(() => validateSourceGrant(f.state, before,
    { sourceAuthorityVerified: true, receipt: 'SYNTHETIC stale source.' }));
  f.run({ kind: 'compatible', grantId: 'a', head: '2'.repeat(40) });
  assert.equal(validateSourceGrant(f.state, lifecycleRequest(f.state, 'a'),
    { sourceAuthorityVerified: true, receipt: 'SYNTHETIC current-source compatibility.' }).valid, true);
  const priorSource = lifecycleRequest(f.state, 'a');
  f.run({ kind: 'compatible', grantId: 'a', head: '2'.repeat(40),
    source: { ...priorSource.source, head: '3'.repeat(40) } });
  assert.throws(() => validateSourceGrant(f.state, priorSource,
    { sourceAuthorityVerified: true, receipt: 'SYNTHETIC old worktree source.' }));
  assert.equal(validateSourceGrant(f.state, lifecycleRequest(f.state, 'a'),
    { sourceAuthorityVerified: true, receipt: 'SYNTHETIC exact authorized source advancement.' }).valid, true);
  assert.throws(() => f.run({ kind: 'compatible', grantId: 'a', head: '2'.repeat(40),
    source: { ...priorSource.source, worktree: 'Q:\\Synthetic\\different-owner' } }), /source identity/);
});

test('v10 unknown launch, cancellation, abandonment and coordinator restart retain work and cannot relaunch old grants', () => {
  const f = lifecycleFixture();
  lifecycleRegister(f, 'a', ['src\\a.cs'], [], false);
  f.run({ kind: 'dispatch', grantId: 'a' });
  f.run({ kind: 'ambiguous', grantId: 'a', reason: 'SYNTHETIC uncertain host result.' });
  assert.throws(() => f.run({ kind: 'dispatch', grantId: 'a' }), /dispatch once/);
  assert.throws(() => f.run({ kind: 'close', key: 'a', outcome: 'cancelled', releases: [] }), /Logical closure/);
  f.run({ kind: 'revoke', grantId: 'a', reason: 'SYNTHETIC independently reconciled launch and joined work.' });
  f.run({ kind: 'close', key: 'a', outcome: 'cancelled', releases: [] });
  assert.equal(lifecycleProjection(f.state).capacity.ready, 10);
  assert.equal(lifecycleProjection(f.state).reservations.length, 1);
  lifecycleRegister(f, 'b', ['src\\b.cs']);
  const stale = lifecycleRequest(f.state, 'b');
  f.run({ kind: 'pause', reason: 'SYNTHETIC explicit interruption; exact joined owners.' });
  f.run({ kind: 'recover', coordinator: 'synthetic-new-coordinator' });
  f.run({ kind: 'resume' });
  f.run({ kind: 'refresh', head: '1'.repeat(40) });
  assert.throws(() => validateSourceGrant(f.state, stale,
    { sourceAuthorityVerified: true, receipt: 'SYNTHETIC old coordinator message.' }));
  f.run({ kind: 'close', key: 'b', outcome: 'abandoned', releases: [] });
  assert.equal(lifecycleProjection(f.state).reservations.length, 2);
  assert.equal(f.state.history.some(r => r.event.command.kind === 'cleanup'), false);
});

test('v10 closure and transfer fail without complete joined preservation or real-shaped independent readback', () => {
  const f = lifecycleFixture();
  lifecycleRegister(f, 'a', ['src\\a.cs']);
  const command = { kind: 'revoke', grantId: 'a', reason: 'SYNTHETIC complete stop.' };
  const original = JSON.stringify(f.state);
  for (const mutate of [
    i => { i.event.evidence.holders[0].pendingInstructions = 1; },
    i => { i.event.evidence.holders[0].processingInstructions = null; },
    i => { i.event.evidence.holders[0].fence.established = false; },
    i => { i.event.evidence.holders[0].workJoined = false; },
    i => { i.event.evidence.holders[0].preservation.unpreserved = ['src\\a.cs']; },
    i => { i.event.evidence.holders[0].preservation.retained = []; },
    i => { i.event.evidence.holders[0].preservation.commits = []; },
    i => { i.lifecycleEvidence.holdersVerified = []; },
    i => { delete i.lifecycleEvidence; }
  ]) {
    const input = lifecycleInput(f.state, command);
    mutate(input);
    if (input.lifecycleEvidence) {
      const binding = lifecycleProofDigest(input.event);
      input.event.evidence.human.proofDigest = binding;
      input.event.evidence.holders.forEach(h => { h.acknowledgment.proofDigest = binding; });
      input.lifecycleEvidence.eventDigest = digest(input.event);
    }
    assert.throws(() => applyEvent(f.state, input.event, input));
    assert.equal(JSON.stringify(f.state), original);
  }
  const extra = structuredClone(f.state); extra.projection = { capacity: { ready: 10 } };
  assert.throws(() => validate(extra), /Malformed lifecycle/);
  const altered = structuredClone(f.state); altered.history[0].event.command.evidence = 'rewritten';
  assert.throws(() => validate(altered), /history digest/);
});

test('v10 full legacy 1-9 preview and explicit adoption preserve accepted histories and never mint execution', () => {
  let legacy = refinementFixture(true);
  const states = [legacy];
  legacy = applyEvent(legacy, refinementEvent(legacy)).state; states.push(legacy);
  const base = fencedExtensionFixture();
  for (let version = 3; version <= 8; version++) {
    const value = structuredClone(base);
    value.version = version;
    value.refinements = []; value.amendments = [];
    value.items.forEach(i => {
      i.scopeRevision = 0; i.scopeHistory = []; i.sourceWritesRetired = false; i.scopeAmendments = [];
    });
    if (version >= 4) { value.readmissions = []; value.items.forEach(i => { i.admissionHistory = []; }); }
    if (version >= 5) value.supersessions = [];
    if (version >= 6) { value.recoveries = []; value.items.forEach(i => { i.scopeRecoveries = []; }); }
    if (version >= 7) value.recoveryRenewals = [];
    validate(value); states.push(value);
  }
  const before = supersessionFixture(), event = fencedSupersessionEvent(before);
  states.push(applyEvent(before, event, fencedSupersessionContext(event)).state);
  assert.deepEqual(states.map(s => s.version), [1, 2, 3, 4, 5, 6, 7, 8, 9]);
  for (const old of states) {
    const bytes = JSON.stringify(old), preview = previewLifecycleMigration(old);
    assert.equal(preview.activating, false);
    assert.equal(preview.legacyDigest, digest(old));
    assert.equal(preview.projection.grants.some(g => g.status === 'active'), false);
    assert.ok(preview.projection.reservations.every(r => r.status === 'HELD'));
    const shadow = { version: 10, origin: { coordinator: old.coordinator, head: old.head, mode: old.mode,
      legacy: old, legacyDigest: digest(old) }, history: [] };
    const input = lifecycleInput(shadow, { kind: 'adopt', legacyDigest: digest(old) });
    // The test input must account for already accepted actual/planned manifests.
    for (const h of input.event.evidence.holders) {
      const item = old.items.find(i => `legacy:${i.key}` === h.grantId);
      for (const record of [...(item.scopeHistory ?? []), ...(item.scopeAmendments ?? [])]) {
        h.preservation.actual = [...new Set([...h.preservation.actual, ...record.proof.manifest.actual])];
        h.preservation.planned = [...new Set([...h.preservation.planned, ...record.proof.manifest.planned])];
      }
      h.preservation.retained = [...new Set([...h.preservation.retained, ...h.preservation.actual])];
    }
    const binding = lifecycleProofDigest(input.event);
    input.event.evidence.human.proofDigest = binding;
    input.event.evidence.holders.forEach(h => { h.acknowledgment.proofDigest = binding; });
    input.lifecycleEvidence.eventDigest = digest(input.event);
    const result = adoptLifecycle(old, input.event, input);
    assert.equal(JSON.stringify(old), bytes);
    assert.equal(JSON.stringify(result.state.origin.legacy), bytes);
    assert.deepEqual(result.actions, []);
    assert.deepEqual(lifecycleProjection(result.state).reservations, preview.projection.reservations);
    assert.equal(lifecycleProjection(result.state).grants.some(g => g.status === 'active'), false);
    assert.throws(() => applyEvent(result.state, input.event, input), /cannot be replayed/);
  }
});

test('v10 fresh same-PR grant may reuse a preserved chat/source without reviving its retired grant', () => {
  const f = lifecycleFixture();
  lifecycleRegister(f, 'a', ['src\\a.cs']);
  const stale = lifecycleRequest(f.state, 'a');
  lifecycleClose(f, 'a', 81, [{ reservationId: 'grant:a', paths: ['src\\a.cs'] }]);
  f.run({ kind: 'repair', key: 'a', pr: 81, grantId: 'a-generation2', scopes: ['src\\a.cs'], transfers: [] });
  f.run({ kind: 'dispatch', grantId: 'a-generation2' });
  f.run({ kind: 'attach', grantId: 'a-generation2', owner: stale.owner, source: stale.source });
  assert.throws(() => validateSourceGrant(f.state, stale,
    { sourceAuthorityVerified: true, receipt: 'SYNTHETIC old instruction.' }));
  assert.equal(validateSourceGrant(f.state, lifecycleRequest(f.state, 'a-generation2'),
    { sourceAuthorityVerified: true, receipt: 'SYNTHETIC fresh bounded execution authority.' }).valid, true);
});

test('v10 repair can replace a joined published but not closed execution without inventing delivery completion', () => {
  const f = lifecycleFixture();
  lifecycleRegister(f, 'a', ['src\\a.cs']);
  const original = lifecycleRequest(f.state, 'a');
  f.run({ kind: 'publish', grantId: 'a', publication: { pushedHead: '2'.repeat(40),
    remoteHead: '2'.repeat(40), pr: 86, ready: true, enrolled: true, receipt: 'SYNTHETIC current PR.' } });
  f.run({ kind: 'repair', key: 'a', pr: 86, grantId: 'fresh-open-repair', scopes: ['src\\a.cs'],
    transfers: [{ reservationId: 'grant:a', paths: ['src\\a.cs'] }] });
  const p = lifecycleProjection(f.state);
  assert.equal(p.works[0].status, 'open');
  assert.equal(p.works[0].pr, 86);
  assert.deepEqual(p.capacity, { repair: 3, ready: 10 });
  assert.deepEqual(p.grants.map(g => g.status), ['revoked', 'allocated']);
  assert.throws(() => validateSourceGrant(f.state, original,
    { sourceAuthorityVerified: true, receipt: 'SYNTHETIC superseded execution.' }));
});

test('v10 imports old authority identities without permitting replay or external preparation activation', () => {
  const before = supersessionFixture(), handoff = fencedSupersessionEvent(before);
  const old = applyEvent(before, handoff, fencedSupersessionContext(handoff)).state;
  const shadow = { version: 10, origin: { coordinator: old.coordinator, head: old.head, mode: old.mode,
    legacy: old, legacyDigest: digest(old) }, history: [] };
  const adoption = lifecycleInput(shadow, { kind: 'adopt', legacyDigest: digest(old) });
  const adopted = adoptLifecycle(old, adoption.event, adoption).state;
  const target = old.items.find(i => i.key === 'successor');
  const input = lifecycleInput(adopted, { kind: 'allocate', key: target.key,
    grantId: 'synthetic-fresh-successor', scopes: target.scopes,
    transfers: [{ reservationId: `legacy:${target.key}`, paths: target.scopes }] });
  input.event.evidence.human.id = handoff.authorization.id;
  input.event.evidence.human.proofDigest = lifecycleProofDigest(input.event);
  input.lifecycleEvidence.eventDigest = digest(input.event);
  assert.throws(() => applyEvent(adopted, input.event, input), /Fresh exact human/);
  const duplicateOldId = lifecycleInput(adopted, { kind: 'register', key: 'other',
    outcome: 'SYNTHETIC other', pool: 'ready', workKind: 'feature', pr: null, evidence: 'SYNTHETIC' },
  handoff.id);
  assert.throws(() => applyEvent(adopted, duplicateOldId.event, duplicateOldId), /replay binding/);
  const externalState = supersessionFixture();
  externalState.coordinator = 'synthetic-external-coordinator';
  const external = supersessionEvent(before, 41, 'prepare-external', [{
    repository: 'roryprimrose/Kora', revision: 1, state: externalState,
    stateDigest: digest(externalState), receipt: 'SYNTHETIC complete external snapshot.' }]);
  const prepared = applyEvent(before, external, { revision: external.proof.checkpointRevision }).state;
  const preview = previewLifecycleMigration(prepared);
  assert.equal(preview.activating, false);
  assert.ok(preview.projection.reservations.length > 0);
  const preparedShadow = { version: 10, origin: { coordinator: prepared.coordinator, head: prepared.head,
    mode: prepared.mode, legacy: prepared, legacyDigest: digest(prepared) }, history: [] };
  const adoptionInput = lifecycleInput(preparedShadow, { kind: 'adopt', legacyDigest: digest(prepared) });
  const imported = adoptLifecycle(prepared, adoptionInput.event, adoptionInput).state;
  const allocation = lifecycleInput(imported, { kind: 'allocate', key: 'successor',
    grantId: 'external-successor', scopes: ['src\\disjoint.cs'], transfers: [] });
  assert.throws(() => applyEvent(imported, allocation.event, allocation), /external preparation/);
  const disjoint = lifecycleInput(imported, { kind: 'allocate', key: 'prior-0',
    grantId: 'fresh-disjoint-prior', scopes: ['other\\disjoint.cs'], transfers: [] });
  const renewed = applyEvent(imported, disjoint.event, disjoint).state;
  assert.equal(require('./registry.cjs').preparedCandidate(renewed, 'prior-0'), false);
});

test('v10 documented retained-work example uses public exports and preserves all three closed worktrees', () => {
  const readme = fs.readFileSync(path.join(__dirname, '..', 'README.md'), 'utf8');
  const section = readme.split('### Synthetic retained-work example')[1].split('## Non-writing host probe')[0];
  const code = section.match(/```javascript\r?\n([\s\S]*?)```/)[1];
  const result = spawnSync(process.execPath, ['-e', code], { encoding: 'utf8' });
  assert.equal(result.status, 0, result.stderr);
  assert.deepEqual(JSON.parse(result.stdout), { closed: ['delivered', 'delivered', 'delivered'],
    capacity: { repair: 4, ready: 10 }, retained: true, custody: ['c'],
    grants: ['revoked', 'revoked', 'revoked'] });
});

test('v10 adoption retains real accepted amendment, admission, recovery, renewal, retirement and cleanup histories', () => {
  const version3 = candidateFixture(true);
  const version4 = applyEvent(version3, candidateEvent(version3)).state;
  const old5 = supersessionFixture();
  const version5 = applyEvent(old5, supersessionEvent(old5), { revision: 41 }).state;
  const old6 = incidentFixture(), recovery = recoveryEvent(old6);
  const version6 = applyEvent(old6, recovery, recoveryContext(recovery)).state;
  const old7 = renewalFixture(), renewal = renewalEvent(old7);
  const version7 = applyEvent(old7, renewal, renewalContext(renewal)).state;
  const old8 = fencedExtensionFixture(), extension = fencedExtensionEvent(old8);
  const version8 = applyEvent(old8, extension, fencedExtensionContext(extension)).state;
  for (const old of [version3, version4, version5, version6, version7, version8]) {
    const bytes = JSON.stringify(old);
    const shadow = { version: 10, origin: { coordinator: old.coordinator, head: old.head, mode: old.mode,
      legacy: old, legacyDigest: digest(old) }, history: [] };
    const input = lifecycleInput(shadow, { kind: 'adopt', legacyDigest: digest(old) });
    const result = adoptLifecycle(old, input.event, input);
    assert.equal(JSON.stringify(result.state.origin.legacy), bytes);
    assert.deepEqual(result.actions, []);
    assert.equal(lifecycleProjection(result.state).grants.some(g => g.status === 'active'), false);
    validate(result.state);
  }
});

test('v10 optional observed physical cleanup preserves custody/capacity and requires fresh complete preservation', () => {
  const f = lifecycleFixture();
  lifecycleRegister(f, 'a', ['src\\a.cs']);
  lifecycleClose(f, 'a', 92);
  const before = lifecycleProjection(f.state);
  const command = { kind: 'cleanup', key: 'a', worktreeAbsent: true, branchAbsent: true,
    receipt: 'SYNTHETIC separately authorized observed absence, no deletion command.' };
  const bad = lifecycleInput(f.state, command);
  bad.lifecycleEvidence.sourceOwnershipVerified = false;
  assert.throws(() => applyEvent(f.state, bad.event, bad), /physical cleanup/);
  assert.deepEqual(f.run(command).actions, []);
  const after = lifecycleProjection(f.state);
  assert.deepEqual(after.reservations, before.reservations);
  assert.deepEqual(after.capacity, before.capacity);
  assert.deepEqual(after.works[0].cleanup,
    { worktreeAbsent: true, branchAbsent: true, receipt: command.receipt });
});

test('UNKNOWN holder handoff is prospective, preserves published owners and guards, and emits no actions', () => {
  const before = supersessionFixture(), event = fencedSupersessionEvent(before);
  const unchanged = structuredClone(before);
  const result = applyEvent(before, event, fencedSupersessionContext(event));
  const state = result.state;
  assert.deepEqual(before, unchanged);
  assert.equal(state.version, 9);
  assert.deepEqual(result.actions, []);
  assert.deepEqual(state.planner, before.planner);
  assert.deepEqual(state.supersessions[0].commands, []);
  assert.equal(state.items.find(item => item.key === 'successor').phase, 'candidate');
  for (const original of before.items.filter(item => item.owner)) {
    const current = state.items.find(item => item.key === original.key);
    for (const name of Object.keys(original)) assert.deepEqual(current[name], original[name]);
    assert.equal(supersededWriter(state, original.key), true);
    assert.equal(current.sourceWritesRetired, false);
  }
  for (const holder of state.supersessions[0].event.proof.holders) {
    assert.equal(holder.reconciliation.pendingInstructions, 'UNKNOWN');
    assert.equal(holder.reconciliation.processingInstructions, 'UNKNOWN');
    assert.equal(Object.hasOwn(holder.stopped, 'sourceInstructionsCleared'), false);
  }
  assert.deepEqual(heldReservations(state, 'successor').filter(item => item.scopes.length)
    .map(item => item.scopes), [['docs\\retained.md']]);
  assert.throws(() => applyEvent(state, { id: 'premature-owner', type: 'attached',
    key: 'successor', owner: 'new-owner' }), /reservation/);
  const launch = applyEvent(state, { id: 'separately-authorized-dispatch', type: 'refresh', head: state.head });
  assert.deepEqual(launch.actions.filter(action => action.type === 'launch'),
    [{ type: 'launch', key: 'successor', pool: 'ready' }]);
  const attached = applyEvent(launch.state, { id: 'new-owner-attached', type: 'attached',
    key: 'successor', owner: 'new-source-owner' }).state;
  assert.equal(attached.items.find(item => item.key === 'successor').owner, 'new-source-owner');
  assert.deepEqual(attached.supersessions, state.supersessions);
  const mutable = fencedExtensionFixture().items[0];
  const withMutable = applyEvent(attached, { ...mutable, id: 'later-independent-mutable', type: 'adopt' }).state;
  const extension = fencedExtensionEvent(withMutable, 42);
  const extended = applyEvent(withMutable, extension, fencedExtensionContext(extension)).state;
  assert.equal(extended.version, 9);
  assert.deepEqual(extended.supersessions, state.supersessions);
  for (const change of [
    e => { e.proof.reconciliation.fence.nonce = event.proof.holders[0].reconciliation.fence.nonce;
      e.acknowledgment.fence = structuredClone(e.proof.reconciliation.fence); },
    e => { e.authorization.id = event.authorization.id; }
  ]) {
    const replay = structuredClone(extension); change(replay); rebindFencedExtension(replay);
    assert.throws(() => applyEvent(withMutable, replay, fencedExtensionContext(replay)), /nonce|decision replay/);
  }
  const published = applyEvent(attached, { id: 'future-owner-published', type: 'published',
    key: 'successor', owner: 'new-source-owner', proof: { pushedHead: attached.head, remoteHead: attached.head,
      pr: 953, ready: true, enrolled: true } }).state;
  const complete = applyEvent(published, { id: 'future-owner-cleaned', type: 'completed',
    key: 'successor', owner: 'new-source-owner', proof: { remoteHead: published.head, stoppedWriting: true,
      worktreeAbsent: true, branchAbsent: true, pendingInstructions: 0 } }).state;
  assert.deepEqual(complete.supersessions, state.supersessions);
  assert.equal(supersededWriter(complete, 'prior-0'), true);
  assert.deepEqual(heldReservations(complete).filter(item => item.scopes.length)
    .map(item => item.scopes), [['docs\\retained.md']]);
  for (const replay of [event, { ...event, type: 'pause' }, { ...event, authorization: {
    ...event.authorization, receipt: 'changed payload' } }]) {
    assert.throws(() => applyEvent(state, replay, fencedSupersessionContext(event)), /cannot be replayed/);
  }
  validate(JSON.parse(JSON.stringify(state)));
});

test('documented UNKNOWN handoff example is synthetic, exact and nonexecuting', () => {
  const root = path.resolve(__dirname, '..', '..', '..', '..');
  const readme = fs.readFileSync(path.join(__dirname, '..', 'README.md'), 'utf8');
  const example = readme.split('### Human-fenced local holder handoff')[1]
    .match(/```javascript\r?\n([\s\S]*?)```/)[1];
  const result = spawnSync(process.execPath, ['-e', example], { cwd: root, encoding: 'utf8' });
  assert.equal(result.status, 0, result.stderr);
  const output = JSON.parse(result.stdout);
  assert.equal(output.version, 9);
  assert.equal(output.beneficiaryPhase, 'candidate');
  assert.deepEqual(output.actions, []);
  assert.equal(output.input.expectedRevision, 41);
  for (const holder of output.input.event.proof.holders) {
    assert.equal(holder.reconciliation.pendingInstructions, 'UNKNOWN');
    assert.equal(holder.reconciliation.processingInstructions, 'UNKNOWN');
    assert.equal(Object.hasOwn(holder.stopped, 'sourceInstructionsCleared'), false);
  }
});

test('fenced handoff rejects stale, foreign, incomplete, mixed, unjoined or unauthenticated proof before mutation', () => {
  const state = supersessionFixture(), original = structuredClone(state);
  const changes = [
    e => { e.proof.coordinator = 'foreign'; }, e => { e.proof.head = sha(9); },
    e => { e.proof.checkpointRevision--; }, e => { e.proof.expectedStateDigest = '0'.repeat(64); },
    e => { e.proof.candidate.outcome = 'different operation'; },
    e => { e.proof.candidate.scopes.push('src\\new.cs'); },
    e => { e.proof.beneficiaryManifest.actual.push('src\\shared.cs'); },
    e => { e.proof.beneficiaryManifest.planned.pop(); },
    e => { e.proof.localReservations.pop(); }, e => { e.proof.effectiveReservations.pop(); },
    e => { e.proof.holders.pop(); }, e => { e.proof.holders.reverse(); },
    e => { e.proof.holders[0].paths = ['src']; },
    e => { e.proof.holders[0].owner = 'foreign'; },
    e => { e.proof.holders[0].stopped.commandsRunning = true; },
    e => { e.proof.holders[1].stopped.workJoined = false; },
    e => { e.proof.holders[0].stopped.sourceInstructionsCleared = true; },
    e => { e.proof.holders[0].source.head = sha(9); },
    e => { e.proof.holders[0].source.worktree = '..\\foreign'; },
    e => { e.proof.holders[0].reconciliation.pendingInstructions = 0; },
    e => { e.proof.holders[1].reconciliation.processingInstructions = 0; },
    e => { e.proof.holders[0].reconciliation.fence.established = false; },
    e => { e.proof.holders[0].reconciliation.fence.revokesEarlierSourceAuthority = false; },
    e => { e.proof.holders[1].reconciliation.fence.nonce = e.proof.holders[0].reconciliation.fence.nonce; },
    e => { e.proof.holders[0].acknowledgment.kind = 'revocation'; },
    e => { e.proof.holders[0].acknowledgment.owner = 'foreign'; },
    e => { e.proof.holders[0].acknowledgment.staleAuthorityRejected = false; },
    e => { e.proof.holders[0].preservation.unpreserved.push({
      path: 'src\\shared.cs', receipt: 'unpreserved actual effect' }); },
    e => { e.authorization.kind = 'implementation-approval'; },
    e => { e.authorization.actor = ''; },
    e => { e.proof.mode = 'prepare-external'; e.proof.authority.singleLedger = false; }
  ];
  for (const change of changes) {
    const event = fencedSupersessionEvent(state); change(event);
    for (const holder of event.proof.holders) {
      holder.acknowledgment.fence = structuredClone(holder.reconciliation.fence);
    }
    rebindFencedSupersession(event);
    const context = fencedSupersessionContext(event);
    context.revision = 41;
    assert.throws(() => applyEvent(state, event, context));
    assert.deepEqual(state, original);
  }
  for (const change of [
    c => { delete c.supersessionEvidence; },
    c => { c.supersessionEvidence.humanAuthorizationVerified = false; },
    c => { c.supersessionEvidence.humanDecisionDigest = '0'.repeat(64); },
    c => { c.supersessionEvidence.ledgerAuthorityVerified = false; },
    c => { c.supersessionEvidence.beneficiarySourceVerified = false; },
    c => { c.supersessionEvidence.holders.pop(); },
    c => { c.supersessionEvidence.holders[0].ownerResponseId = 'foreign'; },
    c => { c.supersessionEvidence.holders[0].sourceIdentityVerified = false; },
    c => { c.supersessionEvidence.holders[1].preservationVerified = false; },
    c => { c.supersessionEvidence.holders[0].sourceStoppedVerified = false; },
    c => { c.supersessionEvidence.holders[1].commandsJoinedVerified = false; },
    c => { c.supersessionEvidence.holders[0].instructionAuthorityVerified = false; }
  ]) {
    const event = fencedSupersessionEvent(state), context = fencedSupersessionContext(event);
    change(context);
    assert.throws(() => applyEvent(state, event, context));
    assert.deepEqual(state, original);
  }
  const legacy = supersessionEvent(state);
  legacy.proof.holders[0].stopped.pendingInstructions = 'UNKNOWN';
  rebindSupersession(legacy);
  assert.throws(() => applyEvent(state, legacy, { revision: 41 }), /reconciliation/);
});

test('handoff coexists with immutable v8 mutable/recovery fences and cannot revive prior source authority', () => {
  let state = fencedExtensionFixture();
  const extension = fencedExtensionEvent(state);
  state = applyEvent(state, extension, fencedExtensionContext(extension)).state;
  const oldContinuation = fencedExtensionContinuation(state);
  assert.equal(validateScopeExtensionContinuation(state, oldContinuation, { revision: 42 }).coverageVerified, true);
  const incidentOwner = incidentFixture().items[0];
  state = applyEvent(state, { ...incidentOwner, id: 'new-incident-owner', type: 'adopt' }).state;
  const recovery = recoveryEvent(state, 42, 'human-approved-source-fence');
  state = applyEvent(state, recovery, recoveryContext(recovery)).state;
  for (const key of ['mutable', incidentOwner.key]) {
    const item = state.items.find(value => value.key === key);
    state = applyEvent(state, { id: `preserved-publication-${key}`, type: 'published', key,
      owner: item.owner, proof: { pushedHead: item.head, remoteHead: item.head,
        pr: key === 'mutable' ? 951 : 952, ready: true, enrolled: true } }).state;
  }
  if (!state.planner.owner) {
    state = applyEvent(state, { id: 'handoff-planner', type: 'planner', owner: 'handoff-planner' }).state;
  }
  const scopes = ['src\\owned.cs', recovery.proof.incident.unauthorizedPaths[0]];
  state = applyEvent(state, { id: 'handoff-successor', type: 'candidate', key: 'successor',
    owner: state.planner.owner ?? 'planner', pass: state.planner.pass,
    outcome: 'Synthetic exact same-ledger handoff', pool: 'ready', kind: 'feature',
    head: state.head, scopes, evidence: 'SYNTHETIC current-source complete operation.' }).state;
  const event = fencedSupersessionEvent(state, 43);
  for (const holder of event.proof.holders) {
    const original = holder.key === 'mutable' ? extension.proof : recovery.proof;
    holder.source = structuredClone(original.source);
    if (!holder.preservation.commits.some(commit => commit.head === holder.source.head)) {
      holder.preservation.commits.push({ head: holder.source.head, paths: [],
        receipt: 'SYNTHETIC retained original source base alongside publication.' });
    }
    holder.reconciliation.fence.epoch = original.reconciliation.fence.epoch + 1;
    holder.acknowledgment.fence = structuredClone(holder.reconciliation.fence);
  }
  rebindFencedSupersession(event);
  const original = structuredClone(state);
  const result = applyEvent(state, event, fencedSupersessionContext(event));
  assert.deepEqual(result.actions, []);
  assert.equal(result.state.version, 9);
  assert.deepEqual(result.state.recoveries, state.recoveries);
  assert.deepEqual(result.state.recoveryRenewals, state.recoveryRenewals);
  assert.deepEqual(result.state.items.filter(item => item.owner), state.items.filter(item => item.owner));
  assert.equal(result.state.supersessions[0].before.version, 8);
  for (const change of [
    e => { e.proof.holders[0].reconciliation.fence.epoch = 1; },
    e => { e.proof.holders[0].reconciliation.fence.nonce = extension.proof.reconciliation.fence.nonce; },
    e => { e.proof.holders[0].source.branch = 'different-source'; },
    e => { e.proof.holders[0].acknowledgment.id = extension.acknowledgment.id; },
    e => { e.authorization.id = recovery.authorization.id; }
  ]) {
    const altered = structuredClone(event); change(altered);
    for (const holder of altered.proof.holders) {
      holder.acknowledgment.fence = structuredClone(holder.reconciliation.fence);
    }
    rebindFencedSupersession(altered);
    assert.throws(() => applyEvent(state, altered, fencedSupersessionContext(altered)));
    assert.deepEqual(state, original);
  }
  const resurrected = structuredClone(result.state);
  resurrected.items.find(item => item.key === 'mutable').phase = 'running';
  assert.throws(() => validateScopeExtensionContinuation(resurrected, oldContinuation, { revision: 44 }));
  assert.throws(() => applyEvent(resurrected, extension, fencedExtensionContext(extension)));
  for (const change of [
    s => { s.version = 8; },
    s => { s.supersessions[0].before.recoveries = []; },
    s => { s.supersessions[0].event.proof.holders[1].reconciliation.pendingInstructions = 0; },
    s => { s.supersessions[0].hostEvidence.humanAuthorizationVerified = false; },
    s => { s.supersessions[0].commands.push({ type: 'resume' }); },
    s => { s.supersessions = []; }
  ]) {
    const altered = structuredClone(result.state); change(altered);
    assert.throws(() => validate(altered));
  }
});

test('non-incident fenced extension preserves partial writes and UNKNOWN, emits no actions and needs separate exact continuation', () => {
  const state = fencedExtensionFixture();
  state.planner.requested = true;
  const before = structuredClone(state);
  const event = fencedExtensionEvent(state);
  const result = applyEvent(state, event, fencedExtensionContext(event));
  assert.deepEqual(state, before);
  assert.deepEqual(result.actions, []);
  assert.deepEqual(result.state.planner, state.planner);
  const item = result.state.items[0];
  assert.equal(result.state.version, 8);
  assert.deepEqual(result.state.recoveries, []);
  assert.deepEqual(item.scopes, [...state.items[0].scopes, 'src\\shared-review.cs']);
  for (const name of ['owner', 'head', 'phase', 'blocker', 'publication', 'pr']) {
    assert.deepEqual(item[name], state.items[0][name]);
  }
  assert.deepEqual(result.state.items[1].scopes, state.items[1].scopes);
  assert.deepEqual(item.scopeAmendments[0].proof.preservation, event.proof.preservation);
  const request = fencedExtensionContinuation(result.state);
  assert.deepEqual(validateScopeExtensionContinuation(result.state, request, { revision: 42 }),
    { coverageVerified: true, blockers: item.blocker, sourceExecutionAuthorized: false });
  assert.throws(() => applyEvent(result.state, event, fencedExtensionContext(event)), /cannot be replayed/);
  assert.throws(() => applyEvent(result.state, { id: event.id, type: 'pause' }), /cannot be replayed/);
  const legacy = extensionEvent(state, 'mutable', ['src\\legacy-review.cs']);
  legacy.proof.quiescence.pendingInstructions = 'UNKNOWN';
  assert.throws(() => applyEvent(state, rebind(legacy)), /pending-zero/);
});

test('fenced extension rejects missing, stale, foreign, unfinished, outside-authority and conflicting proof without mutation', () => {
  const state = fencedExtensionFixture();
  for (const change of [
    e => { e.proof.repository = 'foreign/repository'; },
    e => { e.proof.coordinator = 'foreign-coordinator'; },
    e => { e.proof.checkpointRevision--; },
    e => { e.proof.head = sha(8); },
    e => { e.proof.expectedItemDigest = '0'.repeat(64); },
    e => { e.owner = 'foreign-owner'; },
    e => { e.proof.source.owner = 'foreign-owner'; },
    e => { e.proof.source.head = sha(8); },
    e => { e.proof.source.worktree = 'relative'; },
    e => { e.proof.fromScopes.pop(); },
    e => { e.proof.toScopes.pop(); },
    e => { e.proof.scopeRevision++; },
    e => { e.proof.quiescence.paused = false; },
    e => { e.proof.quiescence.workJoined = false; },
    e => { e.proof.quiescence.commandsRunning = true; },
    e => { e.proof.reconciliation.pendingInstructions = 0; },
    e => { e.proof.reconciliation.processingInstructions = 0; },
    e => { e.proof.reconciliation.fence.established = false; },
    e => { e.proof.reconciliation.fence.revokesEarlierSourceAuthority = false; },
    e => { e.proof.reconciliation.fence.nonce = ''; },
    e => { e.proof.reconciliation.fence.epoch = 0; },
    e => { e.proof.manifest.actual.push('src\\shared-review.cs');
      e.proof.preservation.retained.push({ path: 'src\\shared-review.cs', receipt: 'SYNTHETIC unauthorized write.' }); },
    e => { e.proof.exclusivity.reservations = []; },
    e => { e.proof.additionalScopes = ['src\\held.cs']; e.proof.toScopes = [...e.proof.fromScopes, 'src\\held.cs'];
      e.proof.manifest.planned = ['src\\held.cs']; },
    e => { e.acknowledgment.workJoined = false; },
    e => { e.acknowledgment.staleAuthorityRejected = false; },
    e => { e.acknowledgment.fence = null; },
    e => { e.authorization.kind = 'implementation-approval'; },
    e => { e.authorization.actor = ''; }
  ]) {
    const event = fencedExtensionEvent(state);
    change(event); rebindFencedExtension(event);
    const before = JSON.stringify(state);
    assert.throws(() => applyEvent(state, event, { ...fencedExtensionContext(event), revision: 41 }),
      undefined, change.toString());
    assert.equal(JSON.stringify(state), before);
  }
  const event = fencedExtensionEvent(state);
  assert.throws(() => applyEvent(state, event), /checkpoint/);
  assert.throws(() => applyEvent(state, event, { revision: 41 }), /fields/);
  for (const name of ['sourceIdentityVerified', 'sourcePauseVerified', 'commandsJoinedVerified', 'instructionAuthorityVerified']) {
    const context = fencedExtensionContext(event); context.extensionEvidence[name] = false;
    assert.throws(() => applyEvent(state, event, context), /Independent host readback/);
  }
  for (const name of ['proofDigest', 'ownerResponseId', 'humanDecisionId', 'humanDecisionDigest', 'coordinator', 'checkpointRevision']) {
    const context = fencedExtensionContext(event); context.extensionEvidence[name] = 'stale';
    assert.throws(() => applyEvent(state, event, context), /Independent host readback/);
  }
  event.authorization.subjectDigest = '0'.repeat(64);
  assert.throws(() => applyEvent(state, event, fencedExtensionContext(event)), /human extension decision/);
});

test('fenced continuation rejects stale source, checkpoint, union, guards, fence and reused owner response', () => {
  const before = fencedExtensionFixture(), event = fencedExtensionEvent(before);
  const state = applyEvent(before, event, fencedExtensionContext(event)).state;
  for (const change of [
    r => { r.checkpointRevision--; }, r => { r.owner = 'foreign'; },
    r => { r.source.head = sha(8); }, r => { r.source.worktree = 'Q:\\Other'; },
    r => { r.source.branch = 'other'; }, r => { r.source.owner = 'other'; },
    r => { r.coverageDigest = '0'.repeat(64); }, r => { r.expectedItemDigest = '0'.repeat(64); },
    r => { r.scopeRevision++; }, r => { r.fence.epoch++; },
    r => { r.acknowledgment.id = event.acknowledgment.id; },
    r => { r.acknowledgment.workJoined = false; }, r => { r.acknowledgment.staleAuthorityRejected = false; }
  ]) {
    const request = fencedExtensionContinuation(state);
    change(request); request.acknowledgment.requestDigest = recoveryContinuationDigest(request);
    assert.throws(() => validateScopeExtensionContinuation(state, request, { revision: 42 }));
  }
  const stale = fencedExtensionContinuation(state);
  assert.throws(() => validateScopeExtensionContinuation(state, stale, { revision: 43 }));
  const held = applyEvent(state, { id: 'later-held', type: 'adopt', key: 'later', owner: 'later-owner',
    outcome: 'Another retained source', head: state.head, pool: 'ready', kind: 'feature',
    scopes: ['src\\later.cs'], evidence: 'SYNTHETIC new current exclusive guard.' }).state;
  assert.throws(() => validateScopeExtensionContinuation(held, fencedExtensionContinuation(held), { revision: 42 }));
  const refreshed = applyEvent(state, { id: 'later-main', type: 'refresh', head: sha(8) }).state;
  assert.throws(() => validateScopeExtensionContinuation(refreshed, fencedExtensionContinuation(refreshed), { revision: 42 }));
});

test('fenced extension history cannot downgrade to zero or reuse authority and survives legitimate publication/cleanup', () => {
  const before = fencedExtensionFixture(), first = fencedExtensionEvent(before);
  const state = applyEvent(before, first, fencedExtensionContext(first)).state;
  const zero = extensionEvent(state, 'mutable', ['src\\next.cs'], 'later-zero');
  zero.proof.manifest.planned.unshift('src\\shared-review.cs');
  assert.throws(() => applyEvent(state, rebind(zero)), /cannot be dropped/);
  for (const change of [
    e => { e.authorization.id = first.authorization.id; },
    e => { e.acknowledgment.id = first.acknowledgment.id; },
    e => { e.proof.reconciliation.fence.epoch--; },
    e => { e.proof.reconciliation.fence.nonce = first.proof.reconciliation.fence.nonce; },
    e => { e.proof.source.branch = 'other'; },
    e => { e.proof.manifest.actual = []; e.proof.preservation.retained = []; }
  ]) {
    const second = fencedExtensionEvent(state, 43, ['src\\next.cs']);
    change(second); rebindFencedExtension(second);
    assert.throws(() => applyEvent(state, second, fencedExtensionContext(second)));
  }
  const second = fencedExtensionEvent(state, 43, ['src\\next.cs']);
  const accepted = applyEvent(state, second, fencedExtensionContext(second)).state;
  assert.deepEqual(accepted.items[0].scopeAmendments[0], state.items[0].scopeAmendments[0]);
  assert.equal(validateScopeExtensionContinuation(accepted, fencedExtensionContinuation(accepted, 44),
    { revision: 44 }).sourceExecutionAuthorized, false);
  for (const change of [
    s => { s.version = 7; }, s => { s.items[0].scopeAmendments = []; },
    s => { s.items[0].scopeAmendments[0].hostEvidence.commandsJoinedVerified = false; },
    s => { s.items[0].scopeAmendments[0].before.blocker = null; },
    s => { s.items[0].scopeAmendments[0].authorization.receipt = 'altered'; }
  ]) {
    const altered = structuredClone(accepted); change(altered); assert.throws(() => validate(altered));
  }
  let published = applyEvent(accepted, { id: 'fenced-publish', type: 'published', key: 'mutable',
    owner: 'synthetic-mutable-owner', proof: { pushedHead: sha(9), remoteHead: sha(9),
      pr: 900, ready: true, enrolled: true } }).state;
  published = applyEvent(published, { id: 'fenced-cleanup', type: 'completed', key: 'mutable',
    owner: 'synthetic-mutable-owner', proof: { remoteHead: sha(9), stoppedWriting: true,
      branchAbsent: true, worktreeAbsent: true, pendingInstructions: 0 } }).state;
  assert.deepEqual(published.items[0].scopeAmendments, accepted.items[0].scopeAmendments);
  assert.throws(() => validateScopeExtensionContinuation(published, fencedExtensionContinuation(published, 45),
    { revision: 45 }));
});

test('fenced extension migrates v1-v7 without changing old protocols and cannot bypass retirement or a newer fence', () => {
  const v2 = applyEvent(refinementFixture(), refinementEvent(refinementFixture())).state;
  let v3 = applyEvent(v2, { id: 'legacy-owner', type: 'adopt', key: 'legacy', owner: 'legacy-owner',
    outcome: 'Legacy unrelated amendment', head: v2.head, scopes: ['legacy\\original.cs'],
    pool: 'ready', kind: 'feature', evidence: 'SYNTHETIC legacy migration.' }).state;
  v3 = applyEvent(v3, extensionEvent(v3, 'legacy', ['legacy\\next.cs'])).state;
  const incident = incidentFixture(), recovery = recoveryEvent(incident);
  const v6 = applyEvent(incident, recovery, recoveryContext(recovery)).state;
  const renewal = renewalFixture(), renewalProof = renewalEvent(renewal);
  const v7 = applyEvent(renewal, renewalProof, renewalContext(renewalProof)).state;
  const candidate = candidateFixture(true);
  const v4 = applyEvent(candidate, candidateEvent(candidate)).state;
  const supersession = supersessionFixture();
  const v5 = applyEvent(supersession, supersessionEvent(supersession), { revision: 41 }).state;
  const versions = [createState('legacy-coordinator', sha(1), 'delivery'), v2, v3,
    v4, v5, v6, v7];
  assert.deepEqual(versions.map(state => state.version), [1, 2, 3, 4, 5, 6, 7]);
  for (let state of versions) {
    const old = structuredClone(state);
    state = applyEvent(state, { id: 'new-mutable-migration-owner', type: 'adopt', key: 'mutable',
      owner: 'synthetic-mutable-owner', outcome: 'Synthetic new migration target',
      head: state.head, pool: 'ready', kind: 'feature', scopes: ['src\\owned.cs', 'tests\\owned.test.cs'],
      evidence: 'SYNTHETIC independent mutable owner.' }).state;
    const event = fencedExtensionEvent(state);
    const accepted = applyEvent(state, event, fencedExtensionContext(event)).state;
    assert.equal(accepted.version, 8);
    for (const previous of old.items) {
      const current = accepted.items.find(item => item.key === previous.key);
      for (const name of Object.keys(previous)) assert.deepEqual(current[name], previous[name]);
    }
    for (const name of ['refinements', 'amendments', 'recoveries', 'recoveryRenewals', 'supersessions', 'readmissions']) {
      if (Object.hasOwn(old, name)) {
        assert.deepEqual(name === 'amendments' ? accepted[name].slice(0, -1) : accepted[name], old[name]);
      }
    }
  }
  const retired = v2;
  const event = fencedExtensionEvent(fencedExtensionFixture());
  event.key = 'holding'; event.owner = retired.items[0].owner;
  assert.throws(() => applyEvent(retired, event, fencedExtensionContext(event)), /nonretired/);
  const p = v6.recoveries[0].event.proof;
  const afterRecovery = fencedExtensionEvent(fencedExtensionFixture(), 43, ['src\\new-review.cs']);
  afterRecovery.key = p.key; afterRecovery.owner = p.owner;
  const item = v6.items.find(value => value.key === p.key);
  afterRecovery.expectedScopeRevision = item.scopeRevision;
  Object.assign(afterRecovery.proof, { key: p.key, owner: p.owner, expectedItemDigest: digest(item),
    coordinator: v6.coordinator, head: v6.head, scopeRevision: item.scopeRevision, fromScopes: item.scopes,
    toScopes: [...item.scopes, 'src\\new-review.cs'], reservation: p.reservation, source: p.source,
    manifest: p.manifest, preservation: p.preservation,
    exclusivity: { complete: true, reservations: heldReservations(v6, p.key), receipt: 'SYNTHETIC current guards.' } });
  afterRecovery.proof.manifest = structuredClone(p.manifest);
  afterRecovery.proof.manifest.planned.push('src\\new-review.cs');
  afterRecovery.proof.reconciliation.fence.epoch = p.reconciliation.fence.epoch + 1;
  Object.assign(afterRecovery.acknowledgment, { key: p.key, owner: p.owner, scopeRevision: item.scopeRevision,
    fence: structuredClone(afterRecovery.proof.reconciliation.fence) });
  rebindFencedExtension(afterRecovery);
  const accepted = applyEvent(v6, afterRecovery, fencedExtensionContext(afterRecovery)).state;
  assert.throws(() => validateRecoveryContinuation(accepted, continuation(accepted, 44), { revision: 44 }),
    /current grant/);
});

test('documented fenced mutable example executes in memory and supplies no source execution authority', () => {
  const root = path.resolve(__dirname, '..', '..', '..', '..');
  const readme = fs.readFileSync(path.join(__dirname, '..', 'README.md'), 'utf8');
  const example = readme.split('### Explicit human-approved UNKNOWN extension')[1]
    .match(/```javascript\r?\n([\s\S]*?)```/)[1];
  const result = spawnSync(process.execPath, ['-e', example], { cwd: root, encoding: 'utf8' });
  assert.equal(result.status, 0, result.stderr);
  const output = JSON.parse(result.stdout);
  assert.deepEqual(output.actions, []);
  assert.equal(output.event.proof.version, 2);
  assert.equal(output.event.proof.reconciliation.pendingInstructions, 'UNKNOWN');
  assert.equal(output.result.coverageVerified, true);
  assert.equal(output.result.sourceExecutionAuthorized, false);
});

test('explicit renewal permits compatible main advancement without mutating original grant, source, scopes or fence', () => {
  for (const mode of ['enumerated-pending-zero', 'human-approved-source-fence']) {
    const state = renewalFixture(mode);
    state.planner = { owner: 'waiting-planner', active: false, requested: true, notified: false, pass: 0 };
    const before = structuredClone(state);
    const stale = continuation(state, 43);
    assert.throws(() => validateRecoveryContinuation(state, stale, { revision: 43 }), /current grant/);
    const event = renewalEvent(state);
    const result = applyEvent(state, event, renewalContext(event));
    assert.equal(result.state.version, 7);
    assert.deepEqual(result.actions, []);
    assert.deepEqual(state, before);
    assert.deepEqual(result.state.items, before.items);
    assert.deepEqual(result.state.planner, before.planner);
    assert.deepEqual(result.state.recoveries, before.recoveries);
    assert.equal(result.state.recoveries[0].event.proof.head, '1'.repeat(40));
    assert.equal(result.state.head, '2'.repeat(40));
    assert.equal(result.state.recoveries[0].event.proof.source.head, '1'.repeat(40));
    assert.equal(result.state.recoveryRenewals[0].event.proof.quiescence.pendingInstructions,
      mode === 'enumerated-pending-zero' ? 0 : 'UNKNOWN');
    assert.deepEqual(heldReservations(result.state, 'incident-owner'), heldReservations(before, 'incident-owner'));
    const request = continuation(result.state, 44, true);
    assert.deepEqual(validateRecoveryContinuation(result.state, request, { revision: 44 }),
      { coverageVerified: true, blockers: before.items[0].blocker, sourceExecutionAuthorized: false });
    assert.throws(() => validateRecoveryContinuation(result.state, continuation(result.state, 44),
      { revision: 44 }), /current grant/);
    assert.throws(() => applyEvent(result.state, event, renewalContext(event)), /cannot be replayed/);
    assert.throws(() => applyEvent(result.state, { id: event.id, type: 'pause' }), /cannot be replayed/);
    assert.throws(() => validateRecoveryContinuation(result.state, request, { revision: 45 }), /current-revision/);
  }
});

test('renewal rejects stale, foreign, conflicting or missing compatibility/owner/host evidence without mutation', () => {
  const state = renewalFixture();
  for (const change of [
    e => { e.proof.repository = 'other/repository'; },
    e => { e.proof.coordinator = 'different'; },
    e => { e.proof.checkpointRevision--; },
    e => { e.proof.head = '3'.repeat(40); },
    e => { e.proof.grantDigest = '0'.repeat(64); },
    e => { e.proof.previousRenewalDigest = '0'.repeat(64); },
    e => { e.proof.expectedItemDigest = '0'.repeat(64); },
    e => { e.proof.key = 'held'; },
    e => { e.proof.owner = 'other-owner'; },
    e => { e.proof.scopeRevision++; },
    e => { e.proof.scopes.push('src\\additional.cs'); },
    e => { e.proof.source.head = '3'.repeat(40); },
    e => { e.proof.source.branch = 'replacement'; },
    e => { e.proof.source.worktree = 'Q:\\Replacement'; },
    e => { e.proof.source.owner = 'replacement'; },
    e => { e.proof.fence.epoch++; },
    e => { e.proof.fence.nonce = 'replacement'; },
    e => { e.proof.fence = null; },
    e => { e.proof.quiescence.pendingInstructions = 0; },
    e => { e.proof.quiescence.processingInstructions = 0; },
    e => { e.proof.quiescence.sourcePaused = false; },
    e => { e.proof.quiescence.workJoined = false; },
    e => { e.proof.quiescence.commandsRunning = true; },
    e => { e.proof.compatibility.baseHead = '3'.repeat(40); },
    e => { e.proof.compatibility.fromHead = '3'.repeat(40); },
    e => { e.proof.compatibility.toHead = '3'.repeat(40); },
    e => { e.proof.compatibility.complete = false; },
    e => { e.proof.compatibility.ancestorVerified = false; },
    e => { e.proof.compatibility.scopeReviewCompatible = false; },
    e => { e.proof.compatibility.sourceReviewCompatible = false; },
    e => { e.proof.compatibility.changedPaths.push('src\\windows\\packages.lock.json'); },
    e => { e.proof.compatibility.changedPaths = ['src']; },
    e => { e.proof.compatibility.receipt = ''; },
    e => { e.proof.exclusivity.reservations = []; },
    e => { e.proof.exclusivity.reservations[0].scopes = ['src']; },
    e => { e.proof.exclusivity.complete = false; },
    e => { e.acknowledgment.owner = 'other-owner'; },
    e => { e.acknowledgment.fence = null; },
    e => { e.acknowledgment.workJoined = false; },
    e => { e.acknowledgment.staleAuthorityRejected = false; },
    e => { e.acknowledgment.id = state.recoveries[0].event.acknowledgment.id; },
    e => { e.acknowledgment.receipt = ''; }
  ]) {
    const event = renewalEvent(state);
    change(event);
    rebindRenewal(event);
    const before = JSON.stringify(state);
    assert.throws(() => applyEvent(state, event, { ...renewalContext(event), revision: 43 }),
      undefined, change.toString());
    assert.equal(JSON.stringify(state), before);
  }
  const event = renewalEvent(state);
  assert.throws(() => applyEvent(state, event, { revision: 43 }));
  for (const change of [
    c => { c.renewalEvidence.kind = 'receipt-only'; },
    c => { c.renewalEvidence.coordinator = 'foreign'; },
    c => { c.renewalEvidence.checkpointRevision--; },
    c => { c.renewalEvidence.proofDigest = '0'.repeat(64); },
    c => { c.renewalEvidence.compatibilityDigest = '0'.repeat(64); },
    c => { c.renewalEvidence.ownerResponseId = 'stale'; },
    c => { c.renewalEvidence.sourceIdentityVerified = false; },
    c => { c.renewalEvidence.sourcePauseVerified = false; },
    c => { c.renewalEvidence.commandsJoinedVerified = false; },
    c => { c.renewalEvidence.instructionAuthorityVerified = false; },
    c => { c.renewalEvidence.compatibilityVerified = false; }
  ]) {
    const context = renewalContext(event);
    change(context);
    assert.throws(() => applyEvent(state, event, context), /Independent current host readback/);
  }
  event.acknowledgment.proofDigest = '0'.repeat(64);
  assert.throws(() => applyEvent(state, event, renewalContext(event)), /acknowledgment/);
});

test('renewed continuation binds latest grant, exact source/item/scope/fence/exclusivity and separate fresh response', () => {
  const before = renewalFixture();
  const event = renewalEvent(before);
  const state = applyEvent(before, event, renewalContext(event)).state;
  for (const change of [
    r => { r.renewalDigest = '0'.repeat(64); },
    r => { r.grantDigest = '0'.repeat(64); },
    r => { r.head = '3'.repeat(40); },
    r => { r.sourceHead = '3'.repeat(40); },
    r => { r.owner = 'other-owner'; },
    r => { r.scopeRevision++; },
    r => { r.expectedItemDigest = '0'.repeat(64); },
    r => { r.fence.epoch++; },
    r => { r.acknowledgment.fence.nonce = 'old'; },
    r => { r.acknowledgment.workJoined = false; },
    r => { r.acknowledgment.staleAuthorityRejected = false; },
    r => { r.acknowledgment.id = event.acknowledgment.id; },
    r => { r.acknowledgment.id = state.recoveries[0].event.acknowledgment.id; }
  ]) {
    const request = continuation(state, 44, true);
    change(request);
    request.acknowledgment.requestDigest = recoveryContinuationDigest(request);
    assert.throws(() => validateRecoveryContinuation(state, request, { revision: 44 }));
  }
  const changedItem = applyEvent(state, { id: 'later-blocker', type: 'blocked', key: 'incident-owner',
    owner: 'synthetic-owner', reason: 'Different live blocker.' }).state;
  assert.throws(() => validateRecoveryContinuation(changedItem, continuation(changedItem, 44, true),
    { revision: 44 }), /current-revision\/item/);
  const changedGuards = applyEvent(state, { id: 'new-disjoint-holder', type: 'adopt', key: 'another',
    owner: 'another-owner', head: state.head, scopes: ['src\\another.cs'], evidence: 'Synthetic new guard.',
    outcome: 'Another retained operation', pool: 'ready', kind: 'feature' }).state;
  assert.throws(() => validateRecoveryContinuation(changedGuards, continuation(changedGuards, 44, true),
    { revision: 44 }), /exclusivity/);
  const staleAcknowledgment = continuation(state, 44, true);
  staleAcknowledgment.acknowledgment.requestDigest = '0'.repeat(64);
  assert.throws(() => validateRecoveryContinuation(state, staleAcknowledgment, { revision: 44 }), /current grant/);
  for (const change of [
    s => { s.enabled = false; },
    s => { s.mode = 'observe'; },
    s => { s.coordinator = 'other-coordinator'; }
  ]) {
    const altered = structuredClone(state);
    change(altered);
    assert.throws(() => validateRecoveryContinuation(altered, continuation(altered, 44, true), { revision: 44 }));
    const next = renewalEvent(altered, 45);
    assert.throws(() => applyEvent(altered, next, renewalContext(next)));
  }
});

test('successive renewal binds the previous record and rejects stale ack, old head, omitted histories and downgrade', () => {
  const before = renewalFixture();
  const first = renewalEvent(before);
  const accepted = applyEvent(before, first, renewalContext(first)).state;
  const state = applyEvent(accepted, { id: 'later-main', type: 'refresh', head: '3'.repeat(40) }).state;
  const second = renewalEvent(state, 45);
  const result = applyEvent(state, second, renewalContext(second));
  assert.deepEqual(result.state.recoveries, before.recoveries);
  assert.deepEqual(result.state.recoveryRenewals[0], accepted.recoveryRenewals[0]);
  assert.deepEqual(result.actions, []);
  assert.equal(validateRecoveryContinuation(result.state, continuation(result.state, 46, true),
    { revision: 46 }).sourceExecutionAuthorized, false);
  const reused = continuation(result.state, 46, true);
  reused.acknowledgment.id = first.acknowledgment.id;
  assert.throws(() => validateRecoveryContinuation(result.state, reused, { revision: 46 }), /fresh acknowledgment/);
  assert.throws(() => validateRecoveryContinuation(result.state, continuation(accepted, 44, true),
    { revision: 46 }));
  for (const change of [
    e => { e.proof.previousRenewalDigest = null; },
    e => { e.proof.compatibility.fromHead = e.proof.compatibility.baseHead; },
    e => { e.acknowledgment.id = first.acknowledgment.id; }
  ]) {
    const stale = renewalEvent(state, 45);
    change(stale);
    rebindRenewal(stale);
    assert.throws(() => applyEvent(state, stale, renewalContext(stale)));
  }
  for (const change of [
    s => { delete s.recoveryRenewals; },
    s => { s.recoveryRenewals = []; },
    s => { s.version = 6; },
    s => { s.recoveryRenewals[0].event.proof.compatibility.receipt = 'altered'; },
    s => { s.recoveryRenewals[0].before.blocker = null; },
    s => { s.recoveryRenewals[0].commands = [{ type: 'resume' }]; },
    s => { s.seen = s.seen.filter(id => id !== first.id); },
    s => { s.recoveryRenewals[1].event.acknowledgment.id = first.acknowledgment.id; }
  ]) {
    const altered = structuredClone(result.state);
    change(altered);
    assert.throws(() => validate(altered));
  }
  let published = applyEvent(result.state, { id: 'renewed-publication', type: 'published',
    key: 'incident-owner', owner: 'synthetic-owner', proof: { pushedHead: sha(9), remoteHead: sha(9),
      pr: 92, ready: true, enrolled: true } }).state;
  assert.deepEqual(published.recoveryRenewals, result.state.recoveryRenewals);
  published = applyEvent(published, { id: 'renewed-cleanup', type: 'completed', key: 'incident-owner',
    owner: 'synthetic-owner', proof: { remoteHead: sha(9), stoppedWriting: true, worktreeAbsent: true,
      branchAbsent: true, pendingInstructions: 0 } }).state;
  assert.deepEqual(published.recoveryRenewals, result.state.recoveryRenewals);
  assert.deepEqual(published.recoveries, before.recoveries);
  assert.throws(() => validateRecoveryContinuation(published, continuation(published, 48, true), { revision: 48 }));
});

test('documented renewal example executes with only synthetic compatibility and leaves original grant main unchanged', () => {
  const readme = fs.readFileSync(path.join(__dirname, '..', 'README.md'), 'utf8');
  const section = readme.split('#### Runnable synthetic renewal example')[1].split('## Shrink-only reservation protocol')[0];
  const expression = section.match(/```javascript\r?\n([\s\S]*?)```/)[1];
  const cli = spawnSync(process.execPath, ['-e', expression], { encoding: 'utf8' });
  assert.equal(cli.status, 0, cli.stderr);
  const result = JSON.parse(cli.stdout);
  assert.deepEqual(result.actions, []);
  assert.equal(result.checked.coverageVerified, true);
  assert.equal(result.checked.sourceExecutionAuthorized, false);
  assert.ok(result.checked.blockers.includes('unrelated'));
});

test('incident recovery is prospective only, retains same owner, blockers, restored history and all held guards', () => {
  for (const mode of ['enumerated-pending-zero', 'human-approved-source-fence']) {
    const state = incidentFixture();
    const original = structuredClone(state);
    const event = recoveryEvent(state, 41, mode);
    const result = applyEvent(state, event, recoveryContext(event));
    const item = result.state.items[0];
    assert.deepEqual(result.actions, []);
    assert.deepEqual(state, original);
    assert.equal(result.state.version, 6);
    assert.equal(item.owner, state.items[0].owner);
    assert.equal(item.blocker, state.items[0].blocker);
    assert.equal(item.sourceWritesRetired, false);
    assert.equal(item.phase, 'running');
    assert.equal(item.scopeRevision, 1);
    assert.deepEqual(item.scopes, [...state.items[0].scopes, incidentPaths.at(-1), 'third-party-notices.md']);
    assert.deepEqual(result.state.recoveries[0].event.proof.manifest.actual, incidentPaths);
    assert.deepEqual(result.state.recoveries[0].event.proof.incident.unauthorizedPaths, [incidentPaths.at(-1)]);
    assert.deepEqual(result.state.recoveries[0].event.proof.workingTree.restored, [incidentPaths.at(-1)]);
    assert.equal(result.state.items[1].blocker, state.items[1].blocker);
    assert.deepEqual(result.state.items[1].publication, state.items[1].publication);
    assert.deepEqual(heldReservations(result.state, item.key), heldReservations(state, item.key));
    assert.deepEqual(applyEvent(result.state, event, { revision: 42 }).actions, []);
    assert.throws(() => applyEvent(result.state, { ...event, type: 'pause' }), /replay/);
    const request = continuation(result.state);
    assert.deepEqual(validateRecoveryContinuation(result.state, request, { revision: 42 }),
      { coverageVerified: true, blockers: item.blocker, sourceExecutionAuthorized: false });
    for (const change of [
      r => { r.fence = null; r.grantDigest = 'old-grant'; },
      r => { r.owner = 'replacement'; },
      r => { r.head = sha(8); },
      r => { r.sourceHead = sha(8); },
      r => { r.checkpointRevision--; },
      r => { r.scopeRevision--; },
      r => { r.acknowledgment.workJoined = false; },
      r => { r.acknowledgment.staleAuthorityRejected = false; },
      r => { r.acknowledgment.id = event.acknowledgment.id; },
      r => { r.fence = { epoch: 0, nonce: 'stale-broad-instruction' }; }
    ]) {
      const stale = structuredClone(request);
      change(stale);
      assert.throws(() => validateRecoveryContinuation(result.state, stale, { revision: 42 }));
    }
  }
});

test('incident recovery rejects stale, missing, forged or incomplete evidence without mutating state', () => {
  const state = incidentFixture();
  const changes = [
    e => { e.proof.repository = 'other/repository'; },
    e => { e.proof.coordinator = 'stale'; },
    e => { e.proof.owner = 'replacement'; },
    e => { e.proof.head = sha(7); },
    e => { e.proof.source.head = sha(7); },
    e => { e.proof.source.owner = 'other-owner'; },
    e => { e.proof.source.worktree = 'relative'; },
    e => { e.proof.source.branch = ''; },
    e => { e.proof.checkpointRevision--; },
    e => { e.proof.expectedItemDigest = '0'.repeat(64); },
    e => { e.proof.scopeRevision++; },
    e => { e.proof.reservation.head = sha(6); },
    e => { e.proof.fromScopes.pop(); },
    e => { e.proof.additionalScopes.push('src'); e.proof.toScopes.push('src'); },
    e => { e.proof.toScopes.push('extra'); },
    e => { e.proof.incident.unauthorizedPaths = []; },
    e => { e.proof.incident.command.receipt = ''; },
    e => { e.proof.incident.originalScopes.pop(); },
    e => { e.proof.workingTree.restored = []; },
    e => { e.proof.manifest.actual.pop(); },
    e => { e.proof.preservation.unpreserved = []; },
    e => { e.proof.manifest.complete = false; },
    e => { e.proof.quiescence.sourcePaused = false; },
    e => { e.proof.quiescence.commandsRunning = true; },
    e => { e.proof.quiescence.workJoined = false; },
    e => { delete e.proof.reconciliation.fence; },
    e => { e.proof.reconciliation.pendingInstructions = 0; },
    e => { e.proof.reconciliation.fence.established = false; },
    e => { e.proof.reconciliation.fence.revokesEarlierSourceAuthority = false; },
    e => { e.proof.exclusivity.reservations = []; },
    e => { e.proof.exclusivity.complete = false; },
    e => { e.proof.exclusivity.reservations[0].scopes = ['src']; },
    e => { e.acknowledgment.owner = 'forged-owner'; },
    e => { e.acknowledgment.workJoined = false; },
    e => { e.acknowledgment.staleAuthorityRejected = false; },
    e => { e.authorization.kind = 'implementation-approval'; },
    e => { e.authorization.actor = ''; },
    e => { e.authorization.decision = 'retroactively-authorize'; }
  ];
  for (const change of changes) {
    const event = recoveryEvent(state);
    change(event);
    rebindRecovery(event);
    const before = JSON.stringify(state);
    assert.throws(() => applyEvent(state, event, { ...recoveryContext(event), revision: 41 }),
      undefined, change.toString());
    assert.equal(JSON.stringify(state), before);
  }
  const event = recoveryEvent(state);
  assert.throws(() => applyEvent(state, event, { revision: 41 }), /fields/);
  for (const change of [
    c => { c.recoveryEvidence.humanDecisionDigest = 'forged-receipt-only'; },
    c => { c.recoveryEvidence.ownerResponseId = 'stale'; },
    c => { c.recoveryEvidence.instructionAuthorityVerified = false; },
    c => { c.recoveryEvidence.commandsJoinedVerified = false; }
  ]) {
    const context = recoveryContext(event);
    change(context);
    assert.throws(() => applyEvent(state, event, context), /host readback/);
  }
  for (const part of ['authorization', 'acknowledgment']) {
    const stale = structuredClone(event);
    stale[part][part === 'authorization' ? 'subjectDigest' : 'proofDigest'] = '0'.repeat(64);
    assert.throws(() => applyEvent(state, stale, recoveryContext(stale)));
  }
});

test('ordinary extension still rejects the incidental write and UNKNOWN; retired owners cannot recover', () => {
  const state = incidentFixture();
  const e = recoveryEvent(state);
  const p = e.proof;
  const extension = { id: 'ordinary', type: 'extend-scopes', key: p.key, owner: p.owner,
    expectedScopeRevision: 0, proof: { version: 1, owner: p.owner, key: p.key,
      reservation: p.reservation, scopeRevision: 0, fromScopes: p.fromScopes,
      additionalScopes: p.additionalScopes, toScopes: p.toScopes, phase: 'running',
      sourceWritesRetired: false, manifest: p.manifest, preservation: p.preservation,
      quiescence: { paused: true, pendingInstructions: 0, receipt: 'Synthetic pause.' },
      unionFrozen: true, exclusivity: p.exclusivity, receipt: 'Synthetic ordinary extension.' },
    acknowledgment: { id: 'ordinary-ack', receipt: 'Synthetic response.', owner: p.owner,
      key: p.key, scopeRevision: 0, proofDigest: '' } };
  extension.acknowledgment.proofDigest = digest(extension.proof);
  assert.throws(() => applyEvent(state, extension), /cannot be legalized/);
  extension.proof.quiescence.pendingInstructions = 'UNKNOWN';
  assert.throws(() => applyEvent(state, extension), /pending-zero/);
  const retired = applyEvent(refinementFixture(), refinementEvent(refinementFixture())).state;
  const wrongOwner = recoveryEvent(state);
  wrongOwner.proof.key = retired.items[0].key;
  wrongOwner.proof.owner = retired.items[0].owner;
  rebindRecovery(wrongOwner);
  assert.throws(() => applyEvent(retired, wrongOwner, recoveryContext(wrongOwner)));
  const superseded = supersessionFixture();
  const frozen = applyEvent(superseded, supersessionEvent(superseded), { revision: 41 }).state;
  assert.throws(() => applyEvent(frozen, e, recoveryContext(e)));
});

test('nine dirty files and touched-then-restored variants both retain unauthorized provenance', () => {
  const state = incidentFixture();
  const event = recoveryEvent(state);
  event.proof.workingTree.dirty = [...incidentPaths];
  event.proof.workingTree.restored = [];
  event.proof.preservation.retained.push({ path: incidentPaths.at(-1), receipt: 'Synthetic ninth dirty lock retained.' });
  event.proof.preservation.unpreserved = [];
  rebindRecovery(event);
  const result = applyEvent(state, event, recoveryContext(event));
  assert.equal(result.state.recoveries[0].event.proof.workingTree.dirty.length, 9);
  assert.deepEqual(result.state.recoveries[0].event.proof.incident.unauthorizedPaths, [incidentPaths.at(-1)]);
  assert.deepEqual(result.actions, []);
});

test('ordinary extensions before/after recovery and later strict refinement retain the complete mixed history', () => {
  let state = incidentFixture();
  state.items[0].scopes.push('src\\unused.cs');
  const initialExtension = extensionEvent(state, 'incident-owner', ['docs\\planned.md'], 'before-recovery');
  state = applyEvent(state, initialExtension).state;
  const event = recoveryEvent(state);
  event.proof.manifest.planned.push('docs\\planned.md');
  rebindRecovery(event);
  state = applyEvent(state, event, recoveryContext(event)).state;
  const history = structuredClone(state.recoveries);
  const extension = extensionEvent(state, 'incident-owner', ['docs\\later.md'], 'after-recovery');
  extension.proof.manifest = structuredClone(event.proof.manifest);
  extension.proof.manifest.planned.push('docs\\later.md');
  extension.proof.preservation = structuredClone(event.proof.preservation);
  extension.acknowledgment.proofDigest = digest(extension.proof);
  state = applyEvent(state, extension).state;
  assert.equal(state.items[0].scopeRevision, 3);
  assert.deepEqual(state.recoveries, history);
  const item = state.items[0];
  const refinement = refinementEvent(refinementFixture(), 'after-recovery-shrink');
  Object.assign(refinement, { key: item.key, owner: item.owner, expectedScopeRevision: item.scopeRevision });
  Object.assign(refinement.proof, { owner: item.owner, key: item.key,
    reservation: event.proof.reservation, scopeRevision: item.scopeRevision, fromScopes: [...item.scopes],
    toScopes: item.scopes.filter(value => value !== 'src\\unused.cs'),
    manifest: structuredClone(extension.proof.manifest),
    preservation: structuredClone(extension.proof.preservation) });
  Object.assign(refinement.acknowledgment, { owner: item.owner, key: item.key,
    scopeRevision: item.scopeRevision, proofDigest: digest(refinement.proof) });
  state = applyEvent(state, refinement).state;
  assert.equal(state.items[0].sourceWritesRetired, true);
  assert.equal(state.items[0].scopeRevision, 4);
  assert.deepEqual(state.recoveries, history);
  assert.throws(() => validateRecoveryContinuation(state, continuation(state), { revision: 42 }));
});

test('subsequent incident preserves restored history and requires a fresh monotonic human-approved fence', () => {
  const initial = incidentFixture();
  const first = recoveryEvent(initial);
  const state = applyEvent(initial, first, recoveryContext(first)).state;
  function next(mode = 'human-approved-source-fence') {
    const event = recoveryEvent(state, 42, mode);
    event.id = 'second-recovery';
    event.acknowledgment.id = 'second-owner-ack';
    event.authorization.id = 'second-human-decision';
    const p = event.proof;
    p.incident.id = 'second-incident';
    p.incident.command.id = 'second-command';
    p.additionalScopes = ['src\\new-lock.json'];
    p.toScopes = [...p.fromScopes, ...p.additionalScopes];
    p.exactFiles.paths = [...p.additionalScopes];
    p.incident.unauthorizedPaths = [...p.additionalScopes];
    p.manifest.actual.push(...p.additionalScopes);
    p.manifest.planned.push(...p.additionalScopes);
    p.workingTree.dirty.push(...p.additionalScopes);
    p.preservation.retained.push({ path: p.additionalScopes[0], receipt: 'Synthetic second retained incidental effect.' });
    if (p.reconciliation.fence) {
      p.reconciliation.fence.epoch = 2;
      p.reconciliation.fence.nonce = 'synthetic-fence-nonce-2';
    }
    return rebindRecovery(event);
  }
  const event = next();
  const result = applyEvent(state, event, recoveryContext(event));
  assert.equal(result.state.recoveries.length, 2);
  assert.deepEqual(result.state.recoveries[0], state.recoveries[0]);
  assert.throws(() => validateRecoveryContinuation(result.state, continuation(state), { revision: 42 }));
  for (const change of [
    e => { e.proof.reconciliation.fence.epoch = 1; },
    e => { e.proof.reconciliation.fence.nonce = first.proof.reconciliation.fence.nonce; },
    e => { e.acknowledgment.id = first.acknowledgment.id; },
    e => { e.authorization.id = first.authorization.id; },
    e => {
      e.proof.manifest.actual = e.proof.manifest.actual.filter(value => value !== incidentPaths.at(-1));
      e.proof.workingTree.restored = [];
      e.proof.preservation.unpreserved = [];
    }
  ]) {
    const stale = next();
    change(stale);
    rebindRecovery(stale);
    assert.throws(() => applyEvent(state, stale, recoveryContext(stale)));
  }
  const droppedFence = next('enumerated-pending-zero');
  assert.throws(() => applyEvent(state, droppedFence, recoveryContext(droppedFence)), /cannot be dropped/);
});

test('documented synthetic recovery input and both in-memory modes execute without host or live checkpoint', () => {
  const directory = __dirname;
  const readme = fs.readFileSync(path.join(directory, '..', 'README.md'), 'utf8');
  const section = readme.split('## Explicit human-authorized scope-incident recovery')[1]
    .split('## Shrink-only reservation protocol')[0];
  const example = section.match(/```javascript\r?\n([\s\S]*?)```/)[1];
  const exampleResult = spawnSync(process.execPath, ['-e', example], { encoding: 'utf8' });
  assert.equal(exampleResult.status, 0, exampleResult.stderr);
  const lines = exampleResult.stdout.trim().split(/\r?\n/).map(line => JSON.parse(line));
  assert.deepEqual(lines.map(line => line.mode), ['enumerated-pending-zero', 'human-approved-source-fence']);
  for (const result of lines) {
    assert.deepEqual(result.actions, []);
    assert.equal(result.checked.coverageVerified, true);
    assert.equal(result.checked.sourceExecutionAuthorized, false);
    assert.ok(result.checked.blockers.includes('unrelated'));
  }
  const generated = spawnSync(process.execPath, [path.join(directory, 'incident-recovery-fixture.cjs')],
    { encoding: 'utf8' });
  assert.equal(generated.status, 0, generated.stderr);
  const input = JSON.parse(generated.stdout);
  assert.equal(input.expectedRevision, 41);
  assert.deepEqual(applyEvent(incidentFixture(), input.event, { revision: input.expectedRevision,
    recoveryEvidence: input.recoveryEvidence }).actions, []);
  for (const file of ['README.md', 'SKILL.md', 'worker.md', 'planner.md']) {
    const markdown = fs.readFileSync(path.join(directory, '..', file), 'utf8');
    assert.match(markdown, /UNKNOWN/);
    assert.match(markdown, /solution-project locks/);
    for (const [, target] of markdown.matchAll(/\[[^\]]+\]\((\.[^)]+)\)/g)) {
      const [relative, anchor] = target.split('#');
      const destination = path.resolve(directory, '..', relative || file);
      assert.ok(fs.existsSync(destination), `${file}: missing ${target}`);
      if (!anchor || !destination.endsWith('.md')) continue;
      const headings = [...fs.readFileSync(destination, 'utf8').matchAll(/^#+ (.+)$/gm)]
        .map(match => match[1].trim().toLowerCase().replace(/[`]/g, '')
          .replace(/[^\p{L}\p{N}\s-]/gu, '').replace(/\s/g, '-'));
      assert.ok(headings.includes(anchor), `${file}: missing heading ${target}`);
    }
  }
});

test('recovery migration preserves v1-v5 protocols and immutable histories survive later publication and cleanup', () => {
  const legacy = incidentFixture();
  const states = [legacy];
  for (const version of [2, 3, 4, 5]) {
    const state = structuredClone(legacy);
    state.version = version;
    state.refinements = [];
    if (version >= 3) state.amendments = [];
    if (version >= 4) state.readmissions = [];
    if (version >= 5) state.supersessions = [];
    for (const item of state.items) {
      item.scopeRevision = 0; item.scopeHistory = []; item.sourceWritesRetired = false;
      if (version >= 3) item.scopeAmendments = [];
      if (version >= 4) item.admissionHistory = [];
    }
    states.push(state);
  }
  for (const state of states) {
    validate(state);
    const event = recoveryEvent(state);
    let result = applyEvent(state, event, recoveryContext(event)).state;
    const history = structuredClone(result.recoveries);
    for (const change of [
      s => { delete s.recoveries; },
      s => { s.recoveries = []; },
      s => { s.items[0].scopeRecoveries = []; },
      s => { s.version = 5; },
      s => { s.recoveries[0].event.proof.workingTree.restored = []; },
      s => { s.recoveries[0].event.authorization.receipt = 'altered'; },
      s => { s.recoveries[0].before.blocker = null; },
      s => { s.seen = s.seen.filter(id => id !== event.id); },
      s => { s.items[0].scopes.pop(); },
      s => { s.recoveries[0].commands = [{ type: 'resume' }]; }
    ]) {
      const altered = structuredClone(result);
      change(altered);
      assert.throws(() => validate(altered));
    }
    const changed = structuredClone(event);
    changed.authorization.receipt = 'altered replay';
    assert.throws(() => applyEvent(result, changed), /replay/);
    result = applyEvent(result, { id: 'later-publication', type: 'published',
      key: event.proof.key, owner: event.proof.owner,
      proof: { pushedHead: sha(2), remoteHead: sha(2), pr: 92, ready: true, enrolled: true } }).state;
    assert.deepEqual(result.recoveries, history);
    assert.equal(history[0].before.blocker, state.items[0].blocker);
    assert.throws(() => applyEvent(result, { id: 'unknown-cleanup', type: 'completed',
      key: event.proof.key, owner: event.proof.owner, proof: { remoteHead: sha(2),
        stoppedWriting: true, worktreeAbsent: true, branchAbsent: true, pendingInstructions: 'UNKNOWN' } }));
    result = applyEvent(result, { id: 'later-cleanup', type: 'completed',
      key: event.proof.key, owner: event.proof.owner, proof: { remoteHead: sha(2),
        stoppedWriting: true, worktreeAbsent: true, branchAbsent: true, pendingInstructions: 0 } }).state;
    assert.deepEqual(result.recoveries, history);
    assert.equal(result.items[1].blocker, state.items[1].blocker);
  }
});

test('recovery migration retains nonempty unrelated retirement and supersession proofs verbatim', () => {
  const refinement = refinementFixture(true);
  const refined = applyEvent(refinement, refinementEvent(refinement)).state;
  const supersession = supersessionFixture();
  const superseded = applyEvent(supersession, supersessionEvent(supersession), { revision: 41 }).state;
  for (const original of [refined, superseded]) {
    const item = incidentFixture().items[0];
    const state = applyEvent(original, { ...item, id: 'adopt-extra-incident', type: 'adopt' }).state;
    const event = recoveryEvent(state);
    const result = applyEvent(state, event, recoveryContext(event));
    assert.deepEqual(result.state.refinements, original.refinements);
    assert.deepEqual(result.state.supersessions, original.supersessions ?? []);
    assert.deepEqual(result.actions, []);
    assert.equal(result.state.items[0].owner, original.items[0].owner);
    assert.equal(result.state.items[0].blocker, original.items[0].blocker);
    assert.deepEqual(result.state.items[0].publication, original.items[0].publication);
    assert.deepEqual(result.state.items[0].scopeHistory, original.items[0].scopeHistory);
    validate(result.state);
  }
});

test('exact local supersession atomically reserves only its beneficiary and preserves historical owners and slots', () => {
  const state = supersessionFixture();
  const before = structuredClone(state);
  const event = supersessionEvent(state);
  const result = applyEvent(state, event, { revision: 41 });
  assert.equal(result.state.version, 5);
  assert.deepEqual(result.state.items.slice(0, 2).map(item => {
    const value = structuredClone(item);
    delete value.scopeRevision; delete value.scopeHistory; delete value.scopeAmendments;
    delete value.admissionHistory; delete value.sourceWritesRetired;
    return value;
  }), before.items.slice(0, 2));
  assert.deepEqual(state, before);
  assert.deepEqual(result.actions, [{ type: 'launch', key: 'successor', pool: 'ready' }]);
  assert.equal(result.state.items.filter(item => item.phase !== 'candidate').length, 3);
  const held = heldReservations(result.state, 'successor');
  assert.deepEqual(held.map(item => item.scopes), [['docs\\retained.md'], []]);
  assert.ok(held.every(item => item.sourceWritesRetired));
  assert.ok(result.state.items.slice(0, 2).every(item => item.sourceWritesRetired === false));
  assert.deepEqual(applyEvent(result.state, event, { revision: 42 }).actions, []);
  for (const altered of [{ ...event, type: 'pause' },
    { ...event, authorization: { ...event.authorization, receipt: 'changed' } }]) {
    assert.throws(() => applyEvent(result.state, altered, { revision: 42 }), /replay/);
  }
  validate(result.state);
});

test('supersession rejects incomplete, stale, ancestor, unknown, unpreserved or unauthorized proof without mutation', () => {
  const state = supersessionFixture();
  const changes = [
    e => { e.proof.holders.pop(); },
    e => { e.proof.localReservations.pop(); },
    e => { e.proof.head = sha(8); },
    e => { e.proof.coordinator = 'different'; },
    e => { e.proof.repository = 'other/repository'; },
    e => { e.proof.expectedItemDigest = '0'.repeat(64); },
    e => { e.proof.candidate.scopes.push('src\\extra.cs'); },
    e => { e.proof.manifestComplete = false; },
    e => { e.proof.authority.singleLedger = false; },
    e => { e.proof.exactFiles.paths.pop(); },
    e => { e.proof.exactFiles.complete = false; },
    e => { e.proof.holders[0].paths = ['src']; },
    e => { e.proof.holders[0].stopped.pendingInstructions = null; },
    e => { e.proof.holders[0].stopped.writingStopped = false; },
    e => { e.proof.holders[0].preservation.unpreserved = [{ path: 'src\\shared.cs', receipt: 'lost' }]; },
    e => { e.proof.holders[0].preservation.commits = []; },
    e => { e.proof.holders[0].itemDigest = '0'.repeat(64); },
    e => { e.proof.holders[0].scopeRevision++; },
    e => { e.authorization.humanAuthorized = false; },
    e => { e.proof.mode = 'activate-external'; }
  ];
  for (const change of changes) {
    const event = supersessionEvent(state);
    change(event);
    rebindSupersession(event);
    assert.throws(() => applyEvent(state, event, { revision: 41 }));
    assert.deepEqual(state, supersessionFixture());
  }
  assert.throws(() => applyEvent(state, supersessionEvent(state)), /revision/);
  assert.throws(() => applyEvent(state, supersessionEvent(state), { revision: 40 }), /revision/);
  const forged = supersessionEvent(state);
  forged.proof.holders[0].acknowledgment.proofDigest = '0'.repeat(64);
  forged.authorization.proofDigest = digest(forged.proof);
  assert.throws(() => applyEvent(state, forged, { revision: 41 }), /acknowledgment/);
});

test('supersession acknowledgment binds the complete multi-holder proof, not only its own holder body', () => {
  const state = supersessionFixture();
  const event = supersessionEvent(state);
  event.proof.holders[1].stopped.receipt = 'Changed independently verified second-holder evidence.';
  event.proof.holders[1].acknowledgment.proofDigest =
    supersessionAcknowledgmentDigest(event.proof, event.proof.holders[1]);
  event.authorization.proofDigest = digest(event.proof);
  assert.throws(() => applyEvent(state, event, { revision: 41 }), /acknowledgment/);
  assert.deepEqual(state, supersessionFixture());
});

test('superseded owners cannot extend, refine, readmit or regain transferred writes after cleanup/resume', () => {
  const state = applyEvent(supersessionFixture(),
    supersessionEvent(supersessionFixture()), { revision: 41 }).state;
  for (const type of ['extend-scopes', 'refine-scopes', 'revalidate-candidate']) {
    assert.throws(() => applyEvent(state, { id: type, type, owner: 'owner-0', key: 'prior-0' }));
  }
  let next = applyEvent(state, { id: 'clean-old', type: 'completed', owner: 'owner-0', key: 'prior-0',
    proof: { remoteHead: state.head, stoppedWriting: true, worktreeAbsent: true,
      branchAbsent: true, pendingInstructions: 0 } }).state;
  next = applyEvent(next, { id: 'pause-new', type: 'pause' }).state;
  next = applyEvent(next, { id: 'resume-new', type: 'resume', owner: next.coordinator,
    authorityConfirmed: true, discover: false }).state;
  assert.deepEqual(next.supersessions, state.supersessions);
  assert.equal(next.items[0].sourceWritesRetired, false);
  validate(next);
  for (const change of [
    s => { s.version = 4; },
    s => { s.supersessions = []; },
    s => { s.items[0].scopes.push('src\\reclaimed.cs'); },
    s => { s.items[0].sourceWritesRetired = true; },
    s => { s.supersessions[0].event.proof.holders.pop(); },
    s => { s.supersessions[0].event.authorization.receipt = 'forged'; },
    s => { s.seen = s.seen.filter(value => value !== 'supersede-once'); }
  ]) {
    const altered = structuredClone(state);
    change(altered);
    assert.throws(() => validate(altered));
  }
});

test('external multi-holder preparation preserves every guard and cannot activate or launch', () => {
  const state = supersessionFixture();
  const snapshots = ['external-one', 'external-two'].map(coordinator => {
    let external = supersessionFixture();
    external.coordinator = coordinator;
    return { repository: 'roryprimrose/Kora', revision: 122, state: external,
      stateDigest: digest(external), receipt: 'Synthetic complete external checkpoint read.' };
  });
  const event = supersessionEvent(state, 41, 'prepare-external', snapshots);
  for (const holder of event.proof.holders) {
    holder.stopped = null;
    holder.acknowledgment = null;
    holder.manifest = null;
    holder.preservation = null;
  }
  event.authorization.proofDigest = digest(event.proof);
  const result = applyEvent(state, event, { revision: 41 });
  assert.equal(result.state.items.at(-1).phase, 'candidate');
  assert.equal(result.actions.some(action => action.type === 'launch'), false);
  assert.equal(heldReservations(result.state, 'successor').length, 6);
  assert.ok(heldReservations(result.state, 'successor').every(item => item.scopes.length > 0));
  const missing = structuredClone(event);
  missing.proof.holders.pop();
  missing.authorization.proofDigest = digest(missing.proof);
  assert.throws(() => applyEvent(state, missing, { revision: 41 }), /holder/);
  const active = rebindSupersession({ ...structuredClone(event),
    proof: { ...structuredClone(event.proof), mode: 'activate-local' } });
  active.proof.authority.singleLedger = true;
  active.authorization.proofDigest = digest(active.proof);
  assert.throws(() => applyEvent(state, active, { revision: 41 }), /External/);
  const stale = structuredClone(event);
  stale.proof.externalCheckpoints[0].state.items[0].scopes.pop();
  stale.authorization.proofDigest = digest(stale.proof);
  assert.throws(() => applyEvent(state, stale, { revision: 41 }), /snapshot/);
});

test('supersession is not slot release and its beneficiary fence prevents rival dispatch, adoption and readmission', () => {
  let state = supersessionFixture();
  for (let index = 0; index < 8; index++) {
    state = applyEvent(state, { id: `fill-${index}`, type: 'adopt', owner: `filler-${index}`,
      key: `fill-${index}`, outcome: `filler outcome ${index}`, head: state.head,
      scopes: [`other\\${index}.cs`], pool: 'ready', kind: 'feature', evidence: 'Synthetic held slot.' }).state;
  }
  state = applyEvent(state, { id: 'rival', type: 'candidate', owner: 'planner', pass: 1,
    key: 'rival', outcome: 'rival outcome', head: state.head, scopes: ['src\\shared.cs'],
    pool: 'ready', kind: 'feature', evidence: 'Independent proposal, not beneficiary authority.' }).state;
  // Put the rival first to distinguish a beneficiary fence from incidental dispatch order.
  state.items.unshift(state.items.pop());
  const event = supersessionEvent(state);
  let result = applyEvent(state, event, { revision: 41 });
  assert.equal(result.actions.some(action => action.type === 'launch'), false);
  assert.equal(result.state.items.filter(item => !['candidate', 'complete'].includes(item.phase)).length, 10);
  assert.equal(result.state.items.find(item => item.key === 'successor').phase, 'candidate');
  assert.throws(() => applyEvent(result.state, { id: 'rival-adopt', type: 'adopt', owner: 'rogue-owner',
    key: 'rogue', outcome: 'rogue outcome', head: state.head, scopes: ['src\\shared.cs'],
    pool: 'repair', kind: 'repair', pr: 999, evidence: 'No authority to take transferred file.' }), /bound|Conflicting/);
  const rival = result.state.items.find(item => item.key === 'rival');
  assert.throws(() => applyEvent(result.state, { id: 'rival-readmit', type: 'revalidate-candidate',
    key: rival.key, owner: 'planner', pass: 1, expectedItemDigest: digest(rival), head: state.head,
    scopes: rival.scopes, evidence: 'Fresh evidence is not a transfer grant.', manifestComplete: true }), /conflicts/);
  result = applyEvent(result.state, { id: 'release-prior', type: 'completed', owner: 'owner-0',
    key: 'prior-0', proof: { remoteHead: state.head, stoppedWriting: true, worktreeAbsent: true,
      branchAbsent: true, pendingInstructions: 0 } });
  assert.deepEqual(result.actions.filter(action => action.type === 'launch'),
    [{ type: 'launch', key: 'successor', pool: 'ready' }]);
  assert.equal(result.state.items.find(item => item.key === 'rival').phase, 'candidate');
});

test('supersession keeps truthful later publication and cleanup possible and distinguishes explicit revocation', () => {
  const state = supersessionFixture();
  const prior = state.items[0];
  prior.phase = 'running'; prior.publication = null; prior.pr = null;
  const event = supersessionEvent(state);
  event.proof.holders[0].acknowledgment.kind = 'revocation';
  event.proof.holders[0].acknowledgment.receipt = 'Synthetic separate explicit revocation authority.';
  event.authorization.proofDigest = digest(event.proof);
  let result = applyEvent(state, event, { revision: 41 }).state;
  result = applyEvent(result, { id: 'publish-old-later', type: 'published', key: prior.key,
    owner: prior.owner, proof: { pushedHead: state.head, remoteHead: state.head,
      pr: 919, ready: true, enrolled: true } }).state;
  result = applyEvent(result, { id: 'cleanup-old-later', type: 'completed', key: prior.key,
    owner: prior.owner, proof: { remoteHead: state.head, stoppedWriting: true,
      worktreeAbsent: true, branchAbsent: true, pendingInstructions: 0 } }).state;
  assert.equal(result.items[0].phase, 'complete');
  assert.equal(result.items[0].sourceWritesRetired, false);
  assert.equal(result.supersessions[0].event.proof.holders[0].acknowledgment.kind, 'revocation');
  validate(result);
});

test('beneficiary fence releases only after complete cleanup for later dispatch, adoption and revalidation', () => {
  const { reserved, running, published, completion, complete } = supersessionBeneficiaryFixture();
  const blocked = applyEvent(published, { id: 'review-successor-blocked', type: 'blocked',
    key: 'successor', owner: 'review-successor', reason: 'Beneficiary cleanup is still blocked.' }).state;
  for (const state of [reserved, running, published, blocked]) {
    assert.ok(heldReservations(state, 'later-feature').some(item =>
      item.key === 'supersession:supersede-once:successor'));
  }
  for (const proof of [
    { ...completion.proof, stoppedWriting: false },
    { ...completion.proof, worktreeAbsent: false },
    { ...completion.proof, branchAbsent: false },
    { ...completion.proof, pendingInstructions: null }
  ]) {
    assert.throws(() => applyEvent(published, { ...completion, proof }), /cleanup/);
    assert.deepEqual(published, supersessionBeneficiaryFixture().published);
  }
  assert.equal(complete.items.find(item => item.key === 'successor').phase, 'complete');
  assert.equal(heldReservations(complete, 'later-feature').some(item =>
    item.key === 'supersession:supersede-once:successor'), false);
  assert.deepEqual(complete.supersessions, reserved.supersessions);
  assert.deepEqual(complete.items.slice(0, 2), reserved.items.slice(0, 2));
  assert.deepEqual(heldReservations(complete, 'later-feature').map(item => item.scopes),
    [['docs\\retained.md'], []]);
  const candidate = { id: 'review-later-feature', type: 'candidate', owner: 'planner', pass: 1,
    key: 'later-feature', outcome: 'later independent outcome', head: complete.head,
    scopes: ['src\\shared.cs'], pool: 'ready', kind: 'feature', evidence: 'Fresh independent admission.' };
  const dispatched = applyEvent(complete, candidate);
  assert.deepEqual(dispatched.actions, [{ type: 'launch', key: candidate.key, pool: 'ready' }]);
  const adopted = applyEvent(complete, { ...candidate, id: 'review-later-adopt', type: 'adopt',
    owner: 'review-later-owner' }).state;
  assert.equal(adopted.items.at(-1).phase, 'running');
  const waiting = applyEvent(complete, { ...candidate, id: 'review-later-maintenance',
    kind: 'maintenance', scopes: ['later\\helper.cs'] }).state;
  const item = waiting.items.at(-1);
  const revalidated = applyEvent(waiting, { id: 'review-later-revalidated', type: 'revalidate-candidate',
    key: item.key, owner: 'planner', pass: 1, expectedItemDigest: digest(item), head: waiting.head,
    scopes: [...item.scopes, 'src\\shared.cs'], evidence: 'Fresh expanded independent maintenance manifest.',
    manifestComplete: true }).state;
  assert.deepEqual(revalidated.items.at(-1).scopes, ['later\\helper.cs', 'src\\shared.cs']);
  for (const type of ['extend-scopes', 'refine-scopes', 'revalidate-candidate']) {
    assert.throws(() => applyEvent(complete, { id: `review-old-${type}`, type,
      owner: 'owner-0', key: 'prior-0' }), /fresh authority/);
  }
  assert.throws(() => applyEvent(complete, { ...candidate, id: 'review-old-revival', type: 'adopt',
    owner: 'owner-0' }), /Duplicate or invalid owner/);
});

test('external preparation imports completed-beneficiary release without dropping remaining external guards', () => {
  const external = supersessionBeneficiaryFixture().complete;
  const base = supersessionFixture();
  base.coordinator = 'review-import-coordinator';
  base.items = [structuredClone(base.items.find(item => item.key === 'successor'))];
  base.items[0].scopes = ['docs\\retained.md'];
  validate(base);
  const event = supersessionEvent(base, 41, 'prepare-external',
    [{ repository: 'roryprimrose/Kora', revision: 45, state: external,
      stateDigest: digest(external), receipt: 'Synthetic post-cleanup external ledger snapshot.' }]);
  const prepared = applyEvent(base, event, { revision: 41 }).state;
  assert.equal(prepared.items[0].phase, 'candidate');
  const guards = heldReservations(prepared, 'later-feature');
  assert.ok(guards.some(item => item.scopes.includes('docs\\retained.md')));
  assert.equal(guards.some(item => item.scopes.includes('src\\shared.cs')), false);
  const later = { id: 'review-import-later', type: 'candidate', owner: 'planner', pass: 1,
    key: 'later-feature', outcome: 'later independent imported outcome', head: prepared.head,
    scopes: ['src\\shared.cs'], pool: 'ready', kind: 'feature', evidence: 'Independent exact-file admission.' };
  assert.deepEqual(applyEvent(prepared, later).actions,
    [{ type: 'launch', key: 'later-feature', pool: 'ready' }]);
  assert.throws(() => applyEvent(prepared, { ...later, type: 'adopt', owner: 'review-external-rogue',
    scopes: ['docs\\retained.md'] }), /Conflicting external/);
  assert.deepEqual(prepared.supersessions[0].event.proof.externalCheckpoints[0].state, external);
});

test('supersession refuses ancestor coverage and authentic-looking but unbound receipts in old schema versions', () => {
  for (const version of [1, 2, 3, 4]) {
    const state = supersessionFixture();
    if (version >= 2) {
      state.version = version; state.refinements = [];
      for (const item of state.items) {
        item.scopeRevision = 0; item.scopeHistory = []; item.sourceWritesRetired = false;
        if (version >= 3) item.scopeAmendments = [];
        if (version >= 4) item.admissionHistory = [];
      }
      if (version >= 3) state.amendments = [];
      if (version >= 4) state.readmissions = [];
    }
    validate(state);
    const event = supersessionEvent(state);
    assert.equal(applyEvent(state, event, { revision: 41 }).state.version, 5);
    const broad = structuredClone(state);
    broad.items[0].scopes = ['src', 'docs\\retained.md'];
    const broadEvent = supersessionEvent(broad);
    // Include the ancestor holder explicitly; equality-only transfer must still reject it.
    broadEvent.proof.holders.unshift({ ...structuredClone(event.proof.holders[0]),
      itemDigest: digest(broad.items[0]) });
    rebindSupersession(broadEvent);
    assert.throws(() => applyEvent(broad, broadEvent, { revision: 41 }), /ancestor/);
    const unbound = structuredClone(event);
    unbound.authorization.receipt = '';
    assert.throws(() => applyEvent(state, unbound, { revision: 41 }), /authorization/);
  }
});

test('supersession shares effective guards with unrelated extension and same-key revalidation', () => {
  let state = supersessionFixture();
  state = applyEvent(state, supersessionEvent(state), { revision: 41 }).state;
  state = applyEvent(state, { id: 'adopt-other', type: 'adopt', owner: 'other-owner',
    key: 'other', outcome: 'other outcome', head: state.head, scopes: ['other\\original.cs'],
    pool: 'ready', kind: 'feature', evidence: 'Independent mutable source authority.' }).state;
  const extension = extensionEvent(state, 'other', ['other\\added.cs'], 'extend-unrelated');
  state = applyEvent(state, extension).state;
  assert.ok(state.items.find(item => item.key === 'other').scopes.includes('other\\added.cs'));
  const conflict = extensionEvent(state, 'other', ['src\\shared.cs'], 'extend-conflict');
  assert.throws(() => applyEvent(state, conflict), /conflicts/);
  state = applyEvent(state, { id: 'unrelated-cache', type: 'candidate', owner: 'planner', pass: 1,
    key: 'cache', outcome: 'cache outcome', head: state.head, scopes: ['other\\cache.cs'],
    pool: 'ready', kind: 'maintenance', evidence: 'Older cache outside transferred scope.' }).state;
  const cached = state.items.find(item => item.key === 'cache');
  state = applyEvent(state, { id: 'revalidate-unrelated', type: 'revalidate-candidate',
    key: cached.key, owner: 'planner', pass: 1, expectedItemDigest: digest(cached),
    head: state.head, scopes: [...cached.scopes, 'other\\cache.test.cjs'],
    evidence: 'Fresh complete unrelated maintenance manifest.', manifestComplete: true }).state;
  assert.equal(state.items.find(item => item.key === 'cache').admissionHistory[0].fromVersion, 5);
  validate(state);
});

test('documented supersession example executes without touching a checkpoint or claiming host proof', () => {
  const readme = fs.readFileSync(path.resolve(__dirname, '..', 'README.md'), 'utf8');
  const section = readme.split('## Exact-path write-reservation supersession')[1];
  const example = section.match(/```javascript\r?\n([\s\S]*?)\r?\n```/)[1];
  const result = spawnSync(process.execPath, ['-e', example], { encoding: 'utf8',
    cwd: path.resolve(__dirname, '..', '..', '..', '..') });
  assert.equal(result.status, 0, result.stderr);
  assert.deepEqual(JSON.parse(result.stdout), { version: 5,
    launch: [{ type: 'launch', key: 'successor', pool: 'ready' }],
    retained: [['docs\\retained.md'], []] });
});

test('supersession preserves accepted refinement history and permanent legacy retirement verbatim', () => {
  let state = refinementFixture(true);
  state = applyEvent(state, refinementEvent(state)).state;
  const prior = structuredClone(state.items[0]);
  state = applyEvent(state, { id: 'refined-successor', type: 'candidate', owner: 'planner', pass: 1,
    key: 'successor', outcome: 'refined successor outcome', head: state.head,
    scopes: [...prior.scopes], pool: 'ready', kind: 'feature',
    evidence: 'Independent exact-file operation after explicit retirement proof.' }).state;
  const result = applyEvent(state, supersessionEvent(state), { revision: 41 }).state;
  const expected = { ...prior, scopeAmendments: [], admissionHistory: [] };
  assert.deepEqual(result.items[0], expected);
  assert.equal(result.items[0].sourceWritesRetired, true);
  assert.deepEqual(result.refinements, state.refinements);
  assert.deepEqual(heldReservations(result, 'successor')[0].scopes, []);
});

test('all seven frozen final manifests fail closed against retained owners including fifteen retired guards', () => {
  const state = candidateFixture();
  validate(state);
  const native = state.items.find(item => item.key === 'generate-owned-required-literal-skill');
  assert.equal(native.sourceWritesRetired, true);
  assert.equal(native.scopes.length, 15);
  const before = structuredClone(state);
  const finals = require('./candidate-readmission-fixture.json').finalCandidates;
  assert.deepEqual(finals.map(item => item.scopes.length), [17, 28, 13, 21, 28, 14, 10]);
  for (const final of finals) {
    assert.throws(() => applyEvent(state, candidateEvent(state, final.key)), /conflicts with held/);
    assert.deepEqual(state, before);
  }
  assert.equal(state.amendments.length, 4);
  assert.equal(state.refinements.length, 1);
});

test('same-key final revalidation dispatches normally only after synthetic completed closure and retains all histories', () => {
  const state = candidateFixture(true);
  const held = state.items.filter(item => item.owner !== null);
  for (const final of require('./candidate-readmission-fixture.json').finalCandidates) {
    const item = state.items.find(value => value.key === final.key);
    const event = candidateEvent(state, item.key);
    const result = applyEvent(state, event);
    const current = result.state.items.find(value => value.key === item.key);
    assert.equal(result.state.version, 4);
    assert.equal(result.state.items.length, state.items.length);
    assert.deepEqual(result.actions, [{ type: 'launch', key: item.key, pool: item.pool }]);
    assert.equal(current.phase, 'reserved');
    assert.equal(current.owner, null);
    assert.equal(current.sourceWritesRetired, false);
    assert.equal(current.scopeRevision, 0);
    assert.deepEqual(current.scopes, final.scopes);
    for (const field of ['key', 'outcome', 'kind', 'pool', 'pr', 'publication', 'blocker']) {
      assert.deepEqual(current[field], item[field]);
    }
    assert.deepEqual(result.state.items.filter(value => value.owner !== null),
      held.map(value => ({ ...value, admissionHistory: [] })));
    assert.deepEqual(result.state.amendments, state.amendments);
    assert.deepEqual(result.state.refinements, state.refinements);
    assert.equal(current.admissionHistory[0].event.expectedItemDigest, digest(item));
    const repeated = applyEvent(result.state, event);
    assert.equal(repeated.duplicate, true);
    assert.deepEqual(repeated.state, result.state);
    assert.deepEqual(repeated.actions, []);
    assert.throws(() => applyEvent(result.state, { ...event, evidence: 'altered replay' }), /replay/);
    assert.throws(() => applyEvent(result.state, { ...event, id: 'new-replay-id' }), /unowned unreserved/);
    assert.throws(() => applyEvent(result.state, { ...item, id: `duplicate-${item.key}`, type: 'candidate',
      owner: state.planner.owner, pass: state.planner.pass, head: state.head }), /Duplicate/);
  }
});

test('candidate revalidation binds exact active planner/source/item and complete canonical manifest', () => {
  const state = candidateFixture(true);
  const event = candidateEvent(state);
  for (const change of [
    { owner: 'wrong-owner' }, { pass: state.planner.pass + 1 }, { head: '9'.repeat(40) },
    { expectedItemDigest: '0'.repeat(64) }, { key: 'missing' }, { outcome: 'replacement' },
    { kind: 'maintenance' }, { pool: 'repair' }, { pr: 123 }, { manifestComplete: false },
    { scopes: [] }, { scopes: [...event.scopes, event.scopes[0]] }, { scopes: ['..\\outside'] },
    { scopes: ['src\\*.cs'] }, { scopes: ['src/one.cs'] }, { scopes: ['src\\missing.cs'] },
    { scopes: ['SRC\\one.cs'] }, { evidence: '' }, { permission: 'write' }
  ]) {
    assert.throws(() => applyEvent(state, { ...event, ...change }));
  }
  for (const change of [
    value => { value.enabled = false; }, value => { value.mode = 'observe'; },
    value => { value.planner.active = false; }, value => { value.head = null; },
    value => { value.items.find(item => item.key === event.key).evidence = 'stale changed evidence'; },
    value => { value.items.find(item => item.key === event.key).sourceWritesRetired = true; },
    value => { value.items.find(item => item.key === event.key).sourceWriteAuthority = true; }
  ]) {
    const altered = structuredClone(state);
    change(altered);
    assert.throws(() => applyEvent(altered, event));
  }
  for (const item of state.items.filter(value => value.owner !== null)) {
    assert.throws(() => applyEvent(state, { ...event, key: item.key, expectedItemDigest: digest(item) }),
      /unowned unreserved/);
  }
  const oldId = state.seen.find(id => id.startsWith('planner-candidate-pass8'));
  assert.throws(() => applyEvent(state, { ...event, id: oldId }), /replay/);
  const current = structuredClone(state);
  const item = current.items.find(value => value.key === event.key);
  item.head = current.head;
  assert.throws(() => applyEvent(current, { ...candidateEvent(current), scopes: [...item.scopes],
    evidence: item.evidence }), /no-op/);
  item.sourceWriteAuthority = true;
  assert.throws(() => applyEvent(current, candidateEvent(current)));
});

test('persisted readmission rejects omission, downgrade, index/digest/history and authority tampering', () => {
  const initial = candidateFixture(true);
  const state = applyEvent(initial, candidateEvent(initial)).state;
  for (const change of [
    value => { delete value.readmissions; }, value => { value.readmissions = []; },
    value => { value.version = 3; },
    value => {
      value.version = 3;
      delete value.readmissions;
      for (const item of value.items) delete item.admissionHistory;
    },
    value => { delete value.items.find(item => item.admissionHistory.length).admissionHistory; },
    value => { value.items.find(item => item.admissionHistory.length).admissionHistory = []; },
    value => { value.items.find(item => item.admissionHistory.length).scopes.pop(); },
    value => { value.items.find(item => item.admissionHistory.length).evidence = 'tampered'; },
    value => { value.readmissions[0].digest = '0'.repeat(64); },
    value => { value.items.find(item => item.admissionHistory.length).admissionHistory[0].event.pass++; },
    value => { value.items.find(item => item.admissionHistory.length).admissionHistory[0].before.owner = 'writer'; },
    value => { value.items.find(item => item.admissionHistory.length).admissionHistory[0].before.pr = 999; },
    value => { value.seen = value.seen.filter(id => id !== state.readmissions[0].eventId); }
  ]) {
    const altered = structuredClone(state);
    change(altered);
    assert.throws(() => validate(altered));
    assert.throws(() => applyEvent(altered, { id: 'refresh-tampered', type: 'refresh', head: state.head }));
  }
});

test('legacy v1/v2/v3 migrate without changing owner proofs and successive readmissions bind complete prior history', () => {
  for (const version of [1, 2, 3]) {
    const state = candidateFixture(true);
    state.items = state.items.filter(item => item.phase === 'candidate');
    state.version = version;
    state.refinements = [];
    state.amendments = [];
    state.seen = [];
    for (const item of state.items) {
      if (version < 3) delete item.scopeAmendments;
      if (version < 2) {
        delete item.scopeHistory;
        delete item.scopeRevision;
        delete item.sourceWritesRetired;
      }
    }
    if (version < 3) delete state.amendments;
    if (version < 2) delete state.refinements;
    validate(state);
    const firstEvent = candidateEvent(state);
    // Maintenance waits for pass end, so two current full manifests can be rebound without attaching.
    const item = state.items.find(value => value.key === firstEvent.key);
    item.kind = 'maintenance';
    firstEvent.expectedItemDigest = digest(item);
    const first = applyEvent(state, firstEvent).state;
    assert.equal(first.items.find(value => value.key === item.key).phase, 'candidate');
    const secondEvent = { ...candidateEvent(first), id: 'second-current-manifest',
      evidence: 'Second fresh complete source inspection.' };
    const second = applyEvent(first, secondEvent).state;
    const current = second.items.find(value => value.key === item.key);
    assert.equal(current.admissionHistory.length, 2);
    assert.equal(current.admissionHistory[1].event.expectedItemDigest,
      digest(first.items.find(value => value.key === item.key)));
    assert.throws(() => applyEvent(first, { ...firstEvent, id: 'replayed-with-new-id' }), /Stale/);
    const ended = applyEvent(second, { id: 'end-readmission-pass', type: 'pass-ended',
      owner: second.planner.owner, pass: second.planner.pass, head: second.head,
      readyExamined: true, repairExamined: true, remaining: false,
      inventoryComplete: true, remainingFrontier: [] }).state;
    validate(ended);
    assert.equal(ended.items.find(value => value.key === item.key).phase, 'reserved');
  }
});

test('v4 admission survives ordinary ownership, extension, permanent refinement, publication and cleanup', () => {
  let state = candidateFixture(true);
  const event = candidateEvent(state);
  state = applyEvent(state, event).state;
  state = applyEvent(state, { id: 'attach-revalidated', type: 'attached',
    key: event.key, owner: 'fixture-revalidated-worker' }).state;
  const admission = structuredClone(state.items.find(item => item.key === event.key).admissionHistory);
  state = applyEvent(state, extensionEvent(state, event.key, ['next\\planned.cs'])).state;
  state = applyEvent(state, { id: 'publish-revalidated', type: 'published',
    key: event.key, owner: 'fixture-revalidated-worker', proof: {
      pushedHead: state.head, remoteHead: state.head, pr: 9010, ready: true, enrolled: true } }).state;
  const item = state.items.find(value => value.key === event.key);
  const refined = refinementEvent({ items: [{ ...item, key: 'holding' }] }, 'refine-revalidated');
  refined.key = event.key;
  refined.proof.key = event.key;
  refined.acknowledgment.key = event.key;
  refined.proof.toScopes = [item.scopes[0], 'next\\planned.cs'];
  refined.proof.manifest = { complete: true, actual: [item.scopes[0]], planned: ['next\\planned.cs'] };
  refined.proof.preservation.commits[0].paths = [item.scopes[0]];
  refined.proof.preservation.commits[0].head = state.head;
  refined.proof.preservation.retained = [];
  state = applyEvent(state, rebind(refined)).state;
  assert.equal(state.items.find(value => value.key === event.key).sourceWritesRetired, true);
  state = applyEvent(state, { id: 'complete-revalidated', type: 'completed', key: event.key,
    owner: item.owner, proof: { remoteHead: state.head, stoppedWriting: true,
      worktreeAbsent: true, branchAbsent: true, pendingInstructions: 0 } }).state;
  assert.deepEqual(state.items.find(value => value.key === event.key).admissionHistory, admission);
  validate(state);
});

test('revalidation retains candidate blockers and later ordinary launch failure can retain a reserved blocker', () => {
  const state = candidateFixture(true);
  const event = candidateEvent(state);
  const reserved = applyEvent(state, event).state;
  const blocked = applyEvent(reserved, { id: 'unknown-revalidated-launch', type: 'launch-blocked',
    key: event.key, reason: 'Unknown host launch result; retain reservation.' }).state;
  const item = blocked.items.find(value => value.key === event.key);
  assert.equal(item.phase, 'reserved');
  assert.equal(item.owner, null);
  assert.equal(item.blocker, 'Unknown host launch result; retain reservation.');
  assert.deepEqual(item.admissionHistory, reserved.items.find(value => value.key === event.key).admissionHistory);
  assert.throws(() => applyEvent(blocked, { id: 'unsafe-revalidated-attach', type: 'attached',
    key: event.key, owner: 'unknown-worker' }), /available reservation/);
  const waiting = structuredClone(state);
  waiting.items.find(value => value.key === event.key).blocker = 'Explicit retained admission blocker.';
  const retained = applyEvent(waiting, candidateEvent(waiting));
  assert.deepEqual(retained.actions, []);
  const candidate = retained.state.items.find(value => value.key === event.key);
  assert.equal(candidate.phase, 'candidate');
  assert.equal(candidate.blocker, 'Explicit retained admission blocker.');
});

test('documented atomic readmission JSON executes with the entire legacy item digest', () => {
  const readme = fs.readFileSync(path.resolve(__dirname, '..', 'README.md'), 'utf8');
  const section = readme.split('## Same-key unowned candidate revalidation')[1];
  const input = JSON.parse(section.match(/```json\r?\n([\s\S]*?)\r?\n```/)[1]);
  const state = createState('fixture-coordinator', input.event.head, 'delivery');
  state.planner = { owner: input.event.owner, active: true, pass: input.event.pass,
    requested: false, notified: true };
  const item = { key: input.event.key, outcome: 'Synthetic complete operation', kind: 'feature',
    pool: 'ready', pr: null, owner: null, phase: 'candidate', blocker: null, publication: null,
    head: '2'.repeat(40), scopes: [input.event.scopes[0]], evidence: 'Old incomplete cache.' };
  state.items.push(item);
  const expression = readme.match(/node -e '([^']+)'/)[1];
  const cli = spawnSync(process.execPath, ['-e', expression], { input: JSON.stringify(item),
    encoding: 'utf8', cwd: path.resolve(__dirname, '..', '..', '..', '..') });
  assert.equal(cli.status, 0, cli.stderr);
  input.event.expectedItemDigest = cli.stdout.trim();
  assert.equal(input.event.expectedItemDigest, digest(item));
  const result = applyEvent(state, input.event);
  assert.equal(input.operation, 'apply');
  assert.ok(Number.isSafeInteger(input.expectedRevision));
  assert.deepEqual(result.actions, [{ type: 'launch', key: item.key, pool: 'ready' }]);
  assert.deepEqual(result.state.items[0].scopes, input.event.scopes);
});

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

test('terminal inventory is retained, forwarded without aliasing, and replaced by the latest pass', () => {
  for (const inventoryComplete of [false, true]) {
    for (const remainingFrontier of [[], ['session conversation', 'governed tools']]) {
      const f = fixture();
      const frontier = { inventoryComplete, remainingFrontier };
      const remaining = !inventoryComplete || remainingFrontier.length > 0;
      const result = f.send({ type: 'pass-ended', owner: planner,
        readyExamined: true, repairExamined: true, remaining, ...frontier });
      assert.deepEqual(result.state.planner.frontier, frontier);
      assert.equal(result.state.planner.active, remaining);
      assert.equal(result.actions.length, Number(remaining));
      if (remaining) {
        assert.equal(result.actions[0].type, 'discover');
        assert.deepEqual(result.actions[0].frontier, frontier);
        result.actions[0].frontier.remainingFrontier.push('action mutation');
        assert.deepEqual(result.state.planner.frontier, frontier);
        const steered = f.send({ type: 'refresh', head: sha(2) });
        assert.equal(steered.actions[0].type, 'steer-planner');
        assert.deepEqual(steered.actions[0].frontier, frontier);
        const ended = f.send({ type: 'pass-ended', owner: planner,
          readyExamined: true, repairExamined: true, remaining: false,
          inventoryComplete: true, remainingFrontier: [] });
        assert.deepEqual(ended.state.planner.frontier,
          { inventoryComplete: true, remainingFrontier: [] });
        assert.deepEqual(ended.actions[0].frontier, ended.state.planner.frontier);
      } else {
        const refreshed = f.send({ type: 'refresh', head: sha(2) });
        assert.deepEqual(refreshed.actions[0].frontier, frontier);
      }
      assert.deepEqual(remainingFrontier, frontier.remainingFrontier);
    }
  }
});

test('new terminal reports reject missing, malformed or inconsistent inventory without mutation', () => {
  const f = fixture(), state = f.state();
  const valid = { id: 'end', type: 'pass-ended', owner: planner, pass: 1,
    readyExamined: true, repairExamined: true, remaining: false,
    inventoryComplete: true, remainingFrontier: [] };
  for (const bad of [
    { inventoryComplete: undefined }, { inventoryComplete: null }, { inventoryComplete: 'true' },
    { remainingFrontier: undefined }, { remainingFrontier: null }, { remainingFrontier: 'next' },
    { remainingFrontier: [''] }, { remainingFrontier: [' '] }, { remainingFrontier: [' next'] },
    { remainingFrontier: ['next '] }, { remainingFrontier: [1] }, { remainingFrontier: [null] },
    { remainingFrontier: ['next', 'next'] }, { remainingFrontier: Array(1) },
    { remaining: true }, { inventoryComplete: false }, { remainingFrontier: ['next'] }
  ]) {
    assert.throws(() => applyEvent(state, { ...valid, ...bad }), /frontier|Remaining/);
    assert.deepEqual(state, f.state());
  }
  for (const frontier of [null, {}, { inventoryComplete: true, remainingFrontier: [' next'] }]) {
    assert.throws(() => validate({ ...state, planner: { ...state.planner, frontier } }), /frontier/);
  }
});

test('saved candidate-readmission state and seen legacy terminal events need no frontier migration', () => {
  const state = structuredClone(require('./candidate-readmission-fixture.json').state);
  const original = structuredClone(state);
  validate(state);
  for (const id of state.seen.filter(id => id.startsWith('planner-pass-ended-'))) {
    const result = applyEvent(state, { id, type: 'pass-ended', owner: state.planner.owner,
      pass: 1, readyExamined: true, repairExamined: true, remaining: false });
    assert.equal(result.duplicate, true);
    assert.deepEqual(result.state, original);
    assert.deepEqual(result.actions, []);
  }
  const refreshed = applyEvent(state, { id: 'legacy-rescan', type: 'refresh', head: state.head, rescan: true });
  assert.equal(refreshed.actions[0].type, 'discover');
  assert.equal(Object.hasOwn(refreshed.actions[0], 'frontier'), false);
  const steered = applyEvent(refreshed.state, { id: 'legacy-change', type: 'refresh', head: sha(2) });
  assert.equal(steered.actions[0].type, 'steer-planner');
  assert.equal(Object.hasOwn(steered.actions[0], 'frontier'), false);
  assert.equal(steered.state.version, original.version);
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
    readyExamined: true, repairExamined: true, remaining: false,
    inventoryComplete: true, remainingFrontier: [] });
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
    readyExamined: true, repairExamined: true, remaining: true,
    inventoryComplete: false, remainingFrontier: [] }).actions[0].type, 'discover');
});

test('early maintenance proposals cannot consume feature slots before bounded discovery finishes', () => {
  const f = fixture();
  for (let index = 0; index < 10; index++) {
    assert.deepEqual(f.offer(`maintenance-${index}`, 'ready', { kind: 'maintenance' }).actions, []);
  }
  assert.deepEqual(f.offer('feature').actions, [{ type: 'launch', key: 'feature', pool: 'ready' }]);
  const result = f.send({ type: 'pass-ended', owner: planner,
    readyExamined: true, repairExamined: true, remaining: false,
    inventoryComplete: true, remainingFrontier: [] });
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
    readyExamined: true, repairExamined: true, remaining: true,
    inventoryComplete: false, remainingFrontier: [] });
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
    owner: planner, readyExamined: true, repairExamined: true, remaining: false,
    inventoryComplete: true, remainingFrontier: [] });
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
    readyExamined: true, repairExamined: true, remaining: false,
    inventoryComplete: true, remainingFrontier: [] }).state;
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

test('mutable extension rejects current snapshot staleness and cross-protocol acknowledgment reuse', () => {
  let state = refinementFixture();
  state = applyEvent(state, refinementEvent(state)).state;
  state = applyEvent(state, { id: 'adopt-mutable', type: 'adopt', key: 'mutable', owner: 'mutable',
    outcome: 'mutable', pool: 'ready', kind: 'feature', head: state.head,
    scopes: ['other\\owned.cs'], evidence: 'Independent owner.' }).state;
  const event = extensionEvent(state, 'mutable', ['other\\next.cs']);
  event.acknowledgment.id = state.items[0].scopeHistory[0].acknowledgment.id;
  assert.throws(() => applyEvent(state, event), /replay/);
  event.acknowledgment.id = 'new-ack';
  const changed = applyEvent(state, { id: 'adopt-third', type: 'adopt', key: 'third', owner: 'third',
    outcome: 'third', pool: 'ready', kind: 'feature', head: state.head,
    scopes: ['third\\owned.cs'], evidence: 'New holding owner.' }).state;
  assert.throws(() => applyEvent(changed, event), /snapshot/);
  const missingPlan = structuredClone(event);
  missingPlan.proof.manifest.planned = [];
  assert.throws(() => applyEvent(state, rebind(missingPlan)), /planned write/);
  const result = applyEvent(state, event);
  const old = state.items[0];
  const migrated = result.state.items[0];
  for (const key of Object.keys(old)) assert.deepEqual(migrated[key], old[key]);
  assert.deepEqual(result.state.refinements, state.refinements);
  assert.equal(migrated.sourceWritesRetired, true);
});

test('mutable amendments remain nonretired after publication, blocker, pause/resume and proven cleanup', () => {
  let state = refinementFixture();
  const publicationHead = state.head;
  state = applyEvent(state, extensionEvent(state, 'holding', ['other\\planned.cs'])).state;
  const amendments = structuredClone(state.items[0].scopeAmendments);
  state = applyEvent(state, { id: 'mutable-publish', type: 'published', owner: 'stopped-owner',
    key: 'holding', proof: { pushedHead: state.head, remoteHead: state.head, pr: 872,
      ready: true, enrolled: true } }).state;
  state = applyEvent(state, { id: 'mutable-blocked', type: 'blocked', owner: 'stopped-owner',
    key: 'holding', reason: 'Host cleanup lock remains.' }).state;
  state = applyEvent(state, { id: 'mutable-pause', type: 'pause' }).state;
  state = applyEvent(state, { id: 'mutable-resume', type: 'resume', owner: 'coordinator',
    authorityConfirmed: true, discover: false }).state;
  assert.deepEqual(state.items[0].scopeAmendments, amendments);
  assert.equal(state.items[0].sourceWritesRetired, false);
  assert.equal(state.items[0].blocker, 'Host cleanup lock remains.');
  assert.throws(() => applyEvent(state, { id: 'mutable-bad-complete', type: 'completed',
    owner: 'stopped-owner', key: 'holding', proof: { remoteHead: publicationHead, stoppedWriting: true,
      worktreeAbsent: false, branchAbsent: true, pendingInstructions: 0 } }), /cleanup/);
  state = applyEvent(state, { id: 'mutable-complete', type: 'completed', owner: 'stopped-owner',
    key: 'holding', proof: { remoteHead: publicationHead, stoppedWriting: true,
      worktreeAbsent: true, branchAbsent: true, pendingInstructions: 0 } }).state;
  assert.deepEqual(state.items[0].scopeAmendments, amendments);
  assert.equal(state.items[0].sourceWritesRetired, false);
  validate(state);
});

  function extensionEvent(state, key, additionalScopes, id = `extend-${key}`) {
    const item = state.items.find(value => value.key === key);
    const actual = [item.scopes[0]];
    const proof = {
      version: 1, owner: item.owner, key, phase: 'running', sourceWritesRetired: false,
      reservation: Object.fromEntries(['outcome', 'kind', 'pool', 'head', 'pr'].map(name => [name, item[name]])),
      scopeRevision: item.scopeRevision ?? 0, fromScopes: [...item.scopes], additionalScopes,
      toScopes: [...item.scopes, ...additionalScopes],
      manifest: { complete: true, actual, planned: [...additionalScopes] },
      preservation: { complete: true, commits: [], retained: [],
        unpreserved: actual.map(value => ({ path: value, receipt: 'Verified dirty file retained in original worktree.' })) },
      quiescence: { paused: true, pendingInstructions: 0, receipt: 'Fresh temporary pause and broad instruction reconciliation.' },
      unionFrozen: true, exclusivity: { complete: true, reservations: heldReservations(state, key),
        receipt: 'Exact current held-owner snapshot corroborated by coordinator.' },
      receipt: 'Fresh complete temporary-pause proof, not permanent retirement.'
    };
    return { id, type: 'extend-scopes', owner: item.owner, key,
      expectedScopeRevision: proof.scopeRevision, proof,
      acknowledgment: { id: `ack-${id}`, receipt: 'Exact owner response to whole proof/union.',
        owner: item.owner, key, scopeRevision: proof.scopeRevision, proofDigest: digest(proof) } };
  }

  function activeRequests() {
    const agent = {
      key: 'agent', scopes: [
        'src\\kora.core\\agents\\ownedagentprofiledocument.cs',
        'src\\kora.application\\definitions\\customizationauthoringservice.cs',
        'src\\kora\\agentprofileeditorcontroller.proposal.cs',
        'tests\\kora.core.unittests\\agents\\ownedagentprofileguidancetests.cs',
        'tests\\kora.application.unittests\\definitions\\customizationauthoringdeclaredagenttests.cs',
        'tests\\kora.windows.integrationtests\\agentprofiledeclaredguidancetests.cs',
        'design\\definition_management.md', 'docs\\agent-profile-authoring.md'
      ], additional: [
        'src\\kora\\agentprofileeditorcontroller.cs',
        'tests\\kora.windows.integrationtests\\agentprofileconversationalauthoringtests.cs',
        'tests\\kora.application.unittests\\documentation\\agentprofileauthoringguidetests.cs'
      ]
    };
    const families = ['appearance', 'speechsummary', 'playbackvolume', 'responsemode',
      'windowsspeechrate', 'installedspeechvoice', 'voicecapturetiming', 'quiethours', 'detailpresentation'];
    const settings = {
      key: 'settings', scopes: [
        'src\\kora.application\\configuration\\registeredsettings\\registeredsettingsuggestioncollection.cs',
        'src\\kora.application\\configuration\\registeredsettings\\registeredsettingsuggestions.cs',
        'src\\kora\\registeredsettings\\sessionsviewmodel.registeredsettings.cs',
        'src\\kora\\registeredsettings\\sessionsviewmodel.settingsuggestioncollection.cs',
        'src\\kora\\sessionswindowcontroller.registeredsettingbindings.cs',
        'tests\\kora.application.unittests\\configuration\\registeredsettingsuggestioncollectiontests.cs',
        'tests\\kora.windows.integrationtests\\sessionsconversationtests.settingsuggestioncollection.cs',
        'design\\user_configuration.md', 'docs\\setting-proposals.md'
      ], additional: [
        ...families.map(name => `src\\kora.application\\configuration\\${name}suggestionreview.cs`),
        ...families.map(name => `src\\kora.application\\viewmodels\\mainviewmodel.${name === 'appearance' ? name : `${name}suggestions`}.cs`),
        'src\\kora\\sessionswindowcontroller.cs',
        'src\\kora\\registeredsettings\\sessionspresentationowner.registeredsettings.cs',
        'src\\kora\\sessionswindow.axaml', 'src\\kora\\compactsessionwindow.axaml'
      ]
    };
    const index = {
      key: 'index', scopes: [
        'src\\kora.core\\context\\localfilelexicalretrieval.cs',
        'src\\kora.core\\context\\localfilelexicalretrieval.citations.cs',
        'src\\kora.core\\context\\localfilelexicalindex.cs',
        'src\\kora.core\\knowledgesources\\iknowledgesourcestore.cs',
        'src\\kora.core\\knowledgesources\\knowledgesourcerevision.cs',
        'src\\kora.application\\knowledgesources\\knowledgesourcemanager.cs',
        'src\\kora.application\\knowledgesources\\knowledgesourceanswerworkflow.cs',
        'src\\kora.windows\\knowledgesources\\windowsknowledgesourcestore.cs',
        'src\\kora.windows\\knowledgesources\\windowsknowledgesourcestore.snapshot.cs',
        'src\\kora.windows\\knowledgesources\\windowsknowledgesourcestore.index.cs',
        'src\\kora\\knowledgesources\\knowledgesourceswindow.cs',
        'tests\\kora.core.unittests\\context\\localfileretrievaltests.cs',
        'tests\\kora.core.unittests\\context\\localfilelexicalindextests.cs',
        'tests\\kora.application.unittests\\knowledgesources\\knowledgesourcemanagertests.cs',
        'tests\\kora.application.unittests\\knowledgesources\\knowledgesourceanswertests.cs',
        'tests\\kora.windows.integrationtests\\knowledgesources\\windowsknowledgesourcetests.index.cs',
        'tests\\kora.windows.integrationtests\\knowledgesources\\knowledgesourceswindowtests.cs',
        'tests\\kora.windows.integrationtests\\knowledgesources\\knowledgesourceanswerwindowtests.cs',
        'design\\model_providers_memory_and_knowledge.md', 'design\\file_and_folder_ingestion.md',
        'docs\\knowledge-sources.md'
      ], additional: [
        'src\\kora.core\\knowledgesources\\knowledgesourceanswercontext.cs',
        'src\\kora\\knowledgesources\\knowledgesourceswindow.answers.cs',
        'tests\\kora.core.unittests\\context\\knowledgesourceanswertests.cs'
      ]
    };
    return [agent, settings, index];
  }

  test('mutable exact active requests 8+3=11, 9+22=31 and 21+3=24 retain every path, slot, owner and dirty write', () => {
    let state = createState(coordinator, sha(1), 'delivery');
    const requests = activeRequests();
    for (const request of requests) {
      state = applyEvent(state, { id: `adopt-${request.key}`, type: 'adopt', key: request.key,
        owner: owner(request.key), outcome: request.key, pool: 'ready', kind: 'feature',
        head: sha(1), scopes: request.scopes, evidence: 'Existing admitted real-request shape.' }).state;
      state = applyEvent(state, { id: `blocked-${request.key}`, type: 'blocked', key: request.key,
        owner: owner(request.key), reason: 'Additional wiring withheld until separate continuation.' }).state;
    }
    for (const [index, request] of requests.entries()) {
      const before = structuredClone(state.items[index]);
      const others = structuredClone(state.items.filter(item => item.key !== request.key));
      const event = extensionEvent(state, request.key, request.additional);
      const result = applyEvent(state, event);
      const item = result.state.items[index];
      assert.equal(item.scopes.length, [11, 31, 24][index]);
      assert.deepEqual(item.scopes, [...request.scopes, ...request.additional]);
      assert.deepEqual(item.scopeAmendments[0].proof.manifest.actual, [request.scopes[0]]);
      for (const name of ['key', 'owner', 'outcome', 'pool', 'kind', 'head', 'pr', 'phase', 'publication', 'blocker']) {
        assert.deepEqual(item[name], before[name]);
      }
      assert.equal(item.sourceWritesRetired, false);
      assert.equal(item.scopeRevision, 1);
      assert.deepEqual(item.scopeHistory, []);
      assert.equal(result.state.items.filter(item => item.phase === 'running').length, 3);
      assert.deepEqual(result.actions, []);
      for (const other of others) {
        const after = result.state.items.find(item => item.key === other.key);
        assert.deepEqual(after.scopes, other.scopes);
        assert.equal(after.owner, other.owner);
      }
      assert.deepEqual(applyEvent(result.state, event), { ...result, actions: [], duplicate: true });
      state = result.state;
    }
    validate(state);
  });

  test('active extensions reject missing/unknown proof, invalid paths, stale owner/revision and uncovered actual/planned writes without mutation', () => {
    const state = refinementFixture();
    const invalid = [
      e => { e.owner = 'wrong'; }, e => { e.key = 'wrong'; },
      e => { e.expectedScopeRevision = 1; }, e => { e.proof.scopeRevision = 1; },
      e => { e.proof.owner = 'wrong'; }, e => { e.proof.key = 'wrong'; },
      e => { e.proof.reservation.outcome = 'wrong'; }, e => { e.proof.reservation.head = sha(99); },
      e => { e.proof.reservation.pool = 'repair'; }, e => { e.proof.reservation.pr = 99; },
      e => { e.proof.phase = 'published'; }, e => { e.proof.sourceWritesRetired = true; },
      e => { e.proof.fromScopes = ['src']; }, e => { e.proof.toScopes.pop(); },
      ...[[], ['src'], ['src\\already-covered.cs'], ['src\\..\\escape'],
        ['new\\.. \\escape'], ['Q:\\new'], ['new/file'], ['new\\*'], ['new', 'NEW'],
        ['new', 'new'], ['new.', 'another'], ['new ', 'another'], ['new', 'new\\file']]
        .map(paths => e => { e.proof.additionalScopes = paths; e.proof.toScopes = [...e.proof.fromScopes, ...paths]; }),
      ...[false, undefined, 'unknown'].map(value => e => { e.proof.quiescence.paused = value; }),
      ...[1, undefined, '0'].map(value => e => { e.proof.quiescence.pendingInstructions = value; }),
      e => { e.proof.quiescence.receipt = ''; }, e => { e.proof.unionFrozen = false; },
      e => { e.proof.manifest.complete = false; }, e => { delete e.proof.manifest.actual; },
      e => { delete e.proof.manifest.planned; }, e => { e.proof.manifest.planned.push('outside\\x'); },
      e => { e.proof.manifest.actual.push('outside\\x'); },
      e => { e.proof.manifest.actual = []; },
      e => { e.proof.preservation.complete = false; }, e => { delete e.proof.preservation.retained; },
      e => { e.proof.preservation.unpreserved[0].receipt = ''; },
      e => {
        e.proof.manifest.actual = ['new\\file'];
        e.proof.preservation.unpreserved[0].path = 'new\\file';
      },
      e => { e.proof.exclusivity.complete = false; }, e => { e.proof.exclusivity.receipt = ''; },
      e => { e.proof.exclusivity.reservations.push({ key: 'unknown', owner: null, scopes: ['elsewhere'],
        scopeRevision: 0, sourceWritesRetired: false }); },
      e => { e.proof.extra = true; }, e => { e.extra = true; },
      e => { e.acknowledgment.owner = 'wrong'; }, e => { e.acknowledgment.key = 'wrong'; },
      e => { e.acknowledgment.scopeRevision = 1; }, e => { e.acknowledgment.id = ''; },
      e => { e.acknowledgment.receipt = ''; }, e => { e.acknowledgment.extra = true; }
    ];
    for (const [index, change] of invalid.entries()) {
      const event = extensionEvent(state, 'holding', ['new\\file']);
      change(event);
      rebind(event);
      const before = structuredClone(state);
      assert.throws(() => applyEvent(state, event), undefined, `case ${index}`);
      assert.deepEqual(state, before);
    }
    for (const field of ['proof', 'acknowledgment']) {
      const event = extensionEvent(state, 'holding', ['new\\file']);
      delete event[field];
      assert.throws(() => applyEvent(state, event));
    }
    const event = extensionEvent(state, 'holding', ['new\\file']);
    event.acknowledgment.proofDigest = '0'.repeat(64);
    assert.throws(() => applyEvent(state, event), /acknowledgment/);
  });

  test('active extension excludes paused/observe/unattached/published/complete and permanently retired owners', () => {
    const original = refinementFixture();
    const event = extensionEvent(original, 'holding', ['new\\file']);
    for (const mode of ['observe', 'delivery']) {
      assert.throws(() => applyEvent({ ...original, mode, enabled: false }, event), /enabled delivery/);
    }
    assert.throws(() => applyEvent({ ...original, mode: 'observe' }, event), /enabled delivery/);
    for (const phase of ['candidate', 'reserved']) {
      const state = structuredClone(original);
      state.items[0].phase = phase;
      state.items[0].owner = null;
      assert.throws(() => applyEvent(state, { ...event, owner: null }), /worker|running/);
    }
    const published = refinementFixture(true);
    assert.throws(() => applyEvent(published, extensionEvent(published, 'holding', ['new\\file'])), /running/);
    const retired = applyEvent(original, refinementEvent(original)).state;
    assert.equal(retired.items[0].phase, 'running');
    assert.throws(() => applyEvent(retired, extensionEvent(retired, 'holding', ['new\\file'])), /nonretired/);
    const completed = applyEvent(published, { id: 'complete-extension-denial', type: 'completed',
      owner: 'stopped-owner', key: 'holding', proof: { remoteHead: published.head,
        stoppedWriting: true, worktreeAbsent: true, branchAbsent: true, pendingInstructions: 0 } }).state;
    assert.throws(() => applyEvent(completed, extensionEvent(completed, 'holding', ['new\\file'])), /worker/);
  });

  test('every held scope including retired actual writes and all seven passive guards blocks extension', () => {
    const originalWrites = [
      'design\\skill_authoring.md', 'docs\\owned-skill-authoring.md',
      'src\\kora.application\\skills\\ownedskillguidancerefinementservice.cs',
      'src\\kora\\ownedskilleditorcontroller.refinement.cs', 'src\\kora\\ownedskilleditorwindow.cs',
      'tests\\kora.application.unittests\\dependencies\\modelturnhosttests.ownedskillgeneration.cs',
      'tests\\kora.windows.integrationtests\\ownedskillauthoringnativetests.refinement.cs',
      'tests\\kora.windows.integrationtests\\sessionsconversationtests.ownedskillinputs.cs'
    ];
    const passive = [
      'tests\\kora.windows.integrationtests\\artifactdiscovery\\ownedpromptrenametests.cs',
      'tests\\kora.windows.integrationtests\\communication\\graphpresencesourcetests.cs',
      'tests\\kora.windows.integrationtests\\knowledgesources\\reviewedsourcefollowuptests.cs',
      'tests\\kora.windows.integrationtests\\knowledgesources\\sessionknowledgesourcebindingtests.cs',
      'tests\\kora.windows.integrationtests\\sessionpermissionreviewertests.cs',
      'tests\\kora.windows.integrationtests\\sessionsconversationtests.statichtmldetails.cs',
      'tests\\kora.windows.integrationtests\\storage\\windowssqlitesessionresponseoutputtests.cs'
    ];
    let state = refinementFixture();
    state.items[0].scopes.push('design', 'docs');
    const retiredEvent = refinementEvent(state);
    retiredEvent.proof.toScopes.push(...originalWrites, ...passive);
    retiredEvent.proof.manifest.actual.push(...originalWrites);
    retiredEvent.proof.preservation.commits[0].paths.push(...originalWrites);
    retiredEvent.proof.manifest.planned.push(...passive);
    state = applyEvent(state, rebind(retiredEvent)).state;
    state = applyEvent(state, { id: 'other', type: 'adopt', owner: 'mutable-worker', key: 'mutable',
      outcome: 'mutable', pool: 'ready', kind: 'feature', head: state.head,
      scopes: ['other\\owned.cs'], evidence: 'Independent mutable owner.' }).state;
    for (const path of [...state.items[0].scopes, 'tests\\kora.windows.integrationtests']) {
      const event = extensionEvent(state, 'mutable', [path]);
      assert.throws(() => applyEvent(state, event), /conflicts/);
      event.proof.exclusivity.reservations = [];
      assert.throws(() => applyEvent(state, rebind(event)), /snapshot/);
    }
  });

  test('mutable revision/manifest/replay history persists through later extension, publication and independent legacy shrink', () => {
    let state = refinementFixture();
    const first = extensionEvent(state, 'holding', ['new\\one']);
    state = applyEvent(state, first).state;
    assert.equal(state.version, 3);
    assert.throws(() => applyEvent(state, { ...first, id: 'stale-extension' }), /Stale/);
    const second = extensionEvent(state, 'holding', ['new\\two'], 'extension-two');
    second.proof.manifest.planned.push('new\\one');
    rebind(second);
    second.acknowledgment.id = first.acknowledgment.id;
    assert.throws(() => applyEvent(state, second), /replay/);
    second.acknowledgment.id = 'fresh-two';
    const lost = structuredClone(second);
    lost.proof.manifest.planned = ['new\\two'];
    assert.throws(() => applyEvent(state, rebind(lost)), /history/);
    state = applyEvent(state, second).state;
    assert.equal(state.items[0].scopeRevision, 2);
    assert.equal(state.items[0].sourceWritesRetired, false);
    const shrink = refinementEvent(state, 'shrink-after-extensions');
    shrink.proof.manifest.actual = ['src\\written.cs', 'tests\\written.test.cs', 'src'];
    shrink.proof.preservation.unpreserved = [{ path: 'src', receipt: 'Original synthetic actual path retained.' }];
    shrink.proof.toScopes = ['src', 'tests\\written.test.cs', 'new\\one', 'new\\two'];
    shrink.proof.manifest.planned.push('new\\one', 'new\\two');
    rebind(shrink);
    state = applyEvent(state, shrink).state;
    assert.equal(state.items[0].scopeRevision, 3);
    assert.equal(state.items[0].sourceWritesRetired, true);
    assert.deepEqual(state.items[0].scopeAmendments.map(record => record.eventId), [first.id, second.id]);
    assert.deepEqual(state.items[0].scopeHistory.map(record => record.eventId), [shrink.id]);
    assert.throws(() => applyEvent(state,
      extensionEvent(state, 'holding', ['new\\three'], 'retired-new-request')), /nonretired/);
    state = applyEvent(state, { id: 'publish-extension-owner', type: 'published', owner: 'stopped-owner',
      key: 'holding', proof: { pushedHead: state.head, remoteHead: state.head, pr: 871,
        ready: true, enrolled: true } }).state;
    validate(state);
  });
