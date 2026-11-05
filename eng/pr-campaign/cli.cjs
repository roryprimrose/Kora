#!/usr/bin/env node
const fs = require('node:fs/promises');
const path = require('node:path');
const { randomUUID, createHash } = require('node:crypto');
const { setTimeout: delay } = require('node:timers/promises');
const { SHA } = require('../merge-queue/policy.cjs');
const { Reader } = require('./github.cjs');
const { ObservationError, boundary, digest, validateCampaign, validateReceipt, newReceipt, evaluate,
  supersede, problem } = require('./campaign.cjs');

const HELP = `Read-only Kora PR campaign observations (no GitHub writes).
node eng\\pr-campaign\\cli.cjs --repository OWNER/REPO --prs 12,34 --owner DELIVERY-ID
  --output .scratch\\pr-campaign\\DELIVERY-ID [--worktree PATH] [--expected-principal LOGIN]
  [--previous RECEIPT.json] [--observations 1] [--budget-seconds 120]
  [--interval-seconds 30] [--command-seconds 20]

Targets are fixed, explicit positive PR numbers (maximum 100). No discovery/enrollment.
Default is one observation. Watch requires an explicit finite observation count and time budget.
Each observation writes a new immutable versioned JSON receipt and one JSON stdout line.
Progress goes to stderr. Exit: 0 all MERGED_VERIFIED; 2 blocker/error; 3 bounded observation
ended before delivery; 64 invalid arguments. Ctrl+C records cancellation after child exit.
--previous compares a retained receipt from this exact campaign, not an authority grant.
Receipts must be inside this worktree's ignored .scratch. No daemon or background handoff.
Old host chat messages cannot be prevented or recalled by this observer.
`;
function parseArgs(args, cwd = process.cwd()) {
  const values = {};
  const names = ['repository', 'prs', 'owner', 'output', 'worktree', 'expected-principal', 'previous',
    'observations', 'budget-seconds', 'interval-seconds', 'command-seconds'];
  for (let index = 0; index < args.length; index += 2) {
    const key = args[index]?.slice(2);
    boundary(args[index]?.startsWith('--') && names.includes(key) && values[key] === undefined
      && args[index + 1] && !args[index + 1].startsWith('--'), 'CLI arguments (see --help)');
    values[key] = args[index + 1];
  }
  const integer = (name, fallback, min, max) => {
    const value = values[name] ?? String(fallback);
    boundary(/^[0-9]+$/.test(value) && Number.isSafeInteger(Number(value))
      && Number(value) >= min && Number(value) <= max, `CLI ${name}`);
    return Number(value);
  };
  boundary(typeof values.prs === 'string' && /^[1-9][0-9]*(,[1-9][0-9]*)*$/.test(values.prs)
    && typeof values.output === 'string', 'explicit targets and output');
  const campaign = validateCampaign({ repository: values.repository, owner: values.owner,
    targets: values.prs.split(',').map(Number).sort((a, b) => a - b),
    expectedPrincipal: values['expected-principal'] ?? null });
  const worktree = path.resolve(cwd, values.worktree || '.');
  const observations = integer('observations', 1, 1, 1000);
  boundary(observations === 1 || values['budget-seconds'] !== undefined, 'explicit watch time budget');
  return { campaign, worktree, output: path.resolve(worktree, values.output),
    previousPath: values.previous ? path.resolve(worktree, values.previous) : null,
    observations,
    budgetSeconds: integer('budget-seconds', 120, 1, 86400),
    intervalSeconds: integer('interval-seconds', 30, 1, 3600),
    commandSeconds: integer('command-seconds', 20, 1, 300) };
}
function createReader(options, signal) {
  return new Reader({ ...options, repository: options.campaign.repository,
    deadline: Date.now() + options.budgetSeconds * 1000, signal });
}
async function scratchPath(worktree, destination) {
  const root = await fs.realpath(worktree);
  const relative = path.relative(root, destination);
  boundary(relative.startsWith(`.scratch${path.sep}`) && !path.isAbsolute(relative)
    && !relative.split(path.sep).includes('..'), 'owning scratch path');
  let existing = path.dirname(destination);
  for (;;) {
    try {
      const actual = await fs.realpath(existing);
      const inside = path.relative(root, actual);
      boundary(inside === '' || inside === '.scratch' || inside.startsWith(`.scratch${path.sep}`),
        'scratch symlink boundary');
      break;
    } catch (error) {
      if (error.code !== 'ENOENT') throw error;
      existing = path.dirname(existing);
    }
  }
}
async function observerIdentity(reader) {
  const source = await reader.command('git', ['rev-parse', 'HEAD']);
  boundary(SHA.test(source), 'observer source commit');
  const hash = createHash('sha256');
  for (const file of ['campaign.cjs', 'github.cjs', 'cli.cjs']) hash.update(await fs.readFile(path.join(__dirname, file)));
  for (const file of ['policy.cjs', 'batch-policy.cjs'])
    hash.update(await fs.readFile(path.join(__dirname, '..', 'merge-queue', file)));
  return { source, digest: hash.digest('hex') };
}
async function observe(options, { reader, observer, previous = null, save, stdout, progress,
  now = () => new Date(), wait = delay, signal } = {}) {
  let last = previous;
  for (let count = 0; count < options.observations; count++) {
    const receipt = newReceipt(options.campaign, now(), observer);
    reader.reconciliations = [];
    try {
      if (last) validateReceipt(last, options.campaign);
      const principal = await reader.preflight(last?.principal || options.campaign.expectedPrincipal);
      receipt.principal = principal;
      const snapshot = await reader.snapshot(options.campaign, principal);
      evaluate(receipt, snapshot);
      supersede(receipt, last);
    } catch (error) {
      const known = error instanceof ObservationError;
      receipt.outcome = 'blocked';
      receipt.blockers.push(problem(known ? error.code : 'invalid-evidence'));
      receipt.failure = { boundary: known ? error.boundary : 'unexpected-observer-error',
        type: known ? 'ObservationError' : 'UnexpectedError' };
    }
    receipt.reconciliations = [...reader.reconciliations];
    receipt.observedAt = now().toISOString();
    if (receipt.outcome === 'observing' && (count + 1 >= options.observations
      || reader.deadline - now().getTime() <= options.intervalSeconds * 1000)) {
      receipt.blockers.push(problem('budget-exhausted'));
      receipt.outcome = 'observing';
    }
    await save(receipt);
    stdout(JSON.stringify(receipt));
    progress(`${receipt.outcome}: ${receipt.pulls.filter(pull => pull.outcome === 'MERGED_VERIFIED').length}`
      + `/${options.campaign.targets.length}; ${receipt.blockers.map(item => item.code).join(',') || 'no blockers'}`);
    if (receipt.outcome === 'MERGED_VERIFIED') return 0;
    if (receipt.outcome === 'blocked') return 2;
    if (receipt.blockers.length) return 3;
    last = receipt;
    try { await wait(options.intervalSeconds * 1000, undefined, { signal }); }
    catch (error) {
      if (error.name !== 'AbortError') throw error;
      const cancelled = newReceipt(options.campaign, now(), observer);
      cancelled.blockers.push(problem('cancelled'));
      await save(cancelled);
      stdout(JSON.stringify(cancelled));
      progress('blocked: cancelled; no ongoing observation');
      return 2;
    }
  }
  return 3;
}
async function main(args = process.argv.slice(2)) {
  if (args.length === 1 && args[0] === '--help') { process.stdout.write(HELP); return 0; }
  let options;
  try { options = parseArgs(args); }
  catch { process.stderr.write('Invalid campaign arguments. Use --help; no observation started.\n'); return 64; }
  const controller = new AbortController();
  const cancel = () => controller.abort();
  process.on('SIGINT', cancel);
  process.on('SIGTERM', cancel);
  const reader = createReader(options, controller.signal);
  try {
    await scratchPath(options.worktree, path.join(options.output, 'receipt.json'));
    await reader.command('git', ['check-ignore', '--', path.join(options.output, 'receipt.json')]);
    await fs.mkdir(options.output, { recursive: true });
    await scratchPath(options.worktree, path.join(options.output, 'receipt.json'));
    const observer = await observerIdentity(reader);
    let previous = null;
    if (options.previousPath) {
      await scratchPath(options.worktree, options.previousPath);
      await reader.command('git', ['check-ignore', '--', options.previousPath]);
      try { previous = JSON.parse(await fs.readFile(options.previousPath, 'utf8')); }
      catch { throw new ObservationError('invalid-evidence', 'previous receipt JSON/file'); }
      validateReceipt(previous, options.campaign);
    }
    return await observe(options, { reader, observer, previous, signal: controller.signal,
      save: async receipt => {
        const name = `receipt-${receipt.observedAt.replaceAll(/[:.]/g, '-')}-${randomUUID()}.json`;
        await fs.writeFile(path.join(options.output, name), `${JSON.stringify(receipt, null, 2)}\n`, { flag: 'wx' });
        process.stderr.write(`Receipt: ${path.join(options.output, name)} sha256=${digest(receipt)}\n`);
      }, stdout: text => process.stdout.write(`${text}\n`),
      progress: text => process.stderr.write(`${text}\n`) });
  } catch (error) {
    const detail = error instanceof ObservationError ? `${error.code}: ${error.boundary}` : 'local I/O failed';
    process.stderr.write(`Observation stopped: ${detail}. Existing receipts retained; no success claimed.\n`);
    return 2;
  } finally {
    process.removeListener('SIGINT', cancel);
    process.removeListener('SIGTERM', cancel);
  }
}
if (require.main === module) main().then(code => { process.exitCode = code; }, () => {
  process.stderr.write('Observer failed; no success claimed.\n');
  process.exitCode = 2;
});
module.exports = { HELP, parseArgs, createReader, scratchPath, observerIdentity, observe, main };
