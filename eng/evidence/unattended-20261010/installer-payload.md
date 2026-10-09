# Maintained installer payload rejection proof

**PASS** - full maintained suite, unattended, in a verified distinct task
worktree based on `56df1ee136a687d8cd3e2294ca25e38f9eab75dd`. The root checkout
was not used for execution or writes. No production code change was needed.

The run started at `2026-10-09T20:44:51.5208634Z` and finished at
`2026-10-09T20:45:02.5386968Z` (2026-10-10 local task date), exit code **0**,
elapsed **11.018 seconds**. PowerShell **7.6.6** was used; .NET SDK **10.0.401**
and runtime **10.0.12** were available but no restore or compiler was invoked.

## Command and coverage

From the assigned isolated worktree:

```powershell
pwsh -NoLogo -NoProfile -NonInteractive -File .\eng\Test-InstallerPayloadContracts.ps1
```

The complete [maintained suite](../../Test-InstallerPayloadContracts.ps1)
exercised the [manifest validator](../../Test-InstallerPayload.ps1) and the
transferred-payload rejection boundary in
[installer packaging](../../Build-Installer.ps1).

| Scenario | Expected boundary/result | Observed |
| --- | --- | --- |
| Exact manifest, including a Windows hidden notice | Two exact synthetic files verify | Passed |
| Changed application bytes | Digest mismatch | Rejected |
| Changed hidden-notice bytes | Digest mismatch | Rejected |
| Removed hidden file | File-count mismatch | Rejected |
| Added unexpected file | File-count mismatch | Rejected |
| Manifest version mismatch | Build-identity mismatch | Rejected |
| Manifest source-revision mismatch | Build-identity mismatch | Rejected |
| Missing manifest | Missing manifest error | Rejected |

**Counts:** one positive manifest check and seven rejection cases passed.
Three additional tracked/present MSI source preconditions passed:
[project](../../../installer/Kora.Msi/Kora.Msi.wixproj),
[authoring](../../../installer/Kora.Msi/Package.wxs), and
[dependency lock](../../../installer/Kora.Msi/packages.lock.json).

The counts above describe the maintained suite's sequential assertions, not
an independent test-runner report. Successful completion requires every
rejection to match its expected error, **zero `dotnet` calls**, and no change
to installer staging after each case. The suite traps `dotnet` with a function
that increments a counter and throws; therefore compiler/restore/publish/WiX
work cannot be reported as a passing rejection.

## Ownership, integrity and cleanup

- The worktree and its clean baseline were verified before execution.
- Only the suite's freshly owned synthetic fixture was mutated. Its
  `Kora.dll` was text, not an executable assembly; the fixture was never
  installed, loaded or launched.
- The valid manifest was tied to version `0.1.0` and the exact source revision.
  Hidden file content, inventory changes and build-identity changes retained
  their maintained fail-closed checks.
- Installer staging was absent before and after the run. An external recursive
  inventory check also confirmed no staging changes.
- The suite's `finally` removed its owned fixture. Before/after temporary
  directory snapshots found **zero new fixture directories remaining**.
- SHA-256 values of the three inspected scripts and three MSI authoring/lock
  files were unchanged after execution. The
  [sanitized receipt](installer-payload.json) pins the exact executed file
  bytes, run timestamps, case results and raw-log digest.
- Raw output and the machine-local runner receipt were retained outside the
  repository as `contracts-20261009T204451298Z.log` and
  `run-20261009T204451298Z.json`. Absolute host/user/temporary paths are omitted
  from this committed evidence.
- No scratch code, synthetic assets or raw machine-local logs are committed.

## Scope limits

This is synthetic rejection-boundary evidence, not inspection of a fresh
published application payload and not installer/release acceptance. No app,
MSI, setup, elevation, GUI, device/audio, machine-configuration or startup
operation was performed. No new consent, credentials, provisioning or license
acceptance was requested. There were no blocked slices within this maintained
suite. Fresh published-byte inspection belongs to a separate proof task.
