# Unattended published-byte qualification: 2026-10-10

## Exact source and bounded result

**PASS for the maintained static published-byte profile only. Product/release
acceptance remains BLOCKED. The optional local MSI/Burn slice was unavailable.**

This is new evidence, not a replacement for any historical receipt. Both
payloads were freshly built from clean source revision
`56df1ee136a687d8cd3e2294ca25e38f9eab75dd` in a verified, separately registered
Git worktree on branch `agents/unattended-published-byte-qualification`.
The primary checkout was neither built in nor edited. No production scripts,
schemas, identity checks or licence requirements were changed.

The installed tools were .NET SDK **10.0.401**, tool-host runtime **10.0.12**,
and PowerShell **7.6.6**. The repository's feature-branch version resolver
returned **0.1.0**, product version **0.1.0**, and `Publish=false`. This is a
feature proof at an exact main baseline, not a main GitVersion beta release or
an authenticated CI/cross-publish receipt. Application informational versions
were checked statically and both were exactly `0.1.0`.

| Observed result | win-x64 | win-x86 |
|---|---:|---:|
| Final files, including transferred-payload manifest | 207 | 203 |
| Total file bytes | 264,625,314 | 247,775,421 |
| Files bound by transferred-payload manifest, excluding itself | 206 | 202 |
| PE files inspected | 73 | 71 |
| Managed PE files | 66 | 66 |
| Native PE files, including apphost | 7 | 5 |
| Declared native assets, including symbols/import libraries | 10 | 6 |
| Published dependency-package licence records | 53 | 52 |
| First-party manifest-resource names inventoried | 25 | 25 |
| Exact bundled skill/script resources checked against source | 13 | 13 |
| Maintained publish contracts passed | 27 | 27 |

That is **54 maintained publish contracts**, **26 exact resource-byte checks**
over the same 13 declared resource identities, **410 final files**, and
**144 PE inspections**. Both original payloads passed a full byte-identity
recheck after the owned negative-fixture copies were exercised. The contracts
include Windows junction rejection; wrong RID/native architecture; missing,
extra or changed framework/native/dependency data; tampering; licensing-file
presence/absence and exact/null hashes; immutable-payload protection; and
refusal to overwrite earlier evidence.

The initial solution build and both initial publishes used `--no-restore` and
failed with exit code **1**, `NETSDK1004` (missing `project.assets.json`).
The solution build reported **13 errors, 0 warnings**. Only then was a locked
solution restore performed. Restore, the subsequent Release build, and both
fresh framework-dependent publishes succeeded; the Release build reported
**0 warnings, 0 errors**. No test executables or GUI fixtures were run.

The existing dependency licence/notice gate passed using the already available
pinned `nuget-license` **4.0.18**. It reported **91 package entries**, retained
**91 licence text files** and **28 package-provided notice files**, and verified
that the committed third-party notice was current. Both final publish trees
include the CI-required licence and package-notice directories, project licence,
third-party notice, and transferred-payload manifest. Licence text/assets are
not committed with this evidence.

## Static native, runtime, resource and licence findings

Both runtime receipts require exactly `Microsoft.NETCore.App/10.0.0` and
`Microsoft.WindowsDesktop.App/10.0.0`, declare their correct dependency RID,
and contain no bundled `coreclr.dll`. The installed tool-host runtime version
does not change these framework-dependent minimum contracts.

- x64 native PE machines are all `Amd64`; x86 native PE machines are all
  `I386`. All seven first-party PE files per RID have an empty Authenticode
  certificate table. Third-party certificate-table sizes are inventories,
  not signature trust verification.
- Both payloads have declared `SQLitePCLRaw.lib.e_sqlite3/2.1.12` native bytes;
  `e_sqlite3.dll` normally imports `KERNEL32.dll`. This is the standard SQLite
  production closure, not the experimental SQLCipher profile.
- Both also contain ANGLE
  `Avalonia.Angle.Windows.Natives/2.1.27548.20260419`, HarfBuzz
  `HarfBuzzSharp.NativeAssets.Win32/8.3.1.3`, and Skia
  `SkiaSharp.NativeAssets.Win32/3.119.4`. Their complete observed normal-import
  names, architecture, certificate-table sizes and native-asset identities
  are retained in each schema-2 receipt.
- x64 declares `Microsoft.ML.OnnxRuntime/1.30.0` and contains
  `onnxruntime.dll` plus `onnxruntime_providers_shared.dll`.
  The former normally imports `VCRUNTIME140.dll`, `VCRUNTIME140_1.dll`,
  `MSVCP140.dll`, and `MSVCP140_1.dll`; the latter imports
  `VCRUNTIME140.dll`. These are observed external requirements, not successful
  DLL loading or runtime-health evidence.
- x86 declares `Microsoft.ML.OnnxRuntime.Managed/1.30.0` but has no ONNX
  Runtime native PE. Its maintained receipt explicitly records:
  `win-x86 declares ONNX Runtime packages without an ONNX Runtime native payload; inference closure is not qualified.`
  The successful static x86 tests preserve this blocker; they do not qualify
  x86 inference.
- Actual PE metadata includes `!AvaloniaResources`. All 13 bundled
  skill/script resource lengths and SHA-256 identities exactly matched their
  explicitly declared source files in each final definitions assembly.
  Every resource receipt has `invocationAvailable=false`; no embedded script
  was executed.
- Raw nuspec metadata remains incomplete for
  `OpenTK.Audio.OpenAL/5.0.0-pre.13`, `OpenTK.Core/5.0.0-pre.13`, and
  `OpenTK.Mathematics/5.0.0-pre.13` in both receipts. The inspector's three
  warnings per inspection remain visible. The passing solution gate uses the
  existing exact-version MIT overrides; it does not rewrite raw metadata,
  weaken the inspector, or establish per-release redistribution clearance.

Normal imports only were inspected. No application, setup, native library,
embedded skill, audio or control operation was run or loaded. No MSI install,
repair, uninstall, prerequisite execution, startup registration, account,
provider, credential, elevation, device, Windows-policy or runtime-install
operation occurred.

## Optional local WiX/Burn slice: UNAVAILABLE

The existing [build](../../Build-Installer.ps1) and
[inspection](../../Test-Installer.ps1) scripts and
[documented WiX terms](../../../installer/README.md#wix-terms) were read.
The projects acknowledge the maintainer's non-revenue-generating proof
selection, but that is not a new licence grant or approval for arbitrary tool
acquisition.

The local cache lacked WiX SDK **7.0.0**'s required
`tools\net472\x64\wix.exe`, SDK EULA and licence text, and the required
**7.0.0** Netfx and Util extension archives. Only the separately restored
bootstrapper API EULA was present; its SHA-256 matched the repository-pinned
identity
`3358af585772039e45d1192213838f906a478463ac3bb4da208ec0d874c2219b`.
This does not establish the complete, reviewed packaging-tool closure.

No WiX acquisition, extension installation, new EULA acceptance, installer
build or installer extraction was attempted. Neither installer script was
executed. There is **no new MSI/Burn candidate**, no ICE-validation result,
and no installed-product result in this run. The absent slice did not stop
the safe application-publish pipeline. See the
[local availability receipt](published-bytes.wix-availability.json).

## Production commands and evidence shapes

The commands follow the reviewed
[CI workflow](../../../.github/workflows/ci.yml),
[distribution inspection guidance](../../../Design/Distribution_And_Updates.md),
and [dependency licence policy](../../../DEPENDENCY-LICENSES.md).
Run from the assigned isolated repository root. `$run` below represents the
operator's fresh private output directory, outside both immutable payloads.
The recorded run uses Windows build/publish, not CI's Linux cross-publish.
The installed SDK was used; no global tool/runtime installation was done.

Initial attempts, before any restore:

```powershell
dotnet build .\Kora.slnx --configuration Release --no-restore
foreach ($rid in 'win-x64', 'win-x86') {
    dotnet publish .\src\Kora\Kora.csproj --configuration Release `
        --runtime $rid --self-contained false --no-restore `
        --property:RestoreLockedMode=true --property:Version=0.1.0 `
        --output (Join-Path $run "initial-$rid")
}
```

After the observed missing-dependency failures:

```powershell
dotnet restore .\Kora.slnx --locked-mode
dotnet build .\Kora.slnx --configuration Release --no-restore
pwsh -NoProfile -NonInteractive -File .\eng\Get-BuildVersion.ps1
foreach ($rid in 'win-x64', 'win-x86') {
    dotnet publish .\src\Kora\Kora.csproj --configuration Release `
        --runtime $rid --self-contained false --no-restore `
        --property:RestoreLockedMode=true --property:Version=0.1.0 `
        --output (Join-Path $run "Kora-$rid")
}
pwsh -NoProfile -NonInteractive -File .\eng\Test-DependencyLicenses.ps1
```

For each RID, the licence directories were copied before the transferred
manifest and inspection were written. The exact executed origin string is
retained in each payload receipt. All production-script child shells used
`-NoProfile -NonInteractive`.

```powershell
$revision = '56df1ee136a687d8cd3e2294ca25e38f9eab75dd'
$origin = 'Unattended 2026-10-10; isolated exact-revision Windows build; SDK 10.0.401; runtime 10.0.12; pwsh 7.6.6; feature proof version 0.1.0; static-only'
foreach ($rid in 'win-x64', 'win-x86') {
    $payload = Join-Path $run "Kora-$rid"
    $inspection = Join-Path $run "inspection-$rid"
    foreach ($directory in 'licenses', 'package-notices') {
        Copy-Item -LiteralPath (Join-Path '.\artifacts\license-compliance' $directory) `
            -Destination (Join-Path $payload $directory) -Recurse
    }
    pwsh -NoProfile -NonInteractive -File .\eng\Test-InstallerPayload.ps1 `
        -PayloadPath $payload -Version 0.1.0 -SourceRevision $revision -WriteManifest
    pwsh -NoProfile -NonInteractive -File .\eng\Test-InstallerPayload.ps1 `
        -PayloadPath $payload -Version 0.1.0 -SourceRevision $revision
    pwsh -NoProfile -NonInteractive -File .\eng\Test-EmbeddedSkillResources.ps1 `
        -AssemblyPath (Join-Path $payload 'Kora.Definitions.dll')
    pwsh -NoProfile -NonInteractive -File .\eng\Inspect-Publish.ps1 `
        -Payload $payload -Rid $rid -Revision $revision `
        -EvidenceDirectory $inspection -BuildOrigin $origin
    pwsh -NoProfile -NonInteractive -File .\eng\Test-PublishContracts.ps1 `
        -Payload $payload -Rid $rid -Inspection (Join-Path $inspection 'payload.json') `
        -OutputDirectory (Join-Path $run "contracts-$rid")
}
```

The resource check was executed immediately after publish, before licence
directory copies; those copies do not alter the inspected assembly. Its
object output was serialized with `ConvertTo-Json -Depth 5` in a
`pwsh -NoProfile -NonInteractive -Command` child. At the end,
`Assert-Payload` from [Distribution.Common.ps1](../../Distribution.Common.ps1)
rechecked every original final file against its complete inspection receipt.
Every native/script exit code was checked fail-closed; expected negative
rejections were counted only by the maintained contract suite.

| Receipt | Shape and binding |
|---|---|
| [Run summary](published-bytes.run.json) | New bounded run schema 1; source/tool identity, exact counts, raw-log digests and receipt digests, no machine paths. |
| [Reviewed inputs](published-bytes.inputs.json) | SHA-256 identities for 21 production/configuration inputs read in this run. |
| [x64 inspection](published-bytes.win-x64.payload.json), [x86 inspection](published-bytes.win-x86.payload.json) | Unmodified maintained schema 2, inspector `1.0.0`, profile `windows-framework-dependent-v1`; complete relative file inventory, hashes, PE/resources/imports, dependency licences and unchanged blockers. |
| [x64 sums](published-bytes.win-x64.SHA256SUMS), [x86 sums](published-bytes.win-x86.SHA256SUMS) | Exact complete maintained `SHA256SUMS` bytes; not a subset inventory. |
| [x64 contracts](published-bytes.win-x64.contracts.json), [x86 contracts](published-bytes.win-x86.contracts.json) | Unmodified maintained schema 1, all 27 case names, exact RID/revision, static-only flag and inspection SHA-256 binding. |
| [x64 resources](published-bytes.win-x64.resources.json), [x86 resources](published-bytes.win-x86.resources.json) | All 13 resource IDs, byte lengths and SHA-256 values; invocation remains unavailable. |
| [x64 transfer](published-bytes.win-x64.transfer.json), [x86 transfer](published-bytes.win-x86.transfer.json) | Unmodified transferred-payload identity shape with exact version, revision and complete file hashes excluding the manifest itself. |

Raw logs, exact original published payloads and detailed inspection/fixture
receipts are retained in the private parent-session proof output. Machine
paths, binaries, assets, licence texts and secrets are not committed. Owned
temporary contract payload copies and generated build intermediate/output
directories were removed after proof collection; no temporary helper code was
created or retained. The assigned worktree remains for the parent's coordinated
merge and cleanup.

Both maintained payload receipts still block per-release redistribution
clearance, installed Windows protection/runtime-only acceptance, and
bundled-resource/worker acceptance. x86 additionally blocks ONNX inference
closure. Nothing in this static success closes protected deployment, DLL
resolution/delay-load/dynamic-loading, installed/native loading, physical
audio/control, runtime health, production product qualification, or D-005.
