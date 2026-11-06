const { SHA, invariant } = require('./policy.cjs');
const { validateRelease } = require('./delivery.cjs');

const PROFILE = 'hourly-v1';
const WORKFLOW = '.github/workflows/ci.yml';
const EVENTS = ['schedule', 'workflow_dispatch'];
const STATUSES = ['queued', 'in_progress', 'completed', 'waiting', 'pending', 'requested'];
const CONCLUSIONS = ['success', 'failure', 'cancelled', 'timed_out', 'neutral', 'skipped',
  'action_required', 'stale', 'startup_failure'];
const positive = value => Number.isSafeInteger(value) && value > 0;
const sha = value => typeof value === 'string' && SHA.test(value);
const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);
const exactKeys = (value, keys) => object(value) && Object.keys(value).every(key => keys.includes(key));

function canonical(value) {
  return typeof value === 'string' && Number.isFinite(Date.parse(value))
    && new Date(value).toISOString() === value;
}

function timestamp(value) {
  invariant(typeof value === 'string'
    && /^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d{3})?Z$/.test(value),
  'Invalid hourly main API timestamp.');
  const normalized = value.includes('.') ? value : value.replace(/Z$/, '.000Z');
  invariant(canonical(normalized), 'Invalid hourly main API timestamp.');
  return normalized;
}

function mainBuildProfile(value = '') {
  invariant(['', PROFILE].includes(value), 'Unknown main build profile.');
  return value === PROFILE ? PROFILE : 'per-merge';
}

// Validates only hourly state; the controller owns queue/profile compatibility and transitions.
function validateHourlyState(state) {
  invariant(object(state), 'Invalid hourly main state.');
  invariant(state.mainBuildProfile === undefined || state.mainBuildProfile === PROFILE,
    'Unknown durable main build profile.');
  if (state.mainBuildProfile === undefined) {
    invariant(state.hourlyMain === undefined, 'Hourly main state requires the durable hourly profile.');
    return state;
  }
  const hourly = state.hourlyMain;
  invariant(exactKeys(hourly, ['baseline', 'activated', 'lastAttempt', 'observedThrough'])
    && canonical(hourly.activated), 'Invalid hourly main activation.');
  const baseline = hourly.baseline;
  invariant(exactKeys(baseline, ['source', 'tree', 'qualification'])
    && sha(baseline.source) && sha(baseline.tree)
    && exactKeys(baseline.qualification, ['run', 'job', 'source'])
    && positive(baseline.qualification.run) && positive(baseline.qualification.job)
    && baseline.qualification.source === baseline.source, 'Invalid hourly main baseline.');
  if (hourly.lastAttempt !== undefined) {
    const attempt = hourly.lastAttempt;
    invariant(exactKeys(attempt, ['run', 'source', 'created', 'event'])
      && positive(attempt.run) && sha(attempt.source) && canonical(attempt.created)
      && attempt.created >= hourly.activated && EVENTS.includes(attempt.event),
    'Invalid hourly main attempt.');
  }
  if (hourly.observedThrough !== undefined) {
    const cursor = hourly.observedThrough;
    invariant(exactKeys(cursor, ['run', 'created']) && positive(cursor.run)
      && canonical(cursor.created) && cursor.created >= hourly.activated,
    'Invalid hourly main observation cursor.');
    invariant(cursor.run !== hourly.lastAttempt?.run || cursor.created === hourly.lastAttempt.created,
      'Hourly main cursor and attempt disagree.');
  }
  return state;
}

function checkpoint(api) {
  invariant(sha(api.commit) && object(api.repo)
    && typeof api.repo.owner === 'string' && api.repo.owner.length > 0
    && typeof api.repo.repo === 'string' && api.repo.repo.length > 0
    && api.repository === `${api.repo.owner}/${api.repo.repo}`,
  'Hourly main requires pinned repository state.');
  return api.commit;
}

function identity(run, api) {
  invariant(object(run) && positive(run.id) && positive(run.run_attempt)
    && sha(run.head_sha) && run.head_branch === 'main'
    && run.path === WORKFLOW && run.display_title === 'CI main'
    && EVENTS.includes(run.event) && run.repository?.full_name === api.repository
    && run.head_repository?.full_name === api.repository,
  'Invalid hourly main run identity.');
  invariant(STATUSES.includes(run.status)
    && (run.status === 'completed' ? CONCLUSIONS.includes(run.conclusion) : run.conclusion === null),
  'Invalid hourly main run status.');
  return { run: run.id, source: run.head_sha, created: timestamp(run.created_at), event: run.event };
}

function compare(left, right) {
  return left.created.localeCompare(right.created) || left.run - right.run;
}

function latest(left, right) {
  return !left || right && compare(right, left) > 0 ? right : left;
}

function sameAttempt(left, right) {
  return left.run === right.run && left.source === right.source
    && left.created === right.created && left.event === right.event;
}

function slot(created) {
  return created.slice(0, 13);
}

async function sourceProof(api, source, main, revision, baseline) {
  const record = validateRelease(await api.releaseSource(source, revision), source);
  invariant(await api.containsBase(source, main) === true, 'Hourly main source is not reachable from main.');
  invariant(source !== baseline.source || record.tree === baseline.tree,
    'Hourly main baseline tree changed.');
}

async function attempts(api, state, revision, main, exclude) {
  const hourly = state.hourlyMain;
  const cursor = hourly.observedThrough;
  const lower = cursor?.created ?? hourly.activated;
  const runs = await api.completePages(api.github.rest.actions.listWorkflowRuns,
    { workflow_id: 'ci.yml', branch: 'main', created: `>=${lower}` }, 'hourly main run');
  invariant(Array.isArray(runs) && runs.every(run => positive(run?.id))
    && new Set(runs.map(run => run.id)).size === runs.length,
  'Invalid or duplicate hourly main run identities.');
  const eligible = [];
  for (const run of runs) {
    invariant(typeof run.event === 'string' && typeof run.display_title === 'string',
      'Missing hourly main run metadata.');
    if (!EVENTS.includes(run.event) || run.display_title !== 'CI main') continue;
    const attempt = identity(run, api);
    invariant(attempt.run !== cursor?.run || attempt.created === cursor.created,
      'Hourly main cursor run identity changed.');
    invariant(attempt.run !== hourly.lastAttempt?.run
      || sameAttempt(attempt, hourly.lastAttempt),
    'Hourly main recorded attempt identity changed.');
    if (attempt.created < hourly.activated || cursor && compare(attempt, cursor) <= 0
      || run.id === exclude) continue;
    eligible.push({ run, attempt });
  }
  eligible.sort((left, right) => compare(left.attempt, right.attempt));
  const observations = [];
  for (const item of eligible) {
    let { run, attempt } = item;
    if (run.run_attempt !== 1) {
      const { data } = await api.github.rest.actions.getWorkflowRunAttempt({
        ...api.repo, run_id: run.id, attempt_number: 1 });
      const original = identity(data, api);
      invariant(data.run_attempt === 1 && sameAttempt(original, attempt),
        'Hourly main first-attempt identity changed.');
      run = data;
      attempt = original;
    }
    const jobs = await api.jobs(run.id, 1);
    invariant(Array.isArray(jobs) && jobs.every(job => positive(job?.id)
      && job.run_id === run.id && job.run_attempt === 1 && job.head_sha === run.head_sha
      && typeof job.name === 'string' && STATUSES.includes(job.status)
      && (job.status === 'completed' ? CONCLUSIONS.includes(job.conclusion) : job.conclusion === null))
      && new Set(jobs.map(job => job.id)).size === jobs.length,
    'Invalid or duplicate hourly main job identities.');
    const version = jobs.filter(job => job.name === 'Resolve build version');
    invariant(version.length <= 1, 'Duplicate hourly main version jobs.');
    const job = version[0];
    const full = job && (['queued', 'in_progress'].includes(job.status)
      || job.status === 'completed' && job.conclusion !== 'skipped');
    const pending = !job || !full && !(job.status === 'completed' && job.conclusion === 'skipped');
    if (full) await sourceProof(api, attempt.source, main, revision, hourly.baseline);
    observations.push({ attempt, full: Boolean(full), pending });
  }
  return observations;
}

// Mutates only hourlyMain after all reads succeed; returns whether the controller must save.
// Missing version-job evidence holds the cursor without recording an attempted source.
async function observeAttempts(api, state) {
  validateHourlyState(state);
  if (state.mainBuildProfile !== PROFILE) return false;
  const revision = checkpoint(api);
  const main = await api.main();
  invariant(sha(main), 'Unknown main revision for hourly observation.');
  const observations = await attempts(api, state, revision, main);
  const next = { ...state.hourlyMain };
  let pending = false;
  for (const observation of observations) {
    if (observation.full) next.lastAttempt = latest(next.lastAttempt, observation.attempt);
    pending ||= observation.pending;
    if (!pending) next.observedThrough = {
      run: observation.attempt.run, created: observation.attempt.created,
    };
  }
  const changed = JSON.stringify(next) !== JSON.stringify(state.hourlyMain);
  if (changed) state.hourlyMain = next;
  return changed;
}

// Read-only admission. context is { eventName, ref, sha, runId, runAttempt } from GitHub,
// never dispatch inputs. UTC slots use actual trigger creation, not delayed job start.
// Neither helper saves, dispatches, qualifies a source, nor authorizes publication.
async function admitScheduled(api, context, state) {
  validateHourlyState(state);
  invariant(state.mainBuildProfile === PROFILE, 'Scheduled main requires the active hourly profile.');
  invariant(context?.eventName === 'schedule' && context.ref === 'refs/heads/main'
    && sha(context.sha) && positive(context.runId) && context.runAttempt === 1,
  'Hourly main requires a trusted first-attempt schedule.');
  const revision = checkpoint(api);
  const { data: run } = await api.github.rest.actions.getWorkflowRun({ ...api.repo, run_id: context.runId });
  const current = identity(run, api);
  invariant(current.run === context.runId && current.source === context.sha && run.run_attempt === 1
    && current.event === 'schedule' && current.created >= state.hourlyMain.activated,
  'Scheduled main context differs from its actual run.');
  const main = await api.main();
  invariant(sha(main), 'Unknown main revision for hourly admission.');
  await sourceProof(api, current.source, main, revision, state.hourlyMain.baseline);
  const decision = (full, reason) => {
    api.info(`Hourly main schedule: ${reason}; source ${current.source}.`);
    return full;
  };
  if (state.mainSchedulingTransition?.phase === 'ready') return decision(false, 'administrator-hold');
  const hourly = state.hourlyMain;
  if (current.source === hourly.baseline.source) return decision(false, 'qualified-baseline');
  if (current.source === hourly.lastAttempt?.source) return decision(false, 'unchanged-source');
  if (hourly.lastAttempt && compare(hourly.lastAttempt, current) > 0)
    return decision(false, 'newer-attempt');
  if (hourly.lastAttempt && slot(hourly.lastAttempt.created) === slot(current.created))
    return decision(false, 'hour-already-attempted');
  const observations = await attempts(api, state, revision, main, current.run);
  let previous = hourly.lastAttempt;
  for (const observation of observations) {
    if (observation.full) {
      previous = latest(previous, observation.attempt);
      if (slot(observation.attempt.created) === slot(current.created))
        return decision(false, 'hour-already-attempted');
    }
    if (observation.pending && compare(observation.attempt, current) < 0
      && (observation.attempt.source === current.source
        || slot(observation.attempt.created) === slot(current.created)))
      return decision(false, 'earlier-run-unresolved');
  }
  if (previous?.source === current.source) return decision(false, 'unchanged-source');
  if (previous && compare(previous, current) > 0) return decision(false, 'newer-attempt');
  return decision(true, 'changed-source');
}

module.exports = { mainBuildProfile, validateHourlyState, observeAttempts, admitScheduled };
