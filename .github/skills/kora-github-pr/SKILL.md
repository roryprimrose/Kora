---
name: kora-github-pr
description: 'Create and update Kora PRs with protected Auto-merge through the verified repository gh CLI account, never VS Code or MCP PR forms.'
---

## Runtime

Runtime support: `LocalOnly`. Requires the exact owning checkout, Git and a pre-authenticated GitHub CLI context with repository access. Select local engineering through the host-installed `agent-runtime` skill when available; resolve that skill by name, not a machine-specific repository dependency.

Without this local authenticated runner, return `UnsupportedRuntime` for writes. Do not substitute a VS Code account, browser session, MCP connector, another identity or invented successful publication.

## Required Account-Safe Adapter

Use this skill for Kora publication and PR repair. The Copilot/VS Code login, Git author and repository GitHub publisher can be different accounts. A repository selector in a PR form proves neither the authenticated publisher nor write permission.

All publication writes use **non-interactive `gh` CLI and Git** under the caller's explicitly recorded repository publishing principal. Do not call VS Code/MCP `create_pull_request`, `openPullRequest`, GitHub PR forms or browser automation, even if a generic skill example recommends that UI.

A generic skill may provide commit/template guidance; it cannot override this account and transport boundary. Invoke the applicable commit skill for new commits. If this repository skill is not yet indexed, read and apply this exact file rather than falling back to an account-ambiguous UI.

Do not change global authentication, log out another account, run `gh auth setup-git`, edit credentials/configuration or print tokens to make publication work. Use the established repository CLI context; account changes need explicit authorization.

## Inputs and Authority

Before writes, resolve:

- Expected GitHub principal from the caller's verified repository context, not the Copilot profile or commit author.
- Exact repository, owner/worktree/branch, intended base, committed source head and approved file/feature scope.
- Whether publication, draft and protected squash Auto-merge are authorized by the job/run. An admitted end-to-end child does not ask the coordinator for another slot, phase activation or ordinary publication permission.
- Actual pre-publication checks, failures and pending hosted checks under [contributor validation](../../../CONTRIBUTING.md#development-checks).

Keep one PR owner and one PR per feature or explicitly agreed shared prerequisite. Do not relabel a combined feature, absorb another owner's changes or transfer its proof to a different source head.

## Preflight: Verify Before Mutating

Pin every command to the actual owning directory/branch and use absolute project/script paths or Git `-C`. Never operate in an inherited coordinator checkout.

1. Verify `git status`, branch, exact committed head and `origin`. Match the approved repository and base; no default-branch push.
2. Read the actual API principal with `gh api --hostname github.com user --jq .login`. Compare it with the recorded expected principal and check the command's exit code.
3. Read `gh api --hostname github.com repos/<owner>/<repo>` and verify the intended `full_name` and `permissions.push`. A correct repository name alone is not account verification.
4. Successfully read the live queue mode and required protection/check publishers according to the [PR client protocol](../../../eng/merge-queue/README.md#copilot-and-other-pr-clients). Missing variables on a successful read differ from API/authentication failure; never turn an error into disabled or enabled mode.
5. Read current remote branch and PR state to prevent duplicates or overwriting intervening work. Reconcile App-created updates before an actual repair. A closed/merged PR is not permission to recreate retired or already-delivered work.

Pin the GitHub host as well as the account. `$Repository` below is host-qualified, such as `github.com/roryprimrose/Kora`, and must agree with the verified origin. Inherited `GH_HOST` or `GH_REPO` must not retarget these operations.

For example, the caller supplies `$ExpectedPrincipal`; do not infer it:

```powershell
$actualPrincipal = gh api --hostname github.com user --jq .login
if ($LASTEXITCODE -ne 0 -or -not [string]::Equals(
    $actualPrincipal, $ExpectedPrincipal, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Repository GitHub CLI identity is unverified or mismatched; no write performed.'
}
```

An identity mismatch or unavailable capability found before a write is a real preflight blocker. An actual authentication/authorization denial or policy rejection is not permission to retry through MCP/UI, switch identities or bypass protection.

## Commit and Push

Use repository commit conventions, normal hooks/signing and the required coauthor trailer. No empty commit, amend, hook/signing suppression, forced update or main chase merely for publication.

Git push authentication must also use the verified publisher. For the verified HTTPS GitHub origin, scope Git's credential helper to this command so a different Credential Manager account cannot silently perform the push:

```powershell
git -C $Worktree -c credential.helper= `
    -c 'credential.helper=!gh auth git-credential' `
    push --set-upstream origin "HEAD:refs/heads/$Branch"
```

The helper returns credentials privately to Git, not to chat or logs. This does not modify Git configuration. Do not assume an SSH key authenticates as the CLI principal; require the caller's verified SSH publishing identity or stop for an approved adapter.

Re-read the actual remote head after push and require it to equal the intended committed head. An ambiguous push must be reconciled before retry; never blindly replay or force over unexpected remote work.

## Create or Update the PR Without UI

Read the repository's PR template before drafting/editing its body, preserving headings, comments, checklist items and order. Include approved outcome, exact scope, unavailable boundaries and actual validation commands/results. Pending CI, unrun checks and historical failures remain explicit.

Look up the branch first:

```powershell
gh pr list --repo $Repository --head $Branch --state all `
    --json number,state,url,headRefOid,baseRefName
```

Reuse the matching owned open PR. If none exists and the remote head is verified, create one non-interactively:

```powershell
gh pr create --repo $Repository --head $Branch --base main `
    --title $Title --body-file $BodyPath
```

Use `--draft` only when explicitly requested; incomplete or failed mandatory local checks must not be represented as passed readiness. Full hosted candidate gates are enforced before merge, not replaced by a parent-issued local full-suite lease.

Keep generated PR bodies in the workflow's approved scratch/artifact location, not committed as unrelated source files. Do not expose credentials, private logs or content outside the approved change.

Verify returned PR number/URL, repository/base, exact remote head, draft state and body/scope. A newly created PR's actual author must match the expected publisher. A filled form, button awaiting a click, pushed branch or proposed URL is **not a created PR**.

## Enroll Protected Auto-merge

When authorized, publish and enroll as the same admitted job. With hosted mode enabled, Auto-merge is queue intent: GitHub serializes candidate updates, builds, portable coverage, Windows/installer validation and protected merge.

Read the current owned PR head immediately before the request:

```powershell
gh pr merge $PullRequest --repo $Repository --auto --squash `
    --match-head-commit $VerifiedRemoteHead
```

No `--admin`, direct force merge, manual candidate dispatch, fabricated check or queue-state edit. Do not rebase repeatedly while the queue updates its selected candidate.

Read back the actual PR and enrollment:

```powershell
gh pr view $PullRequest --repo $Repository `
    --json number,url,state,author,baseRefName,headRefOid,isDraft,autoMergeRequest,mergeStateStatus,mergeCommit,statusCheckRollup
```

Verify repository/base/author, actual URL/current remote head, draft state and `autoMergeRequest.mergeMethod`/`enabledAt` against authority. Already enrolled means readback, not disable/re-enable to force a fresh ticket. Preserve a chosen draft/PR-only exception explicitly; never infer Auto-merge authority. Reconcile a changed head before claiming exact-head enrollment.

Record current candidate-head App checks and ticket when available, not merely main-workflow results. Pending is not passed; skipped ordinary waiting-PR jobs are not successful candidate admission. Enrollment is a publication milestone, not merge proof.

## Handoff, Repair and Uncertainty

For a coordinator job, immediately publish `pr-enrolled-ci-wait` to the registered shared outbox after actual PR/enrollment readback and all local finite consequential commands have terminated/joined. Follow the agent's [publication milestone protocol](../../agents/roadmap-coordinator.agent.md#publication-milestone-protocol) and [mailbox protocol](../../agents/roadmap-coordinator.agent.md#shared-file-mailbox-protocol); do not wait for final merge or parent ACK, or send a redundant native message.

The agent owns event fields, once-only release/refill, atomic file delivery/readback, retained-owner continuation and authority exceptions; do not duplicate that protocol here. Explicitly retained legacy native runs preserve their recorded transport until a safe transition.

Apply the agent's [routine continuation rule](../../agents/roadmap-coordinator.agent.md#routine-continuation-without-user-input): finish the authorized handoff and release execution without a stop/wait/check-again user prompt. Retain the verified administrative inbox continuation or record the need for a budgeted wake. Supported re-entry retains existing authority and repair admission; pending hosted checks do not authorize child CI polling or imply continuous monitoring. The coordinator keeps its separate bounded scheduling loop running.

Return one compact durable handoff with principal, repository, exact launch-mapped owner/branch/current remote head, actual PR URL, Auto method/`enabledAt` or explicit authority exception, current candidate state, evidence pointers, failures and command termination. Persist deep CLI evidence in the owner's receipt; status alone is not an event, but verified atomic mailbox publication is delivery without native messaging.

Retain the same source owner/worktree and PR for approved review/CI repair. Preserve App updates, changed-input validation and required checks. Hosted CI wait does not consume execution allocation.

On supported resume, report concrete source-bound failure for repair admission; an actual scoped fix and normal same-PR push return the agent's distinct repair handoff.

## Scratch Usage

Keep durable receipts in the established host-owned artifact location. For transient repository scratch, use `New-NetAaaScratchPath -RepoRoot <owning-worktree> -SkillName 'kora-github-pr'` from the installed shared helper.

Coordinator transport is separate from transient PR scratch: use the exact absolute shared dataset/run roots supplied at launch, not a relative `.scratch` directory in each worktree. The default dataset lives under the common Git metadata directory so disposable worktrees/chats do not own its lifetime. Follow the agent's [ownership/recovery](../../agents/roadmap-coordinator.agent.md#shared-dataset-ownership-and-recovery) and [retention/cleanup](../../agents/roadmap-coordinator.agent.md#shared-dataset-retention-and-cleanup) rules: one fenced scheduling leader, shared accounting across sessions/runs, retained source owners and no deletion of live/uncertain or referenced data. A coordinator takeover is not renewed publication/merge authority. Never bootstrap an unavailable scratch helper merely for transport.

Verify approved `.scratch/` ignore policy; obtain consent before changing it. Do not add a dependency or bootstrap tooling just for scratch.

For `MERGED_VERIFIED`, require authoritative merged state, actual merge SHA and reachability from freshly fetched main. Auto-merge requested, green CI or an archived session alone is not merge proof. No unapproved cleanup or unsupported continuous-monitoring claim.

After an ambiguous PR/enrollment write, read the exact existing branch/PR state through this same authorized CLI before deciding whether a retry is needed. Never convert unavailable readback into success or use another account/transport after denial.
