'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { randomUUID } = require('node:crypto');
const { createState, applyEvent, validate } = require('./registry.cjs');

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
      const result = applyEvent(value.state, input.event);
      if (result.duplicate) return value;
      value.state = result.state;
      const at = new Date().toISOString();
      value.events.push({ id: input.event.id, type: input.event.type, at });
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
