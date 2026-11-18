'use strict';

const { createState, applyEvent, digest, heldReservations, recoveryContinuationDigest } = require('./registry.cjs');

const head = '1'.repeat(40);
const paths = ['directory.packages.props', 'src\\app\\app.csproj', 'src\\app\\packages.lock.json',
  'src\\desktop\\packages.lock.json', 'installer\\packages.lock.json',
  'tests\\app\\packages.lock.json', 'tests\\windows\\packages.lock.json',
  'tests\\fixture\\packages.lock.json', 'src\\windows\\packages.lock.json'];

function incidentFixture() {
  let state = createState('synthetic-coordinator', head, 'delivery');
  state = applyEvent(state, { id: 'adopt-incident', type: 'adopt', owner: 'synthetic-owner',
    key: 'incident-owner', outcome: 'Synthetic bounded dependency operation', pool: 'ready',
    kind: 'feature', head, scopes: paths.slice(0, -1), evidence: 'Synthetic original exact reservation.' }).state;
  state = applyEvent(state, { id: 'incident-blocked', type: 'blocked', owner: 'synthetic-owner',
    key: 'incident-owner', reason: 'Scope incident; unrelated retained ownership/cleanup blocker.' }).state;
  state = applyEvent(state, { id: 'held-owner', type: 'adopt', owner: 'other-owner',
    key: 'held', outcome: 'Other retained work', pool: 'ready', kind: 'feature', head,
    scopes: ['src\\held.cs'], evidence: 'Synthetic exclusive held guard.' }).state;
  state = applyEvent(state, { id: 'held-publication', type: 'published', owner: 'other-owner',
    key: 'held', proof: { pushedHead: head, remoteHead: head, pr: 91, ready: true, enrolled: true } }).state;
  return applyEvent(state, { id: 'held-blocker', type: 'blocked', owner: 'other-owner',
    key: 'held', reason: 'Host-managed worktree cleanup blocked.' }).state;
}

function rebindRecovery(event) {
  event.acknowledgment.proofDigest = digest(event.proof);
  event.acknowledgment.fence = structuredClone(event.proof.reconciliation.fence ?? null);
  event.authorization.subjectDigest = digest({ proof: event.proof, acknowledgment: event.acknowledgment });
  return event;
}

function recoveryEvidence(event) {
  return { kind: 'host-verified-scope-incident', coordinator: event.proof.coordinator,
    checkpointRevision: event.proof.checkpointRevision, proofDigest: digest(event.proof),
    ownerResponseId: event.acknowledgment.id, humanDecisionId: event.authorization.id,
    humanDecisionDigest: digest(event.authorization), sourceIdentityVerified: true, sourcePauseVerified: true,
    commandsJoinedVerified: true, instructionAuthorityVerified: true,
    receipt: 'SYNTHETIC host readback, not human authentication or live evidence.' };
}

function recoveryEvent(state, revision = 41, mode = 'human-approved-source-fence') {
  const item = state.items.find(value => value.key === 'incident-owner');
  const fence = { epoch: 1, nonce: 'synthetic-fence-nonce-1', revokesEarlierSourceAuthority: true,
    established: true, receipt: 'SYNTHETIC exact-owner enforced authority fence.' };
  const proof = { version: 1, repository: 'roryprimrose/Kora', coordinator: state.coordinator,
    checkpointRevision: revision, head: state.head, owner: item.owner, key: item.key,
    source: { owner: item.owner, worktree: 'Q:\\Synthetic\\incident-owner',
      branch: 'synthetic/incident-owner', head: item.head,
      receipt: 'SYNTHETIC current exact-owner source identity, not a real worktree.' },
    expectedItemDigest: digest(item), reservation: Object.fromEntries(
      ['outcome', 'kind', 'pool', 'head', 'pr'].map(name => [name, item[name]])),
    scopeRevision: item.scopeRevision ?? 0, fromScopes: [...item.scopes],
    additionalScopes: [paths.at(-1), 'third-party-notices.md'],
    toScopes: [...item.scopes, paths.at(-1), 'third-party-notices.md'],
    exactFiles: { complete: true, paths: [paths.at(-1), 'third-party-notices.md'],
      receipt: 'SYNTHETIC verified exact tracked file kinds, not directory or glob coverage.' },
    incident: { id: 'synthetic-restore-incident', owner: item.owner, key: item.key,
      command: { id: 'synthetic-command-1', command: 'whole-solution restore (not executed)',
        receipt: 'SYNTHETIC original command and complete touched-path inventory.' },
      originalHead: item.head, originalScopes: [...item.scopes],
      unauthorizedPaths: [paths.at(-1)], receipt: 'SYNTHETIC unauthorized incidental effect; never retroauthorized.' },
    manifest: { complete: true, actual: [...paths], planned: [...paths, 'third-party-notices.md'] },
    preservation: { complete: true, commits: [],
      retained: paths.slice(0, -1).map(path => ({ path, receipt: 'SYNTHETIC retained dirty file.' })),
      unpreserved: [{ path: paths.at(-1), receipt: 'SYNTHETIC touched then restored; history retained.' }] },
    workingTree: { complete: true, dirty: paths.slice(0, -1), restored: [paths.at(-1)],
      receipt: 'SYNTHETIC full status plus original command/diff and restored-path history.' },
    quiescence: { sourcePaused: true, commandsRunning: false, workJoined: true,
      receipt: 'SYNTHETIC live pause and joined work, independent of pending enumeration.' },
    reconciliation: mode === 'enumerated-pending-zero'
      ? { kind: mode, pendingInstructions: 0, processingInstructions: 0,
        receipt: 'SYNTHETIC authoritative pending and processing enumeration.' }
      : { kind: mode, pendingInstructions: 'UNKNOWN', processingInstructions: 'UNKNOWN',
        fence, receipt: 'SYNTHETIC explicit fence; no authoritative queue drain claimed.' },
    exclusivity: { complete: true, reservations: heldReservations(state, item.key),
      receipt: 'SYNTHETIC complete current exclusive held guards.' } };
  return rebindRecovery({ id: 'recover-synthetic-incident', type: 'recover-scope-incident', proof,
    acknowledgment: { id: 'synthetic-fresh-owner-response', receipt: 'SYNTHETIC final whole-proof owner response.',
      owner: item.owner, key: item.key, scopeRevision: proof.scopeRevision, proofDigest: '',
      workJoined: true, staleAuthorityRejected: true, fence: null },
    authorization: { kind: 'explicit-human-recovery-decision', id: 'synthetic-human-decision',
      actor: 'synthetic-human', decision: 'prospective-coverage-only', subjectDigest: '',
      receipt: 'SYNTHETIC current explicit human decision over final proof/ack, including fence.' } });
}

function recoveryContext(event) {
  return { revision: event.proof.checkpointRevision, recoveryEvidence: recoveryEvidence(event) };
}

function continuation(state, revision = 42, renewed = false) {
  const record = state.recoveries.at(-1);
  const item = state.items.find(value => value.key === record.event.proof.key);
  const grantDigest = digest(record);
  const fence = structuredClone(record.event.proof.reconciliation.fence ?? null);
  const request = { version: renewed ? 2 : 1, kind: 'exact-recovered-owner-continuation', coordinator: state.coordinator,
    head: state.head, sourceHead: record.event.proof.source.head,
    checkpointRevision: revision, key: item.key, owner: item.owner,
    expectedItemDigest: digest(item), scopeRevision: item.scopeRevision, grantDigest, fence,
    acknowledgment: { id: 'synthetic-later-owner-response', owner: item.owner, grantDigest,
      requestDigest: '', fence: structuredClone(fence), workJoined: true, staleAuthorityRejected: true,
      receipt: 'SYNTHETIC separate later exact continuation response; not blocker resolution.' } };
  request.acknowledgment.requestDigest = recoveryContinuationDigest(request);
  if (renewed) {
    request.renewalDigest = digest(state.recoveryRenewals.at(-1));
    request.acknowledgment.id = `synthetic-renewed-continuation-${revision}`;
    request.acknowledgment.requestDigest = recoveryContinuationDigest(request);
  }
  return request;
}

function renewalFixture(mode = 'human-approved-source-fence') {
  const initial = incidentFixture();
  const recovery = recoveryEvent(initial, 41, mode);
  const accepted = applyEvent(initial, recovery, recoveryContext(recovery)).state;
  return applyEvent(accepted, { id: 'synthetic-new-main', type: 'refresh', head: '2'.repeat(40) }).state;
}

function rebindRenewal(event) {
  event.acknowledgment.proofDigest = digest(event.proof);
  return event;
}

function renewalEvent(state, revision = 43) {
  const grant = state.recoveries.at(-1);
  const original = grant.event.proof;
  const item = state.items.find(value => value.key === original.key);
  const previous = (state.recoveryRenewals ?? []).filter(record =>
    record.event.proof.grantDigest === digest(grant)).at(-1);
  const proof = { version: 1, repository: original.repository, coordinator: state.coordinator,
    checkpointRevision: revision, head: state.head, key: item.key, owner: item.owner,
    grantDigest: digest(grant), previousRenewalDigest: previous ? digest(previous) : null,
    expectedItemDigest: digest(item), scopeRevision: item.scopeRevision, scopes: [...item.scopes],
    source: { ...original.source, receipt: 'SYNTHETIC fresh unchanged source identity readback.' },
    fence: structuredClone(original.reconciliation.fence ?? null),
    quiescence: { sourcePaused: true, commandsRunning: false, workJoined: true,
      pendingInstructions: original.reconciliation.pendingInstructions,
      processingInstructions: original.reconciliation.processingInstructions,
      receipt: 'SYNTHETIC fresh live pause/join and exact instruction authority readback.' },
    exclusivity: { complete: true, reservations: heldReservations(state, item.key),
      receipt: 'SYNTHETIC current complete exclusive reservation inventory.' },
    compatibility: { baseHead: original.head, fromHead: previous?.event.proof.head ?? original.head,
      toHead: state.head, ancestorVerified: true, complete: true,
      changedPaths: ['docs\\unrelated.md'], scopeReviewCompatible: true, sourceReviewCompatible: true,
      receipt: 'SYNTHETIC complete original-main to current-main diff, ancestry and source/contract review; not a Git query.' } };
  return rebindRenewal({ id: `synthetic-renewal-${revision}`, type: 'renew-recovery-continuation', proof,
    acknowledgment: { id: `synthetic-renewal-owner-response-${revision}`, owner: item.owner, key: item.key,
      scopeRevision: item.scopeRevision, proofDigest: '', fence: structuredClone(proof.fence),
      workJoined: true, staleAuthorityRejected: true,
      receipt: 'SYNTHETIC fresh exact owner whole-renewal compatibility response.' } });
}

function renewalEvidence(event) {
  const p = event.proof;
  return { kind: 'host-verified-recovery-renewal', coordinator: p.coordinator,
    checkpointRevision: p.checkpointRevision, proofDigest: digest(p),
    ownerResponseId: event.acknowledgment.id, compatibilityDigest: digest(p.compatibility),
    sourceIdentityVerified: true, sourcePauseVerified: true, commandsJoinedVerified: true,
    instructionAuthorityVerified: true, compatibilityVerified: true,
    receipt: 'SYNTHETIC independent host observations; not real compatibility, authentication or execution authority.' };
}

function renewalContext(event) {
  return { revision: event.proof.checkpointRevision, renewalEvidence: renewalEvidence(event) };
}

module.exports = { incidentFixture, recoveryEvent, rebindRecovery, recoveryEvidence, recoveryContext,
  continuation, paths, renewalFixture, renewalEvent, rebindRenewal, renewalEvidence, renewalContext };

if (require.main === module) {
  const state = incidentFixture();
  const event = recoveryEvent(state);
  console.log(JSON.stringify({ operation: 'apply', expectedRevision: 41, event,
    recoveryEvidence: recoveryEvidence(event) }, null, 2));
}
