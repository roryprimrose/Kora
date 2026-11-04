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

module.exports = { recover, changeBatchState };
