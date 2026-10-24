const { randomUUID } = require('node:crypto');
const { CHECKS, GATE, MERGE_PHASES, MERGE_CONFIRMATION_MS, invariant, validateState,
  ready, priority, matchingRuns, successfulJobs } = require('./policy.cjs');

const MAX_CANDIDATES = 10;

async function reconcile(api, options = {}) {
  const result = await reconcileOnce(api, options);
  if (['blocked', 'continuing', 'merged', 'already-merged', 'retired'].includes(result)) await api.wake();
  return result;
}

async function reconcileOnce(api, { now = new Date(), ticket } = {}) {
  await api.verifyProtection();
  const state = validateState(await api.load());
  const base = await api.main();
  const prs = await api.pulls();
  const eligible = prs.filter(pr => ready(pr, api.repository));
  state.blocked = state.blocked.filter(item => eligible.some(pr =>
    pr.number === item.number && pr.head.sha === item.head));

  async function persist() { validateState(state); await api.save(state); }
  async function complete(active, conclusion, message, jobs = []) {
    for (const name of [...CHECKS, GATE]) {
      if (!active.checks[name]) active.checks[name] = await api.createCheck(active, name);
      await api.finishCheck(active.checks[name], name === GATE ? conclusion
        : jobs.find(job => job.name === name)?.conclusion === 'success' ? 'success' : conclusion, message);
    }
  }
  async function block(active, message) {
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
    if (active.phase === 'merge-unconfirmed') return 'merge-unconfirmed';
    if (!mergeWaitExpired(active)) return 'merge-pending';
    await api.finishCheck(active.checks[GATE], 'failure', message);
    active.phase = 'merge-unconfirmed';
    await persist();
    api.notice(message);
    return 'merge-unconfirmed';
  }
  async function dispatch(active) {
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
    await api.dispatch(active);
    return 'dispatched';
  }
  async function quiescent(active, runs) {
    for (const run of runs.filter(run => run.status !== 'completed')) await api.cancel(run.id);
    if (runs.some(run => run.status !== 'completed')) return false;
    // An eventually consistent run listing cannot prove a recorded dispatch is quiescent.
    if (runs.length === 0 && active.dispatchRecorded) {
      if (now - Date.parse(active.created) >= 10 * 60_000)
        api.notice('Candidate dispatch has no observable run. The lane remains held pending cancellation or reviewed state repair.');
      return false;
    }
    return true;
  }

  if (state.active) {
    const active = state.active;
    const pr = await api.pull(active.number);
    const runs = matchingRuns(await api.runs(active), active);
    if (pr.merged === true) {
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
        return 'updating';
      }
      active.head = pr.head.sha;
      active.phase = 'reserved';
      await persist();
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
      return 'waiting-for-run';
    }
    invariant(runs.length === 1, 'Multiple candidate runs share one ticket; refusing merge authority.');
    const run = runs[0];
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
    if (run.conclusion !== 'success' || !successfulJobs(jobs)) {
      await block(active, 'Candidate validation failed, was cancelled, or omitted a required job.');
      return 'blocked';
    }
    const latest = await api.pull(active.number);
    if ((await api.main()) !== active.base || latest.head.sha !== active.head
      || !ready(latest, api.repository) || !(await api.containsBase(active.base, active.head))) {
      active.phase = 'retiring';
      await persist();
      return 'retiring';
    }
    if (await api.enrollment(active.number) !== active.enrollment) {
      active.phase = 'retiring';
      await persist();
      return 'retiring';
    }
    if (!(await api.reviewReady(active.number))) {
      await block(active, 'Candidate review requirements changed. Re-enable auto-merge after review.');
      return 'blocked';
    }
    if (MERGE_PHASES.includes(active.phase)) {
      if (active.phase === 'merge-unconfirmed') return 'merge-unconfirmed';
      if (!mergeWaitExpired(active)) return 'merge-pending';
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
    await complete(active, 'success', `Validated head ${active.head} against main ${active.base}; run ${run.id}.`, jobs);
    // Strict branch protection and the expected head remain authoritative across the final API race.
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
  let selected = 0;
  for (const { pr: candidate, rank } of candidates) {
    const enrollment = await api.enrollment(candidate.number);
    if (!enrollment) continue;
    const blocked = state.blocked.find(item => item.number === candidate.number && item.head === candidate.head.sha);
    if (blocked?.enrollment === enrollment) continue;
    state.blocked = state.blocked.filter(item => item.number !== candidate.number);
    const pr = await api.pull(candidate.number);
    if (!ready(pr, api.repository) || typeof pr.mergeable !== 'boolean' || !(await api.reviewReady(pr.number))) continue;
    const currentRank = candidatePriority(pr);
    if (currentRank === null) continue;
    if (currentRank !== rank) {
      await persist();
      api.notice(`PR #${pr.number} priority changed during selection; refreshing queue order.`);
      return 'continuing';
    }
    if (selected === MAX_CANDIDATES) {
      await persist();
      return 'continuing';
    }
    selected++;
    const active = { number: pr.number, head: pr.head.sha, base, ticket: ticket ?? randomUUID(),
      enrollment, created: now.toISOString(), phase: 'reserved', dispatchRecorded: false, checks: {} };
    state.active = active;
    await persist();
    if (pr.mergeable === false) { await block(active, 'Selected PR conflicts with main.'); continue; }
    if (!(await api.containsBase(base, active.head))) {
      active.phase = 'updating';
      await persist();
      await api.updateBranch(active);
      return 'updating';
    }
    return dispatch(active);
  }
  await persist();
  return selected > 0 ? 'blocked' : 'idle';
}

module.exports = { reconcile };
