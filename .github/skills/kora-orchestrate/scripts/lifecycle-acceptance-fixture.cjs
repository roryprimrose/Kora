'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');
const { createState, applyEvent } = require('./registry.cjs');
const registry = require('./registry.cjs');
const { checkpoint } = require('./checkpoint.cjs');

function legacyScenario() {
  const coordinator = 'synthetic:lifecycle-coordinator';
  const planner = 'synthetic:lifecycle-planner';
  const owner = 'synthetic:lifecycle-worker-a';
  const head = 'a'.repeat(40);
  const key = 'synthetic-retained-delivery';
  const scopes = ['synthetic\\shared.cjs', 'synthetic\\remainder.cjs'];
  return {
    coordinator, head, owner, key, scopes,
    events: [
      { id: 'legacy-planner', type: 'planner', owner: planner },
      { id: 'legacy-candidate', type: 'candidate', owner: planner, pass: 1,
        key, outcome: 'Synthetic retained-file delivery', pool: 'ready', kind: 'feature',
        head, scopes, evidence: 'Synthetic scenario input; not live product or human authority.' },
      { id: 'legacy-attached', type: 'attached', key, owner },
      { id: 'legacy-published', type: 'published', key, owner,
        proof: { pushedHead: head, remoteHead: head, pr: 101, ready: true, enrolled: true } }
    ]
  };
}

function replay(events, initial, context = {}) {
  let state = structuredClone(initial);
  const steps = [];
  for (const event of events) {
    const result = applyEvent(state, event, context);
    steps.push({ event: structuredClone(event), actions: structuredClone(result.actions) });
    state = result.state;
  }
  return { state, steps };
}

function createWorkspace(root) {
  assert.equal(typeof root, 'string', 'An explicit ignored scratch root is required.');
  assert(path.isAbsolute(root) && path.normalize(root) === root &&
    root.split(path.sep).includes('.scratch'), 'Scratch root must be absolute and canonical.');
  let existing = root;
  while (!fs.existsSync(existing)) {
    const parent = path.dirname(existing);
    assert.notEqual(parent, existing, 'Scratch root must belong to an existing checkout.');
    existing = parent;
  }
  assert.equal(fs.realpathSync.native(existing).toLowerCase(), existing.toLowerCase(),
    'Scratch ancestors must not redirect to another location.');
  execFileSync('git', ['-C', existing, 'check-ignore', '--quiet', '--',
    path.join(root, 'lifecycle-acceptance-probe')], { stdio: 'pipe' });
  fs.mkdirSync(root, { recursive: true });
  return fs.mkdtempSync(path.join(root, 'acceptance-'));
}

function snapshotTree(directory) {
  const files = [];
  function visit(current) {
    for (const entry of fs.readdirSync(current, { withFileTypes: true })
      .sort((a, b) => a.name.localeCompare(b.name))) {
      const file = path.join(current, entry.name);
      assert(!entry.isSymbolicLink(), 'Synthetic datasets must not traverse symbolic links.');
      if (entry.isDirectory()) visit(file);
      else {
        assert(entry.isFile(), 'Synthetic datasets contain only directories and regular files.');
        files.push({ path: path.relative(directory, file), bytes: fs.readFileSync(file).toString('hex') });
      }
    }
  }
  visit(directory);
  return files;
}

function checkpointDriver(directory) {
  assert(path.isAbsolute(directory) && path.basename(directory).startsWith('acceptance-') &&
    directory.split(path.sep).includes('.scratch'), 'Use an exclusively allocated acceptance workspace.');
  const file = path.join(directory, 'checkpoint.json');
  return {
    file,
    run(input, io = fs) { return checkpoint(file, input, io); },
    read() { return checkpoint(file, { operation: 'read' }); }
  };
}

function failingIo(method, file) {
  assert(['writeFileSync', 'renameSync'].includes(method), 'Unsupported checkpoint failure seam.');
  let calls = 0;
  const io = Object.create(fs);
  io[method] = (...args) => {
    const target = method === 'renameSync' ? args[1] : args[0];
    if (method === 'renameSync' ? target === file :
      typeof target === 'string' && target.startsWith(`${file}.`) && target.endsWith('.tmp')) {
      calls++;
      const error = new Error(`Synthetic checkpoint ${method} failure.`);
      error.code = 'EIO';
      throw error;
    }
    return fs[method](...args);
  };
  return { io, calls: () => calls };
}

function lifecycleEvent(state, id, command, evidence) {
  const event = {
    id, type: 'lifecycle', command: structuredClone(command),
    expected: registry.lifecycleRevisions(state, command),
    evidence: structuredClone(evidence)
  };
  const proofDigest = registry.lifecycleProofDigest(event);
  if (event.evidence.human !== null) event.evidence.human.proofDigest = proofDigest;
  for (const holder of event.evidence.holders) holder.acknowledgment.proofDigest = proofDigest;
  return event;
}

function applyLifecycle(state, event, context) {
  const before = structuredClone({ state, event, context });
  const result = applyEvent(state, event, context);
  assert.deepEqual({ state, event, context }, before, 'Public lifecycle input must remain immutable.');
  registry.validate(result.state);
  assert.equal(result.state.version, 10);
  assert(Array.isArray(result.actions), 'Public reducer must return an explicit action list.');
  return result;
}

function sourceDirectory(directory, worktree) {
  assert(path.isAbsolute(directory) && path.basename(directory).startsWith('acceptance-') &&
    directory.split(path.sep).includes('.scratch'), 'An allocated synthetic workspace is required.');
  assert(typeof worktree === 'string' && path.win32.isAbsolute(worktree) &&
    path.win32.normalize(worktree) === worktree, 'A canonical synthetic Windows source is required.');
  const root = path.win32.join('Q:\\Synthetic', path.basename(directory));
  const relative = path.win32.relative(root, worktree);
  assert(relative && !path.win32.isAbsolute(relative) &&
    !relative.split('\\').includes('..'), 'Synthetic source must be inside the allocated dataset.');
  return path.join(directory, ...relative.split('\\'));
}

function writeSyntheticSource(directory, state, request, context, content) {
  const worktree = sourceDirectory(directory, request.source.worktree);
  assert.equal(typeof content, 'string', 'Synthetic source content must be explicit.');
  assert(request.paths.length > 0, 'Synthetic source operations must name their paths.');
  registry.canonicalPaths(request.paths, true);
  const result = registry.validateSourceGrant(state, request, context);
  assert.deepEqual(result, { valid: true, executionAuthorized: false,
    grantId: request.grantId, grantRevision: request.grantRevision });
  for (const name of request.paths) {
    const file = path.join(worktree, ...name.split('\\'));
    let ancestor = file;
    while (!fs.existsSync(ancestor)) ancestor = path.dirname(ancestor);
    assert.equal(fs.realpathSync.native(ancestor).toLowerCase(), ancestor.toLowerCase(),
      'Synthetic writes must not traverse redirected paths.');
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.writeFileSync(file, content);
  }
  return result;
}

function lifecycleDriver(initial) {
  let state = structuredClone(initial);
  let serial = state.history.length;
  const epochs = new Map();
  const legacy = state.origin.legacy;
  if (legacy) {
    for (const item of legacy.items) {
      const fences = [
        ...(item.scopeAmendments ?? []).map(record => record.proof.reconciliation?.fence),
        ...(legacy.recoveries ?? []).filter(record => record.event.proof.key === item.key)
          .map(record => record.event.proof.reconciliation.fence),
        ...(legacy.supersessions ?? []).flatMap(record => record.event.proof.holders)
          .filter(holder => holder.key === item.key).map(holder => holder.reconciliation?.fence)
      ].filter(Boolean);
      if (fences.length) epochs.set(`legacy:${item.key}`, Math.max(...fences.map(fence => fence.epoch)));
    }
  }
  for (const record of state.history) {
    for (const holder of record.event.evidence.holders) epochs.set(holder.grantId, holder.fence.epoch);
  }
  function prepare(command, holderIds = []) {
    const projection = registry.lifecycleProjection(state);
    const id = `synthetic-acceptance-${++serial}`;
    const ids = [...holderIds].sort();
    const bookkeeping = ['register', 'publish', 'refresh', 'ambiguous', 'pause'].includes(command.kind);
    const evidence = {
      human: bookkeeping ? null :
        { id: `${id}-human`, actor: 'synthetic-human', proofDigest: '', receipt: `synthetic:${id}-decision` },
      holders: ids.map(grantId => {
        const grant = projection.grants.find(value => value.id === grantId);
        assert(grant, `An explicit current projected holder is required: ${grantId}`);
        const epoch = (epochs.get(grantId) ?? 0) + 1;
        epochs.set(grantId, epoch);
        const prior = state.history.flatMap(record => record.event.evidence.holders)
          .filter(holder => holder.grantId === grantId).flatMap(holder => holder.preservation.commits);
        const work = projection.works.find(work => work.key === grant.key);
        const legacyItem = legacy?.items.find(item => item.key === grant.key);
        const legacyProofs = legacyItem ? [
          ...(legacyItem.scopeHistory ?? []).map(record => record.proof),
          ...(legacyItem.scopeAmendments ?? []).map(record => record.proof),
          ...(legacy.recoveries ?? []).filter(record => record.event.proof.key === grant.key)
            .map(record => record.event.proof),
          ...(legacy.supersessions ?? []).flatMap(record => record.event.proof.holders)
            .filter(holder => holder.key === grant.key && holder.coordinator === legacy.coordinator)
        ] : [];
        const heads = [...new Set([grant.head, grant.source?.head,
          ...(command.kind === 'compatible' && command.grantId === grantId ? [command.source?.head] : []),
          ...work.publications.map(publication => publication.remoteHead),
          ...legacyProofs.flatMap(proof => [proof.source?.head,
            ...(proof.preservation?.commits ?? []).map(commit => commit.head)]),
          ...prior.map(commit => commit.head)].filter(Boolean))];
        const commits = prior.filter((commit, index) => prior.findIndex(value =>
          value.head === commit.head && value.ref === commit.ref) === index).map(commit => ({ ...commit }));
        for (const head of heads) {
          if (!commits.some(commit => commit.head === head)) {
            commits.push({ head, ref: `refs/synthetic/${grantId.replaceAll(':', '-')}/${head}` });
          }
        }
        return {
          grantId, owner: grant.owner, writingStopped: true, commandsRunning: false, workJoined: true,
          pendingInstructions: 'UNKNOWN', processingInstructions: 'UNKNOWN',
          fence: { epoch, nonce: `${id}-${grantId}-fence`, established: true,
            revokesEarlierSourceAuthority: true },
          preservation: { actual: [...grant.scopes], planned: [...grant.scopes],
            committed: heads.length ? [...grant.scopes] : [], restored: [],
            retained: [...grant.scopes], unpreserved: [],
            commits,
            bytesPreserved: true, refsPreserved: true, historyPreserved: true,
            receipt: `synthetic:${id}-${grantId}-preservation` },
          acknowledgment: { id: `${id}-${grantId}-ack`, proofDigest: '',
            receipt: `synthetic:${id}-${grantId}-response` }
        };
      }),
      receipt: `synthetic:${id}-complete-input`
    };
    const event = lifecycleEvent(state, id, command, evidence);
    const context = { lifecycleEvidence: {
      kind: 'host-verified-lifecycle', eventDigest: registry.digest(event),
      coordinator: projection.coordinator, ledgerAuthorityVerified: true,
      humanAuthorizationVerified: !bookkeeping, holdersVerified: ids,
      sourceOwnershipVerified: true, deliveryVerified: true, preservationVerified: true,
      receipt: `synthetic:${id}-structural-readback-not-authentication`
    } };
    return { event, context };
  }
  return {
    get state() { return state; },
    prepare,
    accept({ event, context }) {
      const result = applyLifecycle(state, event, context);
      state = result.state;
      return result;
    },
    apply(command, holderIds = []) { return this.accept(prepare(command, holderIds)); },
    projection() { return registry.lifecycleProjection(state); }
  };
}

function grantRequest(state, grantId, paths) {
  const projection = registry.lifecycleProjection(state);
  const grant = projection.grants.find(value => value.id === grantId);
  assert(grant?.source && grant.owner, 'A real projected attached grant is required.');
  return { grantId, grantRevision: grant.revision, owner: grant.owner,
    source: structuredClone(grant.source), head: projection.head, paths: [...paths] };
}

function rebindLifecycle(prepared) {
  const binding = registry.lifecycleProofDigest(prepared.event);
  if (prepared.event.evidence.human) prepared.event.evidence.human.proofDigest = binding;
  for (const holder of prepared.event.evidence.holders) holder.acknowledgment.proofDigest = binding;
  prepared.context.lifecycleEvidence.eventDigest = registry.digest(prepared.event);
  return prepared;
}

function legacyVersionScenario(version) {
  assert(Number.isInteger(version) && version >= 1 && version <= 9);
  const scenario = legacyScenario();
  let state = replay(scenario.events.slice(0, version === 5 || version === 9 ? 4 : 3),
    createState(scenario.coordinator, scenario.head, 'delivery')).state;
  const apply = (event, context = {}) => { state = applyEvent(state, event, context).state; };
  const item = () => state.items.find(value => value.key === scenario.key);
  const reservation = value => Object.fromEntries(['outcome', 'kind', 'pool', 'head', 'pr']
    .map(name => [name, value[name]]));
  const source = value => ({ owner: value.owner, worktree: 'Q:\\Synthetic\\LegacySource',
    branch: 'synthetic/legacy', head: value.head, receipt: 'synthetic:source' });
  const fence = { epoch: 1, nonce: 'synthetic:legacy-fence', revokesEarlierSourceAuthority: true,
    established: true, receipt: 'synthetic:fence' };
  const reconciliation = { kind: 'human-approved-source-fence', pendingInstructions: 'UNKNOWN',
    processingInstructions: 'UNKNOWN', fence, receipt: 'synthetic:reconciliation' };
  const acknowledgment = proof => ({ id: 'synthetic:legacy-owner-ack', receipt: 'synthetic:response',
    owner: proof.owner, key: proof.key, scopeRevision: proof.scopeRevision,
    proofDigest: registry.digest(proof) });
  const host = (proof, ack, authorization, kind) => ({
    kind, coordinator: state.coordinator, checkpointRevision: proof.checkpointRevision,
    proofDigest: registry.digest(proof), ownerResponseId: ack.id, humanDecisionId: authorization.id,
    humanDecisionDigest: registry.digest(authorization), sourceIdentityVerified: true,
    sourcePauseVerified: true, commandsJoinedVerified: true, instructionAuthorityVerified: true,
    receipt: 'synthetic:independent-readback'
  });
  const preservation = paths => ({ complete: true, commits: [],
    retained: paths.map(path => ({ path, receipt: 'synthetic:retained' })), unpreserved: [] });

  if (version === 2) {
    const value = item();
    const proof = { version: 1, owner: value.owner, key: value.key,
      reservation: reservation(value), publication: null, scopeRevision: 0,
      fromScopes: value.scopes, toScopes: [value.scopes[0]], phase: 'running', publicationHead: null,
      manifest: { complete: true, actual: [], planned: [value.scopes[0]] },
      preservation: preservation([]), stoppedWriting: true, expansionFrozen: true,
      pendingInstructions: 0, receipt: 'synthetic:refinement' };
    apply({ id: 'synthetic-refinement', type: 'refine-scopes', owner: value.owner,
      key: value.key, expectedScopeRevision: 0, proof, acknowledgment: acknowledgment(proof) });
  }
  if (version === 3 || version === 8) {
    const value = item();
    const additions = ['synthetic\\extension.cjs'];
    const proof = { version: version === 8 ? 2 : 1, owner: value.owner, key: value.key,
      reservation: reservation(value), scopeRevision: 0, fromScopes: value.scopes,
      additionalScopes: additions, toScopes: [...value.scopes, ...additions], phase: 'running',
      sourceWritesRetired: false, manifest: { complete: true, actual: [], planned: additions },
      preservation: preservation([]),
      quiescence: version === 8 ? { paused: true, commandsRunning: false, workJoined: true,
        receipt: 'synthetic:pause-join' } : { paused: true, pendingInstructions: 0, receipt: 'synthetic:pause' },
      unionFrozen: true, exclusivity: { complete: true,
        reservations: registry.heldReservations(state, value.key), receipt: 'synthetic:exclusivity' },
      receipt: 'synthetic:extension' };
    if (version === 8) Object.assign(proof, { repository: 'roryprimrose/Kora',
      coordinator: state.coordinator, checkpointRevision: 1, head: state.head,
      source: source(value), expectedItemDigest: registry.digest(value), reconciliation });
    const ack = acknowledgment(proof);
    if (version === 8) Object.assign(ack, { workJoined: true, staleAuthorityRejected: true, fence });
    const event = { id: 'synthetic-extension', type: 'extend-scopes', key: value.key,
      owner: value.owner, expectedScopeRevision: 0, proof, acknowledgment: ack };
    const context = {};
    if (version === 8) {
      event.authorization = { kind: 'explicit-human-scope-extension-decision', id: 'synthetic:extension-human',
        actor: 'synthetic-human', decision: 'prospective-coverage-only',
        subjectDigest: registry.digest({ proof, acknowledgment: ack }), receipt: 'synthetic:decision' };
      context.revision = 1;
      context.extensionEvidence = host(proof, ack, event.authorization, 'host-verified-fenced-scope-extension');
    }
    apply(event, context);
  }
  if (version === 4 || version === 5 || version === 9) {
    apply({ ...scenario.events[1], id: 'synthetic-contested-candidate', key: 'synthetic-successor',
      outcome: 'Independent synthetic successor', kind: 'maintenance',
      scopes: version === 4 ? ['synthetic\\readmitted.cjs'] : [scenario.scopes[0]] });
    const candidate = state.items.find(value => value.key === 'synthetic-successor');
    if (version === 4) {
      apply({ id: 'synthetic-readmission', type: 'revalidate-candidate', owner: state.planner.owner,
        pass: state.planner.pass, key: candidate.key, expectedItemDigest: registry.digest(candidate),
        head: state.head, scopes: [...candidate.scopes, 'synthetic\\readmitted-test.cjs'],
        evidence: 'Synthetic newly verified complete manifest.', manifestComplete: true });
    } else {
      const value = item();
      const holder = { coordinator: state.coordinator, key: value.key, owner: value.owner,
        itemDigest: registry.digest(value), scopeRevision: 0, paths: candidate.scopes,
        manifest: { complete: true, actual: value.scopes, planned: value.scopes },
        preservation: { complete: true, commits: [{
          head: value.publication.remoteHead, paths: value.scopes, receipt: 'synthetic:published-commit'
        }], retained: [], unpreserved: [] },
        stopped: version === 9 ? { writingStopped: true, commandsRunning: false, workJoined: true,
          receipt: 'synthetic:join' } : { writingStopped: true, pendingInstructions: 0,
          sourceInstructionsCleared: true, receipt: 'synthetic:enumerated' },
        acknowledgment: { kind: 'owner', id: 'synthetic:handoff-ack', receipt: 'synthetic:response',
          proofDigest: '' } };
      if (version === 9) {
        Object.assign(holder, { source: source(value), reconciliation });
        Object.assign(holder.acknowledgment, { owner: value.owner, workJoined: true,
          staleAuthorityRejected: true, fence });
      }
      const proof = { version: version === 9 ? 2 : 1, repository: 'roryprimrose/Kora',
        coordinator: state.coordinator, checkpointRevision: 1, head: state.head, mode: 'activate-local',
        authority: { singleLedger: true, receipt: 'synthetic:single-ledger' },
        exactFiles: { complete: true, paths: candidate.scopes, receipt: 'synthetic:exact-files' },
        candidate, expectedItemDigest: registry.digest(candidate), manifestComplete: true,
        localReservations: state.items.filter(value => !['candidate', 'complete'].includes(value.phase)),
        externalCheckpoints: [], holders: [holder] };
      if (version === 9) Object.assign(proof, { expectedStateDigest: registry.digest(state),
        effectiveReservations: registry.heldReservations(state, candidate.key),
        beneficiaryManifest: { complete: true, actual: [], planned: candidate.scopes } });
      holder.acknowledgment.proofDigest = registry.supersessionAcknowledgmentDigest(proof, holder);
      const authorization = { humanAuthorized: true, receipt: 'synthetic:decision',
        proofDigest: registry.digest(proof) };
      const context = { revision: 1 };
      if (version === 9) {
        Object.assign(authorization, { kind: 'explicit-human-reservation-handoff-decision',
          id: 'synthetic:handoff-human', actor: 'synthetic-human',
          decision: 'retire-prior-writers-for-exact-beneficiary' });
        context.supersessionEvidence = { kind: 'host-verified-reservation-handoff',
          coordinator: state.coordinator, checkpointRevision: 1, proofDigest: registry.digest(proof),
          humanDecisionId: authorization.id, humanDecisionDigest: registry.digest(authorization),
          humanAuthorizationVerified: true, ledgerAuthorityVerified: true, beneficiarySourceVerified: true,
          holders: [{ coordinator: holder.coordinator, key: holder.key, owner: holder.owner,
            itemDigest: holder.itemDigest, ownerResponseId: holder.acknowledgment.id,
            sourceDigest: registry.digest(holder.source),
            preservationDigest: registry.digest({ manifest: holder.manifest, preservation: holder.preservation }),
            fenceDigest: registry.digest(fence), sourceIdentityVerified: true, preservationVerified: true,
            sourceStoppedVerified: true, commandsJoinedVerified: true, instructionAuthorityVerified: true,
            receipt: 'synthetic:holder-readback' }], receipt: 'synthetic:handoff-readback' };
      }
      apply({ id: 'synthetic-supersession', type: 'supersede-write-reservations', proof, authorization }, context);
    }
  }
  if (version === 6 || version === 7) {
    const value = item(), additions = ['synthetic\\incident.cjs'];
    const paths = [...value.scopes, ...additions];
    const proof = { version: 1, repository: 'roryprimrose/Kora', coordinator: state.coordinator,
      checkpointRevision: 1, head: state.head, source: source(value), owner: value.owner, key: value.key,
      expectedItemDigest: registry.digest(value), reservation: reservation(value), scopeRevision: 0,
      fromScopes: value.scopes, additionalScopes: additions, toScopes: paths,
      exactFiles: { complete: true, paths: additions, receipt: 'synthetic:exact-files' },
      incident: { id: 'synthetic:incident', owner: value.owner, key: value.key,
        command: { id: 'synthetic:incident-command', command: 'Synthetic unauthorized-source observation',
          receipt: 'synthetic:observed-provenance' }, originalHead: value.head,
        originalScopes: value.scopes, unauthorizedPaths: additions, receipt: 'synthetic:incident' },
      manifest: { complete: true, actual: paths, planned: paths }, preservation: preservation(paths),
      workingTree: { complete: true, dirty: paths, restored: [], receipt: 'synthetic:working-tree' },
      quiescence: { sourcePaused: true, commandsRunning: false, workJoined: true, receipt: 'synthetic:join' },
      reconciliation, exclusivity: { complete: true,
        reservations: registry.heldReservations(state, value.key), receipt: 'synthetic:exclusive' } };
    const ack = { ...acknowledgment(proof), workJoined: true, staleAuthorityRejected: true, fence };
    const authorization = { kind: 'explicit-human-recovery-decision', id: 'synthetic:recovery-human',
      actor: 'synthetic-human', decision: 'prospective-coverage-only',
      subjectDigest: registry.digest({ proof, acknowledgment: ack }), receipt: 'synthetic:human-decision' };
    apply({ id: 'synthetic-recovery', type: 'recover-scope-incident', proof, acknowledgment: ack, authorization },
      { revision: 1, recoveryEvidence: host(proof, ack, authorization, 'host-verified-scope-incident') });
    if (version === 7) {
      const grant = state.recoveries.at(-1);
      apply({ id: 'synthetic-main-refresh', type: 'refresh', head: 'c'.repeat(40) });
      const current = item();
      const renewal = { version: 1, repository: 'roryprimrose/Kora', coordinator: state.coordinator,
        checkpointRevision: 2, head: state.head, key: current.key, owner: current.owner,
        grantDigest: registry.digest(grant), previousRenewalDigest: null, expectedItemDigest: registry.digest(current),
        scopeRevision: current.scopeRevision, scopes: current.scopes, source: proof.source, fence,
        quiescence: { sourcePaused: true, commandsRunning: false, workJoined: true,
          pendingInstructions: 'UNKNOWN', processingInstructions: 'UNKNOWN', receipt: 'synthetic:renewal-join' },
        exclusivity: { complete: true, reservations: registry.heldReservations(state, current.key),
          receipt: 'synthetic:exclusive' },
        compatibility: { baseHead: proof.head, fromHead: proof.head, toHead: state.head,
          ancestorVerified: true, complete: true, changedPaths: ['synthetic\\unrelated.cjs'],
          scopeReviewCompatible: true, sourceReviewCompatible: true, receipt: 'synthetic:compatibility' } };
      const response = { ...acknowledgment(renewal), id: 'synthetic:renewal-ack',
        workJoined: true, staleAuthorityRejected: true, fence };
      const evidence = { kind: 'host-verified-recovery-renewal', coordinator: state.coordinator,
        checkpointRevision: 2, proofDigest: registry.digest(renewal), ownerResponseId: response.id,
        compatibilityDigest: registry.digest(renewal.compatibility), sourceIdentityVerified: true,
        sourcePauseVerified: true, commandsJoinedVerified: true, instructionAuthorityVerified: true,
        compatibilityVerified: true, receipt: 'synthetic:renewal-readback' };
      apply({ id: 'synthetic-renewal', type: 'renew-recovery-continuation',
        proof: renewal, acknowledgment: response }, { revision: 2, renewalEvidence: evidence });
    }
  }
  registry.validate(state);
  assert.equal(state.version, version, 'Public legacy events must produce the requested accepted schema.');
  return state;
}

module.exports = { legacyScenario, replay, createWorkspace, snapshotTree, checkpointDriver, failingIo,
  lifecycleEvent, applyLifecycle, sourceDirectory, writeSyntheticSource, lifecycleDriver, grantRequest, rebindLifecycle,
  legacyVersionScenario };
