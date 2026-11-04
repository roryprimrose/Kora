const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const QUEUE_MODULES = ['batch', 'controller', 'github', 'recovery', 'version', 'workflows']
  .map(name => `${name}.test.cjs`);
const SELECTOR_MODULE = path.join('eng', 'tests', 'policy-test-selection.test.cjs');
const CAMPAIGN_DIRECTORY = path.join('eng', 'pr-campaign');
const CAMPAIGN_MODULE = path.join(CAMPAIGN_DIRECTORY, 'campaign.test.cjs');
const CAMPAIGN_MARKERS = ['', 'campaign.test.cjs', 'campaign.cjs', 'github.cjs', 'cli.cjs',
  'receipt.schema.json', 'receipt.example.json'].map(name => path.join(CAMPAIGN_DIRECTORY, name));

function discoverPolicyTests(root, fileSystem = fs) {
  const stat = name => fileSystem.statSync(path.join(root, name), { throwIfNoEntry: false });
  const requireFile = name => {
    if (!stat(name)?.isFile()) throw new Error(`Required policy test module is missing or not a file: ${name}`);
  };
  const discover = directory => {
    if (!stat(directory)?.isDirectory()) throw new Error(`Policy test directory is missing or invalid: ${directory}`);
    const files = fileSystem.readdirSync(path.join(root, directory))
      .filter(name => name.endsWith('.test.cjs')).sort().map(name => path.join(directory, name));
    if (files.length === 0) throw new Error(`Policy test discovery is empty: ${directory}`);
    for (const file of files) requireFile(file);
    return files;
  };
  const queueDirectory = path.join('eng', 'merge-queue');
  for (const name of QUEUE_MODULES) requireFile(path.join(queueDirectory, name));
  requireFile(SELECTOR_MODULE);
  const files = [...discover(queueDirectory), SELECTOR_MODULE];
  const installed = CAMPAIGN_MARKERS.some(name => stat(name) !== undefined);
  if (installed) {
    requireFile(CAMPAIGN_MODULE);
    files.push(...discover(CAMPAIGN_DIRECTORY));
  }
  return { files, campaign: installed ? 'INSTALLED' : 'NOT_INSTALLED' };
}

function runPolicyTests(root, { fileSystem = fs, execute = spawnSync, write = console.log } = {}) {
  const selection = discoverPolicyTests(root, fileSystem);
  write(selection.campaign === 'NOT_INSTALLED'
    ? 'Campaign policy tests: NOT_INSTALLED (entire module absent; not a campaign test pass).'
    : 'Campaign policy tests: INSTALLED (required module and all discovered test modules selected).');
  for (const file of selection.files) write(`Policy test module: ${file}`);
  const result = execute(process.execPath, ['--test', ...selection.files], { cwd: root, stdio: 'inherit' });
  if (result.error) throw new Error('Node policy test execution could not start.', { cause: result.error });
  if (result.status !== 0 || result.signal) {
    throw new Error(`Node policy tests failed: exit ${result.status}; signal ${result.signal ?? 'none'}.`);
  }
  write(`Policy tests executed successfully: ${selection.files.length} modules; campaign ${selection.campaign}.`);
  return { ...selection, exitCode: result.status };
}

module.exports = { discoverPolicyTests, runPolicyTests, QUEUE_MODULES, SELECTOR_MODULE, CAMPAIGN_MODULE };
if (require.main === module) {
  try {
    runPolicyTests(path.resolve(__dirname, '..'));
  } catch (error) {
    console.error(error);
    process.exitCode = 1;
  }
}
