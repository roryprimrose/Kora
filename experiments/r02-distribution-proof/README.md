# R02 distribution feasibility (D-005)

Status: **partial proof; release-blocking validation remains open**. This is not
an installer release, an updater, or R17 acceptance. All scripts, dependencies
and outputs are local to this experiment; production manifests and CI are
unchanged. Following review, the
[canonical distribution outcomes](../../Design/Distribution_And_Updates.md#r02-distribution-outcomes-and-direction)
and [R02/R17 follow-up roadmap](../../Design/Implementation_Roadmap.md#r02-distribution-follow-up-and-r17-delivery)
record the findings, owners, next actions and closure conditions. This directory
retains the runnable proof and historical evidence, not the release plan's only
source of truth.

The subsequent [production direction](../../Design/Distribution_And_Updates.md#selected-windows-installer-direction)
is **WiX MSI + Burn**, with Linux builds wherever feasible and Windows
packaging where needed. The NSIS prototype and original receipts are retained as
historical evidence, not a production installer or WiX acceptance. Source and
inspection scripts receive regression fixes without rewriting those receipts.
The unexecuted Linux NSIS recipe below is no longer a release gate; no separate
WiX feasibility project is required before R17 implementation.

Platform scope confirmed by the user: **Windows-only deployed runtime**.
GitHub builds/packaging should use Linux as far as feasible; that is build
infrastructure, not Linux application support. Preserve portable domain/
application boundaries and OS-specific composition for future extensibility,
without shipping or promising Linux/macOS runtime artifacts. This experiment
does not add another OS backend or change that architecture.

Read together with the [distribution](../../Design/Distribution_And_Updates.md),
[setup](../../Design/Environment_Setup.md),
[integrity](../../Design/Security_Data_Flows.md#application-integrity-and-no-self-modification)
and [acceptance](../../Design/Acceptance_Criteria.md#distribution-and-startup-gate)
contracts. Approved R01 is commit
`7d5e6a352261dce48f2ca4d3048650ee13f51705` (#19), verified as an ancestor after
the initial fetch/rebase. Policy approval is not implemented enforcement.

## Actual versus blocked evidence

Evidence recorded on 2026-10-05:

| Experiment | Actual result | Boundary / missing evidence |
|---|---|---|
| Existing Linux cross-publish | [CI run 37245014084](https://github.com/roryprimrose/Kora/actions/runs/37245014084), portable job `111561063374`, succeeded on Ubuntu 24.04, runner image `20260927.320.1`, SDK 10.0.401 at the exact R01 revision. Downloaded artifact `11319076296` (`Kora-win-x64`) and inspected the bytes. | Existing CI uploads a directory, not an NSIS setup or approved release. Its separate Windows integration job is not runtime-only installer acceptance. |
| Framework-dependent inspection | 81 files, 257,367,795 bytes, 53 NuGet package records; launch-critical metadata and declared native files present; native PEs are AMD64. [Payload inventory](evidence/linux-payload.json) and [file hashes](evidence/linux-payload-SHA256SUMS). | Static inspection cannot prove dynamic/delay-loaded dependencies, hardware behavior or licence clearance. Includes large native PDBs and ONNX `.lib` files; no stripping/change of production publish outputs here. |
| Runtime family | Actual `Kora.runtimeconfig.json` requires **both** `Microsoft.NETCore.App` **10.0.0** and `Microsoft.WindowsDesktop.App` **10.0.0**, x64. | Do not recommend the base runtime alone. NAudio.WinForms 3.1.0 declares the WindowsForms shared-framework reference. Require supported patched .NET 10 x64 Desktop Runtime; no SDK/Git/build on binary launch. |
| Other native prerequisites | ONNX imports `VCRUNTIME140.dll`, `VCRUNTIME140_1.dll`, `MSVCP140.dll`, `MSVCP140_1.dll`; these are not in the publish directory. | Declare Microsoft Visual C++ v14 x64 Redistributable and validate its supported version in the lab. File presence checks are not a loader/health test. OpenTK/OpenAL and optional speech dynamic loading still need actual trials. |
| NSIS first prototype | Portable NSIS 3.13 assembled **one unsigned setup EXE on Windows** from the Linux-produced payload. [Setup receipt](evidence/windows-packaged-setup.json) records final size/hash, compiler/script identity and OS. First-party PEs/setup have no Authenticode certificate table. | **Not Linux packaging proof.** Third-party native assets retain their existing certificate tables; no claim that every vendor DLL is unsigned. Installer was not executed. |
| Managed source | Real clean clone of the exact R01 revision, locked restore/publish with SDK 10.0.401, static inspection, versioned output promotion and hash-verified no-build rerun succeeded on Windows. | Local build provenance, not official release provenance. User-writable output is staging, not an integrity boundary or installed runtime evidence. |
| Failure/idempotence fixtures | [Orchestration smoke results](evidence/orchestration-tests.json) cover dedicated ownership, exact revision, dirty checkout preservation, rerun, wrong origin, missing revision, failure/empty output, old-output retention, tamper and concurrent operator rejection. [Publish smoke results](evidence/publish-tests.json) cover observed frameworks/imports, absent runtimeconfig, wrong native architecture and tamper before packaging. | Synthetic/static results, not Windows ACL/worker/application acceptance. |
| Linux NSIS execution | **Blocked**: no installed WSL, Docker or approved external Linux shell here. Native build recipe and hash-pinned tool inputs below. | No Wine, required Windows build job, or Windows compilation substituted for this gate. |
| Protected deployment / runtime-only Windows | **Blocked by explicit user choice**: build/inspect only; do not install or launch Kora on this host. | No UAC, effective ACL, unprivileged launch, fresh data, tray, microphone, lock, SmartScreen or runtime-only machine result is claimed. |
| Redistribution | **Blocked**: no project licence found (GitHub licence endpoint also returns 404); OpenTK 5.0.0-pre.13 packages have no licence declaration in their nuspecs. | Review actual project/native/font/embedded-text licences and preserve notices. NuGet declarations and content hashes are inventory, not legal approval. |
| Future bundled resources | Kora.Application embeds 11 documentation pages; Kora embeds Avalonia resources. No implemented bundled skill catalogue, protected interpreter or execution worker exists in this baseline. | Do not count documentation as embedded lock-script/manifests or claim worker/resource acceptance. These require later R11/R16/R17 proof. |

The GitHub artifact archive digest was
`31c863411f79f5c61847d4385120f33106bdbfdcc5e5e14a0e921ae39fa801e7`.
That API-reported archive digest is **not** the final setup digest, an independent
signature, a build attestation, or an official release record. Download used
maintainer tooling (`gh`); token-free public release acquisition is not proven.
Committed receipts retain measured historical artifacts when later main
revisions are validated; never relabel old bytes with a new source revision.
The post-rebase assembly is recorded separately in the
[post-rebase setup receipt](evidence/post-rebase-setup.json). It has the same
payload and size but a different final EXE digest from the earlier assembly.
Do not infer a setup digest from its payload identity or filename: hash each
finished EXE, including on repeated builds on the same host.

## Reproduce without installing or launching Kora

Use PowerShell 7. Required commands are Git, Windows `curl.exe`, the reviewed
.NET SDK 10.0.401, and, for retrieving historical evidence, authenticated
`gh`. Restore/build executes code: review the exact source and this script
before invoking it.
Nothing silently installs prerequisites, elevates, launches Kora, or downloads
Ollama/models. Choose **new** output names on reruns; packaging does not overwrite.

From the repository root:

```powershell
$proof = Join-Path $PWD 'experiments\r02-distribution-proof'
$revision = '7d5e6a352261dce48f2ca4d3048650ee13f51705'
$managedRoot = 'C:\KoraR02\managed-reproduction'

# External operator source build. A second identical invocation verifies/reuses.
& "$proof\Publish-Source.ps1" -Root $managedRoot -Revision $revision

# Reacquire the historical Linux publish, not an official public release.
gh run download 37245014084 --name Kora-win-x64 --dir "$proof\out\ci-reproduction"
& "$proof\Inspect-Publish.ps1" -Payload "$proof\out\ci-reproduction" `
    -Revision $revision -EvidenceDirectory "$proof\out\inspection-reproduction" `
    -BuildOrigin 'GitHub CI 37245014084; job 111561063374; artifact 11319076296; Ubuntu 24.04; SDK 10.0.401'

& "$proof\Get-Nsis.ps1" -Archive windows
& "$proof\Build-Setup.ps1" -Payload "$proof\out\ci-reproduction" `
    -Inspection "$proof\out\inspection-reproduction\payload.json" `
    -MakeNsis "$proof\tools\nsis-3.13\makensis.exe" `
    -OutputDirectory "$proof\out\setup-reproduction"

& "$proof\Test-Proof.ps1" -OutputDirectory "$proof\out\orchestration-reproduction"
& "$proof\Test-PublishContracts.ps1" -Payload "$proof\out\ci-reproduction" `
    -Inspection "$proof\out\inspection-reproduction\payload.json" `
    -MakeNsis "$proof\tools\nsis-3.13\makensis.exe" `
    -OutputDirectory "$proof\out\publish-tests-reproduction"
```

For another reviewed baseline, pass its **full 40-character commit**, not `main`,
a mutable tag, or an inferred latest artifact. Verify the CI run's source SHA
and successful publish job before recording that run as its build origin.
`BuildOrigin` is operator-supplied descriptive metadata, not independently
authenticated provenance. Each setup receipt hashes the exact final EXE, payload
manifest, NSIS executable and installer script. Reproducible operations do not
promise bit-identical builds across different toolchains/OSes.

Use a new, dedicated, deliberately short managed root. Git long-path support is
enabled in the detached checkout, but the Windows SDK/MSBuild toolchain used by
the historical revision can still fail when its generated intermediate paths
are nested beneath this already-deep worktree. The root is never cleaned or
repaired automatically; retain failed roots for review and choose a new path.

## Linux packaging recipe (not executed here)

Use an approved Linux shell with PowerShell 7, pinned SDK 10.0.401, Git, Python 3,
a native C/C++ compiler and zlib development headers already provisioned by the
operator. Record their exact versions/image in the resulting evidence. Missing
compiler/headers block this trial; do not install global tools or use Wine.

Run `Publish-Source.ps1` on that Linux machine for the chosen exact revision.
Then `Get-Nsis.ps1 -Archive windows` and `Get-Nsis.ps1 -Archive source` verify the
hash-pinned archives in [tools.lock.json](tools.lock.json). NSIS's documented
POSIX `install-compiler` path builds native `makensis` against the release's
prebuilt Windows installer stubs/plugins; it does not execute those EXEs.
See the pinned source archive's `INSTALL`, special POSIX case.

In the Linux shell, from this experiment directory:

```sh
python3 -m venv tools/nsis-build
tools/nsis-build/bin/python -m pip install --require-hashes --only-binary=:all: -r linux-build-requirements.txt
tar -xjf tools/nsis-3.13-src.tar.bz2 -C tools
prefix="$(pwd)/tools/nsis-3.13"
(
  cd tools/nsis-3.13-src
  ../nsis-build/bin/scons SKIPSTUBS=all SKIPPLUGINS=all SKIPUTILS=all SKIPMISC=all \
    NSIS_CONFIG_CONST_DATA_PATH=no PREFIX="$prefix" PREFIX_BIN="$prefix" \
    PREFIX_DATA="$prefix" PREFIX_CONF="$prefix" install-compiler
)
export NSISDIR="$prefix"
```

Invoke `Build-Setup.ps1` with the resulting **native** `makensis`, the Linux
managed-source `payload` and its `inspection/payload.json`. The script uses
POSIX NSIS switches on Linux and records the actual packaging OS. Repeat static
tests and attach the resulting final setup digest. Do not mark Linux packaging
successful until those commands actually run. Keep SCons in the local venv;
its wheel is hash-pinned in [linux-build-requirements.txt](linux-build-requirements.txt).

## Managed-source findings

- Each revision gets a dedicated detached checkout and unique staging attempt.
  No arbitrary developer checkout is adopted, pulled, reset, cleaned or switched.
- Managed checkouts explicitly retain Git for Windows long-path support without
  requiring a machine-global Git setting. Build roots must still remain short
  enough for the selected SDK/MSBuild toolchain.
- Root metadata records source identity and mode; `.git` alone is insufficient.
  Origin, exact HEAD, tracked edits, untracked files and linked paths are checked
  before build/reuse and again immediately before promoting new output.
  Post-build identity or source changes fail without promotion; staging,
  modified checkout state and previous deployments remain for operator review.
  Rerunning never repairs or overwrites a dirty/partial checkout automatically.
- Exclusive operator locking prevents overlapping builds of one managed root.
  Failure keeps staging for diagnosis and all previous versioned outputs.
  Promotion follows static checks/hashes; it is **not activation or installation**.
- Ignored build outputs are not treated as tracked source edits. Hashes detect
  changed published files; ignored-file tampering and compromised build caches
  remain installation-time trust concerns. Metadata is not an authentication root.
- A production bootstrap still needs Windows launch smoke tests **before**
  offering a runnable deployment, protected checkout/output identities, installer
  metadata, channel/release resolution and interrupted-install reconciliation.
  This external operator prototype deliberately accepts exact revisions only.
  Source builds must remain explicitly local/custom, never called official.

## Current Inspection Receipts

Fresh `Inspect-Publish.ps1` receipts record `licensingEvidence` for `LICENSE`
and `THIRD-PARTY-NOTICES.md`: observed presence, size and SHA-256, or null
identities when absent. `releaseBlockers` reports missing licensing files only
when the inspected payload actually lacks them. Presence and hashes are not
licence-content validation, complete notices review or redistribution clearance.

Static inspection always leaves per-release redistribution, installed Windows
protection/runtime-only trials and bundled-resource/worker acceptance unproved.
`releaseAcceptance` remains Blocked and summarizes those outstanding gates; it
does not assume every newly inspected revision has the old baseline's missing
project licence or absent resources.

The historical committed R01 receipts and their missing-licence observations
remain unchanged. New fixture runs separately cover all four licence/notice
presence combinations and post-build HEAD/origin/tracked/untracked changes;
they do not relabel historical results or authorize installation/launch.

## Protected deployment and containment coordination

The NSIS prototype is lab-only: admin-requesting x86 Unicode stub containing the
win-x64 payload, fixed separate `%ProgramFiles%\Kora-R02-Proof\<identity>`,
refuses an existing version directory and silent mode, no shortcut/registry/
startup/uninstaller/updater/auto-launch. It discloses Unknown Publisher/
SmartScreen; do not disable OS protections to run it. It checks both runtime
families and the observed VC++ DLL names with actionable manual remediation.

The proposed ACL requests are SYSTEM/Administrators full control, Users RX,
no inherited writable entries, owner Administrators on the version tree.
**These requests have not been exercised or proven.** Parent ownership/DACL,
effective standard/split-token rights, reparse/hard-link aliases, replacement
races, worker identity, registered source/repository roots and all executable
search paths require real tests. This script is not a race-resistant privileged
deployment boundary; do not run it outside a disposable lab.

Sent the containment workstream the Program Files/admin-owned/unprivileged-app
assumptions and requested its worker identity and alias/race requirements.
No unmerged containment code is consumed. Until independent containment and
protected-root enforcement pass, no worker/executable capability may be enabled.
An ACL fixture alone would not prove the complete application gate.

## Required approved Windows lab evidence and recommendation

Before executing anything, obtain approval for a disposable Windows 11 x64 lab
and record CPU, OS build, RAM, SSD, device/power profile and exact runtime/native
versions. Use synthetic data and a fresh profile; no existing installation,
speech model, Ollama, database or startup entry. Bind **every result** to the
final setup/payload hashes and actual app/worker identities.

1. On a machine with no SDK, Git, build tooling or checkout, test missing runtime,
   base-only runtime, wrong architecture and wrong major: actionable failure,
   no source-build/SDK fallback. Then install supported Desktop Runtime and VC++
   prerequisites out-of-band and repeat.
2. Observe UAC, unsigned Unknown Publisher and SmartScreen with normal OS
   protection enabled. Refusing/cancelling must not claim installation success.
3. Install only into the separate lab proof root. Verify owner/DACL/effective
   denial of write/delete/rename/replacement for the unprivileged app and each
   admitted worker. Exercise source/Git metadata, ancestors, hard links,
   reparse targets, target replacement and remote repository identity bypasses.
4. Verify every installed payload hash externally before launch. Launch the
   published EXE unprivileged with no build command. Check fresh setup/tray,
   storage/SQLite and unavailable optional capabilities, native loading and
   microphone consent/lifecycle. Current automatic-listening bootstrap behavior
   is not R01 consent enforcement; coordinate R03 rather than claiming it passes.
5. Resource/script/worker tests remain blocked until those components exist.
   Do not substitute embedded documentation or a fake worker. Never invoke
   real lock/power actions on a shared machine.
6. Check existing installations/startup registrations are untouched. No updater
   or logon registration is implemented here; future R18 is notify-only.
   Dispose/reset the approved lab, not a user's installation.

Current recommendation: retain this **NSIS proof as historical evidence** and
implement the selected WiX MSI + Burn direction under R17. Linux remains
preferred for portable build/cross-publish/release stages; allow Windows
packaging. Validate the WiX lifecycle, tooling/asset terms, exact-byte provenance,
protection and runtime-only Windows launch before release acceptance. Do not
use MSIX/App Installer in the unsigned phase or add an in-app updater.
Once the WiX pipeline has equivalent source-identity, payload-inspection and
packaging-contract coverage, migrate those generic checks and remove the
NSIS-specific acquisition/build path while preserving reviewed receipts.
The proof/design may merge without closing D-005; actual release candidates
remain draft/unreleased while their implementation and acceptance gates are open.
