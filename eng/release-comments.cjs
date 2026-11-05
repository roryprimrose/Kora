'use strict';

const { execFileSync } = require('node:child_process');

const repository = 'roryprimrose/Kora';
const versionPattern = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-beta\d+)?$/;
const shaPattern = /^[a-f0-9]{40}$/;

function requireValue(condition, message) {
  if (!condition) throw new Error(message);
}

function releaseIdentity(release) {
  requireValue(release && Number.isSafeInteger(release.id) && release.id > 0 &&
    typeof release.tag_name === 'string' && release.tag_name.startsWith('v') &&
    versionPattern.test(release.tag_name.slice(1)), 'Invalid release identity.');
  const version = release.tag_name.slice(1);
  const markers = [...(release.body ?? '').matchAll(/<!-- kora-source: ([a-f0-9]{40}) -->/g)];
  requireValue(release.draft === false && typeof release.prerelease === 'boolean' &&
    release.prerelease === version.includes('-beta') && markers.length === 1 &&
    release.target_commitish === markers[0][1] &&
    release.html_url === `https://github.com/${repository}/releases/tag/${release.tag_name}` &&
    typeof release.published_at === 'string' && Number.isFinite(Date.parse(release.published_at)),
  'Release is not a published exact-source/channel Kora release.');
  return { version, source: markers[0][1], published: Date.parse(release.published_at) };
}

function readGitHistory(source, baseline) {
  const args = ['rev-list', '--topo-order', source];
  if (baseline) args.push(`^${baseline}`);
  return execFileSync('git', args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 })
    .trim().split(/\r?\n/).filter(Boolean);
}

const linkedIssuesQuery = `
  query($owner: String!, $repo: String!, $number: Int!, $cursor: String) {
    repository(owner: $owner, name: $repo) {
      pullRequest(number: $number) {
        merged
        baseRefName
        baseRepository { nameWithOwner }
        mergeCommit { oid }
        closingIssuesReferences(first: 100, after: $cursor) {
          nodes { number state repository { nameWithOwner } }
          pageInfo { hasNextPage endCursor }
        }
      }
    }
  }`;

async function closingIssues(github, owner, repo, number, commits) {
  const issues = new Set();
  const cursors = new Set();
  let cursor = null;
  for (;;) {
    const data = await github.graphql(linkedIssuesQuery, { owner, repo, number, cursor });
    const pr = data?.repository?.pullRequest;
    requireValue(pr && typeof pr.merged === 'boolean', `Missing PR #${number} linkage evidence.`);
    if (!pr.merged || pr.baseRefName !== 'main' || pr.baseRepository?.nameWithOwner !== repository) break;
    requireValue(shaPattern.test(pr.mergeCommit?.oid ?? ''), `Missing PR #${number} merge revision.`);
    if (!commits.has(pr.mergeCommit.oid)) break;
    const connection = pr.closingIssuesReferences;
    requireValue(Array.isArray(connection?.nodes) && typeof connection.pageInfo?.hasNextPage === 'boolean',
      `Incomplete PR #${number} issue linkage.`);
    for (const issue of connection.nodes) {
      requireValue(issue && Number.isSafeInteger(issue.number) && issue.number > 0 &&
        ['OPEN', 'CLOSED'].includes(issue.state) && typeof issue.repository?.nameWithOwner === 'string',
      `Malformed PR #${number} linked issue.`);
      if (issue.state === 'CLOSED' && issue.repository.nameWithOwner === repository) issues.add(issue.number);
    }
    if (!connection.pageInfo.hasNextPage) break;
    cursor = connection.pageInfo.endCursor;
    requireValue(typeof cursor === 'string' && cursor.length > 0 && !cursors.has(cursor),
      `Invalid PR #${number} issue pagination.`);
    cursors.add(cursor);
  }
  return issues;
}

async function reconcile({ github, context, version, sourceRevision, readHistory = readGitHistory }) {
  requireValue(context?.repo && `${context.repo.owner}/${context.repo.repo}` === repository &&
    ['push', 'workflow_dispatch'].includes(context.eventName) && context.sha === sourceRevision &&
    (context.ref === 'refs/heads/main' ||
      (!version?.includes('-beta') && context.ref === `refs/tags/v${version}`)) &&
    versionPattern.test(version ?? '') && shaPattern.test(sourceRevision ?? ''),
  'Issue release comments require the canonical main/stable-tag publication workflow.');
  const { owner, repo } = context.repo;
  const parameters = { owner, repo };
  const { data: release } = await github.rest.repos.getReleaseByTag({ ...parameters, tag: `v${version}` });
  const current = releaseIdentity(release);
  requireValue(current.version === version && current.source === sourceRevision, 'Published release source changed.');

  const history = readHistory(sourceRevision);
  requireValue(Array.isArray(history) && history.length > 0 && history[0] === sourceRevision &&
    history.every(sha => shaPattern.test(sha)), 'Complete source history is unavailable.');
  const ancestors = new Set(history);
  const releases = await github.paginate(github.rest.repos.listReleases, { ...parameters, per_page: 100 });
  const boundaries = new Set();
  for (const previous of releases) {
    if (previous.id === release.id || previous.draft === true) continue;
    // Non-version releases are not Kora distribution boundaries.
    if (!versionPattern.test(previous.tag_name?.slice(1) ?? '') || !previous.tag_name.startsWith('v')) continue;
    const identity = releaseIdentity(previous);
    if (identity.published >= current.published || identity.source === sourceRevision ||
      (!release.prerelease && previous.prerelease) || !ancestors.has(identity.source)) continue;
    boundaries.add(identity.source);
  }
  const baseline = history.find(sha => boundaries.has(sha));
  const range = baseline ? readHistory(sourceRevision, baseline) : history;
  requireValue(Array.isArray(range) && range.length > 0 && range[0] === sourceRevision &&
    range.every(sha => shaPattern.test(sha) && ancestors.has(sha) && sha !== baseline),
  'Published release commit range is unavailable.');
  const commits = new Set(range);
  const prs = new Set();
  for (const commit_sha of commits) {
    const associated = await github.paginate(github.rest.repos.listPullRequestsAssociatedWithCommit,
      { ...parameters, commit_sha, per_page: 100 });
    for (const pr of associated) {
      requireValue(Number.isSafeInteger(pr.number) && pr.number > 0, 'Invalid associated PR number.');
      prs.add(pr.number);
    }
  }
  const issues = new Set();
  for (const number of prs) {
    for (const issue of await closingIssues(github, owner, repo, number, commits)) issues.add(issue);
  }

  const pending = [];
  for (const issue_number of [...issues].sort((a, b) => a - b)) {
    const marker = `<!-- kora-fix-version:v1:${version} -->`;
    const body = `Fix published in Kora ${version} (${release.prerelease ? 'beta prerelease' : 'stable release'}).\n\n` +
      `${release.html_url}\n\n${marker}`;
    const comments = await github.paginate(github.rest.issues.listComments,
      { ...parameters, issue_number, per_page: 100 });
    const existing = comments.filter(comment => comment.user?.login === 'github-actions[bot]' &&
      comment.user.type === 'Bot' && typeof comment.body === 'string' && comment.body.includes(marker));
    requireValue(existing.length <= 1 && existing.every(comment => comment.body === body),
      `Conflicting release comment on issue #${issue_number}.`);
    if (existing.length === 0) pending.push({ issue_number, body });
  }

  // Finish all discovery before writing; an uncertain POST is reconciled by the next run, not retried here.
  let written = 0;
  for (const { issue_number, body } of pending) {
    const { data: issue } = await github.rest.issues.get({ ...parameters, issue_number });
    requireValue(issue?.number === issue_number && ['open', 'closed'].includes(issue.state) &&
      !issue.pull_request, `Issue #${issue_number} identity/state is unavailable.`);
    if (issue.state !== 'closed') continue;
    await github.rest.issues.createComment({ ...parameters, issue_number, body });
    written++;
  }
  return { baseline: baseline ?? null, issues: issues.size, written };
}

module.exports = { reconcile };
