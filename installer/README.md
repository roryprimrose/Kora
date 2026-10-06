# WiX MSI and custom Burn installer

The Windows 11 x64 installer uses WiX MSI and a custom Avalonia Burn UI.
Artifacts are currently **unsigned**; signing is a known technical constraint.
The delivered packaging/UI/lifecycle implementation is not production
acceptance or complete redistribution approval. Source bootstrap, protected
activation, updates and the remaining R17 installed gates are not implemented
or established by this installer.

## Build and preview

From the repository root on Windows with the pinned .NET SDK:

```powershell
.\eng\Build-Installer.ps1
```

The script restores locked dependencies, enforces the solution licence gate,
publishes the framework-dependent application and self-contained bootstrapper,
then builds an embedded-cabinet MSI and a Burn EXE. Both use WiX **7.0.0**.
Every attempt uses a fresh `artifacts/installer/build-<id>` staging directory;
failed attempts do not overwrite earlier candidates.

The successful attempt's `output` directory contains:

- `Kora-0.1.0-win-x64.msi`;
- `Kora-Setup-0.1.0-win-x64.exe`;
- WiX debugging symbols;
- `payload-manifest.json`, with the application file hashes and build identity;
- `installer-build.json`, with final MSI/EXE hashes, source revision, dirty
  worktree indication, build origin, and the MSI ICE-validation outcome.

No package is installed by this build. To preview the UI without connecting to
Burn or enabling package operations:

```powershell
dotnet run --project .\installer\Kora.Setup\Kora.Setup.csproj -- --preview
```

Preview-only `--theme light` and `--theme dark` overrides let you inspect both
palettes without changing Windows preferences. Preview dependency statuses
are explicitly sample data; they do not probe or install on the machine.
Add `--complete` for a synthetic successful-install finish page. Closing any
preview never starts Kora or a native package operation.
Use `--mode repair` or `--mode uninstall` to inspect installed maintenance
views without registry detection or native actions. These can be combined with
`--complete` and either theme override.

On a machine whose Windows policy blocks MSI ICE validation, a local-only,
explicitly unvalidated packaging preview is available:

```powershell
.\eng\Build-Installer.ps1 -SkipMsiValidation
```

This emits a warning and records `skipped-explicitly`, not `passed`. CI rejects
that flag and requires full ICE validation. Do not change Windows policy or
claim installed acceptance from a skipped validation.

Transferred application payloads are verified before tool restore, compilation
or staging and rechecked before packaging. Windows CI runs
[payload contract tests](../eng/Test-InstallerPayloadContracts.ps1), including
tracked MSI project/authoring/lockfile presence, tampered/hidden/missing/extra
files and invalid build identity. The source directory has an explicit Git
ignore exception because Windows' case-insensitive `*.msi` rule otherwise
hides `Kora.Msi`; generated MSI binaries and `obj` output remain ignored. NSIS-only
acquisition/build code has been retired; the retained
[source/native inspection checks and receipts](../experiments/r02-distribution-proof/README.md)
do not supply installed acceptance.

## UI and branding

The custom out-of-process Burn bootstrapper uses the same Avalonia packages as
Kora. Its own .NET runtime is bundled so it can show the requirements even when
the application runtime is missing. The application remains framework-dependent.
Burn's native connection runs on the MTA entry thread; Burn creates the STA UI
thread. Standalone preview creates a separate STA thread without connecting to
Burn.

- The static executable/window/installed-product icon is the existing
  [multi-resolution Kora icon](../src/Kora/Assets/Kora.ico).
- The display version appears beneath the Kora heading and as the window-title
  suffix. Preview retains its explicit `preview` version label in both places.
- The header does not display an unsigned/POC banner. Signing remains a known
  technical constraint disclosed in documentation and release notes; this
  presentation change does not alter signing or acceptance status.
- The free-standing upright ribbon follows the source geometry and complete
  seven-colour palette in [Branding](../Design/Branding.md).
- The UI follows the current Windows **app** light/dark preference through
  Avalonia's default theme variant, including platform theme updates. It shares
  [Kora's theme resources](../src/Kora/Assets/ThemeResources.axaml), rather than
  forcing dark mode or maintaining a second palette. Text and controls use
  theme-aware resources; body/status/disclaimer text meets 4.5:1 contrast in
  both palettes.
- Colours travel along the ribbon, not through a rotating page gradient.
  One circuit takes eight seconds, independently of installation progress.
- Motion respects the Windows client-area animation preference, read when the
  window is created. There is no animation checkbox. Render-clock frame
  callbacks drive continuous travel; static/reduced-motion mode retains all
  seven colours, and callbacks stop when the control is detached.
- Text, an accessible live status region, and the progress bar convey setup
  state; animation is not an activity or progress signal.
- The initial view shows concise component/status rows. Selecting an optional
  component expands its verification, download and licence details; deselecting
  collapses them. Every row can also be expanded independently to inspect
  installed or blocked components without selecting or starting work. Blocking
  states, scope, download approval and possible elevation stay visible.
- Successful detection is required before planning changes. Clicking **Install**,
  **Repair**, or **Uninstall** is approval for that action; there is no separate
  approval checkbox. Install/repair approve the displayed scope and required/
  selected downloads and start-at-login configuration. Repair/uninstall require a known installed package.
- Cancellation is supported before planning. Close is disabled while planning
  or applying, so the UI does not abandon a running transaction or rollback.
  Optional per-user preparation has its own **Cancel optional setup** action.
- HRESULT failures are logged through Burn and shown explicitly. Restart-required
  success returns `3010`; setup does not restart Windows automatically.

The preview has no installer engine, and all package buttons remain disabled.
The maintenance-mode switch remains interactive because it only changes the
view; it never plans or applies an action.
The bootstrapper deliberately rejects `/quiet`, passive display, and layout requests
before planning. Related-bundle upgrade flows that require a silent old
bootstrapper are therefore **not supported or accepted**. Until that path is
implemented and validated, use an explicitly approved external
uninstall/reinstall workflow and retain user data; do not advertise seamless
in-place bundle upgrades.

## Package behavior and limits

The MSI targets Windows 11 x64 and is a Windows Installer 5.0 **dual-purpose
package**, defaulting to **Just for me**. Setup offers an explicit scope choice:

| Scope | Application location | Start-menu shortcut | Elevation |
|---|---|---|---|
| Just for me (default) | Current user's Windows Programs known folder, normally `%LocalAppData%\Programs\Kora\<major.minor.patch>` | Current user | Not required for Kora itself; missing/older shared prerequisites can still require it. |
| All users | `Program Files\Kora\<major.minor.patch>` | All users | Required. |

WiX authors `ALLUSERS=2`, `MSIINSTALLPERUSER=1`, and `Scope=perUserOrMachine`.
The custom BA passes the explicit choice to Burn's `Plan` scope argument;
Burn handles the MSI scope properties. Windows Installer redirects the
standard `ProgramFiles64Folder` and `ProgramMenuFolder` for the selected
context, without hard-coded profile paths, privileged per-user writes, or
custom actions. Scope and optional selections remain editable until the action
button is clicked, then the approved plan is frozen; detection or changing a
selection alone never starts package work.

Maintenance locks scope to the detected bundle/related MSI context. Unknown
or conflicting scope fails closed; changing scope requires removing the old
installation first rather than silently migrating it. Direct MSI installation
is not the accepted interactive entry point; an installed MSI with no
resolvable bundle/related-package scope is directed to the original setup or
Windows Installed apps for recovery.

### Maintenance views

Repair and uninstall hide editable installation-scope controls and show only
the detected scope as read-only information. Repair retains required-runtime,
optional-component and start-at-login choices because they can affect approved
repair work. Uninstall hides those controls and dependency retry, and explains
that Kora/startup registration are removed while user data, shared runtimes and
optional components are retained.
Explicit repair/uninstall with no detected installation shows a no-package
message rather than presenting fresh-install options.

**Switch to repair** / **Switch to uninstall** changes the view only. The newly
visible primary **Repair** / **Uninstall** button is the explicit action
approval; switching cannot apply hidden repair/startup defaults. Completed,
failed and cancelled views hide configuration and inactive action/retry
controls, retaining status, Close and eligible successful-install/repair
launch-on-close choice.

The MSI adds an advertised shortcut, uses a stable MSI upgrade family, and embeds the complete published
application and its licences/notices. Burn embeds the MSI and all bootstrapper
dependencies, including native Burn interop and the bootstrapper's runtime.

Setup never enables listening, sets up accounts/grants, or deletes user
preferences, models, databases, or logs. Windows Installer's standard
Program Files behavior is **not proof** of the independently administered,
protected deployment and activation boundary required by the product design.
Per-user executable locations are user-writable and likewise do **not**
establish that boundary; app/worker security checks are not weakened by this
packaging option.

### Start after setup

After successful install or repair, **Start Kora when setup closes** defaults
on. Uncheck it to finish without starting Kora. The request occurs only after
the UI actually closes and native/optional work is quiescent; failed/cancelled
setup, uninstall, previews and required restarts cannot launch Kora.

The original non-elevated interactive setup process starts the installed EXE
directly, without a shell, elevation or ambient executable lookup. An elevated
or non-interactive context is rejected with explicit manual-launch recovery.
The scope/version path and installed EXE/DLL must match this candidate's pinned
SHA-256 hashes. These are coherence checks, not protected-deployment, complete
native-closure or host-readiness evidence. Normal application ownership,
session, privacy, consent and storage gates remain unchanged.

Launch failures are logged and reported explicitly; Kora remains installed.
Actual installed launch/repair/uninstall/all-users and protection acceptance
remain separate from the fake launcher tests and non-launching previews.

### Start at login

The startup checkbox replaces the former explanatory panel. It defaults on
when the selected scope has no registration, and reflects a matching existing
registration. Read-only inspection uses the 64-bit `Run` and `StartupApproved`
keys; scope changes reload the corresponding snapshot. A Windows-disabled
entry defaults off and is not forcibly re-enabled: use Windows Startup apps
and recheck. Foreign commands, unsupported types or unknown metadata block
install/repair rather than silently overwrite an entry. Inspection is repeated
before native planning.

Clicking Install/Repair approves the displayed choice. The MSI owns the
`Kora` value at `SOFTWARE\Microsoft\Windows\CurrentVersion\Run`, with `HKMU`
resolving to HKCU for Just for me or HKLM for All users. Its command is the
quoted installed `Kora.exe`, never a checkout, build/staging path or elevated
service. Windows per-user startup overrides remain untouched for machine-wide
registration. The conditional, transitive component supports repair changes
and standard MSI rollback/uninstall cleanup. Bare MSI defaults to no startup
registration unless the property is explicitly supplied.

Startup registration does not grant voice consent or bypass host ownership,
session, storage or permission checks. Native component authoring/condition
checks are not installed logon/repair/uninstall/upgrade acceptance. The POC
still needs supported upgrade and stable-launcher qualification.

### Required prerequisites

Burn detects the x64 .NET 10 desktop and base runtimes using the official
NetFx searches and the VC++ installed/version values in the x64 registry.
Missing/older prerequisites are downloaded and installed **before Kora**,
after interactive consent, using `/install /quiet /norestart`.
The custom UI rejects unsupported OS/architecture before planning any
prerequisite changes; uninstall remains available for removing an existing
installation.
The MSI reads `CurrentBuildNumber` from the native 64-bit OS registry:
Windows Installer's legacy `WindowsBuild` property can report `9600` even on
Windows 11. Missing/invalid builds and pre-22000 builds reject installation,
while full removal remains available. Packaging exercises the actual native
AppSearch and condition evaluator, including threshold/unknown/architecture
cases, without invoking install actions.

| Prerequisite | Pinned package | Download size | Source and verification |
|---|---|---|---|
| .NET 10 x64 Desktop Runtime (includes base runtime) | 10.0.12 | 60,032,984 bytes | Immutable Microsoft builds URL; SHA-512 matches Microsoft's .NET 10 release metadata. |
| VC++ v14 x64 Redistributable | 14.51.36247.0 | 18,731,856 bytes | Version-specific Microsoft Visual Studio download; SHA-256 matched the Microsoft winget manifest, and the full bytes were SHA-512-pinned for Burn. |

The authoritative URLs, sizes, SHA-512 hashes, version conditions, and chain
ordering are in [Bundle.wxs](Kora.Bundle/Bundle.wxs). Both downloaded candidates
were independently acquired without execution and had valid Microsoft
Authenticode signatures. Burn checks exact payload identity when downloading;
there is no floating `aka.ms` download or trust in an installer filename.

Newer compatible runtimes are reused. Both prerequisites are permanent shared
packages: Kora uninstall/rollback does not remove them or downgrade other
applications' runtimes. Failures are vital and prevent Kora installation;
the chain never installs an SDK. Internet is needed only for missing packages.
Their payload cache strategy is `remove`: already-present runtimes do not
download solely to populate Burn's cache. Required acquisitions are verified
and executed, then their cached installers can be removed without uninstalling
the permanent shared runtimes.
Detection does not establish loader health or a clean-machine launch result.
The user-selected policy retains these machine-wide runtime installers even
for **Just for me**: if prerequisites are missing/older, Windows can still
request administrator approval. With compatible prerequisites already present,
Kora's per-user install does not require that approval. This is not a fully
elevation-free clean-machine/runtime-private distribution.

### Selectable optional components

All currently implemented, independently installable optional capabilities
are represented in setup. Only actionable preparation/verification choices
have checkboxes, which start **unchecked**; installed PowerShell appears as
a read-only, reused dependency rather than an installation choice.

Before Install/Repair is enabled, independent read-only preflight checks run:

- Burn's required-runtime states/versions are displayed as installed, missing,
  or update required, using its authoritative detection conditions.
- PowerShell reuses the existing bounded no-profile version probe, with telemetry
  and update checks disabled for that child process only.
- Ollama inspection uses only local version/model-metadata GETs, with no
  redirect/proxy, service startup, pull or inference. A stopped installed
  executable, missing model, detected pinned digest, mismatch and unknown
  endpoint are distinct results.
  Digest comparison accepts both Ollama's bare 64-character SHA-256 API value
  and the algorithm-qualified `sha256:` form, case-insensitively, but rejects
  malformed values, other algorithms and genuinely different hashes. The
  same authoritative comparison is used by preparation, readiness and inference.
- Kokoro checks only existing production-user installation records/files. It
  creates no directories and loads neither the model nor voice data. Recorded
  hashes/presence are explicitly **not** fresh content/loader validation.
- Unknown/incompatible optional state is explained and preparation is blocked
  for that component, without preventing a Kora-only install. Detection can
  be explicitly retried. Closing cancels pending checks and ignores late
  successful results; it does not approve any work.

Selected services re-probe/verify during approved preparation after native
apply. Preflight is presentation evidence, not durable readiness or permission.
Missing assets may change before approval; the displayed action approves
required/selected preparation, not an automatic overwrite of a mismatched
Ollama model. Uninstall does not wait for optional checks or prepare components.

| Selection | Preparation | User scope and consent |
|---|---|---|
| PowerShell 7 | Reuse a verified 7.4+ installation or install `Microsoft.PowerShell` via winget; re-run the no-profile version probe. | Current user; Microsoft/MIT; requires Windows Package Manager. No script execution grant. |
| Local inference | Reuse a healthy Ollama endpoint/runtime or install the app's pinned `Ollama.Ollama` 0.35.1; obtain `qwen3:1.7b`, verify the app's pinned digest, and complete functional inference. | Current user; Ollama/MIT, model/Apache-2.0. Fresh download about 2.94 GB before overhead. No cloud fallback. |
| Kokoro neural speech | Download the existing pinned model and all voices through the app's SHA-256/size-checked downloader; load the model and verify a non-empty voice set. | `IApplicationDataPaths` release user partition; about 229 MB, GitHub KokoroSharpBinaries v2.0.0. No voice playback or microphone capture. |

Preparation runs after successful native application/runtime installation in
the **non-elevated bootstrapper**, not the elevated Burn engine. A setup
started as administrator cannot prepare per-user optional assets. Existing
app services are reused rather than duplicating URLs, parsing, hash rules, or
supported-version policy. The app still re-probes readiness; no provider
preference or future-install consent is written by the installer.

On optional failure/cancellation, Kora stays installed and setup explicitly
reports the incomplete optional work. Existing or partially acquired external
components are retained, not uninstalled by rollback. Reopen setup and use
**Repair**, or complete setup in Kora Settings. Owned temporary Ollama servers
are stopped/disposed when their preparation finishes; user-owned services are
not stopped. Optional outcomes are pre-host installer diagnostics in the Burn
log, not host-authorized security-audit grants.

Windows speech/language packs are OS-managed; microphone permission, enrollment,
accounts, experimental wake/verifier dependencies, and unimplemented adapters
cannot be prepared by this POC. The UI explains these limits instead of offering
checkboxes that cannot perform their advertised actions. In particular,
Windows Settings remains the installation/configuration path for OS speech
resources. The Kokoro voice-asset redistribution review remains open; runtime
acquisition for this proof is not release clearance.

Install/repair/uninstall, major upgrades/downgrade refusal, cancellation and
rollback during native execution, reboot handling, locked files, advertised
shortcut behavior, prerequisite failures, and actual installed ACL/token and
worker boundaries still need approved disposable-Windows acceptance. Automated
tests cover the managed lifecycle using a fake engine, not Windows installation.

## Versioning and GitHub Actions

Application binaries and installer packaging use the same
[version resolver](../eng/Get-BuildVersion.ps1):

| Source | Display/informational/release version |
|---|---|
| Local feature branches and feature/PR CI | `0.1.0` |
| Untagged main, local or CI | GitVersion's `MajorMinorPatch` + `-beta` + `PreReleaseNumber`, e.g. `0.1.0-beta12` |
| Main revision carrying a stable `v<major>.<minor>.<patch>` tag | GitVersion's `MajorMinorPatch`, e.g. `0.1.0` |

Ordinary managed main builds resolve this automatically through
[Directory.Build.targets](../Directory.Build.targets); restore local tools
with `dotnet tool restore` first. CI resolves once and passes the version to
all builds/publishes and packaging. Explicit candidate-build version properties
remain available. Informational versions do not append SHA/build metadata;
source revision is recorded separately in the manifests/receipts.
Kora's own version response reads the informational version rather than the
numeric CLR assembly version, so it retains the beta suffix.

The resolver evaluates pinned **GitVersion 6.8.2** with complete history/tags and
[GitVersion.yml](../GitVersion.yml).
The policy follows
[ModelBuilder's configuration](https://github.com/roryprimrose/ModelBuilder/blob/main/GitVersion.yml):
`GitHubFlow/v1`, `beta` on main, and GitVersion 6 `ContinuousDelivery` for main,
feature, unknown, and hotfix branches. Kora adds an initial `0.1.0` version seed.

The beta format deliberately removes GitVersion's usual dot before the
increment. Detached stable-tag builds retain the tagged version and verify
that its revision belongs to `origin/main`. GitVersion must calculate the exact
checkout SHA, without normalizing/moving checkout refs. PR/fork runs cannot
publish. Beta release tags do not convert reruns into stable releases.

`MajorMinorPatch` supplies the numeric MSI/Burn version. MSI permits at most
`255.255.65535` and does not understand prerelease ordering. Multiple beta
candidates with the same numeric version are **not independently upgrade-ordered**;
the installer does not enable same-version upgrade overrides. Release notes
must retain this limitation; a new beta label is not an MSI upgrade sequence.

Portable builds/tests/application publishing remain on Linux. The Windows
packaging job verifies the exact Linux-produced x64 payload file hashes,
version, and source revision before packaging; it does not rebuild that app.
It publishes only the self-contained UI on Windows.

All feature pushes and PRs build MSI/Burn, but **never upload the installer**.
Canonical main builds and stable main-tag builds upload application/installer
Actions artifacts and automatically create GitHub Releases after all CI gates
pass. Beta versions are prereleases, never `latest`; stable tags create normal
releases. Assets include x64/x86 compiled application ZIPs, x64 MSI/Burn,
SHA-256 checksums and exact-source payload/installer/release manifests.
The x86 publish is not an accepted x86 installer/runtime commitment.

Release notes combine generated PR changes using [.github/release.yml](../.github/release.yml)
with explicit unsigned/non-production POC disclosures, prerequisites, numeric
MSI beta limitations and remaining installed/protection/storage gates. Only
the release job has `contents: write`; no PR/fork code gets publication rights.

Before rebuilding, CI checks the exact version's release, source/channel,
expected assets and GitHub SHA-256 digests against its provenance/checksums.
Complete matches skip rebuilding/publication. Unknown lookups, mismatches and
interrupted drafts fail closed for manual reconciliation; published assets
are never overwritten or tags moved. A new release is assembled as a draft
and published only after its complete upload.

Validation is front-loaded and risk-based for this POC, not an exhaustive
manual installed trial on every MSI. Each release still requires locked
dependencies, licence policy, portable/Windows tests, exact-payload inspection
and full non-skipped MSI ICE in Actions. Native installed acceptance is ad hoc
and revisited for lifecycle/native/security-sensitive changes. No job signs,
installs setup, deploys it or claims production/native/storage admission.
Feature application/test/coverage artifacts retain their existing CI behavior.

## WiX terms

The maintainer selected WiX 7 for a non-revenue-generating proof. The installer
projects set the documented `AcceptEula=wix7` acknowledgment. This does not pay a
fee or exempt other users/organizations.

Review WiX's [OSMF EULA](https://github.com/wixtoolset/wix/blob/v7.0.0/OSMFEULA.txt)
and [Microsoft Reciprocal License](https://github.com/wixtoolset/wix/blob/v7.0.0/LICENSE.TXT).
The EULA's applicability depends on the user's revenue-generating activities
and gross revenue, not simply whether Kora is sold.

The solution licence gate maps the exact SHA-256-pinned WiX 7 EULA file to
`LicenseRef-WiX-7-OSMF` and retains its original text. It does not misclassify the
package as MIT or discard the maintenance terms. WiX SDK/Burn redistribution,
source provision and notices, the self-contained .NET runtime, and other
native assets still require review before production distribution. A passing
NuGet gate is not clearance of those additional obligations.

## Validation and remaining acceptance

Run the managed solution tests after a warning-free Release build. Installer
regressions cover scope/consent freezing, read-only preflight and unknown-state
refusal, startup inspection, safe maintenance switching, optional failures/
cancellation, restart/closure launch suppression, exact launch identity and
shared-service/animation lifetimes. They use fakes; they do not install packages,
write live startup entries, acquire optional assets or establish app readiness.
The safe `--preview` mode remains useful UI-maintenance tooling, not a temporary
installer implementation or installed result.

Version fixtures exercise real GitVersion histories and compiled metadata.
Publication fixtures use a fake GitHub CLI and never make a release/network
mutation. Payload contract fixtures reject changed bytes and identity before
compiler invocation. CI additionally enforces locked dependencies/licences,
portable coverage, Windows tests and full MSI ICE.

Each packaging run extracts Burn, decompiles MSI and checks versions, scope,
quoted startup registration/conditions/costing, actual Windows-build detection,
required prerequisite chain/cache/download pins, the embedded MSI and every
bootstrapper digest, and complete application paths. `installer-build.json`
records that candidate's source, dirty state, final hashes and ICE outcome.
These checks never run install/apply.

Installed validation is front-loaded and revisited ad hoc for lifecycle,
native and security-sensitive changes, not an exhaustive manual trial of every
POC MSI. Separate approved Windows trials remain for actual install/repair/
uninstall/all-users/logon/completion, upgrade/recovery, prerequisite failures,
rollback/restart/locked files, effective ACL/token/native-loading protection,
user-data retention and runtime-only launch. The known silent-upgrade and
numeric-beta limitations above remain open.

Merge, publication, process creation, static native inspection and a local ICE
skip do not establish protected deployment, encrypted-storage admission or
future resource/worker acceptance. Keep per-revision CI/TRX and package receipts
as evidence rather than treating historical test totals as current validation.
