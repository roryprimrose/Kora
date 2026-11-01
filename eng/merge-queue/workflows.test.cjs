const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { execution, CHECKS, GATE, JOBS, terminalJobs } = require('./policy.cjs');
const root = path.resolve(__dirname, '..', '..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8').replaceAll('\r\n', '\n');
const ci = process.env.KORA_QUEUE_WORKFLOW_TEST_PATH
  ? fs.readFileSync(process.env.KORA_QUEUE_WORKFLOW_TEST_PATH, 'utf8').replaceAll('\r\n', '\n')
  : read('.github/workflows/ci.yml');
const controller = read('.github/workflows/merge-queue.yml');
const coverage = read('.github/workflows/coverage-comment.yml');
const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;

function job(name) {
  const match = ci.match(new RegExp(`\\n  ${name}:\\n([\\s\\S]*?)(?=\\n  [a-z][\\w-]*:\\n|$)`));
  assert.ok(match, `Missing job ${name}`);
  return match[1];
}

test('CI admission inline policy matches the deterministic ordinary and comparison routing contract', async () => {
  const script = job('admission').match(/          script: \|\n([\s\S]*)/)[1]
    .split('\n').map(line => line.replace(/^            /, '')).join('\n');
  const execute = new AsyncFunction('process', 'context', 'core', 'github', 'require', script);
  for (const modeValue of ['', 'disabled', 'enabled', 'typo']) {
    for (const eventName of ['pull_request', 'push', 'workflow_dispatch']) {
      for (const ref of ['refs/heads/main', 'refs/heads/topic', 'refs/tags/v1.2.3']) {
        for (const inputs of [{}, { compare_portable_tests: true }, { compare_native_evidence: true }]) {
          const outputs = {};
          const operation = execute({ env: { QUEUE_MODE: modeValue, QUEUE_INPUTS: JSON.stringify(inputs) } },
            { eventName, ref, sha: 'a'.repeat(40) },
            { setOutput: (key, value) => { outputs[key] = value; }, notice() {} }, {}, name => {
              assert.equal(name, './eng/merge-queue/policy.cjs');
              return { execution };
            });
          if (modeValue === 'typo') { await assert.rejects(operation); continue; }
          await operation;
          const expected = execution({ modeValue, eventName, ref, sha: 'a'.repeat(40), inputs });
          assert.equal(outputs.full, String(expected.full), JSON.stringify({ modeValue, eventName, ref, inputs }));
          assert.equal(outputs.source, expected.source);
          assert.equal(outputs.candidate, 'false');
        }
      }
    }
  }
});

test('candidate routing rejects disabled mode and delegates source identity to trusted admission', async () => {
  const script = job('admission').match(/          script: \|\n([\s\S]*)/)[1]
    .split('\n').map(line => line.replace(/^            /, '')).join('\n');
  const execute = new AsyncFunction('process', 'context', 'core', 'github', 'require', script);
  await assert.rejects(execute({ env: { QUEUE_MODE: '', QUEUE_INPUTS: '{"queue_head":"x"}' } },
    {}, {}, {}, () => assert.fail()));
  const outputs = {};
  await execute({ env: { QUEUE_MODE: 'enabled', QUEUE_INPUTS: '{"queue_ticket":"ticket"}', QUEUE_APP_ID: '42' } },
    { eventName: 'workflow_dispatch', ref: 'refs/heads/main', sha: 'main' },
    { setOutput: (key, value) => { outputs[key] = value; } }, {},
    name => {
      if (name === './eng/merge-queue/policy.cjs') return { execution };
      assert.equal(name, './eng/merge-queue/github.cjs');
      return { admit: async ({ inputs }) => { assert.equal(inputs.app_id, '42'); return 'candidate'; } };
    });
  assert.equal(outputs.full, 'true');
  assert.equal(outputs.source, 'candidate');
  assert.equal(outputs.candidate, 'true');
  await assert.rejects(execute({ env: { QUEUE_MODE: 'enabled',
    QUEUE_INPUTS: '{"queue_ticket":"ticket","compare_portable_tests":true}' } },
    { eventName: 'workflow_dispatch', ref: 'refs/heads/main', sha: 'main' }, {}, {},
    () => ({ execution })), /timing comparison/);
});

test('candidate jobs check out the admitted SHA, preserve all required identities and cannot publish', () => {
  assert.match(ci, /^run-name: "\$\{\{[^\n]*\}\}"$/m);
  for (const name of CHECKS) assert.ok(ci.includes(`name: ${name}`));
  assert.match(job('version'), /needs: admission\n    if: needs\.admission\.outputs\.full == 'true'/);
  assert.match(job('version'), /ref: \$\{\{ needs\.admission\.outputs\.source \}\}/);
  assert.match(job('version'), /publish: \$\{\{ needs\.admission\.outputs\.candidate != 'true'/);
  assert.match(job('version'), /if: steps\.version\.outputs\.publish == 'true' && needs\.admission\.outputs\.candidate != 'true'/);
  for (const name of ['portable-tests', 'payload', 'windows-test-build', 'windows-test-partitions', 'installer']) {
    assert.match(job(name), /ref: \$\{\{ needs\.version\.outputs\.source-revision \}\}/);
    assert.match(job(name), /persist-credentials: false/);
    assert.doesNotMatch(job(name), /permission[s]?:[\s\S]*write/);
  }
  for (const name of ['portable-tests', 'payload', 'installer']) {
    assert.match(job(name), /SOURCE_REVISION: \$\{\{ needs\.version\.outputs\.source-revision \}\}/);
    assert.doesNotMatch(job(name), /SourceRevision "\$env:GITHUB_SHA"|Revision "\$env:GITHUB_SHA"/);
  }
  assert.match(job('release'), /inputs\.queue_ticket == ''/);
  assert.match(job('windows-integration'), /needs: \[admission, version, windows-integration-shards\]/);
  assert.match(job('portable'), /needs: \[admission, version, portable-tests, payload\]/);
  assert.match(job('installer'), /needs: \[version, payload\]/);
  assert.match(job('payload'), /needs: version/);
  assert.doesNotMatch(job('payload'), /needs:.*portable/);
  assert.match(ci, /cancel-in-progress: \$\{\{ !inputs\.queue_ticket \}\}/);
});

test('independent producers are joined without preventing retirement of predecessor workflow runs', () => {
  const predecessorJobs = JOBS.map(name => ({ name, status: 'completed', conclusion: 'success' }));
  assert.equal(terminalJobs(predecessorJobs), true);
  assert.equal(terminalJobs([...predecessorJobs,
    { name: 'Portable build, tests, and coverage', status: 'completed', conclusion: 'success' },
    { name: 'Publish and inspect exact-source payloads', status: 'completed', conclusion: 'success' },
  ]), true);
  assert.ok(!JOBS.includes('Portable build, tests, and coverage'));
  assert.ok(!JOBS.includes('Publish and inspect exact-source payloads'));
  assert.match(job('portable-tests'), /name: Portable build, tests, and coverage/);
  assert.match(job('payload'), /name: Publish and inspect exact-source payloads/);
  assert.match(job('release'), /needs: \[version, portable, windows-integration, installer\]/);
  assert.match(job('portable'), /needs: \[admission, version, portable-tests, payload\]/);
});

test('Windows tests scale out to twelve artifact-only jobs behind predecessor-compatible gates', () => {
  const producer = job('windows-test-build');
  const consumers = job('windows-test-partitions');
  assert.match(producer, /runs-on: windows-latest/);
  assert.equal((producer.match(/dotnet build tests\/Kora.Windows.IntegrationTests/g) || []).length, 1);
  assert.match(producer, /Publish-WindowsTestArtifact\.ps1 -SourceRevision '\$\{\{ needs\.version\.outputs\.source-revision \}\}'/);
  assert.match(consumers, /needs: \[version, windows-test-build\]/);
  assert.match(consumers, /fail-fast: false/);
  const partitions = consumers.match(/partition: \[([^\]]+)\]/)[1].split(',').map(value => value.trim());
  assert.equal(partitions.length, 12);
  assert.equal(new Set(partitions).size, 12);
  assert.doesNotMatch(consumers, /dotnet (restore|build)|--project|max-threads|larger|xlarge/);
  assert.match(consumers, /-Partition '\$\{\{ matrix\.partition \}\}' -TestModuleDirectory/);
  assert.match(consumers, /-ExpectedSourceRevision '\$\{\{ needs\.version\.outputs\.source-revision \}\}'/);
  assert.match(consumers, /KORA_TEST_NATIVE_EVIDENCE_MODE: \$\{\{ matrix\.partition == 'native-ux' && 'buffered' \|\| 'synchronous' \}\}/);
  assert.match(consumers, /windows-test-build-\$\{\{ needs\.version\.outputs\.source-revision \}\}/);
  assert.match(consumers, /windows-execution-results-\$\{\{ matrix\.partition \}\}/);
  assert.match(job('windows-integration-shards'), /needs: \[admission, version, windows-test-build, windows-test-partitions\]/);
  assert.ok(!JOBS.includes('Build exact-source Windows test modules'));
  for (const partition of partitions) assert.ok(!JOBS.includes(`Windows test partition (${partition})`));
  assert.match(job('windows-integration'), /Test-WindowsIntegrationScheduling\.ps1 -RetainedResultsDirectory/);
  assert.match(job('windows-integration'), /-ExpectedCatalogPath.*-ExpectedSourceRevision/);
});

test('legacy Windows job identities fail closed unless the shared build and all twelve jobs succeed', () => {
  const source = job('windows-integration-shards');
  const guard = source.match(/        run: \|\n([\s\S]*)/)[1]
    .split('\n').map(line => line.replace(/^          /, '')).join('\n');
  assert.match(guard, /if \(\$env:BUILD_RESULT -cne 'success' -or \$env:PARTITION_RESULT -cne 'success'\)/);
  assert.match(guard, /throw "Windows execution is incomplete/);
  assert.match(source, /if: always\(\)/);
});

test('required aggregate skips only an explicitly waiting admission or an already-published release', () => {
  for (const name of ['portable', 'windows-integration']) {
    const expression = job(name).match(/^    if: (.+)$/m)[1]
      .replace('always()', 'true')
      .replaceAll('needs.version.outputs.already-published', "needs.version.outputs['already-published']");
    const evaluate = new Function('needs', `return ${expression};`);
    for (const result of ['success', 'failure', 'cancelled', 'skipped']) {
      for (const full of ['true', 'false', '']) {
        for (const versionResult of ['success', 'failure', 'cancelled', 'skipped']) {
          const needs = { admission: { result, outputs: { full } },
            version: { result: versionResult, outputs: {} } };
          assert.equal(evaluate(needs), !(result === 'success' && full === 'false'),
            JSON.stringify(needs));
          needs.version.outputs['already-published'] = 'true';
          assert.equal(evaluate(needs), false);
        }
      }
    }
  }
});

test('portable aggregate requires both actual producers and fails closed for incomplete outcomes', () => {
  const source = job('portable');
  assert.match(source, /TEST_RESULT: \$\{\{ needs\.portable-tests\.result \}\}/);
  assert.match(source, /PAYLOAD_RESULT: \$\{\{ needs\.payload\.result \}\}/);
  const script = source.match(/        run: \|\n([\s\S]*)/)[1]
    .split('\n').map(line => line.replace(/^          /, '')).join('\n');
  const { spawnSync } = require('node:child_process');
  const outcomes = ['success', 'failure', 'cancelled', 'skipped', '', 'unknown', 'SUCCESS'];
  const checks = outcomes.flatMap(tests => outcomes.map(payload =>
    `$env:TEST_RESULT = '${tests}'; $env:PAYLOAD_RESULT = '${payload}'; `
    + `$failed = $false; try { & { ${script} } } catch { $failed = $true }; `
    + `if ($failed -ne $${tests !== 'success' || payload !== 'success'}) { throw 'Incorrect aggregate outcome.' }`));
  const result = spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', checks.join('\n')], { encoding: 'utf8' });
  assert.equal(result.error, undefined);
  assert.equal(result.status, 0, result.stderr || result.stdout);
});

test('controller wakes on both priority label additions and removals without trusting event labels', () => {
  const events = controller.match(/pull_request_target:\n    branches: \[main\]\n    types: \[([^\]]+)\]/);
  assert.ok(events);
  const types = events[1].split(',').map(value => value.trim());
  assert.ok(types.includes('labeled'));
  assert.ok(types.includes('unlabeled'));
  assert.doesNotMatch(controller, /github\.event\.label|context\.payload\.label/);
});

test('merge and CI events wake the serialized trusted controller without waiting for the recovery timer', () => {
  assert.match(controller, /pull_request_target:\n    branches: \[main\]\n    types: \[[^\]]*\bclosed\b[^\]]*\]/);
  assert.match(controller, /\n  push:\n    branches: \[main\]/);
  assert.match(controller, /\n  workflow_run:\n    workflows: \[CI\]\n    types: \[requested, completed\]/);
  assert.match(controller, /\n  schedule:\n    - cron: '\*\/5 \* \* \* \*'/);
  assert.match(controller, /\n  workflow_dispatch:/);
  assert.match(controller, /concurrency:\n  group: merge-queue-controller\n  cancel-in-progress: false/);
});

test('controller workflow awaits its summary on success and preserves reconciliation failures', async () => {
  const script = controller.match(/          script: \|\n([\s\S]*)/)[1]
    .split('\n').map(line => line.replace(/^            /, '')).join('\n');
  const execute = new AsyncFunction('process', 'context', 'core', 'github', 'require', script);
  for (const outcome of ['idle', 'validating', 'dispatched', 'merged', 'failed']) {
    const calls = [];
    const failure = new Error('Synthetic reconciliation failure');
    const api = { summarize: async result => {
      await Promise.resolve();
      calls.push(['summary', result]);
    } };
    const operation = execute({ env: { QUEUE_APP_ID: '42' } }, {}, {
      notice: message => calls.push(['notice', message]),
    }, {}, name => {
      if (name === './eng/merge-queue/github.cjs') return { GitHubQueue: class {
        constructor(github, context, core, appId) { assert.equal(appId, '42'); return api; }
      } };
      assert.equal(name, './eng/merge-queue/controller.cjs');
      return { reconcile: async value => {
        assert.equal(value, api);
        if (outcome === 'failed') throw failure;
        return outcome;
      } };
    });
    if (outcome === 'failed') await assert.rejects(operation, error => error === failure);
    else await operation;
    assert.deepEqual(calls, outcome === 'failed' ? [['summary', 'failed']]
      : [['notice', `Merge queue: ${outcome}.`], ['summary', outcome]]);
  }
});

test('privileged controller never checks out PR code, bypasses protection or waits for CI on a runner', () => {
  assert.match(controller, /environment: merge-queue/);
  assert.match(controller, /ref: refs\/heads\/main/);
  assert.match(controller, /persist-credentials: false/);
  assert.match(controller, /cancel-in-progress: false/);
  assert.match(controller, /permission-administration: read/);
  assert.doesNotMatch(controller, /permission-administration: write|persist-credentials: true|pull_request\.head|sleep|while \(/);
  assert.match(controller, /if: vars\.KORA_MERGE_QUEUE_MODE == 'enabled'/);
  assert.ok(GATE);
  for (const uses of (ci + controller).matchAll(/uses: ([^\s]+)/g)) {
    assert.match(uses[1], /^[^@]+@[0-9a-f]{40}$/);
  }
  new vm.Script(`(async () => { ${controller.match(/          script: \|\n([\s\S]*)/)[1]
    .split('\n').map(line => line.replace(/^            /, '')).join('\n')} })`);
});

function coverageScript(name) {
  const match = coverage.match(new RegExp(`      - name: ${name}\\n[\\s\\S]*?          script: \\|\\n([\\s\\S]*?)(?=\\n      - name: |$)`));
  assert.ok(match, `Missing coverage script ${name}`);
  return match[1]
    .split('\n').map(line => line.replace(/^            /, '')).join('\n');
}

function coverageFixture(overrides = {}) {
  const script = coverageScript('Resolve validated coverage destination');
  const execute = new AsyncFunction('process', 'context', 'core', 'github', script);
  const outputs = {};
  const calls = [];
  const ticket = '01234567-89ab-cdef-0123-456789abcdef';
  const run = { event: 'workflow_dispatch', display_title: `Merge queue PR #17 [${ticket}]`,
    head_branch: 'main', ...overrides };
  const github = {
    rest: {
      pulls: { get: async args => { calls.push(['pull', args]); return { data: { head: { sha: 'current-head' } } }; } },
      checks: { listForRef: 'listChecks' },
    },
    paginate: async (method, args, transform) => {
      assert.equal(method, 'listChecks');
      calls.push(['checks', args]);
      const data = [{ app: { id: 42 }, external_id: ticket }];
      return transform ? transform({ data }) : data;
    },
  };
  return { outputs, calls, github, run, execute: () => execute({ env: { QUEUE_APP_ID: '42' } },
    { repo: { owner: 'owner', repo: 'repo' }, payload: { workflow_run: run } },
    { setOutput: (key, value) => { outputs[key] = value; }, notice: message => calls.push(['notice', message]) }, github) };
}

test('ordinary PR coverage keeps its associated destination and rejects missing PR identity', async () => {
  const value = coverageFixture({ event: 'pull_request', pull_requests: [{ number: 12 }] });
  await value.execute();
  assert.equal(value.outputs.number, 12);
  assert.deepEqual(value.calls, []);
  await assert.rejects(coverageFixture({ event: 'pull_request', pull_requests: [] }).execute(), /no associated pull request/);
});

test('candidate coverage requires the queue App ticket on the current head', async () => {
  const value = coverageFixture();
  await value.execute();
  assert.equal(value.outputs.number, 17);
  assert.equal(value.calls[0][1].pull_number, 17);
  assert.equal(value.calls[1][1].ref, 'current-head');
  assert.equal(value.calls[1][1].check_name, GATE);
  assert.equal(value.calls[1][1].per_page, 100);
});

test('stale-head, unrelated ticket and wrong-App coverage is not posted as current evidence', async () => {
  for (const checks of [[], [{ app: { id: 42 }, external_id: 'other-ticket' }],
    [{ app: { id: 7 }, external_id: '01234567-89ab-cdef-0123-456789abcdef' }]]) {
    const value = coverageFixture();
    value.github.paginate = async () => checks;
    await value.execute();
    assert.equal(value.outputs.number, undefined);
    assert.equal(value.calls.at(-1)[0], 'notice');
  }
  assert.equal((coverage.match(/if: steps\.destination\.outputs\.number != ''/g) || []).length, 1);
});

test('malformed candidate coverage titles and untrusted workflow branches fail explicitly', async () => {
  for (const overrides of [{ display_title: 'Merge queue PR #17 [not-a-ticket]' },
    { display_title: 'Merge queue PR #0 [01234567-89ab-cdef-0123-456789abcdef]' },
    { head_branch: 'topic' }]) {
    const value = coverageFixture(overrides);
    await assert.rejects(value.execute(), /Invalid candidate coverage source/);
    assert.equal(value.outputs.number, undefined);
    assert.deepEqual(value.calls, []);
  }
});

function coverageProducerFixture({ event = 'pull_request', jobs = [
  { name: CHECKS[0], status: 'completed', conclusion: 'success' },
] } = {}) {
  const execute = new AsyncFunction('context', 'core', 'github', coverageScript('Verify coverage producer'));
  const outputs = {};
  const notices = [];
  const calls = [];
  const github = {
    rest: { actions: { listJobsForWorkflowRun: 'listJobs' } },
    paginate: async (method, args) => {
      calls.push({ method, args });
      return jobs;
    },
  };
  return { outputs, notices, calls, github, execute: () => execute(
    { repo: { owner: 'owner', repo: 'repo' }, payload: { workflow_run: { id: 123, event } } },
    { setOutput: (key, value) => { outputs[key] = value; }, notice: message => notices.push(message) }, github) };
}

test('ordinary and candidate coverage requires a successful portable producer from the latest run attempt', async () => {
  for (const event of ['pull_request', 'workflow_dispatch']) {
    const value = coverageProducerFixture({ event });
    await value.execute();
    assert.equal(value.outputs.ready, 'true');
    assert.deepEqual(value.notices, []);
    assert.deepEqual(value.calls, [{ method: 'listJobs', args: {
      owner: 'owner', repo: 'repo', run_id: 123, filter: 'latest', per_page: 100,
    } }]);
  }
});

test('policy-only PR coverage skips reporting with an explicit notice and no ready output', async () => {
  const value = coverageProducerFixture({ jobs: [
    { name: CHECKS[0], status: 'completed', conclusion: 'skipped' },
    { name: 'Queue policy feedback', status: 'completed', conclusion: 'success' },
  ] });
  await value.execute();
  assert.deepEqual(value.outputs, {});
  assert.equal(value.notices.length, 1);
  assert.match(value.notices[0], /policy-only.*coverage was not produced/i);
});

test('missing, duplicate and unfinished coverage producers fail instead of silently skipping', async () => {
  const producer = { name: CHECKS[0], status: 'completed', conclusion: 'success' };
  for (const jobs of [[], [{ ...producer, name: 'Other job' }], [producer, producer],
    [{ ...producer, status: 'in_progress' }]]) {
    const value = coverageProducerFixture({ jobs });
    await assert.rejects(value.execute(), /exactly one completed portable build job/);
    assert.deepEqual(value.outputs, {});
    assert.deepEqual(value.notices, []);
  }
});

test('unsuccessful portable producers cannot publish coverage for ordinary or candidate runs', async () => {
  for (const event of ['pull_request', 'workflow_dispatch']) {
    for (const conclusion of ['skipped', 'failure', 'cancelled', 'neutral', null]) {
      const value = coverageProducerFixture({ event, jobs: [
        { name: CHECKS[0], status: 'completed', conclusion },
      ] });
      await assert.rejects(value.execute(), /portable build did not succeed/);
      assert.deepEqual(value.outputs, {});
      assert.deepEqual(value.notices, []);
    }
  }
});

test('only successful policy feedback can justify a skipped PR producer, never a candidate producer', async () => {
  const producer = { name: CHECKS[0], status: 'completed', conclusion: 'skipped' };
  const feedback = { name: 'Queue policy feedback', status: 'completed', conclusion: 'success' };
  for (const jobs of [[producer, { ...feedback, conclusion: 'failure' }],
    [producer, { ...feedback, conclusion: 'skipped' }],
    [producer, { ...feedback, status: 'in_progress' }], [producer, feedback, feedback]]) {
    const value = coverageProducerFixture({ jobs });
    await assert.rejects(value.execute(), /portable build did not succeed/);
    assert.deepEqual(value.outputs, {});
    assert.deepEqual(value.notices, []);
  }
  const candidate = coverageProducerFixture({ event: 'workflow_dispatch', jobs: [producer, feedback] });
  await assert.rejects(candidate.execute(), /portable build did not succeed/);
  assert.deepEqual(candidate.outputs, {});
  assert.deepEqual(candidate.notices, []);
});

test('coverage job lookup failures remain explicit and cannot authorize reporting', async () => {
  const value = coverageProducerFixture();
  value.github.paginate = async () => { throw new Error('Job lookup unavailable'); };
  await assert.rejects(value.execute(), /Job lookup unavailable/);
  assert.deepEqual(value.outputs, {});
  assert.deepEqual(value.notices, []);
});

test('coverage download and publication require producer readiness without hiding missing artifacts', () => {
  assert.equal((coverage.match(/if: steps\.producer\.outputs\.ready == 'true'/g) || []).length, 2);
  assert.match(coverage, /name: Verify coverage producer\n        if: steps\.destination\.outputs\.number != ''\n        id: producer/);
  assert.match(coverage, /run-id: \$\{\{ github\.event\.workflow_run\.id \}\}/);
  assert.match(coverage, /name: coverage-report/);
  assert.match(coverage, /fs\.readFileSync\('coverage-report\/SummaryGithub\.md', 'utf8'\)/);
  assert.doesNotMatch(coverage, /continue-on-error|actions\/checkout|KORA_MERGE_QUEUE_MODE/);
});
