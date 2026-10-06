# Distribution, Startup, and Application Maintenance

Status: WiX MSI + custom Burn binary packaging, setup UI and release automation are implemented. External managed-source preview/build/staging/verification is implemented; source activation, protected deployment and installed acceptance remain open. Source bootstrap and precompiled framework-dependent binaries are required distribution options.
R02's partial NSIS 3.13 proof remains historical evidence, not the production installer. During the initial unsigned phase, update policy is automatic metadata checking with notify-only handling; Kora cannot download, stage, execute, or activate an application update.
The source host is a public source-available GitHub repository; use Linux GitHub Actions wherever feasible, with Windows jobs for WiX MSI/Burn packaging and other justified Windows-specific work.
Initial binary and setup artifacts are intentionally unsigned.
Windows remains the only supported application runtime for the foreseeable future; Linux build runners do not imply Linux releases.

Related: [MVP Scope](MVP_Scope.md), [Architecture](Architecture.md), [Application Integrity](Security_Data_Flows.md#application-integrity-and-no-self-modification), [Acceptance Criteria](Acceptance_Criteria.md).

The [WiX MSI/custom Burn installer](../installer/README.md) implements the
unsigned x64 packaging/UI slice. Local feature builds use `0.1.0`; untagged
main uses GitVersion `<major>.<minor>.<patch>-beta<increment>`, stable-tagged
main uses `<major>.<minor>.<patch>` across binaries and setup. Feature/PR CI
builds but does not upload the installer. Canonical main/tag CI publishes
application/installer artifacts and explicitly non-production POC GitHub
releases with notes, checksums and provenance; beta versions are prereleases.
The installer neither establishes protected deployment nor closes installed
acceptance, source-bootstrap, prerequisite-health, or redistribution gates.
It now detects/reuses or obtains and installs pinned required runtimes through
Burn, and offers unchecked PowerShell, Ollama/Qwen, and Kokoro preparation
using non-elevated per-user app setup services.
The installer defaults to **Just for me** and also offers **All users**.
Windows Installer's dual-purpose package redirects application/shortcut
locations to the selected user/machine context. Shared prerequisites retain
their machine-wide scope and can still require elevation; the per-user option
is not a fully elevation-free clean-machine distribution. User-writable
per-user executable locations do not satisfy the protected-deployment
candidate below or weaken any application/worker admission checks.
Successful install/repair offers a default-on **Start Kora when setup closes**
option. This requests an ordinary non-elevated launch after quiescent closure,
with candidate EXE/DLL coherence checks; it is suppressed on failure,
cancellation, uninstall, previews or required restart. Process creation is
not host, protected-loading, private-storage or authoritative-evidence admission.

## R02 Distribution Outcomes and Direction

The 2026-10-05 [distribution proof](../experiments/r02-distribution-proof/README.md)
inspected the bootstrap at approved R01 revision
`7d5e6a352261dce48f2ca4d3048650ee13f51705`. It establishes the narrow findings
below, not a completed installer, protected deployment or D-005 acceptance.
Future builds must inspect their own exact revision and final bytes.

| Finding | Design / implementation consequence |
|---|---|
| Ubuntu 24.04 CI cross-published the existing framework-dependent win-x64 bootstrap with SDK 10.0.401. | Retain normal managed cross-publishing on Linux as the build baseline. Windows is the only deployed runtime; preserve the [platform seams](Architecture.md#platform-boundaries-and-support) for future extensibility without adding other OS releases. |
| NSIS 3.13 assembled one unsigned EXE on Windows from that Linux-produced payload. Native Linux NSIS execution was unavailable. | Historical receipts are retained, but NSIS acquisition/build code and the Linux recipe are retired. WiX MSI + custom Burn now supplies binary packaging with exact payload/provenance checks; installed behavior remains R17 acceptance, not another standalone installer feasibility proof. |
| Runtime metadata requires both .NET 10 base and Windows Desktop shared frameworks, and ONNX imports external VC++ runtime DLLs. | Declare the observed [launch prerequisites](#launch-prerequisite-baseline) separately from Kora-led capability setup. Binary users do not need an SDK, Git or source checkout. |
| Exact-revision managed-source publishing and hash-verified reruns succeeded; fixtures preserve local edits and earlier outputs on failure. | Reuse dedicated detached checkout, versioned staging, explicit ownership and non-destructive reruns. Add protected deployment, interrupted-install reconciliation and actual Windows launch smoke tests before calling it a production source bootstrap. |
| Repeated setup assembly changed the final EXE digest despite unchanged payload identity and size. | Hash every finished setup, not its filename, revision or input directory alone; bind provenance and Windows results to those exact final bytes. Do not claim bit-identical builds from this experiment. |
| At the proof revision, no project licence was found and three OpenTK prerelease nuspecs lacked licence declarations. | The repository now declares PolyForm Shield 1.0.0 and a [NuGet licence/notice gate with version-specific overrides](../DEPENDENCY-LICENSES.md). Preserve the historical finding; apply the current controls to each release and separately review native/model assets and WiX build-tool terms before selection/distribution. A passed NuGet gate is not clearance for every external asset. |
| Only documentation/Avalonia resources are embedded; the planned skill catalogue, scripts and protected workers are absent. | Keep their R11/R16/R17 gates open. Shipping or testing this bootstrap does not establish resource completeness or worker protection for the designed release. |
| Windows installation/launch trials were not approved; ACL and runtime-only evidence are absent. | Allocate an approved disposable Windows 11 x64 lab. Unsigned POC publication is approved under the risk-based policy below, not production acceptance. Dependent protected-execution capabilities remain unavailable until their actual boundaries pass. |

The path forward is staged in the
[distribution follow-up roadmap](Implementation_Roadmap.md#r02-distribution-follow-up-and-r17-delivery).
Apply the current redistribution controls and establish the Windows
protected-deployment boundary; these investigations can run in parallel.
Binary MSI/Burn packaging and release metadata/provenance are implemented.
Complete the required managed-source delivery option and accept the exact
installed candidate on Windows after its resource/privacy/worker prerequisites
exist. Do not revive the retired NSIS executable path or require another
standalone packaging proof.
Do not wait for unrelated R02 provider proofs to run private packaging tests,
and do not confuse permission to prototype with permission to release.

The protected-deployment candidate is an independently administered, versioned
x64 Program Files tree, with the application running non-elevated. The
containment workstream confirmed these as assumptions to test, not guarantees:
both the deployment parent and version trees need independent ownership and
effective protection of binaries, native libraries, runtime/dependency metadata,
launch selectors and future workers. External runtime/dependency roots and
DLL resolution must not admit writable substitutions. Validate parent
delete-child rights, all token/group grants, links and replacement races,
not merely a displayed Users RX entry. The experiment's version-tree ACL
requests alone do not establish this boundary. Source/Git/build roots remain
protected resources even when staging is separate.

Production R17 work must resolve the Windows known folder and independently
protect activation authority, preserve writable user-data partitions outside
executable loading paths, and prove the application's actual non-elevated
token plus each admitted worker identity. Never launch the assistant directly
with an elevated installer's token. Unknown protection disables affected
write/execution capabilities; no unmerged containment implementation is
assumed. No install-capable updater is part of this path.

## Installation Options

| Option | User prerequisites | Build location | Intended use |
|---|---|---|---|
| Source bootstrap | Git and the pinned compatible .NET SDK, plus documented build prerequisites | Dedicated installer-managed checkout | Users who want to obtain/build from source |
| Precompiled framework-dependent release | Matching supported .NET runtime and documented native prerequisites; no Git or SDK | CI | Users who want ready-to-run compiled binaries |
| Self-contained release | No separately installed .NET runtime; documented native prerequisites still apply | CI | Optional future/additional convenience artifact |
| Developer checkout | Developer toolchain | User-owned checkout | Development; no automatic checkout mutation |

Neither required option depends on the other.
Both support installing Kora and its launch-critical code/assets without
selecting optional providers or capability dependencies. The running application
retains dependency detection and installation/configuration. An installer may
offer optional dependency assistance under the
[shared setup contract](Environment_Setup.md#optional-dependencies-and-built-in-command-only-operation);
this is an extra convenience, not a required delivery option or consent to
install a complete AI stack.
The bootstrap script is a convenience installer, not a requirement to run a binary release.
Framework-dependent means compiled/published application binaries, not source compiled on first launch.
Self-contained is distinct from Native AOT and does not automatically eliminate every native dependency.

SQLite is part of those launch-critical application assets. The managed
provider and architecture-matched standard native engine ship inside
every binary release and installer payload; they are not an installer checkbox,
Burn prerequisite package, first-run download, or separately installed SQLite
server/runtime. Source bootstrap restores the pinned build packages before
publication, but the published application never resolves SQLite from NuGet or
the machine at runtime. Packaging acceptance treats a missing, wrong-architecture
or ambient-system SQLite load as a broken release.

## Source Bootstrap

### Implemented Preview / Build-Only Interface

[Invoke-SourceBootstrap.ps1](../eng/Invoke-SourceBootstrap.ps1), interface version
**1.0.0**, is an external operator tool, not an in-app setup action or updater.
Its default `Preview` returns a reviewable plan without creating directories,
cloning, restoring, building, installing or launching. `Build` requires
`-TrustBuildCode`: installation-time trust in the reviewed bootstrap, exact
source and dependency/build code, not a grant to the running assistant.
The canonical repository is fixed to `https://github.com/roryprimrose/Kora.git`;
the public interface does not accept forks, local repositories or arbitrary URLs.
An operator must select a full lowercase 40-character commit from that source.
Branches, abbreviated revisions and automatic mutable "latest" selection are
not supported.

The only admitted channel is **local-source**. It means a local build of exact
canonical source, not an official beta/stable binary, release publication or
protected deployment. Detached source builds use the existing feature/local
version resolver (`0.1.0` at this baseline); the full commit, SDK and final-byte
hashes, not that version number alone, identify the output. Official release
channel resolution, immutable bootstrap-asset publication and protected
installation remain future D01/D02 work. Stable-tag classification never
confers activation authority.

Review the script and its complete helper set from an independently selected
immutable repository snapshot or source archive that contains this tooling.
The entry point is reviewable/downloadable source, not a standalone installer;
it needs its relative `eng` and retained distribution-helper files. Verify
acquired bytes against the selected trusted snapshot before executing.
Do not pipe a mutable URL to PowerShell, download/execute a helper on demand,
or infer publisher authentication from a self-supplied checksum. This
source delivery is not itself a published immutable bootstrap asset.

From that reviewed tooling checkout:

```powershell
$revision = 'd1fc77f8083985c5d86ed0ef3496ac68c4a150ed' # Explicit reviewed build baseline
$root = 'C:\KoraSource\managed-d1fc77f'                # New dedicated root outside any repository
.\eng\Invoke-SourceBootstrap.ps1 -Root $root -Revision $revision
.\eng\Invoke-SourceBootstrap.ps1 -Root $root -Revision $revision -Action Build -TrustBuildCode
```

Use a short owned path. PowerShell 7, Git and the exact selected revision's
`global.json` SDK are required. Preview reports executable availability and
official manual remediation links; SDK identity is checked in the detached
checkout before restore. A different roll-forward-selected SDK is rejected,
not silently accepted or installed. Framework-dependent **binary users need
neither Git nor an SDK**; their runtime/native requirements remain separate.
Clone/history and locked NuGet acquisition use network access. Restore may
read/write the normal configured per-user package cache; no machine-wide
prerequisite setup, accounts, security settings or models are changed.

### Ownership, Verification and Recovery

The [orchestrator](../eng/SourceBootstrap.Common.ps1) reuses retained R02
exact-checkout, no-links, payload inventory/hash and
[native/resource/runtime inspection](../experiments/r02-distribution-proof/Inspect-Publish.ps1)
helpers **read-only**. This is not a second installer feasibility project.
Their migration into shared maintained release tooling remains coordinated
D02 work; historical experiment receipts are unchanged.

- Only a new dedicated root or a root with a matching, versioned
  `source-owner.json` can be used. Existing repositories, nested worktree
  paths, unowned directories, filesystem roots, reparse paths and live
  Program Files/Windows destinations are refused.
- The root has an exclusive operator lock, detached `checkouts\<full-sha>`,
  fresh `staging\<full-sha>-<attempt>` and immutable `outputs\<full-sha>`.
  No checkout reset, clean, pull, branch mutation or automatic partial repair
  is performed. Local tracked/untracked edits and wrong origin/HEAD fail
  before build/reuse and are checked again before output promotion.
- Exact SDK, locked multi-RID dependency restore, separate Release win-x64
  framework-dependent build and no-build/no-restore publish use attempt-owned
  SDK artifact paths. Restore preserves both locked RIDs; it does not rewrite
  dependency locks to make a single-RID invocation pass.
- Inspection records runtime frameworks, native assets/imports, native AMD64
  architecture, embedded resource inventory and complete payload hashes.
  First-party EXE/DLL product versions and embedded Avalonia resources must
  match the local build. Licence/notice files and standard SQLite managed/native
  payloads are required; the SQLite package identity must match the selected
  dependency lock. SQLite is not an optional setup/download package.
- A **60-second bounded static PowerShell child** revalidates stage identity,
  source inputs, tooling digests, evidence and payload hashes. It never loads
  Kora assemblies/native libraries or launches the app. This is structural
  smoke verification, **not Windows runtime/loader, audio, UI or installed
  smoke acceptance**. A nonzero exit or timeout blocks promotion.
- `source-build.json` records mode, root/checkout/output paths, canonical
  source SHA, local channel, exact SDK/build configuration, source-input and
  bootstrap-helper hashes, payload/evidence hashes, smoke outcome, explicit
  operator maintenance and unavailable activation. It does not register an
  installation. Receipts are local tamper-detection/ownership records, not
  signatures or protection against a user who can rewrite them.
- Only a fully verified attempt moves to a previously absent versioned
  output. Reruns verify the entire receipt/input/tool/evidence/payload identity
  and reuse without rebuild or receipt mutation. Identity/digest mismatch,
  malformed/incomplete receipts or changed tooling are failures, never
  "already installed" success. Earlier bytes remain untouched for review.
- Restore/build/publish/inspection/smoke failures retain partial staging,
  logs and `failure.json`; a clean-source retry gets a new attempt. A failed
  clone/checkout leaves any partial checkout for manual review and refuses to
  adopt/reset it on retry. Choose a new dedicated root after reviewing such
  failures. No automated deletion, cleanup or replacement is offered.

### Remaining Installation Gates

Output promotion is **not activation**. The interface always reports
activation **Unavailable**; it never copies to a live installation, runs an
MSI/installer, elevates, uninstalls, registers start-at-login, launches Kora,
acquires models or mutates user data/previous runnable deployments.
It makes no runnable/protected-deployment claim about user-writable staging.
The separately approved D01/D03 lab must implement independent protected
activation/ownership, runtime/native loading and non-elevated launch,
registration, interrupted installation/replacement reconciliation and
data retention under the relevant parent gates. Existing WiX policy remains
unchanged: equal-numeric beta upgrades and silent old-BA related upgrades are
unsupported; retain external uninstall/reinstall guidance.

### Verification Snapshot (2026-10-06)

On an isolated branch based on `d1fc77f8083985c5d86ed0ef3496ac68c4a150ed`,
an actual canonical clone at that exact revision passed locked restore,
Release build/publish, inspection, bounded static child verification and an
unchanged-receipt no-build rerun with SDK **10.0.401**. The local `0.1.0`
framework-dependent win-x64 output contained **83 files / 257,865,286 bytes**
and **7 native AMD64 PEs**. No Kora/installer process was executed.
Its local receipt SHA-256 was
`bdb738324f433c87be5e11a7763882f6f580c603a84c3bec67ac968a22d8ca25`.
This identifies that observed scratch output only, not a release or
bit-reproducibility guarantee. The pinned NuGet licence gate passed using
current overrides/notices; static OpenTK nuspec warnings still do not clear
per-release/native/tool/asset redistribution review.

Validation also passed root Release (zero warnings/errors), all suites
(307 Core, 941 Application, 437 Windows; zero skipped), portable **100% line
and branch coverage**, existing version/release/payload/licence gates,
17 retained source-proof and 9 native publish-contract checks, 74 new
[source fixtures](../eng/Test-SourceBootstrap.ps1) and 15
[stage-inspection contracts](../eng/Test-SourceStageContracts.ps1).
Fixture builds/receipts are explicitly synthetic; they do not replace the
actual exact-revision publish above or installed/privilege/ICE lab evidence.

For fresh owned test directories **outside every source repository**:

```powershell
.\eng\Test-SourceBootstrap.ps1 -OutputDirectory 'C:\KoraSourceTests\fresh-fixtures'
.\eng\Test-SourceStageContracts.ps1 -VerifiedOutput "$root\outputs\$revision" `
    -OutputDirectory 'C:\KoraSourceTests\fresh-inspection'
```

CI ownership remains separate: these new script gates must be wired into the
appropriate job alongside CI repair, without importing sibling code or
changing release prechecks. Installed/protected/runtime-only acceptance and
immutable source-tool distribution are still open, not inferred from tests.

## Precompiled Build Artifacts

CI produces a downloadable framework-dependent publish artifact for each explicitly supported Windows architecture.
Windows x64 is the initial reference target; other architectures require their own packaging and acceptance evidence.

Include:

- Application executable, managed assemblies, runtime/dependency metadata, and required native libraries.
- Built-in skill manifests/instructions/fixtures/scripts embedded in the application binary, plus separately protected interpreter/worker runtime assets; no loose built-in skill files.
- Version, source revision, architecture, supported runtime requirements, licences, final-byte hashes, and published build provenance.
- A prominent disclosure that the initial executable and setup package are unsigned and may produce Windows Unknown Publisher or SmartScreen warnings.
- Setup/launch instructions that require neither `dotnet build` nor `dotnet restore`.

Specify the required runtime family/version from the actual published runtime configuration; do not assume the SDK or Windows Desktop runtime is needed merely because the app has a desktop UI.
The installer/launcher reports missing or wrong-architecture runtimes with actionable instructions.
It must not respond by installing an SDK or switching to a source build.

### Launch Prerequisite Baseline

For the inspected R01 win-x64 payload, `Kora.runtimeconfig.json` declares
`Microsoft.NETCore.App` **10.0.0** and `Microsoft.WindowsDesktop.App` **10.0.0**.
The Desktop dependency comes from NAudio.WinForms 3.1.0's WindowsForms framework
reference, not an assumption based on Avalonia having a desktop UI.
Use a supported, patched **.NET 10 x64 Desktop Runtime** installation providing
both frameworks. The declared minimum and the runtime patch actually selected
on the test machine are separate evidence; registry presence alone is not
launch success. Reinspect this contract after dependency changes.

The bundled ONNX native binaries import `VCRUNTIME140.dll`,
`VCRUNTIME140_1.dll`, `MSVCP140.dll` and `MSVCP140_1.dll`, absent from that
publish directory. The current candidate therefore declares the Microsoft
Visual C++ v14 x64 Redistributable as an external native prerequisite.
Select and record its supported version during Windows validation; static
import names or file-presence checks do not prove loader compatibility.
Verify delayed/dynamic loads, including OpenTK/OpenAL and optional speech,
separately. A missing optional capability must remain explicitly unavailable,
not strand the launchable shell/setup experience or trigger a hidden download.

Prerequisite failure must identify the missing family/version/architecture and
provide an official manual remediation path, with no automatic SDK install,
source-build fallback or unrelated provider/model setup. Delivery handles
pre-launch requirements; the running application cannot repair a prerequisite
required for its own process to start. Neither runtime nor native prerequisite
health has yet been proven on a clean runtime-only Windows machine.

### Payload and Deployment Responsibilities

Speech models, Ollama and other capability-specific requirements remain
detectable/configurable by Kora's built-in setup controller after launch.
Optional installer assistance may prepare explicitly consented catalogue
dependencies; the published application must not depend on that assistance.
User stores/databases remain app-owned initialisation, not installer provisioning.
Users may decline providers and retain the supported deterministic built-in
subset, subject to each command's dependencies and policy.
Disclose downloads, verify their identity/licences, and report capabilities unavailable until setup completes.
That setup must not compile application code or silently use cloud speech as a fallback.

Executable content must be protected under the deployment's actual application/worker identities.
Verify application build provenance and deployment identity through the strongest available trusted deployment/launch boundary; embedded resources are not independently editable or independently updatable.
During the unsigned phase, canonical release origin, exact source revision, final-byte hashes, build attestations, and protected installed-file permissions provide traceability and tamper detection but do not provide Authenticode publisher authentication.
Source builds record their local build trust/provenance and cannot be advertised as official release binaries.
A ZIP extracted to a user-writable directory is not by itself evidence of integrity isolation.
Establish the required protection or disable affected write/executable capabilities with an explicit explanation.

## Public GitHub and Linux-First Release Pipeline

Host source, issues, and public release assets in the configured canonical GitHub repository.
Repository owner/name and project licence are selected before publication; forks/custom builds do not become official update publishers by name alone.
Do not require end users to have a GitHub account, token, Git, or SDK to download/run official binary releases.
Public update checks use bounded unauthenticated release/manifest requests, conditional caching, and rate-limit/backoff handling.
A rate-limited or inaccessible feed is Unknown/deferred, not proof that Kora is current.

Use Linux GitHub Actions for portable build/test, Windows cross-publishing and
release metadata/publication wherever feasible. Use an explicit Windows job
for WiX MSI/Burn assembly and any justified Windows-specific build work.
Transfer the exact inspected payload between jobs with verified digests and
source/build provenance; do not silently rebuild or relabel it while packaging.
Select pinned SDK/dependency/tool versions that cross-publish the supported Windows RID, initially Windows x64.
Use normal .NET managed publishing; single-file/self-contained options require their own native-asset evidence, and Native AOT is not assumed cross-buildable.
Every Windows/native dependency needs reviewed redistributable assets or a
documented build path. Pin and record each job's OS/tool identities; keep
Windows-only work bounded rather than moving portable stages to Windows by
default. Do not use Wine or describe Windows packaging as Linux-native evidence.

### Build Versions and Publication Pre-Check

The shared resolver uses pinned GitVersion for local and CI main builds.
Untagged main produces `<major>.<minor>.<patch>-beta<increment>` and CI publishes
a beta prerelease. A stable `v<major>.<minor>.<patch>` tag on main produces that
numeric GitVersion version and CI publishes a normal release. Tagged revisions
must belong to main; conflicting tags, checkout SHA or GitVersion values fail.
Local feature and feature/PR CI versions remain `0.1.0`, without publication
authority. Numeric MSI/Burn metadata cannot encode beta ordering; repeated
betas with the same major/minor/patch are not independently upgrade-ordered.

The maintainer explicitly approved automated **unsigned/non-production POC**
publication, not production readiness. Stable GitHub channel classification
does not close installed/protection/storage or redistribution-review gates.
Portable/Windows tests, locked licence checks, exact-payload inspection and
full MSI ICE remain required per build. Manual installed validation is
front-loaded and ad hoc/risk-based, revisited when related boundaries change;
there is no exhaustive per-MSI manual laboratory gate in this POC workflow.

Before restore/build/package/upload, query the canonical GitHub Releases
record for the exact version tag:

- If that beta/stable version is already published with matching immutable
  source/provenance and complete expected artifacts, report **already published**
  and skip rebuilding/republishing it. Never overwrite/delete assets, move its
  tag or reuse the version number for different bytes.
- If a same-version record conflicts with the source, channel or expected
  artifact identities, stop for maintainer reconciliation; it is not a no-op
  success and not permission to repair a published release automatically.
- A draft is not an already published version; a prerelease must match the
  requested beta channel. Interrupted drafts and source/channel/digest conflicts
  stop for manual reconciliation; never silently promote or replace them.
- Build only after a successful lookup establishes that production publication
  has not happened. Authentication, network, rate-limit or ambiguous lookup
  failure blocks publication; do not treat it as an absent version.

Serialise production workflows by canonical repository/version and recheck
immediately before creating/publishing the release. Surface any competing
publication or API conflict without replacing assets. An interrupted draft can
be resumed only after verifying its exact revision, completed assets/digests and
remaining gates. The pre-check makes publication idempotent; it does not prove
bit-identical builds or authenticate an unsigned Windows publisher.
The POC implements this pre-check and serialized publication for its stated
non-production scope. The stronger production acceptance stages below remain
R17 work; public POC assets do not supply their evidence.

Proposed release stages:

1. Validate the protected tag/revision and version/channel, run the exact-version publication pre-check, then validate dependency locks, licences and embedded built-in resource catalogue.
2. Build and run portable unit/contract tests on Linux; simulated Windows APIs do not count as Windows integration evidence.
3. Cross-publish Windows binaries and inspect RID/runtime/native asset completeness and resource identity.
4. Assemble the WiX MSI and single user-facing Burn setup EXE in the Windows packaging job from the exact inspected payload.
5. Generate hashes, version/runtime/architecture metadata, release notes, unsigned-artifact disclosure, and source/build provenance for the final bytes.
6. Record the required Windows validation evidence for this exact candidate through an external test environment.
7. Recheck version publication and publish only the approved exact artifacts to the appropriate GitHub Release/channel without replacing an already published version.

One downloadable setup EXE does not mean one installed file or one hosted release asset.
The pipeline may also publish a standalone/portable payload, authenticated release metadata, checksums, licences/SBOM, and updater packages where required.
Portable execution retains the existing protected-deployment limitations.
Keep update discovery tied to official releases, not latest successful workflow artifacts or arbitrary default-branch commits.

### Unsigned Initial Releases and Public Contributions

Initial application binaries, maintenance binaries, and setup packages are not Authenticode-signed.
Release pages, setup instructions, and update prompts must say so plainly and must not display a verified-publisher claim.
Document the expected Windows Unknown Publisher/SmartScreen experience and provide a user-verifiable SHA-256 digest for every downloadable artifact.
Canonical GitHub release origin, protected workflow permissions, exact source revision, immutable action pins, build attestations, and final-byte hashes provide traceability; they do not authenticate the Windows publisher or protect against compromise of the release account/workflow.
Do not rely on a branch/tag name alone to identify an artifact, and do not describe a checksum hosted beside an artifact as equivalent to an independent signature.
Fork/PR checks run without release-write permissions or production identity.
Publishing is restricted to protected maintainer-approved revisions/environments; never execute fork/PR-supplied code with those privileges.
Use least-privilege workflow permissions and short-lived credentials where supported.
Code signing is a later hardening milestone and requires a separate design update, key/service protection, timestamping, verification, and acceptance evidence before any signed-publisher claims are made.

### Installed Windows Validation Separate from Packaging

Neither Linux cross-publishing nor Windows installer assembly proves installed
Windows runtime correctness. Use an approved runtime-only Windows test
machine/lab, separate from build/packaging, and attach evidence to the exact
artifact digest before marking a release ready.
Validate installation/UAC, protected ACLs, unprivileged launch, tray/microphone/lock behaviour, bundled-resource execution, native libraries, update/quiescence/recovery, and runtime-only operation.
The Windows packaging job is not a substitute for clean-machine installation,
upgrade/repair/uninstall or actual protection/launch evidence.
Production-acceptance candidates stay draft/unreleased when mandatory initial
Windows evidence is missing; passing Linux tests is not a substitute. The
explicitly approved public POC beta/stable releases above carry unsigned and
non-production disclosures rather than claiming that acceptance. Installed
validation is front-loaded and risk-based, not exhaustive manual testing of
every release MSI. Repeatable Windows-image smoke/lifecycle trials can become
Actions gates once implemented; the current Windows tests and native packaging
inspection do not install the MSI.

## Running and Logon Startup

Launch published binaries, not a build command.
After either installation option, Kora's setup offers start-at-logon registration with explicit consent and an accessible disable option.
The [WiX/Burn installer](../installer/README.md#start-at-login) presents that
choice before Install/Repair, defaulting on for an absent entry and reflecting
matching registry configuration. Registration matches the explicitly chosen
installation scope (HKCU/current user or HKLM/all users), is owned by a native
MSI component, and is recorded in the installation transaction. Windows-disabled
or conflicting configuration is surfaced rather than silently overridden.
This installer authoring does not establish installed logon, protected loading,
repair/uninstall or upgrade acceptance.
Use a stable registered launcher/package identity that survives version changes; the startup entry must not point to a transient checkout or staging directory.
The application runs as the interactive user, without elevation or a pre-logon microphone service.
Enforce a single desktop instance and avoid duplicate startup registrations.
Use [Instance Coordination](Instance_Coordination.md) across installed/developer builds: same-build launch reveals the owner; different-build launch requires approved quiescent handoff and an explicit return offer after exit.
No handoff installs code or bypasses the current maintenance restrictions.

Startup opens the shell/tray and automatically attempts listening when the
host has saved ongoing voice consent and fresh ownership, interactive-session,
permission, selected-microphone, asset and voice/call-policy checks permit it.
First launch obtains explicit consent, not recording permission from install,
device selection or logon registration.
Manual disablement lasts for the current process; a later app restart/logon again
uses the automatic startup policy. Locked, disconnected, or unknown session
states still block acquisition and require explicit recovery after unlock.
Unlock/resume and permission/device recovery in the current run never reopen
capture silently; consent withdrawal persists across restart. See the
[canonical microphone matrix](Security_Data_Flows.md#microphone-consent-and-enablement-matrix).
Uninstallation removes startup/deployment registrations and offers to retain user skills/settings.
It does not delete shared profile skills.

## Update Ownership and Deployment Mode

Application maintenance is separate from the agent's model, tools, scripts, skills, and work queue.
During the initial unsigned phase, Kora checks for updates and notifies through the host interaction broker, but has no install-capable updater.
Voice may defer, suppress reminders, show release details, or open the configured canonical release page.
It cannot approve download, staging, execution, source mutation, or activation.
The user obtains and launches a replacement outside Kora under the normal unsigned-install disclosure and OS prompts.
An install-capable updater is a future capability requiring independently authenticated signed metadata with a protected offline/root trust anchor, expiry, rollback/freeze protection, threshold/key-rotation design, exact host-owned voice/UI approval with mandatory OS checks, and separate acceptance evidence.

Do not infer update ownership merely by finding `.git`.
Use installation metadata to distinguish a managed source deployment, binary deployment, and developer checkout.
Unknown mode or repository identity disables automatic source mutation.
Protected update origins/channels cannot be changed by skill content or agent tools.

Proposed maintenance paths:

- Binary deployment: poll the configured hosted repository release feed and notify/open the canonical release page. Kora does not obtain or execute replacement artifacts during the unsigned phase.
- Managed source deployment: notify that an update exists; leave fetch/build/activation to an explicit external operator workflow during the unsigned phase.
- Developer checkout: notify only; leave branch selection, fetching, building, and local changes to the developer.

A release channel should identify tested releases; default-branch tracking is an explicit development-channel choice, not an implicit default.
Update checks have bounded frequency/timeouts, respect offline/maintenance network policy, and do not block voice controls.
Never describe a failed check as proof that the installation is current.

## Hosted Release Feed and Notify-Only Interaction

GitHub Releases is the sole current version host for both prerelease and
production versions of `roryprimrose/Kora`. Use its release API, or future
authenticated release metadata published with those releases, not arbitrary
page text, another hosting feed or a model's proposed download URL.
The canonical release page is https://github.com/roryprimrose/Kora/releases.
Select the configured channel and matching architecture/runtime; production
excludes drafts and prereleases, while explicitly selected preview discovery
can include published prereleases. Drafts remain excluded in every channel.
Prerelease discovery must use release enumeration rather than treating the
latest-production endpoint as a preview feed. CI artifacts and default-branch
builds are not released versions.
Compare published version/release identity, not commit timestamps or a local Git branch.
Validate the configured canonical repository, release identity, immutable source revision, expected artifact name, and final-byte digest against protected maintenance configuration.
During the unsigned phase this detects changed or mismatched release content but does not provide independent Windows publisher authentication; surface that limitation rather than claiming a trusted publisher.
Repository credentials, if required, remain confined to maintenance.

Proposed checking cadence is startup plus every 6 hours with jitter, bounded requests, and failure backoff.
Network-disabled/offline mode reports deferred checking and permits a later explicit check; it does not report "up to date".
Never check through an unapproved remote path under local-only mode.
Routine checks discover metadata only; no Kora-owned download or staging path exists during the unsigned phase.

The proactive prompt identifies version, unsigned status, expected manual replacement impact, and canonical release origin.
“Open the release page” is a browser-navigation proposal bound to that exact canonical URL, not installation approval.
Locked/disconnected sessions do not open it; after unlock, the user can request it again.
See [Proactive Voice Interaction](Proactive_Interaction.md).

Manual replacement instructions require the user to exit Kora and preserve skill partitions, credentials, and settings outside deployment directories.
Kora never replaces executable files underneath active work.

## Selected Windows Installer Direction

Select **WiX MSI + Burn** for production Windows delivery. MSI supplies the
standard installation, repair, upgrade and uninstall lifecycle; Burn supplies
one user-facing setup EXE and reviewed prerequisite chaining. This reduces
bespoke lifecycle scripting compared with NSIS, at the cost of more authoring
and a Windows packaging job. Optional dependencies and a branded UI alone are
not unique WiX benefits. Retain NSIS receipts as historical evidence, not a
second supported installer. Its acquisition/build path and unexecuted Linux
recipe are retired; generic managed-source/native inspection checks remain
until equivalent maintained delivery tooling supersedes them.

The delivered slice pins WiX 7.0.0 with a custom Avalonia bootstrapper, explicit
install/repair/uninstall views and a self-contained .NET runtime so the UI works
before Kora's required runtimes are present. Signing is a known constraint,
disclosed in release notes/documentation rather than a UI POC banner.
Required .NET Desktop/base and VC++ runtimes are distinct from selectable
optional capability preparation. Preserve closure, licence and notice
inspection on pin changes; the selected tools' current
[Open Source Maintenance Fee terms](https://github.com/wixtoolset/wix#open-source-maintenance-fee)
remain part of the [dependency policy](../DEPENDENCY-LICENSES.md).

Burn may offer independently consented optional capability assistance, but
must retain a Kora-only path and the app's detection/setup/refusal/re-entry
contract. Required launch prerequisites are distinct from optional Ollama,
model, speech or future Copilot dependencies. Shared/pre-existing components
are not automatically removed on Kora uninstall; ownership and data-retention
decisions must be explicit.

The implemented R17 binary slice has MSI component/product/upgrade identities
and shared version mapping; production upgrade behavior is not established.
The custom BA currently rejects silent/passive/layout operation, so related
bundle upgrades requiring silent execution of the old BA are unsupported.
Betas sharing a numeric MSI version are not independently upgrade-ordered;
do not advertise seamless upgrade or enable same-version overrides as a
shortcut. Until supported upgrade paths are implemented/validated, use an
explicitly approved external uninstall/reinstall workflow retaining user data.
Validate fresh install, upgrade, repair, interrupted/failed
operations and uninstall, with previous-version recovery, local-edit/user-data
preservation, exact-byte provenance, prerequisite handling and the independent
protected-deployment boundary. Standard MSI behavior does not by itself prove
those policies, unprivileged launch or future worker/resource acceptance.
The app remains notify-only; manual external installer execution is not a
new in-app updater or permission to bypass release/OS approval.

Velopack's integrated update machinery is not selected; future update authority
and authenticated metadata/root trust remain separately gated.
MSIX/App Installer is deferred while initial artifacts remain unsigned because its normal trusted deployment model depends on package signing.
Installer/activation mechanisms remain behind the Windows platform boundary;
this selection does not add Linux/macOS runtime support.
The bootstrap concept does not commit Kora to Chocolatey as a dependency.
Future package configuration must preserve per-release exact host-owned approval and mandatory OS checks; do not enable unattended App Installer updates that bypass that policy.
Do not implement multiple independent update authorities for the same installation.

References: [WiX documentation](https://docs.firegiant.com/wix/), [WiX build integration tutorial](https://docs.firegiant.com/wix/tutorial/), [WiX source and maintenance terms](https://github.com/wixtoolset/wix), [historical distribution receipts and retained checks](../experiments/r02-distribution-proof/README.md).
