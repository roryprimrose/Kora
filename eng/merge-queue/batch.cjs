const { randomUUID } = require('node:crypto');
const { invariant, validateState, readinessIssue, matchingRuns, requiredJobsState, terminalJobs,
  JOB_OBSERVATION_MS, DISPATCH_OBSERVATION_MS, MERGE_CONFIRMATION_MS } = require('./policy.cjs');
const { PROFILE, MAX_MEMBERS, CONTROL_PATH, digest, manifestFor, liveMember, ordered, gateMessage, GATE,
  BatchUpdateError } = require('./batch-policy.cjs');
const { priority } = require('./policy.cjs');

async function reconcileBatch(api, { now = new Date(), ticket = randomUUID() } = {}) {
  await api.verifyProtection(PROFILE);
  const state = validateState(await api.load());
  invariant(state.version === 2, 'Batch rollout requires an explicitly migrated idle, quiescent v2 state.');
  api.info(`Batch profile: ${state.active ? `ticket ${state.active.ticket}, phase ${state.active.phase}` : 'idle'}; reconciling live protected state.`);
  const save = async () => { validateState(state); await api.save(state); };
  const main = await api.main();
  if (state.active && state.active.kind !== 'batch')
    return reconcileSingleton(api, state, state.active.number, { now, ticket });
  if (!state.active) {
    // Hold the lane for actual-tip qualification/publication, even under a sustained arrival rate.
    if (state.completedTip && !state.completedTip.qualification) {
      invariant(main === state.completedTip.sha, 'Main moved before completed-tip qualification; explicit recovery required.');
      const qualification = await api.tipQualification(state.completedTip);
      if (!qualification) {
        if (!state.completedTip.qualificationRequested) {
          state.completedTip.qualificationRequested = true;
          await save();
          await api.dispatchQualification(state.completedTip);
        }
        return 'waiting-for-tip-qualification';
      }
      state.completedTip.qualification = qualification;
      await save();
    }
    const members = [];
    const prs = await api.pulls();
    for (const pr of prs) {
      if (readinessIssue(pr, api.repository) === null && priority(pr) === null)
        api.notice(`PR #${pr.number} has invalid or conflicting queue priority labels; use one supported priority label.`);
    }
    for (const candidate of ordered(prs, api.repository)) {
      const pr = await api.pull(candidate.number);
      if (priority(pr) !== priority(candidate)) {
        await api.wake();
        return 'continuing';
      }
      if (pr.head.sha !== candidate.head.sha || pr.mergeable !== true) continue;
      const enrollment = await api.enrollment(pr.number);
      const review = await api.reviewState(pr.number);
      if (!enrollment || ![null, 'APPROVED'].includes(review)) continue;
      if (state.blocked.some(item => item.number === pr.number && item.head === pr.head.sha
        && item.enrollment === enrollment)) continue;
      const member = { number: pr.number, head: pr.head.sha, enrollment, review };
      if (!liveMember(pr, member, api.repository, enrollment, review)) continue;
      const files = await api.files(pr.number);
      invariant(Array.isArray(files) && files.length > 0 && files.every(file => typeof file === 'string'),
        'Candidate file inventory is unavailable.');
      const singleton = files.some(file => CONTROL_PATH.test(file))
        || state.singletonRetries.some(item => item.number === member.number && item.head === member.head
          && item.enrollment === member.enrollment);
      if (singleton && members.length) break;
      if (singleton) return reconcileSingleton(api, state, member.number, { now, ticket });
      members.push(member);
      if (singleton || members.length === MAX_MEMBERS) break;
    }
    if (!members.length) return 'idle';
    const inventory = await api.trustedInventory(main);
    invariant(inventory, 'Trusted batch workflow inventory is unavailable.');
    state.active = { kind: 'batch', ticket, base: main, created: now.toISOString(), members,
      phase: 'constructing', dispatchRecorded: false, prefixes: [], inventory };
    await save();
    api.info(`Reserved batch ${ticket}; base ${main}; ordered members ${members.map(member => member.number).join(',')}.`);
  }
  const active = state.active;
  invariant(Number.isFinite(now.valueOf()) && now >= Date.parse(active.created),
    'Batch reconciliation clock moved before its reservation.');
  const merged = () => active.members.filter(member => member.merged).length;
  const expectedMain = () => merged() ? active.members[merged() - 1].merged : active.base;
  async function halt(reason) {
    active.phase = 'retiring';
    active.reason = reason;
    await save();
    api.notice(reason);
    await api.wake();
    return active.phase;
  }
  async function authorized(member) {
    const pr = await api.pull(member.number);
    return liveMember(pr, member, api.repository, await api.enrollment(member.number),
      await api.reviewState(member.number));
  }
  async function recoverRequired(reason) {
    active.phase = 'recovery-required';
    active.reason = reason;
    await save();
    api.notice(reason);
    return 'recovery-required';
  }
  const recovering = active.phase === 'recovery-required';
  if (recovering) {
    const pending = active.members[merged()];
    if (!pending?.mergeIntent || (await api.pull(pending.number)).merged !== true) return 'recovery-required';
  }

  if (active.phase === 'constructing') {
    if (await api.main() !== active.base) return halt('Main changed before batch construction.');
    for (const member of active.members)
      if (!await authorized(member)) return halt('Batch enrollment, source or reviews changed before construction.');
    for (let index = active.prefixes.length; index < active.members.length; index++) {
      const prefix = await api.syntheticPrefix(active, index);
      if (prefix === null) return halt('Synthetic batch has a merge conflict; no original PR was merged.');
      active.prefixes.push(prefix);
      await save();
      api.info(`Batch ${active.ticket}: prefix ${index + 1}; commit ${prefix.sha}; tree ${prefix.tree}.`);
    }
    active.head = active.prefixes.at(-1).sha;
    active.phase = 'reserved';
    active.digest = digest(manifestFor(active));
    await save();
  }
  if (active.phase === 'reserved') {
    if (await api.main() !== active.base) return halt('Main changed before batch dispatch.');
    for (const member of active.members)
      if (!await authorized(member)) return halt('Batch authority changed before dispatch.');
    active.dispatchRecorded = true;
    active.phase = 'dispatched';
    await save();
    await api.dispatch(active);
    api.info(`Batch ${active.ticket}: combined source ${active.head}; manifest ${active.digest}; dispatch intent recorded.`);
    return 'dispatched';
  }

  const runs = matchingRuns(await api.runs(active), active);
  invariant(runs.length <= 1, 'Duplicate batch runs; lane and release block retained.');
  let observed;
  if (runs.length) {
    const run = runs[0];
    invariant(run.head_sha === active.base && run.run_attempt === 1, 'Batch workflow source or attempt changed.');
    const jobs = await api.jobs(run.id, run.run_attempt);
    invariant(Array.isArray(jobs) && jobs.every(job => Number.isSafeInteger(job.id) && job.id > 0
      && job.run_id === run.id && job.run_attempt === 1 && job.head_sha === active.base)
      && new Set(jobs.map(job => job.id)).size === jobs.length, 'Invalid batch job identity.');
    observed = { run, jobs };
  }
  async function quiescent() {
    if (!observed) return !active.dispatchRecorded;
    if (observed.run.status !== 'completed' || (!terminalJobs(observed.jobs, active.inventory.jobs)
      && !await api.rejectedAdmission(active.base, observed.run, observed.jobs))) {
      await api.cancel(observed.run.id);
      return false;
    }
    return true;
  }
  if (active.phase === 'retiring') {
    if (!await quiescent()) return 'retiring';
    if (merged()) return recoverRequired('Partially integrated batch is quiescent; explicit recovery required.');
    for (const member of active.members) {
      if (member.check) await api.finishCheck(member.check, 'cancelled', active.reason);
    }
    if (active.phase === 'dispatched') {
      let current = await api.main() === active.base;
      for (const member of active.members) current = await authorized(member) && current;
      if (!current) {
        await halt('Batch source, protected base, enrollment or reviews changed during validation.');
        await quiescent();
        return 'retiring';
      }
    }
    // Group failure is not independent member failure. Isolate with full singleton validations.
    if (active.members.length > 1) state.singletonRetries.push(...active.members.map(({ number, head, enrollment, review }) =>
      ({ number, head, enrollment, review })));
    else state.blocked.push({ number: active.members[0].number, head: active.members[0].head,
      enrollment: active.members[0].enrollment, reason: active.reason });
    state.active = null;
    await save();
    await api.wake();
    return 'retired';
  }
  if (!observed) {
    if (now - Date.parse(active.created) >= DISPATCH_OBSERVATION_MS)
      return halt('Batch dispatch is unobservable; cancellation acknowledgement cannot release its lane.');
    return 'waiting-for-run';
  }
  const { run, jobs } = observed;
  api.info(`Batch ${active.ticket}: observed run ${run.id}, status ${run.status}; ${jobs.length} exact-run job records.`);
  if (run.status !== 'completed') return 'validating';
  const outcome = requiredJobsState(jobs, active.inventory.jobs);
  if (run.conclusion !== 'success' || outcome === 'failure')
    return halt('Combined validation failed or duplicated required evidence; no independent member failure is inferred.');
  if (outcome !== 'success' || !terminalJobs(jobs, active.inventory.jobs)) {
    active.jobObservationStarted ??= now.toISOString();
    await save();
    if (now - Date.parse(active.jobObservationStarted) >= JOB_OBSERVATION_MS)
      return halt('Batch required-job observation deadline expired; lane held until physical quiescence.');
    return 'waiting-for-jobs';
  }
  if (active.phase === 'dispatched') {
    active.phase = 'proven';
    delete active.jobObservationStarted;
    await save();
  }

  const index = merged();
  const member = active.members[index];
  if (!member) return recoverRequired('Unexpected exhausted batch without completed-tip receipt.');
  const prefix = active.prefixes[index];
  if (member.updateIntent && !member.updatedHead) {
    let updated;
    try { updated = await api.expectedUpdate(member, member.updateIntent.base, prefix.tree); }
    catch (error) {
      if (!(error instanceof BatchUpdateError)) throw error;
      return recoverRequired(error.message);
    }
    if (!updated) {
      if (now - Date.parse(member.updateIntent.created) >= MERGE_CONFIRMATION_MS)
        return recoverRequired('Expected branch update is unconfirmed; update will not be retried.');
      return 'updating';
    }
    member.updatedHead = updated;
    delete member.check;
    active.phase = 'proven';
    await save();
  }
  const pr = await api.pull(member.number);
  if (member.mergeIntent) {
    // Check the irreversible outcome before source/enrollment: a successful merge removes Auto-merge intent.
    if (pr.merged === true) {
      const commit = await api.commitInfo(pr.merge_commit_sha);
      const actualMain = await api.main();
      if (!commit || commit.parents.length !== 1 || commit.parents[0] !== member.mergeIntent.base
        || commit.tree !== prefix.tree || !await api.containsBase(pr.merge_commit_sha, actualMain)
        || actualMain !== pr.merge_commit_sha && !recovering)
        return recoverRequired('Actual merged prefix parent, tree or main reachability differs from the frozen manifest.');
      member.merged = pr.merge_commit_sha;
      active.phase = 'proven';
      await save();
      api.info(`Batch ${active.ticket}: confirmed original PR #${member.number} merged at ${member.merged}; prefix ${index + 1}.`);
      if (recovering) return recoverRequired('Late actual merge recorded; explicit reviewed suffix recovery is still required.');
      if (index + 1 === active.members.length) {
        invariant(commit.tree === active.prefixes.at(-1).tree, 'Completed main differs from combined candidate tree.');
        state.completedTip = { sha: member.merged, tree: commit.tree, ticket: active.ticket,
          combined: active.head, manifest: active.digest,
          members: active.members.map(item => ({ number: item.number, source: item.head,
            admittedHead: item.mergeIntent.head, merged: item.merged })) };
        state.active = null;
        state.singletonRetries = state.singletonRetries.filter(item => !active.members.some(done =>
          done.number === item.number && done.head === item.head && done.enrollment === item.enrollment));
        await save();
        await api.wake();
        return 'batch-completed';
      }
      await api.wake();
      return 'prefix-confirmed';
    }
    if (!await authorized(member) || await api.main() !== member.mergeIntent.base)
      return recoverRequired('Pending merge authority or actual main changed; ambiguous merge will not be retried.');
    if (now - Date.parse(member.mergeIntent.created) >= MERGE_CONFIRMATION_MS)
      return recoverRequired('Merge outcome remains ambiguous; no automatic retry.');
    return 'merge-pending';
  }
  if (await api.main() !== expectedMain()) return recoverRequired('Unexpected main movement; release remains blocked.');
  for (const remaining of active.members.slice(index)) {
    if (!await authorized(remaining)) {
      if (member.check) await api.finishCheck(member.check, 'failure', 'Batch authority changed; admission withdrawn.');
      return merged() ? recoverRequired('Unmerged suffix changed or withdrew intent; explicit recovery required.')
        : halt('Batch authority changed before its first merge.');
    }
  }
  if (!await api.containsBase(expectedMain(), member.updatedHead || member.head)) {
    if (!member.updateIntent) {
      if (!member.check) {
        member.check = await api.createCheck({ head: member.head, base: active.base,
          number: member.number, ticket: active.ticket }, GATE);
        await save();
      }
      member.updateIntent = { head: member.head, base: expectedMain(), created: now.toISOString() };
      active.phase = 'updating';
      await save();
      await api.updateBranch({ number: member.number, head: member.head });
      return 'updating';
    }
    return 'updating';
  }
  if (!await api.memberTree(member.updatedHead || member.head, prefix.tree))
    return recoverRequired('Member candidate tree differs from its precomputed prefix.');
  if (!member.check) {
    member.check = await api.createCheck({ head: member.updatedHead || member.head, base: active.base,
      number: member.number, ticket: active.ticket }, GATE);
    await save();
  }
  if (!await authorized(member) || await api.main() !== expectedMain())
    return recoverRequired('Final batch merge fence changed.');
  member.mergeIntent = { head: member.updatedHead || member.head, base: expectedMain(), created: now.toISOString() };
  active.phase = 'merging';
  await save();
  await api.finishCheck(member.check, 'success', gateMessage(active, index, run.id));
  const afterAttestation = await api.pull(member.number);
  if (afterAttestation.merged === true) {
    await api.wake();
    return 'merge-pending';
  }
  if (!await authorized(member) || await api.main() !== member.mergeIntent.base) {
    await api.finishCheck(member.check, 'failure', 'Authority changed after attestation; protected merge request withheld.');
    return recoverRequired('Final authority changed after attestation; native merge outcome must be reconciled.');
  }
  api.info(`Batch ${active.ticket}: requesting protected original PR #${member.number} squash with expected head ${member.mergeIntent.head}.`);
  await api.merge({ number: member.number, head: member.mergeIntent.head });
  // Even an affirmative response is not a merged-prefix receipt.
  await api.wake();
  return 'merge-pending';
}

module.exports = { reconcileBatch };

async function reconcileSingleton(api, state, number, options) {
  // Keep the existing exact-head path for controller/workflow/release changes and isolated retries.
  const view = {
    verifyProtection: () => api.verifyProtection(PROFILE),
    load: async () => ({ version: 1, active: state.active, blocked: state.blocked }),
    pulls: async () => (await api.pulls()).filter(pr => pr.number === number),
  };
  view.save = async inner => {
    let tip;
    if (state.active && inner.active === null) {
      const pr = await api.pull(number);
      if (pr.merged === true) {
        const commit = await api.commitInfo(pr.merge_commit_sha);
        invariant(commit.parents.length === 1 && commit.parents[0] === state.active.base
          && await api.memberTree(state.active.head, commit.tree)
          && await api.main() === pr.merge_commit_sha
          && await api.containsBase(pr.merge_commit_sha, await api.main()),
        'Singleton actual merge differs from validated source/base; release remains blocked.');
        tip = { sha: pr.merge_commit_sha, tree: commit.tree, ticket: state.active.ticket };
      }
    }
    state.active = inner.active;
    state.blocked = inner.blocked;
    if (tip) state.completedTip = tip;
    await api.save(state);
  };
  const singleton = new Proxy(api, { get(target, key) {
    if (Object.hasOwn(view, key)) return view[key];
    const value = Reflect.get(target, key);
    return typeof value === 'function' ? value.bind(target) : value;
  } });
  return require('./controller.cjs').reconcile(singleton, options);
}
