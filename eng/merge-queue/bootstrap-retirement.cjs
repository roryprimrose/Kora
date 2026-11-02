const { isDeepStrictEqual } = require('node:util');
const { SHA, invariant, title, matchingRuns } = require('./policy.cjs');

const BRANCH = 'agents/bootstrap-retire-690-20261103';
const CHECKPOINT = 'bcdfa11a7c8ff706fd1f307ab5903d2a69624efe';
const RUN = 41493848513;
const WORKFLOW = '.github/workflows/merge-queue.yml';
const EXPECTED = {
  version: 1,
  active: {
    number: 690, head: 'a7946ef6a12c5840826d387a93540c2312a014a3',
    base: '11db653721d3ffb1edbd33b766d4fdf199cdf9e8',
    ticket: '8f111c05-007d-472b-834d-6a4e0c061782',
    enrollment: '2026-11-02T03:23:44Z', created: '2026-11-02T17:37:22.889Z',
    phase: 'retiring', dispatchRecorded: true,
    checks: {
      'Portable build, tests, coverage, and package': 125443387887,
      'Windows integration tests': 125443416832,
      'Build WiX MSI and custom Burn setup': 125443443029,
      'Merge admission': 125443466693,
    },
  },
  blocked: [
    { number: 683, head: '2b05629953babd14291a057d0ab955fcafc73868',
      enrollment: '2026-11-02T02:35:22Z', reason: 'Selected PR conflicts with main.' },
    { number: 690, head: 'a7946ef6a12c5840826d387a93540c2312a014a3',
      enrollment: '2026-11-02T03:23:44Z',
      reason: 'Candidate validation failed, was cancelled, or duplicated a required job.' },
  ],
};

async function boundary(api, context, lock) {
  await api.verifyProtection();
  invariant(await api.administrator(context.actor), 'Bootstrap requires a live repository administrator.');
  invariant(await api.main() === EXPECTED.active.base, 'Protected main changed; bootstrap is not authorized.');
  const { data: ref } = await api.github.rest.git.getRef({ ...api.repo, ref: `heads/${BRANCH}` });
  invariant(ref.object?.sha === context.sha, 'Bootstrap source branch changed.');
  const { data: ruleset } = await api.github.request('GET /repos/{owner}/{repo}/rulesets/{ruleset_id}',
    { ...api.repo, ruleset_id: Number(lock) });
  invariant(ruleset.id === Number(lock) && ruleset.target === 'branch' && ruleset.enforcement === 'active'
    && isDeepStrictEqual(ruleset.conditions?.ref_name, { exclude: [], include: [context.ref] })
    && Array.isArray(ruleset.bypass_actors) && ruleset.bypass_actors.length === 0
    && Array.isArray(ruleset.rules) && ruleset.rules.length === 3
    && ['creation', 'update', 'deletion'].every(type => ruleset.rules.filter(rule => rule.type === type).length === 1),
  'Bootstrap branch must be locked against all writers, without bypass actors.');
  const { data: policies } = await api.github.request(
    'GET /repos/{owner}/{repo}/environments/{environment_name}/deployment-branch-policies',
    { ...api.repo, environment_name: 'merge-queue', per_page: 100 });
  invariant(policies.total_count === 2 && Array.isArray(policies.branch_policies)
    && policies.branch_policies.length === 2
    && ['main', BRANCH].every(name =>
      policies.branch_policies.filter(policy => policy.type === 'branch' && policy.name === name).length === 1),
  'Bootstrap environment must retain main and only the exact temporary branch.');
  const { data: caller } = await api.github.rest.actions.getWorkflowRun({ ...api.repo, run_id: context.runId });
  invariant(caller.id === context.runId && caller.run_attempt === 1 && caller.event === 'workflow_dispatch'
    && caller.head_branch === BRANCH && caller.head_sha === context.sha && caller.path === WORKFLOW
    && caller.repository?.full_name === api.repository && caller.head_repository?.full_name === api.repository
    && caller.status === 'in_progress' && caller.actor?.login === context.actor,
  'Bootstrap caller does not match the first-attempt approved source and actor.');
}

async function evidence(api) {
  const runs = matchingRuns(await api.runs(EXPECTED.active), EXPECTED.active);
  invariant(runs.length === 1 && runs[0].id === RUN && runs[0].run_attempt === 1
    && runs[0].status === 'completed' && runs[0].head_sha === EXPECTED.active.base,
  'Bootstrap ticket has changed, duplicate, retried or nonterminal workflow work.');
  const { data: run } = await api.github.rest.actions.getWorkflowRun({ ...api.repo, run_id: RUN });
  invariant(run.id === RUN && run.run_attempt === 1 && run.event === 'workflow_dispatch'
    && run.status === 'completed' && run.conclusion === 'failure'
    && run.head_branch === 'main' && run.head_sha === EXPECTED.active.base
    && run.path === '.github/workflows/ci.yml' && run.workflow_id === 373584582
    && run.repository?.full_name === api.repository && run.head_repository?.full_name === api.repository
    && run.display_title === title(EXPECTED.active), 'Pinned failed CI run identity changed.');
  const jobs = await api.jobs(RUN, 1);
  invariant(await api.rejectedAdmission(EXPECTED.active.base, run, jobs),
    'The exact failed run does not prove complete terminal admission-only non-execution.');
  for (const [name, id] of Object.entries(EXPECTED.active.checks)) {
    const { data: check } = await api.github.rest.checks.get({ ...api.repo, check_run_id: id });
    invariant(check.id === id && check.name === name && check.app?.id === api.appId
      && check.head_sha === EXPECTED.active.head && check.status === 'completed' && check.conclusion === 'failure',
    'Failed App check identity or outcome changed.');
  }
}

async function bootstrap(api, { context, inputs, attempt }) {
  invariant(api.repository === 'roryprimrose/Kora' && context.eventName === 'workflow_dispatch'
    && context.ref === `refs/heads/${BRANCH}` && SHA.test(context.sha)
    && Number.isSafeInteger(context.runId) && context.runId > 0 && attempt === 1
    && inputs.acknowledge === true && typeof inputs.lock_ruleset === 'string'
    && /^[1-9][0-9]*$/.test(inputs.lock_ruleset) && Number.isSafeInteger(Number(inputs.lock_ruleset)),
  'Bootstrap requires explicit first-attempt administrator authorization on its exact locked branch.');
  let state;
  for (let pass = 0; pass < 2; pass++) {
    await boundary(api, context, inputs.lock_ruleset);
    await evidence(api);
    state = await api.load();
    invariant(api.commit === CHECKPOINT && isDeepStrictEqual(state, EXPECTED),
      'Reviewed queue checkpoint or state changed; do not retry bootstrap against a new checkpoint.');
  }
  invariant(await api.administrator(context.actor) && await api.main() === EXPECTED.active.base,
    'Administrator authority or main changed immediately before recovery.');
  const next = { ...state, active: null };
  await api.save(next, `ci: retire reviewed admission-only rejection\n\nAdministrator: ${context.actor}\n`
    + `Bootstrap workflow run: ${context.runId}\nBootstrap source: ${context.sha}\n`
    + `Ticket: ${EXPECTED.active.ticket}\nPrevious checkpoint: ${CHECKPOINT}\n`
    + `Terminal failed CI run: ${RUN}\nFailed checks and blocked revisions retained unchanged.`);
  const committed = api.commit;
  const observed = await api.load();
  invariant(committed !== CHECKPOINT && api.commit === committed && isDeepStrictEqual(observed, next),
    'Recovery write is unconfirmed; inspect actual state before any further action.');
  await evidence(api);
  api.info(`Verified App-owned recovery ${committed}; only active cleared; both blocked revisions and four failed checks unchanged.`);
  await api.wake();
  return 'recovered';
}

module.exports = { bootstrap, BRANCH, CHECKPOINT, RUN, EXPECTED };
