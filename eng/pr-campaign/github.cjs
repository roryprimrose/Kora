const { spawn } = require('node:child_process');
const { createHash } = require('node:crypto');
const { SHA, STATE_BRANCH, mode, validateState, matchingRuns, workflowJobs, GATE } =
  require('../merge-queue/policy.cjs');
const { profile } = require('../merge-queue/batch-policy.cjs');
const { ObservationError, boundary, id, login, timestamp, safeUrl, validatePull, summarizeRun } = require('./campaign.cjs');

function execute(file, args, { cwd, signal, timeout }) {
  return new Promise((resolve, reject) => {
    const child = spawn(file, args, { cwd, shell: false, windowsHide: true,
      stdio: ['ignore', 'pipe', 'pipe'], env: { ...process.env, GH_PROMPT_DISABLED: '1', GIT_TERMINAL_PROMPT: '0',
        GIT_PAGER: 'cat', GH_PAGER: 'cat' } });
    const chunks = [];
    let bytes = 0;
    let failure = null;
    const stop = code => { failure ||= code; child.kill(); };
    const abort = () => stop('cancelled');
    signal?.addEventListener('abort', abort, { once: true });
    if (signal?.aborted) abort();
    const timer = setTimeout(() => stop('command-timeout'), timeout);
    child.stdout.on('data', chunk => {
      bytes += chunk.length;
      if (bytes > 8 * 1024 * 1024) stop('invalid-evidence');
      else chunks.push(chunk);
    });
    let authFailure = false;
    child.stderr.on('data', chunk => {
      // Classify without retaining or disclosing arbitrary CLI stderr.
      if (/HTTP (401|403)|authentication|not logged|GH_TOKEN/i.test(chunk.toString())) authFailure = true;
    });
    child.on('error', () => { failure ||= 'transport-error'; });
    child.on('close', code => {
      clearTimeout(timer);
      signal?.removeEventListener('abort', abort);
      if (failure) reject(new ObservationError(failure, file));
      else if (code !== 0) reject(Object.assign(
        new ObservationError(authFailure ? 'authentication-error' : 'transport-error', file), { exitCode: code }));
      else resolve(Buffer.concat(chunks).toString('utf8').trim());
    });
  });
}
class Reader {
  constructor({ repository, worktree, commandSeconds = 20, deadline, signal, run = execute, now = Date.now }) {
    this.repository = repository;
    this.worktree = worktree;
    this.commandSeconds = commandSeconds;
    this.deadline = deadline;
    this.signal = signal;
    this.run = run;
    this.now = now;
    this.reconciliations = [];
  }
  async command(file, args) {
    if (this.signal?.aborted) throw new ObservationError('cancelled', 'observation');
    const remaining = this.deadline - this.now();
    if (remaining <= 0) throw new ObservationError('budget-exhausted', 'observation');
    return this.run(file, args, { cwd: this.worktree, signal: this.signal,
      timeout: Math.min(this.commandSeconds * 1000, remaining) });
  }
  async api(endpoint, query = null) {
    const args = ['api', '--hostname', 'github.com', '--method', query ? 'POST' : 'GET', endpoint,
      ...(query ? ['-f', `query=${query}`] : [])];
    let text;
    try { text = await this.command('gh', args); }
    catch (error) {
      if (!(error instanceof ObservationError) || error.code !== 'transport-error') throw error;
      // Only a new successful exact read can reconcile an ambiguous CLI transport result.
      text = await this.command('gh', args);
      this.reconciliations.push({ classification: 'transport-error-reconciled-by-exact-read',
        boundary: query ? 'fixed-PR-query' : endpoint.split('?')[0] });
    }
    let result;
    try { result = JSON.parse(text); }
    catch { throw new ObservationError('invalid-evidence', 'API JSON'); }
    boundary(result && typeof result === 'object' && !Array.isArray(result)
      && !Object.hasOwn(result, 'errors'), 'complete API response');
    return result;
  }
  async pages(endpoint, key) {
    let expected;
    const records = [];
    for (let page = 1; page <= 10; page++) {
      const data = await this.api(`${endpoint}${endpoint.includes('?') ? '&' : '?'}per_page=100&page=${page}`);
      const location = `${endpoint}; page=${page}`;
      boundary(Number.isSafeInteger(data.total_count) && data.total_count >= 0 && data.total_count <= 1000
        && (expected === undefined || expected === data.total_count) && Array.isArray(data[key]),
      `complete bounded pagination: ${location}`);
      expected = data.total_count;
      records.push(...data[key]);
      boundary(records.length <= expected && (data[key].length > 0 || records.length === expected),
        `missing API page: ${location}; expected=${expected}; observed=${records.length}`);
      if (records.length === expected) {
        const identities = records.map(record => record.id ?? record.name);
        boundary(identities.every(value => value !== undefined)
          && new Set(identities).size === records.length, `duplicate API page identities: ${location}`);
        return records;
      }
    }
    throw new ObservationError('invalid-evidence', 'pagination budget');
  }
  repo(endpoint) { return `repos/${this.repository}/${endpoint}`; }
  async preflight(expectedPrincipal) {
    const origin = await this.command('git', ['remote', 'get-url', 'origin']);
    boundary(origin.toLowerCase() === `https://github.com/${this.repository}.git`.toLowerCase(),
      'owning HTTPS GitHub origin');
    const principal = (await this.api('user')).login;
    if (!login(principal) || expectedPrincipal
      && principal.toLowerCase() !== expectedPrincipal.toLowerCase())
      throw new ObservationError('authentication-error', 'expected CLI principal');
    const repository = await this.api(`repos/${this.repository}`);
    boundary(repository.full_name === this.repository && repository.permissions?.pull === true,
      'authenticated repository access');
    return principal;
  }
  async pulls(numbers) {
    const [owner, name] = this.repository.split('/');
    const fields = numbers.map(number => `p${number}:pullRequest(number:${number}) {
      number state headRefOid baseRefName isDraft mergeable reviewDecision url mergedAt author{login}
      headRepository{nameWithOwner} mergeCommit{oid} autoMergeRequest{enabledAt mergeMethod}
      labels(first:100){totalCount pageInfo{hasNextPage} nodes{name}}
    }`).join('\n');
    const data = await this.api('graphql', `query { repository(owner:"${owner}",name:"${name}") { ${fields} } }`);
    const repository = data.data?.repository;
    boundary(repository && Object.keys(repository).length === numbers.length, 'complete fixed PR target query');
    return numbers.map(number => {
      const pull = repository[`p${number}`];
      boundary(pull && pull.labels?.pageInfo?.hasNextPage === false && Array.isArray(pull.labels.nodes)
        && pull.labels.nodes.length === pull.labels.totalCount
        && pull.labels.nodes.every(label => typeof label?.name === 'string')
        && Object.hasOwn(pull, 'autoMergeRequest') && Object.hasOwn(pull, 'reviewDecision')
        && Object.hasOwn(pull, 'mergeCommit') && Object.hasOwn(pull, 'mergedAt')
        && (pull.mergeCommit === null || SHA.test(pull.mergeCommit?.oid)), 'PR existence and complete labels');
      return validatePull({ number: pull.number, state: pull.state, head: pull.headRefOid,
        base: pull.baseRefName, draft: pull.isDraft, mergeable: pull.mergeable, review: pull.reviewDecision,
        url: pull.url, mergedAt: pull.mergedAt, merge: pull.mergeCommit?.oid ?? null,
        author: pull.author?.login, repository: pull.headRepository?.nameWithOwner,
        enrollment: pull.autoMergeRequest === null ? null : pull.autoMergeRequest?.enabledAt,
        method: pull.autoMergeRequest === null ? null : pull.autoMergeRequest?.mergeMethod,
        labels: pull.labels.nodes.map(label => label.name) }, number, this.repository);
    });
  }
  async ref() {
    const ref = await this.api(this.repo(`git/ref/heads/${STATE_BRANCH}`));
    boundary(SHA.test(ref.object?.sha) && ref.object.type === 'commit', 'queue ref');
    return ref.object.sha;
  }
  async content(file, ref) {
    const data = await this.api(this.repo(`contents/${file}?ref=${encodeURIComponent(ref)}`));
    boundary(data.type === 'file' && data.encoding === 'base64' && SHA.test(data.sha)
      && typeof data.content === 'string' && /^[a-zA-Z0-9+/=\r\n]+$/.test(data.content), 'pinned file bytes');
    const bytes = Buffer.from(data.content, 'base64');
    boundary(createHash('sha1').update(`blob ${bytes.length}\0`).update(bytes).digest('hex') === data.sha,
      'pinned file blob digest');
    return { blob: data.sha, text: bytes.toString('utf8') };
  }
  async inventory(source) {
    const content = await this.content('.github/workflows/ci.yml', source);
    const jobs = workflowJobs(content.text, content.blob);
    boundary(jobs !== null, 'maintained workflow job schema');
    return { blob: content.blob, jobs, schema: content.text.replaceAll('\r\n', '\n').split('\n')
      .includes('# merge-queue-job-schema: windows-12-v1') ? 'windows-12-v1' : 'historical' };
  }
  async runEvidence(run, kind, source, ticket, inventory) {
    const exact = await this.api(this.repo(`actions/runs/${run.id}`));
    boundary(exact.id === run.id && exact.run_attempt === run.run_attempt && exact.head_sha === run.head_sha,
      'exact listed run identity');
    boundary(exact.head_branch === 'main'
      && (kind === 'candidate' ? exact.event === 'workflow_dispatch' && exact.display_title === run.display_title
        : ['push', 'workflow_dispatch'].includes(exact.event)
          && typeof exact.display_title === 'string' && !exact.display_title.startsWith('Merge queue ')),
    'exact run event/branch/title');
    const jobs = await this.pages(this.repo(`actions/runs/${run.id}/attempts/${exact.run_attempt}/jobs`), 'jobs');
    return summarizeRun(exact, jobs, inventory || await this.inventory(exact.head_sha),
      this.repository, kind, source, ticket);
  }
  async snapshot(campaign, principal) {
    const variables = await this.pages(this.repo('actions/variables'), 'variables');
    const variable = name => variables.find(item => item.name === name)?.value ?? '';
    let enabled;
    let queueProfile;
    try {
      enabled = mode(variable('KORA_MERGE_QUEUE_MODE'));
      queueProfile = profile(variable('KORA_MERGE_QUEUE_PROFILE'));
    } catch { throw new ObservationError('invalid-evidence', 'live queue mode/profile'); }
    boundary(enabled, 'enabled maintained merge queue');
    const app = Number(variable('KORA_MERGE_QUEUE_APP_ID'));
    boundary(id(app), 'queue App identity');
    const commit = await this.ref();
    const content = await this.content('queue.json', commit);
    let state;
    try { state = validateState(JSON.parse(content.text)); }
    catch { throw new ObservationError('invalid-evidence', 'maintained queue state schema'); }
    const active = state.active;
    const context = active?.kind === 'batch' ? active.members.map(member => member.number) : active ? [active.number] : [];
    const numbers = [...new Set([...campaign.targets, ...context])];
    const pulls = await this.pulls(numbers);
    const checks = {};
    for (const pull of pulls.filter(item => campaign.targets.includes(item.number) && item.state === 'OPEN')) {
      const records = await this.pages(this.repo(`commits/${pull.head}/check-runs?check_name=${encodeURIComponent(GATE)}`),
        'check_runs');
      checks[pull.number] = records.filter(check => check.app?.id === app).map(check => {
        boundary(id(check.id) && check.head_sha === pull.head && check.name === GATE
          && ['queued', 'in_progress', 'completed'].includes(check.status)
          && (check.conclusion === null || ['success', 'failure', 'cancelled', 'timed_out', 'neutral',
            'skipped', 'action_required', 'stale'].includes(check.conclusion))
          && (check.external_id === '' || /^[a-f0-9-]{36}$/.test(check.external_id)), 'candidate-head App check');
        return { id: check.id, app, name: GATE, head: check.head_sha, ticket: check.external_id || null,
          status: check.status, conclusion: check.conclusion, url: safeUrl(check.html_url, this.repository) };
      });
    }
    let candidate = null;
    if (active?.head) {
      const since = new Date(Date.parse(active.created) - 5 * 60_000).toISOString();
      const runs = await this.pages(this.repo(`actions/workflows/ci.yml/runs?event=workflow_dispatch&branch=main&created=${encodeURIComponent(`>=${since}`)}`), 'workflow_runs');
      const matching = matchingRuns(runs, active);
      boundary(matching.length <= 1, 'unique current-ticket candidate run');
      if (matching.length) {
        boundary(matching[0].head_sha === active.base && matching[0].run_attempt === 1,
          'trusted candidate workflow source/attempt');
        const inventory = await this.inventory(active.base);
        boundary(active.kind !== 'batch' || active.inventory.blob === inventory.blob
          && JSON.stringify(active.inventory.jobs) === JSON.stringify(inventory.jobs), 'frozen batch inventory');
        candidate = await this.runEvidence(matching[0], 'candidate', active.head, active.ticket, inventory);
      }
    }
    let qualification = null;
    const duplicates = [];
    if (state.completedTip) {
      const tip = state.completedTip;
      const runs = await this.pages(this.repo(`actions/workflows/ci.yml/runs?branch=main&head_sha=${tip.sha}`),
        'workflow_runs');
      if (tip.qualification) {
        const exact = await this.api(this.repo(`actions/runs/${tip.qualification.run}`));
        boundary(exact.head_branch === 'main' && ['push', 'workflow_dispatch'].includes(exact.event)
          && typeof exact.display_title === 'string' && !exact.display_title.startsWith('Merge queue '),
        'ordinary completed-tip qualification run');
        qualification = await this.runEvidence(exact, 'qualification', tip.sha, tip.ticket);
      }
      for (const run of runs.filter(item => item.id !== tip.qualification?.run
        && item.head_sha === tip.sha && ['push', 'workflow_dispatch'].includes(item.event)
        && !item.display_title?.startsWith('Merge queue '))) {
        boundary(run.head_branch === 'main', 'tip run branch');
        duplicates.push(await this.runEvidence(run, 'qualification', tip.sha, tip.ticket));
      }
    }
    const controllers = [];
    for (const status of ['in_progress', 'queued', 'waiting', 'pending', 'requested']) {
      for (const run of await this.pages(this.repo(`actions/workflows/merge-queue.yml/runs?status=${status}`),
        'workflow_runs')) {
        boundary(id(run.id) && SHA.test(run.head_sha) && typeof run.head_branch === 'string'
          && ['pull_request_target', 'push', 'workflow_run', 'schedule', 'workflow_dispatch'].includes(run.event)
          && run.status === status, 'controller trigger/run identity');
        controllers.push({ id: run.id, url: safeUrl(run.html_url, this.repository), status: run.status,
          triggerHead: run.head_sha, event: run.event });
      }
    }
    boundary(new Set(controllers.map(run => run.id)).size === controllers.length, 'stable controller pagination');
    controllers.sort((a, b) => (a.status === 'in_progress' ? 0 : 1) - (b.status === 'in_progress' ? 0 : 1) || a.id - b.id);
    await this.command('git', ['fetch', '--no-tags', 'origin', 'refs/heads/main']);
    const main = await this.command('git', ['rev-parse', 'FETCH_HEAD']);
    boundary(SHA.test(main), 'fresh fetched main');
    const reachable = {};
    for (const pull of pulls.filter(item => campaign.targets.includes(item.number) && item.state === 'MERGED')) {
      try {
        await this.command('git', ['merge-base', '--is-ancestor', pull.merge, main]);
        reachable[pull.number] = true;
      } catch (error) {
        // A nonzero Git ancestry result is never a successful merge receipt.
        if (!(error instanceof ObservationError) || error.code !== 'transport-error' || error.exitCode !== 1) throw error;
        reachable[pull.number] = false;
      }
    }
    const fresh = await this.pulls(numbers);
    boundary(JSON.stringify(fresh) === JSON.stringify(pulls) && await this.ref() === commit,
      'stable PR and queue revision snapshot; retry observation');
    return { principal, profile: queueProfile, state, pulls, checks, candidate, qualification, duplicates, controllers,
      reachable, references: { queue: { branch: STATE_BRANCH, commit, blob: content.blob },
        main: { sha: main, fetchedAt: new Date(this.now()).toISOString() } } };
  }
}
module.exports = { Reader, execute };
