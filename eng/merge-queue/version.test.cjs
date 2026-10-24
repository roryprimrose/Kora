const { test } = require('node:test');
const assert = require('node:assert/strict');
const { spawnSync } = require('node:child_process');
const { mkdtempSync, rmSync } = require('node:fs');
const { tmpdir } = require('node:os');
const path = require('node:path');

test('candidate version resolution verifies its checkout and can never publish as main or a tag', () => {
  const directory = mkdtempSync(path.join(tmpdir(), 'kora-queue-version-'));
  const script = path.join(__dirname, '..', 'Get-BuildVersion.ps1');
  const env = { ...process.env, GITHUB_ACTIONS: 'true', GITHUB_EVENT_NAME: 'workflow_dispatch',
    GITHUB_REPOSITORY: 'roryprimrose/Kora', GITHUB_REF: 'refs/heads/main', GITHUB_SHA: 'a'.repeat(40) };
  function git(...args) {
    const result = spawnSync('git', ['-C', directory, ...args], { encoding: 'utf8' });
    assert.equal(result.status, 0, result.stderr);
    return result.stdout.trim();
  }
  function resolve(args, overrides = {}) {
    return spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command',
      `& '${script.replaceAll("'", "''")}' -RepositoryPath '${directory.replaceAll("'", "''")}' ${args} | ConvertTo-Json -Compress`],
    { env: { ...env, ...overrides }, encoding: 'utf8' });
  }
  try {
    git('init', '-b', 'candidate');
    git('-c', 'user.name=Queue tests', '-c', 'user.email=queue@example.invalid',
      '-c', 'commit.gpgsign=false', '-c', 'core.hooksPath=', 'commit', '--allow-empty', '-m', 'Synthetic candidate');
    const sha = git('rev-parse', 'HEAD');
    for (const ref of ['refs/heads/main', 'refs/tags/v1.2.3']) {
      const result = resolve(`-ExpectedRevision ${sha} -ValidationOnly`, { GITHUB_REF: ref });
      assert.equal(result.status, 0, result.stderr);
      const version = JSON.parse(result.stdout);
      assert.equal(version.Version, '0.1.0');
      assert.equal(version.SourceRevision, sha);
      assert.equal(version.Publish, false);
    }
    for (const args of [`-ExpectedRevision ${sha}`, '-ValidationOnly',
      '-ExpectedRevision main -ValidationOnly', `-ExpectedRevision ${'b'.repeat(40)} -ValidationOnly`]) {
      assert.notEqual(resolve(args).status, 0, args);
    }
    const legacy = resolve('', { GITHUB_EVENT_NAME: 'pull_request', GITHUB_SHA: sha, GITHUB_REF: 'refs/pull/1/merge' });
    assert.equal(legacy.status, 0, legacy.stderr);
    assert.equal(JSON.parse(legacy.stdout).Publish, false);
    assert.notEqual(resolve('').status, 0);
  } finally {
    rmSync(directory, { recursive: true });
  }
});
