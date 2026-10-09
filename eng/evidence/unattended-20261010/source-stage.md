# Unattended exact-revision source bootstrap / stage qualification

Date: 2026-10-10. Outcome: **build-only and static qualification passed**.
Installed, runtime/native-loader, protected-deployment and release acceptance
remain **blocked / unqualified**. No production code changes were required.

## Identity and isolation

- Reviewed tooling and application source:
  `56df1ee136a687d8cd3e2294ca25e38f9eab75dd`.
- Canonical origin: `https://github.com/roryprimrose/Kora.git`.
- Assigned worktree: `unattended-source-bootstrap-validation`; branch:
  `agents/unattended-source-bootstrap-validation`. Git confirmed its distinct
  worktree location and clean baseline before qualification. The root checkout
  was not edited or built.
- Actual application source was a new canonical clone under the owned
  `stage-20261010-ce76541d` source root, **outside all repository worktrees**.
  HEAD was the exact revision above, detached, with no tracked or untracked
  changes after bootstrap and after both RID checks. Raw receipts record the
  actual paths, origin, HEAD, branch and complete porcelain status.
- SDK **10.0.401**, installed base/Windows Desktop runtime **10.0.12**,
  PowerShell **7.6.6**. Published runtime metadata requires the two shared
  frameworks at minimum **10.0.0**; observed installed runtimes were not loaded
  for application acceptance.
- Public interface **1.1.0**, inspector schema **2**, inspector **1.0.0**,
  profile `windows-framework-dependent-v1`. The source receipt binds **202**
  source-input hashes, all **eight** maintained bootstrap helper hashes and
  complete payload/evidence hashes.

The reviewed requirements are in
[Distribution and Updates](../../../Design/Distribution_And_Updates.md).
Execution used the maintained
[bootstrap entry point](../../Invoke-SourceBootstrap.ps1),
[orchestrator](../../SourceBootstrap.Common.ps1),
[static verifier](../../Test-SourceStage.ps1) and
[stage contracts](../../Test-SourceStageContracts.ps1).
No experiment code supplied source-delivery authority.

## Observed results

| Check | Result |
|---|---|
| Read-only public Preview | Passed; proposed source root remained absent. |
| Fresh initial no-restore build | Exit 1, exclusively diagnosed missing `project.assets.json` / `NETSDK1004`; locked restore was then permitted. |
| Separate synthetic bootstrap child | **82/82** contracts; fixture-only, not actual application evidence. |
| Actual public canonical Build with `-TrustBuildCode` | Locked restore, Release framework-dependent win-x64 build and exactly one no-build/no-restore publish passed. Build: **0 warnings, 0 errors**. |
| Bootstrap structural smoke | Passed bounded **60-second** static PowerShell child; application launch recorded false. |
| Explicit verifier against promoted actual output | Passed. |
| Exact no-build bootstrap rerun | `reused`; receipt SHA-256 **and last-write timestamp unchanged**. |
| Actual-output stage contracts | **18/18**, using owned copies of the verified output. |
| win-x64 publish contracts | **27/27**, against that same fresh bootstrap payload. |
| Fresh separate win-x86 artifacts | Initial no-restore failed only for missing assets; locked multi-RID restore, Release build and exactly one no-build/no-restore publish passed. Build: **0 warnings, 0 errors**. |
| win-x86 inspection / publish contracts | Passed schema-2 inspection and **27/27** contracts. |
| Final integrity | Promoted x64 stage revalidated after x86 work; canonical source remained exact, detached and clean. |

Total: **154 contract checks**, zero failed. Synthetic contracts and actual
source qualification ran in different bounded, noninteractive PowerShell
children (300-second and 1,800-second deadlines); both exited zero without
timeout. The bootstrap-owned smoke child was separately bounded to 60 seconds.
The actual child ran from 07:46:29 to 07:52:04 +11:00. There was **one fresh
publish per RID**, not a second standalone x64 publish.

| Actual final payload | Files | Bytes | Native PEs | Declared native assets |
|---|---:|---:|---:|---:|
| win-x64 | 87 | 260,990,934 | 7 AMD64 | 10 |
| win-x86 | 83 | 244,140,855 | 5 I386 | 6 |

Both inspections retained licence/notice presence, embedded Avalonia resources,
both .NET 10 shared frameworks and
standard `SQLitePCLRaw.lib.e_sqlite3/2.1.12` native bytes. The x64 stage also
verified first-party `0.1.0` versions and passed selected-checkout SQLite lock
parity. Contract mutations refused
identity/schema/profile/version/resource mismatches, missing launch-critical
files, unpinned SQLite, wrong target/native RID or PE architecture, tampered
transferred bytes, links, evidence overwrite and false release acceptance.

## Executed command sequence

Paths below are sanitized bindings to the actual isolated worktree, new source
root, initial artifact tree, fresh x86 artifact tree and owned contract-copy
trees. Exact arguments and local paths remain in the raw child/command receipts.
Outer script invocations used `pwsh -NoLogo -NoProfile -NonInteractive -File`.

```powershell
$revision = '56df1ee136a687d8cd3e2294ca25e38f9eab75dd'
# Separate synthetic child:
.\eng\Test-SourceBootstrap.ps1 -OutputDirectory $SyntheticFixtures

# Separate actual-source child, initially in the assigned worktree:
.\eng\Invoke-SourceBootstrap.ps1 -Root $ManagedRoot -Revision $revision
dotnet build .\src\Kora\Kora.csproj --configuration Release --runtime win-x64 `
    --artifacts-path $InitialArtifacts --no-restore --self-contained false `
    --property:Version=0.1.0 --property:ContinuousIntegrationBuild=true
.\eng\Invoke-SourceBootstrap.ps1 -Root $ManagedRoot -Revision $revision `
    -Action Build -TrustBuildCode
.\eng\Invoke-SourceBootstrap.ps1 -Root $ManagedRoot -Revision $revision `
    -Action Build -TrustBuildCode
.\eng\Test-SourceStage.ps1 -Stage $VerifiedOutput -Root $ManagedRoot `
    -Checkout $CanonicalCheckout -Repository 'https://github.com/roryprimrose/Kora.git' `
    -Revision $revision -Deployment $VerifiedOutput
.\eng\Test-SourceStageContracts.ps1 -VerifiedOutput $VerifiedOutput `
    -OutputDirectory $StageContracts
.\eng\Test-PublishContracts.ps1 -Payload $X64Payload -Inspection $X64Inspection `
    -OutputDirectory $X64Contracts -Rid win-x64

# In the same exact detached canonical checkout, with fresh x86 artifacts:
dotnet build .\src\Kora\Kora.csproj --configuration Release --runtime win-x86 `
    --artifacts-path $X86Artifacts --property:Version=0.1.0 `
    --property:ContinuousIntegrationBuild=true --self-contained false --no-restore
dotnet restore .\src\Kora\Kora.csproj --locked-mode `
    --property:ArtifactsPath=$X86Artifacts --property:Version=0.1.0
dotnet build .\src\Kora\Kora.csproj --configuration Release --runtime win-x86 `
    --artifacts-path $X86Artifacts --property:Version=0.1.0 `
    --property:ContinuousIntegrationBuild=true --self-contained false --no-restore
dotnet publish .\src\Kora\Kora.csproj --configuration Release --runtime win-x86 `
    --artifacts-path $X86Artifacts --property:Version=0.1.0 `
    --property:ContinuousIntegrationBuild=true --self-contained false --no-restore `
    --no-build --output $X86Payload
& $MaintainedInspector -Payload $X86Payload -Revision $revision `
    -EvidenceDirectory $X86InspectionDirectory -Rid win-x86 `
    -BuildOrigin 'Exact canonical detached source; operator local-source build; SDK 10.0.401; static-only qualification.'
& $MaintainedPublishContracts -Payload $X86Payload -Inspection $X86Inspection `
    -OutputDirectory $X86Contracts -Rid win-x86
```

The public x64 build internally used the maintained exact-SDK check, locked
restore without narrowing the locked RID set, attempt-owned artifact paths,
Release build with `--no-restore`, and publish with `--no-build --no-restore`.
No guard, origin, approved revision, receipt or trust acknowledgement was
replaced or simulated in the actual-source run.

## Evidence and cleanup

The [sanitized receipt](source-stage.json) contains counts, identities and
retained raw-evidence hashes. Raw evidence lives in the coordinating session's
`files\maintained-proofs\source-stage`: **123 read-only files**, including
stdout/stderr, build/restore/publish/smoke logs, original receipts, inspection
inventories, contract receipts, source status, child deadlines/exit codes and
explicit cleanup results. Its checksum manifest binds the other **122** files.
These are local tamper-detection records, not publisher signatures.

The actual source-build receipt SHA-256 is
`2550e55b57fd8102316605011d4ad968d6cc9a57764d8a1afb1cf2a020069e86`.
This identifies the observed final local output only; it is not official release
provenance or a bit-reproducibility guarantee.

After copying receipts/logs, the following exact owned temporary trees were
removed and their absence verified: `stage-20261010-ce76541d`,
`contracts-20261010-ce76541d`, `initial-20261010-ce76541d`,
`x86-20261010-ce76541d`, and `mutations-20261010-ce76541d`.
This removed the canonical clone, all fixture clones/code, SDK intermediates,
published binaries and mutation copies. Both temporary harness scripts were
also removed after recording their executed hashes. Shared package caches,
other worktrees and the assigned task worktree were retained; the parent owns
final merge/worktree cleanup. No generated binaries or absolute user paths
are included in this deliverable.

## Explicit limits

- **Build-only local-source**; `TrustBuildCode` remains required, canonical
  origin and full revision remain exact, activation remains **Unavailable**.
- No application/installer execution, native-library loading, activation,
  installation, account/credential setup, UI, audio/device use, elevation or
  machine-wide configuration changes. No new consent or licence acceptance.
- Static PE/import/resource/hash checks are not Windows runtime/loader,
  installed smoke, protection/ownership/ACL or physical-device acceptance.
- x86 still has **no ONNX Runtime native payload** despite declared ONNX
  packages; its inference closure and related release acceptance remain blocked.
  x64 ONNX normal VC++ imports do not qualify delay/dynamic loading.
- Per-release redistribution clearance, bundled-resource/worker acceptance and
  protected deployment require separate evidence. OpenTK nuspec warnings remain
  metadata warnings, not licence clearance.
- No WiX assembly, runtime-only lab, broader unit suite, release publication or
  live CI receipt is claimed by this source-stage proof.
