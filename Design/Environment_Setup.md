# First-Run Environment Setup and Readiness

Status: proposed full setup capability. The current bootstrap implements
independent storage/SQLite, PowerShell, and local-model readiness tasks;
remaining catalogue and acceptance requirements below are not claimed as
shipped. Delivery installs Kora; the running app owns setup of its environment.

Related: [Distribution](Distribution_And_Updates.md), [Architecture](Architecture.md), [Security](Security_Data_Flows.md), [Acceptance Criteria](Acceptance_Criteria.md).

## Boundary

Source and binary delivery install/publish Kora and the code/assets required to launch its shell and setup controller.
They do not provision an entire AI stack, populate user databases, configure providers, or install Ollama.
Source delivery still requires build prerequisites; framework-dependent binary delivery still requires the declared .NET runtime before Kora can run.
Kora cannot install a prerequisite for its own process before that process can launch.

Once running, Kora identifies the requirements of selected capabilities, explains what is missing, and configures the environment through trusted setup operations.
Do not install every possible connector, model, or tool "just in case".

## Built-In Setup Controller

Setup is deterministic host functionality, available without a configured model or skill.
It owns a versioned dependency catalogue, probes, scoped setup actions, readiness records, cancellation, and recovery.
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
| Microphone/device readiness | Enumerate capture endpoints without recording; select System by default, validate permissions and actual capture readiness | Ordinary safe startup automatically attempts listening after readiness; offer testing, disable/recovery, device selection, and continue-without-voice via [Interaction Fallback](Interaction_Fallback.md) |
| Ollama | Detect compatible existing runtime/endpoint; offer supported installation/configuration when a local-model adapter is available and selected | Reuse first; external installation/download/startup changes require consent |
| Local language model | Match selected use case to supported model and hardware/storage budget; download only the chosen model | Explain size/licence/resources; verify model identity and test inference |
| Remote provider | Detect/configure adapter and supported identity/session | Secure sign-in; no secret dictation or automatic account substitution |
| MCP connector | Probe admitted transport, identity, server, tools, and schemas | Do not install arbitrary servers or grant permissions from discovery |
| Call-aware speech | Offer manual mode and verified supported communication detectors; show coverage/freshness/Unknown handling | Optional Graph sign-in/network consent; no automatic broad Microsoft permissions |
| Logon startup | Inspect Kora registration and offer enable/disable | User consent in Kora setup; no elevated/pre-logon microphone service |

Internal initialisation must not touch shared skills, source repositories, or executable code.
On storage/migration failure, report the affected capability and preserve recoverable data; never silently create a second database elsewhere.
Schema changes need backup/recovery and downgrade compatibility appropriate to the migration.

## Ollama and Existing User Installations

Ollama is optional, not a requirement for a user selecting only a supported remote provider.
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

Frequent-speaker learning and speaker verification are optional, separate from wake listening and general speech recognition.
Offer separately explained local **Learn my voice** consent; declining/failing it does not block general voice or imply consent from microphone setup.
After consent, the non-authorizing learning capability uses only new deliberately activated commands under [its privacy/quality contract](Security_Data_Flows.md#optional-local-frequent-speaker-learning); no ambient/history training or retained recording archive.
Explicit verification enrollment retains its protected OS flow and fresh prompted samples; it never treats the learned profile or ordinary command audio as that enrollment.
Declining/failing verification leaves confidence `Unavailable`, not an authenticated owner or a block on baseline voice use.
Both capabilities discard transient sample audio and keep derived templates device-local, protected, and absent from roaming, logs, diagnostics, and model-accessible stores.
Provide status/test/correct/reset/delete recovery; protected-call changes require new UI initiation under the origin gate.

## Setup Flow

1. Launch Kora's setup shell and initialise its own local storage.
2. Ask which interaction/provider capabilities the user wants.
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
Keep temporary downloads separate and clean up only known owned staging content.
Do not roll back or uninstall pre-existing user components.
Record partial/unknown machine changes and require reconciliation before retrying an uncertain installation.
