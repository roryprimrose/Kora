const { createHash } = require('node:crypto');
const { CHECKS, GATE, STATE_BRANCH, SHA, TICKET, invariant, validateState, title, workflowJobs } = require('./policy.cjs');

const FILE = 'queue.json';
const WORKFLOW = 'ci.yml';

class GitHubQueue {
  constructor(github, context, core, appId) {
    this.github = github;
    this.context = context;
    this.core = core;
    this.repo = context.repo;
    this.repository = `${this.repo.owner}/${this.repo.repo}`;
    this.appId = Number(appId);
    invariant(Number.isSafeInteger(this.appId) && this.appId > 0, 'Missing queue App ID.');
    this.commit = null;
    this.serialized = null;
    this.messages = [];
  }

  async verifyProtection(profile = 'singleton-v1') {
    profile = require('./batch-policy.cjs').profile(profile);
    const { data } = await this.github.rest.repos.getBranchProtection({ ...this.repo, branch: 'main' });
    invariant(data.required_status_checks?.strict === true && data.enforce_admins?.enabled === true,
      'Queue requires strict up-to-date checks enforced for administrators.');
    const required = profile === 'batch-v1' ? [GATE] : [...CHECKS, GATE];
    if (profile === 'batch-v1') invariant(data.required_status_checks.checks?.length === 1
      && (data.required_status_checks.contexts ?? []).every(name => name === GATE),
    'Batch profile requires the separately authorized admission-only protection profile.');
    for (const name of required) {
      invariant(data.required_status_checks.checks?.some(check =>
        check.context === name && check.app_id === this.appId), `Required queue App check is missing: ${name}`);
    }
  }

  async load() {
    let ref;
    try {
      ref = (await this.github.rest.git.getRef({ ...this.repo, ref: `heads/${STATE_BRANCH}` })).data;
    } catch (error) {
      if (error.status !== 404) throw error;
      return { version: 1, active: null, blocked: [] };
    }
    this.commit = ref.object.sha;
    const { data } = await this.github.rest.repos.getContent({ ...this.repo, ref: this.commit, path: FILE });
    invariant(data.type === 'file' && data.encoding === 'base64', 'Queue state is not a file.');
    const state = validateState(JSON.parse(Buffer.from(data.content, 'base64').toString('utf8')));
    this.serialized = JSON.stringify(state);
    return state;
  }

  async save(state, message = 'ci: checkpoint merge queue state') {
    const serialized = JSON.stringify(validateState(state));
    if (this.commit && this.serialized === serialized) return;
    const { data: blob } = await this.github.rest.git.createBlob({ ...this.repo,
      content: `${serialized}\n`, encoding: 'utf-8' });
    const { data: tree } = await this.github.rest.git.createTree({ ...this.repo,
      tree: [{ path: FILE, mode: '100644', type: 'blob', sha: blob.sha }] });
    const { data: commit } = await this.github.rest.git.createCommit({ ...this.repo,
      message, tree: tree.sha, parents: this.commit ? [this.commit] : [] });
    if (this.commit) {
      const { data: current } = await this.github.rest.git.getRef({ ...this.repo, ref: `heads/${STATE_BRANCH}` });
      invariant(current.object.sha === this.commit, 'Queue state changed outside the controller.');
      await this.github.rest.git.updateRef({ ...this.repo, ref: `heads/${STATE_BRANCH}`, sha: commit.sha, force: false });
    } else {
      await this.github.rest.git.createRef({ ...this.repo, ref: `refs/heads/${STATE_BRANCH}`, sha: commit.sha });
    }
    this.commit = commit.sha;
    this.serialized = serialized;
  }

  async main() {
    return (await this.github.rest.git.getRef({ ...this.repo, ref: 'heads/main' })).data.object.sha;
  }
  async pulls() {
    return this.github.paginate(this.github.rest.pulls.list, { ...this.repo, state: 'open', base: 'main',
      sort: 'created', direction: 'asc', per_page: 100 });
  }
  async pull(number) {
    return (await this.github.rest.pulls.get({ ...this.repo, pull_number: number })).data;
  }
  async reviewReady(number) {
    const decision = await this.reviewState(number);
    return decision === null || decision === 'APPROVED';
  }
  async reviewState(number) {
    const data = await this.github.graphql(`query($owner:String!,$repo:String!,$number:Int!) {
      repository(owner:$owner,name:$repo) { pullRequest(number:$number) { reviewDecision } }
    }`, { ...this.repo, number });
    const decision = data.repository?.pullRequest?.reviewDecision;
    invariant([null, 'APPROVED', 'REVIEW_REQUIRED', 'CHANGES_REQUESTED'].includes(decision), 'Unknown review state.');
    return decision;
  }
  async administrator(actor) {
    invariant(typeof actor === 'string' && /^[a-z0-9][a-z0-9-]{0,38}$/i.test(actor), 'Invalid recovery actor.');
    const { data } = await this.github.rest.repos.getCollaboratorPermissionLevel({ ...this.repo, username: actor });
    invariant(typeof data.user?.login === 'string' && data.user.login.toLowerCase() === actor.toLowerCase()
      && ['admin', 'maintain', 'write', 'read', 'none'].includes(data.permission), 'Unknown recovery actor authority.');
    return data.permission === 'admin';
  }
  async enrollment(number) {
    const data = await this.github.graphql(`query($owner:String!,$repo:String!,$number:Int!) {
      repository(owner:$owner,name:$repo) { pullRequest(number:$number) { autoMergeRequest { enabledAt } } }
    }`, { ...this.repo, number });
    invariant(data.repository?.pullRequest, 'Queue enrollment PR is unavailable.');
    const request = data.repository.pullRequest.autoMergeRequest;
    if (request === null) return null;
    invariant(typeof request?.enabledAt === 'string' && Number.isFinite(Date.parse(request.enabledAt)),
      'Unknown queue enrollment timestamp.');
    return request.enabledAt;
  }
  async containsBase(base, head) {
    const { data } = await this.github.rest.repos.compareCommits({ ...this.repo, base, head });
    return ['ahead', 'identical'].includes(data.status);
  }
  async updateBranch(active) {
    await this.github.rest.pulls.updateBranch({ ...this.repo, pull_number: active.number, expected_head_sha: active.head });
  }
  async createCheck(active, name) {
    const { data } = await this.github.rest.checks.create({ ...this.repo, name, head_sha: active.head,
      status: 'in_progress', external_id: active.ticket,
      output: { title: 'Queued validation', summary: `PR #${active.number}; base ${active.base}.` } });
    invariant(data.app.id === this.appId, 'Admission check was not issued by the configured queue App.');
    return data.id;
  }
  async finishCheck(id, conclusion, message) {
    await this.github.rest.checks.update({ ...this.repo, check_run_id: id, status: 'completed', conclusion,
      output: { title: conclusion === 'success' ? 'Validated candidate' : 'Candidate not admitted', summary: message } });
  }
  async dispatch(active) {
    const batch = active.kind === 'batch';
    await this.github.rest.actions.createWorkflowDispatch({ ...this.repo, workflow_id: WORKFLOW, ref: 'main',
      inputs: { queue_pr: String(batch ? active.members[0].number : active.number),
        queue_head: active.head, queue_base: active.base, queue_ticket: active.ticket,
        ...(batch ? { queue_batch: active.digest } : {}) } });
  }
  async wake() {
    await this.github.rest.actions.createWorkflowDispatch({ ...this.repo, workflow_id: 'merge-queue.yml', ref: 'main' });
  }
  async runs(active) {
    return this.completePages(this.github.rest.actions.listWorkflowRuns, { workflow_id: WORKFLOW,
      event: 'workflow_dispatch', branch: 'main',
      created: `>=${new Date(Date.parse(active.created) - 5 * 60_000).toISOString()}` }, 'candidate run');
  }
  async jobs(runId, attempt = 1) {
    invariant(Number.isSafeInteger(attempt) && attempt > 0, 'Unknown candidate job attempt.');
    return this.completePages(this.github.rest.actions.listJobsForWorkflowRunAttempt,
      { run_id: runId, attempt_number: attempt }, 'candidate job');
  }
  async completePages(method, parameters, kind) {
    let expected;
    const records = await this.github.paginate(method, { ...this.repo, ...parameters, per_page: 100 }, ({ data }) => {
      invariant(Array.isArray(data) && Number.isSafeInteger(data.total_count) && data.total_count >= 0
        && (expected === undefined || expected === data.total_count), `Incomplete or inconsistent ${kind} pagination.`);
      expected = data.total_count;
      return data;
    });
    invariant(Array.isArray(records) && records.length === expected, `Incomplete ${kind} pagination.`);
    return records;
  }
  async expectedJobs(source) {
    return (await this.workflowInventory(source))?.jobs ?? null;
  }
  async trustedInventory(source) {
    const inventory = await this.workflowInventory(source);
    return inventory?.schema === 'windows-12-v1' ? inventory : null;
  }
  async workflowInventory(source) {
    invariant(SHA.test(source), 'Unknown candidate workflow source.');
    let data;
    try {
      ({ data } = await this.github.rest.repos.getContent({ ...this.repo,
        ref: source, path: `.github/workflows/${WORKFLOW}` }));
    } catch (error) {
      if (error.status !== 404) throw error;
      this.notice('Candidate workflow source is unavailable; physical quiescence is unknown.');
      return null;
    }
    if (data?.type !== 'file' || data.encoding !== 'base64' || !SHA.test(data.sha)
      || typeof data.content !== 'string' || data.content.length === 0) {
      this.notice('Candidate workflow source metadata is incomplete; physical quiescence is unknown.');
      return null;
    }
    const bytes = Buffer.from(data.content, 'base64');
    const blob = createHash('sha1').update(`blob ${bytes.length}\0`).update(bytes).digest('hex');
    if (blob !== data.sha) {
      this.notice('Candidate workflow source bytes do not match its blob; physical quiescence is unknown.');
      return null;
    }
    const expected = workflowJobs(bytes.toString('utf8'), data.sha);
    if (expected === null)
      this.notice('Candidate workflow job schema is unknown; physical quiescence is unknown.');
    return expected === null ? null : { blob: data.sha, jobs: expected,
      schema: bytes.toString('utf8').replaceAll('\r\n', '\n').split('\n')
        .includes('# merge-queue-job-schema: windows-12-v1') ? 'windows-12-v1' : 'historical' };
  }
  async files(number) {
    const pr = await this.pull(number);
    invariant(Number.isSafeInteger(pr.changed_files) && pr.changed_files > 0 && pr.changed_files < 3000,
      'Batch changed-file inventory is unavailable or exceeds GitHub complete pagination limits.');
    const files = await this.github.paginate(this.github.rest.pulls.listFiles,
      { ...this.repo, pull_number: number, per_page: 100 });
    invariant(files.length === pr.changed_files && new Set(files.map(file => file.filename)).size === files.length,
      'Incomplete or duplicate batch file inventory.');
    invariant(files.every(file => typeof file.filename === 'string' && file.filename.length > 0
      && (file.previous_filename === undefined || typeof file.previous_filename === 'string'
        && file.previous_filename.length > 0)), 'Malformed batch file path metadata.');
    return files.flatMap(file => file.previous_filename === undefined
      ? [file.filename] : [file.filename, file.previous_filename]);
  }
  async commitInfo(sha) {
    invariant(SHA.test(sha), 'Unknown commit identity.');
    const { data } = await this.github.rest.repos.getCommit({ ...this.repo, ref: sha });
    invariant(data.sha === sha && SHA.test(data.commit?.tree?.sha) && Array.isArray(data.parents)
      && data.parents.every(parent => SHA.test(parent.sha)), 'Incomplete commit tree or parent evidence.');
    return { sha, tree: data.commit.tree.sha, parents: data.parents.map(parent => parent.sha),
      author: data.author?.login, verified: data.commit.verification?.verified === true };
  }
  async memberTree(head, tree) { return (await this.commitInfo(head)).tree === tree; }
  async expectedUpdate(member, base, tree) {
    const pr = await this.pull(member.number);
    if (pr.head.sha === member.head) return null;
    const commit = await this.commitInfo(pr.head.sha);
    const checks = await this.github.paginate(this.github.rest.checks.listForRef,
      { ...this.repo, ref: member.head, check_name: GATE, per_page: 100 });
    const authority = checks.filter(check => check.app?.id === this.appId
      && typeof check.app.slug === 'string').map(check => `${check.app.slug}[bot]`);
    if (!(commit.parents.length === 2 && commit.parents[0] === member.head && commit.parents[1] === base
      && commit.tree === tree && commit.verified && authority.includes(commit.author)))
      throw new (require('./batch-policy.cjs').BatchUpdateError)(
        'Changed member head is not a verified expected queue-App branch update.');
    return pr.head.sha;
  }
  async syntheticPrefix(active, index) {
    const previous = index ? active.prefixes[index - 1].sha : active.base;
    const head = active.members[index].head;
    const ref = `heads/automation/merge-queue-batch/${active.ticket}/${index + 1}`;
    let current;
    try { current = (await this.github.rest.git.getRef({ ...this.repo, ref })).data.object.sha; }
    catch (error) {
      if (error.status !== 404) throw error;
      await this.github.rest.git.createRef({ ...this.repo, ref: `refs/${ref}`, sha: previous });
      current = previous;
    }
    if (current === previous) {
      try {
        await this.github.rest.repos.merge({ ...this.repo, base: ref.slice('heads/'.length), head,
          commit_message: `Merge queue synthetic ${active.ticket} prefix ${index + 1}` });
      } catch (error) {
        if (error.status === 409) return null;
        throw error;
      }
      current = (await this.github.rest.git.getRef({ ...this.repo, ref })).data.object.sha;
    }
    const commit = await this.commitInfo(current);
    invariant(current === head && await this.containsBase(previous, head)
      || current === previous && await this.containsBase(head, previous)
      || commit.parents.length === 2 && commit.parents[0] === previous && commit.parents[1] === head,
    'Synthetic prefix moved outside its expected parent identities.');
    return { sha: current, tree: commit.tree };
  }
  async tipQualification(tip) {
    const runs = await this.completePages(this.github.rest.actions.listWorkflowRuns,
      { workflow_id: WORKFLOW, branch: 'main', head_sha: tip.sha, status: 'completed' }, 'tip qualification run');
    for (const run of runs) {
      if (run.head_sha !== tip.sha || run.run_attempt !== 1 || run.status !== 'completed'
        || run.conclusion !== 'success' || !['push', 'workflow_dispatch'].includes(run.event)
        || run.display_title.startsWith('Merge queue ')) continue;
      const jobs = await this.jobs(run.id, 1);
      const expected = await this.expectedJobs(tip.sha);
      const qualification = jobs.filter(job => job.name === 'Qualify completed batch tip');
      if (jobs.every(job => job.run_id === run.id && job.run_attempt === 1 && job.head_sha === tip.sha)
        && new Set(jobs.map(job => job.id)).size === jobs.length
        && require('./policy.cjs').successfulJobs(jobs, expected)
        && qualification.length === 1 && qualification[0].status === 'completed'
        && qualification[0].conclusion === 'success')
        return { run: run.id, source: tip.sha, job: qualification[0].id };
    }
    return null;
  }
  async dispatchQualification(tip) {
    invariant(await this.main() === tip.sha, 'Completed-tip source changed before qualification dispatch.');
    await this.github.rest.actions.createWorkflowDispatch({ ...this.repo, workflow_id: WORKFLOW,
      ref: 'main', inputs: {} });
  }
  async ciQuiescent() {
    for (const status of ['queued', 'in_progress', 'waiting', 'pending', 'requested']) {
      const runs = await this.completePages(this.github.rest.actions.listWorkflowRuns,
        { workflow_id: WORKFLOW, status }, 'rollout live CI run');
      if (runs.length) return false;
    }
    return true;
  }
  async cancel(runId) {
    try {
      await this.github.rest.actions.cancelWorkflowRun({ ...this.repo, run_id: runId });
    } catch (error) {
      if (error.status !== 409) throw error;
      const { data } = await this.github.rest.actions.getWorkflowRun({ ...this.repo, run_id: runId });
      invariant(data.status === 'completed', 'GitHub refused cancellation of a nonterminal candidate.');
      this.notice('Candidate completed concurrently with cancellation.');
    }
  }
  async merge(active) {
    try {
      return (await this.github.rest.pulls.merge({ ...this.repo, pull_number: active.number,
        sha: active.head, merge_method: 'squash' })).data;
    } catch (error) {
      if (![405, 409].includes(error.status)) throw error;
      this.notice(`Protected merge refused: HTTP ${error.status}.`);
      return { merged: false };
    }
  }
  info(message) {
    this.messages.push(message);
    this.core.info(message);
  }
  notice(message) {
    this.messages.push(`Warning: ${message}`);
    this.core.warning(message);
  }
  async summarize(result) {
    await this.core.summary
      .addHeading('Merge queue reconciliation')
      .addTable([
        [{ data: 'Repository', header: true }, { data: 'Trigger', header: true }, { data: 'Outcome', header: true }],
        [this.repository, this.context.eventName, result],
      ])
      .addHeading('Observations and decisions', 2)
      .addCodeBlock(this.messages.join('\n'), 'text')
      .addRaw(result === 'failed' ? '\nReconciliation failed. Inspect the step error; no successful admission outcome is claimed.\n' : '')
      .write();
  }
}

async function admit({ github, context, inputs }) {
  invariant(context.eventName === 'workflow_dispatch' && context.ref === 'refs/heads/main',
    'Candidate validation must be dispatched on trusted main.');
  invariant(SHA.test(inputs.queue_head) && SHA.test(inputs.queue_base) && TICKET.test(inputs.queue_ticket)
    && /^[1-9][0-9]*$/.test(inputs.queue_pr), 'Invalid candidate dispatch inputs.');
  const api = new GitHubQueue(github, context, null, inputs.app_id);
  const state = await api.load();
  const active = state.active;
  const { profile, liveMember } = require('./batch-policy.cjs');
  if (profile(inputs.profile) === 'batch-v1' && active?.kind === 'batch') {
    invariant(state.version === 2 && active?.kind === 'batch' && active.phase === 'dispatched'
      && active.digest === inputs.queue_batch && active.ticket === inputs.queue_ticket
      && active.head === inputs.queue_head && active.base === inputs.queue_base
      && String(active.members[0].number) === inputs.queue_pr && context.sha === active.base
      && await api.main() === active.base, 'Batch dispatch manifest or protected base is stale.');
    const inventory = await api.trustedInventory(active.base);
    invariant(inventory?.blob === active.inventory.blob && inventory.schema === active.inventory.schema
      && JSON.stringify(inventory.jobs) === JSON.stringify(active.inventory.jobs),
    'Trusted batch workflow inventory changed.');
    for (const member of active.members) {
      invariant(liveMember(await api.pull(member.number), member, api.repository,
        await api.enrollment(member.number), await api.reviewState(member.number)),
      'Batch member source, enrollment or review changed after dispatch.');
    }
    invariant(await api.memberTree(active.head, active.prefixes.at(-1).tree), 'Combined candidate tree changed.');
    return active.head;
  }
  invariant((state.version === 1 || profile(inputs.profile) === 'batch-v1' && state.version === 2
    && active?.kind !== 'batch') && !inputs.queue_batch, 'Legacy dispatch cannot consume batch state or inputs.');
  invariant(active && active.phase === 'dispatched' && active.ticket === inputs.queue_ticket
    && active.head === inputs.queue_head && active.base === inputs.queue_base
    && String(active.number) === inputs.queue_pr && context.sha === active.base
    && (await api.main()) === active.base, 'Candidate dispatch is stale or was not admitted.');
  const pr = await api.pull(active.number);
  const { ready } = require('./policy.cjs');
  invariant(ready(pr, api.repository) && pr.head.sha === active.head
    && await api.containsBase(active.base, active.head)
    && await api.enrollment(active.number) === active.enrollment, 'Candidate PR changed after dispatch.');
  return active.head;
}

async function admitBatchTip({ github, context, appId }) {
  const api = new GitHubQueue(github, context, null, appId);
  const state = await api.load();
  invariant(state.version === 2, 'Batch main qualification requires migrated state.');
  if (state.active !== null || !state.completedTip || state.completedTip.sha !== context.sha) return false;
  return await api.main() === context.sha;
}

module.exports = { GitHubQueue, admit, admitBatchTip };
