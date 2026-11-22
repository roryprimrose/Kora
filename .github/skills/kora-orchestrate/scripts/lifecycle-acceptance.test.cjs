'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { test } = require('node:test');
const { createState, validate, heldReservations } = require('./registry.cjs');
const { createLifecycleState, lifecycleProjection, lifecycleRevisions, lifecycleProofDigest,
  previewLifecycleMigration, adoptLifecycle, validateSourceGrant } = require('./registry.cjs');
const { workerLaunch } = require('./worker-launch.cjs');
const { discoverPolicyTests } = require('../../../../eng/run-policy-tests.cjs');
const { legacyScenario, replay, createWorkspace, snapshotTree, checkpointDriver, failingIo,
  lifecycleEvent, writeSyntheticSource, lifecycleDriver, grantRequest, rebindLifecycle, legacyVersionScenario } =
  require('./lifecycle-acceptance-fixture.cjs');

const repository = path.resolve(__dirname, '..', '..', '..', '..');
const scratch = process.env.KORA_LIFECYCLE_ACCEPTANCE_SCRATCH ??
  path.join(repository, '.scratch', 'orchestration-lifecycle');

function allocate() {
  const directory = createWorkspace(scratch);
  console.log(`Preserved synthetic lifecycle dataset: ${directory}`);
  return directory;
}

function initial(scenario) {
  return createState(scenario.coordinator, scenario.head, 'delivery');
}

test('legacy public replay preserves its inputs and publication still holds the reservation', () => {
  const scenario = legacyScenario();
  const before = structuredClone(scenario);
  const state = initial(scenario);
  const original = structuredClone(state);
  const result = replay(scenario.events, state);
  validate(result.state);
  assert.deepEqual(scenario, before);
  assert.deepEqual(state, original);
  assert.equal(result.state.items[0].phase, 'published');
  assert.deepEqual(heldReservations(result.state).find(item => item.key === scenario.key).scopes,
    scenario.scopes);
  assert.equal(result.steps.flatMap(step => step.actions).filter(action => action.type === 'launch').length, 1);
});

test('legacy launch driver consumes the real reservation and rejects absent publication authority', () => {
  const scenario = legacyScenario();
  const { state, steps } = replay(scenario.events.slice(0, 2), initial(scenario));
  const action = steps.flatMap(step => step.actions).find(action => action.type === 'launch');
  assert(action, 'The public reducer must supply the launch action.');
  const settings = { repository: 'roryprimrose/Kora', publicationAuthorized: true,
    controlWorkspace: 'Q:\\Synthetic\\Control', productionWorkspace: 'Q:\\Synthetic\\Source',
    scratch: 'Q:\\Synthetic\\Control\\.scratch\\acceptance', expectedPublisher: 'synthetic-publisher' };
  const before = structuredClone(state);
  const launch = workerLaunch(state, action, settings);
  assert.equal(launch.worktree, true);
  assert(launch.prompt.includes(scenario.key));
  assert.throws(() => workerLaunch(state, action, { ...settings, publicationAuthorized: false }),
    /Explicit repository publication authority/);
  const attached = replay([scenario.events[2]], state).state;
  assert.throws(() => workerLaunch(attached, action, settings), /already attached/);
  assert.deepEqual(state, before);
});

test('public checkpoint persists legacy events and restart without deleting retained synthetic files', () => {
  const scenario = legacyScenario();
  const directory = allocate();
  const retained = path.join(directory, 'retained-worktrees');
  for (const actor of ['a', 'b', 'c']) {
    const worktree = path.join(retained, actor);
    fs.mkdirSync(worktree, { recursive: true });
    fs.writeFileSync(path.join(worktree, 'shared.cjs'), `synthetic retained bytes for ${actor}\n`, { flag: 'wx' });
  }
  const bytes = snapshotTree(retained);
  const driver = checkpointDriver(directory);
  let value = driver.run({ operation: 'init', expectedRevision: 0,
    coordinator: scenario.coordinator, head: scenario.head, mode: 'delivery' });
  for (const event of scenario.events) {
    value = driver.run({ operation: 'apply', expectedRevision: value.revision, event });
  }
  assert.deepEqual(value.state, replay(scenario.events, initial(scenario)).state);
  assert.deepEqual(checkpointDriver(directory).read(), value);
  assert.deepEqual(value.events.map(event => event.id), scenario.events.map(event => event.id));
  assert.equal(value.actions.filter(action => action.command.type === 'launch').length, 1);
  assert.deepEqual(snapshotTree(retained), bytes);
});

test('public checkpoint refusals preserve exact bytes, history, actions and retained files', async t => {
  for (const failure of ['stale-revision', 'unknown-event', 'lock', 'writeFileSync', 'renameSync']) {
    await t.test(failure, () => {
      const scenario = legacyScenario();
      const directory = allocate();
      const driver = checkpointDriver(directory);
      const value = driver.run({ operation: 'init', expectedRevision: 0,
        coordinator: scenario.coordinator, head: scenario.head, mode: 'delivery' });
      fs.writeFileSync(path.join(directory, 'retained-source.cjs'), 'preserve synthetic source\n', { flag: 'wx' });
      const original = snapshotTree(directory);
      const input = { operation: 'apply', expectedRevision: value.revision, event: scenario.events[0] };
      if (failure === 'stale-revision') {
        assert.throws(() => driver.run({ ...input, expectedRevision: 0 }), /Stale checkpoint revision/);
      } else if (failure === 'unknown-event') {
        assert.throws(() => driver.run({ ...input, event: { id: 'unknown', type: 'not-an-event' } }),
          /Unknown event/);
      } else if (failure === 'lock') {
        const lock = `${driver.file}.lock`;
        const handle = fs.openSync(lock, 'wx');
        try {
          assert.throws(() => driver.run(input), { code: 'EEXIST' });
          assert(fs.existsSync(lock), 'The refused writer must not remove the other writer lock.');
        } finally {
          fs.closeSync(handle);
          fs.unlinkSync(lock);
        }
      } else {
        const fault = failingIo(failure, driver.file);
        assert.throws(() => driver.run(input, fault.io), { code: 'EIO' });
        assert.equal(fault.calls(), 1, 'The actual public persistence seam must reach the injected failure.');
      }
      assert.deepEqual(snapshotTree(directory), original);
      assert.deepEqual(driver.read(), value);
    });
  }
});

test('existing policy discovery selects acceptance once and never selects its fixture', () => {
  const { files } = discoverPolicyTests(repository);
  const relative = path.relative(repository, __filename);
  assert.equal(files.filter(file => file === relative).length, 1);
  assert(!files.includes(path.join(path.dirname(relative), 'lifecycle-acceptance-fixture.cjs')));
});

function emptyLifecycle() {
  const scenario = legacyScenario();
  return createLifecycleState(scenario.coordinator, scenario.head, 'delivery');
}

function registerCommand(key = 'synthetic-v10-work') {
  return { kind: 'register', key, outcome: `Bounded synthetic acceptance work: ${key}`,
    pool: 'ready', workKind: 'feature', evidence: 'Synthetic structural evidence only.', pr: null };
}

function syntheticDecision(id) {
  return { id, actor: 'synthetic-human', proofDigest: '', receipt: `synthetic:${id}` };
}

test('v10 empty state and read-only legacy preview never create execution authority', () => {
  const state = emptyLifecycle();
  const before = structuredClone(state);
  validate(state);
  const projection = lifecycleProjection(state);
  assert.equal(projection.version, 10);
  assert.deepEqual(projection.works, []);
  assert.deepEqual(projection.grants, []);
  assert.deepEqual(projection.reservations, []);
  assert.deepEqual(state.history, []);
  assert.equal(state.origin.legacy, null);
  assert.deepEqual(state, before);

  const scenario = legacyScenario();
  const legacy = replay(scenario.events, initial(scenario)).state;
  const original = structuredClone(legacy);
  const preview = previewLifecycleMigration(legacy);
  assert.equal(preview.activating, false);
  assert.equal(preview.version, 10);
  assert.deepEqual(legacy, original);
  const published = preview.projection.works.find(work => work.key === scenario.key);
  assert(published, 'Preview must retain legacy work.');
  assert.deepEqual(published.publications, [legacy.items[0].publication]);
  const held = preview.projection.reservations.filter(value => value.key === scenario.key);
  assert(held.length > 0, 'An unresolved legacy exclusion must remain held.');
  assert(held.every(value => value.status === 'HELD'));
  assert.deepEqual([...new Set(held.flatMap(value => value.scopes))].sort(), [...scenario.scopes].sort());
  assert(preview.projection.grants.filter(grant => grant.key === scenario.key)
    .every(grant => grant.status !== 'active'), 'Preview cannot activate a legacy source grant.');
});

test('v10 exact proof binds affected revisions and evidence but omits only circular proof digests', () => {
  const state = emptyLifecycle();
  const event = lifecycleEvent(state, 'synthetic-proof', registerCommand(), {
    human: syntheticDecision('synthetic-decision'), holders: [], receipt: 'synthetic:event'
  });
  const digest = lifecycleProofDigest(event);
  assert.match(digest, /^[0-9a-f]{64}$/);
  assert.equal(event.evidence.human.proofDigest, digest);
  const circular = structuredClone(event);
  circular.evidence.human.proofDigest = 'different-circular-value';
  assert.equal(lifecycleProofDigest(circular), digest);
  for (const mutate of [
    value => { value.id = 'changed-id'; },
    value => { value.command.evidence = 'changed-command-evidence'; },
    value => { value.evidence.receipt = 'changed-receipt'; },
    value => { value.evidence.human.id = 'changed-human-id'; },
    value => { value.evidence.human.actor = 'changed-actor'; },
    value => { value.evidence.human.receipt = 'changed-human-receipt'; },
    value => { value.expected.authorityRevision++; },
    value => { value.expected.sourceRevision++; }
  ]) {
    const changed = structuredClone(event);
    mutate(changed);
    assert.notEqual(lifecycleProofDigest(changed), digest);
  }
  assert.deepEqual(event.expected, lifecycleRevisions(state, event.command));
});

test('v10 holder acknowledgment binds source preservation and established cooperative fence', () => {
  const state = emptyLifecycle();
  const holder = {
    grantId: 'synthetic-held-grant', owner: 'synthetic:owner',
    writingStopped: true, commandsRunning: false, workJoined: true,
    pendingInstructions: 'UNKNOWN', processingInstructions: 'UNKNOWN',
    fence: { epoch: 1, nonce: 'synthetic-fence', established: true,
      revokesEarlierSourceAuthority: true },
    preservation: { actual: ['synthetic\\shared.cjs'], planned: ['synthetic\\shared.cjs'],
      committed: [], restored: [], retained: ['synthetic\\shared.cjs'], unpreserved: [],
      commits: [], bytesPreserved: true, refsPreserved: true, historyPreserved: true,
      receipt: 'synthetic:preservation' },
    acknowledgment: { id: 'synthetic-holder-ack', proofDigest: '', receipt: 'synthetic:ack' }
  };
  const event = lifecycleEvent(state, 'synthetic-holder-proof', registerCommand(), {
    human: syntheticDecision('synthetic-holder-decision'), holders: [holder],
    receipt: 'synthetic:proof-only'
  });
  const digest = lifecycleProofDigest(event);
  assert.equal(event.evidence.holders[0].acknowledgment.proofDigest, digest);
  const circular = structuredClone(event);
  circular.evidence.holders[0].acknowledgment.proofDigest = 'changed-circular-value';
  assert.equal(lifecycleProofDigest(circular), digest);
  for (const mutate of [
    value => { value.grantId = 'another-grant'; },
    value => { value.owner = 'another-owner'; },
    value => { value.pendingInstructions = 0; },
    value => { value.processingInstructions = 0; },
    value => { value.fence.epoch++; },
    value => { value.fence.nonce = 'changed-fence'; },
    value => { value.fence.established = false; },
    value => { value.preservation.retained = []; },
    value => { value.preservation.receipt = 'changed-preservation'; },
    value => { value.acknowledgment.id = 'changed-ack-id'; },
    value => { value.acknowledgment.receipt = 'changed-ack-receipt'; }
  ]) {
    const changed = structuredClone(event);
    mutate(changed.evidence.holders[0]);
    assert.notEqual(lifecycleProofDigest(changed), digest);
  }
});

test('v10 missing host authority cannot register, adopt or write synthetic source', () => {
  const state = emptyLifecycle();
  const before = structuredClone(state);
  const event = lifecycleEvent(state, 'synthetic-missing-authority', registerCommand(), {
    human: null, holders: [], receipt: 'synthetic:no-host-authority'
  });
  assert.throws(() => replay([event], state, {}), Error);
  assert.deepEqual(state, before);
  assert.deepEqual(lifecycleProjection(state).works, []);

  const scenario = legacyScenario();
  const legacy = replay(scenario.events, initial(scenario)).state;
  const legacyBefore = structuredClone(legacy);
  assert.throws(() => adoptLifecycle(legacy, {
    id: 'synthetic-invalid-adoption', type: 'lifecycle',
    command: { kind: 'adopt', legacyDigest: previewLifecycleMigration(legacy).legacyDigest },
    evidence: { human: null, holders: [], receipt: 'synthetic:missing-expected-and-host' }
  }, {}), Error);
  assert.deepEqual(legacy, legacyBefore);

  const directory = allocate();
  const request = { grantId: 'synthetic-nonexistent-grant', grantRevision: 1,
    owner: 'synthetic:owner', source: { worktree: path.join(directory, 'source-a'),
      branch: 'synthetic-branch-a', head: scenario.head },
    head: scenario.head, paths: ['synthetic\\shared.cjs'] };
  const bytes = snapshotTree(directory);
  assert.throws(() => validateSourceGrant(state, request, {
    sourceAuthorityVerified: true, receipt: 'synthetic:structural-authority'
  }), Error);
  assert.throws(() => writeSyntheticSource(directory, state, request, {
    sourceAuthorityVerified: true, receipt: 'synthetic:structural-authority'
  }, 'must never be written'), Error);
  assert.deepEqual(snapshotTree(directory), bytes);
  assert.deepEqual(state, before);
});

test('v10 checkpoint init is explicit, restart is read-only and malformed history is refused', () => {
  const directory = allocate();
  const driver = checkpointDriver(directory);
  const scenario = legacyScenario();
  const value = driver.run({ operation: 'init', expectedRevision: 0,
    coordinator: scenario.coordinator, head: scenario.head, mode: 'delivery', lifecycleVersion: 10 });
  assert.equal(value.version, 1);
  assert.equal(value.state.version, 10);
  assert.deepEqual(value.state, emptyLifecycle());
  assert.deepEqual(value.actions, []);
  assert.deepEqual(value.events, []);
  const before = snapshotTree(directory);
  assert.deepEqual(checkpointDriver(directory).read(), value);
  assert.deepEqual(snapshotTree(directory), before);
  const tampered = structuredClone(value);
  tampered.state.history.push({
    event: lifecycleEvent(value.state, 'synthetic-forged-history', registerCommand(), {
      human: null, holders: [], receipt: 'synthetic:forged-history'
    }),
    hostEvidence: null,
    digest: '0'.repeat(64)
  });
  fs.writeFileSync(driver.file, JSON.stringify(tampered));
  const tamperedBytes = snapshotTree(directory);
  assert.throws(() => driver.read(), Error);
  assert.deepEqual(snapshotTree(directory), tamperedBytes);
});

test('v10 checkpoint stale revision and missing authority preserve the complete file', () => {
  const directory = allocate();
  const driver = checkpointDriver(directory);
  const scenario = legacyScenario();
  const value = driver.run({ operation: 'init', expectedRevision: 0,
    coordinator: scenario.coordinator, head: scenario.head, mode: 'delivery', lifecycleVersion: 10 });
  const event = lifecycleEvent(value.state, 'synthetic-checkpoint-no-authority', registerCommand(), {
    human: null, holders: [], receipt: 'synthetic:no-host'
  });
  const bytes = snapshotTree(directory);
  assert.throws(() => driver.run({ operation: 'apply', expectedRevision: 0, event }),
    /Stale checkpoint revision/);
  assert.deepEqual(snapshotTree(directory), bytes);
  assert.throws(() => driver.run({ operation: 'apply', expectedRevision: value.revision, event }), Error);
  assert.deepEqual(snapshotTree(directory), bytes);
  assert.deepEqual(driver.read(), value);
});

test('v10 checkpoint initialization failures preserve retained bytes and never leave a grant', async t => {
  for (const failure of ['writeFileSync', 'renameSync']) {
    await t.test(failure, () => {
      const directory = allocate();
      const driver = checkpointDriver(directory);
      fs.writeFileSync(path.join(directory, 'retained.cjs'), 'retained synthetic bytes\n', { flag: 'wx' });
      const before = snapshotTree(directory);
      const scenario = legacyScenario();
      const fault = failingIo(failure, driver.file);
      assert.throws(() => driver.run({ operation: 'init', expectedRevision: 0,
        coordinator: scenario.coordinator, head: scenario.head, mode: 'delivery',
        lifecycleVersion: 10 }, fault.io), { code: 'EIO' });
      assert.equal(fault.calls(), 1);
      assert.deepEqual(snapshotTree(directory), before);
      assert(!fs.existsSync(driver.file), 'Failed initialization cannot persist execution authority.');
    });
  }
});

test('legacy checkpoint preview does not adopt, rewrite bytes or acknowledge pending launch actions', () => {
  const directory = allocate();
  const driver = checkpointDriver(directory);
  const scenario = legacyScenario();
  let value = driver.run({ operation: 'init', expectedRevision: 0,
    coordinator: scenario.coordinator, head: scenario.head, mode: 'delivery' });
  for (const event of scenario.events) {
    value = driver.run({ operation: 'apply', expectedRevision: value.revision, event });
  }
  const bytes = snapshotTree(directory);
  const preview = driver.run({ operation: 'preview-lifecycle' });
  assert.equal(preview.activating, false);
  assert.deepEqual(preview, previewLifecycleMigration(value.state));
  assert.deepEqual(snapshotTree(directory), bytes);
  assert.deepEqual(driver.read(), value);
  assert(value.actions.some(action => action.command.type === 'launch' && action.status === 'pending'));
});

test('legacy preview retains accepted publication and cleanup shapes without invented receipts', () => {
  const scenario = legacyScenario();
  const proof = { remoteHead: scenario.head, stoppedWriting: true,
    worktreeAbsent: true, branchAbsent: true, pendingInstructions: 0 };
  const legacy = replay([...scenario.events, {
    id: 'legacy-completed', type: 'completed', key: scenario.key, owner: scenario.owner, proof
  }], initial(scenario)).state;
  const before = structuredClone(legacy);
  const preview = previewLifecycleMigration(legacy);
  const work = preview.projection.works.find(value => value.key === scenario.key);
  assert(work, 'Completed legacy work remains immutable history.');
  assert.deepEqual(work.publications, [legacy.items[0].publication]);
  assert.deepEqual(work.cleanup, proof);
  assert.deepEqual(legacy, before);
  assert.equal(preview.activating, false);
});

const sourceAuthority = { sourceAuthorityVerified: true, receipt: 'synthetic:source-observation' };

function attachExecution(driver, directory, key, grantId, scopes, transfers = [], holders = []) {
  driver.apply(registerCommand(key));
  assert.deepEqual(driver.apply({ kind: 'allocate', key, grantId, scopes, transfers }, holders).actions, []);
  const launch = driver.apply({ kind: 'dispatch', grantId }).actions;
  assert.equal(launch.length, 1);
  assert.equal(launch[0].grantId, grantId);
  const controlWorkspace = 'Q:\\Synthetic\\lifecycle-control';
  const settings = { repository: 'roryprimrose/Kora', publicationAuthorized: true,
    controlWorkspace, productionWorkspace: 'Q:\\Synthetic\\lifecycle-production',
    scratch: path.win32.join(controlWorkspace, '.scratch', 'receipts'), expectedPublisher: 'synthetic-publisher' };
  const prompt = workerLaunch(driver.state, launch[0], settings);
  assert(prompt.prompt.includes(grantId), 'Public launch must carry exact source grant identity.');
  const source = { worktree: path.join(directory, key), branch: `synthetic/${key}`, head: 'a'.repeat(40) };
  driver.apply({ kind: 'attach', grantId, owner: `synthetic:${key}`, source });
  return grantRequest(driver.state, grantId, scopes);
}

function publication(grantId, pr) {
  return { kind: 'publish', grantId, publication: {
    pushedHead: 'b'.repeat(40), remoteHead: 'b'.repeat(40), pr, ready: true,
    enrolled: true, receipt: `synthetic:publication-${pr}`
  } };
}

function retireAndClose(driver, key, grantId, outcome = 'delivered', releases = [], allGrants = [grantId]) {
  driver.apply({ kind: 'revoke', grantId, reason: 'Synthetic actual pause/join and source fence.' }, [grantId]);
  assert.deepEqual(driver.apply({ kind: 'close', key, outcome, releases }, allGrants).actions, []);
}

function transferFor(driver, key, paths) {
  const reservation = driver.projection().reservations.find(value => value.key === key &&
    paths.every(name => value.scopes.includes(name)));
  assert(reservation, 'Exact current custody must exist before a transfer.');
  return { reservationId: reservation.id, paths: [...paths] };
}

function refusesUnchanged(driver, prepared, pattern = Error) {
  const before = structuredClone(driver.state);
  assert.throws(() => driver.accept(prepared), pattern);
  assert.deepEqual(driver.state, before);
}

test('A to B to C executes under fresh grants while old grants and retained worktree bytes stay retired', () => {
  const directory = allocate();
  const driver = lifecycleDriver(emptyLifecycle());
  const capacity = driver.projection().capacity.ready;
  const scopes = ['synthetic\\shared.cjs'];
  const retired = [];
  const snapshots = new Map();
  for (const [index, key] of ['a', 'b', 'c'].entries()) {
    const grantId = `execution-${key}`;
    const prior = ['a', 'b'][index - 1];
    const transfers = prior ? [transferFor(driver, prior, scopes)] : [];
    const history = structuredClone(driver.state.history);
    const request = attachExecution(driver, directory, key, grantId, scopes, transfers,
      prior ? [`execution-${prior}`] : []);
    assert.deepEqual(driver.state.history.slice(0, history.length), history);
    assert.equal(driver.projection().capacity.ready, capacity - 1);
    writeSyntheticSource(directory, driver.state, request, sourceAuthority, `${key}: synthetic work\n`);
    driver.apply(publication(grantId, 201 + index));
    assert.equal(driver.projection().capacity.ready, capacity - 1, 'Publication alone cannot free capacity.');
    assert.equal(driver.projection().grants.find(grant => grant.id === grantId).status, 'active');
    retireAndClose(driver, key, grantId);
    assert.equal(driver.projection().capacity.ready, capacity);
    assert.equal(driver.projection().works.find(work => work.key === key).cleanup, null);
    snapshots.set(key, snapshotTree(request.source.worktree));
    retired.push(request);
    const allBytes = snapshotTree(directory);
    for (const old of retired) {
      assert.throws(() => writeSyntheticSource(directory, driver.state, old, sourceAuthority, 'forbidden'), Error);
    }
    assert.deepEqual(snapshotTree(directory), allBytes, 'Retired grant refusal must precede every source effect.');
    for (const [owner, bytes] of snapshots) assert.deepEqual(snapshotTree(path.join(directory, owner)), bytes);
  }
  assert.deepEqual(driver.projection().works.map(work => work.status), ['delivered', 'delivered', 'delivered']);
  assert(driver.projection().grants.every(grant => grant.status === 'revoked'));
  assert.deepEqual(driver.projection().reservations.map(held => held.key), ['c']);
});

test('partial two-holder transfer preserves unrelated custody and explicit retired remainder release', () => {
  const directory = allocate();
  const driver = lifecycleDriver(emptyLifecycle());
  const left = ['synthetic\\left.cjs', 'synthetic\\left-remainder.cjs'];
  const right = ['synthetic\\right.cjs', 'synthetic\\right-remainder.cjs'];
  for (const [key, scopes, pr] of [['left', left, 301], ['right', right, 302]]) {
    const request = attachExecution(driver, directory, key, `grant-${key}`, scopes);
    writeSyntheticSource(directory, driver.state, request, sourceAuthority, `${key}: preserved\n`);
    driver.apply(publication(`grant-${key}`, pr));
    retireAndClose(driver, key, `grant-${key}`);
  }
  const before = snapshotTree(directory);
  const files = [left[0], right[0]];
  attachExecution(driver, directory, 'successor', 'grant-successor', files,
    [transferFor(driver, 'left', [left[0]]), transferFor(driver, 'right', [right[0]])],
    ['grant-left', 'grant-right']);
  const held = driver.projection().reservations;
  assert.deepEqual(held.find(value => value.key === 'left').scopes, [left[1]]);
  assert.deepEqual(held.find(value => value.key === 'right').scopes, [right[1]]);
  assert.deepEqual(held.find(value => value.key === 'successor').scopes, files);
  assert.deepEqual(snapshotTree(directory), before);
  const release = { kind: 'release', releases: [transferFor(driver, 'left', [left[1]])] };
  refusesUnchanged(driver, driver.prepare(release), /holder/i);
  assert.deepEqual(driver.apply(release, ['grant-left']).actions, []);
  assert(!driver.projection().reservations.some(value => value.key === 'left'));
  assert.deepEqual(driver.projection().reservations.find(value => value.key === 'right').scopes, [right[1]]);
  assert.deepEqual(snapshotTree(directory), before);
});

test('fresh same-PR repair consumes repair capacity and requires its own latest publication', () => {
  const directory = allocate();
  const driver = lifecycleDriver(emptyLifecycle());
  const scopes = ['synthetic\\repair.cjs'];
  const original = attachExecution(driver, directory, 'repair-work', 'original-grant', scopes);
  writeSyntheticSource(directory, driver.state, original, sourceAuthority, 'original preserved\n');
  driver.apply(publication('original-grant', 401));
  retireAndClose(driver, 'repair-work', 'original-grant');
  const capacity = driver.projection().capacity;
  const repair = { kind: 'repair', key: 'repair-work', pr: 401, grantId: 'repair-grant',
    scopes, transfers: [transferFor(driver, 'repair-work', scopes)] };
  driver.apply(repair, ['original-grant']);
  assert.equal(driver.projection().capacity.repair, capacity.repair - 1);
  assert.equal(driver.projection().capacity.ready, capacity.ready);
  assert.equal(driver.projection().grants.find(value => value.id === 'repair-grant').generation, 2);
  driver.apply({ kind: 'dispatch', grantId: 'repair-grant' });
  driver.apply({ kind: 'attach', grantId: 'repair-grant', owner: original.owner,
    source: { ...original.source, head: 'b'.repeat(40) } });
  const request = grantRequest(driver.state, 'repair-grant', scopes);
  writeSyntheticSource(directory, driver.state, request, sourceAuthority, 'fresh same-PR repair\n');
  const branch = lifecycleDriver(driver.state);
  branch.apply({ kind: 'revoke', grantId: 'repair-grant', reason: 'Synthetic joined repair.' }, ['repair-grant']);
  refusesUnchanged(branch, branch.prepare({ kind: 'close', key: 'repair-work', outcome: 'delivered',
    releases: [] }, ['original-grant', 'repair-grant']), /closure/i);
  driver.apply(publication('repair-grant', 401));
  retireAndClose(driver, 'repair-work', 'repair-grant', 'delivered', [], ['original-grant', 'repair-grant']);
  assert.equal(driver.projection().works.length, 1);
  assert.deepEqual(driver.projection().works[0].publications.map(value => value.pr), [401, 401]);
  assert.equal(driver.projection().capacity.repair, capacity.repair);
  const bytes = snapshotTree(directory);
  assert.throws(() => writeSyntheticSource(directory, driver.state, original, sourceAuthority, 'old-grant'), Error);
  assert.deepEqual(snapshotTree(directory), bytes);
});

test('affected proof revisions and source compatibility invalidate stale grants but not unrelated bookkeeping', () => {
  const directory = allocate();
  const driver = lifecycleDriver(emptyLifecycle());
  const scopes = ['synthetic\\current.cjs'];
  const original = attachExecution(driver, directory, 'current', 'current-grant', scopes);
  const union = [...scopes, 'synthetic\\added.cjs'];
  const prepared = driver.prepare({ kind: 'amend', grantId: 'current-grant', scopes: union }, ['current-grant']);
  driver.apply(registerCommand('unrelated'));
  driver.accept(prepared);
  assert.throws(() => validateSourceGrant(driver.state, original, sourceAuthority), Error);
  const current = grantRequest(driver.state, 'current-grant', union);
  writeSyntheticSource(directory, driver.state, current, sourceAuthority, 'amended synthetic source\n');
  const stale = driver.prepare({ kind: 'revoke', grantId: 'current-grant', reason: 'Prepared before source change.' },
    ['current-grant']);
  driver.apply({ kind: 'refresh', head: 'c'.repeat(40) });
  refusesUnchanged(driver, stale, /Stale affected/);
  assert.throws(() => validateSourceGrant(driver.state, current, sourceAuthority), Error);
  const compatible = { kind: 'compatible', grantId: 'current-grant', head: 'c'.repeat(40),
    source: { ...current.source, head: 'd'.repeat(40) } };
  const bad = driver.prepare(compatible, ['current-grant']);
  bad.context.lifecycleEvidence.sourceOwnershipVerified = false;
  refusesUnchanged(driver, bad, /compatibility/i);
  driver.apply(compatible, ['current-grant']);
  const next = grantRequest(driver.state, 'current-grant', union);
  assert.equal(next.source.head, 'd'.repeat(40));
  assert.equal(next.head, 'c'.repeat(40));
  writeSyntheticSource(directory, driver.state, next, sourceAuthority, 'compatible advanced source\n');
  assert.throws(() => validateSourceGrant(driver.state, current, sourceAuthority), Error);
  assert(driver.state.history.some(record => record.event.command.kind === 'attach' &&
    record.event.command.source.head === original.source.head));
  const substitute = driver.prepare({ ...compatible, source: { ...compatible.source, branch: 'substitute' } },
    ['current-grant']);
  refusesUnchanged(driver, substitute, /attached source identity/);
});

test('missing preservation, changed evidence and UNKNOWN without a fence refuse authority without source effects', () => {
  const directory = allocate();
  const driver = lifecycleDriver(emptyLifecycle());
  const scopes = ['synthetic\\protected.cjs'];
  const request = attachExecution(driver, directory, 'protected', 'protected-grant', scopes);
  writeSyntheticSource(directory, driver.state, request, sourceAuthority, 'preserve all bytes\n');
  const bytes = snapshotTree(directory);
  for (const mutate of [
    value => { value.event.evidence.human = null; },
    value => { value.event.evidence.holders[0].fence.established = false; },
    value => { value.event.evidence.holders[0].workJoined = false; },
    value => { value.event.evidence.holders[0].preservation.unpreserved = scopes; },
    value => { value.event.evidence.holders[0].preservation.retained = []; },
    value => { value.context.lifecycleEvidence.humanAuthorizationVerified = false; },
    value => { value.context.lifecycleEvidence.preservationVerified = false; }
  ]) {
    const prepared = driver.prepare({ kind: 'revoke', grantId: 'protected-grant', reason: 'Synthetic stop.' },
      ['protected-grant']);
    mutate(prepared);
    rebindLifecycle(prepared);
    refusesUnchanged(driver, prepared);
    assert.deepEqual(snapshotTree(directory), bytes);
  }
  const staleDecision = driver.prepare({ kind: 'revoke', grantId: 'protected-grant', reason: 'Synthetic stop.' },
    ['protected-grant']);
  staleDecision.event.evidence.holders[0].preservation.receipt = 'changed after exact approval';
  staleDecision.context.lifecycleEvidence.eventDigest = require('./registry.cjs').digest(staleDecision.event);
  refusesUnchanged(driver, staleDecision, /human lifecycle decision/);
  assert.deepEqual(snapshotTree(directory), bytes);
});

test('cancel and abandon preserve custody; ambiguous launch and recovery never recreate retired execution', () => {
  const directory = allocate();
  const driver = lifecycleDriver(emptyLifecycle());
  const capacity = driver.projection().capacity.ready;
  for (const outcome of ['cancelled', 'abandoned']) {
    const scopes = [`synthetic\\${outcome}.cjs`];
    const request = attachExecution(driver, directory, outcome, `grant-${outcome}`, scopes);
    writeSyntheticSource(directory, driver.state, request, sourceAuthority, `${outcome}: retained\n`);
    retireAndClose(driver, outcome, `grant-${outcome}`, outcome);
    assert.equal(driver.projection().works.find(work => work.key === outcome).status, outcome);
    assert(driver.projection().reservations.some(value => value.key === outcome));
    assert.equal(driver.projection().capacity.ready, capacity);
  }
  driver.apply(registerCommand('ambiguous'));
  driver.apply({ kind: 'allocate', key: 'ambiguous', grantId: 'ambiguous-grant',
    scopes: ['synthetic\\ambiguous.cjs'], transfers: [] });
  driver.apply({ kind: 'dispatch', grantId: 'ambiguous-grant' });
  driver.apply({ kind: 'ambiguous', grantId: 'ambiguous-grant', reason: 'Synthetic uncertain launch result.' });
  refusesUnchanged(driver, driver.prepare({ kind: 'dispatch', grantId: 'ambiguous-grant' }), /dispatch/);
  assert.equal(driver.projection().capacity.ready, capacity - 1);
  driver.apply({ kind: 'pause', reason: 'Synthetic joined old coordinator.' }, ['ambiguous-grant']);
  const prefix = structuredClone(driver.state.history);
  driver.apply({ kind: 'recover', coordinator: 'synthetic:replacement-coordinator' });
  assert.equal(driver.projection().enabled, false);
  assert.equal(driver.projection().head, null);
  driver.apply({ kind: 'resume' });
  assert.equal(driver.projection().head, null, 'Resume cannot invent source freshness.');
  assert.deepEqual(driver.state.history.slice(0, prefix.length), prefix);
  assert.equal(lifecycleDriver(driver.state).projection().grants.find(grant => grant.id === 'ambiguous-grant').status,
    'revoked');
  driver.apply({ kind: 'refresh', head: 'a'.repeat(40) });
  refusesUnchanged(driver, driver.prepare({ kind: 'dispatch', grantId: 'ambiguous-grant' }), /dispatch/);
});

function adoptionShadow(legacy) {
  const preview = previewLifecycleMigration(legacy);
  return { version: 10, origin: { coordinator: legacy.coordinator, head: legacy.head, mode: legacy.mode,
    legacy: structuredClone(legacy), legacyDigest: preview.legacyDigest }, history: [] };
}

test('public legacy1-9 construction previews held exclusions and opt-in adoption preserves exact histories', async t => {
  for (let version = 1; version <= 9; version++) {
    await t.test(`legacy ${version}`, () => {
      const legacy = legacyVersionScenario(version);
      const before = structuredClone(legacy);
      const originalBytes = JSON.stringify(legacy);
      const preview = previewLifecycleMigration(legacy);
      assert.equal(preview.activating, false);
      const effective = heldReservations(legacy).flatMap(held => held.scopes).sort();
      const projected = preview.projection.reservations.flatMap(held => held.scopes).sort();
      assert.deepEqual(projected, effective);
      assert.deepEqual(legacy, before);
      const driver = lifecycleDriver(adoptionShadow(legacy));
      const holders = driver.projection().grants.filter(grant => grant.status !== 'revoked')
        .map(grant => grant.id);
      const prepared = driver.prepare({ kind: 'adopt', legacyDigest: preview.legacyDigest }, holders);
      const missing = structuredClone(prepared);
      missing.context.lifecycleEvidence.humanAuthorizationVerified = false;
      assert.throws(() => adoptLifecycle(legacy, missing.event, missing.context), Error);
      assert.deepEqual(legacy, before);
      const result = adoptLifecycle(legacy, prepared.event, prepared.context);
      assert.deepEqual(result.actions, []);
      assert.equal(JSON.stringify(result.state.origin.legacy), originalBytes);
      assert.deepEqual(legacy, before);
      assert(lifecycleProjection(result.state).grants.every(grant => grant.status !== 'active'));
      assert.deepEqual(lifecycleProjection(result.state).reservations, preview.projection.reservations);
      const continued = lifecycleDriver(result.state);
      continued.apply(registerCommand(`new-work-after-legacy-${version}`));
      assert.equal(JSON.stringify(continued.state.origin.legacy), originalBytes);
      for (const grant of lifecycleProjection(result.state).grants) {
        assert.throws(() => validateSourceGrant(result.state, {
          grantId: grant.id, grantRevision: grant.revision, owner: grant.owner ?? 'synthetic:missing-owner',
          source: { worktree: 'Q:\\Synthetic\\NeverActivated', branch: 'synthetic/no-grant',
            head: legacy.head }, head: legacy.head, paths: grant.scopes
        }, sourceAuthority), Error);
      }
    });
  }
});

test('actual v10 checkpoint dispatch, global revisions and atomic failure leave complete accepted history unchanged', () => {
  const directory = allocate();
  const persistence = checkpointDriver(directory);
  const scenario = legacyScenario();
  let value = persistence.run({ operation: 'init', expectedRevision: 0,
    coordinator: scenario.coordinator, head: scenario.head, mode: 'delivery', lifecycleVersion: 10 });
  const driver = lifecycleDriver(value.state);
  function commit(prepared) {
    value = persistence.run({ operation: 'apply', expectedRevision: value.revision, event: prepared.event,
      lifecycleEvidence: prepared.context.lifecycleEvidence });
    driver.accept(prepared);
    assert.deepEqual(value.state, driver.state);
  }
  commit(driver.prepare(registerCommand('durable')));
  commit(driver.prepare({ kind: 'allocate', key: 'durable', grantId: 'durable-grant',
    scopes: ['synthetic\\durable.cjs'], transfers: [] }));
  commit(driver.prepare({ kind: 'dispatch', grantId: 'durable-grant' }));
  assert.equal(value.actions.filter(action => action.command.type === 'launch').length, 1);
  assert.deepEqual(checkpointDriver(directory).read(), value);
  const prepared = driver.prepare(registerCommand('unrelated-after-ack'));
  const oldRevision = value.revision;
  const pending = value.actions.find(action => action.command.type === 'launch');
  value = persistence.run({ operation: 'ack', expectedRevision: value.revision,
    actionId: pending.id, status: 'done', evidence: 'synthetic:exact-launch-result' });
  const bytes = snapshotTree(directory);
  const input = { operation: 'apply', expectedRevision: value.revision, event: prepared.event,
    lifecycleEvidence: prepared.context.lifecycleEvidence };
  assert.throws(() => persistence.run({ ...input, expectedRevision: oldRevision }), /Stale checkpoint/);
  assert.deepEqual(snapshotTree(directory), bytes);
  for (const method of ['writeFileSync', 'renameSync']) {
    const fault = failingIo(method, persistence.file);
    assert.throws(() => persistence.run(input, fault.io), { code: 'EIO' });
    assert.equal(fault.calls(), 1);
    assert.deepEqual(snapshotTree(directory), bytes);
    assert.deepEqual(persistence.read(), value);
  }
  const lock = `${persistence.file}.lock`;
  const handle = fs.openSync(lock, 'wx');
  try {
    assert.throws(() => persistence.run(input), { code: 'EEXIST' });
    assert(fs.existsSync(lock));
  } finally {
    fs.closeSync(handle);
    fs.unlinkSync(lock);
  }
  assert.deepEqual(snapshotTree(directory), bytes);
  commit(prepared);
  assert.equal(value.actions.filter(action => action.command.type === 'launch').length, 1);
  assert.equal(value.actions.find(action => action.id === pending.id).status, 'done');
  const accepted = snapshotTree(directory);
  assert.throws(() => persistence.run({ operation: 'apply', expectedRevision: value.revision,
    event: driver.state.history.find(record => record.event.command.kind === 'dispatch').event,
    lifecycleEvidence: driver.state.history.find(record => record.event.command.kind === 'dispatch').hostEvidence }),
  /replayed/);
  assert.deepEqual(snapshotTree(directory), accepted);
  const tampered = structuredClone(value);
  tampered.state.history.at(-1).digest = '0'.repeat(64);
  fs.writeFileSync(persistence.file, JSON.stringify(tampered));
  const badBytes = snapshotTree(directory);
  assert.throws(() => persistence.read(), Error);
  assert.deepEqual(snapshotTree(directory), badBytes);
});

test('published source anchors and valid retained refs are required before source retirement', async t => {
  const directory = allocate();
  const driver = lifecycleDriver(emptyLifecycle());
  const request = attachExecution(driver, directory, 'anchors', 'anchor-grant', ['synthetic\\anchor.cjs']);
  writeSyntheticSource(directory, driver.state, request, sourceAuthority, 'durable source bytes\n');
  driver.apply(publication('anchor-grant', 501));
  const bytes = snapshotTree(directory);
  for (const mutation of ['arbitrary-head', 'invalid-ref', 'missing-publication', 'missing-source']) {
    await t.test(mutation, () => {
      const branch = lifecycleDriver(driver.state);
      const prepared = branch.prepare({ kind: 'revoke', grantId: 'anchor-grant',
        reason: 'Synthetic complete source retirement.' }, ['anchor-grant']);
      const manifest = prepared.event.evidence.holders[0].preservation;
      if (mutation === 'arbitrary-head') manifest.commits = [{ head: '9'.repeat(40), ref: 'refs/synthetic/arbitrary' }];
      if (mutation === 'invalid-ref') manifest.commits[0].ref = 'not-a-git-ref';
      if (mutation === 'missing-publication') manifest.commits = manifest.commits.filter(value =>
        value.head !== 'b'.repeat(40));
      if (mutation === 'missing-source') manifest.commits = manifest.commits.filter(value =>
        value.head !== request.source.head);
      rebindLifecycle(prepared);
      console.log(`Required anchor refusal ${mutation}: ${JSON.stringify({ state: branch.state, ...prepared })}`);
      refusesUnchanged(branch, prepared);
      assert.deepEqual(snapshotTree(directory), bytes);
    });
  }
});

test('later holder evidence cannot omit previously declared durable commit and ref anchors', () => {
  const directory = allocate();
  const driver = lifecycleDriver(emptyLifecycle());
  attachExecution(driver, directory, 'retained-anchors', 'retained-anchor-grant', ['synthetic\\anchor.cjs']);
  driver.apply(publication('retained-anchor-grant', 502));
  driver.apply({ kind: 'revoke', grantId: 'retained-anchor-grant', reason: 'Synthetic preserved retirement.' },
    ['retained-anchor-grant']);
  const prepared = driver.prepare({ kind: 'close', key: 'retained-anchors', outcome: 'delivered',
    releases: [] }, ['retained-anchor-grant']);
  const oldAnchor = prepared.event.evidence.holders[0].preservation.commits[0];
  prepared.event.evidence.holders[0].preservation.commits[0] = {
    head: oldAnchor.head, ref: `${oldAnchor.ref}-substituted`
  };
  rebindLifecycle(prepared);
  console.log(`Required durable ref retention: ${JSON.stringify({ state: driver.state, ...prepared })}`);
  refusesUnchanged(driver, prepared);
});

test('new unadmitted planned or actual paths cannot become accepted retirement evidence', async t => {
  const directory = allocate();
  const driver = lifecycleDriver(emptyLifecycle());
  attachExecution(driver, directory, 'bounded-manifest', 'bounded-manifest-grant', ['synthetic\\admitted.cjs']);
  for (const list of ['planned', 'actual']) {
    await t.test(list, () => {
      const branch = lifecycleDriver(driver.state);
      const prepared = branch.prepare({ kind: 'revoke', grantId: 'bounded-manifest-grant',
        reason: 'Synthetic bounded authority retirement.' }, ['bounded-manifest-grant']);
      prepared.event.evidence.holders[0].preservation[list].push('unadmitted\\outside.cjs');
      if (list === 'actual') prepared.event.evidence.holders[0].preservation.retained.push('unadmitted\\outside.cjs');
      rebindLifecycle(prepared);
      console.log(`Required unadmitted ${list} refusal: ${JSON.stringify({ state: branch.state, ...prepared })}`);
      refusesUnchanged(branch, prepared);
    });
  }
});

test('standard release refuses adopted local future custody without a retired execution grant', () => {
  const legacy = legacyVersionScenario(5);
  const preview = previewLifecycleMigration(legacy);
  const driver = lifecycleDriver(adoptionShadow(legacy));
  const holders = driver.projection().grants.filter(grant => grant.status !== 'revoked').map(grant => grant.id);
  driver.accept(driver.prepare({ kind: 'adopt', legacyDigest: preview.legacyDigest }, holders));
  const reservation = driver.projection().reservations.find(value => value.key === 'synthetic-successor');
  assert(reservation);
  assert.equal(reservation.grantId, null);
  const original = JSON.stringify(driver.state.origin.legacy);
  const prepared = driver.prepare({ kind: 'release',
    releases: [{ reservationId: reservation.id, paths: reservation.scopes }] });
  refusesUnchanged(driver, prepared);
  assert(driver.projection().reservations.some(value => value.id === reservation.id));
  assert.equal(JSON.stringify(driver.state.origin.legacy), original);
  assert(driver.projection().grants.every(value => value.status !== 'active'));
});

test('explicit unexecuted release requires origin, exact target verification and actual predecessor preservation', async t => {
  const directory = allocate();
  const legacy = legacyVersionScenario(5);
  const driver = lifecycleDriver(adoptionShadow(legacy));
  driver.apply({ kind: 'adopt', legacyDigest: previewLifecycleMigration(legacy).legacyDigest },
    driver.projection().grants.filter(grant => grant.status !== 'revoked').map(grant => grant.id));
  const projection = driver.projection();
  const reservation = projection.reservations.find(value => value.key === 'synthetic-successor');
  assert(reservation);
  assert.equal(reservation.grantId, null);
  assert(!projection.grants.some(grant => grant.key === reservation.key));
  const predecessorIds = [...new Set(driver.state.origin.legacy.supersessions
    .filter(record => record.event.proof.mode === 'activate-local' &&
      record.event.proof.candidate.key === reservation.key)
    .flatMap(record => record.event.proof.holders)
    .map(holder => {
      assert.equal(holder.coordinator, driver.state.origin.coordinator);
      const grants = projection.grants.filter(grant => grant.key === holder.key && grant.owner === holder.owner);
      assert.equal(grants.length, 1, 'The public projection must retain the actual accepted predecessor.');
      assert.equal(grants[0].status, 'revoked');
      return grants[0].id;
    }))].sort();
  assert(predecessorIds.length > 0);
  const command = { kind: 'release-unexecuted', legacyDigest: driver.state.origin.legacyDigest,
    releases: [{ reservationId: reservation.id, paths: [...reservation.scopes] }] };
  const bytes = snapshotTree(directory);
  for (const mutation of ['changed-origin-binding', 'missing-target-verification',
    'changed-target-verification', 'missing-predecessor-preservation']) {
    await t.test(mutation, () => {
      const branch = lifecycleDriver(driver.state);
      const prepared = branch.prepare(command, predecessorIds);
      prepared.context.lifecycleEvidence.unexecutedCustodyVerified = [reservation.id];
      if (mutation === 'changed-origin-binding') prepared.event.command.legacyDigest = '0'.repeat(64);
      if (mutation === 'missing-target-verification') {
        delete prepared.context.lifecycleEvidence.unexecutedCustodyVerified;
      }
      if (mutation === 'changed-target-verification') {
        prepared.context.lifecycleEvidence.unexecutedCustodyVerified = predecessorIds;
      }
      if (mutation === 'missing-predecessor-preservation') {
        prepared.event.evidence.holders = [];
        prepared.context.lifecycleEvidence.holdersVerified = [];
      }
      rebindLifecycle(prepared);
      refusesUnchanged(branch, prepared);
      assert.deepEqual(snapshotTree(directory), bytes);
    });
  }
  await t.test('fresh explicit disposition preserves work, predecessor history and unrelated custody', () => {
    const branch = lifecycleDriver(driver.state);
    const original = JSON.stringify(branch.state.origin);
    const prepared = branch.prepare(command, predecessorIds);
    prepared.context.lifecycleEvidence.unexecutedCustodyVerified = [reservation.id];
    assert.deepEqual(Object.keys(prepared.event.expected.grants).sort(), predecessorIds);
    for (const grantId of predecessorIds) {
      const predecessor = projection.grants.find(grant => grant.id === grantId);
      assert(Object.hasOwn(prepared.event.expected.works, predecessor.key));
      for (const guard of projection.reservations.filter(value => value.grantId === grantId)) {
        assert(Object.hasOwn(prepared.event.expected.reservations, guard.id));
      }
    }
    assert.deepEqual(branch.accept(prepared).actions, []);
    assert.deepEqual(branch.projection().reservations,
      projection.reservations.filter(value => value.id !== reservation.id));
    assert.deepEqual(branch.projection().works, projection.works);
    assert.deepEqual(branch.projection().grants.map(({ id, status, scopes, owner, source }) =>
      ({ id, status, scopes, owner, source })), projection.grants.map(({ id, status, scopes, owner, source }) =>
      ({ id, status, scopes, owner, source })));
    assert.deepEqual(branch.projection().capacity, projection.capacity);
    assert.equal(JSON.stringify(branch.state.origin), original);
    assert.deepEqual(snapshotTree(directory), bytes);
  });
});

test('legitimate unwritten retained paths do not require full planned or actual scope equality', () => {
  const directory = allocate();
  const driver = lifecycleDriver(emptyLifecycle());
  const scopes = ['synthetic\\unwritten.cjs', 'synthetic\\also-unwritten.cjs'];
  attachExecution(driver, directory, 'unwritten', 'unwritten-grant', scopes);
  const prepared = driver.prepare({ kind: 'revoke', grantId: 'unwritten-grant',
    reason: 'Synthetic stop with no source changes.' }, ['unwritten-grant']);
  const manifest = prepared.event.evidence.holders[0].preservation;
  manifest.actual = [];
  manifest.planned = [];
  manifest.committed = [];
  rebindLifecycle(prepared);
  const bytes = snapshotTree(directory);
  assert.deepEqual(driver.accept(prepared).actions, []);
  assert.deepEqual(snapshotTree(directory), bytes);
});

test('real checkpoint opt-in adoption appends v10 while preserving all legacy bytes, journals and exclusions', () => {
  const directory = allocate();
  const persistence = checkpointDriver(directory);
  const scenario = legacyScenario();
  let value = persistence.run({ operation: 'init', expectedRevision: 0,
    coordinator: scenario.coordinator, head: scenario.head, mode: 'delivery' });
  for (const event of scenario.events) {
    value = persistence.run({ operation: 'apply', expectedRevision: value.revision, event });
  }
  const old = structuredClone(value);
  const driver = lifecycleDriver(adoptionShadow(value.state));
  const holders = driver.projection().grants.filter(grant => grant.status !== 'revoked').map(grant => grant.id);
  const prepared = driver.prepare({ kind: 'adopt', legacyDigest: previewLifecycleMigration(value.state).legacyDigest },
    holders);
  const bytes = snapshotTree(directory);
  const missing = structuredClone(prepared);
  missing.context.lifecycleEvidence.ledgerAuthorityVerified = false;
  assert.throws(() => persistence.run({ operation: 'adopt-lifecycle', expectedRevision: value.revision,
    event: missing.event, lifecycleEvidence: missing.context.lifecycleEvidence }), Error);
  assert.deepEqual(snapshotTree(directory), bytes);
  value = persistence.run({ operation: 'adopt-lifecycle', expectedRevision: value.revision,
    event: prepared.event, lifecycleEvidence: prepared.context.lifecycleEvidence });
  assert.equal(value.state.version, 10);
  assert.deepEqual(value.state.origin.legacy, old.state);
  assert.deepEqual(value.events.slice(0, old.events.length), old.events);
  assert.deepEqual(value.actions, old.actions);
  assert.deepEqual(persistence.read(), value);
  assert(lifecycleProjection(value.state).grants.every(grant => grant.status !== 'active'));
});
