const { SHA, TICKET, DISPATCH_OBSERVATION_MS, invariant, controllerOperation, matchingRuns } = require('./policy.cjs');

function unobservable(runs, active) {
  invariant(Array.isArray(runs) && runs.every(run => run && Number.isSafeInteger(run.id) && run.id > 0
    && typeof run.display_title === 'string' && run.event === 'workflow_dispatch' && run.head_branch === 'main')
    && new Set(runs.map(run => run.id)).size === runs.length, 'Invalid or duplicate recovery run observations.');
  invariant(matchingRuns(runs, active).length === 0, 'The recovery ticket has an observable run; use ordinary reconciliation.');
  invariant(runs.every(run => run.status === 'completed'), 'Nonterminal workflow work prevents reviewed recovery.');
}

async function recover(api, { context, inputs, attempt, now = new Date() }) {
  invariant(controllerOperation(inputs) === 'recover-unobservable-dispatch'
    && context.eventName === 'workflow_dispatch' && context.ref === 'refs/heads/main' && attempt === 1
    && Number.isSafeInteger(context.runId) && context.runId > 0, 'Recovery requires a first-attempt manual run on trusted main.');
  invariant(inputs.acknowledge_uncertain_dispatch === true
    && typeof inputs.recovery_checkpoint === 'string' && SHA.test(inputs.recovery_checkpoint)
    && typeof inputs.recovery_ticket === 'string' && TICKET.test(inputs.recovery_ticket),
  'Recovery requires the exact checkpoint, ticket and explicit acknowledgement of reviewed dispatch uncertainty.');
  await api.verifyProtection();
  invariant(await api.administrator(context.actor), 'Recovery requires a live repository administrator.');
  const state = await api.load();
  invariant(api.commit === inputs.recovery_checkpoint, 'Recovery checkpoint changed; no state repair performed.');
  const active = state.active;
  invariant(active?.kind !== 'batch' && active?.ticket === inputs.recovery_ticket && active.phase === 'retiring'
    && active.dispatchRecorded === true && active.mergeStarted === undefined,
  'Recovery is limited to the exact retiring, unobservable dispatch; merge uncertainty requires separate review.');
  invariant(Number.isFinite(now.valueOf()) && now - Date.parse(active.created) >= DISPATCH_OBSERVATION_MS,
    'The dispatch observation budget has not elapsed.');
  api.info(`Administrator ${context.actor} requested reviewed recovery of PR #${active.number}; ticket ${active.ticket}; checkpoint ${api.commit}; workflow run ${context.runId}.`);
  unobservable(await api.runs(active), active);
  invariant(await api.administrator(context.actor), 'Recovery administrator authority changed.');
  unobservable(await api.runs(active), active);
  if (!state.blocked.some(item => item.number === active.number
    && item.head === active.head && item.enrollment === active.enrollment)) {
    state.blocked.push({ number: active.number, head: active.head, enrollment: active.enrollment,
      reason: `Reviewed administrator recovery of unobservable dispatch ${active.ticket}; workflow run ${context.runId}.` });
  }
  state.active = null;
  await api.save(state, `ci: recover reviewed unobservable merge queue dispatch\n\nAdministrator: ${context.actor}\nWorkflow run: ${context.runId}\nTicket: ${active.ticket}\nPrevious checkpoint: ${inputs.recovery_checkpoint}`);
  const expectedCommit = api.commit;
  const observed = await api.load();
  invariant(api.commit === expectedCommit && api.commit !== inputs.recovery_checkpoint
    && JSON.stringify(observed) === JSON.stringify(state), 'Recovery checkpoint readback differs; inspect state before further action.');
  api.info(`Reviewed recovery checkpoint ${api.commit} verified; failed revision retained and existing check results unchanged.`);
  await api.wake();
  return 'recovered';
}

async function changeBatchState(api, { context, inputs, attempt, now = new Date() }) {
  const operation = controllerOperation(inputs);
  invariant(['migrate-batch-v1', 'rollback-batch-v1', 'repair-batch', 'resume-batch'].includes(operation)
    && context.eventName === 'workflow_dispatch' && context.ref === 'refs/heads/main' && attempt === 1
    && Number.isSafeInteger(context.runId) && context.runId > 0 && Number.isFinite(now.valueOf())
    && inputs.acknowledge_batch_change === true && SHA.test(inputs.recovery_checkpoint),
  'Batch state changes require explicit first-attempt administrator acknowledgement and exact checkpoint.');
  invariant(await api.administrator(context.actor), 'Batch state changes require a live repository administrator.');
  const state = await api.load();
  invariant(api.commit === inputs.recovery_checkpoint, 'Batch recovery checkpoint changed.');
  invariant(!['migrate-batch-v1', 'rollback-batch-v1'].includes(operation)
    || !state.deliveryTransition || state.deliveryTransition.phase === 'aborted',
  'Profile migration cannot erase an independent delivery transition.');
  invariant(await api.ciQuiescent(), 'Batch state change requires physically quiescent CI.');
  const main = await api.main();
  const commit = await api.commitInfo(main);
  let next;
  if (operation === 'migrate-batch-v1') {
    await api.verifyProtection();
    invariant(state.version === 1 && state.active === null, 'Only idle v1 state may migrate.');
    const { randomUUID } = require('node:crypto');
    next = { version: 2, profile: 'batch-v1', active: null, blocked: state.blocked, singletonRetries: [],
      completedTip: { sha: main, tree: commit.tree, ticket: randomUUID() }, history: [] };
  } else {
    await api.verifyProtection('batch-v1');
    invariant(state.version === 2, 'Batch recovery requires v2 state.');
    if (operation === 'rollback-batch-v1') {
      invariant(state.active === null && state.completedTip?.sha === main && state.completedTip.qualification,
        'Rollback requires the idle, qualified completed main tip.');
      next = { version: 1, active: null, blocked: state.blocked, batchHistory: state };
    } else {
      invariant(state.active?.kind === 'batch' && state.active.ticket === inputs.recovery_ticket
        && state.active.phase === 'recovery-required',
        'Repair requires the exact held recovery batch.');
      const active = state.active;
      if (active.dispatchRecorded) {
        const runs = matchingRuns(await api.runs(active), active);
        invariant(runs.length === 1 && runs[0].head_sha === active.base && runs[0].run_attempt === 1
          && runs[0].status === 'completed', 'Repair requires exact terminal owned workflow evidence.');
        const jobs = await api.jobs(runs[0].id, 1);
        invariant(jobs.every(job => job.run_id === runs[0].id && job.run_attempt === 1
          && job.head_sha === active.base) && new Set(jobs.map(job => job.id)).size === jobs.length
          && (require('./policy.cjs').terminalJobs(jobs, active.inventory.jobs)
            || await api.rejectedAdmission(active.base, runs[0], jobs)),
        'Repair cannot release missing, duplicate or nonterminal owned jobs.');
      }
      const prefix = active.members.filter(member => member.merged);
      const recordedMain = prefix.length ? prefix.at(-1).merged : active.base;
      invariant(main === recordedMain || operation === 'repair-batch' && inputs.recovery_main === main
        && SHA.test(inputs.recovery_main) && await api.containsBase(recordedMain, main),
      'Unexpected main requires explicit exact-main repair and recorded-prefix ancestry; never discard advances.');
      if (prefix.length) invariant((await api.commitInfo(recordedMain)).tree === active.prefixes[prefix.length - 1].tree,
        'Recorded merged prefix tree differs from actual main.');
      invariant(!active.members.some(member => member.mergeIntent && !member.merged),
        'An unresolved merge request cannot be retried or erased by recovery.');
      if (operation === 'resume-batch') {
        const { liveMember } = require('./batch-policy.cjs');
        for (const member of active.members.slice(prefix.length))
          invariant(liveMember(await api.pull(member.number), member, api.repository,
            await api.enrollment(member.number), await api.reviewState(member.number)),
          'Only unchanged, still-authorized suffix members may resume.');
        if (prefix.length === active.members.length) {
          invariant(commit.tree === active.prefixes.at(-1).tree, 'Completed actual main differs from combined proof.');
          next = { ...state, active: null, completedTip: { sha: main, tree: commit.tree, ticket: active.ticket,
            combined: active.head, manifest: active.digest } };
        } else {
          invariant(active.head && active.dispatchRecorded, 'Resume requires a fully constructed, dispatched manifest.');
          next = { ...state, active: { ...active, phase: 'proven', reason: undefined } };
        }
      } else {
      // Start a NEW full proof on actual main; do not reuse combined proof or replay withdrawn members.
      next = { ...state, active: null, completedTip: null,
        history: [...(state.history ?? []), { batch: active, actor: context.actor,
          run: context.runId, checkpoint: api.commit, actualMain: main, repairedAt: now.toISOString() }] };
      }
    }
  }
  invariant(await api.administrator(context.actor) && await api.main() === main && await api.ciQuiescent(),
    'Batch state change authority, main or physical quiescence changed.');
  await api.save(next, `ci: ${operation}\n\nAdministrator: ${context.actor}\nWorkflow run: ${context.runId}\nPrevious checkpoint: ${inputs.recovery_checkpoint}`);
  const expected = api.commit;
  invariant(JSON.stringify(await api.load()) === JSON.stringify(next) && api.commit === expected
    && api.commit !== inputs.recovery_checkpoint, 'Batch state readback changed.');
  return operation;
}

async function changeDeliveryState(api, { context, inputs, attempt, profile, now = new Date() }) {
  const operation = controllerOperation(inputs);
  invariant(['hold-independent-delivery', 'prepare-independent-delivery',
    'resume-independent-delivery', 'abort-independent-delivery'].includes(operation)
    && profile === 'batch-v1' && context.eventName === 'workflow_dispatch' && context.ref === 'refs/heads/main' && attempt === 1
    && Number.isSafeInteger(context.runId) && context.runId > 0 && Number.isFinite(now.valueOf())
    && inputs.acknowledge_batch_change === true && SHA.test(inputs.recovery_checkpoint)
    && !inputs.recovery_main && inputs.acknowledge_uncertain_dispatch !== true,
  'Delivery changes require explicit first-attempt administrator acknowledgement and unmixed exact-checkpoint inputs.');
  await api.verifyProtection('batch-v1');
  invariant(await api.administrator(context.actor), 'Delivery changes require a live repository administrator.');
  const state = await api.load();
  invariant(api.commit === inputs.recovery_checkpoint, 'Delivery checkpoint changed; hold not modified.');
  invariant(state.version === 2 && state.profile === 'batch-v1', 'Delivery transition requires migrated batch state.');
  const resume = operation === 'resume-independent-delivery';
  invariant(Boolean(api.independent) === resume && state.deliveryProfile === undefined,
    'Delivery configuration must remain serial until prepared, then independent only for resume.');
  const starting = operation === 'hold-independent-delivery';
  let change = state.deliveryTransition;
  if (starting) {
    invariant(!inputs.recovery_ticket && (!change || change.phase === 'aborted'),
      'A delivery transition already owns the reservation hold.');
    change = { ticket: require('node:crypto').randomUUID(), actor: context.actor,
      run: context.runId, checkpoint: api.commit, created: now.toISOString(), phase: 'draining' };
    state.deliveryTransition = change;
  } else {
    invariant(change && ['draining', 'ready'].includes(change.phase)
      && inputs.recovery_ticket === change.ticket
      && change.actor.toLowerCase() === context.actor.toLowerCase(),
    'The exact delivery hold and its administrator owner are required.');
  }
  let main;
  if (operation === 'prepare-independent-delivery' || resume) {
    main = await api.main();
    invariant(state.active === null && state.completedTip?.sha === main && state.completedTip.qualification,
      'Delivery requires no active candidate and the qualified current completed tip.');
    if (resume) invariant(change.phase === 'ready' && change.source === main
      && change.tree === state.completedTip.tree
      && JSON.stringify(change.qualification) === JSON.stringify(state.completedTip.qualification),
    'Prepared delivery source or qualification changed; reservations remain held.');
    const observation = await api.tipQualification(state.completedTip);
    invariant(observation?.terminal === true && observation.provisional === false
      && JSON.stringify(observation.qualification) === JSON.stringify(state.completedTip.qualification),
    'Delivery qualification is missing, failed, pending or differs from its durable first-attempt receipt.');
    invariant((await api.commitInfo(main)).tree === state.completedTip.tree && await api.ciQuiescent(),
      'Delivery requires the exact source tree and physically quiescent CI/publication.');
    if (resume) {
      state.deliveryProfile = 'independent-v1';
      Object.assign(change, { phase: 'completed', completed: now.toISOString(), resumeRun: context.runId });
    } else {
      Object.assign(change, { phase: 'ready', source: main, tree: state.completedTip.tree,
        qualification: { ...state.completedTip.qualification }, prepared: now.toISOString(), prepareRun: context.runId });
    }
  } else if (operation === 'abort-independent-delivery') {
    Object.assign(change, { phase: 'aborted', aborted: now.toISOString(), abortRun: context.runId });
  }
  invariant(await api.administrator(context.actor)
    && (main === undefined || await api.main() === main && await api.ciQuiescent()),
  'Delivery administrator, source or physical quiescence changed; reservations remain held.');
  await api.save(state, `ci: ${operation}\n\nAdministrator: ${context.actor}\nWorkflow run: ${context.runId}\nDelivery hold: ${change.ticket}\nPrevious checkpoint: ${inputs.recovery_checkpoint}`);
  const expected = api.commit;
  invariant(JSON.stringify(await api.load()) === JSON.stringify(state) && api.commit === expected
    && api.commit !== inputs.recovery_checkpoint, 'Delivery checkpoint readback differs; inspect before further action.');
  const result = resume ? 'delivery-resumed' : change.phase === 'aborted' ? 'delivery-aborted'
    : change.phase === 'ready' ? 'delivery-ready' : 'delivery-draining';
  api.info(`Independent delivery hold ${change.ticket}: ${result}; checkpoint ${api.commit}; no publisher cancellation or configuration write performed.`);
  return result;
}

async function changeMainBuildState(api, { context, inputs, attempt, profile, now = new Date() }) {
  const operation = controllerOperation(inputs);
  const target = inputs.main_build_profile;
  invariant(['hold-main-builds', 'prepare-main-builds', 'resume-main-builds', 'abort-main-builds'].includes(operation)
    && ['per-merge', 'hourly-v1'].includes(target)
    && profile === 'batch-v1' && api.independent === true
    && context.eventName === 'workflow_dispatch' && context.ref === 'refs/heads/main' && attempt === 1
    && Number.isSafeInteger(context.runId) && context.runId > 0 && Number.isFinite(now.valueOf())
    && inputs.acknowledge_batch_change === true && SHA.test(inputs.recovery_checkpoint)
    && !inputs.recovery_main && inputs.acknowledge_uncertain_dispatch !== true,
  'Main scheduling changes require acknowledged first-attempt administrator operations and unmixed exact-checkpoint inputs.');
  await api.verifyProtection('batch-v1');
  invariant(await api.administrator(context.actor), 'Main scheduling changes require a live repository administrator.');
  const state = await api.load();
  invariant(api.commit === inputs.recovery_checkpoint && state.version === 2
    && state.profile === 'batch-v1' && state.deliveryProfile === 'independent-v1',
  'Main scheduling checkpoint or independent batch profile changed.');
  invariant(!['draining', 'ready'].includes(state.deliveryTransition?.phase),
    'An independent delivery transition already owns admission.');
  const current = state.mainBuildProfile ?? 'per-merge';
  const resume = operation === 'resume-main-builds';
  invariant((api.mainBuildProfile ?? 'per-merge') === (resume ? target : current),
    'Main scheduling configuration changed before its prepared boundary.');
  let change = state.mainSchedulingTransition;
  if (operation === 'hold-main-builds') {
    invariant(target !== current && !inputs.recovery_ticket
      && (!change || ['aborted', 'completed'].includes(change.phase)),
    'Main scheduling already matches the target or another hold owns reservations.');
    change = { ticket: require('node:crypto').randomUUID(), actor: context.actor,
      run: context.runId, checkpoint: api.commit, created: now.toISOString(), phase: 'draining', target };
    state.mainSchedulingTransition = change;
  } else {
    invariant(change && ['draining', 'ready'].includes(change.phase) && change.target === target
      && inputs.recovery_ticket === change.ticket && change.actor.toLowerCase() === context.actor.toLowerCase(),
    'The exact main scheduling hold and its administrator owner are required.');
  }
  let main;
  if (operation === 'prepare-main-builds' || resume) {
    main = await api.main();
    invariant(state.active === null && state.completedTip?.sha === main
      && state.completedTip.delivery === 'independent-v1',
    'Main scheduling requires no active candidate and the recorded current completed tip.');
    const record = await api.releaseSource(main);
    invariant(record.source === main && record.tree === state.completedTip.tree && await api.supportsHourlyMain(main),
      'Main scheduling requires the exact source record and reviewed scheduling workflow.');
    const observation = await api.tipQualification(state.completedTip, { allowScheduled: true });
    invariant(observation?.terminal === true && observation.provisional === false
      && observation.qualification?.source === main
      && observation.owner?.source === main && observation.owner.attempt === 1
      && observation.owner.run === observation.qualification.run,
    'Main scheduling requires successful complete first-attempt qualification, never a skipped or failed main run.');
    if (resume) {
      invariant(change.phase === 'ready' && change.source === main && change.tree === state.completedTip.tree
        && JSON.stringify(change.qualification) === JSON.stringify(observation.qualification),
      'Prepared main scheduling source or full qualification changed.');
    }
    invariant((await api.commitInfo(main)).tree === state.completedTip.tree && await api.ciQuiescent(),
      'Main scheduling requires the exact tree and physically quiescent CI/publication.');
    state.completedTip.qualification = { ...observation.qualification };
    state.completedTip.qualificationOwner = { ...observation.owner };
    if (resume) {
      if (target === 'hourly-v1') {
        state.mainBuildProfile = target;
        state.hourlyMain = { baseline: { source: main, tree: change.tree,
          qualification: { ...change.qualification } }, activated: now.toISOString() };
      } else {
        delete state.mainBuildProfile;
        delete state.hourlyMain;
      }
      Object.assign(change, { phase: 'completed', completed: now.toISOString(), resumeRun: context.runId });
    } else {
      Object.assign(change, { phase: 'ready', source: main, tree: state.completedTip.tree,
        qualification: { ...observation.qualification }, prepared: now.toISOString(), prepareRun: context.runId });
    }
  } else if (operation === 'abort-main-builds') {
    Object.assign(change, { phase: 'aborted', aborted: now.toISOString(), abortRun: context.runId });
  }
  invariant(await api.administrator(context.actor)
    && (main === undefined || await api.main() === main && await api.ciQuiescent()),
  'Main scheduling administrator, source or physical quiescence changed; reservations remain held.');
  await api.save(state, `ci: ${operation}\n\nAdministrator: ${context.actor}\nWorkflow run: ${context.runId}\nMain scheduling hold: ${change.ticket}\nPrevious checkpoint: ${inputs.recovery_checkpoint}`);
  const expected = api.commit;
  invariant(JSON.stringify(await api.load()) === JSON.stringify(state) && api.commit === expected
    && api.commit !== inputs.recovery_checkpoint, 'Main scheduling checkpoint readback changed.');
  return resume ? 'main-builds-resumed' : change.phase === 'aborted' ? 'main-builds-aborted'
    : change.phase === 'ready' ? 'main-builds-ready' : 'main-builds-draining';
}

module.exports = { recover, changeBatchState, changeDeliveryState, changeMainBuildState };
