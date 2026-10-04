# Architecture and Contracts

Status: proposed. Contracts below describe required semantics, not an existing SDK API.

Related: [Extensibility](Extensibility.md), [Security and Data Flows](Security_Data_Flows.md), [Task Lifecycle](Task_Lifecycle.md).

## Runtime Ownership Decision

Kora owns persistent work sessions, task lifecycle, context selection, permission evaluation, approvals, and presentation.
A runtime adapter may own model/tool iteration, but must not bypass those responsibilities.
The canonical channel, routing, lifecycle, and history contract is [Human Interaction and Persistent Sessions](Interaction_And_Sessions.md).

This accommodates agent-oriented SDKs without pretending every provider is a stateless inference API.
It also introduces integration work: each adapter must demonstrate that its automatic behaviours can be disabled or mediated.

There are two adapter families:

- Agent SDK adapter: delegates iteration to an SDK while enforcing Kora's control points.
- Inference adapter: uses a Kora-owned loop to turn model responses into mediated tool requests.

Both expose the same task-facing protocol. Inference adapters share one loop implementation rather than duplicating it per provider.
That loop is built when an inference provider is introduced, not speculatively for Slice A.

## Commands, Tools, and Skill Interaction

[Commands, Tools, Skills, and Model Interaction](Commands_Tools_And_Skills.md)
defines the app -> model -> app contract and the distinction between an input
command, an internal host action exposed as a tool, a skill, and its registered
executable tasks. "Built in" describes ownership, not a requirement that every
capability be a skill or script.

The host advertises relevant admitted tool definitions and enabled, pinned
skill summaries to the runtime. Natural-language interpretation produces typed
proposals, not execution authority. The host resolves a selected skill's
pinned workflow and supplies approved instructions/references to the task
runtime, or interprets declarative steps in its bounded engine. It resolves
registered tasks, applies policy/grants, executes, and returns
bounded structured results to the same runtime after any required egress
review. The runtime may then answer or propose a further checked step.
Internal state-query tools return facts without UI side effects; host
presentation consumes those facts separately.

Exact local commands and essential controls can bypass inference, but not
their applicable action gate. All routes share the underlying host services;
the management session retains only its minimal ledger/proposal contract,
not the execution runtime's tool catalogue or skill instruction bodies.
The current one-response action selector is not this complete tool loop.

## Platform Boundaries and Support

Windows is the only supported application platform for the foreseeable future.
Multi-platform extensibility is an implementation principle, not a commitment to Linux/macOS releases, feature parity, or a porting schedule.
Linux GitHub Actions are build infrastructure, not evidence of Linux application support.

Keep portable domain logic and shared Avalonia presentation separate from first-party Windows integrations:

- Core: task/queue state, policy decisions, approvals, configuration, declarative skills, provider contracts, and presentation data.
- Desktop presentation: native question models, captions, answer/navigation UI, and shared rendering orchestration.
- Windows integration: actual device/session/clipboard APIs, native UI hooks, known folders, credential storage, containment, computer controls, startup, browser backend, and installer/maintenance execution.

The shared core initially targets portable `net10.0`, not `net10.0-windows`, and references no Windows-only API assemblies.
Windows-specific targets/dependencies belong in integration modules or the platform composition/launch layer.
The exact .NET 10 SDK/runtime versions are pinned during implementation; moving to .NET 11 is a deliberate tested dependency upgrade, not a portability strategy.
The logical separation may become projects when real dependencies justify it; do not create empty Linux/macOS projects or speculative backends.

Use narrowly scoped host-owned contracts at real platform seams:

| Contract area | Shared semantics | Windows implementation responsibility |
|---|---|---|
| Audio endpoints/capture | Stable device identity, readiness, explicit consent, generation/cancellation | Enumeration, permissions, capture handles and device notifications |
| Session privacy | Locked/disconnected/unknown denies capture and approvals | Authoritative interactive-session observation and prompt/device shutdown |
| Clipboard and storage locations | Explicit scoped snapshots, app-owned data partitions, canonical resources | Clipboard threading/formats and Windows known-folder resolution |
| Credentials and containment | Opaque credential references; enforce protected-resource denial | Secure store and verified native execution/deployment isolation |
| Desktop integration | Accessible status/recovery, no hidden listening/focus theft | Tray lifecycle, display/window constraints, local startup registration |
| Computer controls | Fixed registered effects, action-bound confirmations/receipts | Embedded Windows script resources and admitted OS actions |
| Content viewing | Typed content, resource limits, isolation and provenance | Verified Windows browser/renderer integration |
| Maintenance | Unsigned-phase notify-only release metadata and canonical-page navigation; future signed-metadata approval/quiescence/recovery boundary | Initial external Windows replacement; future installation/UAC/ACLs, activation and unprivileged relaunch only after separate gate |

A platform adapter supplies observations and mechanisms; it cannot replace or weaken shared policy.
Keep native handles, Windows paths, registry details, and OS exceptions behind these boundaries; do not spread OS checks through task/skill/provider logic.
Model/skill inputs cannot select arbitrary adapter assemblies or executable paths.
Future adapters are trusted application code, not user-installed policy replacements.
Missing/unknown observations fail closed for the affected capability, never default to unlocked, permission granted, plaintext credentials, or unrestricted execution.

Portable tests run on Linux with controlled platform fakes; actual Windows integrations still require Windows evidence.
Launching Kora on another OS reports unsupported platform before enabling native/capture/execution capabilities, rather than advertising a partially supported port.
A future supported platform needs an explicit scope decision, its own native dependencies/package/signing choices, and the same applicable privacy/integrity/approval gates.
Existing Windows AppData, tray, power, and installer requirements remain the concrete supported implementation.

## Component View

The shared lifecycle coordinator and Windows exclusive-ownership/IPC adapter follow [Instance Coordination](Instance_Coordination.md).
Ownership is acquired before assistant/audio/provider/store-migration startup; a return supervisor has lifecycle-only authority, not another assistant execution slot.

```text
Avalonia Shell
  |-- Voice Session Controller -- Local Wake Detector / Endpointing / Speech Adapters
  |       `-- Session Privacy Contract -- Windows Adapter / Microphone Lifetime Gate
  |-- Compact Session UI / Session Manager / History and Detail Viewer
  |-- Structured Interaction Service -- Shared Voice/UI Questions / Recovery / Approval Input
  |-- Session Registry / Event and Artifact Store / Retention Controller
  |-- Reserved Intent Registry -- App Lifecycle / Fixed Computer Controls / Exact Local Management
  |-- Work Manager -- Management Model Adapter
  |       `-- Per-Session Ledger / Bounded Scheduler / Shared-Resource Leases
  |-- Proactive Interaction Broker -- Trusted Task Events / Release-Availability Events
  |-- Speech Policy Service -- Call-State Aggregator / Local and Opt-In Communication Detectors
  |-- Speaker Confidence Service -- Optional Local Verifier / Enrollment and Anti-Spoof Boundary
  |-- Environment Setup Controller -- Dependency Catalogue / Probes / Readiness / Scoped Helpers
  |-- Configuration Service -- Typed Option Registry / Verbal and UI Operations
  |       `-- Security Audit Contract -- Correlated Write / Execution / Approval Events
  |-- Task Controller
  |     |-- Context Broker -- Clipboard / Explicit File Selection
  |     |-- Policy + Approval Service
  |     |-- Runtime Adapter -- SDK or Inference Loop
  |     |       |-- Mediated Context Egress
  |     |       `-- Tool Gateway -- Built-In Tools / MCP / Script Worker
  |     `-- Response Presenter -- Speech Text / Rich Details / Local TTS
  |           `-- Content Viewer -- Isolated HTML / Approved Browser Navigation
  `-- Configuration, Credential References, Audit, and Encrypted Session History
```

External knowledge retrieval is a later context source, not a mandatory path for every request; bounded Kora session-history retrieval is a required host capability.
Deterministic cancellation and basic ledger status stay local.
Contextual work-management decisions use a separate model session, independently schedulable from the execution runtime.
Detailed task interpretation and model/tool iteration remain in the task runtime.

## Core Responsibilities

| Component | Owns | Must not own |
|---|---|---|
| Shell | Coordinated compact/workspace/detail/settings surfaces, shared selected session, accessible native cards, per-session drafts and input | Direct tool execution, provider credentials, or using window focus as execution authority |
| Structured interaction/recovery service | Voice/UI question drafts and typed replies, independent addressed cards, one foreground voice target, endpoint recovery | Model self-answer/self-approval, capture before consent, silent device replacement |
| Session registry/history service | Stable session identity, ordered durable events/artifacts, routing proposals, Active/Done lifecycle, retention/deletion | Replaying execution/grants or exposing all history to a provider |
| Voice controller | Wake-listening consent, local configured-name detection ("Kora" by default), bounded audio buffer, endpointing, transcript, playback-aware interruption | Ambient transcription or authorising actions based on wake detection/speaker verification |
| Task controller | Task IDs, state transitions, deadlines, cancellation | Provider-specific model iteration |
| Work manager/scheduler | Contextual session/request routing, per-session versioned ledger/queue, bounded fair dispatch and resource leases | Running task tools in management inference or bypassing task approvals |
| Context broker | Snapshots, provenance, classification, context selection | Implicit background collection |
| Policy/approval service | Resource-scoped grants, outbound decisions, approval tokens | Trusting model-produced permission claims |
| Runtime adapter | Provider session and event translation | Unreviewed tools, undisclosed egress, global policy |
| Tool gateway | Validate, authorise, invoke, bound, and audit tools | Giving an adapter unrestricted OS access |
| Response presenter | Streamed display, optional speech text, typed rich detail, summary, citations, TTS | Treating partial answers as completed work or untrusted content as controls |
| Content viewer/rendering service | Bounded Markdown/diagram/static HTML rendering, browser provenance/navigation/isolation | Browser automation, implicit context capture, arbitrary renderer plugins or host bridges |
| Proactive interaction broker | Event eligibility, speech timing, deduplication, prompt identity, trusted maintenance dialogue routing | Autonomous tool execution or treating untrusted content as system events |
| Security audit service | Correlated content-minimizing request/outcome events for writes, process/script execution, protected operations, and approvals | Authorizing an action, storing raw content/arguments, or treating diagnostics as tamper-evident evidence |
| Speech policy service | Central playback eligibility, call-state freshness, voice-configurable preferences, one-shot overrides | Claiming universal call detection or allowing lock/mute bypass |
| Speaker confidence service | Separately consented local per-SID frequent-speaker adaptation, protected learned/enrolled profiles, quality/verification observations, optional privacy-policy signal | Authenticating a learned frequent speaker, granting actions, satisfying approvals, ambient/history training, exposing scores/templates, or silently replacing enrollment |
| Reserved intent/lifecycle controller | Exact local control routing, target disambiguation, named confirmations, serialised app/power/maintenance lifecycle | Arbitrary shell commands or user-skill shadowing of privileged controls |
| Storage services | Configuration, metadata, encrypted permitted session history/artifacts/indexes, migration/deletion | Raw audio, plaintext credentials, unrestricted clipboard collection, or silent eviction |
| Environment setup controller | Internal storage/schema initialisation, capability probes, approved dependency setup, ownership and readiness | Arbitrary model-supplied installers or changing Kora code |
| Configuration service | Option schema, validation/scope, revision-safe persistence, voice/UI parity, effective settings | Arbitrary config-file patches or weakening mandatory policy |

Speech decisions are revalidated at playback start and when policy/detector state changes.
Private speech decisions also revalidate current speaker-confidence availability and result; missing or uncertain verification never defaults to owner.
Learned frequent-speaker attribution is personalization, not owner authentication; [Security](Security_Data_Flows.md#optional-local-frequent-speaker-learning) owns consent, adaptation, isolation, deletion, and optional verification boundaries.
Communication detector adapters expose observations, not authority; see [Call-Aware Speech](Call_Aware_Speech.md).
Every user preference, including admitted extension settings, uses [User Configuration](User_Configuration.md); voice and UI share validation and persistence.
The [window design](UI_Workspace_And_Windows.md) defines shell roles and shared view state: workspace/compact share UI selection, independently opened detail references stay immutable, and response/draft updates route by stable session/task/item IDs.
This is not shared provider conversation state or permission to dispatch; the registry/interaction/scheduler remain authoritative.
Rich presentation follows [Information Display](Information_Display.md); native approvals and trust indicators remain outside rendered content.
Native questions and first-run/device-loss recovery follow [Interaction Fallback](Interaction_Fallback.md) and require no working microphone, model, or rich renderer.

## Independent Management and Concurrent Sessions

The work manager stays responsive independently of the task runtime's event loop, tool calls, and approval waits.
Management uses a separate session and cancellation/deadline scope; it does not inherit executable tools or ambient task context.
It proposes typed ledger operations that the host validates and commits atomically.
Local stop and basic status paths bypass management inference.
Independent Kora sessions execute concurrently within a configured, verified budget, initially proposed as two slots with one task per session.
Per-session context/runtime/grant/event isolation, shared/exclusive canonical resource leases, dependency checks, and fair scheduling are mandatory.
Unknown resource effects cannot claim safe concurrency; require an enforced exclusive domain or reject unsupported execution.
UI selection, foreground voice prompt, TTS playback, and execution-slot ownership are independent.

An adapter must prove that execution and management requests can make progress independently, using separate sessions or runtime instances where needed.
Do not share mutable SDK conversation state between lanes.
A provider that serialises all inference behind a long-running execution call does not satisfy this contract without a separately schedulable management implementation.
Remote management still requires the same context egress controls; it is not an exempt background service.
The initial management envelope allows one in-flight request, at most 32 KiB input and 4 KiB output, a 15-second deadline without automatic retry, and 30 remote calls per rolling hour per profile.
Quota, cost, rate-limit, SDK, account-tier, and terms constraints are capability evidence, not deployment assumptions.
Exhaustion or incompatibility activates the deterministic local queue/status/choice path; it never blocks cancellation or the executor.
See [Work Management](Work_Management.md).

## Voice Activation Pipeline

Wake-word activation is a built-in Slice A capability, independent of the agent runtime.
The voice controller owns microphone consent, device lifetime, and a bounded in-memory pre-roll buffer.
A local detector recognises the configured active names ("Kora" initially) without sending ambient audio to transcription or a model.
The host-owned profile/alias registry and atomic detector switching follow [Custom Activation Names](Activation_Name.md); names are data-only settings, not replacement executable code.

On detection, the controller emits activation feedback and captures the command, preserving words spoken immediately after the wake word.
Local voice activity detection ends the command; only activated command audio reaches the local transcription engine.
The wake word is excluded from the task request.
Optional push-to-talk enters the same capture path without requiring the wake word.

During speech output, playback-reference echo rejection prevents Kora's own audio from activating the detector or entering command transcription.
A genuine user activation stops playback and opens command capture.
If reliable playback rejection is unavailable for the current device, disable spoken output with an explicit explanation while retaining wake activation and visual responses; do not silently fall back to push-to-talk.
The selected detector, endpointing, and playback handling require a packaged integration proof and the acceptance tests in [Acceptance Criteria](Acceptance_Criteria.md).
The microphone lifetime gate also requires authoritative unlocked-session state.
Lock/disconnect immediately blocks acquisition and stale callbacks, stops recognition, clears audio, and releases devices.
This policy is not implemented inside a skill and applies to every lock origin.
The original bundled lock skill has a fixed, verified embedded-resource script registration and priority session-control route; see [Bundled Skills](Built_In_Skills.md).

## Runtime Protocol

The implementation must define versioned, strongly typed equivalents of these messages:

| Contract | Required fields and semantics |
|---|---|
| Runtime capabilities | Runtime/version, local or remote destinations, streaming, tool mediation, cancellation, context filtering, supported input types |
| Trusted input/intent lineage | Host input-event ID, initiating channel, current Windows-user scope, original request and approved-plan references; immutable across interpretation/management/task/tool hops, never model-authored or relabeled by a later confirmation |
| Task request | Session/task IDs, user request, trusted intent-lineage reference, approved context IDs, effective policy/revision, deadline, selected runtime |
| Capability catalogue | Snapshot/revision, admitted tool IDs/schema versions/descriptions, enabled source-qualified skill revisions/selection summaries, dependency availability; no implied grants |
| Skill selection | Session/task IDs, source-qualified skill ID and pinned revision, schema-valid inputs, trusted intent lineage; resolves a workflow, not an execution approval |
| Management request/proposal | Management request ID, trusted intent-lineage reference, minimal approved session descriptors/context, registry/ledger revision, typed route/operation, target session/task IDs, evidence or clarification |
| Session record/event | Session ID, lifecycle/work state, sequence/revision, channel/provenance, durable timestamps, history/artifact references, retention due times |
| Structured question/reply | Session/task/question IDs, revision, typed option/field schema, constraints, draft/accepted answer, expiry and submit/cancel meaning |
| Work status | Task/step states, observation timestamps, provenance of progress, queue position, blocker; distinguish plans from confirmed outcomes |
| Context item | ID, immutable content reference, content hash, source, media type, trust/classification, identity scope, capture time, expiry |
| Runtime event | Session/task IDs, sequence, event type, typed payload; no state-changing work hidden in display text |
| Tool invocation | Invocation ID, session/task IDs, tool/version, selected skill revision/registered task where applicable, schema-valid parameters, target resources, host-assigned effect class, trusted intent-lineage reference, host-validated authorization reference, current policy/call generation and setting revision at dispatch, deadline |
| Tool result | Invocation ID, success/pending/blocked/unavailable/conflict/failure/unknown/denied/cancelled status, bounded structured data, observation time, provenance, classification, side-effect receipt |
| Approval request | Session/task/proposal IDs and revision, exact action/destination, resources and script/dependency/parameter hashes, requested `grantScope: Once/Session/Perpetual`, bound session ID for Session, host-eligible scope/required Once reason during protected calls, proposal deadline (not perpetual grant expiry), host-assigned risk/review, optional speaker policy, mandatory OS checks, trusted intent lineage, call-policy generation and setting revision, user-readable summary |
| Grant record/dispatch authorization | Stable grant ID/revision, exact approved operation/resources/identity/digests, scope and bound session where required, creation/edit/provenance; Once consumption and session-end applicability; independently retained Perpetual without expiry/retention/eviction; per-invocation dispatch authorization is distinct from the stored record |
| Completion | Completed/cancelled/failed/unknown-side-effects, final answer references, action receipts, error detail |

Runtime events include answer deltas, tool proposals, context transmission proposals, progress, and terminal events.
Skill selections resolve through the host catalogue before supplying approved
instruction data/tool references for admitted tool/task proposals; selecting
a skill never independently dispatches its script. Skill-summary/instruction
egress obeys the same context controls as other selected sources.
The host must await decisions on proposals before execution or transmission. Approval is not a retrospective notification.

A provider's built-in filesystem, shell, browsing, memory, telemetry, or connector features must be disabled unless they satisfy the same controls.
Passing approved initial context to an SDK is insufficient if the SDK can later collect or transmit additional data independently.
Untrusted content is structurally separated from system/developer policy and workflow-stage controls.
Every proposed operation carries intent lineage to deliberate user input or an explicitly approved host plan step; capability/grant scope alone is not sufficient justification.
Initiating channel comes from the trusted input event, not the adapter/model or the channel used for later confirmation; absent provenance cannot be treated as UI authorization for protected-call settings.
The host stamps/rechecks live call evidence, protection generation, and setting/policy revisions at approval/apply/dispatch boundaries under [Call-Aware Speech](Call_Aware_Speech.md).
A changed protection generation revalidates affected pending authorization rather than trusting the generation captured at planning time; no origin or scope field can bypass the current host gate.
Grant scope/lifetime follows [Security](Security_Data_Flows.md#grant-types-and-inheritance); proposal/dispatch deadlines never become retention or expiry on a Perpetual record.

## Capability Negotiation

Capabilities are verified for the pinned adapter/runtime version and intersected with current user policy.
They are not accepted solely from a self-declared extension manifest.

- Local-only tasks reject runtimes with remote model execution or undisclosed destinations.
- Tools are disabled if the adapter cannot delegate every invocation to the gateway.
- Remote tool results cannot be submitted to a model unless their egress can be mediated.
- If an SDK cannot pause before newly introduced context is sent, restrict it to approved immutable context and host-mediated tool handling, or reject that workflow.
- Cancellation limitations are displayed before enabling affected workflows.
- Unsupported input types produce an explicit capability error, not a lossy conversion.

Automatic provider switching is excluded from the MVP. Changing runtime starts a new task with a new destination/approval assessment.

## Tool Gateway

The [Internal Model Tool Catalogue](Internal_Model_Tools.md) owns the complete current/proposed tool inventory, stable names, caller lanes, schema/effect boundaries, and host-only exclusions.
The current bootstrap exposes registered action/question/grant-change JSON proposals, not this future tool-call API.
Capability negotiation advertises only verified implemented subsets, never every proposed catalogue entry.

All internal host tools, skill-backed task invocations, and external tools use
the same applicable authorisation path, including direct command/UI routes:

1. Resolve the tool/version and validate parameters.
2. Resolve canonical target resources and identity.
3. Verify intent lineage and reject resources/actions introduced only by untrusted content.
4. Evaluate grants, host-assigned risk, owner presence, and any required approval.
5. Bind approval to the resolved action and content.
6. Revalidate immediately before execution.
7. Execute with deadline and output limits.
8. Produce an action receipt and provenance-bearing result.

Query results are separate from window/speech presentation. Return approved,
bounded result data to the task runtime for continued reasoning; direct local
commands can present the same result without inference. A skill/script cannot
open approval controls, classify its own effect, or invent a successful receipt.
Tool names and descriptions are untrusted metadata. Policy bindings are maintained by Kora and reviewed when tools change.

## Storage and Processes

The proposed store uses SQLite for configuration, session/events, and task/action metadata, with encrypted permitted content/artifacts/indexes and OS-protected keys.
Encryption integration, transactional persistence, migration, journal/backup deletion, and failure behavior are release gates; no particular encryption package is selected here.
Machine-local configuration/enablement/audit storage is under `%LOCALAPPDATA%\Kora`.
Kora-specific declarative skill packages are under the Windows Roaming AppData folder at `%APPDATA%\Kora\Skills`.
Shared profile skill roots are registered read-only sources, not writable storage.
Credentials are represented by opaque references to an OS-protected credential store.
Large retained artifacts use a managed encrypted store linked by immutable digest, session ID, and deletion ownership.
Default inactivity archiving/deletion is 24 hours/30 days, both configurable; passive history access never extends retention.
Restart restores readable history, not active dispatch, provider memory, or executable approval tokens.
The running app creates its stores and migrates embedded SQLite schemas; installers do not provision a database server.
Setup is usable before any model is configured and can offer missing speech/Ollama/model requirements for selected capabilities.
The current bootstrap checks/initialises storage and SQLite and tracks
PowerShell 7 readiness/setup independently of Ollama inference. PowerShell
installation is not execution admission. The current local path requires
consented per-user Ollama 0.35.1 and digest-pinned `qwen3:1.7b`, with a real
completed loopback inference check before unmatched requests use it.
Model-produced answers/questions and registered action/grant proposals are
host-validated; they are not free-form tool execution. The future common
version/hash-bound task gate, including direct lock, is specified in
[skill and task execution design](../docs/skill-and-task-execution-design.md),
not implemented by this bootstrap.
See [Environment Setup](Environment_Setup.md).

Kora-owned native speech workers may run out of process for crash containment.
Third-party MCP servers and script workers run separately from the UI process.
Process separation does not imply filesystem or network sandboxing; see the execution trust model.
Agent-executable components must additionally be unable to mutate protected Kora resources; unrestricted ambient-rights workers are not admitted merely because their code is trusted.

## Skill Data Versus Application Code

The built-in authoring service accepts voice refinements and produces declarative skill packages.
Validation, bounded skill-store writes, version registration, and activation are host-owned services.
The store is outside protected code roots and is never an executable loading location.
Models propose package content, not arbitrary destination paths or changes to executable components.
Missing implementation dependencies are reported, not generated/installed as an implicit part of authoring.
All write-capable tools, connectors, and future process workers enforce [Application Integrity](Security_Data_Flows.md#application-integrity-and-no-self-modification).
Application updating is outside the runtime/tool graph entirely.
Automatic release checks publish bounded availability events for proactive speech.
During the unsigned phase the host-owned dialogue is notify-only and cannot reach download, staging, execution, source mutation, or activation.
Any future per-release installation approval reaches a separately gated maintenance controller through exact host-owned voice/UI confirmation and required OS checks, never a model-callable installer tool.
Installation metadata distinguishes managed source and binary deployments from developer checkouts.
Source builds and binary publication converge on versioned deployment outputs; logon launches published code, not the build toolchain.
See [Distribution and Updates](Distribution_And_Updates.md).
The source registry normalises supported native and instruction-only `SKILL.md` packages into source-qualified, digest-pinned descriptors.
It cannot infer trust from a package name, location in the user profile, or roaming metadata.
See [Skill Sources and Roaming Storage](Skill_Storage.md).

## Copilot Integration Proof

Before building the product around Copilot SDK, implement a small disposable integration test that demonstrates:

1. Streaming output and explicit terminal/error events.
2. Disabling or intercepting built-in tools and automatic context collection.
3. Blocking a proposed tool before its side effect occurs.
4. Controlling initial and subsequent outgoing context, including tool results.
5. Detecting required endpoints, session storage, and diagnostic content handling.
6. Cancellation and suppression of late results.
7. Supported authentication without exposing credentials to the model.
8. Version-specific behaviour recorded in automated adapter conformance tests.
9. Independent management session progress while task execution is blocked, without cross-session context, tool, or approval leakage.

Use a mock side-effect tool and distinctive synthetic context markers.
Evidence must show rejected actions never executed and rejected markers never entered outbound model requests.
If supported SDK hooks cannot establish that, the affected capability is unsupported.
Do not monkey-patch undocumented internals or claim that a UI approval compensates for an unmediated SDK path.

## Evolution

Use dependency-injected interfaces and typed protocols internally; do not expose private application services directly to extensions.
Version adapter protocols and skill schemas independently.
Add providers and connectors only after they pass the same applicable lifecycle, egress, and tool tests.
