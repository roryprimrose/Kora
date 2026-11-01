const { CHECKS, GATE, STATE_BRANCH, SHA, TICKET, invariant, validateState, title } = require('./policy.cjs');

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

  async verifyProtection() {
    const { data } = await this.github.rest.repos.getBranchProtection({ ...this.repo, branch: 'main' });
    invariant(data.required_status_checks?.strict === true && data.enforce_admins?.enabled === true,
      'Queue requires strict up-to-date checks enforced for administrators.');
    for (const name of [...CHECKS, GATE]) {
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
    const data = await this.github.graphql(`query($owner:String!,$repo:String!,$number:Int!) {
      repository(owner:$owner,name:$repo) { pullRequest(number:$number) { reviewDecision } }
    }`, { ...this.repo, number });
    const decision = data.repository?.pullRequest?.reviewDecision;
    invariant([null, 'APPROVED', 'REVIEW_REQUIRED', 'CHANGES_REQUESTED'].includes(decision), 'Unknown review state.');
    return decision === null || decision === 'APPROVED';
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
    await this.github.rest.actions.createWorkflowDispatch({ ...this.repo, workflow_id: WORKFLOW, ref: 'main',
      inputs: { queue_pr: String(active.number), queue_head: active.head, queue_base: active.base, queue_ticket: active.ticket } });
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

module.exports = { GitHubQueue, admit };
