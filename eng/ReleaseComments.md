# Published fix-version issue comments

[CI](../.github/workflows/ci.yml) runs [release comment reconciliation](release-comments.cjs)
only after successful canonical main/stable-tag publication, or after the existing
publication checker successfully verifies that exact version is already published.
Failed, cancelled, draft, feature, fork, PR and merge-queue candidate publication
paths cannot authorize issue writes. The existing release job retains contents
write for publication and adds PR read and issues write permissions. It checks out
only the admitted main/tag source, without
persisted credentials, and never executes issue/PR text or downloaded artifacts.

The [batch-release path](../.github/workflows/ci.yml) runs the same reconciliation
after successful completed-tip publication under the existing controller mutation
lock. It retains batch-only routing and qualification; the ordinary release job
cannot publish or reconcile comments in `batch-v1`. Batch retries retain main's
full-validation path rather than using the ordinary already-published shortcut
([workflow regressions](release-comments.test.cjs)).

The range starts after the nearest reachable, earlier published Kora release.
Drafts, failed intermediate builds and releases on unrelated history do not advance
that boundary. Beta releases use the previous beta or stable release; stable
releases use the previous stable release, announcing fixes previously available
only in beta again as stable. A first release scans all reachable history.
Full checkout history is required. Versioned published releases with malformed
source/channel metadata fail reconciliation rather than silently changing the range.

Every commit in that range is checked for associated PRs. Only merged PRs targeting
this repository's `main`, whose merge commit is in the range, qualify. GitHub's
`closingIssuesReferences` connection is the explicit link authority (closing-keyword
or development-linked issues), not arbitrary issue mentions, PR titles or text
parsing. All REST collections and the GraphQL closing-link connection are paginated.
Only closed issues in this repository receive comments, with state checked again
immediately before writing. Cross-repository issues and reopened issues are skipped.
This does not infer a fix from a commit or maintain a new intake service.

Example comment:

```text
Fix published in Kora 1.0.0-beta4 (beta prerelease).

https://github.com/roryprimrose/Kora/releases/tag/v1.0.0-beta4

<!-- kora-fix-version:v1:1.0.0-beta4 -->
```

The version and URL come from the verified published release, not a planned build.
Each published version/issue pair has one marker, accepted only on an exact matching
`github-actions[bot]` comment. User comments cannot suppress delivery. Discovery
finishes before writes; conflicting bot comments fail explicitly. All API errors
propagate, and an uncertain comment POST is never retried in the same invocation.
Rerunning CI for the exact published source/version reconciles partial delivery
through the already-published path, without rebuilding or republishing: artifact
downloads are skipped and the publication script re-verifies the immutable release
before the comment step. Existing per-version release-job concurrency serializes
publication and comment retries. Stable and beta releases have separate markers.
Links/issue state are evaluated at reconciliation time; an unlinked or reopened
issue is not announced. GitHub does not provide an atomic closed-state-and-comment
operation, so a concurrent reopen after the last state read can still race a POST.

## Machine-readable claim format

The exact v1 marker is `<!-- kora-fix-version:v1:<version> -->`, for example
`<!-- kora-fix-version:v1:1.2.3 -->` or
`<!-- kora-fix-version:v1:1.2.3-beta4 -->`. The version is the published version
without a leading `v`; the following full-line regex captures it:

```regex
^<!-- kora-fix-version:v1:((?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)(?:-beta\d+)?) -->$
```

The canonical release URL appears on its own prose line and must exactly equal
`https://github.com/roryprimrose/Kora/releases/tag/v<version>`. Clients can construct
that URL from a valid captured version rather than following an arbitrary comment
link. No HTML, issue text, alternate host or URL fragment is part of this contract.
Unknown marker versions and malformed values are not v1 claims.

Issue lookup clients may read a bounded set of recent comments on closed matching
issues to display these release claims. Comment content, markers, links and author
metadata are untrusted input: even a matching marker is not verification that the
release exists, contains the PR or fixes the reported problem. Display it as a
reported release version, not a verified fix. A missing marker in a bounded window
does not establish that no fix version was published.

## Deterministic validation

Run from the repository root:

```powershell
node --test .\eng\release-comments.test.cjs
```

The fixtures inject in-memory GitHub responses and commit history: no real GitHub
reads/writes, Git subprocess, packages, builds, credentials or scratch files.
They cover release ranges/channel promotion, failed-build gaps, first release,
PR/link eligibility, pagination, deduplication, partial/uncertain-write retries,
reopening, spoofed/conflicting markers, malformed evidence, API failures and workflow
wiring. CI runs this command alongside the existing publication contract tests.
