# Maintained source-bootstrap synthetic proof

## Result

**PASS: 82 checks, zero unexpected failures, exit code 0.**

The complete maintained [source-bootstrap fixture suite](../../Test-SourceBootstrap.ps1)
ran unattended in a verified, distinct task worktree based on
`56df1ee136a687d8cd3e2294ca25e38f9eab75dd`. It used fresh, owned output outside
the repository. The root checkout was not edited or built.

Execution: 2026-10-10 07:42:59 to 07:44:01 (+11:00), 62.095 seconds.
PowerShell 7.6.6 ran with `-NoProfile -NonInteractive` on .NET 10.0.12.
The installed SDK was 10.0.401; the suite models SDK command selection and
does not perform an application restore, build or publish.

The [sanitized machine-readable result](source-bootstrap.results.json) preserves
all 82 check names, the tested revision, exact eight-file source-tool inventory,
byte lengths and SHA-256 pins. Bootstrap version remains 1.1.0. No maintained
script or shared helper changes were necessary; historical evidence is unchanged.

## What this proves

- Maintained script parsing and the exact source-tool closure.
- Read-only preview, explicit build trust, canonical public origin and unavailable
  release/protected activation claims.
- Fail-closed root ownership, receipt types/provenance, pinned inputs, tool bytes,
  payload/evidence integrity and prerequisite handling.
- Detached, clean, exact-revision checkouts with checkout-local long-path support.
- Non-destructive reruns and retries; preserved edits, partial stages, earlier
  output and a user-data stand-in.
- Injected restore/build/publish/inspection/smoke/empty-output failures cannot
  promote output; all six corresponding failure receipts remain retained.
- Post-build tracked/untracked/origin/revision changes prevent promotion, and
  subsequent retries refuse destructive adoption.
- Lock contention, partial clones, missing output receipts and junction roots
  are refused.
- Real owned PowerShell child nonzero exit and bounded timeout rejection,
  plus real command nonzero-exit logging.
- Wrong SDK rejection and modeled locked restore, separated Release build,
  no-build/no-restore publish and owned artifact arguments.

## Qualification limits and exclusions

This is **synthetic orchestration evidence**, not native, installed or production
acceptance. Compile, inspection, SQLite-lock and orchestration smoke mechanisms
are replaced by fixtures. The bounded failure/timeout children are PowerShell
fixtures, not Kora. Expected negative-fixture Git errors in the raw log are
intentional rejection evidence, not unexpected suite failures.

No Kora process, installer, real source-stage build, dependency restore, startup
registration, machine install, elevation, account/provider setup, device/audio
interaction or global Git configuration change was performed. Git identity and
long-path configuration are limited to owned fixture repositories. Real
source-stage build proof is separately assigned and was not duplicated.

No synthetic-suite slice was blocked or required new consent, credentials,
license acceptance, GUI input or administrative policy. This result does not
authorize any excluded slice or protected activation.

## Evidence and cleanup

Raw evidence is retained externally under run identifier
`suite-20261010-074259-f02bddb6`; local paths are deliberately omitted from
committed deliverables. Its test receipt and execution-log SHA-256 hashes are
recorded in the machine-readable result. Raw logs and fixture stages are not
copied into the repository.

The temporary child script and fixture junction were confirmed absent after
success. Owned raw fixture repositories and failure stages remain available for
review. The task worktree remains for parent-coordinated merge and cleanup.
