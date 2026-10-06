# R02 distribution evidence and remaining source/native checks (D-005)

Status: historical distribution evidence plus runnable managed-source and
native-inspection checks. The NSIS acquisition, authoring, packaging and
unexecuted Linux compiler recipe have been retired in favour of the delivered
[WiX MSI/custom Burn installer](../../installer/README.md). No NSIS executable
path remains. Original receipts are unchanged; they do not validate WiX or a
later source revision.

The [canonical distribution outcomes](../../Design/Distribution_And_Updates.md#r02-distribution-outcomes-and-direction)
and [R02/R17 roadmap](../../Design/Implementation_Roadmap.md#r02-distribution-follow-up-and-r17-delivery)
own the delivery plan. Windows is the only deployed runtime; Linux build
runners and cross-publishing do not add Linux/macOS application support.
Unsigned beta/stable POC publication is approved with front-loaded, risk-based
validation. This is not production sign-off, protected-deployment acceptance,
encrypted-storage admission or an in-app updater.

## Actual versus blocked evidence

The following results were measured on 2026-10-05 against approved R01 revision
`7d5e6a352261dce48f2ca4d3048650ee13f51705` (#19), not the current installer:

| Experiment | Historical result | Boundary |
|---|---|---|
| Linux cross-publish | [CI run 37245014084](https://github.com/roryprimrose/Kora/actions/runs/37245014084), job `111561063374`, artifact `11319076296`, Ubuntu 24.04, SDK 10.0.401. | Cross-publishing, not Windows installation or runtime-only acceptance. |
| Payload inspection | 81 files, 257,367,795 bytes, 53 NuGet records; declared native files present and native PEs AMD64. [Inventory](evidence/linux-payload.json), [file hashes](evidence/linux-payload-SHA256SUMS). | Static normal imports, not delay/dynamic loading or legal clearance. |
| Runtime/native inputs | Both .NET 10 base and Windows Desktop frameworks; ONNX imports external VC++ v14 DLLs. | Supported patched runtimes and actual installed loading still require qualification. |
| Retired NSIS prototype | One unsigned setup assembled on Windows from the Linux payload. [First receipt](evidence/windows-packaged-setup.json), [separate post-rebase receipt](evidence/post-rebase-setup.json). | Never executed; not Linux NSIS execution or WiX acceptance. Repeated assembly changed final EXE hashes despite equal payload/size. |
| Managed source | Exact-revision clone, locked publish, unique staging and hash-verified no-build rerun. [Build receipt](evidence/windows-source-build.json). | Local source provenance; user-writable staging is not protected installation. |
| Synthetic checks | [Source orchestration](evidence/orchestration-tests.json) and [publish contracts](evidence/publish-tests.json). | Historical synthetic results, not new results after retirement or installed protection. |
| Licensing/resources | Historical missing project licence/OpenTK declarations and no skill catalogue/protected worker. | Current licence controls must be applied per release; historical absences cannot be assumed for new source. Future resource/worker gates remain separate. |
| Installed/protection trials | No installation, elevation or application launch authorised by that proof. | Actual ACL/token/native/resource/runtime-only evidence was not obtained. |

The historical GitHub archive digest was
`31c863411f79f5c61847d4385120f33106bdbfdcc5e5e14a0e921ae39fa801e7`.
It is not a setup hash, signature or official release record. Never relabel
these receipts with new source identity or infer final setup hashes from
filenames/input payloads.

## Retained code and removal gates

- [ManagedSource.ps1](ManagedSource.ps1), [Publish-Source.ps1](Publish-Source.ps1)
  and [Test-Proof.ps1](Test-Proof.ps1) remain until the external managed-source
  bootstrap has equivalent ownership, exact-checkout, dirty-state, locking,
  failure-retention and idempotence tests. Binary setup does not implement that
  separately required delivery option.
- [Inspect-Publish.ps1](Inspect-Publish.ps1), [Test-PublishContracts.ps1](Test-PublishContracts.ps1)
  and their [shared helpers](Common.ps1) remain until native architecture,
  declared-asset, import and runtime-metadata inspection is migrated into
  maintained release tooling. Exact file hashes alone do not replace those
  checks.
- The tampered-payload-before-packaging assertion now lives in
  [Test-InstallerPayloadContracts.ps1](../../eng/Test-InstallerPayloadContracts.ps1),
  exercised by Windows CI against [Build-Installer.ps1](../../eng/Build-Installer.ps1).
  Invalid transferred bytes/version/source/manifest must fail before tool
  restore, compilation or installer staging. The retained static proof also
  rejects altered identity, without invoking an obsolete installer.

## Reproduce without installing or launching Kora

Use PowerShell 7, Git and the reviewed .NET SDK 10.0.401. Historical CI
retrieval additionally needs authenticated `gh`; this is maintainer tooling,
not the user-facing acquisition contract. Review source before executing
restore/build. Choose fresh output names and a dedicated short managed root;
no script installs dependencies, elevates, launches Kora or acquires models.

```powershell
$proof = Join-Path $PWD 'experiments\r02-distribution-proof'
$revision = '7d5e6a352261dce48f2ca4d3048650ee13f51705'

& "$proof\Publish-Source.ps1" -Root 'C:\KoraR02\managed-reproduction' -Revision $revision
gh run download 37245014084 --name Kora-win-x64 --dir "$proof\out\ci-reproduction"
& "$proof\Inspect-Publish.ps1" -Payload "$proof\out\ci-reproduction" `
    -Revision $revision -EvidenceDirectory "$proof\out\inspection-reproduction" `
    -BuildOrigin 'Historical CI 37245014084; artifact 11319076296; Ubuntu 24.04; SDK 10.0.401'
& "$proof\Test-Proof.ps1" -OutputDirectory "$proof\out\orchestration-reproduction"
& "$proof\Test-PublishContracts.ps1" -Payload "$proof\out\ci-reproduction" `
    -Inspection "$proof\out\inspection-reproduction\payload.json" `
    -OutputDirectory "$proof\out\publish-tests-reproduction"
```

For another reviewed baseline, pass its full 40-character revision and inspect
its own files. Verify the CI source SHA and job before describing provenance.
`BuildOrigin` is descriptive operator metadata, not authenticated authority.
Do not overwrite an existing fixture root or rewrite committed evidence.

## Managed-source findings

Each revision owns a detached checkout and unique staging attempt. Origin,
HEAD, tracked/untracked edits and linked paths are checked before build/reuse
and before promotion. Windows long-path support is checkout-local; keep build
roots short enough for SDK/MSBuild. Unknown roots are never adopted and
dirty/partial checkouts are never reset, cleaned or repaired automatically.
Exclusive operator locking prevents concurrent builds of the same root.
Failures retain staging and earlier outputs; verified reruns do not rebuild.
Output promotion is not activation, installed protection or publisher trust.

Production source delivery still needs protected checkout/output/activation
identities, release/channel metadata, interrupted-install reconciliation and
actual Windows launch trials. Local/custom builds are not official release
provenance.

## Current Inspection Receipts

Fresh inspector receipts record actual `LICENSE`/`THIRD-PARTY-NOTICES.md`
presence, size and SHA-256, with null identities for absent files. Missing-file
blockers reflect that inspected revision, not the old baseline.
`releaseAcceptance` remains Blocked for unproved redistribution,
installed protection/runtime-only and resource/worker gates. Licensing-file
presence is not licence-content or complete-notice clearance.

## Required approved Windows lab evidence

Use the current [installer behaviour and limits](../../installer/README.md#package-behavior-and-limits)
and [distribution/startup acceptance](../../Design/Acceptance_Criteria.md#distribution-and-startup-gate),
not the removed NSIS copy/ACL routine. Obtain separate bounded approval for
installation, launch, elevation, startup writes or dependency operations.
Record environment, exact setup/payload hashes and observed app/worker/native
identities. Trials cover runtime-only launch, missing/wrong prerequisites,
scope/UAC/SmartScreen, repair/uninstall/logon/completion, upgrades/recovery,
user-data retention and effective deployment/native-loading protection.
Missing future resources/workers remain untested.

This is front-loaded/ad-hoc risk-based installed validation, not exhaustive
manual installation of every POC MSI. Each CI package still requires its
automated gates, including non-skipped MSI ICE. Hashes and process creation
do not grant ownership, capture consent, protected loading or storage admission.
