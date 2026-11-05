const { randomUUID } = require('node:crypto');
const { SHA, invariant, validateState, readinessIssue, matchingRuns, requiredJobsState, terminalJobs,
  JOB_OBSERVATION_MS, DISPATCH_OBSERVATION_MS, MERGE_CONFIRMATION_MS } = require('./policy.cjs');
const { PROFILE, MAX_MEMBERS, CONTROL_PATH, digest, manifestFor, liveMember, ordered, gateMessage, GATE,
  BatchUpdateError } = require('./batch-policy.cjs');
const { priority } = require('./policy.cjs');

async function reconcileBatch(api, { now = new Date(), ticket = randomUUID() } = {}) {
  await api.verifyProtection(PROFILE);
  const state = validateState(await api.load());
  invariant(state.version === 2, 'Batch rollout requires an explicitly migrated idle, quiescent v2 state.');
  invariant(state.deliveryProfile !== 'independent-v1' || api.independent,
    'Durable independent delivery configuration differs from this controller; reservations remain closed.');
  const holding = ['draining', 'ready'].includes(state.deliveryTransition?.phase);
  invariant(!holding || !api.independent || state.deliveryTransition.phase === 'ready',
    'Independent configuration changed before the delivery boundary was prepared.');
  api.info(`Batch profile: ${state.active ? `ticket ${state.active.ticket}, phase ${state.active.phase}` : 'idle'}; reconciling live protected state.`);
  const save = async () => { validateState(state); await api.save(state); };
  const main = await api.main();
  if (state.active && state.active.kind !== 'batch')
    return reconcileSingleton(api, state, state.active.number, { now, ticket });
  if (!state.active) {
    // Independent delivery waits only until an admitted full source run starts, never for its result.
    if (state.completedTip && !state.completedTip.qualification
      && !(api.independent && state.completedTip.delivery === 'independent-v1'
        && state.completedTip.qualificationOwner)) {
      invariant(main === state.completedTip.sha, 'Main moved before completed-tip qualification; explicit recovery required.');
      const observation = await api.tipQualification(state.completedTip);
      if (observation?.provisional) return 'waiting-for-tip-qualification';
      if (observation) {
        state.completedTip.qualificationOwner = observation.owner;
        state.completedTip.qualificationRequested = true;
        if (observation.qualification) state.completedTip.qualification = observation.qualification;
        await save();
      }
      if (!state.completedTip.qualification && !(api.independent
        && state.completedTip.delivery === 'independent-v1' && observation && !observation.provisional)) {
        if (observation?.health?.status === 'BLOCKED_PUBLICATION') {
          const health = observation.health;
          api.notice(`BLOCKED_PUBLICATION: source ${health.source}; run ${health.run}; attempt ${health.attempt}; publication job ${health.job}; conclusion ${health.conclusion}; last durable progress: completed tip ${state.completedTip.sha}, qualification owner ${state.completedTip.qualificationOwner.run}, queue checkpoint ${api.commit}; authorized recovery required. Admission remains held; no retry dispatched.`);
          return 'blocked-publication';
        }
        if (!state.completedTip.qualificationRequested && !state.completedTip.qualificationOwner) {
          state.completedTip.qualificationRequested = true;
          await save();
          await api.dispatchQualification(state.completedTip);
        }
        return 'waiting-for-tip-qualification';
      }
    }
    if (holding) {
      api.info(`Independent delivery transition ${state.deliveryTransition.ticket}: ${state.deliveryTransition.phase}; new reservations held, existing work and qualification preserved.`);
      return state.deliveryTransition.phase === 'ready' ? 'delivery-ready' : 'delivery-draining';
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
      const member = { number: pr.number, head: pr.head.sha, enrollment, review, priority: priority(pr) };
      if (!liveMember(pr, member, api.repository, enrollment, review)) continue;
      const files = await api.files(pr.number, pr);
      invariant(Array.isArray(files) && files.every(file => typeof file === 'string' && file.length > 0),
        'Candidate file inventory is unavailable.');
      const singleton = files.length === 0 || files.some(file => CONTROL_PATH.test(file))
        || state.singletonRetries.some(item => item.number === member.number && item.head === member.head
          && item.enrollment === member.enrollment);
      if (singleton && members.length) break;
      if (singleton) {
        if (files.length === 0)
          api.info(`PR #${pr.number}: complete zero-file inventory; requiring exact-head singleton validation and actual merge confirmation.`);
        return reconcileSingleton(api, state, member.number, { now, ticket });
      }
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
  async function halt(reason, failureKind = 'stale', cancelWork = false) {
    active.phase = 'retiring';
    active.reason = reason;
    active.failureKind = failureKind;
    await save();
    api.notice(reason);
    if (cancelWork) await quiescent();
    await api.wake();
    return active.phase;
  }
  async function authorized(member) {
    const pr = await api.pull(member.number);
    invariant(pr && ['open', 'closed'].includes(pr.state) && typeof pr.draft === 'boolean'
      && typeof pr.base?.ref === 'string' && pr.base.ref.length > 0 && SHA.test(pr.head?.sha)
      && typeof pr.head?.repo?.full_name === 'string' && pr.head.repo.full_name.length > 0
      && (pr.auto_merge === null || ['squash', 'merge', 'rebase'].includes(pr.auto_merge?.merge_method)),
    'Batch live source or intent metadata is unavailable; staleness is not established.');
    invariant(pr.state !== 'open' || typeof pr.mergeable === 'boolean',
      'Batch mergeability is unavailable; authority and physical quiescence remain unconfirmed.');
    const enrollment = await api.enrollment(member.number);
    const review = await api.reviewState(member.number);
    invariant((enrollment === null || typeof enrollment === 'string' && Number.isFinite(Date.parse(enrollment)))
      && [null, 'APPROVED', 'REVIEW_REQUIRED', 'CHANGES_REQUESTED'].includes(review),
    'Batch enrollment or review authority is unavailable; staleness is not established.');
    return liveMember(pr, member, api.repository, enrollment, review);
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
      if (prefix === null) {
        const conflict = active.members[index];
        if (!index) return halt('First synthetic member conflicts with protected main; isolate its exact head.',
          'construction-conflict');
        // No manifest or dispatch exists yet. Seal only the contiguous compatible prefix.
        if (!state.singletonRetries.some(item => item.number === conflict.number && item.head === conflict.head
          && item.enrollment === conflict.enrollment)) state.singletonRetries.push({ ...conflict });
        active.members = active.members.slice(0, index);
        api.notice(`Batch ${active.ticket}: construction conflict at PR #${conflict.number}; sealing a new `
          + `${index}-member prefix manifest. The conflicting member retains its ordered singleton retry.`);
        break;
      }
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
    // Group failure is not independent member failure. Isolate with full singleton validations.
    const retry = active.failureKind === 'construction-conflict' ? active.members.slice(0, 1)
      : active.failureKind === undefined || active.failureKind === 'validation-failure' ? active.members : [];
    if (active.failureKind === 'construction-conflict' || retry.length > 1) {
      for (const member of retry)
        if (!state.singletonRetries.some(item => item.number === member.number && item.head === member.head
          && item.enrollment === member.enrollment)) {
          const { number, head, enrollment, review } = member;
          state.singletonRetries.push({ number, head, enrollment, review });
        }
    } else if (retry.length === 1) state.blocked.push({ number: retry[0].number, head: retry[0].head,
      enrollment: retry[0].enrollment, reason: active.reason });
    state.active = null;
    await save();
    await api.wake();
    return 'retired';
  }
  if (active.phase === 'dispatched') {
    let current = await api.main() === active.base;
    for (const member of active.members) current = await authorized(member) && current;
    if (!current)
      return halt('Batch source, protected base, enrollment, priority or reviews changed during validation.',
        'stale', true);
  }
  if (!observed) {
    if (now - Date.parse(active.created) >= DISPATCH_OBSERVATION_MS)
      return halt('Batch dispatch is unobservable; cancellation acknowledgement cannot release its lane.', 'unavailable');
    return 'waiting-for-run';
  }
  const { run, jobs } = observed;
  api.info(`Batch ${active.ticket}: observed run ${run.id}, status ${run.status}; ${jobs.length} exact-run job records.`);
  const outcome = requiredJobsState(jobs, active.inventory.jobs);
  if (outcome === 'failure' || run.status === 'completed' && run.conclusion !== 'success')
    return halt('Combined validation failed or duplicated required evidence; no independent member failure is inferred.',
      'validation-failure', true);
  if (run.status !== 'completed') return 'validating';
  if (outcome !== 'success' || !terminalJobs(jobs, active.inventory.jobs)) {
    active.jobObservationStarted ??= now.toISOString();
    await save();
    if (now - Date.parse(active.jobObservationStarted) >= JOB_OBSERVATION_MS)
      return halt('Batch required-job observation deadline expired; lane held until physical quiescence.', 'unavailable', true);
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
        if (api.independent) {
          invariant(active.inventory.schema === 'windows-12-delivery-v1',
            'Independent delivery requires the reviewed delivery workflow.');
          Object.assign(state.completedTip, { delivery: 'independent-v1', base: active.base,
            admission: manifestFor(active),
            validation: { run: run.id, attempt: 1, source: active.head, workflow: active.base,
              inventory: active.inventory.blob, schema: active.inventory.schema } });
          state.deliveryProfile = 'independent-v1';
        }
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
        if (api.independent) {
          const inventory = await api.trustedInventory(state.active.base);
          const runs = matchingRuns(await api.runs(state.active), state.active);
          invariant(inventory?.schema === 'windows-12-delivery-v1' && runs.length === 1
            && runs[0].status === 'completed' && runs[0].conclusion === 'success'
            && runs[0].run_attempt === 1 && runs[0].head_sha === state.active.base,
          'Independent singleton completion lacks trusted candidate proof.');
          const jobs = await api.jobs(runs[0].id, 1);
          invariant(requiredJobsState(jobs, inventory.jobs) === 'success' && terminalJobs(jobs, inventory.jobs),
            'Independent singleton candidate jobs are incomplete.');
          invariant(jobs.every(job => job.run_id === runs[0].id && job.run_attempt === 1
            && job.head_sha === state.active.base), 'Independent singleton job identity changed.');
          Object.assign(tip, { delivery: 'independent-v1', base: state.active.base,
            combined: state.active.head, validation: { run: runs[0].id, attempt: 1,
              source: state.active.head, workflow: state.active.base, inventory: inventory.blob, schema: inventory.schema } });
          state.deliveryProfile = 'independent-v1';
        }
      }
    }
    state.active = inner.active;
    state.blocked = inner.blocked;
    if (tip) state.completedTip = tip;
    await api.save(state);
  };
  view.independent = false;
  const singleton = new Proxy(api, { get(target, key) {
    if (Object.hasOwn(view, key)) return view[key];
    const value = Reflect.get(target, key);
    return typeof value === 'function' ? value.bind(target) : value;
  } });
  return require('./controller.cjs').reconcile(singleton, options);
}
