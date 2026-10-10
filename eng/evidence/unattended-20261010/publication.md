# Unattended fake release publication/retry proof

## Fresh execution

The complete maintained [publication contract suite](../../Test-GitHubRelease.ps1)
passed on `56df1ee136a687d8cd3e2294ca25e38f9eab75dd` in a verified distinct,
clean task worktree. The root worktree was not used. This run made no changes
to the publication implementation, tests or coupled helpers: no concrete
coupled defect surfaced.

```powershell
pwsh -NoProfile -NonInteractive -File .\eng\Test-GitHubRelease.ps1
```

| Measurement | Result |
| --- | --- |
| PowerShell / platform | 7.6.6 / Windows |
| Start (UTC) | 2026-10-09T20:44:51.9688519+00:00 |
| Finish (UTC) | 2026-10-09T20:51:14.7642270+00:00 |
| Duration / timeout | 382.661 seconds / 900 seconds |
| Exit / timeout reached | 0 / no |
| Assertions | 381 |
| Actions-style process cases | 56 |
| Partial-write/hard-interruption retries | 15 |
| Total Actions runner invocations | 71 (56 initial cases + 15 retries) |
| Stderr | Empty |
| Temporary fixture entries after suite cleanup | 0 |

The suite emitted these exact terminal summaries:

```text
Actions pwsh process tests passed: 56 cases plus 15 partial-write/hard-interruption retries.
Publication tests passed: 381 assertions; draft lifecycle, exact bytes, collisions, retries, source/channel, immutable no-op and Actions process exit.
```

The [sanitized receipt](publication.json) records the command, timing, counts,
raw-log hashes and seven directly coupled input identities. Git blob IDs
identify canonical baseline bytes; checkout SHA-256 values identify the
Windows on-disk inputs actually executed.

## Exercised guards

- Accepted 404 absence resets the native exit status for the Actions epilogue;
  authentication/authorization, throttling and service errors fail closed.
  Untrusted workflow origin, event, ref and source values are rejected.
- Invalid ICE receipts and missing/corrupt source tools cannot cause writes.
  Exact-source source-tool archives, payload manifests, hidden payload files,
  asset digests and sizes, release provenance and checksums remain checked.
- Draft creation is followed by authenticated direct-ID readback, including
  hidden/stale list results. Malformed create responses, missing/incorrect
  typed IDs, source/body/channel/publication drift, direct-read failures and
  timeout, competing releases and upload-time body drift fail closed.
- Complete draft retry preserves existing asset and original provenance
  bytes despite a changed run ID and payload timestamps. Conflicting
  source/channel, ZIP bytes and final asset digests cannot be overwritten.
  Draft checks do not promote; exact published releases remain immutable
  no-ops. Historical eight-asset publications stay read-only; historical
  draft provenance cannot be rewritten to add tools.
- Legacy draft disclosure repair preserves assets and generated notes.
  Its before/after failure and hard-interruption paths are included.
- Every create/upload/tag/publish failure before and after a write, plus
  controlled hard interruption, is retried from a fresh invocation and
  reconciles one publication. The Actions-style child runners assert both
  exit status and success-output presence.
- Pagination, duplicate releases/assets, malformed or conflicting provenance
  and checksums, tag/source drift and bounded annotated-tag traversal are
  exercised. Stable and beta latest policies remain distinct; an existing
  stable tag is not replaced.
- Success and failure staging cleanup is checked by the suite. The outer
  runner also verified the isolated temporary directory was empty, then
  removed that empty directory. Temporary wrapper/state code was not kept.

## Safety and limits

All publication calls used the [fake GitHub fixture](../../tests/GitHubRelease.Fixture.ps1),
including the Actions-style child processes. Unexpected fake operations
throw rather than fall through to a live executable. No real release or tag
was published, edited, moved or deleted. There were no dependency
installs/restores, account/provider actions, installer actions, elevation,
device/GUI steps, credential acquisition or consent/license acceptance.

This proves maintained script contracts against simulated GitHub state. It
does **not** qualify live GitHub authentication, permissions, network
behavior or service consistency. Application/MSI/setup bytes are synthetic;
the receipt's ICE/package-inspection fields are fixtures, not actual ICE or
installation evidence. Controlled child exits are not machine-shutdown
tests. Signing, installed lifecycle and production acceptance remain
unproven. Other maintained suites are outside this publication task.

Raw stdout, empty stderr and execution metadata are retained externally in
the parent session's `maintained-proofs/publication` evidence, run
`run-20261009T204450031Z`. Only this sanitized receipt and proof are committed;
private filesystem paths, raw output and generated candidate artifacts are
excluded.
