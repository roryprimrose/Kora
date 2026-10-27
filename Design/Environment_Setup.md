# First-Run Environment Setup and Readiness

Status: proposed full setup capability. The current bootstrap implements
independent storage/SQLite, PowerShell, and local-model readiness tasks;
remaining catalogue and acceptance requirements below are not claimed as
shipped. Delivery installs Kora; the running app owns dependency detection and
setup even when optional installer assistance is offered.

Related: [Dependency Catalogue](Dependency_Catalogue.md), [Distribution](Distribution_And_Updates.md), [Architecture](Architecture.md), [Security](Security_Data_Flows.md), [Acceptance Criteria](Acceptance_Criteria.md).

## Boundary

Source and binary delivery install/publish Kora and the code/assets required to launch its shell and setup controller.
Neither delivery path requires a preconfigured AI stack, provider selection,
model downloads or Ollama installation. Optional installer-assisted dependency
setup is a convenience, not a replacement for the running application's setup
controller. User stores are initialised by Kora, not populated by the installer.
Source delivery still requires build prerequisites; framework-dependent binary delivery still requires the declared .NET runtime before Kora can run.
Kora cannot install a prerequisite for its own process before that process can launch.

The external [managed-source bootstrap](Distribution_And_Updates.md#source-bootstrap)
now implements read-only preview and explicitly trusted build/staging verification
only. It reports Git/PowerShell/exact-SDK requirements and manual remediation;
it never installs prerequisites or elevates. Its local-source receipts are not
first-run readiness or protected installation authority. Actual activation and
launch remain unavailable pending separate approved D01/D03 scope. No provider,
model, microphone, startup or capability configuration is performed by the
build tool. Standard SQLite remains a pinned launch-critical managed/native
publish payload, not a setup checkbox or post-launch acquisition.

R02 inspection established concrete pre-launch requirements for the current
framework-dependent win-x64 bootstrap: a supported patched .NET 10 x64 Desktop
Runtime providing both declared .NET shared frameworks, plus external VC++ v14
x64 runtime DLLs imported by ONNX. See the canonical
[launch prerequisite baseline](Distribution_And_Updates.md#launch-prerequisite-baseline)
for exact observations and remaining Windows loader tests. These are delivery
requirements, not model-selected setup actions or a reason to install an SDK
on a binary user's machine. Missing optional speech/model assets still belong
to the running app's scoped readiness flow.

Once running, Kora identifies the requirements of selected capabilities, explains what is missing, and configures the environment through trusted setup operations.
Do not install every possible connector, model, or tool "just in case".

## Optional Dependencies and Built-In-Command-Only Operation

Users may decline all optional inference providers and their external
dependencies, including Ollama/local models and any external runtime or account
requirements of an admitted Copilot adapter. No provider choice or capability
dependency selection is required to install Kora or finish basic onboarding.
This does not make the launch-critical prerequisites optional.

Without a ready selected inference provider, Kora remains usable for its
supported deterministic built-in commands, native settings, help, readiness and
recovery. Each command still needs its own actual dependencies and applicable
privacy/approval gates: declining speech assets may leave typed/native input
available but voice unavailable; declining an interpreter disables commands
that require it. Do not advertise all built-ins as dependency-free. Free-form
inference and model-mediated tools remain explicitly unavailable, with no cloud
fallback or model calls to interpret an unsupported request.

Declining setup is a supported choice, not an installation failure or consent
to retry. Preserve the choice without repeatedly opening setup or downloading
dependencies on startup. Users can later reopen setup and select a capability;
Kora detects current state and obtains any required consent before installing.
Detection, installation/configuration, cancellation and health verification
remain available inside Kora regardless of whether the installer offered help.
Re-probe installer-prepared dependencies rather than trusting its success flag.

If optional installer assistance is provided, it uses the same reviewed
dependency identities, supported versions, sources, verification and consent
requirements as app-led setup. It must offer a complete Kora-only path without
requiring users to choose packages or providers upfront. Assistance cannot
install every dependency by default, silently configure an account, enable
listening, create execution grants, or authorise later app-led setup. Detect and
reuse compatible existing installations; preserve their ownership and settings.
Partial, failed or skipped assistance is reconciled by Kora's normal readiness
flow, not treated as permission to reinstall or remove user-owned components.
Missing adapter implementation still requires an application release, not
dependency setup that patches Kora code.

These are capability/setup requirements, not a waiver of release acceptance.
For example, the product must still implement and prove the required A2 local
adapter even though an individual user may decline to install or use its model.
The [WiX/Burn installer](../installer/README.md) offers unchecked PowerShell,
Ollama/pinned Qwen model, and Kokoro model/voice preparation using the existing
app services, after native prerequisite/application installation. Optional
work runs for the non-elevated interactive user; failure/cancellation retains
Kora and reports incomplete preparation. OS speech resources, accounts and
experimental/unimplemented adapters remain outside this installer slice.
This installer does not complete the durable refusal/onboarding policy or native
installed acceptance.
Kora's installation scope defaults to the current user, with all-users as an
explicit administrator-approved alternative; optional assets remain scoped
to the interactive user in either case. Missing shared runtimes can still
require elevation even when Kora itself is installed per-user.
The installer now performs read-only preflight and displays installed, missing,
stopped, asset-detected, incompatible and unverified states before approval.
It does not start Ollama, run inference, load Kokoro, obtain assets or change
provider preferences merely by opening setup. A detected asset record is not
accepted as a fresh content/functional check; selected preparation revalidates
after native apply.
Successful install/repair also offers a default-on **Start Kora when setup
closes** option. It requests one non-elevated interactive launch after
quiescent closure, with scope/version EXE/DLL coherence checks. Failure,
cancellation, uninstall, preview or a required restart suppresses launch.
This is separate from start-at-login and grants no listening consent, provider
configuration, ownership/host eligibility or storage/native admission.

## Built-In Setup Controller

Setup is deterministic host functionality, available without a configured model or skill.
It owns a versioned dependency catalogue, probes, scoped setup actions, readiness records, cancellation, and recovery.
The [source-backed dependency inventory](Dependency_Catalogue.md) distinguishes
current PowerShell/Ollama/speech/Kokoro requirements, bundled and launch
dependencies, experimental wake candidates and future provider/connector/viewer
requirements. It is a design inventory, not the implemented executable catalogue.
The catalogue defines supported versions/platforms, trusted sources, verification, installation scope, health probes, resource estimates, and removal ownership.
Models can explain a host plan but cannot invent package URLs, choose executables, change the catalogue, or run an arbitrary setup script.

Readiness states:

- Ready: required probes succeeded.
- Missing: an identified requirement is absent.
- Needs Configuration: installed but incomplete.
- Incompatible: unsupported version/architecture/capability.
- Blocked: consent, permissions, resources, or policy prevent setup.
- Failed: an attempted step failed with evidence.

Probe failure is not proof that software is absent.
Use bounded probes; an unresponsive service is reported separately from an uninstalled one.
Enable a capability only after its actual health/functional checks succeed.
Recheck on startup and relevant configuration/environment changes without reinstalling automatically.

## Responsibilities by Requirement

| Requirement | Detection/configuration | Consent and scope |
|---|---|---|
| Kora local directories | Resolve Windows known folders; create expected local/roaming data directories with safe permissions | Normal internal initialisation; no separate prompt for each folder |
| SQLite databases | Create local schema and apply versioned transactional migrations | Embedded application storage, not installation of a database server |
| PowerShell 7 | Probe a supported `pwsh.exe` (7.4 or later); offer separate verified setup | Consent to installation; readiness is independent of local inference and does not authorise a script |
| Skills | Initialise Kora-specific store, register immutable bundled skills, offer shared source selection | Discover/enable shared sources only under existing skill policy |
| Speech/wake models | Inspect supported local assets; offer verified downloads if missing | Show source, size, storage, licence, and network use before obtaining assets |
| Optional frequent-speaker learning | Detect a supported local learner and current-profile/device readiness; offer status/test/correction/reset/delete | Separate explained consent; newly activated commands only; protected derived features, no ambient/history training or remote biometric processing; not mandatory verification enrollment |
| Optional speaker verifier | Detect supported local verifier/anti-spoof assets and per-user enrollment state | Explicit opt-in; Windows Hello or equivalent native reauthentication before enrollment/replacement; no cloud biometric processing |
| Microphone/device readiness | Enumerate capture endpoints without recording; select System by default, validate permissions and actual capture readiness | Obtain explicit ongoing voice consent before first capture; later ordinary safe startup uses saved consent and fresh gates under the [microphone matrix](Security_Data_Flows.md#microphone-consent-and-enablement-matrix); tests/selection grant no ongoing consent; native recovery via [Interaction Fallback](Interaction_Fallback.md) |
| Ollama | Detect compatible existing runtime/endpoint; offer supported installation/configuration when a local-model adapter is available and selected | Reuse first; external installation/download/startup changes require consent |
| Local language model | Match selected use case to supported model and hardware/storage budget; download only the chosen model | Explain size/licence/resources; verify model identity and test inference |
| Remote provider | Detect/configure adapter and supported identity/session | Secure sign-in; no secret dictation or automatic account substitution |
| MCP connector | Probe admitted transport, identity, server, tools, and schemas | Do not install arbitrary servers or grant permissions from discovery |
| Call-aware speech | Offer manual mode and verified supported communication detectors; show coverage/freshness/Unknown handling | Optional Graph sign-in/network consent; no automatic broad Microsoft permissions |
| Logon startup | Inspect scope-matched Kora registration and offer enable/disable; installer POC defaults on for absent entries and reflects matching registry configuration | Install/Repair approves the visible choice; MSI owns HKCU/HKLM registration matching installation scope. Preserve Windows-disabled/unknown/foreign configuration; no elevated/pre-logon microphone service or voice-consent grant. Installed lifecycle proof remains open. |

Internal initialisation must not touch shared skills, source repositories, or executable code.
On storage/migration failure, report the affected capability and preserve recoverable data; never silently create a second database elsewhere.
Schema changes need backup/recovery and downgrade compatibility appropriate to the migration.

## Ollama and Existing User Installations

Ollama is optional, not a requirement for users selecting remote processing or
no inference provider at all.
Installing Ollama is useful only when the running Kora version has a compatible local-model adapter.
Missing application adapters require a verified Kora release or out-of-band development, not generation of new executable code by setup.
The initial Ollama adapter rollout remains governed by MVP/provider scope.

The current local-model setup offers per-user winget installation of
`Ollama.Ollama` 0.35.1 and the chosen `qwen3:1.7b` model (approximately
1.36 GB), each behind explicit setup consent. It uses
`127.0.0.1:11434`, compares the installed model's pinned digest
`sha256:8F68893C685C3DDFF2AA3FFFCE2AA60A30BB2DA65CA488B61FFF134A4D1730E7`,
and requires a nonempty completed inference response before reporting ready.
An unexpected digest is incompatible rather than silently replaced; malformed
metadata is reported as incompatible, not a startup crash. Storage/SQLite and
PowerShell setup tasks remain independently visible when inference is missing.
Readiness records and progress do not prove acceptable latency or offline use;
see [D-003](Decision_Register.md#d-003-local-inference-baseline).

- Probe the configured endpoint and validate runtime version/capabilities.
- Prefer a healthy existing installation and model; do not replace them or assume ownership.
- If missing, present a supported setup plan with installation destination, download, machine changes, and any elevation.
- Keep the UI/runtime unprivileged; any required elevation is a narrow explicit helper operation.
- Default to a local-only endpoint; network exposure, firewall changes, or remote binding are separate decisions.
- Track any process/service started by Kora and stop only owned instances when requested.
- Verify endpoint health and actual selected-model inference, not merely installer exit code.
- Do not delete user models, kill unrelated processes, or alter existing Ollama startup/settings without approval.

CPU-only support and hardware limits must be communicated; installing a large model does not prove that it will perform acceptably.

The [2026-10-09 unattended preflight](Local_Inference.md#unattended-provisioning-admission---2026-10-09)
rejected the existing winget/Inno acquisition path under the operator's
narrow no-UI/no-broad-PATH/no-unrelated-process approval. The pinned tagged
installer source declares a user-environment PATH update, post-install desktop
app launch and image-name process termination hooks. Package-manager
noninteractivity is not permission for those effects or ownership of a
package-started process. No installation was attempted. A portable archive
must receive a separately reviewed/approved acquisition, identity, storage,
owned-listener and deterministic teardown contract before being an alternative;
an official download link alone is insufficient.

### Local Inference Provisioning Budget

The [R02 outcomes](Local_Inference.md#outcomes-and-their-consequences) require
setup to distinguish transfer size, persistent storage, peak staging and
runtime RAM. Public metadata collected on 2026-10-05 records:

- Ollama 0.35.1 Windows setup: **1,580,352,416 download bytes**.
- Pinned qwen model blobs: **1,359,293,444 download bytes**, approximately
  1.36 GB; blob-plus-manifest storage is a lower bound, not peak pull storage.
- Fresh setup plus model transfer: **2,939,645,860 bytes**, before
  protocol/retry/additional-asset overhead. Already verified assets may be reused.

The bootstrap's existing **2,000,000,000-byte** free-space check applies to
the model volume only. It is not proof of sufficient space for the runtime,
installer, staging, KV cache or the complete setup. Expanded runtime and peak
provisioning/storage requirements are still unmeasured; do not present them as
a known minimum or change the guard based solely on compressed download size.

[R02-L2](Implementation_Roadmap.md#r02-local-inference-continuation) must measure
installation/staging/model needs on their actual volumes and verify installed
identities and distribution notices. R10 then presents a per-volume plan with
measured headroom, sources/licences, ownership and consent before acquisition.
Until qualified, label total-install resource estimates unresolved rather than
implying hardware suitability from a readiness check.

Runtime compatibility is also a qualification input: the current probe
accepts an identified responding runtime, whereas the R02 measurement preflight
requires the exact candidate version. R10 must use the tested envelope from
R02-L5, preserving healthy existing components and explaining unsupported
versions without automatic replacement. Nothing in this plan authorises asset
installation, runtime startup or firewall changes on a shared development host.

### Optional Speech Personalization

Frequent-speaker learning and speaker verification are optional, separate from wake listening and general speech recognition.
Offer separately explained local **Learn my voice** consent; declining/failing it does not block general voice or imply consent from microphone setup.
After consent, the non-authorizing learning capability uses only new deliberately activated commands under [its privacy/quality contract](Security_Data_Flows.md#optional-local-frequent-speaker-learning); no ambient/history training or retained recording archive.
Explicit verification enrollment retains its protected OS flow and fresh prompted samples; it never treats the learned profile or ordinary command audio as that enrollment.
Declining/failing verification leaves confidence `Unavailable`, not an authenticated owner or a block on baseline voice use.
Both capabilities discard transient sample audio and keep derived templates device-local, protected, and absent from roaming, logs, diagnostics, and model-accessible stores.
Provide status/test/correct/reset/delete recovery; protected-call changes require new UI initiation under the origin gate.

## Optional Local Speech Component Lifecycle

The [approved speech-input design](Speech_Input.md#optional-installation-and-runtime-lifecycle)
requires future qualified wake/VAD/ASR and optional verifier assets to be
available through both explicit WiX/Burn install-time preparation and later
Settings setup. Current installer choices remain PowerShell, Ollama/Qwen and
Kokoro. The bounded [shared component service](../src/Kora.Application/Dependencies/SpeechComponentSetupService.cs)
now implements exact review, consent, verification, removal and retry orchestration,
with [private atomic data storage](../src/Kora.Windows/Dependencies/WindowsSpeechComponentStore.cs).
The [production catalogue](../src/Kora.Core/Dependencies/SpeechComponentCatalogue.cs)
is empty: D-002 has not supplied an authoritative qualified input backend/model
offer. Settings and Burn therefore report no optional input offers, not fake
installation success; no speech profile is enabled.

The [catalogue metadata](../src/Kora.Core/Dependencies/SpeechComponentDefinition.cs)
pins an exact model and immutable installed runtime-profile identity, source
bytes/digests, licence, qualification and resource costs. The installed host
adapter owns exact native/backend/hardware compatibility, all consumers,
exclusive quiescence and inert functional checks through
[the runtime seam](../src/Kora.Core/Dependencies/ISpeechComponentRuntime.cs).
The production unavailable adapter denies those operations. A release adding
an offer must supply and qualify that adapter; component setup cannot install
native code or launch prerequisites.

Readiness remains a cohesive setup observation, not a runtime-profile selection
contract. An inspection failure returns no storage snapshot and an explicit
**Unknown** reason rather than false/missing/ready. Installed-adapter observations
distinguish unknown, unavailable and incompatible state; functional evidence is
not-checked, verified, failed, unknown or unavailable. Inspection reads only
existing authoritative evidence and never performs a functional probe
([readiness](../src/Kora.Core/Dependencies/SpeechComponentReadiness.cs),
[runtime port](../src/Kora.Core/Dependencies/ISpeechComponentRuntime.cs)).
The exclusive mutation port returns a
[typed admission](../src/Kora.Core/Dependencies/SpeechComponentMutationAdmission.cs):
acquired ownership carries a real owned lease, while unavailable, busy, unknown,
shared/user-owned and policy-denied states carry distinct status/audit reasons.
Denied states cannot become a successful no-op lease.

[Settings](../src/Kora/SettingsWindow.SpeechComponents.cs) and
[Burn choices](../installer/Kora.Setup/SpeechComponentInstallChoice.cs)
consume the same one-use, expiring exact review and shared service. Inspection
hashes existing data without downloads or model loading. Mutation rechecks the
reviewed content, host admission and exclusive ownership; preparation publishes
a complete verified directory without overwriting existing data. Interrupted
removal retains an exact recovery record. A failed/uncertain operation requires
inspection and a new explicit review rather than automatic replay
([deterministic tests](../tests/Kora.Windows.IntegrationTests/Dependencies/WindowsSpeechComponentStoreTests.cs)).

Extend one versioned host catalogue and existing setup seams, rather than
implementing independent installer and application download policies. Each
component records exact engine/runtime/model identities, supported
Windows/architecture/CPU/GPU, trusted source, byte/hash verification, licence
and redistribution disposition, download and staged/installed disk estimates,
RAM/VRAM estimates, dependencies, scope/ownership, health checks and removal
rules. Bundled native code/launch dependencies remain distinct from optional
data; a new adapter/runtime must arrive through admitted application delivery.

Both entry points must:

1. Inspect existing assets and compatibility without starting capture, learning,
   inference or downloads during read-only preflight. Unknown is not Missing or
   Ready. Show install/reuse/verify needs and retain a continue-without path.
2. Present only actionable qualified choices, unchecked at install time, with
   source/licence, download/storage/resource costs and any external prerequisite
   before exact consent. Required shared runtimes/elevation stay under
   distribution policy; never silently install GPU drivers or SDKs.
3. Prepare only selected assets using the common bounded service. Follow the
   existing non-elevated interactive-user asset scope even for all-users Kora
   installation, with production paths supplied by `IApplicationDataPaths`.
   Isolate staging and verify bytes before atomic publication; preserve
   pre-existing/shared assets and report partial or uncertain outcomes.
4. Re-probe after preparation. A verified download or installed record is not
   functional readiness, capability qualification or selected provider state.
   Functional checks use inert fixtures, never microphone capture or commands.
   The app revalidates readiness on use; no installer receipt grants host
   authority, microphone consent, enrollment, learning or future downloads.
5. Surface progress, cancellation, failures and retry/reconciliation in Settings.
   Optional failure/cancellation does not roll back Kora or remove user-owned
   components. Offline users can decline acquisition or explicitly verify
   supported already-present assets without hidden network fallback.
6. Keep installation/removal, active profile choice, compute/residency and
   learning/protection settings separate. Join owned capture/model work before
   replacing/removing in-use assets; check shared consumers, preserve saved
   selections as unavailable if their assets are removed, and never select
   another provider or disable protection implicitly.

App upgrade/repair/uninstall follows the existing ownership/retention policy,
not automatic removal of model data or derived voice profiles. Version or
backend incompatibility reports unavailable pending explicit reconciliation.
Explicit derived-profile deletion also clears bounded candidate/recovery
revisions and cached features; deleting a model is not implied biometric-data
deletion. Installer versus Settings paths need equivalent detection,
verification, failure/cancel/retry and existing-asset preservation coverage.

## Setup Flow

1. Launch Kora's setup shell and initialise its own local storage.
2. Offer interaction/provider capabilities and an explicit continue-without-optional-dependencies path; no provider selection is compulsory.
3. Probe those requirements and show Ready/Missing/Blocked items.
4. Present a bounded plan for missing/configuration steps, including downloads and changes.
5. Obtain required consent and execute registered host actions in dependency order.
6. Validate each result, record ownership, and make partial progress visible.
7. Enable ready capabilities and explain anything still unavailable.

Once local speech/wake assets are ready and microphone consent is active, setup can continue by voice.
Before then, the app provides an accessible visual setup path: it cannot use a missing recogniser to obtain consent to install that recogniser.
Mouse-based questions are a required ongoing interaction channel, not just a temporary setup exception; see [Mouse-Based Interaction](Interaction_Fallback.md).
Kora asks which detected microphone to use and accepts selections/consent entirely by mouse; unavailable devices/permissions do not strand onboarding.
If assets are already present, voice setup can begin after validation and consent.

Setup can be reopened later. Runtime readiness events may produce proactive offers, but suggestions do not approve installation.
Once voice is ready, every supported setup preference uses [User Configuration](User_Configuration.md); installations and secure sign-in remain separately approved workflows, not dictated configuration values.
User rejection keeps the selected capability unavailable with an explanation; no cloud fallback or unrelated package installation.

## Security, Offline Operation, and Recovery

Downloads/external installations are explicit setup network operations separate from task execution.
Local-only tasks never silently fetch assets; fully offline setup can use already installed/verified assets.
External setup actions are restricted to the protected catalogue and exact host proposal, not exposed as model/skill installer tools.
Changes requiring arbitrary code/elevation retain deliberate visual confirmation; simple model-data downloads may use a scoped voice confirmation.

Dependency locations are separate from Kora executable/source roots and skill data.
No setup operation can update Kora code, security policy, bundled scripts, or its updater; application maintenance remains a different approved channel.
Verify publisher/integrity, restrict redirects/arguments, and respect protected-resource isolation.
An external installer capable of touching protected Kora resources must be constrained/mediated or rejected; trusting its name is insufficient.

Use resumable, idempotent steps and verify after cancellation/restart instead of trusting old success markers.
**Install and verify** is one setup operation: its original task remains
**Running** and cancellable through installation and every final readiness
check. Verification does not start a second setup task or mark installation as
completion. Its progress is indeterminate during final checks; only verified
local-model readiness completes the original task at 100%. Missing or failed
readiness, an absent required verification result or a verification error leaves
that task **Failed**, with partial progress available for explicit inspection.
Cancelling an approved local-model setup during final health verification joins
the outstanding check before reporting **Setup cancelled**. Even if that check
returns normally, it must not publish a fresh readiness observation or complete
the original setup successfully after cancellation. Independently completed
checks retain their observations; cancellation does not roll back installed
bytes, remove existing assets or stop a user-owned server. Inspect partial
progress with an explicit fresh readiness check, and review any new installation
separately. Cancellation does not automatically retry, reinstall or invoke the
model, and a fresh observation alone does not qualify or enable the runtime.
Keep temporary downloads separate and clean up only known owned staging content.
Do not roll back or uninstall pre-existing user components.
Record partial/unknown machine changes and require reconciliation before retrying an uncertain installation.
