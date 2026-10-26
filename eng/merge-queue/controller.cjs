const { randomUUID } = require('node:crypto');
const { CHECKS, GATE, JOBS, MERGE_PHASES, MERGE_CONFIRMATION_MS, invariant, validateState,
  ready, readinessIssue, priority, matchingRuns, successfulJobs } = require('./policy.cjs');

const MAX_CANDIDATES = 10;

async function reconcile(api, options = {}) {
  api.info(`Starting reconciliation for ${api.repository}; verifying required queue App checks and branch protection.`);
  const result = await reconcileOnce(api, options);
  if (['blocked', 'continuing', 'merged', 'already-merged', 'retired'].includes(result)) {
    api.info(`Decision: ${result}; requesting another controller reconciliation on main.`);
    await api.wake();
    api.info('Controller continuation requested.');
  }
  api.info(`Reconciliation finished: ${result}.`);
  return result;
}

async function reconcileOnce(api, { now = new Date(), ticket } = {}) {
  await api.verifyProtection();
  api.info('Required queue App checks and strict administrator-enforced branch protection verified.');
  const state = validateState(await api.load());
  api.info(`Loaded queue state: ${state.active ? `active PR #${state.active.number} (${state.active.phase})` : 'no active candidate'}; ${state.blocked.length} blocked revision(s).`);
  const base = await api.main();
  api.info(`Current main: ${base}; listing open PRs targeting main.`);
  const prs = await api.pulls();
  const eligible = prs.filter(pr => {
    const issue = readinessIssue(pr, api.repository);
    if (issue) api.info(`PR #${pr.number}: skipped; ${issue}.`);
    return issue === null;
  });
  api.info(`Inspected ${prs.length} open PR(s); ${eligible.length} express same-repository squash Auto-merge intent.`);
  const blockedCount = state.blocked.length;
  state.blocked = state.blocked.filter(item => eligible.some(pr =>
    pr.number === item.number && pr.head.sha === item.head));
  api.info(`Blocked revisions retained: ${state.blocked.length}; obsolete records removed: ${blockedCount - state.blocked.length}.`);

  async function persist() { validateState(state); await api.save(state); }
  async function complete(active, conclusion, message, jobs = []) {
    api.info(`PR #${active.number}: completing candidate checks (${conclusion}); ${message}`);
    for (const name of [...CHECKS, GATE]) {
      if (!active.checks[name]) active.checks[name] = await api.createCheck(active, name);
      await api.finishCheck(active.checks[name], name === GATE ? conclusion
        : jobs.find(job => job.name === name)?.conclusion === 'success' ? 'success' : conclusion, message);
    }
  }
  async function block(active, message) {
    api.info(`PR #${active.number}: quarantining head ${active.head} for the current enrollment; ${message}`);
    if (active.mergeStarted !== undefined) await api.finishCheck(active.checks[GATE], 'failure', message);
    else await complete(active, 'failure', message);
    state.blocked.push({ number: active.number, head: active.head, enrollment: active.enrollment, reason: message });
    state.active = null;
    await persist();
    api.notice(message);
  }
  function mergeWaitExpired(active) {
    const elapsed = now - Date.parse(active.mergeStarted);
    invariant(elapsed >= 0, 'Merge reconciliation clock moved before the recorded attempt.');
    return elapsed >= MERGE_CONFIRMATION_MS;
  }
  async function unconfirmedMerge(active, message) {
    if (active.phase === 'merge-unconfirmed') {
      api.info(`PR #${active.number}: merge outcome remains unconfirmed; lane held without retry.`);
      return 'merge-unconfirmed';
    }
    if (!mergeWaitExpired(active)) {
      api.info(`PR #${active.number}: awaiting merge visibility within the ten-minute confirmation window; lane held without retry.`);
      return 'merge-pending';
    }
    await api.finishCheck(active.checks[GATE], 'failure', message);
    active.phase = 'merge-unconfirmed';
    await persist();
    api.notice(message);
    return 'merge-unconfirmed';
  }
  async function dispatch(active) {
    api.info(`PR #${active.number}: preparing candidate-head App checks; head ${active.head}; base ${active.base}; ticket ${active.ticket}.`);
    for (const name of [...CHECKS, GATE]) {
      if (!active.checks[name]) {
        active.checks[name] = await api.createCheck(active, name);
        await persist();
      }
    }
    // Record dispatch intent before the API call. An ambiguous response must never cause duplicate dispatch.
    active.phase = 'dispatched';
    active.dispatchRecorded = true;
    await persist();
    api.info(`PR #${active.number}: dispatch intent persisted; requesting CI on trusted main for ticket ${active.ticket}.`);
    await api.dispatch(active);
    api.info(`PR #${active.number}: candidate CI dispatch accepted; waiting for its matching workflow run.`);
    return 'dispatched';
  }
  async function quiescent(active, runs) {
    for (const run of runs.filter(run => run.status !== 'completed')) {
      api.info(`PR #${active.number}: requesting cancellation of matching run ${run.id} (${run.status}).`);
      await api.cancel(run.id);
    }
    if (runs.some(run => run.status !== 'completed')) {
      api.info(`PR #${active.number}: lane held until matching runs are observed terminal.`);
      return false;
    }
    // An eventually consistent run listing cannot prove a recorded dispatch is quiescent.
    if (runs.length === 0 && active.dispatchRecorded) {
      api.info(`PR #${active.number}: recorded dispatch has no observable matching run; lane held, not assumed quiescent.`);
      if (now - Date.parse(active.created) >= 10 * 60_000)
        api.notice('Candidate dispatch has no observable run. The lane remains held pending cancellation or reviewed state repair.');
      return false;
    }
    api.info(`PR #${active.number}: candidate work is quiescent; lane may be released.`);
    return true;
  }

  if (state.active) {
    const active = state.active;
    api.info(`Reconciling active PR #${active.number}; phase ${active.phase}; head ${active.head}; base ${active.base}; ticket ${active.ticket}. Waiting PRs cannot preempt this lane.`);
    const pr = await api.pull(active.number);
    const runs = matchingRuns(await api.runs(active), active);
    api.info(`PR #${active.number}: found ${runs.length} workflow run(s) matching the active ticket.`);
    if (pr.merged === true) {
      api.info(`PR #${active.number}: GitHub reports merged; retiring candidate work without overwriting validated checks.`);
      active.phase = 'retiring';
      await persist();
      if (!(await quiescent(active, runs))) return 'retiring';
      state.active = null;
      await persist();
      return 'already-merged';
    }
    const current = ready(pr, api.repository) && active.base === base
      && await api.enrollment(active.number) === active.enrollment
      && (active.phase === 'updating' || pr.head.sha === active.head);
    if (!current || active.phase === 'retiring') {
      api.info(`PR #${active.number}: candidate is stale (head, main, readiness or enrollment changed) or already retiring; cancelling validation before lane release.`);
      active.phase = 'retiring';
      await persist();
      const terminal = await quiescent(active, runs);
      const message = 'Candidate head, base or queue intent changed. Validation is not merge evidence.';
      if (active.mergeStarted !== undefined) await api.finishCheck(active.checks[GATE], 'cancelled', message);
      else await complete(active, 'cancelled', message);
      if (!terminal) return 'retiring';
      state.active = null;
      await persist();
      return 'retired';
    }
    if (active.phase === 'updating') {
      if (pr.mergeable === false) { await block(active, 'Selected PR conflicts with main.'); return 'blocked'; }
      if (!(await api.containsBase(base, pr.head.sha))) {
        if (now - Date.parse(active.created) > 10 * 60_000) {
          await block(active, 'GitHub branch update did not complete.');
          return 'blocked';
        }
        api.info(`PR #${active.number}: branch update is not yet visible; waiting without repeating the update request.`);
        return 'updating';
      }
      active.head = pr.head.sha;
      active.phase = 'reserved';
      await persist();
      api.info(`PR #${active.number}: branch update observed; head ${active.head} contains current main.`);
    }
    if (active.phase === 'reserved') return dispatch(active);
    if (runs.length === 0) {
      if (MERGE_PHASES.includes(active.phase)) {
        return unconfirmedMerge(active,
          'Merge outcome is unconfirmed and candidate validation is not observable. The lane remains held; no automatic merge retry.');
      }
      if (now - Date.parse(active.created) > 10 * 60_000) {
        const message = 'No candidate workflow appeared after dispatch. The lane remains held pending cancellation or reviewed state repair.';
        active.phase = 'retiring';
        state.blocked.push({ number: active.number, head: active.head, enrollment: active.enrollment, reason: message });
        await persist();
        await complete(active, 'failure', message);
        api.notice(message);
        return 'retiring';
      }
      api.info(`PR #${active.number}: no matching workflow run is visible yet; waiting without redispatch.`);
      return 'waiting-for-run';
    }
    invariant(runs.length === 1, 'Multiple candidate runs share one ticket; refusing merge authority.');
    const run = runs[0];
    api.info(`PR #${active.number}: matching run ${run.id}; status ${run.status}; conclusion ${run.conclusion ?? 'pending'}; attempt ${run.run_attempt}.`);
    invariant(run.head_sha === active.base && run.run_attempt === 1,
      'Candidate workflow source or attempt changed; refusing merge authority.');
    if (run.status !== 'completed') {
      if (MERGE_PHASES.includes(active.phase)) {
        return unconfirmedMerge(active,
          'Merge outcome is unconfirmed and candidate validation is not terminal. The lane remains held; no automatic merge retry.');
      }
      return 'validating';
    }
    const jobs = await api.jobs(run.id);
    for (const name of JOBS) {
      const matches = jobs.filter(job => job.name === name);
      api.info(`Run ${run.id}: required job "${name}"; ${matches.length} match(es); conclusion(s): ${matches.map(job => job.conclusion ?? 'pending').join(', ') || 'missing'}.`);
    }
    if (run.conclusion !== 'success' || !successfulJobs(jobs)) {
      await block(active, 'Candidate validation failed, was cancelled, or omitted a required job.');
      return 'blocked';
    }
    const latest = await api.pull(active.number);
    if ((await api.main()) !== active.base || latest.head.sha !== active.head
      || !ready(latest, api.repository) || !(await api.containsBase(active.base, active.head))) {
      api.info(`PR #${active.number}: final source/base/readiness/ancestry revalidation changed; retiring without merge attestation.`);
      active.phase = 'retiring';
      await persist();
      return 'retiring';
    }
    if (await api.enrollment(active.number) !== active.enrollment) {
      api.info(`PR #${active.number}: Auto-merge enrollment changed during final revalidation; retiring without merge attestation.`);
      active.phase = 'retiring';
      await persist();
      return 'retiring';
    }
    if (!(await api.reviewReady(active.number))) {
      await block(active, 'Candidate review requirements changed. Re-enable auto-merge after review.');
      return 'blocked';
    }
    if (MERGE_PHASES.includes(active.phase)) {
      if (active.phase === 'merge-unconfirmed') {
        api.info(`PR #${active.number}: merge outcome remains unconfirmed; lane held without retry.`);
        return 'merge-unconfirmed';
      }
      if (!mergeWaitExpired(active)) {
        api.info(`PR #${active.number}: awaiting merge visibility within the ten-minute confirmation window; no automatic retry.`);
        return 'merge-pending';
      }
      if (active.phase === 'merge-refused') {
        await block(active, 'The protected merge request was refused and no merge was observed within ten minutes. Validated CI remains successful; inspect before re-enrollment.');
        return 'blocked';
      }
      return unconfirmedMerge(active,
        'The protected merge outcome remains unconfirmed after ten minutes. The lane remains held; inspect before withdrawing or renewing queue intent.');
    }
    // Record merge intent before checks can trigger native Auto-merge or the request can lose its response.
    active.phase = 'merging';
    active.mergeStarted = now.toISOString();
    await persist();
    api.info(`PR #${active.number}: exact required jobs and final head/base/enrollment/review/ancestry checks passed; merge intent persisted.`);
    await complete(active, 'success', `Validated head ${active.head} against main ${active.base}; run ${run.id}.`, jobs);
    // Strict branch protection and the expected head remain authoritative across the final API race.
    api.info(`PR #${active.number}: requesting protected squash merge with expected head ${active.head}.`);
    const result = await api.merge(active);
    invariant(typeof result?.merged === 'boolean', 'Unknown protected merge response; the lane remains held.');
    if (!result.merged) {
      active.phase = 'merge-refused';
      await persist();
      const observed = await api.pull(active.number);
      if (observed.merged !== true) {
        api.notice('Protected merge request was refused; awaiting native merge visibility without retrying or overwriting validated CI.');
        return 'merge-pending';
      }
    }
    state.active = null;
    await persist();
    api.info(`PR #${active.number}: merge confirmed; candidate lane released.`);
    return 'merged';
  }

  function candidatePriority(pr) {
    const rank = priority(pr);
    if (rank === null)
      api.notice(`PR #${pr.number} has invalid or conflicting queue priority labels; use at most one supported priority label.`);
    return rank;
  }
  const candidates = eligible.map(pr => ({ pr, rank: candidatePriority(pr) }))
    .filter(candidate => candidate.rank !== null)
    .sort((left, right) => left.rank - right.rank || left.pr.number - right.pr.number);
  api.info(`Selection order (${candidates.length} candidate(s); priority then PR number): ${candidates.map(({ pr, rank }) => `#${pr.number} (${['high', 'normal', 'low'][rank]})`).join(', ') || 'none'}.`);
  let selected = 0;
  for (const { pr: candidate, rank } of candidates) {
    api.info(`PR #${candidate.number}: checking live enrollment, quarantined revision, mergeability and reviews.`);
    const enrollment = await api.enrollment(candidate.number);
    if (!enrollment) {
      api.info(`PR #${candidate.number}: skipped; no current Auto-merge enrollment.`);
      continue;
    }
    const blocked = state.blocked.find(item => item.number === candidate.number && item.head === candidate.head.sha);
    if (blocked?.enrollment === enrollment) {
      api.info(`PR #${candidate.number}: skipped; head ${candidate.head.sha} is quarantined for this enrollment. Repair the head or intentionally re-enroll Auto-merge.`);
      continue;
    }
    state.blocked = state.blocked.filter(item => item.number !== candidate.number);
    const pr = await api.pull(candidate.number);
    const issue = readinessIssue(pr, api.repository);
    if (issue) {
      api.info(`PR #${pr.number}: skipped on live reread; ${issue}.`);
      continue;
    }
    if (typeof pr.mergeable !== 'boolean') {
      api.info(`PR #${pr.number}: skipped; mergeability is unknown.`);
      continue;
    }
    if (!(await api.reviewReady(pr.number))) {
      api.info(`PR #${pr.number}: skipped; required reviews are not satisfied.`);
      continue;
    }
    const currentRank = candidatePriority(pr);
    if (currentRank === null) continue;
    if (currentRank !== rank) {
      await persist();
      api.notice(`PR #${pr.number} priority changed during selection; refreshing queue order.`);
      return 'continuing';
    }
    if (selected === MAX_CANDIDATES) {
      api.info(`Reservation limit (${MAX_CANDIDATES}) reached; checkpointing before continuing selection.`);
      await persist();
      return 'continuing';
    }
    selected++;
    const active = { number: pr.number, head: pr.head.sha, base, ticket: ticket ?? randomUUID(),
      enrollment, created: now.toISOString(), phase: 'reserved', dispatchRecorded: false, checks: {} };
    state.active = active;
    await persist();
    api.info(`PR #${pr.number}: reserved the candidate lane (${['high', 'normal', 'low'][rank]} priority); head ${active.head}; base ${base}; ticket ${active.ticket}.`);
    if (pr.mergeable === false) { await block(active, 'Selected PR conflicts with main.'); continue; }
    if (!(await api.containsBase(base, active.head))) {
      active.phase = 'updating';
      await persist();
      api.info(`PR #${pr.number}: head does not contain current main; requesting GitHub branch update with expected head ${active.head}.`);
      await api.updateBranch(active);
      api.info(`PR #${pr.number}: branch update request accepted; retaining the lane until the update is observed.`);
      return 'updating';
    }
    return dispatch(active);
  }
  await persist();
  api.info(`Selection exhausted; no runnable candidate${selected > 0 ? ` after quarantining ${selected} conflicting revision(s)` : ''}.`);
  return selected > 0 ? 'blocked' : 'idle';
}

module.exports = { reconcile };
