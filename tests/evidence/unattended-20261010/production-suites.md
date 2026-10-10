# Maintained production-suite qualification: 2026-10-10

**Result: all authorized tests passed; three native-UI cases remain explicitly unqualified.**
This is not an unfiltered full-Windows-suite success claim.

The machine-readable [receipt](production-suites.json) binds the results to source
`56df1ee136a687d8cd3e2294ca25e38f9eab75dd` and records exact counts, UTC execution
times, commands, safety exclusions, and SHA-256 hashes of external build/discovery
logs and TRX reports. The local qualification date is UTC+11.

## Isolation and preparation

- Ran only in the parent-confirmed, distinct `production-test-isolated-worktree`
  worktree, branch `agents/production-test-isolated-worktree`, initially clean at
  the source revision above. No build or edit was performed in the main checkout.
- Read [repository instructions](../../../.github/copilot-instructions.md),
  [build policy](../../../Directory.Build.props),
  [maintained CI](../../../.github/workflows/ci.yml), all five test project
  definitions, and the relevant safety fixtures before executing their tests.
- Used installed .NET SDK 10.0.401, runtime 10.0.12, and PowerShell 7.6.6 on
  Windows x64. Shell operations used `pwsh -NoProfile -NonInteractive`; no new
  global dependency/tool installation, credentials, license acceptance, prompts,
  elevation, or nested agents were needed.
- First ran `dotnet build .\Kora.slnx --configuration Release --no-restore -warnaserror`.
  It failed with `NETSDK1004` because the fresh worktree lacked dependency assets.
  Only then ran `dotnet restore .\Kora.slnx --locked-mode`, which succeeded.
- Repeated the same Release solution build: **exit 0, zero warnings, zero errors**.
  No source, test-fixture, package-lock, or shared `eng` changes were necessary.

## Verified suite results

| Suite | Executed | Passed | Failed | Runner skips | Safety exclusions | Exit |
|---|---:|---:|---:|---:|---:|---:|
| Kora.Core.UnitTests | 1,314 | 1,314 | 0 | 0 | 0 | 0 |
| Kora.Application.UnitTests | 3,570 | 3,570 | 0 | 0 | 0 | 0 |
| Kora.Tools.UnitTests | 90 | 90 | 0 | 0 | 0 | 0 |
| Kora.Definitions.UnitTests | 6 | 6 | 0 | 0 | 0 | 0 |
| Kora.Windows.IntegrationTests | 1,291 | 1,291 | 0 | 0 | 3 | 0 |
| **Total** | **6,271** | **6,271** | **0** | **0** | **3** | **0** |

All five external TRX reports were parsed independently: counters agree with
result-node counts and every result node passed. Windows unfiltered discovery
listed 1,294 cases; authorized discovery listed 1,291. The discovery difference
was exactly the three selectors below, and the authorized discovered test
identities exactly match the 1,291 executed TRX test identities. These are actual
complete runner results, not source-test counts, coverage thresholds, or sampled
checks. The maintained inventory including safety exclusions is 6,274 cases.

## Safety exclusions and preserved boundaries

The following exact methods in
[WindowsPresenceWindowInputTests](../../Kora.Windows.IntegrationTests/Presentation/WindowsPresenceWindowInputTests.cs)
were excluded using `FullyQualifiedName!=` predicates, not a class/namespace-wide
filter and not changes to the maintained tests:

1. `Kora.Windows.IntegrationTests.Presentation.WindowsPresenceWindowInputTests.Native_window_styles_switch_click_through_without_changing_position_or_activation_flags`
   creates and mutates a real Win32 HWND.
2. `Kora.Windows.IntegrationTests.Presentation.WindowsPresenceWindowInputTests.Native_hit_testing_reaches_the_underlying_window_except_during_Ctrl_interaction`
   creates native windows/private desktops and switches the test thread desktop.
3. `Kora.Windows.IntegrationTests.Presentation.WindowsPresenceWindowInputTests.Native_hit_testing_repeatedly_restores_and_releases_private_desktops`
   repeats those native window/private-desktop effects.

Even hidden or private native desktops are outside this task's native-UI
authorization. These three cases were **not executed** and are safety gaps, not
runner-reported skips. All other maintained Windows tests remained selected.

Inspected and retained:

- [HeadlessTestApp](../../Kora.Windows.IntegrationTests/HeadlessTestApp.cs) and
  [HeadlessSession](../../Kora.Windows.IntegrationTests/HeadlessSession.cs):
  off-screen Avalonia/Skia only, without the real Kora composition root.
  **66 cases** across all eight headless test classes passed, including runtime
  accessibility, synthetic clipboard preview, question, session, and native-UX
  fixture contracts.
- [FixtureBoundaries](../../Kora.NativeUxFixture/NativeUx/FixtureBoundaries.cs),
  private clipboard-native fakes, fake setup/installer collaborators, stub HTTP
  handlers, and injected audio/privacy seams: no shared clipboard content,
  provider/network execution, actual installation, capture, playback, account
  changes, or production settings/database access.
- Metadata-only native endpoint/installed-voice/readiness queries and invalid
  argument/missing-endpoint tests remain included. They neither start capture or
  synthesis nor mutate physical-device or user settings. Owned synthesis gain
  and rate tests use injected setters/actions rather than real speech.
- Synthetic preference/profile/storage roots and owned file-link/ACL/encryption
  tests remain included; no real profile packages or production storage were read.
- [OwnedStorageFixture](../../Kora.Windows.IntegrationTests/Storage/OwnedStorageFixture.cs)
  and [OwnedStorageChildProcess](../../Kora.Windows.IntegrationTests/Storage/OwnedStorageChildProcess.cs)
  restrict storage to fresh GUID-named roots. Each interruption test launches the
  exact test-assembly storage helper, validates its owned root, and terminates only
  the child process it created. No name-based or shared-process termination.

| Retained owned-child recovery family | Passed cases |
|---|---:|
| Task intent/dispatch/terminal/recovery | 8 |
| Diagnostic/audit/activity/retention evidence and unsafe reopen refusal | 12 |
| Question/grant/audit and active session generation | 6 |
| Task cancellation/question/audit | 2 |
| Session metadata/schema migration/consolidation | 8 |
| **Owned-child interruption/recovery total** | **36** |

The four owned-handle release-barrier tests also passed. These numbers are
subsets of the Windows total, not extra tests. All existing child-helper and
recovery cases were preserved.

## Reproduction and raw proof

Raw TRX/logs remain in the parent-assigned external production proof directory.
Absolute machine/user paths, raw output, binaries, assets, and secrets are not
part of this committed evidence. In these commands, `$ProofRoot` denotes that
external directory; run them only in a verified isolated worktree after the
same fixture safety inspection.

```powershell
dotnet build .\Kora.slnx --configuration Release --no-restore -warnaserror
# Only if missing assets/dependencies are reported:
dotnet restore .\Kora.slnx --locked-mode
dotnet build .\Kora.slnx --configuration Release --no-restore -warnaserror

$Receipt = Get-Content .\tests\evidence\unattended-20261010\production-suites.json -Raw | ConvertFrom-Json
$WindowsFilter = ($Receipt.safetyExclusions | ForEach-Object { "FullyQualifiedName!=$($_.selector)" }) -join '&'
foreach ($Suite in $Receipt.suites) {
    $Arguments = @(
        'test', '--project', ".\tests\$($Suite.suite)\$($Suite.suite).csproj",
        '--configuration', 'Release', '--no-build', '--report-trx',
        '--report-trx-filename', "$($Suite.artifact).trx",
        '--results-directory', (Join-Path $ProofRoot $Suite.artifact),
        '--timeout', '15m'
    )
    if ($Suite.artifact -eq 'windows') { $Arguments += @('--filter', $WindowsFilter) }
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Suite failed: $($Suite.suite)" }
}
```

The first four suites were run concurrently without filters, then Windows was
run with the exact safety filter. Each suite had a 15-minute runner deadline.
An initial help-only invocation with `-- --help` exposed an SDK help-transport
error; `--help` without the separator succeeded. It executed no tests and did
not require a source change. The qualification runs use the maintained MTP
command form shown above.

## Cleanup and qualification limits

After completion, verified zero remaining owned storage roots, owned continuity
files, or synthetic shared-profile roots. No temporary code or scratch files
were added. Ignored worktree-local build outputs remain available for
reproduction; external raw proof is retained. The parent owns eventual
worktree/branch removal.

This receipt qualifies the identified baseline, not subsequent main changes,
published application bytes, hardware behavior, real GUI/native desktop
behavior, installation, or live accounts/providers. It does not replace CI:
main's three required checks remain `Portable build, tests, coverage, and
package`, `Windows integration tests`, and `Build WiX MSI and custom Burn setup`,
with strict branch protection. The proof PR requests normal auto-squash merge;
no check or protection bypass is part of this qualification.
