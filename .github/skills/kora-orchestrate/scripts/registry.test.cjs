'use strict';

const assert = require('node:assert/strict');
const { test } = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const { createState, applyEvent, validate, digest, heldReservations,
  supersessionAcknowledgmentDigest } = require('./registry.cjs');
const { refinementFixture, refinementEvent, rebind, tamperedStates,
  candidateFixture, candidateEvent, supersessionFixture, supersessionEvent,
  rebindSupersession, supersessionBeneficiaryFixture } = require('./refinement-fixture.cjs');

const sha = number => number.toString(16).padStart(40, '0');
const coordinator = 'agent-host-session://coordinator';
const planner = 'agent-host-session://coordinator?chat=planner';
const owner = key => `agent-host-session://coordinator?chat=${key}`;

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
      readyExamined: true, repairExamined: true, remaining: false }).state;
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
