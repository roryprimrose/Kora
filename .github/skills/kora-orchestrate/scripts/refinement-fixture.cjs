'use strict';

const { createState, applyEvent, digest, supersessionAcknowledgmentDigest,
  heldReservations, recoveryContinuationDigest } = require('./registry.cjs');

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

function supersessionFixture() {
  let state = createState('local-coordinator', head, 'delivery');
  state = applyEvent(state, { id: 'planner', type: 'planner', owner: 'planner' }).state;
  for (const [index, scopes] of [['src\\shared.cs', 'docs\\retained.md'],
    ['tests\\shared.test.cjs']].entries()) {
    const key = `prior-${index}`;
    state = applyEvent(state, { id: key, type: 'adopt', owner: `owner-${index}`, key,
      outcome: key, pool: 'ready', kind: 'feature', head, scopes,
      evidence: 'Synthetic independently admitted historical owner.' }).state;
    state = applyEvent(state, { id: `publication-${index}`, type: 'published',
      owner: `owner-${index}`, key, proof: { pushedHead: head, remoteHead: head,
        pr: 910 + index, ready: true, enrolled: true } }).state;
    state = applyEvent(state, { id: `blocked-${index}`, type: 'blocked',
      owner: `owner-${index}`, key, reason: 'Retained cleanup obligation.' }).state;
  }
  return applyEvent(state, { id: 'candidate', type: 'candidate', owner: 'planner', pass: 1,
    key: 'successor', outcome: 'successor outcome', pool: 'ready', kind: 'feature', head,
    scopes: ['src\\shared.cs', 'tests\\shared.test.cjs'],
    evidence: 'Synthetic complete independently admitted successor.' }).state;
}

function supersessionEvent(state, revision = 41, mode = 'activate-local', externalCheckpoints = []) {
  const candidate = state.items.find(item => item.key === 'successor');
  const proof = {
    version: 1, repository: 'roryprimrose/Kora', coordinator: state.coordinator,
    checkpointRevision: revision, head: state.head, mode, candidate: structuredClone(candidate),
    authority: { singleLedger: mode === 'activate-local',
      receipt: 'Synthetic controlling-ledger authority; external authority explicitly unavailable.' },
    exactFiles: { complete: true, paths: [], receipt: 'Synthetic verified exact file-kind inventory.' },
    expectedItemDigest: digest(candidate), manifestComplete: true,
    localReservations: structuredClone(state.items.filter(item =>
      !['candidate', 'complete'].includes(item.phase))), externalCheckpoints, holders: []
  };
  for (const [coordinator, items] of [[state.coordinator, proof.localReservations],
    ...externalCheckpoints.map(snapshot => [snapshot.state.coordinator,
      snapshot.state.items.filter(item => !['candidate', 'complete'].includes(item.phase))])]) {
    for (const item of items) {
      const paths = candidate.scopes.filter(value => item.scopes.includes(value));
      if (!paths.length) continue;
      const holder = {
        coordinator, key: item.key, owner: item.owner, itemDigest: digest(item),
        scopeRevision: item.scopeRevision ?? 0, paths,
        manifest: { complete: true, actual: [...item.scopes], planned: [] },
        preservation: { complete: true,
          commits: [{ head: item.publication?.remoteHead ?? head, paths: [...item.scopes],
            receipt: 'Synthetic exact commit/blob preservation receipt.' }],
          retained: [], unpreserved: [] },
        stopped: { writingStopped: true, pendingInstructions: 0, sourceInstructionsCleared: true,
          receipt: 'Synthetic independent host quiescence and exact instruction readback.' },
        acknowledgment: null
      };
      proof.holders.push(holder);
    }
  }
  proof.exactFiles.paths = [...new Set(proof.holders.flatMap(holder => holder.paths))];
  return rebindSupersession({ id: 'supersede-once', type: 'supersede-write-reservations', proof,
    authorization: { humanAuthorized: true, receipt: 'Synthetic explicit exact human authority.',
      proofDigest: '' } });
}

function rebindSupersession(event) {
  for (const holder of event.proof.holders) {
    holder.acknowledgment = { kind: 'owner', id: `ack-${event.id}-${holder.coordinator}-${holder.key}`,
      receipt: 'Synthetic exact owner whole-proof response, not real host evidence.',
      proofDigest: supersessionAcknowledgmentDigest(event.proof, holder) };
  }
  event.authorization.proofDigest = digest(event.proof);
  return event;
}

function supersessionBeneficiaryFixture() {
  const before = supersessionFixture();
  const reserved = applyEvent(before, supersessionEvent(before), { revision: 41 }).state;
  const running = applyEvent(reserved, { id: 'review-successor-attached', type: 'attached',
    key: 'successor', owner: 'review-successor' }).state;
  const published = applyEvent(running, { id: 'review-successor-published', type: 'published',
    key: 'successor', owner: 'review-successor', proof: { pushedHead: head, remoteHead: head,
      pr: 912, ready: true, enrolled: true } }).state;
  const completion = { id: 'review-successor-completed', type: 'completed',
    key: 'successor', owner: 'review-successor', proof: { remoteHead: head,
      stoppedWriting: true, worktreeAbsent: true, branchAbsent: true, pendingInstructions: 0 } };
  const complete = applyEvent(published, completion).state;
  return { reserved, running, published, completion, complete };
}

function fencedExtensionFixture() {
  let state = createState('synthetic-coordinator', head, 'delivery');
  state = applyEvent(state, { id: 'synthetic-mutable-owner', type: 'adopt', key: 'mutable',
    owner: 'synthetic-mutable-owner', outcome: 'Synthetic complete reviewed local skill',
    pool: 'ready', kind: 'feature', head, scopes: ['src\\owned.cs', 'tests\\owned.test.cs'],
    evidence: 'SYNTHETIC bounded reserved source, not a live owner.' }).state;
  state = applyEvent(state, { id: 'synthetic-held-owner', type: 'adopt', key: 'held',
    owner: 'synthetic-held-owner', outcome: 'Synthetic retained other work',
    pool: 'ready', kind: 'feature', head, scopes: ['src\\held.cs'],
    evidence: 'SYNTHETIC exclusive retained other owner.' }).state;
  return applyEvent(state, { id: 'synthetic-mutable-blocker', type: 'blocked',
    key: 'mutable', owner: 'synthetic-mutable-owner', reason: 'Existing review scope blocker; no automatic clearance.' }).state;
}

function rebindFencedExtension(event) {
  event.acknowledgment.proofDigest = digest(event.proof);
  event.authorization.subjectDigest = digest({ proof: event.proof, acknowledgment: event.acknowledgment });
  return event;
}

function fencedExtensionEvent(state, revision = 41, additionalScopes = ['src\\shared-review.cs']) {
  const item = state.items.find(value => value.key === 'mutable');
  const previous = item.scopeAmendments?.at(-1)?.proof;
  const fence = { epoch: (previous?.reconciliation?.fence.epoch ?? 0) + 1,
    nonce: `SYNTHETIC-fence-nonce-${revision}`, revokesEarlierSourceAuthority: true,
    established: true, receipt: 'SYNTHETIC cooperative revocation observation; not OS enforcement.' };
  const proof = { version: 2, owner: item.owner, key: item.key,
    reservation: Object.fromEntries(['outcome', 'kind', 'pool', 'head', 'pr'].map(name => [name, item[name]])),
    scopeRevision: item.scopeRevision ?? 0, fromScopes: [...item.scopes], additionalScopes,
    toScopes: [...item.scopes, ...additionalScopes], phase: 'running', sourceWritesRetired: false,
    manifest: { complete: true, actual: previous?.manifest.actual ?? ['src\\owned.cs'],
      planned: [...new Set([...(previous?.manifest.planned ?? []), ...additionalScopes])] },
    preservation: { complete: true, commits: [],
      retained: [{ path: 'src\\owned.cs', receipt: 'SYNTHETIC preserved partial implementation file hash and length.' }],
      unpreserved: [] },
    quiescence: { paused: true, commandsRunning: false, workJoined: true,
      receipt: 'SYNTHETIC fresh exact owner temporary pause/all-command join observation.' },
    unionFrozen: true, exclusivity: { complete: true, reservations: heldReservations(state, item.key),
      receipt: 'SYNTHETIC complete current retained guards.' },
    receipt: 'SYNTHETIC non-incident prospective addition; no outside-scope writes.',
    repository: 'roryprimrose/Kora', coordinator: state.coordinator, checkpointRevision: revision, head: state.head,
    source: { owner: item.owner, worktree: 'Q:\\Synthetic\\Mutable', branch: 'synthetic-mutable', head: item.head,
      receipt: 'SYNTHETIC independently verified exact source identity.' },
    expectedItemDigest: digest(item), reconciliation: { kind: 'human-approved-source-fence',
      pendingInstructions: 'UNKNOWN', processingInstructions: 'UNKNOWN', fence,
      receipt: 'SYNTHETIC explicit human-approved reconciliation; host queue enumeration remains unavailable.' } };
  return rebindFencedExtension({ id: `synthetic-fenced-extension-${revision}`, type: 'extend-scopes',
    key: item.key, owner: item.owner, expectedScopeRevision: proof.scopeRevision, proof,
    acknowledgment: { id: `synthetic-fenced-owner-ack-${revision}`, owner: item.owner, key: item.key,
      scopeRevision: proof.scopeRevision, proofDigest: '', workJoined: true, staleAuthorityRejected: true,
      fence: structuredClone(fence), receipt: 'SYNTHETIC fresh whole-final-proof owner response.' },
    authorization: { kind: 'explicit-human-scope-extension-decision', id: `synthetic-human-decision-${revision}`,
      actor: 'SYNTHETIC original human', decision: 'prospective-coverage-only', subjectDigest: '',
      receipt: 'SYNTHETIC final-proof decision, not actual consent or implementation approval.' } });
}

function fencedExtensionContext(event) {
  return { revision: event.proof.checkpointRevision, extensionEvidence: {
    kind: 'host-verified-fenced-scope-extension', coordinator: event.proof.coordinator,
    checkpointRevision: event.proof.checkpointRevision, proofDigest: digest(event.proof),
    ownerResponseId: event.acknowledgment.id, humanDecisionId: event.authorization.id,
    humanDecisionDigest: digest(event.authorization), sourceIdentityVerified: true,
    sourcePauseVerified: true, commandsJoinedVerified: true, instructionAuthorityVerified: true,
    receipt: 'SYNTHETIC independent human/owner/source/pause/join/fence host readback, not authentication.' } };
}

function fencedExtensionContinuation(state, revision = 42) {
  const item = state.items.find(value => value.key === 'mutable');
  const record = item.scopeAmendments.at(-1);
  const request = { version: 1, kind: 'exact-extended-owner-continuation',
    coordinator: state.coordinator, head: state.head,
    source: Object.fromEntries(['owner', 'worktree', 'branch', 'head'].map(name => [name, record.proof.source[name]])),
    checkpointRevision: revision, key: item.key, owner: item.owner, expectedItemDigest: digest(item),
    scopeRevision: item.scopeRevision, coverageDigest: digest(record),
    fence: structuredClone(record.proof.reconciliation.fence),
    acknowledgment: { id: `synthetic-separate-continuation-${revision}`, owner: item.owner, requestDigest: '',
      coverageDigest: digest(record), fence: structuredClone(record.proof.reconciliation.fence),
      workJoined: true, staleAuthorityRejected: true, receipt: 'SYNTHETIC distinct whole-request acknowledgment; no execution.' } };
  request.acknowledgment.requestDigest = recoveryContinuationDigest(request);
  return request;
}

module.exports = { refinementFixture, refinementEvent, rebind, tamperedStates, head, owner,
  candidateFixture, candidateEvent, supersessionFixture, supersessionEvent, rebindSupersession,
  supersessionBeneficiaryFixture, fencedExtensionFixture, fencedExtensionEvent, rebindFencedExtension,
  fencedExtensionContext, fencedExtensionContinuation };
