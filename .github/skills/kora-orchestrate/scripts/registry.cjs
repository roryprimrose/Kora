'use strict';

const path = require('node:path');
const { createHash } = require('node:crypto');

const LIMITS = Object.freeze({ repair: 4, ready: 10 });
const PHASES = ['candidate', 'reserved', 'running', 'published', 'complete'];
const requireValue = (condition, message) => {
  if (!condition) throw new Error(message);
};
const text = value => typeof value === 'string' && value.trim().length > 0;
const head = value => typeof value === 'string' && /^[0-9a-f]{40}$/i.test(value);
const holdsSlot = item => item.phase !== 'candidate' && item.phase !== 'complete';

function scope(value) {
  requireValue(text(value) && !path.win32.isAbsolute(value) &&
    !/[/*?:]/.test(value) && !value.split('\\').some(part => ['', '.', '..'].includes(part)),
  'Scope must be a repository-relative Windows path without globs or traversal.');
  return value.toLowerCase();
}

function overlaps(left, right) {
  return left.some(a => right.some(b => a === b || a.startsWith(`${b}\\`) || b.startsWith(`${a}\\`)));
}

const REFINEMENT_MARKER = 'scope-refinement:v1:';
const AMENDMENT_MARKER = 'scope-amendment:v1:';
const READMISSION_MARKER = 'candidate-revalidation:v1:';
const SUPERSESSION_MARKER = 'write-supersession:v1:';
const RECOVERY_MARKER = 'scope-incident-recovery:v1:';
const RENEWAL_MARKER = 'recovery-continuation-renewal:v1:';
const equal = (left, right) => JSON.stringify(left) === JSON.stringify(right);
const covers = (scopes, value) => scopes.some(s => value === s || value.startsWith(`${s}\\`));

function digest(value) {
  const canonical = input => Array.isArray(input) ? input.map(canonical) :
    input && typeof input === 'object'
      ? Object.fromEntries(Object.keys(input).sort().map(key => [key, canonical(input[key])])) : input;
  return createHash('sha256').update(JSON.stringify(canonical(value))).digest('hex');
}

function fields(value, names) {
  requireValue(value && typeof value === 'object' && !Array.isArray(value) &&
    equal(Object.keys(value).sort(), names.split(' ').sort()), 'Malformed refinement fields.');
}

function canonicalPaths(values, nonempty = false) {
  requireValue(Array.isArray(values) && (!nonempty || values.length > 0) &&
    values.every(value => scope(value) === value && !/[\x00-\x1f<>"|]/.test(value) &&
      value.split('\\').every(part => part.trim() === part && !part.endsWith('.'))) &&
    new Set(values).size === values.length,
  'Refinement paths must be canonical and unique.');
}

function validateRefinement(record, item, previous) {
  fields(record, 'eventId proof acknowledgment');
  const p = record.proof;
  const a = record.acknowledgment;
  fields(p, 'version owner key reservation publication scopeRevision fromScopes toScopes phase publicationHead manifest preservation stoppedWriting expansionFrozen pendingInstructions receipt');
  fields(a, 'id receipt owner key scopeRevision proofDigest');
  requireValue(text(record.eventId) && p.version === 1 && p.owner === item.owner &&
    p.key === item.key && Number.isSafeInteger(p.scopeRevision) && p.scopeRevision >= 0 &&
    ['running', 'published'].includes(p.phase) && p.stoppedWriting === true &&
    p.expansionFrozen === true && p.pendingInstructions === 0 && text(p.receipt),
  'Exact stopped owner and complete refinement proof required.');
  fields(p.reservation, 'outcome kind pool head pr');
  requireValue(['outcome', 'kind', 'pool', 'head'].every(key => p.reservation[key] === item[key]) &&
    (p.reservation.pr === item.pr || p.phase === 'running' && p.reservation.pr === null),
  'Refinement reservation identity is inconsistent.');
  requireValue(p.phase === 'published'
    ? ['published', 'complete'].includes(item.phase) && head(p.publicationHead) &&
      p.publicationHead === item.publication?.remoteHead && digest(p.publication) === digest(item.publication)
    : p.publicationHead === null && p.publication === null, 'Refinement publication binding is inconsistent.');
  canonicalPaths(p.fromScopes, true);
  canonicalPaths(p.toScopes, true);
  requireValue(p.toScopes.every(value => covers(p.fromScopes, value)) &&
    !p.fromScopes.every(value => covers(p.toScopes, value)),
  'Refinement must be a nonempty proper coverage subset.');
  validateManifest(p);
  if (previous) {
    requireValue(equal(p.fromScopes, previous.proof.toScopes) &&
      (previous.proof.phase !== 'published' || p.phase === 'published') &&
      [...previous.proof.manifest.actual, ...previous.proof.manifest.planned]
        .every(value => [...p.manifest.actual, ...p.manifest.planned].includes(value)) &&
      previous.proof.manifest.actual.every(value => p.manifest.actual.includes(value)),
    'Refinement history loses scopes or written/planned paths.');
  }
  validateAcknowledgment(a, p);
}

function validateManifest(p) {
  fields(p.manifest, 'complete actual planned');
  requireValue(p.manifest.complete === true, 'Complete actual/planned write manifest required.');
  canonicalPaths(p.manifest.actual);
  canonicalPaths(p.manifest.planned);
  fields(p.preservation, 'complete commits retained unpreserved');
  requireValue(p.preservation.complete === true && Array.isArray(p.preservation.commits) &&
    Array.isArray(p.preservation.retained) && Array.isArray(p.preservation.unpreserved),
  'Complete preservation evidence required.');
  const committed = [];
  const commits = new Set();
  for (const commit of p.preservation.commits) {
    fields(commit, 'head paths receipt');
    requireValue(head(commit.head) && !commits.has(commit.head) && text(commit.receipt),
      'Invalid preserved commit evidence.');
    commits.add(commit.head);
    canonicalPaths(commit.paths);
    committed.push(...commit.paths);
  }
  requireValue(p.phase !== 'published' || commits.has(p.publicationHead),
    'Original publication commit must remain preserved.');
  const local = [];
  for (const entry of [...p.preservation.retained, ...p.preservation.unpreserved]) {
    fields(entry, 'path receipt');
    requireValue(text(entry.receipt), 'Local retained/unpreserved path receipt required.');
    local.push(entry.path);
  }
  canonicalPaths(local);
  const actual = new Set([...committed, ...local]);
  requireValue(actual.size === p.manifest.actual.length &&
    p.manifest.actual.every(value => actual.has(value)), 'Preservation and actual writes disagree.');
  requireValue([...p.manifest.actual, ...p.manifest.planned]
    .every(value => covers(p.toScopes, value)), 'Refinement leaves uncovered writes.');
}

function validateAcknowledgment(a, p) {
  requireValue(text(a.id) && text(a.receipt) && a.owner === p.owner && a.key === p.key &&
    a.scopeRevision === p.scopeRevision && a.proofDigest === digest(p),
  'Fresh exact-owner acknowledgment must bind the complete proof.');
}

function validateRecoveryEvent(event) {
  fields(event, 'id type proof acknowledgment authorization');
  const p = event.proof;
  fields(p, 'version repository coordinator checkpointRevision head source owner key expectedItemDigest reservation scopeRevision fromScopes additionalScopes toScopes exactFiles incident manifest preservation workingTree quiescence reconciliation exclusivity');
  fields(p.source, 'owner worktree branch head receipt');
  fields(p.reservation, 'outcome kind pool head pr');
  fields(p.incident, 'id owner key command originalHead originalScopes unauthorizedPaths receipt');
  fields(p.incident.command, 'id command receipt');
  fields(p.workingTree, 'complete dirty restored receipt');
  fields(p.quiescence, 'sourcePaused commandsRunning workJoined receipt');
  fields(p.exclusivity, 'complete reservations receipt');
  const a = event.acknowledgment;
  fields(a, 'id receipt owner key scopeRevision proofDigest workJoined staleAuthorityRejected fence');
  const h = event.authorization;
  fields(h, 'kind id actor decision subjectDigest receipt');
  requireValue(text(event.id) && event.type === 'recover-scope-incident' && p.version === 1 &&
    p.repository === 'roryprimrose/Kora' && text(p.coordinator) && head(p.head) &&
    Number.isSafeInteger(p.checkpointRevision) && p.checkpointRevision > 0 &&
    text(p.owner) && text(p.key) && /^[0-9a-f]{64}$/.test(p.expectedItemDigest) &&
    Number.isSafeInteger(p.scopeRevision) && p.scopeRevision >= 0,
  'Complete versioned incident recovery identity/source/revision required.');
  canonicalPaths(p.fromScopes, true);
  canonicalPaths(p.additionalScopes, true);
  canonicalPaths(p.toScopes, true);
  fields(p.exactFiles, 'complete paths receipt');
  requireValue(p.exactFiles.complete === true && equal(p.exactFiles.paths, p.additionalScopes) &&
    text(p.exactFiles.receipt), 'Complete exact file-kind evidence for every prospective addition required.');
  requireValue(equal(p.toScopes, [...p.fromScopes, ...p.additionalScopes]) &&
    p.additionalScopes.every((value, index) => !overlaps([value],
      [...p.fromScopes, ...p.additionalScopes.slice(0, index)])),
  'Recovery requires an exact prospective disjoint path union.');
  const i = p.incident;
  canonicalPaths(i.originalScopes, true);
  canonicalPaths(i.unauthorizedPaths, true);
  requireValue(text(i.id) && i.owner === p.owner && i.key === p.key &&
    i.originalHead === p.reservation.head && equal(i.originalScopes, p.fromScopes) &&
    text(i.command.id) && text(i.command.command) && text(i.command.receipt) && text(i.receipt),
  'Original incident command, owner and reservation evidence required.');
  validateManifest(p);
  requireValue(p.source.owner === p.owner && text(p.source.worktree) &&
    path.win32.isAbsolute(p.source.worktree) && path.win32.normalize(p.source.worktree) === p.source.worktree &&
    text(p.source.branch) && head(p.source.head) && text(p.source.receipt) &&
    (p.source.head === p.reservation.head || p.preservation.commits.some(commit => commit.head === p.source.head)),
  'Exact current same-owner worktree/branch/head and original or preserved source required.');
  requireValue(equal(i.unauthorizedPaths, p.manifest.actual.filter(value => !covers(p.fromScopes, value))) &&
    i.unauthorizedPaths.every(value => p.additionalScopes.includes(value)) &&
    p.additionalScopes.every(value => p.manifest.planned.includes(value)),
  'Complete unauthorized actual paths must remain incidental, with exact prospective additions.');
  canonicalPaths(p.workingTree.dirty);
  canonicalPaths(p.workingTree.restored);
  requireValue(p.workingTree.complete === true && text(p.workingTree.receipt) &&
    !overlaps(p.workingTree.dirty, p.workingTree.restored) &&
    [...p.workingTree.dirty, ...p.workingTree.restored].every(value => p.manifest.actual.includes(value)) &&
    p.workingTree.dirty.every(value => p.preservation.retained.some(entry => entry.path === value)) &&
    p.workingTree.restored.every(value => p.preservation.unpreserved.some(entry => entry.path === value)) &&
    p.preservation.unpreserved.every(entry => p.workingTree.restored.includes(entry.path)),
  'Complete dirty/restored write history and preservation classification required.');
  requireValue(p.quiescence.sourcePaused === true && p.quiescence.commandsRunning === false &&
    p.quiescence.workJoined === true && text(p.quiescence.receipt),
  'Live source pause and joined command quiescence required independently of queue enumeration.');
  const r = p.reconciliation;
  if (r?.kind === 'enumerated-pending-zero') {
    fields(r, 'kind pendingInstructions processingInstructions receipt');
    requireValue(r.pendingInstructions === 0 && r.processingInstructions === 0 && text(r.receipt) &&
      a.fence === null, 'Real enumerated pending/processing zero evidence required.');
  } else {
    fields(r, 'kind pendingInstructions processingInstructions fence receipt');
    fields(r.fence, 'epoch nonce revokesEarlierSourceAuthority established receipt');
    requireValue(r.kind === 'human-approved-source-fence' && r.pendingInstructions === 'UNKNOWN' &&
      r.processingInstructions === 'UNKNOWN' && text(r.receipt) &&
      Number.isSafeInteger(r.fence.epoch) && r.fence.epoch > 0 && text(r.fence.nonce) &&
      r.fence.revokesEarlierSourceAuthority === true && r.fence.established === true &&
      text(r.fence.receipt) && equal(a.fence, r.fence),
    'UNKNOWN requires an explicit established human-approved source-authority fence, never a queue drain.');
  }
  requireValue(p.exclusivity.complete === true && Array.isArray(p.exclusivity.reservations) &&
    text(p.exclusivity.receipt), 'Complete recovery held-reservation snapshot required.');
  const keys = new Set([p.key]);
  for (const reservation of p.exclusivity.reservations) {
    fields(reservation, 'key owner scopes scopeRevision sourceWritesRetired');
    canonicalPaths(reservation.scopes);
    requireValue(text(reservation.key) && !keys.has(reservation.key) &&
      (reservation.owner === null || text(reservation.owner)) &&
      Number.isSafeInteger(reservation.scopeRevision) && reservation.scopeRevision >= 0 &&
      typeof reservation.sourceWritesRetired === 'boolean' &&
      !overlaps(p.toScopes, reservation.scopes), 'Recovery conflicts with an exclusive held guard.');
    keys.add(reservation.key);
  }
  validateAcknowledgment(a, p);
  requireValue(a.workJoined === true && a.staleAuthorityRejected === true,
    'Fresh exact-owner acknowledgment must confirm joined work and rejection of earlier source authority.');
  requireValue(h.kind === 'explicit-human-recovery-decision' && text(h.id) && text(h.actor) &&
    h.decision === 'prospective-coverage-only' && text(h.receipt) &&
    h.subjectDigest === digest({ proof: p, acknowledgment: a }),
  'Current explicit human decision must bind the final whole proof and exact-owner acknowledgment.');
}

function validateRecoveryEvidence(event, evidence) {
  fields(evidence, 'kind coordinator checkpointRevision proofDigest ownerResponseId humanDecisionId humanDecisionDigest sourceIdentityVerified sourcePauseVerified commandsJoinedVerified instructionAuthorityVerified receipt');
  requireValue(evidence.kind === 'host-verified-scope-incident' &&
    evidence.coordinator === event.proof.coordinator &&
    evidence.checkpointRevision === event.proof.checkpointRevision &&
    evidence.proofDigest === digest(event.proof) &&
    evidence.ownerResponseId === event.acknowledgment.id &&
    evidence.humanDecisionId === event.authorization.id &&
    evidence.humanDecisionDigest === digest(event.authorization) &&
    evidence.sourceIdentityVerified === true && evidence.sourcePauseVerified === true &&
    evidence.commandsJoinedVerified === true &&
    evidence.instructionAuthorityVerified === true && text(evidence.receipt),
  'Independent current host readback of human decision, owner response, pause/join and instruction authority required.');
}

function recoveryRecords(state, key) {
  return (state.recoveries ?? []).filter(record => record.event.proof.key === key);
}

function validateRecoveries(state) {
  const markers = state.seen.filter(id => id.startsWith(RECOVERY_MARKER));
  if (state.version < 6) {
    requireValue(!Object.hasOwn(state, 'recoveries') && markers.length === 0 &&
      state.items.every(item => !Object.hasOwn(item, 'scopeRecoveries')), 'Incident recovery schema downgrade.');
    return;
  }
  requireValue(Array.isArray(state.recoveries), 'Incident recovery history missing.');
  const acknowledgments = new Set([...state.items.flatMap(item =>
    [...item.scopeHistory, ...item.scopeAmendments].map(record => record.acknowledgment.id)),
  ...state.supersessions.flatMap(record => record.event.proof.holders
    .filter(holder => holder.acknowledgment).map(holder => holder.acknowledgment.id))]);
  const incidents = new Set();
  const decisions = new Set();
  const events = new Set();
  const nonces = new Set();
  for (const record of state.recoveries) {
    fields(record, 'event before fromVersion hostEvidence commands');
    const { event, before } = record;
    validateRecoveryEvent(event);
    validateRecoveryEvidence(event, record.hostEvidence);
    const p = event.proof;
    const item = state.items.find(value => value.key === p.key);
    for (const [minimum, names] of [[2, ['scopeRevision', 'scopeHistory', 'sourceWritesRetired']],
      [3, ['scopeAmendments']], [4, ['admissionHistory']], [6, ['scopeRecoveries']]]) {
      requireValue(names.every(name => Object.hasOwn(before, name) === (record.fromVersion >= minimum)),
        'Recovery original item schema/version binding changed.');
    }
    requireValue([1, 2, 3, 4, 5, 6, 7, 8, 9].includes(record.fromVersion) &&
      digest(before) === p.expectedItemDigest && before.phase === 'running' &&
      before.owner === p.owner && before.key === p.key && !before.sourceWritesRetired &&
      before.publication === null && (before.scopeRevision ?? 0) === p.scopeRevision &&
      equal(before.scopes, p.fromScopes) &&
      equal(p.reservation, Object.fromEntries(['outcome', 'kind', 'pool', 'head', 'pr']
        .map(name => [name, before[name]]))) &&
      item && item.owner === before.owner &&
      ['outcome', 'kind', 'pool', 'head', 'evidence'].every(name => item[name] === before[name]) &&
      (before.pr === null || before.pr === item.pr) &&
      equal(before.scopeHistory ?? [], item.scopeHistory.filter(old => old.proof.scopeRevision < p.scopeRevision)) &&
      equal(before.scopeAmendments ?? [], item.scopeAmendments.filter(old => old.proof.scopeRevision < p.scopeRevision)) &&
      equal(before.admissionHistory ?? [], item.admissionHistory) &&
      equal(before.scopeRecoveries ?? [], item.scopeRecoveries.slice(0, item.scopeRecoveries.indexOf(event.id))) &&
      equal(record.commands, []), 'Recovery original identity/history or prospective-only action binding changed.');
    requireValue(state.seen.includes(event.id) && !events.has(event.id) &&
      !incidents.has(p.incident.id) && !decisions.has(event.authorization.id) &&
      !acknowledgments.has(event.acknowledgment.id), 'Incident, decision, event or acknowledgment replay.');
    events.add(event.id);
    incidents.add(p.incident.id);
    decisions.add(event.authorization.id);
    acknowledgments.add(event.acknowledgment.id);
    const previousFence = state.recoveries.slice(0, state.recoveries.indexOf(record))
      .filter(old => old.event.proof.owner === p.owner)
      .map(old => old.event.proof.reconciliation.fence).filter(Boolean).at(-1);
    requireValue(!previousFence || p.reconciliation.fence,
      'An established source-authority fence cannot be dropped by later pending-zero evidence.');
    if (p.reconciliation.fence) {
      const fence = p.reconciliation.fence;
      requireValue(!nonces.has(fence.nonce) && (!previousFence || fence.epoch > previousFence.epoch),
        'Stale recovery fence epoch/nonce.');
      nonces.add(fence.nonce);
    }
  }
  for (const item of state.items) {
    requireValue(Array.isArray(item.scopeRecoveries) &&
      equal(item.scopeRecoveries, recoveryRecords(state, item.key).map(record => record.event.id)),
    'Recovery per-item history index omitted or forged.');
  }
  requireValue(equal(markers.slice().sort(), state.recoveries.map(record =>
    `${RECOVERY_MARKER}${digest(record)}`).sort()), 'Recovery complete history/seen binding missing.');
}

function recoveryContinuationDigest(request) {
  const { acknowledgment, ...body } = request;
  return digest(body);
}

function validateRecoveryRenewal(record, grant, previous) {
  fields(record, 'event before hostEvidence commands');
  const { event, before, hostEvidence: h } = record;
  fields(event, 'id type proof acknowledgment');
  const p = event.proof;
  fields(p, 'version repository coordinator checkpointRevision head key owner grantDigest previousRenewalDigest expectedItemDigest scopeRevision scopes source fence quiescence exclusivity compatibility');
  const original = grant.event.proof;
  fields(p.source, 'owner worktree branch head receipt');
  fields(p.quiescence, 'sourcePaused commandsRunning workJoined pendingInstructions processingInstructions receipt');
  fields(p.exclusivity, 'complete reservations receipt');
  fields(p.compatibility, 'baseHead fromHead toHead ancestorVerified complete changedPaths scopeReviewCompatible sourceReviewCompatible receipt');
  fields(event.acknowledgment, 'id receipt owner key scopeRevision proofDigest workJoined staleAuthorityRejected fence');
  fields(h, 'kind coordinator checkpointRevision proofDigest ownerResponseId compatibilityDigest sourceIdentityVerified sourcePauseVerified commandsJoinedVerified instructionAuthorityVerified compatibilityVerified receipt');
  canonicalPaths(p.scopes, true);
  canonicalPaths(p.compatibility.changedPaths);
  requireValue(text(event.id) && event.type === 'renew-recovery-continuation' &&
    p.version === 1 && p.repository === original.repository && p.coordinator === original.coordinator &&
    head(p.head) && p.head !== original.head && p.key === original.key && p.owner === original.owner &&
    Number.isSafeInteger(p.checkpointRevision) && p.checkpointRevision > original.checkpointRevision &&
    p.grantDigest === digest(grant) && p.previousRenewalDigest === (previous ? digest(previous) : null) &&
    (!previous || p.checkpointRevision > previous.event.proof.checkpointRevision) &&
    p.expectedItemDigest === digest(before) && before.key === p.key && before.owner === p.owner &&
    before.phase === 'running' && !before.sourceWritesRetired && before.publication === null &&
    p.scopeRevision === before.scopeRevision && p.scopeRevision === original.scopeRevision + 1 &&
    equal(p.scopes, before.scopes) && equal(p.scopes, original.toScopes) &&
    ['owner', 'worktree', 'branch', 'head'].every(name => p.source[name] === original.source[name]) &&
    text(p.source.receipt) && equal(p.fence, original.reconciliation.fence ?? null) &&
    equal(record.commands, []),
  'Renewal must retain the exact original grant, same owner/source base/scopes/fence and current item.');
  const q = p.quiescence;
  requireValue(q.sourcePaused === true && q.commandsRunning === false && q.workJoined === true &&
    q.pendingInstructions === original.reconciliation.pendingInstructions &&
    q.processingInstructions === original.reconciliation.processingInstructions && text(q.receipt),
  'Renewal requires fresh live pause/join and truthful unchanged instruction reconciliation.');
  const c = p.compatibility;
  requireValue(c.baseHead === original.head && c.fromHead === (previous?.event.proof.head ?? original.head) &&
    c.toHead === p.head && c.ancestorVerified === true && c.complete === true &&
    c.scopeReviewCompatible === true && c.sourceReviewCompatible === true && text(c.receipt) &&
    !overlaps(c.changedPaths, p.scopes),
  'Fresh complete compatible main advancement and unchanged reserved paths/source contracts required.');
  requireValue(p.exclusivity.complete === true && Array.isArray(p.exclusivity.reservations) &&
    text(p.exclusivity.receipt), 'Complete current renewal exclusivity evidence required.');
  const keys = new Set([p.key]);
  for (const held of p.exclusivity.reservations) {
    fields(held, 'key owner scopes scopeRevision sourceWritesRetired');
    canonicalPaths(held.scopes);
    requireValue(text(held.key) && !keys.has(held.key) && (held.owner === null || text(held.owner)) &&
      Number.isSafeInteger(held.scopeRevision) && held.scopeRevision >= 0 &&
      typeof held.sourceWritesRetired === 'boolean' && !overlaps(held.scopes, p.scopes),
    'Renewal conflicts with retained exclusive ownership.');
    keys.add(held.key);
  }
  const a = event.acknowledgment;
  validateAcknowledgment(a, p);
  requireValue(a.workJoined === true && a.staleAuthorityRejected === true && equal(a.fence, p.fence),
    'Fresh owner acknowledgment must bind the entire renewal compatibility proof and retained fence.');
  requireValue(h.kind === 'host-verified-recovery-renewal' && h.coordinator === p.coordinator &&
    h.checkpointRevision === p.checkpointRevision && h.proofDigest === digest(p) &&
    h.ownerResponseId === a.id && h.compatibilityDigest === digest(c) &&
    h.sourceIdentityVerified === true && h.sourcePauseVerified === true &&
    h.commandsJoinedVerified === true && h.instructionAuthorityVerified === true &&
    h.compatibilityVerified === true && text(h.receipt),
  'Independent current host readback of renewal compatibility, source, owner and live instruction authority required.');
}

function validateRecoveryRenewals(state) {
  const markers = state.seen.filter(id => id.startsWith(RENEWAL_MARKER));
  if (state.version < 7) {
    requireValue(!Object.hasOwn(state, 'recoveryRenewals') && markers.length === 0,
      'Recovery renewal schema downgrade.');
    return;
  }
  requireValue(Array.isArray(state.recoveryRenewals), 'Recovery renewal history missing.');
  const events = new Set();
  const acks = new Set([
    ...state.items.flatMap(item => [...item.scopeHistory, ...item.scopeAmendments]
      .map(record => record.acknowledgment.id)),
    ...state.recoveries.map(record => record.event.acknowledgment.id),
    ...state.supersessions.flatMap(record => record.event.proof.holders
      .filter(holder => holder.acknowledgment).map(holder => holder.acknowledgment.id))
  ]);
  const previous = new Map();
  for (const record of state.recoveryRenewals) {
    const grantDigest = record?.event?.proof?.grantDigest;
    const grant = state.recoveries.find(value => digest(value) === grantDigest);
    requireValue(grant, 'Renewal original grant is missing or altered.');
    validateRecoveryRenewal(record, grant, previous.get(grantDigest));
    const { event } = record;
    requireValue(state.seen.includes(event.id) && !events.has(event.id) &&
      !acks.has(event.acknowledgment.id), 'Recovery renewal event or owner acknowledgment replay.');
    events.add(event.id);
    acks.add(event.acknowledgment.id);
    previous.set(grantDigest, record);
  }
  requireValue(equal(markers.slice().sort(), state.recoveryRenewals.map(record =>
    `${RENEWAL_MARKER}${digest(record)}`).sort()), 'Recovery renewal history/seen binding missing.');
}

function validateRecoveryContinuation(state, request, context = {}) {
  validate(state);
  fields(request, `version kind coordinator head sourceHead checkpointRevision key owner expectedItemDigest scopeRevision grantDigest fence acknowledgment${request?.version === 2 ? ' renewalDigest' : ''}`);
  fields(request.acknowledgment, 'id owner requestDigest grantDigest fence workJoined staleAuthorityRejected receipt');
  const item = state.items.find(value => value.key === request.key);
  const record = recoveryRecords(state, request.key).at(-1);
  const fence = record?.event.proof.reconciliation.fence ?? null;
  const renewal = (state.recoveryRenewals ?? []).filter(value =>
    value.event.proof.key === request.key).at(-1);
  const renewed = request.version === 2;
  const currentHead = renewed ? renewal?.event.proof.head : record?.event.proof.head;
  if (renewed) {
    const p = renewal?.event.proof;
    requireValue(p && request.renewalDigest === digest(renewal) && p.grantDigest === digest(record) &&
      p.expectedItemDigest === digest(item) && p.scopeRevision === item.scopeRevision &&
      equal(p.scopes, item.scopes) && equal(p.fence, fence) && p.owner === item.owner &&
      context.revision === p.checkpointRevision + 1 &&
      equal(p.exclusivity.reservations, heldReservations(state, request.key)) &&
      !state.recoveryRenewals.some(old => old.event.acknowledgment.id === request.acknowledgment.id) &&
      !state.recoveries.some(old => old.event.acknowledgment.id === request.acknowledgment.id),
    'Renewed continuation requires the latest current-revision/item/exclusivity renewal and a separate fresh acknowledgment.');
  }
  requireValue(state.enabled && state.mode === 'delivery' && [1, 2].includes(request.version) &&
    request.kind === 'exact-recovered-owner-continuation' && record && item?.phase === 'running' &&
    !(item.scopeAmendments ?? []).some(old => old.proof.version === 2 &&
      old.proof.scopeRevision > record.event.proof.scopeRevision) &&
    record.event.proof.coordinator === state.coordinator && currentHead === state.head &&
    !item.sourceWritesRetired && !supersededWriter(state, item.key) &&
    request.coordinator === state.coordinator && request.head === state.head &&
    request.sourceHead === record.event.proof.source.head &&
    request.owner === item.owner && request.scopeRevision === item.scopeRevision &&
    request.expectedItemDigest === digest(item) &&
    request.checkpointRevision === context.revision && Number.isSafeInteger(context.revision) &&
    request.grantDigest === digest(record) && equal(request.fence, fence) &&
    text(request.acknowledgment.id) && request.acknowledgment.id !== record.event.acknowledgment.id &&
    request.acknowledgment.owner === item.owner &&
    request.acknowledgment.requestDigest === recoveryContinuationDigest(request) &&
    request.acknowledgment.grantDigest === request.grantDigest &&
    equal(request.acknowledgment.fence, fence) && request.acknowledgment.workJoined === true &&
    request.acknowledgment.staleAuthorityRejected === true && text(request.acknowledgment.receipt),
  'Separate fresh exact-owner continuation must revalidate the current grant and source fence.');
  return { coverageVerified: true, blockers: item.blocker, sourceExecutionAuthorized: false };
}

function heldReservations(state, key) {
  if (state?.version === 10) {
    const p = lifecycleProjection(state);
    return p.reservations.filter(r => r.key !== key).map(r => {
      const grant = p.grants.find(g => g.id === r.grantId);
      return { key: r.key, owner: grant?.owner ?? null, scopes: r.scopes,
        scopeRevision: r.revision, sourceWritesRetired: grant?.status === 'revoked' };
    });
  }
  return [...state.items.filter(item => item.key !== key && holdsSlot(item))
    .map(item => ({ key: item.key, owner: item.owner, scopes: effectiveScopes(state, item),
      scopeRevision: item.scopeRevision ?? 0,
      sourceWritesRetired: item.sourceWritesRetired === true || supersededWriter(state, item.key) })),
  ...externalReservations(state), ...(state.supersessions ?? []).filter(({ event: { proof } }) =>
    proof.mode === 'activate-local' && proof.candidate.key !== key &&
    state.items.find(item => item.key === proof.candidate.key)?.phase !== 'complete').map(({ event }) => ({
    key: `supersession:${event.id}:${event.proof.candidate.key}`,
    owner: state.items.find(item => item.key === event.proof.candidate.key)?.owner ?? null,
    scopes: [...new Set(event.proof.holders.flatMap(holder => holder.paths))],
    scopeRevision: 0, sourceWritesRetired: false
  }))]
    .sort((a, b) => a.key.localeCompare(b.key));
}

function supersededWriter(state, key) {
  if (state?.version === 10) {
    const grants = lifecycleProjection(state).grants.filter(g => g.key === key);
    return grants.length > 0 && grants.every(g => g.status === 'revoked');
  }
  return (state.supersessions ?? []).some(({ event: { proof } }) =>
    proof.mode === 'activate-local' && proof.holders.some(holder =>
      holder.coordinator === proof.coordinator && holder.key === key));
}

function effectiveScopes(state, item) {
  if (state?.version === 10) return [...new Set(lifecycleProjection(state).reservations
    .filter(r => r.key === item.key).flatMap(r => r.scopes))];
  const transferred = (state.supersessions ?? []).flatMap(({ event: { proof } }) =>
    proof.mode === 'activate-local' ? proof.holders.filter(holder =>
      holder.coordinator === proof.coordinator && holder.key === item.key).flatMap(holder => holder.paths) : []);
  return item.scopes.filter(value => !transferred.includes(value));
}

function externalReservations(state) {
  const reservations = new Map();
  for (const { event: { proof } } of state.supersessions ?? []) {
    for (const snapshot of proof.externalCheckpoints) {
      for (const item of heldReservations(snapshot.state)) {
        const key = `${snapshot.state.coordinator}::${item.key}`;
        const previous = reservations.get(key);
        reservations.set(key, { ...item, key,
          scopes: [...new Set([...(previous?.scopes ?? []), ...item.scopes])] });
      }
    }
  }
  return [...reservations.values()];
}

function preparedCandidate(state, key) {
  if (state?.version === 10) {
    const p = lifecycleProjection(state);
    return p.reservations.some(r => !p.works.some(w => w.key === r.key) &&
      overlaps(r.scopes, p.grants.filter(g => g.key === key && lifecycleLive(g)).flatMap(g => g.scopes)));
  }
  return (state.supersessions ?? []).some(({ event: { proof } }) =>
    proof.mode === 'prepare-external' && proof.candidate.key === key);
}

function supersessionParticipant(state, key) {
  return (state.supersessions ?? []).some(({ event: { proof } }) =>
    proof.candidate.key === key || proof.mode === 'activate-local' &&
    proof.holders.some(holder => holder.coordinator === proof.coordinator && holder.key === key));
}

function sourceIdentity(item) {
  return { ...Object.fromEntries(['key', 'outcome', 'pool', 'kind', 'head', 'scopes', 'evidence']
    .map(name => [name, item[name]])), scopeRevision: item.scopeRevision ?? 0,
  scopeHistory: item.scopeHistory ?? [], scopeAmendments: item.scopeAmendments ?? [],
  ...(item.scopeRecoveries?.length ? { scopeRecoveries: item.scopeRecoveries } : {}),
  admissionHistory: item.admissionHistory ?? [], sourceWritesRetired: item.sourceWritesRetired ?? false };
}

function supersessionAcknowledgmentDigest(proof, holder) {
  return digest({ proof: { ...proof, holders: proof.holders.map(value => {
    const { acknowledgment, ...body } = value;
    return body;
  }) }, holder: { coordinator: holder.coordinator, key: holder.key, owner: holder.owner,
    itemDigest: holder.itemDigest } });
}

function validateSupersessionEvent(event) {
  fields(event, 'id type proof authorization');
  const p = event.proof;
  const fenced = p?.version === 2;
  fields(p, `version repository coordinator checkpointRevision head mode authority exactFiles candidate expectedItemDigest manifestComplete localReservations externalCheckpoints holders${
    fenced ? ' expectedStateDigest effectiveReservations beneficiaryManifest' : ''}`);
  fields(event.authorization, `humanAuthorized receipt proofDigest${
    fenced ? ' kind id actor decision' : ''}`);
  requireValue(text(event.id) && event.type === 'supersede-write-reservations' &&
    [1, 2].includes(p.version) && p.repository === 'roryprimrose/Kora' && text(p.coordinator) &&
    Number.isSafeInteger(p.checkpointRevision) && p.checkpointRevision > 0 && head(p.head) &&
    ['activate-local', 'prepare-external'].includes(p.mode) &&
    p.manifestComplete === true && p.candidate && unownedCandidate(p.candidate) &&
    p.candidate.blocker === null && p.candidate.head === p.head &&
    p.expectedItemDigest === digest(p.candidate),
  'Complete exact supersession candidate/repository/source/revision proof required.');
  canonicalPaths(p.candidate.scopes, true);
  requireValue(event.authorization.humanAuthorized === true && text(event.authorization.receipt) &&
    event.authorization.proofDigest === digest(p), 'Exact whole-proof human authorization required.');
  if (fenced) {
    const h = event.authorization;
    requireValue(p.mode === 'activate-local' && Array.isArray(p.effectiveReservations) &&
      h.kind === 'explicit-human-reservation-handoff-decision' && text(h.id) && text(h.actor) &&
      h.decision === 'retire-prior-writers-for-exact-beneficiary',
    'Fenced handoff requires a new exact human decision in one controlling local ledger only.');
    fields(p.beneficiaryManifest, 'complete actual planned');
    requireValue(p.beneficiaryManifest.complete === true && equal(p.beneficiaryManifest.actual, []) &&
      equal(p.beneficiaryManifest.planned, p.candidate.scopes),
    'Complete current-source beneficiary manifest must name the exact future union with no prior writes.');
  }
  fields(p.authority, 'singleLedger receipt');
  fields(p.exactFiles, 'complete paths receipt');
  requireValue(p.authority.singleLedger === (p.mode === 'activate-local') && text(p.authority.receipt) &&
    p.exactFiles.complete === true && text(p.exactFiles.receipt),
  'Verified controlling single-ledger authority and exact file-kind evidence required.');
  canonicalPaths(p.exactFiles.paths, true);
  requireValue(Array.isArray(p.localReservations) && Array.isArray(p.externalCheckpoints) &&
    Array.isArray(p.holders) && p.holders.length > 0, 'Complete supersession holder inventory required.');
  requireValue(p.mode !== 'activate-local' || p.externalCheckpoints.length === 0,
    'External authority cannot activate through a local snapshot or two checkpoint writes.');
  requireValue(p.mode !== 'prepare-external' || p.externalCheckpoints.length > 0,
    'External preparation requires a concrete complete checkpoint snapshot.');
  const ledgers = new Set([p.coordinator]);
  const inventory = p.localReservations.map(item => ({ coordinator: p.coordinator, item,
  scopes: fenced ? p.effectiveReservations.find(held => held.key === item.key)?.scopes : item.scopes }));
  for (const snapshot of p.externalCheckpoints) {
    fields(snapshot, 'repository revision state stateDigest receipt');
    requireValue(snapshot.repository === p.repository && Number.isSafeInteger(snapshot.revision) &&
      snapshot.revision > 0 && text(snapshot.receipt) && snapshot.stateDigest === digest(snapshot.state),
    'Complete external checkpoint snapshot/digest required.');
    validate(snapshot.state);
    requireValue(head(snapshot.state.head) && !ledgers.has(snapshot.state.coordinator) &&
      (snapshot.state.supersessions ?? []).every(record => record.event.proof.externalCheckpoints.length === 0),
    'External snapshot must identify a distinct single ledger, not nested authority imports.');
    ledgers.add(snapshot.state.coordinator);
    inventory.push(...snapshot.state.items.filter(holdsSlot).map(item =>
      ({ coordinator: snapshot.state.coordinator, item, scopes: effectiveScopes(snapshot.state, item) })));
  }
  const identities = new Set();
  const expected = [];
  for (const entry of inventory) {
    const { coordinator, item, scopes } = entry;
    requireValue(item && holdsSlot(item) && text(item.key) && head(item.head),
      'Every supersession snapshot reservation must be known.');
    canonicalPaths(item.scopes, true);
    if (fenced) canonicalPaths(scopes);
    const identity = digest([coordinator, item.key]);
    requireValue(!identities.has(identity), 'Duplicate supersession snapshot holder.');
    identities.add(identity);
    if (!overlaps(p.candidate.scopes, scopes)) continue;
    requireValue(text(item.owner) && (fenced ? item.phase === 'published' : ['running', 'published'].includes(item.phase)),
      'Every overlapping supersession holder must be attached and known.');
    const paths = p.candidate.scopes.filter(value => scopes.includes(value));
    requireValue(paths.length > 0 && scopes.filter(value => overlaps([value], p.candidate.scopes))
      .every(value => paths.includes(value)) &&
      paths.every(value => !scopes.some(other => other !== value && overlaps([value], [other]))) &&
      p.candidate.scopes.filter(value => overlaps([value], scopes)).every(value => paths.includes(value)),
    'Supersession permits exact-path equality only, never ancestor subtraction.');
    expected.push({ coordinator, key: item.key, owner: item.owner, itemDigest: digest(item),
      scopeRevision: item.scopeRevision ?? 0, paths });
  }
  requireValue(expected.length === p.holders.length, 'Every exact prior holder must be accounted for.');
  requireValue(equal(p.exactFiles.paths, [...new Set(expected.flatMap(holder => holder.paths))]),
    'Exact file-kind evidence must cover every transferred path, not directories.');
  const acknowledgments = new Set();
  for (const [index, holder] of p.holders.entries()) {
    fields(holder, `coordinator key owner itemDigest scopeRevision paths manifest preservation stopped acknowledgment${
      fenced ? ' source reconciliation' : ''}`);
    requireValue(equal(Object.fromEntries(Object.keys(expected[index]).map(key => [key, holder[key]])),
      expected[index]), 'Supersession exact holder identity/digest/revision/paths mismatch.');
    const item = inventory.find(entry => entry.coordinator === holder.coordinator &&
      entry.item.key === holder.key).item;
    if (p.mode === 'prepare-external' && holder.stopped === null && holder.acknowledgment === null) {
      requireValue(holder.manifest === null && holder.preservation === null,
        'Unknown external evidence must remain explicitly unknown.');
      continue;
    }
    fields(holder.stopped, fenced ? 'writingStopped commandsRunning workJoined receipt'
      : 'writingStopped pendingInstructions sourceInstructionsCleared receipt');
    requireValue(holder.stopped.writingStopped === true && (fenced
      ? holder.stopped.commandsRunning === false && holder.stopped.workJoined === true
      : holder.stopped.pendingInstructions === 0 && holder.stopped.sourceInstructionsCleared === true) &&
      text(holder.stopped.receipt),
    'Fresh stopped-work and complete source-instruction reconciliation required.');
    validateManifest({ manifest: holder.manifest, preservation: holder.preservation,
      phase: item.phase, publicationHead: item.publication?.remoteHead ?? null, toScopes: item.scopes });
    requireValue(holder.preservation.unpreserved.length === 0,
      'Supersession cannot transfer unpreserved actual writes.');
    requireValue([...(item.scopeHistory ?? []), ...(item.scopeAmendments ?? [])].every(record =>
      record.proof.manifest.actual.every(value => holder.manifest.actual.includes(value)) &&
      [...record.proof.manifest.actual, ...record.proof.manifest.planned].every(value =>
        [...holder.manifest.actual, ...holder.manifest.planned].includes(value))),
    'Supersession preservation cannot omit historical actual/planned writes.');
    fields(holder.acknowledgment, `kind id receipt proofDigest${
      fenced ? ' owner workJoined staleAuthorityRejected fence' : ''}`);
    const acknowledgment = holder.acknowledgment;
    requireValue(['owner', 'revocation'].includes(acknowledgment.kind) && text(acknowledgment.id) &&
      !acknowledgments.has(acknowledgment.id) && text(acknowledgment.receipt) &&
      acknowledgment.proofDigest === supersessionAcknowledgmentDigest(p, holder),
    'Fresh exact-holder whole-proof acknowledgment or explicit revocation receipt required.');
    acknowledgments.add(acknowledgment.id);
    if (fenced) {
      fields(holder.source, 'owner worktree branch head receipt');
      fields(holder.reconciliation, 'kind pendingInstructions processingInstructions fence receipt');
      const source = holder.source, r = holder.reconciliation, f = r.fence;
      fields(f, 'epoch nonce revokesEarlierSourceAuthority established receipt');
      requireValue(source.owner === holder.owner && typeof source.worktree === 'string' &&
        path.win32.isAbsolute(source.worktree) && path.win32.normalize(source.worktree) === source.worktree &&
        text(source.branch) && head(source.head) && text(source.receipt) &&
        (source.head === item.head || holder.preservation.commits.some(commit => commit.head === source.head)) &&
        r.kind === 'human-approved-source-fence' && r.pendingInstructions === 'UNKNOWN' &&
        r.processingInstructions === 'UNKNOWN' && text(r.receipt) &&
        Number.isSafeInteger(f.epoch) && f.epoch > 0 && text(f.nonce) &&
        f.revokesEarlierSourceAuthority === true && f.established === true && text(f.receipt) &&
        acknowledgment.kind === 'owner' && acknowledgment.owner === holder.owner &&
        acknowledgment.workJoined === true && acknowledgment.staleAuthorityRejected === true &&
        equal(acknowledgment.fence, f),
      'UNKNOWN handoff requires exact preserved source, joined cooperative owner and established fresh revocation fence.');
    }
  }
}

function validateFencedSupersession(record, state) {
  const { event, before, hostEvidence: e } = record;
  const p = event.proof, h = event.authorization;
  validate(before);
  requireValue(state.version >= 9 && digest(before) === p.expectedStateDigest &&
    before.enabled && before.mode === 'delivery' && before.coordinator === p.coordinator && before.head === p.head &&
    equal(before.items.find(item => item.key === p.candidate.key), p.candidate) &&
    equal(before.items.filter(holdsSlot), p.localReservations) &&
    equal(heldReservations(before, p.candidate.key), p.effectiveReservations) &&
    p.effectiveReservations.filter(held => overlaps(held.scopes, p.candidate.scopes)).every(held =>
      p.holders.some(holder => holder.key === held.key && holder.owner === held.owner)) &&
    equal(record.commands, []) && !before.seen.includes(event.id) &&
    equal(before.seen, state.seen.slice(0, before.seen.length)),
  'Fenced handoff original complete state, effective guards, history or no-action binding changed.');
  for (const name of ['refinements', 'amendments', 'readmissions', 'supersessions', 'recoveries', 'recoveryRenewals']) {
    requireValue(equal(before[name] ?? [], (state[name] ?? []).slice(0, (before[name] ?? []).length)),
      'Fenced handoff cannot omit or rewrite prior immutable histories.');
  }
  const priorTransactions = [...(before.recoveries ?? []).map(old => old.event),
    ...(before.supersessions ?? []).map(old => old.event)];
  const priorAmendments = before.items.flatMap(item => item.scopeAmendments ?? []);
  requireValue(![...priorTransactions, ...priorAmendments].some(old => old.authorization?.id === h.id),
    'Fenced handoff human decision replay.');
  const priorFences = [...priorAmendments.filter(old => old.proof.version === 2).map(old =>
    ({ owner: old.proof.owner, source: old.proof.source, fence: old.proof.reconciliation.fence })),
  ...(before.recoveries ?? []).filter(old => old.event.proof.reconciliation.fence).map(old =>
    ({ owner: old.event.proof.owner, source: old.event.proof.source, fence: old.event.proof.reconciliation.fence })),
  ...(before.supersessions ?? []).filter(old => old.event.proof.version === 2)
    .flatMap(old => old.event.proof.holders.map(holder =>
      ({ owner: holder.owner, source: holder.source, fence: holder.reconciliation.fence })))];
  const nonces = new Set(priorFences.map(old => old.fence.nonce));
  const oldAcknowledgments = new Set([
    ...before.items.flatMap(item => [...(item.scopeHistory ?? []), ...(item.scopeAmendments ?? [])]
      .map(old => old.acknowledgment.id)),
    ...priorTransactions.flatMap(old => old.type === 'supersede-write-reservations'
      ? old.proof.holders.map(holder => holder.acknowledgment?.id) : [old.acknowledgment.id]),
    ...(before.recoveryRenewals ?? []).map(old => old.event.acknowledgment.id)]);
  fields(e, 'kind coordinator checkpointRevision proofDigest humanDecisionId humanDecisionDigest humanAuthorizationVerified ledgerAuthorityVerified beneficiarySourceVerified holders receipt');
  requireValue(e.kind === 'host-verified-reservation-handoff' && e.coordinator === p.coordinator &&
    e.checkpointRevision === p.checkpointRevision && e.proofDigest === digest(p) &&
    e.humanDecisionId === h.id && e.humanDecisionDigest === digest(h) &&
    e.humanAuthorizationVerified === true && e.ledgerAuthorityVerified === true &&
    e.beneficiarySourceVerified === true && text(e.receipt) &&
    Array.isArray(e.holders) && e.holders.length === p.holders.length,
  'Independent current host readback of human decision, local authority and beneficiary source required.');
  for (const [index, holder] of p.holders.entries()) {
    const f = holder.reconciliation.fence, observed = e.holders[index];
    requireValue(!nonces.has(f.nonce) && !oldAcknowledgments.has(holder.acknowledgment.id) &&
      priorFences.filter(old => old.owner === holder.owner).every(old =>
        f.epoch > old.fence.epoch && ['owner', 'worktree', 'branch'].every(name =>
          holder.source[name] === old.source[name])),
    'Fresh same-source holder fence epoch/nonce and whole-proof acknowledgment required.');
    nonces.add(f.nonce);
    const historical = (before.recoveries ?? []).filter(old => old.event.proof.key === holder.key);
    requireValue(historical.every(old => old.event.proof.manifest.actual.every(value =>
      holder.manifest.actual.includes(value)) && old.event.proof.manifest.planned.every(value =>
      [...holder.manifest.actual, ...holder.manifest.planned].includes(value))),
    'Fenced handoff must preserve complete recovery actual/planned and restored-path history.');
    fields(observed, 'coordinator key owner itemDigest ownerResponseId sourceDigest preservationDigest fenceDigest sourceIdentityVerified preservationVerified sourceStoppedVerified commandsJoinedVerified instructionAuthorityVerified receipt');
    requireValue(['coordinator', 'key', 'owner', 'itemDigest'].every(name => observed[name] === holder[name]) &&
      observed.ownerResponseId === holder.acknowledgment.id && observed.sourceDigest === digest(holder.source) &&
      observed.preservationDigest === digest({ manifest: holder.manifest, preservation: holder.preservation }) &&
      observed.fenceDigest === digest(f) && observed.sourceIdentityVerified === true &&
      observed.preservationVerified === true && observed.sourceStoppedVerified === true &&
      observed.commandsJoinedVerified === true && observed.instructionAuthorityVerified === true && text(observed.receipt),
    'Independent host readback of every exact holder/source/preservation/stopped/joined/fenced response required.');
  }
}

function validateSupersessions(state) {
  const markers = state.seen.filter(id => id.startsWith(SUPERSESSION_MARKER));
  if (state.version < 5) {
    requireValue(!Object.hasOwn(state, 'supersessions') && markers.length === 0,
      'Supersession schema downgrade or omission.');
    return;
  }
  requireValue(Array.isArray(state.supersessions), 'Supersession history missing.');
  const events = new Set();
  const candidates = new Set();
  const acknowledgments = new Set(state.items.flatMap(item =>
    [...(item.scopeHistory ?? []), ...(item.scopeAmendments ?? [])].map(record => record.acknowledgment.id)));
  for (const record of state.supersessions) {
    fields(record, `event commands${record.event?.proof?.version === 2 ? ' before hostEvidence' : ''}`);
    requireValue(Array.isArray(record.commands), 'Supersession action binding missing.');
    validateSupersessionEvent(record.event);
    if (record.event.proof.version === 2) validateFencedSupersession(record, state);
    const { event } = record;
    const p = event.proof;
    requireValue(!events.has(event.id) && state.seen.includes(event.id) &&
      !candidates.has(p.candidate.key), 'Duplicate or missing supersession event/candidate history.');
    events.add(event.id);
    candidates.add(p.candidate.key);
    const candidate = state.items.find(item => item.key === p.candidate.key);
    requireValue(candidate && equal(sourceIdentity(candidate), sourceIdentity(p.candidate)) &&
      (p.candidate.pr === null || p.candidate.pr === candidate.pr),
    'Supersession beneficiary identity/scopes/history changed without fresh authority.');
    requireValue(p.mode !== 'prepare-external' || unownedCandidate(candidate),
      'A prepared external proposal cannot reserve, attach or launch its candidate.');
    for (const holder of p.holders) {
      const acknowledgment = holder.acknowledgment;
      if (acknowledgment) {
        requireValue(!acknowledgments.has(acknowledgment.id), 'Supersession acknowledgment replay.');
        acknowledgments.add(acknowledgment.id);
      }
      if (p.mode !== 'activate-local') continue;
      const original = p.localReservations.find(item => item.key === holder.key);
      const current = state.items.find(item => item.key === holder.key);
      requireValue(current && current.owner === original.owner &&
        equal(sourceIdentity(current), sourceIdentity(original)) &&
        (original.publication === null || equal(current.publication, original.publication) &&
          current.pr === original.pr),
      'Superseded holder scopes/publication/history/retirement must remain immutable.');
    }
  }
  requireValue(equal(markers.slice().sort(), state.supersessions.map(record =>
    `${SUPERSESSION_MARKER}${digest(record)}`).sort()),
  'Supersession complete history/event digest binding is inconsistent.');
}

function validateAmendment(record, item, previous) {
  const p = record.proof;
  const fenced = p?.version === 2;
  fields(record, `eventId proof acknowledgment${fenced ? ' authorization hostEvidence before fromVersion commands' : ''}`);
  const a = record.acknowledgment;
  fields(p, `version owner key reservation scopeRevision fromScopes additionalScopes toScopes phase sourceWritesRetired manifest preservation quiescence unionFrozen exclusivity receipt${fenced ? ' repository coordinator checkpointRevision head source expectedItemDigest reconciliation' : ''}`);
  fields(a, `id receipt owner key scopeRevision proofDigest${fenced ? ' workJoined staleAuthorityRejected fence' : ''}`);
  fields(p.reservation, 'outcome kind pool head pr');
  requireValue(text(record.eventId) && [1, 2].includes(p.version) && p.owner === item.owner &&
    p.key === item.key && p.phase === 'running' && p.sourceWritesRetired === false &&
    Number.isSafeInteger(p.scopeRevision) && p.scopeRevision >= 0 &&
    ['outcome', 'kind', 'pool', 'head'].every(key => p.reservation[key] === item[key]) &&
    (p.reservation.pr === item.pr || p.reservation.pr === null) && text(p.receipt),
  'Exact mutable running owner/reservation proof required.');
  canonicalPaths(p.fromScopes, true);
  canonicalPaths(p.additionalScopes, true);
  canonicalPaths(p.toScopes, true);
  requireValue(equal(p.toScopes, [...p.fromScopes, ...p.additionalScopes]) &&
    p.additionalScopes.every((value, index) => !overlaps([value],
      [...p.fromScopes, ...p.additionalScopes.slice(0, index)])),
  'Extension needs exact nonempty disjoint additions and complete original scope union.');
  fields(p.quiescence, fenced ? 'paused commandsRunning workJoined receipt' : 'paused pendingInstructions receipt');
  requireValue(p.quiescence.paused === true &&
    (fenced ? p.quiescence.commandsRunning === false && p.quiescence.workJoined === true : p.quiescence.pendingInstructions === 0) &&
    text(p.quiescence.receipt) && p.unionFrozen === true,
  'Temporary source pause, joined work or verified pending-zero, and frozen union proof required.');
  fields(p.exclusivity, 'complete reservations receipt');
  requireValue(p.exclusivity.complete === true && text(p.exclusivity.receipt) &&
    Array.isArray(p.exclusivity.reservations), 'Complete exclusive ownership proof required.');
  const keys = new Set([item.key]);
  for (const reservation of p.exclusivity.reservations) {
    fields(reservation, 'key owner scopes scopeRevision sourceWritesRetired');
    requireValue(text(reservation.key) && !keys.has(reservation.key) &&
      (reservation.owner === null || text(reservation.owner)) &&
      Number.isSafeInteger(reservation.scopeRevision) && reservation.scopeRevision >= 0 &&
      typeof reservation.sourceWritesRetired === 'boolean', 'Malformed exclusive reservation.');
    keys.add(reservation.key);
    canonicalPaths(reservation.scopes);
    requireValue(!overlaps(p.toScopes, reservation.scopes), 'Extension conflicts with held owner scopes.');
  }
  validateManifest(p);
  requireValue(p.manifest.actual.every(value => covers(p.fromScopes, value)),
    'Actual writes outside original authority cannot be legalized by extension.');
  requireValue(p.additionalScopes.every(value => p.manifest.planned.some(planned => covers([value], planned))),
    'Each additional scope needs an explicit planned write.');
  if (previous) {
    requireValue(equal(p.fromScopes, previous.proof.toScopes) &&
      previous.proof.manifest.actual.every(value => p.manifest.actual.includes(value)) &&
      [...previous.proof.manifest.actual, ...previous.proof.manifest.planned].every(value =>
        [...p.manifest.actual, ...p.manifest.planned].includes(value)),
    'Amendment history loses original scopes or write manifest.');
  }
  validateAcknowledgment(a, p);
  if (fenced) validateFencedAmendment(record, item);
}

function validateFencedAmendment(record, item) {
  const { proof: p, acknowledgment: a, authorization: h, hostEvidence: e, before, fromVersion } = record;
  fields(p.source, 'owner worktree branch head receipt');
  fields(p.reconciliation, 'kind pendingInstructions processingInstructions fence receipt');
  const f = p.reconciliation.fence;
  fields(f, 'epoch nonce revokesEarlierSourceAuthority established receipt');
  fields(h, 'kind id actor decision subjectDigest receipt');
  fields(e, 'kind coordinator checkpointRevision proofDigest ownerResponseId humanDecisionId humanDecisionDigest sourceIdentityVerified sourcePauseVerified commandsJoinedVerified instructionAuthorityVerified receipt');
  fields(before, `key outcome pool kind head scopes evidence pr owner phase blocker publication${
    fromVersion >= 2 ? ' scopeRevision scopeHistory sourceWritesRetired' : ''}${
    fromVersion >= 3 ? ' scopeAmendments' : ''}${fromVersion >= 4 ? ' admissionHistory' : ''}${
    fromVersion >= 6 ? ' scopeRecoveries' : ''}`);
  requireValue(p.repository === 'roryprimrose/Kora' && text(p.coordinator) && head(p.head) &&
    Number.isSafeInteger(p.checkpointRevision) && p.checkpointRevision > 0 &&
    p.source.owner === p.owner && path.win32.isAbsolute(p.source.worktree) &&
    path.win32.normalize(p.source.worktree) === p.source.worktree &&
    text(p.source.branch) && head(p.source.head) && text(p.source.receipt) &&
    (p.source.head === p.reservation.head || p.preservation.commits.some(commit => commit.head === p.source.head)),
  'Fenced extension requires exact current repository, checkpoint and same-owner source identity.');
  requireValue(p.reconciliation.kind === 'human-approved-source-fence' &&
    p.reconciliation.pendingInstructions === 'UNKNOWN' && p.reconciliation.processingInstructions === 'UNKNOWN' &&
    text(p.reconciliation.receipt) && Number.isSafeInteger(f.epoch) && f.epoch > 0 &&
    text(f.nonce) && f.revokesEarlierSourceAuthority === true && f.established === true && text(f.receipt) &&
    a.workJoined === true && a.staleAuthorityRejected === true && equal(a.fence, f),
  'UNKNOWN requires a fresh established human-approved source fence and joined exact-owner acknowledgment.');
  requireValue(h.kind === 'explicit-human-scope-extension-decision' && text(h.id) && text(h.actor) &&
    h.decision === 'prospective-coverage-only' && text(h.receipt) &&
    h.subjectDigest === digest({ proof: p, acknowledgment: a }),
  'Current explicit human extension decision must bind the entire final proof and owner acknowledgment.');
  requireValue(e.kind === 'host-verified-fenced-scope-extension' && e.coordinator === p.coordinator &&
    e.checkpointRevision === p.checkpointRevision && e.proofDigest === digest(p) &&
    e.ownerResponseId === a.id && e.humanDecisionId === h.id && e.humanDecisionDigest === digest(h) &&
    e.sourceIdentityVerified === true && e.sourcePauseVerified === true &&
    e.commandsJoinedVerified === true && e.instructionAuthorityVerified === true && text(e.receipt),
  'Independent host readback of current human authority, owner, source pause/join and fence required.');
  requireValue([1, 2, 3, 4, 5, 6, 7, 8, 9].includes(fromVersion) && digest(before) === p.expectedItemDigest &&
    before.key === p.key && before.owner === p.owner && before.phase === 'running' &&
    !before.sourceWritesRetired && before.publication === null &&
    (before.scopeRevision ?? 0) === p.scopeRevision && equal(before.scopes, p.fromScopes) &&
    equal(p.reservation, Object.fromEntries(['outcome', 'kind', 'pool', 'head', 'pr'].map(name => [name, before[name]]))) &&
    ['outcome', 'kind', 'pool', 'head', 'evidence'].every(name => before[name] === item[name]) &&
    (before.pr === null || before.pr === item.pr) && equal(record.commands, []),
  'Fenced extension original item identity/history or no-action binding changed.');
  for (const [minimum, name] of [[2, 'scopeHistory'], [3, 'scopeAmendments'], [4, 'admissionHistory'], [6, 'scopeRecoveries']]) {
    requireValue(Object.hasOwn(before, name) === (fromVersion >= minimum), 'Fenced extension original schema changed.');
    const expected = name === 'admissionHistory' ? item[name] ?? [] : name === 'scopeRecoveries'
      ? (item.scopeRecoveries ?? []).slice(0, (before.scopeRecoveries ?? []).length)
      : (item[name] ?? []).filter(old => old.proof.scopeRevision < p.scopeRevision);
    requireValue(equal(before[name] ?? [], expected),
      'Fenced extension omitted or altered prior item history.');
  }
  requireValue((before.scopeHistory?.length ?? 0) + (before.scopeAmendments?.length ?? 0) +
    (before.scopeRecoveries?.length ?? 0) === p.scopeRevision, 'Fenced extension loses prior scope revisions.');
}

function validateExtensionFences(state) {
  const extensions = state.items.flatMap(item => (item.scopeAmendments ?? []).map(record => ({
    id: record.eventId, proof: record.proof, authorization: record.authorization, record
  })));
  const fenced = extensions.filter(value => value.proof.version === 2);
  requireValue(!fenced.length || state.version >= 8, 'Fenced mutable extension schema downgrade.');
  const timeline = [...extensions, ...(state.recoveries ?? []).map(record => ({
    id: record.event.id, proof: record.event.proof, authorization: record.event.authorization
  })), ...(state.supersessions ?? []).filter(record => record.event.proof.version === 2).flatMap(record =>
    record.event.proof.holders.map((holder, index) => ({ id: record.event.id,
      proof: { owner: holder.owner, source: holder.source, reconciliation: holder.reconciliation },
      authorization: index === 0 ? record.event.authorization : null
    })))].sort((a, b) => state.seen.indexOf(a.id) - state.seen.indexOf(b.id));
  const owners = new Map(), nonces = new Set(), decisions = new Set();
  for (const entry of timeline) {
    const p = entry.proof;
    const old = owners.get(p.owner);
    if (entry.authorization) {
      requireValue(!decisions.has(entry.authorization.id), 'Human extension/recovery decision replay.');
      decisions.add(entry.authorization.id);
    }
    if (old?.mutable) requireValue(p.reconciliation?.fence,
      'An accepted mutable source-authority fence cannot be dropped by later pending-zero claims.');
    if (p.reconciliation?.fence) {
      const f = p.reconciliation.fence;
      requireValue(!nonces.has(f.nonce) && (!old || f.epoch > old.fence.epoch),
        'Stale or replayed source-authority fence epoch/nonce.');
      if (old && (old.mutable || entry.record)) requireValue(['owner', 'worktree', 'branch'].every(name =>
        p.source[name] === old.source[name]), 'Fenced extension source identity drift.');
      nonces.add(f.nonce);
      owners.set(p.owner, { fence: f, source: p.source, mutable: !!entry.record || old?.mutable });
    }
  }
}

function validateScopeExtensionContinuation(state, request, context = {}) {
  validate(state);
  fields(request, 'version kind coordinator head source checkpointRevision key owner expectedItemDigest scopeRevision coverageDigest fence acknowledgment');
  fields(request.source, 'owner worktree branch head');
  fields(request.acknowledgment, 'id owner requestDigest coverageDigest fence workJoined staleAuthorityRejected receipt');
  const item = state.items.find(value => value.key === request.key);
  const record = item?.scopeAmendments?.at(-1);
  const p = record?.proof;
  const a = request.acknowledgment;
  requireValue(state.enabled && state.mode === 'delivery' && request.version === 1 &&
    request.kind === 'exact-extended-owner-continuation' && p?.version === 2 &&
    p.coordinator === state.coordinator && p.head === state.head && item.phase === 'running' &&
    !item.sourceWritesRetired && !supersededWriter(state, item.key) &&
    request.coordinator === state.coordinator && request.head === state.head &&
    Number.isSafeInteger(context.revision) && context.revision > p.checkpointRevision &&
    request.checkpointRevision === context.revision && request.owner === item.owner &&
    request.expectedItemDigest === digest(item) && request.scopeRevision === item.scopeRevision &&
    item.scopeRevision === p.scopeRevision + 1 && equal(item.scopes, p.toScopes) &&
    ['owner', 'worktree', 'branch', 'head'].every(name => request.source[name] === p.source[name]) &&
    request.coverageDigest === digest(record) && equal(request.fence, p.reconciliation.fence) &&
    equal(p.exclusivity.reservations, heldReservations(state, item.key)) &&
    text(a.id) && text(a.receipt) && a.owner === item.owner && a.coverageDigest === request.coverageDigest &&
    a.requestDigest === recoveryContinuationDigest(request) && equal(a.fence, request.fence) &&
    a.workJoined === true && a.staleAuthorityRejected === true &&
    !state.items.some(value => [...(value.scopeHistory ?? []), ...(value.scopeAmendments ?? [])]
      .some(old => old.acknowledgment.id === a.id)) &&
    !(state.recoveries ?? []).some(old => old.event.acknowledgment.id === a.id) &&
    !(state.recoveryRenewals ?? []).some(old => old.event.acknowledgment.id === a.id),
  'Exact current fenced extension, same source/item/guards and separate fresh continuation acknowledgment required.');
  return { coverageVerified: true, blockers: item.blocker, sourceExecutionAuthorized: false };
}

function validateRefinements(state) {
  const markers = state.seen.filter(id => id.startsWith(REFINEMENT_MARKER));
  if (state.version === 1) {
    requireValue(!Object.hasOwn(state, 'refinements') && markers.length === 0 &&
      state.items.every(item => !['scopeRevision', 'scopeHistory', 'sourceWritesRetired']
        .some(key => Object.hasOwn(item, key))), 'Refinement schema downgrade or omission.');
    return;
  }
  requireValue(Array.isArray(state.refinements), 'Refinement index missing.');
  const records = [];
  const acknowledgments = new Set();
  for (const item of state.items) {
    const amendments = state.version >= 3 ? item.scopeAmendments : [];
    const recoveries = recoveryRecords(state, item.key).map(record => ({
      proof: record.event.proof, acknowledgment: record.event.acknowledgment
    }));
    requireValue(Array.isArray(amendments), 'Mutable amendment history missing.');
    requireValue(Number.isSafeInteger(item.scopeRevision) && item.scopeRevision >= 0 &&
      Array.isArray(item.scopeHistory) &&
      item.scopeRevision === item.scopeHistory.length + amendments.length + recoveries.length &&
      item.sourceWritesRetired === (item.scopeHistory.length > 0), 'Refinement history/retirement missing.');
    let previous = [...amendments, ...recoveries].sort((a, b) =>
      a.proof.scopeRevision - b.proof.scopeRevision).at(-1) ?? null;
    for (const [revision, record] of item.scopeHistory.entries()) {
      validateRefinement(record, item, previous);
      requireValue(record.proof.scopeRevision === revision + amendments.length + recoveries.length &&
        state.seen.includes(record.eventId) && !acknowledgments.has(record.acknowledgment.id),
      'Refinement revision, event or acknowledgment replay mismatch.');
      acknowledgments.add(record.acknowledgment.id);
      records.push({ key: item.key, eventId: record.eventId, digest: digest(record) });
      previous = record;
    }
    requireValue(!previous || equal(item.scopes, previous.proof.toScopes),
      'Current scopes disagree with refinement history.');
  }
  requireValue(new Set(records.map(record => record.eventId)).size === records.length &&
    equal([...records].sort((a, b) => a.eventId.localeCompare(b.eventId)),
      [...state.refinements].sort((a, b) => a.eventId.localeCompare(b.eventId))) &&
    equal(markers.slice().sort(), records.map(record => `${REFINEMENT_MARKER}${record.digest}`).sort()),
  'Refinement index/history/event binding missing or inconsistent.');
}

function validateAmendments(state) {
  const markers = state.seen.filter(id => id.startsWith(AMENDMENT_MARKER));
  if (state.version < 3) {
    requireValue(!Object.hasOwn(state, 'amendments') && markers.length === 0 &&
      state.items.every(item => !Object.hasOwn(item, 'scopeAmendments')),
    'Mutable amendment schema downgrade or omission.');
    return;
  }
  requireValue(Array.isArray(state.amendments), 'Mutable amendment index missing.');
  const records = [];
  const acknowledgments = new Set(state.items.flatMap(item =>
    item.scopeHistory.map(record => record.acknowledgment.id)));
  for (const item of state.items) {
    requireValue(Array.isArray(item.scopeAmendments), 'Mutable amendment history missing.');
    let previous = null;
    const timeline = [...item.scopeAmendments, ...recoveryRecords(state, item.key).map(record => ({
      eventId: record.event.id, proof: record.event.proof, acknowledgment: record.event.acknowledgment,
      recovery: true
    }))].sort((a, b) => a.proof.scopeRevision - b.proof.scopeRevision);
    for (const [revision, record] of timeline.entries()) {
      if (!record.recovery) validateAmendment(record, item, previous);
      if (previous && record.recovery) {
        requireValue(equal(record.proof.fromScopes, previous.proof.toScopes) &&
          previous.proof.manifest.actual.every(value => record.proof.manifest.actual.includes(value)) &&
          previous.proof.manifest.planned.every(value =>
            [...record.proof.manifest.actual, ...record.proof.manifest.planned].includes(value)),
        'Recovery cannot omit earlier actual/restored or planned writes.');
      }
      requireValue(record.proof.scopeRevision === revision && state.seen.includes(record.eventId) &&
        !acknowledgments.has(record.acknowledgment.id), 'Mutable amendment revision/event/ack replay mismatch.');
      acknowledgments.add(record.acknowledgment.id);
      if (!record.recovery) records.push({ key: item.key, eventId: record.eventId, digest: digest(record) });
      previous = record;
    }
    requireValue(!previous || item.scopeHistory.length > 0 || equal(item.scopes, previous.proof.toScopes),
      'Current scopes disagree with mutable amendment history.');
  }
  requireValue(new Set(records.map(record => record.eventId)).size === records.length &&
    equal([...records].sort((a, b) => a.eventId.localeCompare(b.eventId)),
      [...state.amendments].sort((a, b) => a.eventId.localeCompare(b.eventId))) &&
    equal(markers.slice().sort(), records.map(record => `${AMENDMENT_MARKER}${record.digest}`).sort()),
  'Mutable amendment index/history/event binding missing or inconsistent.');
}

function unownedCandidate(item) {
  return item.phase === 'candidate' && item.owner === null && item.publication === null &&
    !Object.hasOwn(item, 'cleanup') && (item.scopeRevision ?? 0) === 0 &&
    (item.sourceWritesRetired ?? false) === false &&
    (item.scopeHistory ?? []).length === 0 && (item.scopeAmendments ?? []).length === 0;
}

function validateReadmissionEvent(event) {
  fields(event, 'id type owner pass key expectedItemDigest head scopes evidence manifestComplete');
  requireValue(text(event.id) && event.type === 'revalidate-candidate' && text(event.owner) &&
    text(event.key) && Number.isSafeInteger(event.pass) && event.pass > 0 &&
    /^[0-9a-f]{64}$/.test(event.expectedItemDigest) && head(event.head) &&
    text(event.evidence) && event.manifestComplete === true,
  'Complete current candidate revalidation evidence required.');
  canonicalPaths(event.scopes, true);
}

function validateReadmissions(state) {
  const markers = state.seen.filter(id => id.startsWith(READMISSION_MARKER));
  if (state.version < 4) {
    requireValue(!Object.hasOwn(state, 'readmissions') && markers.length === 0 &&
      state.items.every(item => !Object.hasOwn(item, 'admissionHistory')),
    'Candidate revalidation schema downgrade or omission.');
    return;
  }
  requireValue(Array.isArray(state.readmissions), 'Candidate revalidation index missing.');
  const records = [];
  for (const item of state.items) {
    requireValue(Array.isArray(item.admissionHistory), 'Candidate revalidation history missing.');
    let previous = null;
    for (const [index, record] of item.admissionHistory.entries()) {
      fields(record, 'event before fromVersion');
      const { event, before, fromVersion } = record;
      validateReadmissionEvent(event);
      requireValue([1, 2, 3, 4, 5, 6, 7, 8, 9].includes(fromVersion) && (index === 0 || fromVersion >= 4),
        'Invalid candidate revalidation source schema.');
      fields(before, `key outcome pool kind head scopes evidence pr owner phase blocker publication${
        fromVersion >= 2 ? ' scopeRevision scopeHistory sourceWritesRetired' : ''}${
        fromVersion >= 3 ? ' scopeAmendments' : ''}${fromVersion >= 6 ? ' scopeRecoveries' : ''}`);
      requireValue(unownedCandidate(before) && before.key === item.key && event.key === item.key &&
        ['outcome', 'pool', 'kind'].every(key => before[key] === item[key]) &&
        (before.pr === item.pr || before.pr === null && !['candidate', 'reserved'].includes(item.phase)) &&
        head(before.head) && text(before.evidence), 'Revalidation changed candidate identity or source authority.');
      canonicalPaths(before.scopes, true);
      requireValue(before.scopes.every(value => covers(event.scopes, value)),
        'Complete candidate manifest must retain every original scope.');
      const completeBefore = fromVersion >= 4
        ? { ...before, admissionHistory: item.admissionHistory.slice(0, index) } : before;
      requireValue(event.expectedItemDigest === digest(completeBefore) &&
        state.seen.includes(event.id), 'Candidate revalidation old-item digest/event mismatch.');
      if (previous) {
        requireValue(before.head === previous.event.head && before.evidence === previous.event.evidence &&
          equal(before.scopes, previous.event.scopes) &&
          before.blocker === previous.before.blocker && before.pr === previous.before.pr,
        'Candidate revalidation history is inconsistent.');
      }
      requireValue(before.head !== event.head || before.evidence !== event.evidence ||
        !equal(before.scopes, event.scopes), 'Candidate revalidation cannot be a no-op.');
      records.push({ key: item.key, eventId: event.id, digest: digest(record) });
      previous = record;
    }
    if (previous) {
      const originalScopes = [...item.scopeAmendments,
        ...recoveryRecords(state, item.key).map(record => ({ proof: record.event.proof }))]
        .sort((a, b) => a.proof.scopeRevision - b.proof.scopeRevision)[0]?.proof.fromScopes ??
        item.scopeHistory[0]?.proof.fromScopes ?? item.scopes;
      requireValue(item.head === previous.event.head && item.evidence === previous.event.evidence &&
        equal(originalScopes, previous.event.scopes) &&
        (item.phase !== 'candidate' || item.blocker === previous.before.blocker),
      'Current candidate admission disagrees with revalidation history.');
    }
  }
  requireValue(new Set(records.map(record => record.eventId)).size === records.length &&
    equal([...records].sort((a, b) => a.eventId.localeCompare(b.eventId)),
      [...state.readmissions].sort((a, b) => a.eventId.localeCompare(b.eventId))) &&
    equal(markers.slice().sort(), records.map(record => `${READMISSION_MARKER}${record.digest}`).sort()),
  'Candidate revalidation index/history/event binding missing or inconsistent.');
}

function validate(state) {
  if (state?.version === 10) { lifecycleProjection(state); return; }
  requireValue([1, 2, 3, 4, 5, 6, 7, 8, 9].includes(state?.version) && typeof state.enabled === 'boolean' &&
    ['observe', 'delivery'].includes(state.mode) &&
    text(state.coordinator) && (state.head === null || head(state.head)) && Array.isArray(state.items) &&
    Array.isArray(state.seen), 'Malformed registry.');
  requireValue(state.planner && ['active', 'requested', 'notified']
    .every(key => typeof state.planner[key] === 'boolean') &&
    Number.isSafeInteger(state.planner.pass) && state.planner.pass >= 0, 'Malformed planner state.');
  requireValue(state.seen.every(text) && new Set(state.seen).size === state.seen.length,
    'Malformed event history.');
  const active = [];
  const keys = new Set();
  const outcomes = new Set();
  const owners = new Set([state.coordinator]);
  if (state.planner.owner !== null) {
    requireValue(text(state.planner.owner) && !owners.has(state.planner.owner), 'Invalid planner owner.');
    owners.add(state.planner.owner);
  }
  requireValue(!state.planner.active || state.planner.owner !== null, 'Active planner has no owner.');
  for (const item of state.items) {
    requireValue(text(item.key) && text(item.outcome) && text(item.evidence) &&
      head(item.head) && Object.hasOwn(LIMITS, item.pool) && PHASES.includes(item.phase) &&
      ['feature', 'maintenance', 'repair'].includes(item.kind) &&
      (item.pool === 'repair') === (item.kind === 'repair') &&
      Array.isArray(item.scopes) && item.scopes.length > 0, 'Malformed registry item.');
    requireValue(item.scopes.every(value => scope(value) === value), 'Noncanonical saved scope.');
    requireValue(!keys.has(item.key) && !outcomes.has(item.outcome), 'Duplicate key or outcome.');
    keys.add(item.key);
    outcomes.add(item.outcome);
    requireValue(item.pr === null || Number.isSafeInteger(item.pr) && item.pr > 0, 'Invalid PR.');
    requireValue(item.pool !== 'repair' || item.pr !== null, 'Repair has no assigned PR.');
    if (item.owner !== null) {
      requireValue(text(item.owner) && !owners.has(item.owner), 'Duplicate or invalid owner.');
      owners.add(item.owner);
    }
    requireValue(!['running', 'published', 'complete'].includes(item.phase) || item.owner !== null,
      'Worker phase has no owner.');
    requireValue(!['candidate', 'reserved'].includes(item.phase) || item.owner === null,
      'Undispatched phase cannot hide an owner.');
    if (item.phase === 'published' || item.phase === 'complete') {
      const proof = item.publication;
      requireValue(proof && head(proof.pushedHead) && proof.pushedHead === proof.remoteHead &&
        proof.pr === item.pr && (item.pool !== 'ready' || proof.ready === true && proof.enrolled === true),
      'Publication evidence missing or malformed.');
    }
    if (item.phase === 'complete') {
      const proof = item.cleanup;
      requireValue(proof && proof.remoteHead === item.publication.remoteHead &&
        proof.stoppedWriting === true && proof.worktreeAbsent === true &&
        proof.branchAbsent === true && proof.pendingInstructions === 0,
      'Completed saved item lacks cleanup evidence.');
    }
    if (holdsSlot(item)) active.push(item);
  }
  for (const [pool, limit] of Object.entries(LIMITS)) {
    requireValue(active.filter(item => item.pool === pool).length <= limit, `${pool} pool is full.`);
  }
  validateSupersessions(state);
  for (let i = 0; i < active.length; i++) {
    for (let j = i + 1; j < active.length; j++) {
      requireValue(!overlaps(effectiveScopes(state, active[i]), effectiveScopes(state, active[j])),
        'Conflicting reserved file scopes.');
      requireValue(active[i].pr === null || active[i].pr !== active[j].pr, 'PR already owned.');
    }
    for (const item of active) {
      requireValue(!heldReservations(state, item.key).filter(other =>
        other.key.startsWith('supersession:')).some(other => overlaps(effectiveScopes(state, item), other.scopes)),
      'Transferred paths remain exclusively bound to their supersession beneficiary.');
      const preexisting = (state.supersessions ?? []).some(record =>
        record.event.proof.mode === 'prepare-external' &&
        record.event.proof.localReservations.some(before => before.key === item.key &&
          equal(sourceIdentity(before), sourceIdentity(item))));
      requireValue(preexisting || !externalReservations(state).some(other =>
        overlaps(effectiveScopes(state, item), other.scopes)), 'Conflicting external reserved file scopes.');
    }
  }
  const represented = state.items.filter(item => item.phase !== 'complete' && item.pr !== null);
  requireValue(new Set(represented.map(item => item.pr)).size === represented.length,
    'PR already represented by an unfinished assignment.');
  validateRefinements(state);
  validateAmendments(state);
  validateReadmissions(state);
  validateRecoveries(state);
  validateRecoveryRenewals(state);
  validateExtensionFences(state);
}

function createState(coordinator, sourceHead, mode = 'observe') {
  const state = {
    version: 1, enabled: true, mode, coordinator, head: sourceHead,
    planner: { owner: null, active: false, requested: true, notified: false, pass: 0 },
    items: [], seen: []
  };
  validate(state);
  return state;
}

function candidate(event) {
  return {
    key: event.key, outcome: event.outcome, pool: event.pool, kind: event.kind,
    head: event.head, scopes: event.scopes?.map(scope), evidence: event.evidence,
    pr: event.pr ?? null, owner: null, phase: 'candidate', blocker: null, publication: null
  };
}

function dispatch(state, actions) {
  if (!state.enabled) return;
  for (const pool of ['ready', 'repair']) {
    const waiting = state.items.filter(item => state.mode === 'delivery' && item.pool === pool &&
      item.phase === 'candidate' && item.head === state.head);
    if (pool === 'ready') waiting.sort((a, b) =>
      Number(a.kind !== 'feature') - Number(b.kind !== 'feature'));
    for (const item of waiting) {
      if (item.kind === 'maintenance' && state.planner.active) continue;
      const active = state.items.filter(holdsSlot);
      if (active.filter(other => other.pool === pool).length >= LIMITS[pool]) break;
      if (item.blocker || preparedCandidate(state, item.key) ||
        heldReservations(state, item.key).some(other => overlaps(item.scopes, other.scopes)) ||
        active.some(other => item.pr !== null && item.pr === other.pr)) continue;
      item.phase = 'reserved';
      actions.push({ type: 'launch', key: item.key, pool });
    }
  }
  if (!state.planner.owner || !state.planner.requested) return;
  const capacity = Object.fromEntries(Object.entries(LIMITS).map(([pool, limit]) =>
    [pool, limit - state.items.filter(item => item.pool === pool && holdsSlot(item)).length]));
  if (state.planner.active) {
    if (!state.planner.notified) {
      state.planner.notified = true;
      actions.push({ type: 'steer-planner', owner: state.planner.owner, capacity });
    }
  } else {
    state.planner.active = true;
    state.planner.pass++;
    state.planner.requested = false;
    state.planner.notified = false;
    actions.push({ type: 'discover', owner: state.planner.owner, capacity,
      examine: ['ready', 'repair'], head: state.head, pass: state.planner.pass });
  }
}

function applyEvent(current, event, context = {}) {
  if (current?.version === 10) return applyLifecycleEvent(current, event, context);
  validate(current);
  requireValue(event && text(event.id) && text(event.type), 'Event identity/type missing.');
  requireValue(![REFINEMENT_MARKER, AMENDMENT_MARKER, READMISSION_MARKER, SUPERSESSION_MARKER, RECOVERY_MARKER, RENEWAL_MARKER]
    .some(prefix => event.id.startsWith(prefix)),
    'Reserved scope event identity.');
  const state = structuredClone(current);
  if (state.seen.includes(event.id)) {
    requireValue(!(event.type === 'extend-scopes' && event.proof?.version === 2) &&
      !state.items.some(item => (item.scopeAmendments ?? []).some(record =>
        record.eventId === event.id && record.proof.version === 2)),
    'Fenced extension requests cannot be replayed; read the accepted checkpoint instead.');
    requireValue(event.type !== 'renew-recovery-continuation' &&
      !(state.recoveryRenewals ?? []).some(record => record.event.id === event.id),
    'Recovery renewal requests cannot be replayed; read the accepted checkpoint instead.');
    const recovery = (state.recoveries ?? []).find(record => record.event.id === event.id);
    if (recovery || event.type === 'recover-scope-incident') {
      requireValue(recovery && equal(recovery.event, event),
        'Incident recovery replay must match its original complete accepted event.');
    }
    const supersession = (state.supersessions ?? []).find(record => record.event.id === event.id);
    requireValue(supersession?.event.proof.version !== 2 &&
      !(event.type === 'supersede-write-reservations' && event.proof?.version === 2),
    'Fenced handoff requests cannot be replayed; read the accepted checkpoint instead.');
    if (supersession || event.type === 'supersede-write-reservations') {
      requireValue(supersession && equal(supersession.event, event),
        'Supersession replay must match its original complete accepted event.');
    }
    if (event.type === 'revalidate-candidate') {
      const record = state.items.flatMap(item => item.admissionHistory ?? [])
        .find(value => value.event.id === event.id);
      requireValue(record && equal(record.event, event),
        'Candidate revalidation replay must match its original accepted event.');
    }
    return { state, actions: [], duplicate: true };
  }
  const actions = [];
  if (['refine-scopes', 'extend-scopes', 'revalidate-candidate', 'recover-scope-incident'].includes(event.type)) {
    requireValue(!supersessionParticipant(state, event.key),
      'Supersession participants require fresh authority, not scope/readmission mutation.');
  }
  const worker = () => {
    const item = state.items.find(value => value.key === event.key);
    requireValue(item && holdsSlot(item) && item.owner === event.owner, 'Unknown or mismatched worker.');
    return item;
  };
  switch (event.type) {
    case 'planner':
      requireValue(state.planner.owner === null && text(event.owner), 'Planner already reserved or invalid.');
      state.planner.owner = event.owner;
      break;
    case 'candidate':
      requireValue(state.enabled && state.planner.active && event.owner === state.planner.owner,
        'Candidate must come from the active assigned planner.');
      requireValue(event.pass === state.planner.pass, 'Candidate belongs to another discovery pass.');
      requireValue(head(state.head) && event.head === state.head, 'Candidate needs a source refresh.');
      state.items.push(candidate(event));
      break;
    case 'revalidate-candidate': {
      validateReadmissionEvent(event);
      requireValue(state.enabled && state.mode === 'delivery' && state.planner.active &&
        event.owner === state.planner.owner && event.pass === state.planner.pass,
      'Candidate revalidation requires enabled delivery and the active assigned planner/pass.');
      requireValue(head(state.head) && event.head === state.head,
        'Candidate revalidation needs current verified source.');
      const item = state.items.find(value => value.key === event.key);
      requireValue(item && unownedCandidate(item), 'Only an unowned unreserved candidate can be revalidated.');
      requireValue(digest(item) === event.expectedItemDigest, 'Stale complete candidate item digest.');
      requireValue(!heldReservations(state, item.key).some(other => overlaps(event.scopes, other.scopes)) &&
        !state.items.filter(holdsSlot).some(other => item.pr !== null && item.pr === other.pr),
        'Candidate revalidation conflicts with held scopes; retain original candidate.');
      const before = structuredClone(item);
      delete before.admissionHistory;
      const record = { event: structuredClone(event), before, fromVersion: state.version };
      if (state.version < 2) {
        state.refinements = [];
        for (const other of state.items) {
          other.scopeRevision = 0;
          other.scopeHistory = [];
          other.sourceWritesRetired = false;
        }
      }
      if (state.version < 3) {
        state.amendments = [];
        for (const other of state.items) other.scopeAmendments = [];
      }
      if (state.version < 4) {
        state.version = 4;
        state.readmissions = [];
        for (const other of state.items) other.admissionHistory = [];
      }
      item.admissionHistory.push(record);
      item.head = event.head;
      item.scopes = structuredClone(event.scopes);
      item.evidence = event.evidence;
      const hash = digest(record);
      state.readmissions.push({ key: item.key, eventId: event.id, digest: hash });
      state.seen.push(`${READMISSION_MARKER}${hash}`);
      break;
    }
    case 'supersede-write-reservations': {
      validateSupersessionEvent(event);
      const p = event.proof;
      const fenced = p.version === 2;
      const before = fenced ? structuredClone(state) : null;
      requireValue(Number.isSafeInteger(context.revision) && p.checkpointRevision === context.revision,
        'Supersession requires the exact current durable checkpoint revision.');
      requireValue(state.enabled && state.mode === 'delivery' && p.coordinator === state.coordinator &&
        p.head === state.head, 'Supersession requires current coordinator, source and enabled delivery authority.');
      const item = state.items.find(value => value.key === p.candidate.key);
      requireValue(item && unownedCandidate(item) && equal(item, p.candidate) &&
        !supersessionParticipant(state, item.key), 'Stale or already bound complete supersession candidate.');
      requireValue(equal(p.localReservations, state.items.filter(holdsSlot)),
        'Stale or incomplete local supersession reservation snapshot.');
      requireValue(p.holders.filter(holder => holder.coordinator === p.coordinator)
        .every(holder => !supersessionParticipant(state, holder.key)),
      'A superseded writer cannot regain or reassign transferred source authority.');
      if (fenced) requireValue(p.expectedStateDigest === digest(state) &&
        equal(p.effectiveReservations, heldReservations(state, p.candidate.key)),
      'Fenced handoff requires the current whole state and complete effective reservation snapshot.');
      if (state.version < 2) {
        state.refinements = [];
        for (const other of state.items) {
          other.scopeRevision = 0; other.scopeHistory = []; other.sourceWritesRetired = false;
        }
      }
      if (state.version < 3) {
        state.amendments = [];
        for (const other of state.items) other.scopeAmendments = [];
      }
      if (state.version < 4) {
        state.readmissions = [];
        for (const other of state.items) other.admissionHistory = [];
      }
      if (state.version < 5) { state.version = 5; state.supersessions = []; }
      if (fenced) {
        if (state.version < 6) {
          state.recoveries = [];
          for (const other of state.items) other.scopeRecoveries = [];
        }
        if (state.version < 7) state.recoveryRenewals = [];
        state.version = 9;
      }
      const record = { event: structuredClone(event), commands: [],
        ...(fenced ? { before, hostEvidence: structuredClone(context.supersessionEvidence) } : {}) };
      state.supersessions.push(record);
      state.seen.push(`${SUPERSESSION_MARKER}${digest(record)}`);
      break;
    }
    case 'adopt': {
      requireValue(state.enabled && text(event.owner), 'Adoption requires an enabled workflow and owner.');
      state.items.push({ ...candidate(event), owner: event.owner, phase: 'running' });
      break;
    }
    case 'attached': {
      const item = state.items.find(value => value.key === event.key);
      requireValue(item?.phase === 'reserved' && item.owner === null &&
        text(event.owner) && !item.blocker, 'Launch is not an available reservation.');
      if (event.pr !== undefined) {
        requireValue(item.pr === null || item.pr === event.pr, 'Assigned PR changed.');
        item.pr = event.pr;
      }
      item.owner = event.owner;
      item.phase = 'running';
      break;
    }
    case 'launch-blocked': {
      const item = state.items.find(value => value.key === event.key);
      requireValue(item?.phase === 'reserved' && item.owner === null && text(event.reason),
        'Only an unattached reservation can have a launch blocker.');
      item.blocker = event.reason;
      break;
    }
    case 'published': {
      const item = worker();
      const proof = event.proof;
      requireValue(item.phase === 'running' && proof && head(proof.pushedHead) &&
        proof.pushedHead === proof.remoteHead && Number.isSafeInteger(proof.pr) && proof.pr > 0,
      'Matching pushed/remote head and actual PR evidence required.');
      requireValue(item.pr === null || item.pr === proof.pr, 'Publication changed assigned PR.');
      requireValue(item.pool !== 'ready' || proof.ready === true && proof.enrolled === true,
        'Delivery needs an actual ready PR and verified Auto-merge enrollment.');
      item.pr = proof.pr;
      item.publication = structuredClone(proof);
      item.phase = 'published';
      item.blocker = null;
      break;
    }
    case 'blocked': {
      const item = worker();
      requireValue(text(event.reason), 'Blocker must be explicit.');
      item.blocker = event.reason;
      break;
    }
    case 'refine-scopes': {
      requireValue(state.enabled && state.mode === 'delivery',
        'Scope refinement requires enabled delivery authority.');
      const item = worker();
      requireValue(['running', 'published'].includes(item.phase), 'Refinement requires an attached holding worker.');
      fields(event, 'id type key owner expectedScopeRevision proof acknowledgment');
      requireValue(event.expectedScopeRevision === (item.scopeRevision ?? 0) &&
        event.proof?.scopeRevision === event.expectedScopeRevision &&
        event.proof?.phase === item.phase && equal(event.proof?.fromScopes, item.scopes),
      'Stale scope revision, phase or current scope binding.');
      const record = { eventId: event.id, proof: structuredClone(event.proof),
        acknowledgment: structuredClone(event.acknowledgment) };
      const previous = [...(item.scopeHistory ?? []), ...(item.scopeAmendments ?? []),
        ...recoveryRecords(state, item.key).map(old => ({ proof: old.event.proof }))]
        .sort((a, b) => a.proof.scopeRevision - b.proof.scopeRevision).at(-1);
      validateRefinement(record, item, previous);
      requireValue(!state.items.some(other => [...(other.scopeHistory ?? []), ...(other.scopeAmendments ?? [])].some(old =>
        old.acknowledgment.id === record.acknowledgment.id)), 'Acknowledgment replay.');
      if (state.version === 1) {
        state.version = 2;
        state.refinements = [];
        for (const other of state.items) {
          other.scopeRevision = 0;
          other.scopeHistory = [];
          other.sourceWritesRetired = false;
        }
      }
      item.scopeHistory.push(record);
      item.scopeRevision++;
      item.sourceWritesRetired = true;
      item.scopes = structuredClone(record.proof.toScopes);
      const hash = digest(record);
      state.refinements.push({ key: item.key, eventId: event.id, digest: hash });
      state.seen.push(`${REFINEMENT_MARKER}${hash}`);
      state.planner.requested = true;
      break;
    }
    case 'extend-scopes': {
      requireValue(state.enabled && state.mode === 'delivery', 'Scope extension requires enabled delivery.');
      const item = worker();
      requireValue(item.phase === 'running' && (item.sourceWritesRetired ?? false) === false,
        'Scope extension requires an attached running nonretired owner.');
      const fenced = event.proof?.version === 2;
      fields(event, `id type key owner expectedScopeRevision proof acknowledgment${fenced ? ' authorization' : ''}`);
      requireValue(event.expectedScopeRevision === (item.scopeRevision ?? 0) &&
        event.proof?.scopeRevision === event.expectedScopeRevision &&
        equal(event.proof?.fromScopes, item.scopes) &&
        event.proof?.reservation?.pr === item.pr,
      'Stale mutable scope revision or current reservation binding.');
      const record = { eventId: event.id, proof: structuredClone(event.proof),
        acknowledgment: structuredClone(event.acknowledgment),
        ...(fenced ? { authorization: structuredClone(event.authorization),
          hostEvidence: structuredClone(context.extensionEvidence), before: structuredClone(item),
          fromVersion: state.version, commands: [] } : {}) };
      if (fenced) requireValue(record.proof.coordinator === state.coordinator &&
        record.proof.head === state.head && record.proof.checkpointRevision === context.revision &&
        Number.isSafeInteger(context.revision) && record.proof.expectedItemDigest === digest(item) &&
        item.publication === null, 'Fenced extension requires current coordinator, main, checkpoint and complete item.');
      const previous = [...(item.scopeAmendments ?? []),
        ...recoveryRecords(state, item.key).map(old => ({ proof: old.event.proof }))]
        .sort((a, b) => a.proof.scopeRevision - b.proof.scopeRevision).at(-1);
      validateAmendment(record, item, previous);
      requireValue(equal(record.proof.exclusivity.reservations, heldReservations(state, item.key)),
        'Stale or incomplete held-owner exclusivity snapshot.');
      requireValue(!state.items.some(other => [...(other.scopeHistory ?? []), ...(other.scopeAmendments ?? [])]
        .some(old => old.acknowledgment.id === record.acknowledgment.id)), 'Acknowledgment replay.');
      if (state.version === 1) {
        state.refinements = [];
        for (const other of state.items) {
          other.scopeRevision = 0;
          other.scopeHistory = [];
          other.sourceWritesRetired = false;
        }
      }
      if (state.version < 3) {
        state.version = 3;
        state.amendments = [];
        for (const other of state.items) other.scopeAmendments = [];
      }
      if (fenced) {
        if (state.version < 4) {
          state.readmissions = [];
          for (const other of state.items) other.admissionHistory = [];
        }
        if (state.version < 5) state.supersessions = [];
        if (state.version < 6) {
          state.recoveries = [];
          for (const other of state.items) other.scopeRecoveries = [];
        }
        if (state.version < 7) state.recoveryRenewals = [];
        state.version = Math.max(state.version, 8);
      }
      item.scopeAmendments.push(record);
      item.scopeRevision++;
      item.scopes = structuredClone(record.proof.toScopes);
      const hash = digest(record);
      state.amendments.push({ key: item.key, eventId: event.id, digest: hash });
      state.seen.push(`${AMENDMENT_MARKER}${hash}`);
      break;
    }
    case 'recover-scope-incident': {
      validateRecoveryEvent(event);
      validateRecoveryEvidence(event, context.recoveryEvidence);
      const p = event.proof;
      const item = state.items.find(value => value.key === p.key);
      requireValue(state.enabled && state.mode === 'delivery' && p.coordinator === state.coordinator &&
        p.head === state.head && p.checkpointRevision === context.revision &&
        Number.isSafeInteger(context.revision) && item?.phase === 'running' &&
        item.owner === p.owner && !item.sourceWritesRetired && !supersededWriter(state, p.key) &&
        !supersessionParticipant(state, p.key) && item.publication === null &&
        digest(item) === p.expectedItemDigest &&
        equal(p.exclusivity.reservations, heldReservations(state, item.key)),
      'Recovery requires the current live nonretired same owner, whole item, durable revision and held guards.');
      const record = { event: structuredClone(event), before: structuredClone(item),
        fromVersion: state.version, hostEvidence: structuredClone(context.recoveryEvidence), commands: [] };
      if (state.version < 2) {
        state.refinements = [];
        for (const other of state.items) {
          other.scopeRevision = 0; other.scopeHistory = []; other.sourceWritesRetired = false;
        }
      }
      if (state.version < 3) {
        state.amendments = [];
        for (const other of state.items) other.scopeAmendments = [];
      }
      if (state.version < 4) {
        state.readmissions = [];
        for (const other of state.items) other.admissionHistory = [];
      }
      if (state.version < 5) state.supersessions = [];
      if (state.version < 6) {
        state.version = 6;
        state.recoveries = [];
        for (const other of state.items) other.scopeRecoveries = [];
      }
      item.scopeRecoveries.push(event.id);
      item.scopeRevision++;
      item.scopes = structuredClone(p.toScopes);
      state.recoveries.push(record);
      state.seen.push(`${RECOVERY_MARKER}${digest(record)}`);
      break;
    }
    case 'renew-recovery-continuation': {
      const p = event.proof;
      const item = state.items.find(value => value.key === p?.key);
      const grant = recoveryRecords(state, item?.key).at(-1);
      requireValue(state.enabled && state.mode === 'delivery' && state.version >= 6 &&
        grant && item?.phase === 'running' && !item.sourceWritesRetired &&
        !supersededWriter(state, item.key) && !supersessionParticipant(state, item.key) &&
        p.coordinator === state.coordinator && p.head === state.head &&
        p.checkpointRevision === context.revision && Number.isSafeInteger(context.revision) &&
        equal(p.exclusivity?.reservations, heldReservations(state, item.key)),
      'Renewal requires current enabled same-owner recovery, main/checkpoint and exclusive guards.');
      const record = { event: structuredClone(event), before: structuredClone(item),
        hostEvidence: structuredClone(context.renewalEvidence), commands: [] };
      validateRecoveryRenewal(record, grant, (state.recoveryRenewals ?? [])
        .filter(old => old.event.proof.grantDigest === digest(grant)).at(-1));
      if (state.version < 7) { state.version = 7; state.recoveryRenewals = []; }
      state.recoveryRenewals.push(record);
      state.seen.push(`${RENEWAL_MARKER}${digest(record)}`);
      break;
    }
    case 'completed': {
      const item = worker();
      const proof = event.proof;
      requireValue(item.phase === 'published' && proof &&
        proof.remoteHead === item.publication.remoteHead && proof.stoppedWriting === true &&
        proof.worktreeAbsent === true && proof.branchAbsent === true &&
        proof.pendingInstructions === 0, 'Publication and complete cleanup proof required before release.');
      item.phase = 'complete';
      item.blocker = null;
      item.cleanup = structuredClone(proof);
      actions.push({ type: 'archive', owner: item.owner, key: item.key });
      state.planner.requested = true;
      break;
    }
    case 'pass-ended':
      requireValue(state.planner.active && event.owner === state.planner.owner &&
        event.pass === state.planner.pass &&
        event.readyExamined === true && event.repairExamined === true &&
        typeof event.remaining === 'boolean', 'The active assigned pass must examine both pools.');
      state.planner.active = false;
      state.planner.notified = false;
      state.planner.requested ||= event.remaining;
      break;
    case 'refresh': {
      requireValue(head(event.head), 'Verified source head required.');
      if (event.rescan !== undefined) {
        requireValue(typeof event.rescan === 'boolean', 'Rescan must be explicit boolean.');
      }
      const changedKnownSource = state.head !== null && state.head !== event.head;
      state.head = event.head;
      state.planner.requested ||= changedKnownSource || event.rescan === true;
      break;
    }
    case 'pause':
      state.enabled = false;
      state.planner.requested = false;
      for (const item of state.items.filter(holdsSlot)) {
        if (item.owner) actions.push({ type: 'pause', owner: item.owner, key: item.key });
      }
      if (state.planner.owner) actions.push({ type: 'pause', owner: state.planner.owner });
      break;
    case 'handoff': {
      requireValue(!state.enabled && event.fromCoordinator === state.coordinator &&
        text(event.toCoordinator) && event.toCoordinator !== state.coordinator,
      'Handoff requires a paused workflow and exact old/new coordinator identities.');
      const requiredOwners = [state.coordinator, state.planner.owner,
        ...state.items.filter(holdsSlot).map(item => item.owner)].filter(value => value !== null);
      requireValue(!state.items.some(item => holdsSlot(item) && item.owner === null),
        'An unattached reservation must be reconciled before handoff.');
      requireValue(event.proof && event.proof.pendingInstructions === 0 &&
        Array.isArray(event.proof.stoppedOwners) &&
        requiredOwners.every(owner => event.proof.stoppedOwners.includes(owner)),
      'Every old writer must be stopped and restarting instructions cleared before handoff.');
      state.coordinator = event.toCoordinator;
      state.planner.active = false;
      state.planner.requested = false;
      state.planner.notified = false;
      break;
    }
    case 'resume':
      requireValue(!state.enabled && event.owner === state.coordinator &&
        event.authorityConfirmed === true && typeof event.discover === 'boolean',
      'Resume requires the exact coordinator, confirmed authority and explicit discovery choice.');
      state.enabled = true;
      state.head = null;
      state.planner.requested = event.discover;
      break;
    default:
      throw new Error(`Unknown event type: ${event.type}`);
  }
  state.seen.push(event.id);
  if (state.version >= 2) {
    for (const item of state.items) {
      if (!Object.hasOwn(item, 'scopeRevision')) {
        item.scopeRevision = 0;
        item.scopeHistory = [];
        item.sourceWritesRetired = false;
      }
      if (state.version >= 3 && !Object.hasOwn(item, 'scopeAmendments')) item.scopeAmendments = [];
      if (state.version >= 4 && !Object.hasOwn(item, 'admissionHistory')) item.admissionHistory = [];
      if (state.version >= 6 && !Object.hasOwn(item, 'scopeRecoveries')) item.scopeRecoveries = [];
    }
  }
  validate(state);
  if (!['recover-scope-incident', 'renew-recovery-continuation'].includes(event.type) &&
    !(['extend-scopes', 'supersede-write-reservations'].includes(event.type) &&
      event.proof.version === 2)) dispatch(state, actions);
  if (event.type === 'supersede-write-reservations') {
    const record = state.supersessions.at(-1);
    const marker = `${SUPERSESSION_MARKER}${digest(record)}`;
    record.commands = structuredClone(actions);
    state.seen[state.seen.indexOf(marker)] = `${SUPERSESSION_MARKER}${digest(record)}`;
  }
  validate(state);
  return { state, actions, duplicate: false };
}

const LIFECYCLE_COMMANDS = Object.freeze({
  adopt: 'kind legacyDigest',
  register: 'kind key outcome pool workKind evidence pr',
  allocate: 'kind key grantId scopes transfers',
  dispatch: 'kind grantId',
  attach: 'kind grantId owner source',
  ambiguous: 'kind grantId reason',
  revoke: 'kind grantId reason',
  publish: 'kind grantId publication',
  amend: 'kind grantId scopes',
  refresh: 'kind head',
  compatible: 'kind grantId head',
  close: 'kind key outcome releases',
  release: 'kind releases',
  repair: 'kind key pr grantId scopes transfers',
  pause: 'kind reason',
  recover: 'kind coordinator',
  resume: 'kind',
  cleanup: 'kind key worktreeAbsent branchAbsent receipt'
});
const lifecycleLive = g => ['allocated', 'reserved', 'ambiguous', 'active', 'unresolved'].includes(g.status);
const lifecycleAuthority = kind => !['register', 'publish', 'refresh', 'ambiguous', 'pause'].includes(kind);
const hashValue = value => typeof value === 'string' && /^[0-9a-f]{64}$/.test(value);

function lifecycleFields(value, names) {
  requireValue(value && typeof value === 'object' && !Array.isArray(value) &&
    equal(Object.keys(value).sort(), names.split(' ').sort()), 'Malformed lifecycle fields.');
}

function lifecycleSource(source) {
  lifecycleFields(source, 'worktree branch head');
  requireValue(typeof source.worktree === 'string' && path.win32.isAbsolute(source.worktree) &&
    path.win32.normalize(source.worktree) === source.worktree && text(source.branch) && head(source.head),
  'Exact canonical lifecycle source required.');
}

function lifecycleCommand(c) {
  requireValue(c && Object.hasOwn(LIFECYCLE_COMMANDS, c.kind), 'Unknown lifecycle command.');
  lifecycleFields(c, LIFECYCLE_COMMANDS[c.kind] +
    (c.kind === 'compatible' && Object.hasOwn(c, 'source') ? ' source' : ''));
  for (const name of ['key', 'grantId', 'outcome', 'evidence', 'owner', 'reason', 'coordinator', 'receipt']) {
    if (Object.hasOwn(c, name)) requireValue(text(c[name]), `Lifecycle ${name} required.`);
  }
  if (Object.hasOwn(c, 'scopes')) canonicalPaths(c.scopes, true);
  if (Object.hasOwn(c, 'head')) requireValue(head(c.head), 'Lifecycle source head required.');
  if (c.source) lifecycleSource(c.source);
  for (const name of ['transfers', 'releases']) {
    if (!Object.hasOwn(c, name)) continue;
    requireValue(Array.isArray(c[name]), 'Explicit lifecycle custody disposition required.');
    const ids = new Set();
    for (const d of c[name]) {
      lifecycleFields(d, 'reservationId paths');
      requireValue(text(d.reservationId) && !ids.has(d.reservationId), 'Duplicate custody disposition.');
      ids.add(d.reservationId);
      canonicalPaths(d.paths, true);
    }
  }
}

function createLifecycleState(coordinator, sourceHead, mode = 'observe') {
  const state = { version: 10, origin: { coordinator, head: sourceHead, mode,
    legacy: null, legacyDigest: null }, history: [] };
  lifecycleProjection(state);
  return state;
}

function lifecycleInitial(origin) {
  lifecycleFields(origin, 'coordinator head mode legacy legacyDigest');
  requireValue(text(origin.coordinator) && (origin.head === null || head(origin.head)) &&
    ['observe', 'delivery'].includes(origin.mode), 'Malformed lifecycle origin.');
  const p = { version: 10, coordinator: origin.coordinator, enabled: true, mode: origin.mode,
    head: origin.head, sourceRevision: 0, authorityRevision: 0,
    works: [], grants: [], reservations: [], capacity: { ...LIMITS } };
  if (origin.legacy === null) {
    requireValue(origin.legacyDigest === null, 'Unexpected legacy digest.');
    return p;
  }
  requireValue(origin.legacy?.version !== 10, 'Recursive lifecycle adoption forbidden.');
  validate(origin.legacy);
  const old = origin.legacy;
  requireValue(origin.legacyDigest === digest(old) && old.coordinator === origin.coordinator &&
    old.head === origin.head && old.mode === origin.mode, 'Complete legacy origin/digest mismatch.');
  p.enabled = old.enabled;
  for (const item of old.items) {
    p.works.push({ key: item.key, outcome: item.outcome, pool: item.pool, kind: item.kind,
      evidence: item.evidence, pr: item.pr, status: item.phase === 'candidate' ? 'candidate' :
        item.phase === 'complete' ? 'delivered' : 'open',
      publications: item.publication ? [structuredClone(item.publication)] : [],
      cleanup: item.cleanup ? structuredClone(item.cleanup) : null });
    if (item.phase !== 'candidate') p.grants.push({ id: `legacy:${item.key}`, key: item.key,
      generation: 0, revision: 0, scopes: structuredClone(item.scopes),
      status: supersededWriter(old, item.key) || item.sourceWritesRetired || item.phase === 'complete'
        ? 'revoked' : 'unresolved', owner: item.owner, source: null, head: item.head, sourceRevision: -1 });
  }
  for (const held of heldReservations(old)) {
    if (held.scopes.length === 0) continue;
    const supersession = (old.supersessions ?? []).find(r =>
      `supersession:${r.event.id}:${r.event.proof.candidate.key}` === held.key);
    const key = supersession?.event.proof.candidate.key ?? held.key;
    const id = `legacy:${key}`;
    const existing = p.reservations.find(r => r.id === id);
    if (existing) existing.scopes = [...new Set([...existing.scopes, ...held.scopes])];
    else p.reservations.push({ id, key, grantId: p.grants.find(g => g.key === key)?.id ?? null,
      scopes: structuredClone(held.scopes), revision: 0, status: 'HELD' });
  }
  lifecycleCapacity(p);
  return p;
}

function lifecycleCapacity(p) {
  for (const [pool, limit] of Object.entries(LIMITS)) {
    p.capacity[pool] = limit - p.works.filter(w => w.pool === pool && w.status === 'open').length;
    requireValue(p.capacity[pool] >= 0, `Lifecycle ${pool} pool is full.`);
  }
}

function previewLifecycleMigration(legacy) {
  requireValue(legacy?.version !== 10, 'Preview requires accepted legacy version1-9.');
  validate(legacy);
  const legacyDigest = digest(legacy);
  return { version: 10, legacyDigest, activating: false,
    projection: lifecycleInitial({ coordinator: legacy.coordinator, head: legacy.head,
      mode: legacy.mode, legacy, legacyDigest }) };
}

function lifecycleAffected(p, c) {
  lifecycleCommand(c);
  const all = ['adopt', 'pause', 'recover', 'resume'].includes(c.kind);
  const keys = new Set(c.key ? [c.key] : []);
  const grant = p.grants.find(g => g.id === c.grantId);
  if (grant) keys.add(grant.key);
  const representedPr = p.works.find(w => keys.has(w.key))?.pr;
  if (representedPr !== null && representedPr !== undefined)
    p.works.filter(w => w.pr === representedPr).forEach(w => keys.add(w.key));
  const ids = new Set([...(c.transfers ?? []), ...(c.releases ?? [])].map(d => d.reservationId));
  const paths = [...(c.scopes ?? []), ...(grant?.scopes ?? [])];
  for (const r of p.reservations) {
    if (ids.has(r.id) || overlaps(r.scopes, paths)) keys.add(r.key);
  }
  const workKeys = all ? p.works.map(w => w.key) : [...keys];
  const grants = p.grants.filter(g => all || keys.has(g.key));
  const reservations = p.reservations.filter(r => all || keys.has(r.key) || overlaps(r.scopes, paths));
  return { sourceRevision: p.sourceRevision, authorityRevision: p.authorityRevision,
    works: Object.fromEntries(workKeys.sort().map(key => [key, p.works.some(w => w.key === key)
      ? digest(p.works.find(w => w.key === key)) : null])),
    grants: Object.fromEntries([...new Set([...grants.map(g => g.id), ...(c.grantId ? [c.grantId] : [])])]
      .sort().map(id => [id, p.grants.some(g => g.id === id) ? digest(p.grants.find(g => g.id === id)) : null])),
    reservations: Object.fromEntries(reservations.sort((a, b) => a.id.localeCompare(b.id))
      .map(r => [r.id, digest(r)])) };
}

function lifecycleRevisions(state, command) {
  return lifecycleAffected(lifecycleProjection(state), command);
}

function lifecycleProofDigest(event) {
  const value = structuredClone(event);
  if (value.evidence?.human) delete value.evidence.human.proofDigest;
  for (const holder of value.evidence?.holders ?? []) delete holder.acknowledgment.proofDigest;
  return digest(value);
}

function lifecycleHolders(p, c) {
  const ids = new Set([...(c.transfers ?? []), ...(c.releases ?? [])].map(d =>
    p.reservations.find(r => r.id === d.reservationId)?.grantId).filter(Boolean));
  if (['revoke', 'amend', 'compatible'].includes(c.kind)) ids.add(c.grantId);
  if (['allocate', 'repair', 'close', 'cleanup'].includes(c.kind)) {
    p.grants.filter(g => g.key === c.key).forEach(g => ids.add(g.id));
  }
  if (['adopt', 'pause', 'recover'].includes(c.kind)) {
    p.grants.filter(lifecycleLive).forEach(g => ids.add(g.id));
  }
  return [...ids].sort().map(id => p.grants.find(g => g.id === id));
}

function lifecycleEvidence(p, event, e, used) {
  lifecycleFields(event, 'id type command expected evidence');
  requireValue(text(event.id) && event.type === 'lifecycle', 'Lifecycle event identity/type required.');
  const c = event.command;
  lifecycleCommand(c);
  requireValue(equal(event.expected, lifecycleAffected(p, c)), 'Stale affected lifecycle revisions.');
  lifecycleFields(event.evidence, 'human holders receipt');
  requireValue(text(event.evidence.receipt) && Array.isArray(event.evidence.holders),
    'Explicit lifecycle evidence required.');
  lifecycleFields(e, 'kind eventDigest coordinator ledgerAuthorityVerified humanAuthorizationVerified holdersVerified sourceOwnershipVerified deliveryVerified preservationVerified receipt');
  requireValue(e.kind === 'host-verified-lifecycle' && e.eventDigest === digest(event) &&
    e.coordinator === p.coordinator && e.ledgerAuthorityVerified === true &&
    ['humanAuthorizationVerified', 'sourceOwnershipVerified', 'deliveryVerified', 'preservationVerified']
      .every(name => typeof e[name] === 'boolean') && text(e.receipt),
  'Independent current host lifecycle readback required.');
  const binding = lifecycleProofDigest(event), h = event.evidence.human;
  if (lifecycleAuthority(c.kind)) {
    lifecycleFields(h, 'id actor proofDigest receipt');
    requireValue(text(h.id) && text(h.actor) && text(h.receipt) && h.proofDigest === binding &&
      e.humanAuthorizationVerified === true && !used.decisions.has(h.id),
    'Fresh exact human lifecycle decision required.');
  } else requireValue(h === null, 'Bookkeeping must not claim unnecessary human authority.');
  const required = lifecycleHolders(p, c);
  requireValue(required.every(Boolean) && equal(required.map(g => g.id),
    event.evidence.holders.map(v => v.grantId)) && equal(e.holdersVerified, required.map(g => g.id)),
  'Every affected exact holder requires independent readback.');
  for (const [index, grant] of required.entries()) {
    const v = event.evidence.holders[index];
    lifecycleFields(v, 'grantId owner writingStopped commandsRunning workJoined pendingInstructions processingInstructions fence preservation acknowledgment');
    lifecycleFields(v.fence, 'epoch nonce established revokesEarlierSourceAuthority');
    lifecycleFields(v.acknowledgment, 'id proofDigest receipt');
    requireValue(v.owner === grant.owner && v.writingStopped === true && v.commandsRunning === false &&
      v.workJoined === true && [0, 'UNKNOWN'].includes(v.pendingInstructions) &&
      [0, 'UNKNOWN'].includes(v.processingInstructions) && Number.isSafeInteger(v.fence.epoch) &&
      v.fence.epoch > (used.epochs.get(grant.id) ?? 0) && text(v.fence.nonce) &&
      !used.nonces.has(v.fence.nonce) && v.fence.established === true &&
      v.fence.revokesEarlierSourceAuthority === true && text(v.acknowledgment.id) &&
      !used.acknowledgments.has(v.acknowledgment.id) && v.acknowledgment.proofDigest === binding &&
      text(v.acknowledgment.receipt),
    'Fresh exact holder pause/join/source fence/acknowledgment required; UNKNOWN is not zero.');
    const m = v.preservation;
    lifecycleFields(m, 'actual planned committed restored retained unpreserved commits bytesPreserved refsPreserved historyPreserved receipt');
    for (const name of ['actual', 'planned', 'committed', 'restored', 'retained', 'unpreserved'])
      canonicalPaths(m[name]);
    requireValue(m.unpreserved.length === 0 && m.bytesPreserved === true && m.refsPreserved === true &&
      m.historyPreserved === true && text(m.receipt) && Array.isArray(m.commits) &&
      m.commits.every(commit => {
        lifecycleFields(commit, 'head ref');
        return head(commit.head) && text(commit.ref);
      }) && [...m.committed, ...m.restored].every(value => m.actual.includes(value)) &&
      [...m.actual, ...m.committed, ...m.restored].every(value => covers(m.retained, value)) &&
      (m.committed.length === 0 || m.commits.length > 0) && e.preservationVerified === true,
    'Complete durable lifecycle bytes/refs/history preservation required.');
    requireValue(c.kind !== 'amend' || [...m.actual, ...m.committed, ...m.restored]
      .every(value => covers(grant.scopes, value)),
    'Ordinary lifecycle amendment cannot retroauthorize an outside-scope write.');
    const prior = used.manifests.get(grant.id);
    requireValue(!prior || ['actual', 'planned', 'committed', 'restored'].every(name =>
      prior[name].every(value => (name === 'planned' ? [...m.actual, ...m.planned] : m[name])
        .includes(value))), 'Lifecycle preservation history was omitted.');
    used.manifests.set(grant.id, m);
    used.epochs.set(grant.id, v.fence.epoch);
    used.nonces.add(v.fence.nonce);
    used.acknowledgments.add(v.acknowledgment.id);
  }
  if (h) used.decisions.add(h.id);
}

function lifecycleDispose(p, dispositions, targetScopes = null) {
  for (const d of dispositions) {
    const r = p.reservations.find(r => r.id === d.reservationId);
    requireValue(r && p.works.some(w => w.key === r.key) &&
      d.paths.every(value => r.scopes.includes(value) && (!targetScopes || targetScopes.includes(value))),
    'Exact local custody disposition required; external preparation cannot activate.');
    r.scopes = r.scopes.filter(value => !d.paths.includes(value));
    r.revision++;
  }
  p.reservations = p.reservations.filter(r => r.scopes.length);
}

function lifecycleReduce(p, event, e, used, origin, index) {
  lifecycleEvidence(p, event, e, used);
  const c = event.command, actions = [];
  const work = p.works.find(w => w.key === c.key);
  const grant = p.grants.find(g => g.id === c.grantId);
  const revoke = g => { g.status = 'revoked'; g.revision++; };
  requireValue(!origin.legacy || index > 0 || c.kind === 'adopt',
    'Legacy shadow must be explicitly adopted before lifecycle operations.');
  if (['allocate', 'repair', 'dispatch', 'attach', 'amend', 'compatible', 'publish'].includes(c.kind)) {
    requireValue(p.enabled && p.mode === 'delivery' && head(p.head),
      'Active lifecycle delivery and verified source required.');
  }
  switch (c.kind) {
    case 'adopt':
      requireValue(index === 0 && origin.legacy && c.legacyDigest === origin.legacyDigest,
        'Explicit complete legacy adoption required; never automatic.');
      break;
    case 'register':
      requireValue(!work && !p.works.some(w => w.outcome === c.outcome) &&
        Object.hasOwn(LIMITS, c.pool) && ['feature', 'maintenance', 'repair'].includes(c.workKind) &&
        (c.pool === 'repair') === (c.workKind === 'repair') &&
        (c.pr === null || Number.isSafeInteger(c.pr) && c.pr > 0) &&
        (c.pool !== 'repair' || c.pr !== null) && !p.works.some(w => c.pr !== null && w.pr === c.pr),
      'Unique admitted lifecycle outcome/key/PR required; use same-work repair.');
      p.works.push({ key: c.key, outcome: c.outcome, pool: c.pool, kind: c.workKind,
        evidence: c.evidence, pr: c.pr, status: 'candidate', publications: [], cleanup: null });
      break;
    case 'allocate':
    case 'repair': {
      requireValue(work && (c.kind === 'repair' ? ['open', 'delivered', 'cancelled', 'abandoned'].includes(work.status) &&
        work.pr === c.pr && Number.isSafeInteger(c.pr) && c.pr > 0 : ['candidate', 'open'].includes(work.status)) &&
        !p.grants.some(g => g.id === c.grantId) && !c.grantId.startsWith('legacy:'),
      'Fresh bounded execution identity required; retired grants never restart.');
      requireValue(work.pr === null || !p.works.some(w =>
        w.key !== work.key && w.pr === work.pr && w.status === 'open'), 'PR already has a current execution.');
      requireValue(!origin.legacy || !preparedCandidate(origin.legacy, c.key),
        'Legacy external preparation is nonactivating, not execution authority.');
      lifecycleHolders(p, c).forEach(revoke);
      lifecycleDispose(p, c.transfers, c.scopes);
      requireValue(!p.reservations.some(r => overlaps(r.scopes, c.scopes)),
        'Lifecycle custody conflicts with retained exclusions.');
      const generation = Math.max(0, ...p.grants.filter(g => g.key === c.key).map(g => g.generation)) + 1;
      p.grants.push({ id: c.grantId, key: c.key, generation, revision: 0, scopes: structuredClone(c.scopes),
        status: 'allocated', owner: null, source: null, head: p.head, sourceRevision: p.sourceRevision });
      p.reservations.push({ id: `grant:${c.grantId}`, key: c.key, grantId: c.grantId,
        scopes: structuredClone(c.scopes), revision: 0, status: 'HELD' });
      work.status = 'open';
      if (c.kind === 'repair') { work.pool = 'repair'; work.kind = 'repair'; }
      break;
    }
    case 'dispatch':
      requireValue(grant?.status === 'allocated' && grant.head === p.head &&
        grant.sourceRevision === p.sourceRevision, 'Only a fresh allocated grant may dispatch once.');
      grant.status = 'reserved'; grant.revision++;
      actions.push({ type: 'launch', key: grant.key, pool: p.works.find(w => w.key === grant.key).pool,
        grantId: grant.id, grantRevision: grant.revision });
      break;
    case 'attach':
      requireValue(grant && ['reserved', 'ambiguous'].includes(grant.status) && grant.owner === null &&
        e.sourceOwnershipVerified === true &&
        c.owner !== p.coordinator && !p.grants.some(g => lifecycleLive(g) &&
          (g.owner === c.owner || g.source &&
            (g.source.worktree.toLowerCase() === c.source.worktree.toLowerCase() ||
              g.source.branch === c.source.branch))),
      'Exact new child and exclusive source attachment required.');
      grant.owner = c.owner; grant.source = structuredClone(c.source);
      grant.status = 'active'; grant.revision++;
      break;
    case 'ambiguous':
      requireValue(grant?.status === 'reserved', 'Only an unresolved launch may be ambiguous.');
      grant.status = 'ambiguous'; grant.revision++;
      break;
    case 'revoke':
      requireValue(grant && lifecycleLive(grant), 'Only a current grant may be revoked.');
      revoke(grant);
      break;
    case 'publish': {
      const proof = c.publication, current = p.works.find(w => w.key === grant?.key);
      lifecycleFields(proof, 'pushedHead remoteHead pr ready enrolled receipt');
      requireValue(grant?.status === 'active' && grant.head === p.head &&
        grant.sourceRevision === p.sourceRevision && head(proof.pushedHead) &&
        proof.pushedHead === proof.remoteHead && Number.isSafeInteger(proof.pr) && proof.pr > 0 &&
        (current.pr === null || current.pr === proof.pr) && proof.ready === true &&
        proof.enrolled === true && text(proof.receipt) &&
        !p.works.some(w => w.key !== current.key && w.pr === proof.pr && w.status === 'open'),
      'Verified same-PR publication required.');
      current.pr = proof.pr; current.publications.push(structuredClone(proof));
      used.published.add(grant.id);
      break;
    }
    case 'amend': {
      requireValue(grant?.status === 'active' && grant.head === p.head &&
        grant.sourceRevision === p.sourceRevision && grant.scopes.every(s => c.scopes.includes(s)) &&
        !p.reservations.some(r => r.grantId !== grant.id && overlaps(r.scopes, c.scopes)),
      'Current source and exclusive complete scope union required.');
      grant.scopes = structuredClone(c.scopes); grant.revision++;
      const r = p.reservations.find(r => r.grantId === grant.id);
      requireValue(r, 'Active grant custody missing.');
      r.scopes = structuredClone(c.scopes); r.revision++;
      break;
    }
    case 'refresh':
      if (p.head !== c.head) { p.head = c.head; p.sourceRevision++; }
      break;
    case 'compatible':
      requireValue(grant?.status === 'active' && c.head === p.head && e.sourceOwnershipVerified === true,
        'Fresh independent current-source compatibility required.');
      if (c.source) {
        requireValue(c.source.worktree === grant.source.worktree && c.source.branch === grant.source.branch,
          'Compatibility cannot replace the attached source identity.');
        grant.source = structuredClone(c.source);
      }
      grant.head = c.head; grant.sourceRevision = p.sourceRevision; grant.revision++;
      break;
    case 'close':
      requireValue(work && work.status === 'open' && ['delivered', 'cancelled', 'abandoned'].includes(c.outcome) &&
        !p.grants.some(g => g.key === work.key && lifecycleLive(g)) &&
        (c.outcome !== 'delivered' || work.publications.length > 0 && e.deliveryVerified === true &&
          used.published.has(p.grants.filter(g => g.key === work.key).at(-1)?.id)) &&
        e.preservationVerified === true && c.releases.every(d =>
          p.reservations.find(r => r.id === d.reservationId)?.key === c.key),
      'Logical closure requires terminated joined authority, delivery and durable preservation.');
      lifecycleDispose(p, c.releases);
      work.status = c.outcome;
      break;
    case 'release':
      requireValue(c.releases.length > 0 && c.releases.every(d => {
        const r = p.reservations.find(r => r.id === d.reservationId);
        return r && p.grants.find(g => g.id === r.grantId)?.status === 'revoked';
      }), 'Only explicitly retired remainder custody may be released.');
      lifecycleDispose(p, c.releases);
      break;
    case 'pause':
      requireValue(p.enabled, 'Lifecycle already paused.');
      p.enabled = false; p.authorityRevision++;
      p.grants.filter(lifecycleLive).forEach(revoke);
      break;
    case 'recover':
      requireValue(!p.enabled && c.coordinator !== p.coordinator &&
        !p.grants.some(lifecycleLive), 'Recovery requires paused joined authority.');
      p.coordinator = c.coordinator; p.authorityRevision++; p.head = null; p.sourceRevision++;
      break;
    case 'resume':
      requireValue(!p.enabled, 'Lifecycle is not paused.');
      p.enabled = true; p.authorityRevision++;
      break;
    case 'cleanup':
      requireValue(work && ['delivered', 'cancelled', 'abandoned'].includes(work.status) &&
        c.worktreeAbsent === true && c.branchAbsent === true && e.preservationVerified === true &&
        e.sourceOwnershipVerified === true,
      'Optional physical cleanup needs explicit authority and actual absence evidence.');
      work.cleanup = { worktreeAbsent: true, branchAbsent: true, receipt: c.receipt };
      break;
    default:
      throw new Error('Unsupported lifecycle command.');
  }
  lifecycleCapacity(p);
  return actions;
}

function lifecycleReplay(state) {
  lifecycleFields(state, 'version origin history');
  requireValue(state.version === 10 && Array.isArray(state.history), 'Malformed lifecycle registry.');
  const p = lifecycleInitial(state.origin);
  const used = { decisions: new Set(), acknowledgments: new Set(), nonces: new Set(),
    epochs: new Map(), manifests: new Map(), published: new Set() };
  if (state.origin.legacy) {
    const old = state.origin.legacy;
    const remember = (key, acknowledgment, authorization, fence) => {
      if (acknowledgment?.id) used.acknowledgments.add(acknowledgment.id);
      if (authorization?.id) used.decisions.add(authorization.id);
      if (fence) {
        used.nonces.add(fence.nonce);
        used.epochs.set(`legacy:${key}`, Math.max(used.epochs.get(`legacy:${key}`) ?? 0, fence.epoch));
      }
    };
    for (const item of old.items) for (const r of [...(item.scopeHistory ?? []), ...(item.scopeAmendments ?? [])])
      remember(item.key, r.acknowledgment, r.authorization, r.proof.reconciliation?.fence);
    for (const r of [...(old.recoveries ?? []), ...(old.recoveryRenewals ?? [])]) {
      const event = r.event;
      remember(event.proof.key, event.acknowledgment, event.authorization, event.proof.reconciliation?.fence);
    }
    for (const r of old.supersessions ?? []) for (const h of r.event.proof.holders)
      remember(h.key, h.acknowledgment, r.event.authorization, h.reconciliation?.fence);
    for (const item of old.items) {
      if (item.publication) used.published.add(`legacy:${item.key}`);
      const manifests = [...(item.scopeHistory ?? []), ...(item.scopeAmendments ?? [])]
        .map(r => r.proof.manifest);
      manifests.push(...(old.recoveries ?? []).filter(r => r.event.proof.key === item.key)
        .map(r => ({ ...r.event.proof.manifest, restored: r.event.proof.workingTree.restored })));
      manifests.push(...(old.supersessions ?? []).flatMap(r => r.event.proof.holders
        .filter(h => h.key === item.key && h.coordinator === old.coordinator).map(h => h.manifest))
        .filter(Boolean));
      used.manifests.set(`legacy:${item.key}`, Object.fromEntries(['actual', 'planned', 'committed', 'restored']
        .map(name => [name, [...new Set(manifests.flatMap(m => m[name] ?? []))]])));
    }
  }
  const ids = new Set(state.origin.legacy?.seen ?? []), commands = [];
  let previous = null;
  for (const [index, record] of state.history.entries()) {
    lifecycleFields(record, 'event hostEvidence digest');
    requireValue(hashValue(record.digest) && record.digest === digest({
      previous, event: record.event, hostEvidence: record.hostEvidence }) &&
      !ids.has(record.event.id), 'Lifecycle history digest/replay binding changed.');
    commands.push(lifecycleReduce(p, record.event, record.hostEvidence, used, state.origin, index));
    ids.add(record.event.id);
    previous = record.digest;
  }
  return { projection: p, commands };
}

function lifecycleProjection(state) {
  return lifecycleReplay(state).projection;
}

function applyLifecycleEvent(current, event, context) {
  const replay = lifecycleReplay(current);
  const prior = current.history.find(r => r.event.id === event?.id);
  if (prior) {
    requireValue(equal(prior.event, event) && !lifecycleAuthority(event.command.kind),
      'Lifecycle authority cannot be replayed; read the accepted checkpoint.');
    return { state: structuredClone(current), actions: [], duplicate: true };
  }
  const state = structuredClone(current);
  const record = { event: structuredClone(event), hostEvidence: structuredClone(context.lifecycleEvidence) };
  record.digest = digest({ previous: current.history.at(-1)?.digest ?? null, ...record });
  state.history.push(record);
  const checked = lifecycleReplay(state);
  requireValue(checked.commands.length === replay.commands.length + 1, 'Lifecycle history append failed.');
  return { state, actions: checked.commands.at(-1), duplicate: false };
}

function adoptLifecycle(legacy, event, context = {}) {
  const preview = previewLifecycleMigration(legacy);
  requireValue(event?.command?.kind === 'adopt', 'Explicit adoption event required.');
  return applyLifecycleEvent({ version: 10, origin: { coordinator: legacy.coordinator,
    head: legacy.head, mode: legacy.mode, legacy: structuredClone(legacy),
    legacyDigest: preview.legacyDigest }, history: [] }, event, context);
}

function validateSourceGrant(state, request, context = {}) {
  const p = lifecycleProjection(state);
  lifecycleFields(request, 'grantId grantRevision owner source head paths');
  lifecycleSource(request.source);
  canonicalPaths(request.paths, true);
  const g = p.grants.find(g => g.id === request.grantId);
  requireValue(p.enabled && p.mode === 'delivery' && g?.status === 'active' &&
    g.revision === request.grantRevision && g.owner === request.owner && equal(g.source, request.source) &&
    head(p.head) && request.head === p.head && g.head === p.head && g.sourceRevision === p.sourceRevision &&
    request.paths.every(value => covers(g.scopes, value)) &&
    p.reservations.some(r => r.grantId === g.id && request.paths.every(value => covers(r.scopes, value))) &&
    !p.reservations.some(r => r.grantId !== g.id && overlaps(r.scopes, request.paths)) &&
    context.sourceAuthorityVerified === true && text(context.receipt),
  'Current exact source grant/owner/revision/custody and independent host authority required.');
  return { valid: true, executionAuthorized: false, grantId: g.id, grantRevision: g.revision };
}

module.exports = { createState, applyEvent, validate, digest, scope, canonicalPaths, overlaps,
  heldReservations, effectiveScopes, supersededWriter, preparedCandidate,
  supersessionAcknowledgmentDigest, validateRecoveryContinuation, recoveryContinuationDigest,
  validateScopeExtensionContinuation, LIMITS, createLifecycleState, previewLifecycleMigration,
  adoptLifecycle, lifecycleProjection, lifecycleRevisions, lifecycleProofDigest, validateSourceGrant };

if (require.main === module) {
  try {
    const input = JSON.parse(require('node:fs').readFileSync(0, 'utf8'));
    const result = input.operation === 'init'
      ? { state: (input.lifecycleVersion === 10 ? createLifecycleState : createState)(
        input.coordinator, input.head, input.mode), actions: [] }
      : input.operation === 'preview-lifecycle' ? previewLifecycleMigration(input.state)
      : input.operation === 'adopt-lifecycle' ? adoptLifecycle(input.state, input.event, input.context)
      : applyEvent(input.state, input.event, input.context);
    console.log(JSON.stringify(result));
  } catch (error) {
    console.error(error);
    process.exitCode = 1;
  }
}
