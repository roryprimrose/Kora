# Distribution, Startup, and Application Maintenance

Status: proposed. Source bootstrap and precompiled framework-dependent binaries are required distribution options.
Installer/package technology remains undecided; R02 has a partial NSIS 3.13 proof and a defined follow-up path below. During the initial unsigned phase, update policy is automatic metadata checking with notify-only handling; Kora cannot download, stage, execute, or activate an application update.
The expected source host is a public open-source GitHub repository; Linux GitHub Actions runners are the build/package/release baseline.
Initial binary and setup artifacts are intentionally unsigned.
Windows remains the only supported application runtime for the foreseeable future; Linux build runners do not imply Linux releases.

Related: [MVP Scope](MVP_Scope.md), [Architecture](Architecture.md), [Application Integrity](Security_Data_Flows.md#application-integrity-and-no-self-modification), [Acceptance Criteria](Acceptance_Criteria.md).

## R02 Distribution Outcomes and Direction

The 2026-10-05 [distribution proof](../experiments/r02-distribution-proof/README.md)
inspected the bootstrap at approved R01 revision
`7d5e6a352261dce48f2ca4d3048650ee13f51705`. It establishes the narrow findings
below, not a completed installer, protected deployment or D-005 acceptance.
Future builds must inspect their own exact revision and final bytes.

| Finding | Design / implementation consequence |
|---|---|
| Ubuntu 24.04 CI cross-published the existing framework-dependent win-x64 bootstrap with SDK 10.0.401. | Retain normal managed cross-publishing on Linux as the build baseline. Windows is the only deployed runtime; preserve the [platform seams](Architecture.md#platform-boundaries-and-support) for future extensibility without adding other OS releases. |
| NSIS 3.13 assembled one unsigned EXE on Windows from that Linux-produced payload. Native Linux NSIS execution was unavailable. | Continue with NSIS as the first packaging candidate, using its native POSIX compiler and redistributable Windows stubs/plugins. Prove Linux assembly before selecting it; a Windows compiler or Wine is not a substitute. |
| Runtime metadata requires both .NET 10 base and Windows Desktop shared frameworks, and ONNX imports external VC++ runtime DLLs. | Declare the observed [launch prerequisites](#launch-prerequisite-baseline) separately from Kora-led capability setup. Binary users do not need an SDK, Git or source checkout. |
| Exact-revision managed-source publishing and hash-verified reruns succeeded; fixtures preserve local edits and earlier outputs on failure. | Reuse dedicated detached checkout, versioned staging, explicit ownership and non-destructive reruns. Add protected deployment, interrupted-install reconciliation and actual Windows launch smoke tests before calling it a production source bootstrap. |
| Repeated setup assembly changed the final EXE digest despite unchanged payload identity and size. | Hash every finished setup, not its filename, revision or input directory alone; bind provenance and Windows results to those exact final bytes. Do not claim bit-identical builds from this experiment. |
| No project licence was found; three OpenTK prerelease nuspecs lack licence declarations. | Obtain the product owner's project-licence decision and review actual third-party redistribution terms/notices before public distribution. Missing declarations are unresolved evidence, not proof of either permission or prohibition. |
| Only documentation/Avalonia resources are embedded; the planned skill catalogue, scripts and protected workers are absent. | Keep their R11/R16/R17 gates open. Shipping or testing this bootstrap does not establish resource completeness or worker protection for the designed release. |
| Windows installation/launch trials were not approved; ACL and runtime-only evidence are absent. | Allocate an approved disposable Windows 11 x64 lab. Keep release candidates draft/unreleased and dependent execution capabilities unavailable until the actual boundaries pass. |

The path forward is staged in the
[distribution follow-up roadmap](Implementation_Roadmap.md#r02-distribution-follow-up-and-r17-delivery).
First clear redistribution decisions, prove native Linux setup assembly and
establish the Windows protected-deployment boundary; these investigations can
run in parallel. Then turn the prototype into the two required R17 delivery
options, integrate release metadata/provenance, and accept the exact installed
candidate on Windows after its resource/privacy/worker prerequisites exist.
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

## Source Bootstrap

Provide a documented, versioned script with a short invocation and a downloadable/reviewable form.
Do not require users to execute an unverified mutable script blindly.

The bootstrap:

1. Explains planned locations, prerequisites, network access, and startup registration.
2. Detects prerequisites; offers explicit setup or actionable remediation rather than silently installing/elevating.
3. Resolves an identified repository and an exact release revision using the selected channel.
4. Clones into a dedicated managed checkout, never an arbitrary existing user repository.
5. Restores/builds/publishes with the pinned SDK and dependency configuration into a versioned staging directory.
6. Verifies outputs and smoke-test results before publishing a runnable deployment.
7. Records installation mode, source identity, revision, deployment path, and maintenance ownership.
8. Launches Kora's setup experience; provider/model provisioning and optional logon registration belong to that running app.

Build scripts and dependency restore execute code and require installation-time trust; this is not a skill action.
Bootstrap reruns must be idempotent, detect prior/partial installations, and never reset or overwrite local edits.
On build/publish failure, preserve the previous runnable deployment and report the failure.

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

## Public GitHub and Linux Release Pipeline

Host source, issues, and public release assets in the configured canonical GitHub repository.
Repository owner/name and project licence are selected before publication; forks/custom builds do not become official update publishers by name alone.
Do not require end users to have a GitHub account, token, Git, or SDK to download/run official binary releases.
Public update checks use bounded unauthenticated release/manifest requests, conditional caching, and rate-limit/backoff handling.
A rate-limited or inaccessible feed is Unknown/deferred, not proof that Kora is current.

Required build/package/publish jobs run on Linux GitHub Actions.
Select pinned SDK/dependency/tool versions that cross-publish the supported Windows RID, initially Windows x64.
Use normal .NET managed publishing; single-file/self-contained options require their own native-asset evidence, and Native AOT is not assumed cross-buildable.
Every Windows/native dependency must supply redistributable prebuilt assets or a proven Linux-compatible build path.
Do not quietly add Windows-only build/compiler/MSI toolchain paths or Wine to satisfy an unproven dependency.
Raise the incompatible requirement before choosing the implementation.

Proposed release stages:

1. Validate the selected revision, version/channel, dependency locks, licences, and embedded built-in resource catalogue.
2. Build and run portable unit/contract tests on Linux; simulated Windows APIs do not count as Windows integration evidence.
3. Cross-publish Windows binaries and inspect RID/runtime/native asset completeness and resource identity.
4. Assemble a single user-facing setup EXE with a Linux-capable packaging tool.
5. Generate hashes, version/runtime/architecture metadata, release notes, unsigned-artifact disclosure, and source/build provenance for the final bytes.
6. Record the required Windows validation evidence for this exact candidate through an external test environment.
7. Publish only the approved exact artifacts to the appropriate GitHub Release/channel.

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

### Windows Validation Without Windows Build Jobs

Linux packaging does not prove Windows installation/runtime correctness.
Use a Windows test machine/lab outside the required Linux Actions pipeline and attach evidence to the exact artifact digest before marking a release ready.
Validate installation/UAC, protected ACLs, unprivileged launch, tray/microphone/lock behaviour, bundled-resource execution, native libraries, update/quiescence/recovery, and runtime-only operation.
No required Windows-hosted GitHub Actions job is introduced.
Candidate releases stay draft/unreleased when mandatory Windows evidence is missing; passing Linux tests is not a substitute.

## Running and Logon Startup

Launch published binaries, not a build command.
After either installation option, Kora's setup offers start-at-logon registration with explicit consent and an accessible disable option.
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

Use the host's configured repository/release API or authenticated release manifest, not arbitrary page text or a model's proposed download URL.
Select the configured channel and matching architecture/runtime; stable excludes drafts and prereleases.
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

## Technology Decision Still Required

The leading initial proposal remains NSIS for a single setup EXE without an install-capable maintenance coordinator; the R02 Windows-assembled prototype is evidence to continue its evaluation, not a completed technology selection.
NSIS can generate Windows installers on POSIX/Linux without Windows or Wine; it does not supply a complete release-feed/self-update framework.
The application handles notify-only release discovery; protected installation/activation remains an external user operation.

Velopack is the alternative for integrated packaging/update machinery and can build Windows packages on Linux.
Its default per-user writable setup is not sufficient for the current protected-code requirement.
Any future updater must first prove independent signed-metadata trust, actual updater rights, graceful process handling, exact-origin/digest verification, and no automatic pending-update activation.
Do not assume Linux cross-packaging also proves a selected MSI/per-machine configuration.
Its feed packages/manifests are additional release assets even when users initially download one setup EXE.

WiX is not the baseline: avoid its authoring burden and required Windows toolchain paths for this design.
MSIX/App Installer is deferred while initial artifacts remain unsigned because its normal trusted deployment model depends on package signing.
Select the implementation only after demonstrating the Linux build/package/release path and Windows protection/update gates.
Installer/activation mechanisms sit behind the platform boundary; NSIS is a Windows candidate, not a universal installer or a commitment to other platform packages.
The bootstrap concept does not commit Kora to Chocolatey as a dependency.
Future package configuration must preserve per-release exact host-owned approval and mandatory OS checks; do not enable unattended App Installer updates that bypass that policy.
Do not implement multiple independent update authorities for the same installation.

References: [NSIS portable compiler](https://nsis.sourceforge.io/Features), [Velopack cross-compiling](https://docs.velopack.io/packaging/cross-compiling), [Velopack update controls](https://docs.velopack.io/integrating/overview).
