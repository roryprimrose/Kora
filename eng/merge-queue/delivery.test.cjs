const { test } = require('node:test');
const assert = require('node:assert/strict');
const { independent, releaseRecord, validateRelease, packagingSelection } = require('./delivery.cjs');
const { createHash } = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const { REJECTED_DELIVERY_BLOB, REJECTED_DELIVERY_JOBS, terminalRejectedDelivery,
  terminalRejectedAdmission, terminalRejectedPreflight } = require('./policy.cjs');
const sha = n => String(n).repeat(40);
const tip = () => ({ delivery: 'independent-v1', sha: sha(1), tree: sha(2), base: sha(3),
  combined: sha(4), ticket: '01234567-89ab-cdef-0123-456789abcdef',
  validation: { run: 42, attempt: 1, source: sha(4), workflow: sha(3), inventory: sha(5), schema: 'windows-12-delivery-v1' } });

test('new failed-preflight physical retirement is bound to the exact reviewed workflow and all twenty no-fanout jobs', () => {
  const bytes = Buffer.from(fs.readFileSync(path.join(__dirname, '..', '..', '.github', 'workflows', 'ci.yml'), 'utf8').replaceAll('\r\n', '\n'));
  assert.equal(createHash('sha1').update(`blob ${bytes.length}\0`).update(bytes).digest('hex'), REJECTED_DELIVERY_BLOB);
  assert.equal(REJECTED_DELIVERY_JOBS.length, 20);
  for (const failed of ['Admit CI execution', 'Resolve build version']) {
    const run = { id: 42, run_attempt: 1, head_sha: sha(1), status: 'completed', conclusion: 'failure',
      event: 'workflow_dispatch', head_branch: 'main' };
    const jobs = REJECTED_DELIVERY_JOBS.map((name, index) => ({ id: index + 1, name,
      run_id: 42, run_attempt: 1, head_sha: sha(1), status: 'completed',
      conclusion: name === failed ? 'failure' : name === 'Admit CI execution' ? 'success' : 'skipped' }));
    assert.equal(terminalRejectedDelivery(run, jobs, REJECTED_DELIVERY_BLOB), failed === 'Resolve build version');
    assert.equal(terminalRejectedAdmission(run, jobs, REJECTED_DELIVERY_BLOB), false);
    assert.equal(terminalRejectedPreflight(run, jobs, REJECTED_DELIVERY_BLOB), false);
    assert.equal(terminalRejectedDelivery(run, jobs, sha(9)), false);
    assert.equal(terminalRejectedDelivery({ ...run, run_attempt: 2 }, jobs, REJECTED_DELIVERY_BLOB), false);
    assert.equal(terminalRejectedDelivery(run, jobs.slice(1), REJECTED_DELIVERY_BLOB), false);
    for (const change of [{ status: 'in_progress', conclusion: null }, { id: 1 }, { name: 'unknown' },
      { run_id: 43 }, { head_sha: sha(2) }, { conclusion: 'success' }]) {
      const invalid = jobs.map(job => ({ ...job }));
      Object.assign(invalid.at(-1), change);
      assert.equal(terminalRejectedDelivery(run, invalid, REJECTED_DELIVERY_BLOB), false, JSON.stringify(change));
    }
  }
});

test('independent delivery is explicit and unknown profiles fail closed', () => {
  assert.equal(independent(), false);
  assert.equal(independent('independent-v1'), true);
  assert.throws(() => independent('future'));
});

test('completed-source authority is immutable and excludes transient main qualification observations', () => {
  const source = tip();
  const record = releaseRecord(source);
  source.qualificationOwner = { run: 99 };
  source.qualification = { run: 99 };
  assert.deepEqual(releaseRecord(source), record);
  assert.deepEqual(validateRelease(record, source.sha), record);
  for (const change of [{ delivery: '' }, { sha: 'unknown' }, { tree: '' }, { ticket: '' },
    { base: '' }, { combined: '' }, { validation: { run: 42, attempt: 2 } }])
    assert.throws(() => releaseRecord({ ...tip(), ...change }));
  assert.throws(() => validateRelease(record, sha(9)));
  assert.throws(() => validateRelease({ ...record, schema: 'future' }, source.sha));
  source.admission = { schema: 'batch-manifest-v1', ticket: source.ticket, base: source.base,
    combined: source.combined, inventory: { blob: source.validation.inventory, schema: source.validation.schema },
    prefixes: [{ sha: source.combined, tree: source.tree }] };
  source.manifest = require('./batch-policy.cjs').digest(source.admission);
  assert.deepEqual(validateRelease(releaseRecord(source), source.sha).admission, source.admission);
  source.admission.base = sha(9);
  source.manifest = require('./batch-policy.cjs').digest(source.admission);
  assert.throws(() => releaseRecord(source));
});

test('only complete ordinary source changes defer physical candidate packaging', () => {
  const selected = paths => packagingSelection(paths.map(filename => ({ filename, status: 'modified' }))).build;
  assert.equal(selected(['src/Kora.Core/Feature.cs', 'tests/Kora.UnitTests/Test.cs', 'docs/Help.md']), false);
  for (const path of ['installer/Kora.Setup/View.cs', 'src/Kora/Assets/icon.ico', 'Directory.Build.props',
    'Directory.Packages.props', 'global.json', 'GitVersion.yml', 'NuGet.Config',
    'src/Kora/Kora.csproj', 'src/Kora/packages.lock.json', '.github/workflows/ci.yml',
    'eng/Build-Installer.ps1', 'unknown/file', 'Kora.slnx'])
    assert.equal(selected([path]), true, path);
  assert.equal(packagingSelection([{ filename: 'src/Kora.Core/X.cs', previous_filename: 'installer/X.cs', status: 'renamed' }]).build, true);
  assert.equal(packagingSelection([{ filename: 'installer/X.cs', status: 'removed' }]).build, true);
  assert.equal(packagingSelection([{ filename: 'src/X.cs', status: 'renamed' }]).build, true);
  for (const files of [undefined, [], [{}], [{ filename: 'src/X.cs', status: 'future' }],
    Array.from({ length: 300 }, () => ({ filename: 'src/X.cs', status: 'modified' }))])
    assert.equal(packagingSelection(files).build, true);
});
