# Maintained BUILD VERSION proof - 2026-10-10

Result: **PASS**, local unattended proof only. This receipt does not claim
protected CI success, publication, installation, native qualification or
production readiness.

## Source and isolation

- Baseline: `56df1ee136a687d8cd3e2294ca25e38f9eab75dd`.
- Assigned branch: `agents/isolated-build-version-proof-setup`.
- A distinct task worktree was verified before execution. The root checkout
  was neither edited nor built. Absolute host/user paths are deliberately
  excluded from this committed report.
- The tested source was the baseline plus changes to
  [Test-BuildVersion.ps1](../../Test-BuildVersion.ps1) and
  [Get-BuildVersion.ps1](../../Get-BuildVersion.ps1); exact tested bytes and
  raw-output hashes are recorded in [build-version.json](build-version.json).
- SDK `10.0.401`, installed .NET runtime `10.0.12`, PowerShell `7.6.6`,
  Git `2.55.0.windows.5`, and pinned GitVersion `6.8.2` were observed.
- SDK/tool/package/version policy pins, build targets and the baseline
  repository-resolved Git identity/provenance were unchanged. Only disposable
  fixture repositories received fixture-local Git identity configuration.

## Concrete defect and bounded fix

The untouched maintained suite passed, including its three compiled product
version checks. A new focused negative case then failed: a stable-tag workflow
whose named tag did not exist was accepted because the checkout had another
stable tag and belonged to main. Checkout SHA validation alone did not bind
the workflow tag to that checkout.

The resolver now requires the exact, case-sensitive workflow release tag to
be among the tags pointing at `HEAD`, before accepting main ancestry. Tests
reject both an absent tag and an existing tag on a different main revision.
They also verify conflicting stable tags and match all six rejection
diagnostics, preventing unrelated failures from satisfying negative tests.
Feature/PR checks now run with the canonical repository identity rather than
inheriting the preceding fork identity.

Main beta increment/rerun, stable version, local feature/PR proof version,
publication gating, main ancestry, checkout SHA and output-file contracts
are preserved. No shared design documentation was changed.

## Commands and measured results

The initial baseline used the existing PowerShell host to invoke the untouched
maintained script. Resumed executions used `pwsh -NoProfile -NonInteractive`.
The final invocation ran the maintained script inside a child-process command
wrapper with five owned environment sentinels:

```powershell
pwsh -NoProfile -NonInteractive -Command {
    # The external harness supplies and verifies five environment sentinels.
    & .\eng\Test-BuildVersion.ps1
}
```

The generated, dependency-free fixture uses these build/restore commands.
Restore occurs only after an actual `NETSDK1004` missing-assets failure:

```powershell
dotnet build <owned-fixture>\VersionFixture.csproj --configuration Release --nologo --no-restore --property:KoraResolveBuildVersion=true --property:KoraVersionRepositoryPath=<owned-fixture>\
dotnet restore <owned-fixture>\VersionFixture.csproj --locked-mode --nologo
```

There are no package references or pre-existing dependency lock file in this
temporary fixture; this is not an application locked-dependency proof. No
local-tool restore or machine tooling installation was needed.

| Execution | Measured result |
| --- | --- |
| Untouched baseline | Exit 0; three successful compiled product version checks; 0 build warnings/errors |
| Added missing-tag regression, before resolver fix | Exit 1: `Expected versioning to fail closed.` |
| Full maintained suite after fix | Exit 0; 34 assertions; 20 resolver calls; six diagnostic-matched expected rejections |
| Compiled metadata | Three generated binaries; nine product/file/assembly metadata assertions |
| Build preparation | Four build attempts: one expected missing-assets failure, followed by one locked-mode restore and three successful builds |
| Successful builds | 0 warnings and 0 errors |
| Environment restoration | Five of five owned sentinels restored |
| Final proof duration | 101.9113981 seconds; UTC `2026-10-09T20:48:19.8459456Z` to `2026-10-09T20:50:01.7573437Z` |
| Script hygiene | Both scoped scripts parsed without errors; `git diff --check` passed |

| Compiled case | Product version | File version | Assembly version |
| --- | --- | --- | --- |
| Local main beta | `0.1.0-beta1` | `0.1.0.0` | `0.1.0.0` |
| Stable main | `0.1.0` | `0.1.0.0` | `0.1.0.0` |
| Local feature | `0.1.0` | `0.1.0.0` | `0.1.0.0` |

Coverage includes main beta increments, generated beta-tag reruns, commits
after beta/stable tags, simulated main push authority, detached stable-tag
authority, forks, canonical feature CI and PRs, local feature binaries,
absent/wrong-source/conflicting stable tags, off-main tags, beta-tag workflow
rejection and checkout SHA mismatch. Simulated publication flags are return
values only, not real publication.

## Evidence, cleanup and limits

Raw logs remain outside Git in the parent's private session artifact area,
under `maintained-proofs/version`: `baseline-proof.log`,
`tag-binding-regression.log`, `maintained-proof.log`, and `provenance.log`.
Their SHA-256 values are in the machine-readable receipt. Raw output contains
host-specific paths and is intentionally not committed.

All three owned temporary fixture repositories were verified absent after
their `finally` cleanup; generated project source, assets and binaries were
removed with them. The final wrapper created no runner source file. The task
worktree is retained for the parent to merge and clean up.

No real tags/releases/publication, global Git configuration writes, machine
tool installs, license/consent acceptance, elevation, GUI/device operations or
installer execution occurred. No unavailable slice was needed for this proof.
The application/native/full-solution suites were outside this bounded scope.

The main protection query retained strict checks and enforced administrators:
`Portable build, tests, coverage, and package`, `Windows integration tests`,
and `Build WiX MSI and custom Burn setup`. The PR must satisfy those checks;
local fixture success is not a bypass or a claim that they have passed.
