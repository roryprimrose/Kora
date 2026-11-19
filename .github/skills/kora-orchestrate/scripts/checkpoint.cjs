'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { randomUUID } = require('node:crypto');
const { createState, applyEvent, validate, digest } = require('./registry.cjs');

function requireValue(condition, message) {
  if (!condition) throw new Error(message);
}

function validateCheckpoint(value) {
  requireValue(value?.version === 1 && Number.isSafeInteger(value.revision) && value.revision > 0 &&
    Array.isArray(value.actions) && Array.isArray(value.events), 'Malformed checkpoint.');
  validate(value.state);
  requireValue(value.actions.every(action => typeof action.id === 'string' &&
    ['pending', 'done', 'blocked'].includes(action.status) && action.command &&
    typeof action.command.type === 'string'), 'Malformed action journal.');
  const refinements = value.events.filter(event => event.type === 'refine-scopes');
  const index = value.state.refinements ?? [];
  requireValue(refinements.length === index.length &&
    refinements.every(event => index.some(record => record.eventId === event.id)) &&
    new Set(refinements.map(event => event.id)).size === refinements.length,
  'Checkpoint refinement event/schema binding is inconsistent.');
  const amendments = value.events.filter(event => event.type === 'extend-scopes');
  const amendmentIndex = value.state.amendments ?? [];
  requireValue(amendments.length === amendmentIndex.length &&
    amendments.every(event => amendmentIndex.some(record => record.eventId === event.id)) &&
    new Set(amendments.map(event => event.id)).size === amendments.length,
  'Checkpoint mutable amendment event/schema binding is inconsistent.');
  const fencedAmendments = value.state.items.flatMap(item => (item.scopeAmendments ?? [])
    .filter(record => record.proof.version === 2));
  requireValue(fencedAmendments.every(record => {
    const journal = amendments.find(event => event.id === record.eventId);
    return journal?.digest === digest(record) && record.proof.checkpointRevision < value.revision &&
      Array.isArray(journal.commands) && journal.commands.length === 0 &&
      !value.actions.some(action => action.id.startsWith(`${record.eventId}:`));
  }), 'Checkpoint fenced extension complete history/no-action journal binding is inconsistent.');
  const readmissions = value.events.filter(event => event.type === 'revalidate-candidate');
  const readmissionIndex = value.state.readmissions ?? [];
  requireValue(readmissions.length === readmissionIndex.length &&
    readmissions.every(event => readmissionIndex.some(record => record.eventId === event.id)) &&
    new Set(readmissions.map(event => event.id)).size === readmissions.length,
  'Checkpoint candidate revalidation event/schema binding is inconsistent.');
  const supersessions = value.state.supersessions ?? [];
  const renewals = value.state.recoveryRenewals ?? [];
  const renewalJournal = value.events.filter(event => event.type === 'renew-recovery-continuation');
  requireValue(renewalJournal.length === renewals.length &&
    new Set(renewalJournal.map(event => event.id)).size === renewalJournal.length &&
    renewalJournal.every(event => {
      const record = renewals.find(record => record.event.id === event.id);
      return record && event.digest === digest(record) && record.event.proof.checkpointRevision < value.revision &&
        Array.isArray(event.commands) && event.commands.length === 0 &&
        !value.actions.some(action => action.id.startsWith(`${event.id}:`));
    }), 'Checkpoint recovery renewal complete history/action journal binding is inconsistent.');
  const recoveries = value.state.recoveries ?? [];
  const recoveryJournal = value.events.filter(event => event.type === 'recover-scope-incident');
  requireValue(recoveryJournal.length === recoveries.length &&
    new Set(recoveryJournal.map(event => event.id)).size === recoveryJournal.length &&
    recoveryJournal.every(event => {
      const record = recoveries.find(record => record.event.id === event.id);
      return record && event.digest === digest(record) &&
        Array.isArray(event.commands) && event.commands.length === 0 &&
        !value.actions.some(action => action.id.startsWith(`${event.id}:`));
    }), 'Checkpoint incident recovery complete history/action journal binding is inconsistent.');
  const journal = value.events.filter(event => event.type === 'supersede-write-reservations');
  requireValue(journal.length === supersessions.length &&
    new Set(journal.map(event => event.id)).size === journal.length &&
    new Set(value.actions.map(action => action.id)).size === value.actions.length &&
    journal.every(event => {
      const record = supersessions.find(record => record.event.id === event.id);
      const actions = value.actions.filter(action => action.id.startsWith(`${event.id}:`));
      return record && event.digest === digest(record) &&
        (record.event.proof.version !== 2 || record.event.proof.checkpointRevision < value.revision &&
          event.commands?.length === 0 && actions.length === 0) &&
        Array.isArray(event.commands) && digest(event.commands) === digest(record.commands) &&
        event.commands.every((command, index) =>
          actions[index]?.id === `${event.id}:${index}` &&
          digest(actions[index].command) === digest(command)) &&
        actions.length === event.commands.length;
    }), 'Checkpoint supersession complete history/action journal binding is inconsistent.');
}

function checkpoint(file, input, io = fs) {
  requireValue(path.isAbsolute(file) && path.normalize(file) === file &&
    file.split(path.sep).includes('.scratch'), 'Checkpoint must be an absolute canonical ignored scratch path.');
  if (input.operation === 'read') {
    const value = JSON.parse(io.readFileSync(file, 'utf8'));
    validateCheckpoint(value);
    return value;
  }
  requireValue(Number.isSafeInteger(input.expectedRevision) && input.expectedRevision >= 0,
    'Expected checkpoint revision required.');
  io.mkdirSync(path.dirname(file), { recursive: true });
  const lock = `${file}.lock`;
  const handle = io.openSync(lock, 'wx');
  let temporary = null;
  try {
    const exists = io.existsSync(file);
    let value = exists ? JSON.parse(io.readFileSync(file, 'utf8')) : null;
    if (value) validateCheckpoint(value);
    requireValue((value?.revision ?? 0) === input.expectedRevision, 'Stale checkpoint revision.');
    if (input.operation === 'init') {
      requireValue(!exists, 'Checkpoint already initialized.');
      value = { version: 1, revision: 0,
        state: createState(input.coordinator, input.head, input.mode), actions: [], events: [] };
    } else if (input.operation === 'apply') {
      requireValue(value, 'Checkpoint is not initialized.');
      const result = applyEvent(value.state, input.event, {
        revision: value.revision, recoveryEvidence: input.recoveryEvidence, renewalEvidence: input.renewalEvidence,
        extensionEvidence: input.extensionEvidence, supersessionEvidence: input.supersessionEvidence
      });
      if (result.duplicate) return value;
      value.state = result.state;
      const at = new Date().toISOString();
      const fencedAmendment = input.event.type === 'extend-scopes' && input.event.proof.version === 2
        ? value.state.items.find(item => item.key === input.event.key).scopeAmendments.at(-1) : null;
      value.events.push({ id: input.event.id, type: input.event.type, at,
        ...(fencedAmendment ? { digest: digest(fencedAmendment), commands: [] } : {}),
        ...(['supersede-write-reservations', 'recover-scope-incident', 'renew-recovery-continuation'].includes(input.event.type)
          ? { digest: digest(input.event.type === 'renew-recovery-continuation'
            ? value.state.recoveryRenewals.at(-1) : input.event.type === 'recover-scope-incident'
            ? value.state.recoveries.at(-1) : value.state.supersessions.at(-1)),
          commands: structuredClone(result.actions) } : {}) });
      result.actions.forEach((command, index) => value.actions.push({
        id: `${input.event.id}:${index}`, command, status: 'pending', at, evidence: null
      }));
    } else if (input.operation === 'ack') {
      requireValue(value, 'Checkpoint is not initialized.');
      const action = value.actions.find(item => item.id === input.actionId);
      requireValue(action?.status === 'pending' && ['done', 'blocked'].includes(input.status) &&
        typeof input.evidence === 'string' && input.evidence.trim(),
      'Pending action, explicit result, and concrete evidence required.');
      action.status = input.status;
      action.evidence = input.evidence;
      action.completedAt = new Date().toISOString();
    } else {
      throw new Error(`Unknown checkpoint operation: ${input.operation}`);
    }
    value.revision++;
    validateCheckpoint(value);
    temporary = `${file}.${randomUUID()}.tmp`;
    io.writeFileSync(temporary, JSON.stringify(value, null, 2), { flag: 'wx' });
    io.renameSync(temporary, file);
    temporary = null;
    return value;
  } finally {
    if (temporary && io.existsSync(temporary)) io.unlinkSync(temporary);
    io.closeSync(handle);
    io.unlinkSync(lock);
  }
}

module.exports = { checkpoint };

if (require.main === module) {
  try {
    requireValue(process.argv.length === 3, 'Pass exactly one absolute checkpoint path.');
    const input = JSON.parse(fs.readFileSync(0, 'utf8'));
    console.log(JSON.stringify(checkpoint(process.argv[2], input)));
  } catch (error) {
    console.error(error);
    process.exitCode = 1;
  }
}
