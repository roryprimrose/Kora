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
  invariant(active?.ticket === inputs.recovery_ticket && active.phase === 'retiring'
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

module.exports = { recover };
