const { createHash } = require('node:crypto');
const { CHECKS, GATE, STATE_BRANCH, SHA, TICKET, invariant, validateState, title, candidateJobs, workflowJobs,
  requiredJobsState, terminalJobs, terminalRejectedAdmission, terminalRejectedPreflight,
  terminalRejectedDelivery } = require('./policy.cjs');

const FILE = 'queue.json';
const WORKFLOW = 'ci.yml';

class GitHubQueue {
  constructor(github, context, core, appId, deliveryProfile = '') {
    this.github = github;
    this.context = context;
    this.core = core;
    this.repo = context.repo;
    this.repository = `${this.repo.owner}/${this.repo.repo}`;
    this.appId = Number(appId);
    this.independent = require('./delivery.cjs').independent(deliveryProfile);
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
    const entries = [{ path: FILE, mode: '100644', type: 'blob', sha: blob.sha }];
    let baseTree;
    if (this.commit) {
      const { data: previous } = await this.github.rest.repos.getCommit({ ...this.repo, ref: this.commit });
      baseTree = previous.commit?.tree?.sha;
      invariant(previous.sha === this.commit && SHA.test(baseTree), 'Cannot preserve the independent release tree.');
    }
    if (state.deliveryProfile === 'independent-v1') {
      invariant(this.commit, 'Independent releases require existing durable queue state.');
      if (state.completedTip?.delivery === 'independent-v1') {
        const record = require('./delivery.cjs').releaseRecord(state.completedTip);
        const path = `releases/${record.source}.json`;
        let existing;
        try {
          existing = await this.releaseSource(record.source, this.commit);
        } catch (error) {
          if (error.status !== 404) throw error;
        }
        invariant(!existing || JSON.stringify(existing) === JSON.stringify(record),
          'Immutable completed-source record changed.');
        if (!existing) {
          const { data: release } = await this.github.rest.git.createBlob({ ...this.repo,
            content: `${JSON.stringify(record)}\n`, encoding: 'utf-8' });
          entries.push({ path, mode: '100644', type: 'blob', sha: release.sha });
        }
      }
    }
    const { data: tree } = await this.github.rest.git.createTree({ ...this.repo,
      ...(baseTree ? { base_tree: baseTree } : {}), tree: entries });
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
    return (await this.workflowInventory(source, true))?.jobs ?? null;
  }
  async rejectedAdmission(source, run, jobs) {
    if (run.head_sha !== source) return false;
    const inventory = await this.workflowInventory(source);
    return inventory !== null && (terminalRejectedAdmission(run, jobs, inventory.blob)
      || terminalRejectedPreflight(run, jobs, inventory.blob) || terminalRejectedDelivery(run, jobs, inventory.blob));
  }
  async trustedInventory(source) {
    const inventory = await this.workflowInventory(source, true);
    return candidateJobs(inventory?.schema) ? inventory : null;
  }
  async workflowInventory(source, candidate = false) {
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
    const lines = bytes.toString('utf8').replaceAll('\r\n', '\n').split('\n');
    const expected = workflowJobs(bytes.toString('utf8'), data.sha, candidate);
    if (expected === null)
      this.notice('Candidate workflow job schema is unknown; physical quiescence is unknown.');
    return expected === null ? null : { blob: data.sha, jobs: expected,
      schema: lines.includes('# merge-queue-job-schema: parallel-pr-v1') ? 'parallel-pr-v1'
        : lines.includes('# merge-queue-job-schema: unit-pr-v1') ? 'unit-pr-v1'
          : lines.includes('# merge-queue-delivery-schema: independent-v1') ? 'windows-12-delivery-v1'
            : lines.includes('# merge-queue-job-schema: windows-12-v1') ? 'windows-12-v1' : 'historical' };
  }
  async releaseSource(source, ref = this.commit) {
    invariant(SHA.test(source) && SHA.test(ref), 'Independent release requires pinned source and queue revisions.');
    const { data } = await this.github.rest.repos.getContent({ ...this.repo,
      ref, path: `releases/${source}.json` });
    invariant(data?.type === 'file' && data.encoding === 'base64' && typeof data.content === 'string',
      'Independent source record is not a file.');
    const bytes = Buffer.from(data.content, 'base64');
    invariant(createHash('sha1').update(`blob ${bytes.length}\0`).update(bytes).digest('hex') === data.sha,
      'Independent source record blob differs from its bytes.');
    return require('./delivery.cjs').validateRelease(JSON.parse(bytes.toString('utf8')), source);
  }
  async candidatePackaging(base, head) {
    invariant(SHA.test(base) && SHA.test(head), 'Packaging selection requires exact admitted revisions.');
    const { data } = await this.github.rest.repos.compareCommits({ ...this.repo, base, head, per_page: 1 });
    return require('./delivery.cjs').packagingSelection(['ahead', 'identical'].includes(data.status)
      && data.base_commit?.sha === base && data.merge_base_commit?.sha === base ? data.files : undefined);
  }
  async files(number, expected) {
    const pr = await this.pull(number);
    const identity = value => {
      invariant(value?.number === number && SHA.test(value.head?.sha)
        && value.head?.repo?.full_name === this.repository && value.base?.ref === 'main'
        && SHA.test(value.base?.sha), `PR #${number}: incomplete or foreign file inventory source metadata.`);
      return [value.number, value.head.sha, value.head.repo.full_name, value.base.ref, value.base.sha,
        value.changed_files];
    };
    const source = identity(pr);
    invariant(expected === undefined || JSON.stringify(identity(expected)) === JSON.stringify(source),
      `PR #${number}: file inventory source changed before observation.`);
    invariant(Number.isSafeInteger(pr.changed_files) && pr.changed_files >= 0 && pr.changed_files < 3000,
      'Batch changed-file inventory is unavailable or exceeds GitHub complete pagination limits.');
    const files = await this.github.paginate(this.github.rest.pulls.listFiles,
      { ...this.repo, pull_number: number, per_page: 100 });
    invariant(Array.isArray(files), 'Batch file inventory is unavailable.');
    invariant(files.every(file => file && typeof file.filename === 'string' && file.filename.length > 0
      && (file.previous_filename === undefined || typeof file.previous_filename === 'string'
        && file.previous_filename.length > 0)), 'Malformed batch file path metadata.');
    invariant(files.length === pr.changed_files && new Set(files.map(file => file.filename)).size === files.length,
      'Incomplete or duplicate batch file inventory.');
    invariant(JSON.stringify(identity(await this.pull(number))) === JSON.stringify(source),
      `PR #${number}: file inventory source or base changed during pagination.`);
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
      { workflow_id: WORKFLOW, branch: 'main', head_sha: tip.sha }, 'tip qualification run');
    invariant(runs.every(run => Number.isSafeInteger(run.id) && run.id > 0)
      && new Set(runs.map(run => run.id)).size === runs.length, 'Invalid or duplicate qualification run identities.');
    const eligible = run => run.head_sha === tip.sha && run.head_branch === 'main'
      && run.run_attempt === 1 && ['push', 'workflow_dispatch'].includes(run.event)
      && run.display_title === 'CI main';
    const inventory = await this.workflowInventory(tip.sha);
    invariant(inventory, 'Completed-tip workflow inventory is unavailable.');
    async function observe(api, run) {
      invariant(eligible(run), 'Completed-tip owner source, event or first attempt changed.');
      const jobs = await api.jobs(run.id, 1);
      invariant(jobs.every(job => Number.isSafeInteger(job.id) && job.id > 0
        && job.run_id === run.id && job.run_attempt === 1 && job.head_sha === tip.sha)
        && new Set(jobs.map(job => job.id)).size === jobs.length, 'Invalid completed-tip job identity.');
      const qualification = jobs.filter(job => job.name === 'Qualify completed batch tip');
      const version = jobs.filter(job => job.name === 'Resolve build version');
      const suppressed = run.status === 'completed' && run.conclusion === 'success'
        && version.length === 1 && version[0].status === 'completed' && version[0].conclusion === 'skipped'
        && qualification.length === 1 && qualification[0].status === 'completed'
        && qualification[0].conclusion === 'skipped' && jobs.every(job => job.status === 'completed');
      const started = jobs.some(job => inventory.jobs.includes(job.name) && job.name !== 'Admit CI execution'
        && (job.status === 'in_progress' || job.status === 'completed' && job.conclusion !== 'skipped'));
      const proof = run.status === 'completed' && run.conclusion === 'success'
        && requiredJobsState(jobs, inventory.jobs) === 'success'
        && terminalJobs(jobs, inventory.jobs) && qualification.length === 1
        && qualification[0].status === 'completed' && qualification[0].conclusion === 'success'
        ? { run: run.id, source: tip.sha, job: qualification[0].id } : null;
      const deliveryRejected = terminalRejectedDelivery(run, jobs, inventory.blob);
      const publication = jobs.filter(job => job.name === 'Publish completed batch release');
      const health = run.status === 'completed' && ['failure', 'cancelled', 'timed_out'].includes(run.conclusion)
        && run.path === '.github/workflows/ci.yml'
        && run.repository?.full_name === api.repository && run.head_repository?.full_name === api.repository
        && terminalJobs(jobs, [...inventory.jobs, 'Publish completed batch release', 'Qualify completed batch tip'])
        && publication.length === 1 && ['failure', 'cancelled', 'timed_out'].includes(publication[0].conclusion)
        ? { status: 'BLOCKED_PUBLICATION', source: tip.sha, run: run.id, attempt: 1,
          job: publication[0].id, conclusion: publication[0].conclusion, recovery: 'authorized-recovery-required' }
        : null;
      return { owner: { run: run.id, source: tip.sha, attempt: 1 }, qualification: proof,
        ...(health ? { health } : {}),
        provisional: !started && !(api.independent && deliveryRejected), suppressed,
        terminal: run.status === 'completed' && (terminalJobs(jobs, inventory.jobs)
          || terminalRejectedAdmission(run, jobs, inventory.blob) || deliveryRejected) };
    }
    let previous;
    if (tip.qualificationOwner) {
      const run = runs.find(run => run.id === tip.qualificationOwner.run)
        ?? (await this.github.rest.actions.getWorkflowRun({ ...this.repo, run_id: tip.qualificationOwner.run })).data;
      invariant(run.id === tip.qualificationOwner.run, 'Completed-tip owner run identity changed.');
      previous = await observe(this, run);
      if (previous.qualification || !previous.terminal) return previous;
    }
    // Reconcile existing first attempts before dispatch. Failed owners can only be
    // replaced by observed fresh work after their exact jobs are physically terminal.
    let provisional;
    for (const run of runs.filter(eligible).sort((a, b) =>
      Number(b.status === 'completed') - Number(a.status === 'completed') || a.id - b.id)) {
      if (run.id === tip.qualificationOwner?.run) continue;
      if (run.status === 'completed' && run.conclusion !== 'success' && !this.independent) continue;
      const observation = await observe(this, run);
      if (observation.suppressed) continue;
      if (observation.provisional) provisional ??= observation;
      else return observation;
    }
    return previous ?? provisional ?? null;
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

async function admitBatchTip({ github, context, appId, deliveryProfile = '' }) {
  const api = new GitHubQueue(github, context, null, appId, deliveryProfile);
  const state = await api.load();
  invariant(state.version === 2, 'Batch main qualification requires migrated state.');
  if (api.independent) {
    try {
      const record = await api.releaseSource(context.sha);
      return await api.containsBase(record.source, await api.main());
    } catch (error) {
      if (error.status !== 404) throw error;
      // Push admission can arrive before the controller has recorded the verified merge.
      if (state.completedTip?.sha !== context.sha || state.completedTip.delivery === 'independent-v1') return false;
    }
  }
  if (state.active !== null || !state.completedTip || state.completedTip.sha !== context.sha) return false;
  return await api.main() === context.sha;
}

module.exports = { GitHubQueue, admit, admitBatchTip };
