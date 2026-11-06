const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { randomUUID } = require('node:crypto');
const { spawnSync } = require('node:child_process');
const { discoverPolicyTests, runPolicyTests, QUEUE_MODULES, SELECTOR_MODULE, CAMPAIGN_MODULE } =
  require('../run-policy-tests.cjs');

const root = path.resolve(__dirname, '..', '..');
const queueDirectory = path.join('eng', 'merge-queue');
const campaignDirectory = path.join('eng', 'pr-campaign');
const queueFiles = QUEUE_MODULES.map(name => path.join(queueDirectory, name));
const mandatoryFiles = [...queueFiles, SELECTOR_MODULE];

function fakeFileSystem({ campaign = [], omit = [], directory = false, fault } = {}) {
  const files = new Set([...mandatoryFiles, ...campaign].filter(name => !omit.includes(name)));
  const directories = new Set([queueDirectory, path.dirname(SELECTOR_MODULE)]);
  if (directory || campaign.length) directories.add(campaignDirectory);
  return {
    statSync(name) {
      const relative = path.relative(root, name);
      if (relative === fault) throw new Error('Fixture access denied.');
      if (!files.has(relative) && !directories.has(relative)) return undefined;
      return { isFile: () => files.has(relative), isDirectory: () => directories.has(relative) };
    },
    readdirSync(name) {
      const relative = path.relative(root, name);
      return [...files].filter(file => path.dirname(file) === relative).map(file => path.basename(file));
    },
  };
}

test('entirely absent campaign is explicitly NOT_INSTALLED, never a campaign pass', () => {
  const output = [];
  const calls = [];
  const result = runPolicyTests(root, {
    fileSystem: fakeFileSystem(),
    execute: (...args) => { calls.push(args); return { status: 0, signal: null }; },
    write: message => output.push(message),
  });
  assert.equal(result.campaign, 'NOT_INSTALLED');
  assert.deepEqual(result.files, [...queueFiles].sort().concat(SELECTOR_MODULE));
  assert.ok(output.some(line => line.includes('NOT_INSTALLED') && line.includes('not a campaign test pass')));
  assert.ok(!result.files.includes(CAMPAIGN_MODULE));
  assert.equal(calls.length, 1);
  assert.equal(calls[0][0], process.execPath);
  assert.deepEqual(calls[0][1], ['--test', ...result.files]);
  assert.deepEqual(calls[0][2], { cwd: root, stdio: 'inherit' });
});

for (const marker of ['', 'campaign.cjs', 'github.cjs', 'cli.cjs', 'receipt.schema.json', 'receipt.example.json']) {
  test(`campaign marker '${marker || 'directory'}' without required module fails before Node runs`, () => {
    assert.throws(() => runPolicyTests(root, {
      fileSystem: fakeFileSystem({ directory: true,
        campaign: marker ? [path.join(campaignDirectory, marker)] : [] }),
      execute: () => assert.fail('Incomplete campaign must not execute a success-shaped subset.'),
    }), /Required policy test module is missing/);
  });
}

test('installed campaign discovers the required module and every additional test module', () => {
  const extra = path.join(campaignDirectory, 'future-policy.test.cjs');
  const selection = discoverPolicyTests(root, fakeFileSystem({ campaign: [
    CAMPAIGN_MODULE, extra, path.join(campaignDirectory, 'campaign.cjs'),
  ] }));
  assert.equal(selection.campaign, 'INSTALLED');
  assert.deepEqual(selection.files, [...queueFiles].sort().concat(SELECTOR_MODULE, [CAMPAIGN_MODULE, extra].sort()));
  const output = [];
  const result = runPolicyTests(root, {
    fileSystem: fakeFileSystem({ campaign: [CAMPAIGN_MODULE, extra] }),
    execute: (command, args) => {
      assert.equal(command, process.execPath);
      assert.deepEqual(args, ['--test', ...selection.files]);
      return { status: 0 };
    },
    write: message => output.push(message),
  });
  assert.equal(result.exitCode, 0);
  assert.ok(output.at(-1).includes('campaign INSTALLED'));
});

for (const missing of mandatoryFiles) {
  test(`missing mandatory module ${missing} fails instead of passing a subset`, () => {
    assert.throws(() => discoverPolicyTests(root, fakeFileSystem({ omit: [missing] })), /Required policy test module/);
  });
}

test('another campaign test is not a substitute for the known mandatory module', () => {
  assert.throws(() => discoverPolicyTests(root, fakeFileSystem({
    campaign: [path.join(campaignDirectory, 'unrelated.test.cjs')],
  })), /Required policy test module/);
});

test('unknown filesystem state is not reinterpreted as NOT_INSTALLED', () => {
  assert.throws(() => discoverPolicyTests(root, fakeFileSystem({ fault: campaignDirectory })), /Fixture access denied/);
});

test('empty discovery fails even if metadata claims the mandatory files exist', () => {
  const fileSystem = fakeFileSystem();
  fileSystem.readdirSync = () => [];
  assert.throws(() => discoverPolicyTests(root, fileSystem), /discovery is empty/);
});

for (const execution of [
  { status: 1 }, { status: 13 }, { status: null, signal: 'SIGTERM' },
  { status: null, error: new Error('Fixture spawn failure') }, {},
]) {
  test(`Node failure or missing exit evidence fails closed: ${JSON.stringify(execution)}`, () => {
    const output = [];
    assert.throws(() => runPolicyTests(root, {
      fileSystem: fakeFileSystem({ campaign: [CAMPAIGN_MODULE] }),
      execute: () => execution,
      write: message => output.push(message),
    }), /Node policy test/);
    assert.ok(!output.some(line => line.includes('executed successfully')));
  });
}

test('real child Node execution supplies every selected module and propagates a failing module', () => {
  const fixture = path.join(root, '.scratch', 'policy-test-selection', randomUUID());
  const files = [...mandatoryFiles, CAMPAIGN_MODULE, path.join(campaignDirectory, 'future.test.cjs')];
  const execute = (command, args, options) => {
    const env = { ...process.env };
    // This fixture starts an independent runner, not another child of the current Node test runner.
    delete env.NODE_TEST_CONTEXT;
    return spawnSync(command, args, { ...options, env, stdio: 'pipe', encoding: 'utf8' });
  };
  try {
    for (const [index, name] of files.entries()) {
      const file = path.join(fixture, name);
      fs.mkdirSync(path.dirname(file), { recursive: true });
      fs.writeFileSync(file, `require('node:test').test('module ${index}', () => {
        require('node:fs').writeFileSync(${JSON.stringify(path.join(fixture, `executed-${index}`))}, 'executed');
      });\n`);
    }
    const output = [];
    const actual = runPolicyTests(fixture, {
      execute,
      write: message => output.push(message),
    });
    assert.equal(actual.exitCode, 0);
    assert.equal(actual.campaign, 'INSTALLED');
    for (let index = 0; index < files.length; index++)
      assert.equal(fs.readFileSync(path.join(fixture, `executed-${index}`), 'utf8'), 'executed');
    fs.writeFileSync(path.join(fixture, CAMPAIGN_MODULE), 'process.exitCode = 13;\n');
    output.length = 0;
    assert.throws(() => runPolicyTests(fixture, {
      execute,
      write: message => output.push(message),
    }), /Node policy tests failed/);
    assert.ok(!output.some(line => line.includes('executed successfully')));
  } finally {
    fs.rmSync(fixture, { recursive: true, force: true });
  }
});
