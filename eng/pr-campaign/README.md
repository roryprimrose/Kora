---
title: Read-only PR campaign evidence
description: Fixed-target, bounded observations with retained revision and merge receipts.
---

## Purpose and boundary

The [CLI](cli.cjs) observes a caller-owned, fixed explicit list of PRs. It does not enroll, label, merge, dispatch, retry CI, change queue state, or operate an AI/background service. It supports the enabled maintained queue profiles; absent, disabled, unknown or unreadable queue metadata stops observation rather than inventing readiness ([adapter](github.cjs)).

Campaign ownership is an explicit caller-supplied delivery ID, separate from the authenticated repository CLI principal and PR authors. `--expected-principal` optionally pins that principal; a previous receipt also pins its recorded principal. No contributor is a shared default. The HTTPS origin and authenticated repository read access must agree with `--repository` ([preflight](github.cjs)).

The controller remains the sole admission policy owner. This tool reuses its [state/job/ticket policy](../merge-queue/policy.cjs) and [batch contract](../merge-queue/batch-policy.cjs), not a copy of the scratch backlog watcher or another merge controller. It neither installs itself nor continues after its process/host exits. A runtime observer **cannot prevent, recall or alter delivery of older host chat messages**; receipt comparisons classify their old observations, not the host message itself ([supersession](campaign.cjs)).

## Commands

Use the repository's existing Node runtime, authenticated `gh` and Git. No packages, tokens, global configuration or daemon installation are required. Run from the owning checkout; use its ignored `.scratch` for output and retained previous receipts ([entry point](cli.cjs)).

```powershell
node .\eng\pr-campaign\cli.cjs --help
node .\eng\pr-campaign\cli.cjs --repository OWNER/REPO --prs 12,34 --owner delivery-2026-11 --output .scratch\pr-campaign\delivery-2026-11 --expected-principal LOGIN
```

For bounded repeated observation, supply both an observation count and a wall-clock budget. Each child command is also bounded. Ctrl+C terminates the current CLI child and waits for its exit before recording cancellation. Waiting between observations is abortable; no future work is scheduled after termination ([bounded lifecycle](cli.cjs), [child execution](github.cjs)).

```powershell
node .\eng\pr-campaign\cli.cjs --repository OWNER/REPO --prs 12,34 --owner delivery-2026-11 --output .scratch\pr-campaign\delivery-2026-11 --observations 20 --budget-seconds 1800 --interval-seconds 30 --command-seconds 20
```

Limits are 100 unique targets, 1,000 observations, 86,400 total seconds, 300 seconds per command, 3,600 seconds between observations and 1,000 records per paginated boundary. Hitting a limit never proves completion. Defaults are one observation, 120 total seconds, 20 per command and 30 between observations ([argument validation](cli.cjs), [pagination](github.cjs)).

Each completed observation creates a **new immutable** JSON file, prints its path and SHA-256 receipt ID to stderr, emits one machine-readable JSON line to stdout and prints a concise progress line to stderr. There is no mutable latest receipt and no automatic history deletion. An I/O/startup/invalid-previous-receipt failure returns nonzero and explicitly retains existing files; it never reinterprets an old file as current success ([persistence](cli.cjs)).

| Exit | Meaning |
|---|---|
| 0 | Every fixed target is `MERGED_VERIFIED`: GitHub state is `MERGED`, an actual merge SHA/merged timestamp exists and the SHA is reachable from newly fetched main. |
| 2 | Actionable blocker, unavailable/invalid evidence, cancellation, command timeout, or local persistence failure. |
| 3 | Explicit observation/time budget ended while targets remain incomplete. |
| 64 | Invalid CLI arguments; no observation started. |

These are [implemented exit meanings](cli.cjs); authoritative merge evidence is [evaluated separately](campaign.cjs) from queue or CI readiness.

## Evidence and reconciliation

- **Fixed targets:** the GraphQL query names every requested PR. Null/missing PRs, GraphQL errors (even with partial data), missing fields, missing label pages, inconsistent/duplicate REST pages or API/auth errors fail closed. Active batch members may be read as context to detect source changes; they are never added to campaign ownership ([collector](github.cjs)).
- **Revision:** queue state is read at a pinned ref commit and verified blob digest. PR metadata and the queue ref are re-read after fresh-main fetch/ancestry checks. Any intervening change invalidates that observation, rather than producing a mixed-revision receipt ([snapshot](github.cjs)).
- **Source and runs:** candidate ticket/title, trusted workflow source, first attempt, exact run ID, each job's run/attempt/source, workflow blob and maintained job inventory remain distinct. Combined-batch proof is explicitly `combined-batch-not-solo-head`; original and admitted branch-update heads are retained separately. Missing/skipped/waiting jobs are not successful producer evidence ([classifier](campaign.cjs)).
- **Qualification:** a durable completed-tip `{run, job, source}` is checked against that exact successful first-attempt ordinary-main run and qualification join. Other runs at the same SHA are retained as non-authoritative duplicates, not selected instead of the controller handoff. A failed duplicate is **not** diagnosed as a benign publication-guard failure from job names alone; the receipt says `publication-guard-cause-unconfirmed` ([qualification evaluation](campaign.cjs)).
- **Transport:** a nonzero read CLI result may be reconciled once by a new successful read of the **same endpoint/query**. Auth denial, timeout, cancellation, invalid/partial JSON or a failed second read remain failures. No `gh run watch` exit or log phrase clears producer failure. Error output/logs/HTML are not dumped into receipts ([transport](github.cjs)).
- **Blockers:** closed-unmerged, conflict, reviews, invalid enrollment/priority, exact blocked revision, recovery, changed source, actual required producer failure, other workflow failure, incomplete jobs, unknown metadata and unqualified main movement have explicit codes/actions. Timeout is not failure of an actual producer; green CI/enrollment is not merge completion ([problem codes](campaign.cjs)).

## Previous observations and schema

Pass an explicitly retained receipt from the **same repository, target set, owner and expected-principal configuration**. A different owner or malformed/unsupported receipt is rejected; it is not silently discarded or migrated ([receipt validation](campaign.cjs)).

```powershell
node .\eng\pr-campaign\cli.cjs --repository OWNER/REPO --prs 12,34 --owner delivery-2026-11 --output .scratch\pr-campaign\delivery-2026-11 --previous .scratch\pr-campaign\delivery-2026-11\receipt-PREVIOUS.json
```

The [versioned machine schema](receipt.schema.json) describes `kora-pr-campaign-v1`. Receipts bind the observer's Git source and code digest, authenticated principal, queue commit/blob, freshly fetched main, each PR/head/ticket/source, exact run/attempt/job/inventory and qualification identity. No credentials, titles, PR bodies, review text, raw logs or arbitrary server error content are persisted ([receipt construction](campaign.cjs), [metadata projection](github.cjs)).

The [synthetic merge receipt example](receipt.example.json) is a format fixture, not evidence about an actual repository or permission to write one. Its merge SHA differs from fetched main to demonstrate reachability rather than tip equality ([example regression](campaign.test.cjs)).

Changed head/ticket/enrollment/source or observer code produces explicit `superseded` entries with the prior receipt digest and old/current identities. Completed-tip context changes are also classified. The previous file is never edited; no terminal success or failure is copied into current authority. Receipts are local evidence, **not signed attestations, queue authority, or permission to write GitHub** ([comparison](campaign.cjs)).

## Deterministic validation

```powershell
node --test .\eng\pr-campaign\*.test.cjs
node --test .\eng\merge-queue\*.test.cjs .\eng\pr-campaign\*.test.cjs
```

The [regressions](campaign.test.cjs) exercise pure evaluation and fake gh/git/clock boundaries, plus bounded local child lifecycle behavior. They make no real GitHub writes and do not establish hosted CI, installed readiness, or ongoing observation. Preserve the [queue client protocol](../merge-queue/README.md#copilot-and-other-pr-clients) for publication and repair; this tool does not replace it.
