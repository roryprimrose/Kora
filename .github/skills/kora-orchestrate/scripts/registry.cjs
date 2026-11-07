'use strict';

const path = require('node:path');

const LIMITS = Object.freeze({ repair: 4, ready: 10 });
const PHASES = ['candidate', 'reserved', 'running', 'published', 'complete'];
const requireValue = (condition, message) => {
  if (!condition) throw new Error(message);
};
const text = value => typeof value === 'string' && value.trim().length > 0;
const head = value => typeof value === 'string' && /^[0-9a-f]{40}$/i.test(value);
const holdsSlot = item => item.phase !== 'candidate' && item.phase !== 'complete';

function scope(value) {
  requireValue(text(value) && !path.win32.isAbsolute(value) &&
    !/[/*?:]/.test(value) && !value.split('\\').some(part => ['', '.', '..'].includes(part)),
  'Scope must be a repository-relative Windows path without globs or traversal.');
  return value.toLowerCase();
}

function overlaps(left, right) {
  return left.some(a => right.some(b => a === b || a.startsWith(`${b}\\`) || b.startsWith(`${a}\\`)));
}

function validate(state) {
  requireValue(state?.version === 1 && typeof state.enabled === 'boolean' &&
    ['observe', 'delivery'].includes(state.mode) &&
    text(state.coordinator) && (state.head === null || head(state.head)) && Array.isArray(state.items) &&
    Array.isArray(state.seen), 'Malformed registry.');
  requireValue(state.planner && ['active', 'requested', 'notified']
    .every(key => typeof state.planner[key] === 'boolean') &&
    Number.isSafeInteger(state.planner.pass) && state.planner.pass >= 0, 'Malformed planner state.');
  requireValue(state.seen.every(text) && new Set(state.seen).size === state.seen.length,
    'Malformed event history.');
  const active = [];
  const keys = new Set();
  const outcomes = new Set();
  const owners = new Set([state.coordinator]);
  if (state.planner.owner !== null) {
    requireValue(text(state.planner.owner) && !owners.has(state.planner.owner), 'Invalid planner owner.');
    owners.add(state.planner.owner);
  }
  requireValue(!state.planner.active || state.planner.owner !== null, 'Active planner has no owner.');
  for (const item of state.items) {
    requireValue(text(item.key) && text(item.outcome) && text(item.evidence) &&
      head(item.head) && Object.hasOwn(LIMITS, item.pool) && PHASES.includes(item.phase) &&
      ['feature', 'maintenance', 'repair'].includes(item.kind) &&
      (item.pool === 'repair') === (item.kind === 'repair') &&
      Array.isArray(item.scopes) && item.scopes.length > 0, 'Malformed registry item.');
    requireValue(item.scopes.every(value => scope(value) === value), 'Noncanonical saved scope.');
    requireValue(!keys.has(item.key) && !outcomes.has(item.outcome), 'Duplicate key or outcome.');
    keys.add(item.key);
    outcomes.add(item.outcome);
    requireValue(item.pr === null || Number.isSafeInteger(item.pr) && item.pr > 0, 'Invalid PR.');
    requireValue(item.pool !== 'repair' || item.pr !== null, 'Repair has no assigned PR.');
    if (item.owner !== null) {
      requireValue(text(item.owner) && !owners.has(item.owner), 'Duplicate or invalid owner.');
      owners.add(item.owner);
    }
    requireValue(!['running', 'published', 'complete'].includes(item.phase) || item.owner !== null,
      'Worker phase has no owner.');
    requireValue(!['candidate', 'reserved'].includes(item.phase) || item.owner === null,
      'Undispatched phase cannot hide an owner.');
    if (item.phase === 'published' || item.phase === 'complete') {
      const proof = item.publication;
      requireValue(proof && head(proof.pushedHead) && proof.pushedHead === proof.remoteHead &&
        proof.pr === item.pr && (item.pool !== 'ready' || proof.ready === true && proof.enrolled === true),
      'Publication evidence missing or malformed.');
    }
    if (item.phase === 'complete') {
      const proof = item.cleanup;
      requireValue(proof && proof.remoteHead === item.publication.remoteHead &&
        proof.stoppedWriting === true && proof.worktreeAbsent === true &&
        proof.branchAbsent === true && proof.pendingInstructions === 0,
      'Completed saved item lacks cleanup evidence.');
    }
    if (holdsSlot(item)) active.push(item);
  }
  for (const [pool, limit] of Object.entries(LIMITS)) {
    requireValue(active.filter(item => item.pool === pool).length <= limit, `${pool} pool is full.`);
  }
  for (let i = 0; i < active.length; i++) {
    for (let j = i + 1; j < active.length; j++) {
      requireValue(!overlaps(active[i].scopes, active[j].scopes), 'Conflicting reserved file scopes.');
      requireValue(active[i].pr === null || active[i].pr !== active[j].pr, 'PR already owned.');
    }
  }
  const represented = state.items.filter(item => item.phase !== 'complete' && item.pr !== null);
  requireValue(new Set(represented.map(item => item.pr)).size === represented.length,
    'PR already represented by an unfinished assignment.');
}

function createState(coordinator, sourceHead, mode = 'observe') {
  const state = {
    version: 1, enabled: true, mode, coordinator, head: sourceHead,
    planner: { owner: null, active: false, requested: true, notified: false, pass: 0 },
    items: [], seen: []
  };
  validate(state);
  return state;
}

function candidate(event) {
  return {
    key: event.key, outcome: event.outcome, pool: event.pool, kind: event.kind,
    head: event.head, scopes: event.scopes?.map(scope), evidence: event.evidence,
    pr: event.pr ?? null, owner: null, phase: 'candidate', blocker: null, publication: null
  };
}

function dispatch(state, actions) {
  if (!state.enabled) return;
  for (const pool of ['ready', 'repair']) {
    const waiting = state.items.filter(item => state.mode === 'delivery' && item.pool === pool &&
      item.phase === 'candidate' && item.head === state.head);
    if (pool === 'ready') waiting.sort((a, b) =>
      Number(a.kind !== 'feature') - Number(b.kind !== 'feature'));
    for (const item of waiting) {
      if (item.kind === 'maintenance' && state.planner.active) continue;
      const active = state.items.filter(holdsSlot);
      if (active.filter(other => other.pool === pool).length >= LIMITS[pool]) break;
      if (item.blocker || active.some(other => overlaps(item.scopes, other.scopes) ||
        item.pr !== null && item.pr === other.pr)) continue;
      item.phase = 'reserved';
      actions.push({ type: 'launch', key: item.key, pool });
    }
  }
  if (!state.planner.owner || !state.planner.requested) return;
  const capacity = Object.fromEntries(Object.entries(LIMITS).map(([pool, limit]) =>
    [pool, limit - state.items.filter(item => item.pool === pool && holdsSlot(item)).length]));
  if (state.planner.active) {
    if (!state.planner.notified) {
      state.planner.notified = true;
      actions.push({ type: 'steer-planner', owner: state.planner.owner, capacity });
    }
  } else {
    state.planner.active = true;
    state.planner.pass++;
    state.planner.requested = false;
    state.planner.notified = false;
    actions.push({ type: 'discover', owner: state.planner.owner, capacity,
      examine: ['ready', 'repair'], head: state.head, pass: state.planner.pass });
  }
}

function applyEvent(current, event) {
  validate(current);
  requireValue(event && text(event.id) && text(event.type), 'Event identity/type missing.');
  const state = structuredClone(current);
  if (state.seen.includes(event.id)) return { state, actions: [], duplicate: true };
  const actions = [];
  const worker = () => {
    const item = state.items.find(value => value.key === event.key);
    requireValue(item && holdsSlot(item) && item.owner === event.owner, 'Unknown or mismatched worker.');
    return item;
  };
  switch (event.type) {
    case 'planner':
      requireValue(state.planner.owner === null && text(event.owner), 'Planner already reserved or invalid.');
      state.planner.owner = event.owner;
      break;
    case 'candidate':
      requireValue(state.enabled && state.planner.active && event.owner === state.planner.owner,
        'Candidate must come from the active assigned planner.');
      requireValue(event.pass === state.planner.pass, 'Candidate belongs to another discovery pass.');
      requireValue(head(state.head) && event.head === state.head, 'Candidate needs a source refresh.');
      state.items.push(candidate(event));
      break;
    case 'adopt': {
      requireValue(state.enabled && text(event.owner), 'Adoption requires an enabled workflow and owner.');
      state.items.push({ ...candidate(event), owner: event.owner, phase: 'running' });
      break;
    }
    case 'attached': {
      const item = state.items.find(value => value.key === event.key);
      requireValue(item?.phase === 'reserved' && item.owner === null &&
        text(event.owner) && !item.blocker, 'Launch is not an available reservation.');
      if (event.pr !== undefined) {
        requireValue(item.pr === null || item.pr === event.pr, 'Assigned PR changed.');
        item.pr = event.pr;
      }
      item.owner = event.owner;
      item.phase = 'running';
      break;
    }
    case 'launch-blocked': {
      const item = state.items.find(value => value.key === event.key);
      requireValue(item?.phase === 'reserved' && item.owner === null && text(event.reason),
        'Only an unattached reservation can have a launch blocker.');
      item.blocker = event.reason;
      break;
    }
    case 'published': {
      const item = worker();
      const proof = event.proof;
      requireValue(item.phase === 'running' && proof && head(proof.pushedHead) &&
        proof.pushedHead === proof.remoteHead && Number.isSafeInteger(proof.pr) && proof.pr > 0,
      'Matching pushed/remote head and actual PR evidence required.');
      requireValue(item.pr === null || item.pr === proof.pr, 'Publication changed assigned PR.');
      requireValue(item.pool !== 'ready' || proof.ready === true && proof.enrolled === true,
        'Delivery needs an actual ready PR and verified Auto-merge enrollment.');
      item.pr = proof.pr;
      item.publication = structuredClone(proof);
      item.phase = 'published';
      item.blocker = null;
      break;
    }
    case 'blocked': {
      const item = worker();
      requireValue(text(event.reason), 'Blocker must be explicit.');
      item.blocker = event.reason;
      break;
    }
    case 'completed': {
      const item = worker();
      const proof = event.proof;
      requireValue(item.phase === 'published' && proof &&
        proof.remoteHead === item.publication.remoteHead && proof.stoppedWriting === true &&
        proof.worktreeAbsent === true && proof.branchAbsent === true &&
        proof.pendingInstructions === 0, 'Publication and complete cleanup proof required before release.');
      item.phase = 'complete';
      item.blocker = null;
      item.cleanup = structuredClone(proof);
      actions.push({ type: 'archive', owner: item.owner, key: item.key });
      state.planner.requested = true;
      break;
    }
    case 'pass-ended':
      requireValue(state.planner.active && event.owner === state.planner.owner &&
        event.pass === state.planner.pass &&
        event.readyExamined === true && event.repairExamined === true &&
        typeof event.remaining === 'boolean', 'The active assigned pass must examine both pools.');
      state.planner.active = false;
      state.planner.notified = false;
      state.planner.requested ||= event.remaining;
      break;
    case 'refresh': {
      requireValue(head(event.head), 'Verified source head required.');
      if (event.rescan !== undefined) {
        requireValue(typeof event.rescan === 'boolean', 'Rescan must be explicit boolean.');
      }
      const changedKnownSource = state.head !== null && state.head !== event.head;
      state.head = event.head;
      state.planner.requested ||= changedKnownSource || event.rescan === true;
      break;
    }
    case 'pause':
      state.enabled = false;
      state.planner.requested = false;
      for (const item of state.items.filter(holdsSlot)) {
        if (item.owner) actions.push({ type: 'pause', owner: item.owner, key: item.key });
      }
      if (state.planner.owner) actions.push({ type: 'pause', owner: state.planner.owner });
      break;
    case 'handoff': {
      requireValue(!state.enabled && event.fromCoordinator === state.coordinator &&
        text(event.toCoordinator) && event.toCoordinator !== state.coordinator,
      'Handoff requires a paused workflow and exact old/new coordinator identities.');
      const requiredOwners = [state.coordinator, state.planner.owner,
        ...state.items.filter(holdsSlot).map(item => item.owner)].filter(value => value !== null);
      requireValue(!state.items.some(item => holdsSlot(item) && item.owner === null),
        'An unattached reservation must be reconciled before handoff.');
      requireValue(event.proof && event.proof.pendingInstructions === 0 &&
        Array.isArray(event.proof.stoppedOwners) &&
        requiredOwners.every(owner => event.proof.stoppedOwners.includes(owner)),
      'Every old writer must be stopped and restarting instructions cleared before handoff.');
      state.coordinator = event.toCoordinator;
      state.planner.active = false;
      state.planner.requested = false;
      state.planner.notified = false;
      break;
    }
    case 'resume':
      requireValue(!state.enabled && event.owner === state.coordinator &&
        event.authorityConfirmed === true && typeof event.discover === 'boolean',
      'Resume requires the exact coordinator, confirmed authority and explicit discovery choice.');
      state.enabled = true;
      state.head = null;
      state.planner.requested = event.discover;
      break;
    default:
      throw new Error(`Unknown event type: ${event.type}`);
  }
  validate(state);
  state.seen.push(event.id);
  dispatch(state, actions);
  validate(state);
  return { state, actions, duplicate: false };
}

module.exports = { createState, applyEvent, validate, LIMITS };

if (require.main === module) {
  try {
    const input = JSON.parse(require('node:fs').readFileSync(0, 'utf8'));
    const result = input.operation === 'init'
      ? { state: createState(input.coordinator, input.head, input.mode), actions: [] }
      : applyEvent(input.state, input.event);
    console.log(JSON.stringify(result));
  } catch (error) {
    console.error(error);
    process.exitCode = 1;
  }
}
