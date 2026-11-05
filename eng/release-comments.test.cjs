'use strict';

const assert = require('node:assert/strict');
const { test } = require('node:test');
const { readFileSync } = require('node:fs');
const path = require('node:path');
const { reconcile } = require('./release-comments.cjs');

const sha = n => n.toString(16).padStart(40, '0');
const repository = 'roryprimrose/Kora';

function workflowJob(workflow, name) {
  const start = workflow.indexOf(`\n  ${name}:`);
  assert.ok(start >= 0, `${name} job must exist in the repository workflow.`);
  return workflow.slice(start + 1).split(/\r?\n(?=  [a-z][\w-]*:)/)[0];
}

function release(id, version, source, day) {
  return {
    id, tag_name: `v${version}`, target_commitish: source, draft: false,
    prerelease: version.includes('-beta'), body: `<!-- kora-source: ${source} -->`,
    html_url: `https://github.com/${repository}/releases/tag/v${version}`,
    published_at: `2026-10-${String(day).padStart(2, '0')}T00:00:00Z`
  };
}

function fixture(version = '1.0.0-beta4') {
  const current = release(4, version, sha(4), 4);
  const state = {
    current, releases: [current, release(1, '0.9.0', sha(1), 1)],
    history: [sha(4), sha(3), sha(2), sha(1)],
    ranges: { [sha(1)]: [sha(4), sha(3), sha(2)], [sha(3)]: [sha(4)] },
    associated: { [sha(2)]: [2], [sha(3)]: [3], [sha(4)]: [4] },
    prs: {}, comments: {}, issueStates: {}, writes: [], reads: [], fail: null
  };
  for (const number of [2, 3, 4]) {
    state.prs[number] = {
      merged: true, baseRefName: 'main', baseRepository: { nameWithOwner: repository },
      mergeCommit: { oid: sha(number) },
      closingIssuesReferences: {
        nodes: [{ number: number + 10, state: 'CLOSED', repository: { nameWithOwner: repository } }],
        pageInfo: { hasNextPage: false, endCursor: null }
      }
    };
  }
  const method = name => async args => {
    state.reads.push({ name, args });
    if (state.fail === name) throw new Error(`Fixture failure: ${name}`);
    switch (name) {
      case 'release': return { data: state.current };
      case 'releases': return state.releases;
      case 'associated': return (state.associated[args.commit_sha] ?? []).map(number => ({ number }));
      case 'comments': return state.comments[args.issue_number] ?? [];
      case 'issue': return { data: { number: args.issue_number, state: state.issueStates[args.issue_number] ?? 'closed' } };
      case 'create': {
        state.writes.push(args);
        const comment = { body: args.body, user: { login: 'github-actions[bot]', type: 'Bot' } };
        (state.comments[args.issue_number] ??= []).push(comment);
        if (state.failAfterWrite) throw new Error('Uncertain POST');
        return { data: comment };
      }
      default: throw new Error(`Unexpected fixture operation: ${name}`);
    }
  };
  const github = {
    rest: {
      repos: { getReleaseByTag: method('release'), listReleases: method('releases'),
        listPullRequestsAssociatedWithCommit: method('associated') },
      issues: { listComments: method('comments'), get: method('issue'), createComment: method('create') }
    },
    paginate: async (operation, args) => {
      assert.equal(args.per_page, 100);
      return operation(args);
    },
    graphql: async (query, args) => {
      assert.match(query, /closingIssuesReferences\(first: 100, after: \$cursor\)/);
      state.reads.push({ name: 'graphql', args });
      if (state.fail === 'graphql') throw new Error('Fixture failure: graphql');
      const pr = structuredClone(state.prs[args.number] ?? null);
      if (pr && args.cursor && state.nextIssues) pr.closingIssuesReferences = state.nextIssues;
      return { repository: { pullRequest: pr } };
    }
  };
  const options = {
    github, version, sourceRevision: sha(4),
    context: { repo: { owner: 'roryprimrose', repo: 'Kora' }, eventName: 'push',
      ref: 'refs/heads/main', sha: sha(4) },
    readHistory: (source, baseline) => {
      assert.equal(source, sha(4));
      state.reads.push({ name: 'history', baseline });
      return baseline ? state.ranges[baseline] : state.history;
    }
  };
  return { state, options };
}

test('all merged PRs since the previous published release include failed intermediate builds', async () => {
  const { state, options } = fixture();
  const result = await reconcile(options);
  assert.deepEqual(result, { baseline: sha(1), issues: 3, written: 3 });
  assert.deepEqual(state.writes.map(write => write.issue_number), [12, 13, 14]);
  assert.equal(state.writes[0].body,
    'Fix published in Kora 1.0.0-beta4 (beta prerelease).\n\n' +
    'https://github.com/roryprimrose/Kora/releases/tag/v1.0.0-beta4\n\n' +
    '<!-- kora-fix-version:v1:1.0.0-beta4 -->');
  assert.equal((await reconcile(options)).written, 0);
  assert.equal(state.writes.length, 3);
});

test('beta uses nearest published ancestor, ignoring drafts, unrelated branches and future releases', async () => {
  const { state, options } = fixture();
  const draft = { ...release(2, '1.0.0-beta2', sha(2), 2), draft: true };
  state.releases.push(draft, release(3, '1.0.0-beta3', sha(3), 3),
    release(5, '2.0.0', sha(5), 3), release(6, '0.9.1', sha(2), 6));
  state.releases.reverse();
  assert.deepEqual(await reconcile(options), { baseline: sha(3), issues: 1, written: 1 });
});

test('stable includes beta fixes again since previous stable, including beta at the same revision', async () => {
  const { state, options } = fixture('1.0.0');
  state.releases.push(release(3, '1.0.0-beta3', sha(3), 3), release(5, '1.0.0-beta4', sha(4), 3));
  options.context.ref = 'refs/tags/v1.0.0';
  assert.deepEqual(await reconcile(options), { baseline: sha(1), issues: 3, written: 3 });
  assert.match(state.writes[0].body, /^Fix published in Kora 1\.0\.0 \(stable release\)\./);
  assert.ok(state.writes[0].body.endsWith('<!-- kora-fix-version:v1:1.0.0 -->'));
});

test('first published release scans reachable history, not only the current commit', async () => {
  const { state, options } = fixture();
  state.releases = [state.current];
  assert.equal((await reconcile(options)).baseline, null);
  assert.equal(state.writes.length, 3);
});

test('only explicit closing links to closed same-repository issues from contained merged main PRs qualify', async () => {
  const { state, options } = fixture();
  state.prs[2].merged = false;
  state.prs[3].baseRefName = 'feature';
  state.prs[4].closingIssuesReferences.nodes = [
    { number: 14, state: 'OPEN', repository: { nameWithOwner: repository } },
    { number: 15, state: 'CLOSED', repository: { nameWithOwner: 'other/repository' } }
  ];
  assert.equal((await reconcile(options)).written, 0);
  state.prs[2].merged = true;
  state.prs[2].mergeCommit.oid = sha(1);
  state.prs[3].baseRefName = 'main';
  state.prs[3].baseRepository.nameWithOwner = 'other/repository';
  assert.equal((await reconcile(options)).written, 0);
});

test('deduplicates PR associations and shared issues and paginates closing links', async () => {
  const { state, options } = fixture();
  state.associated[sha(3)] = [2, 3];
  state.prs[3].closingIssuesReferences.nodes[0].number = 12;
  state.prs[4].closingIssuesReferences.pageInfo = { hasNextPage: true, endCursor: 'page2' };
  state.nextIssues = {
    nodes: [{ number: 15, state: 'CLOSED', repository: { nameWithOwner: repository } }],
    pageInfo: { hasNextPage: false, endCursor: null }
  };
  assert.equal((await reconcile(options)).written, 3);
  assert.deepEqual(state.writes.map(write => write.issue_number), [12, 14, 15]);
  assert.equal(state.reads.filter(read => read.name === 'graphql' && read.args.number === 2).length, 1);
  assert.ok(state.reads.some(read => read.name === 'graphql' && read.args.cursor === 'page2'));
});

test('partial/uncertain issue writes reconcile on retry without reposting successful comments', async () => {
  const { state, options } = fixture();
  state.failAfterWrite = true;
  await assert.rejects(reconcile(options), /Uncertain POST/);
  assert.equal(state.writes.length, 1);
  state.failAfterWrite = false;
  assert.equal((await reconcile(options)).written, 2);
  assert.equal(state.writes.length, 3);
});

test('user-spoofed markers do not suppress a bot comment; conflicting bot markers fail closed', async () => {
  const { state, options } = fixture();
  state.comments[12] = [{ user: { login: 'someone', type: 'User' },
    body: '<!-- kora-fix-version:v1:1.0.0-beta4 -->' }];
  assert.equal((await reconcile(options)).written, 3);
  state.comments[12][1].body += '\nchanged';
  await assert.rejects(reconcile(options), /Conflicting release comment/);
  assert.equal(state.writes.length, 3);
});

test('reopened issues are not written', async () => {
  const { state, options } = fixture();
  state.issueStates[12] = 'open';
  assert.equal((await reconcile(options)).written, 2);
  assert.deepEqual(state.writes.map(write => write.issue_number), [13, 14]);
});

for (const operation of ['release', 'releases', 'associated', 'graphql', 'comments', 'issue', 'create']) {
  test(`API failure at ${operation} propagates without issue writes`, async () => {
    const { state, options } = fixture();
    state.fail = operation;
    await assert.rejects(reconcile(options), /Fixture failure/);
    assert.equal(state.writes.length, 0);
  });
}

for (const [name, change] of [
  ['draft publication', f => { f.state.current.draft = true; }],
  ['wrong channel', f => { f.state.current.prerelease = false; }],
  ['wrong source', f => { f.state.current.target_commitish = sha(3); }],
  ['missing provenance', f => { f.state.current.body = ''; }],
  ['wrong release URL', f => { f.state.current.html_url = 'https://example.com'; }],
  ['invalid publication date', f => { f.state.current.published_at = null; }],
  ['invalid release ID', f => { f.state.current.id = '4'; }],
  ['invalid previous release', f => { f.state.releases[1].body = ''; }],
  ['missing history', f => { f.state.history = []; }],
  ['missing range', f => { f.state.ranges[sha(1)] = []; }],
  ['missing PR evidence', f => { delete f.state.prs[2]; }],
  ['missing link evidence', f => { delete f.state.prs[2].closingIssuesReferences; }],
  ['missing merge SHA', f => { f.state.prs[2].mergeCommit = null; }],
  ['malformed linked issue', f => { f.state.prs[2].closingIssuesReferences.nodes[0].number = '12'; }],
  ['invalid pagination', f => {
    f.state.prs[2].closingIssuesReferences.pageInfo = { hasNextPage: true, endCursor: null };
  }],
  ['pull request event', f => { f.options.context.eventName = 'pull_request'; }],
  ['fork repository', f => { f.options.context.repo.owner = 'other'; }],
  ['feature ref', f => { f.options.context.ref = 'refs/heads/feature'; }],
  ['beta tag ref', f => { f.options.context.ref = 'refs/tags/v1.0.0-beta4'; }],
  ['wrong workflow source', f => { f.options.context.sha = sha(3); }],
  ['invalid version', f => { f.options.version = '1.0.0;bad'; }]
]) {
  test(`${name} fails closed before writes`, async () => {
    const f = fixture();
    change(f);
    await assert.rejects(reconcile(f.options));
    assert.equal(f.state.writes.length, 0);
  });
}

test('workflow isolates issue writes behind successful publication or successful already-published verification', () => {
  const workflow = readFileSync(path.join(__dirname, '..', '.github', 'workflows', 'ci.yml'), 'utf8');
  const job = workflowJob(workflow, 'release');
  assert.match(job, /needs: \[version, portable, windows-integration, installer\]/);
  assert.match(job, /always\(\) && !cancelled\(\)/);
  assert.match(job, /needs\.version\.result == 'success'/);
  assert.match(job, /needs\.version\.outputs\.already-published == 'true' \|\|/);
  for (const dependency of ['portable', 'windows-integration', 'installer']) {
    assert.ok(job.includes(`needs.${dependency}.result == 'success'`));
  }
  assert.match(job, /github\.repository == 'roryprimrose\/Kora'/);
  assert.match(job, /github\.event_name == 'push' \|\| github\.event_name == 'workflow_dispatch'/);
  assert.match(job, /inputs\.queue_ticket == ''/);
  assert.match(job, /issues: write/);
  assert.match(job, /contents: write/);
  assert.doesNotMatch(job, /pull_request_target/);
  assert.match(job, /fetch-depth: 0/);
  assert.match(job, /persist-credentials: false/);
  assert.match(job, /ref: \$\{\{ needs\.version\.outputs\.source-revision \}\}/);
  assert.match(job, /group: release-\$\{\{ needs\.version\.outputs\.version \}\}/);
  assert.match(job, /cancel-in-progress: false/);
  assert.match(job, /require\('\.\/eng\/release-comments\.cjs'\)/);
  assert.match(workflow, /node --test eng\/release-comments\.test\.cjs/);
  assert.equal((job.match(/if: needs\.version\.outputs\.already-published != 'true'/g) ?? []).length, 4);
  const publication = job.indexOf('- name: Verify bytes, generate notes and publish beta/stable release');
  const comments = job.indexOf('- name: Reconcile closed issue comments for the published release');
  assert.ok(publication > 0 && comments > publication);
  assert.doesNotMatch(job.slice(comments), /\n        if:/);
  const publicationStep = job.slice(publication, comments);
  assert.match(publicationStep, /-Action Publish/);
  assert.doesNotMatch(publicationStep, /\n        if:/);
  const publisher = readFileSync(path.join(__dirname, 'Publish-GitHubRelease.ps1'), 'utf8');
  assert.match(publisher,
    /if \(-not \$existing\.draft\) \{\s+Assert-PublishedRelease \$existing\s+Write-Host [^\r\n]+\s+return \[pscustomobject\] @\{ AlreadyPublished = \$true \}/);
  assert.ok(publisher.indexOf('Assert-PublishedRelease $existing') <
    publisher.indexOf("if ($Action -eq 'Check')"));
});

test('batch publication retains locked routing and reconciles comments only after successful publication', () => {
  const workflow = readFileSync(path.join(__dirname, '..', '.github', 'workflows', 'ci.yml'), 'utf8');
  const job = workflowJob(workflow, 'release-batch');
  assert.match(job, /needs: \[version, portable, windows-integration, installer\]/);
  assert.match(job, /if: needs\.version\.outputs\.publish == 'true' && needs\.version\.outputs\.already-published != 'true' && inputs\.queue_ticket == '' && vars\.KORA_MERGE_QUEUE_PROFILE == 'batch-v1'/);
  assert.ok(job.includes("group: ${{ vars.KORA_MERGE_QUEUE_DELIVERY_PROFILE == 'independent-v1' && format('release-source-{0}', github.sha) || 'merge-queue-controller' }}"));
  assert.match(job, /cancel-in-progress: false\r?\n      queue: max/);
  for (const permission of ['contents: write', 'actions: read', 'pull-requests: read', 'issues: write']) {
    assert.ok(job.includes(permission));
  }
  assert.match(job, /ref: \$\{\{ needs\.version\.outputs\.source-revision \}\}/);
  assert.match(job, /fetch-depth: 0/);
  assert.match(job, /persist-credentials: false/);
  const publication = job.indexOf('- name: Verify and publish actual completed-tip bytes under the mutation lock');
  const comments = job.indexOf('- name: Reconcile closed issue comments for the published release');
  assert.ok(publication > 0 && comments > publication);
  assert.match(job.slice(publication, comments), /-Action Publish/);
  assert.match(job.slice(comments), /require\('\.\/eng\/release-comments\.cjs'\)/);
  assert.doesNotMatch(job.slice(publication), /\r?\n        (?:if|continue-on-error):/);
  const qualification = workflowJob(workflow, 'batch-tip-qualified');
  assert.match(qualification, /RELEASE: \$\{\{ needs\.release-batch\.result \}\}/);
  assert.ok(qualification.includes('test "$RELEASE" = success'));
});

test('release job gate admits verified retries but rejects failed/skipped publication prerequisites and cancellation', () => {
  const workflow = readFileSync(path.join(__dirname, '..', '.github', 'workflows', 'ci.yml'), 'utf8');
  const expressions = ['\n', '\r\n'].map(newline => {
    const text = workflow.replace(/\r?\n/g, newline);
    const job = workflowJob(text, 'release');
    const expression = job.match(/^    if: >-\r?\n([\s\S]*?)^    permissions:/m)?.[1].trim();
    assert.ok(expression, 'Release job must declare its folded admission condition before permissions.');
    return expression.replace(/\r\n/g, '\n');
  });
  assert.equal(expressions[0], expressions[1], 'LF and CRLF workflows must have identical admission conditions.');
  const expression = expressions[0];
  // Actions property dereferencing accepts hyphens; JavaScript bracket access models it.
  const jsExpression = expression
    .replaceAll('needs.version.outputs.already-published', "needs.version.outputs['already-published']")
    .replaceAll('needs.windows-integration', "needs['windows-integration']");
  const gate = new Function('always', 'cancelled', 'github', 'needs', 'inputs', 'vars', `return (${jsExpression});`);
  const run = (change = {}) => {
    const needs = {
      version: { result: 'success', outputs: { publish: 'true', 'already-published': 'false' } },
      portable: { result: 'success' }, 'windows-integration': { result: 'success' },
      installer: { result: 'success' }
    };
    const github = { repository, event_name: 'push' };
    const inputs = { queue_ticket: '' };
    const vars = { KORA_MERGE_QUEUE_PROFILE: '' };
    change.apply?.({ needs, github, inputs, vars });
    return gate(() => true, () => change.cancelled ?? false, github, needs, inputs, vars);
  };
  assert.equal(run(), true);
  for (const result of ['failure', 'cancelled', 'skipped']) {
    for (const dependency of ['version', 'portable', 'windows-integration', 'installer']) {
      assert.equal(run({ apply: ({ needs }) => { needs[dependency].result = result; } }), false);
    }
  }
  assert.equal(run({ cancelled: true }), false);
  assert.equal(run({ apply: ({ needs }) => {
    needs.version.outputs['already-published'] = 'true';
    for (const dependency of ['portable', 'windows-integration', 'installer']) needs[dependency].result = 'skipped';
  } }), true);
  for (const event of ['pull_request', 'pull_request_target']) {
    assert.equal(run({ apply: ({ github }) => { github.event_name = event; } }), false);
  }
  assert.equal(run({ apply: ({ github }) => { github.repository = 'other/Kora'; } }), false);
  assert.equal(run({ apply: ({ inputs }) => { inputs.queue_ticket = 'candidate'; } }), false);
  assert.equal(run({ apply: ({ vars }) => { vars.KORA_MERGE_QUEUE_PROFILE = 'batch-v1'; } }), false);
  assert.equal(run({ apply: ({ needs, vars }) => {
    needs.version.outputs['already-published'] = 'true';
    vars.KORA_MERGE_QUEUE_PROFILE = 'batch-v1';
  } }), false);
  assert.equal(run({ apply: ({ needs }) => { needs.version.outputs.publish = 'false'; } }), false);
});
