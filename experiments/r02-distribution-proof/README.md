# R02 distribution evidence and remaining source/native checks (D-005)

Status: **archived historical distribution evidence**, not an executable
delivery contract. Maintained source/bootstrap and static native-inspection
tooling/tests now live under `eng`. The NSIS acquisition, authoring, packaging and
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

## Executable disposition after maintained equivalence

The six specific superseded executable files were retired only after maintained
equivalents and contracts passed. No experiment directory was broadly removed.
There are no remaining executable consumers of these files.

| Retired proof file | Maintained owner / equivalent |
|---|---|
| `ManagedSource.ps1` | [SourceCheckout.Common.ps1](../../eng/SourceCheckout.Common.ps1) owns exact detached origin/HEAD/dirty-state verification; [SourceBootstrap.Common.ps1](../../eng/SourceBootstrap.Common.ps1) owns locking, staging, failure retention and reruns. No duplicate managed-source orchestrator remains. |
| `Publish-Source.ps1` | Explicitly trusted [Invoke-SourceBootstrap.ps1](../../eng/Invoke-SourceBootstrap.ps1) preview/build-only interface, pinned SDK and locked dependencies; unavailable activation. |
| `Test-Proof.ps1` | [Test-SourceBootstrap.ps1](../../eng/Test-SourceBootstrap.ps1), including preserved edits/previous output and pre-promotion identity failures. |
| `Common.ps1` | [Distribution.Common.ps1](../../eng/Distribution.Common.ps1) owns path/inventory/hash helpers; [NativeInspection.Common.ps1](../../eng/NativeInspection.Common.ps1) owns PE/import/runtime/resource inspection. |
| `Inspect-Publish.ps1` | [Maintained inspector](../../eng/Inspect-Publish.ps1), schema 2 with explicit approved RID, inspection profile and current licensing/capability blockers. |
| `Test-PublishContracts.ps1` | [Maintained publish contracts](../../eng/Test-PublishContracts.ps1) and [source-stage contracts](../../eng/Test-SourceStageContracts.ps1), retaining licensing/native/runtime negatives and adding both-RID/path/SQLite/profile tests. |

The tampered-payload-before-packaging assertion remains in
[Test-InstallerPayloadContracts.ps1](../../eng/Test-InstallerPayloadContracts.ps1),
exercised by Windows CI against [Build-Installer.ps1](../../eng/Build-Installer.ps1).
Invalid transferred bytes/version/source/manifest fail before tool restore,
compilation or installer staging. Shared installer payload interfaces are
unchanged; no NSIS path is revived.

The migration ran the old **17 orchestration / 9 native-publish contracts**
before retirement, alongside stronger maintained equivalents on actual
exact-source output. See the [migration verification](../../Design/Distribution_And_Updates.md#maintained-proof-migration-verification-2026-10-07).
No unique distribution-only executable procedure remained after this
equivalence. Other experiments' runtime/Node, containment, storage/native
comparisons, speech and inference procedures are unaffected and retained.

## Historical identity and procedure preservation

The original procedure is reviewable in the
[immutable pre-migration tooling snapshot](https://github.com/roryprimrose/Kora/tree/c5dffabf8f4fa767147be06dd8b296238ea97da0/experiments/r02-distribution-proof).
Its selected **application source** was `7d5e6a352261dce48f2ca4d3048650ee13f51705`,
not the tooling snapshot SHA or current main. The historical CI source/run/job/
artifact identities above and the separate post-rebase setup receipt remain
distinct. The old inspector was x64/schema 1; new schema-2 tooling does not
retroactively qualify those bytes or reproduce an old result.

For historical review, retrieve the recorded CI artifact into a fresh location,
verify its source/job/archive identity, compare its original inventory/hashes,
and inspect the immutable procedure without overwriting committed evidence.
Historical `BuildOrigin` is descriptive operator metadata, not authenticated
authority. Keep unknown/changed source, output and receipts for review; never
rewrite old hashes to claim new reproduction. The NSIS setup was never executed
and no new NSIS execution is required or offered.

All seven tracked evidence files were checked byte-for-byte before and after
migration, with these unchanged SHA-256 values (working-tree bytes):

| Historical file | SHA-256 |
|---|---|
| [linux-payload-SHA256SUMS](evidence/linux-payload-SHA256SUMS) | `57bd6edbf5aeed03673408c4e707d36db7ca8f7ba1c64b8c4e4769d84663110b` |
| [linux-payload.json](evidence/linux-payload.json) | `342b426b10d722fd0567c889172f76676916b712fdea69a91912961156e69747` |
| [orchestration-tests.json](evidence/orchestration-tests.json) | `1886815db6da0b1f8994aa936021d3199f52f6b7e9094d14ca8704365defa6f3` |
| [post-rebase-setup.json](evidence/post-rebase-setup.json) | `af888013ffbfd2029f15fd512239614199e103bae4382e9cda1b7b629c79a2b7` |
| [publish-tests.json](evidence/publish-tests.json) | `72b367266be0b8eac53a27b5135b46c0b7a5ac25d5cacee7bebb0c178ee3bf36` |
| [windows-packaged-setup.json](evidence/windows-packaged-setup.json) | `b1e79afb12a65588e061fcef6689ad899f989999f6becbb6467778e9c0d1448c` |
| [windows-source-build.json](evidence/windows-source-build.json) | `0ad2513422193d8093a70069e6beaeabd9c83cf3cbcfaea832304d5222933b89` |

## Current maintained checks

Use the [reviewed source interface](../../Design/Distribution_And_Updates.md#source-bootstrap)
and [maintained static inspection commands](../../Design/Distribution_And_Updates.md#maintained-proof-migration-verification-2026-10-07)
for newly selected exact source/payloads. Review source and pinned dependencies
before executing build code. Use fresh dedicated roots, separate evidence and
test outputs. These tools do not install prerequisites, elevate, activate,
launch Kora/native libraries or acquire models. A current inspection is a
separate new receipt with its own source, profile and hashes.

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

## Maintained Inspection Receipts

Fresh [maintained inspector](../../eng/Inspect-Publish.ps1) receipts record actual `LICENSE`/`THIRD-PARTY-NOTICES.md`
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
