'use strict';

const { createState, applyEvent, digest } = require('./registry.cjs');

const head = '1'.repeat(40);
const owner = 'stopped-owner';

function refinementFixture(published = false) {
  let state = createState('coordinator', head, 'delivery');
  state = applyEvent(state, { id: 'planner', type: 'planner', owner: 'planner' }).state;
  state = applyEvent(state, { id: 'adopt', type: 'adopt', owner, key: 'holding',
    outcome: 'holding outcome', pool: 'ready', kind: 'feature', head,
    scopes: ['src', 'tests'], evidence: 'Exclusive admitted owner.' }).state;
  if (published) {
    state = applyEvent(state, { id: 'published', type: 'published', owner, key: 'holding',
      proof: { pushedHead: head, remoteHead: head, pr: 869, ready: true, enrolled: true } }).state;
  }
  state = applyEvent(state, { id: 'blocked', type: 'blocked', owner, key: 'holding',
    reason: 'Host locks empty worktree root; local branch retained.' }).state;
  return state;
}

function refinementEvent(state, id = 'refine-once') {
  const item = state.items.find(value => value.key === 'holding');
  const proof = {
    version: 1, owner: item.owner, key: item.key, scopeRevision: item.scopeRevision ?? 0,
    reservation: { outcome: item.outcome, kind: item.kind, pool: item.pool, head: item.head, pr: item.pr },
    publication: structuredClone(item.publication),
    fromScopes: [...item.scopes], toScopes: ['src\\written.cs', 'tests\\written.test.cs'],
    phase: item.phase, publicationHead: item.publication?.remoteHead ?? null,
    manifest: { complete: true, actual: ['src\\written.cs', 'tests\\written.test.cs'],
      planned: ['tests\\written.test.cs'] },
    preservation: { complete: true,
      commits: [{ head, paths: ['src\\written.cs'], receipt: 'Original committed diff and retained local ref.' }],
      retained: [{ path: 'tests\\written.test.cs', receipt: 'Local retained write, verified manifest.' }],
      unpreserved: [] },
    stoppedWriting: true, expansionFrozen: true, pendingInstructions: 0,
    receipt: 'Fresh owner/host proof; remote branch ABSENT, not cleanup or remote equality.'
  };
  return { id, type: 'refine-scopes', key: item.key, owner: item.owner,
    expectedScopeRevision: proof.scopeRevision, proof,
    acknowledgment: { id: `ack-${id}`, receipt: 'Concrete fresh owner response message ID.',
      owner: item.owner, key: item.key, scopeRevision: proof.scopeRevision, proofDigest: digest(proof) } };
}

function rebind(event) {
  event.acknowledgment.proofDigest = digest(event.proof);
  return event;
}

function tamperedStates(state) {
  const changes = [
    value => { delete value.items[0].scopeHistory; },
    value => { value.items[0].scopeHistory = []; },
    value => { value.items[0].scopeHistory[0].proof.receipt = 'altered'; },
    value => { value.items[0].scopeHistory[0].acknowledgment.id = 'altered'; },
    value => { value.items[0].scopes = ['src']; },
    value => { value.items[0].scopeRevision = 0; },
    value => { value.items[0].sourceWritesRetired = false; },
    value => { value.items[0].outcome = 'altered'; },
    value => { value.items[0].head = '2'.repeat(40); },
    value => { delete value.refinements; },
    value => { value.refinements = []; },
    value => { value.version = 1; },
    value => {
      value.version = 1;
      delete value.refinements;
      for (const item of value.items) {
        delete item.scopeHistory;
        delete item.scopeRevision;
        delete item.sourceWritesRetired;
      }
    },
    value => { value.seen = value.seen.filter(id => id !== 'refine-once'); }
  ];
  return changes.map(change => {
    const value = structuredClone(state);
    change(value);
    return value;
  });
}

function candidateFixture(released = false) {
  let state = structuredClone(require('./candidate-readmission-fixture.json').state);
  state.planner = { owner: 'fixture-readmission-planner', active: true, pass: 1,
    requested: false, notified: true };
  if (released) {
    for (const [index, item] of state.items.filter(value =>
      !['candidate', 'complete'].includes(value.phase)).entries()) {
      if (!item.publication) {
        state = applyEvent(state, { id: `fixture-publication-${index}`, type: 'published',
          key: item.key, owner: item.owner, proof: { pushedHead: state.head, remoteHead: state.head,
            pr: 9000 + index, ready: true, enrolled: true } }).state;
      }
      const published = state.items.find(value => value.key === item.key);
      state = applyEvent(state, { id: `fixture-completion-${index}`, type: 'completed',
        key: item.key, owner: item.owner, proof: { remoteHead: published.publication.remoteHead,
          stoppedWriting: true, worktreeAbsent: true, branchAbsent: true, pendingInstructions: 0 } }).state;
    }
  }
  return state;
}

function candidateEvent(state, key = 'independent-local-action-reviewer', id = `revalidate-${key}`) {
  const item = state.items.find(value => value.key === key);
  const final = require('./candidate-readmission-fixture.json').finalCandidates
    .find(value => value.key === key);
  return { id, type: 'revalidate-candidate', owner: state.planner.owner, pass: state.planner.pass,
    key, expectedItemDigest: digest(item), head: state.head, scopes: [...final.scopes],
    evidence: `Fresh current-source complete manifest: ${final.evidence}`, manifestComplete: true };
}

module.exports = { refinementFixture, refinementEvent, rebind, tamperedStates, head, owner,
  candidateFixture, candidateEvent };
