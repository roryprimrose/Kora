# Architecture and Contracts

Status: proposed. Contracts below describe required semantics, not an existing SDK API.

Related: [Extensibility](Extensibility.md), [Security and Data Flows](Security_Data_Flows.md), [Task Lifecycle](Task_Lifecycle.md).

## Runtime Ownership Decision

The bounded native inspector has a consumer-focused
[`ICommittedAuthorityAuditReader`](../src/Kora.Core/Storage/ICommittedAuthorityAuditReader.cs)
seam on the existing interaction store. Its **AuthorityAudit** source is
explicitly separate from diagnostic evidence and mirrors; it admits only
passive committed typed audit observation through host/private-profile access.
It owns no writer or execution policy. See the
[source, snapshot and limitation contract](Information_Display.md#delivered-bounded-native-evidence-inspection).

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
- Tools (`Kora.Tools`): portable host-owned built-in action implementations, grouped by capability folder/namespace with one class per registered action and shared cohesive brokers. Tools references Core; Application references Tools. Neither Windows/presentation nor provider SDK dependencies belong in Tools. See [implementation guidance](Commands_Tools_And_Skills.md#built-in-tool-source-layout-and-implementation).
- Definitions (`Kora.Definitions`): immutable bundled behavior definitions/resources, grouped as Skills and, when implemented, Prompts, Instructions and Agents. Definitions references Core, not execution/presentation/provider modules. Skill catalogue loading and exact resource embedding are delivered; agent profiles/runtime support are not. See [definition guidance](Commands_Tools_And_Skills.md#bundled-definitions-and-agent-profiles).
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
  |-- Evidence Query / Ask Evidence -- Session / Log / Audit Projections / Daily-File Adapter
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
| Context broker | Immutable clipboard/file/source snapshots, provenance, classification, bounded retrieval and context selection | Implicit background collection, arbitrary path access, or treating an index as authority |
| Knowledge source service | Reviewed file/folder source registration, immutable revisions, refresh, format admission, citations, revocation and inventoried derived-data deletion | Original-file mutation, ambient filesystem monitoring, model-selected roots, or destination/egress approval |
| Policy/approval service | Resource-scoped grants, outbound decisions, approval tokens | Trusting model-produced permission claims |
| Runtime adapter | Provider session and event translation | Unreviewed tools, undisclosed egress, global policy |
| Tool gateway | Validate, authorise, invoke, bound, and audit tools | Giving an adapter unrestricted OS access |
| Response presenter | Streamed display, optional speech text, typed rich detail, summary, citations, TTS | Treating partial answers as completed work or untrusted content as controls |
| Content viewer/rendering service | Bounded Markdown/diagram/static HTML rendering, browser provenance/navigation/isolation | Browser automation, implicit context capture, arbitrary renderer plugins or host bridges |
| Proactive interaction broker | Event eligibility, speech timing, deduplication, prompt identity, trusted maintenance dialogue routing | Autonomous tool execution or treating untrusted content as system events |
| Security audit service | Correlated content-minimizing request/outcome events for writes, process/script execution, protected operations, and approvals | Authorizing an action, storing raw content/arguments, or treating diagnostics as tamper-evident evidence |
| Evidence query/reasoning service | Deterministic list/read/search over permitted log and audit records, bounded cross-source correlation/citations, and orchestration of user-requested evidence reasoning through the context broker; explicit retention/gap/source status | Arbitrary SQL/path access, changing source records, treating evidence as instructions/authority, uncited conclusions, or sending an unreviewed cross-source result to a remote model |
| Speech policy service | Central playback eligibility, call-state freshness, voice-configurable preferences, one-shot overrides | Claiming universal call detection or allowing lock/mute bypass |
| Speaker confidence service | Separately consented local per-SID frequent-speaker adaptation, protected learned/enrolled profiles, quality/verification observations, optional privacy-policy signal | Authenticating a learned frequent speaker, granting actions, satisfying approvals, ambient/history training, exposing scores/templates, or silently replacing enrollment |
| Reserved intent/lifecycle controller | Exact local control routing, target disambiguation, named confirmations, serialised app/power/maintenance lifecycle | Arbitrary shell commands or user-skill shadowing of privileged controls |
| Storage services | Configuration, metadata, private-profile session history/artifacts/indexes, migration/deletion | Raw audio, plaintext credentials, unrestricted clipboard collection, or silent eviction |
| Environment setup controller | Internal storage/schema initialisation, capability probes, approved dependency setup, ownership and readiness | Arbitrary model-supplied installers or changing Kora code |
| Configuration service | Option schema, validation/scope, revision-safe persistence, voice/UI parity, effective settings | Arbitrary config-file patches or weakening mandatory policy |

Speech decisions are revalidated at playback start and when policy/detector state changes.
Private speech decisions also revalidate current speaker-confidence availability and result; missing or uncertain verification never defaults to owner.
Learned frequent-speaker attribution is personalization, not owner authentication; [Security](Security_Data_Flows.md#optional-local-frequent-speaker-learning) owns consent, adaptation, isolation, deletion, and optional verification boundaries.
Communication detector adapters expose observations, not authority; see [Call-Aware Speech](Call_Aware_Speech.md).
Every user preference, including admitted extension settings, uses [User Configuration](User_Configuration.md); voice and UI share validation and persistence.
The [window design](UI_Workspace_And_Windows.md) defines shell roles and shared view state: workspace/compact share UI selection, independently opened detail references stay immutable, and response/draft updates route by stable session/task/item IDs.
This is not shared provider conversation state or permission to dispatch; the registry/interaction/scheduler remain authoritative.
Rich presentation follows [Information Display](Information_Display.md):
portable contracts classify immutable content origin/profile/identity,
application orchestration admits and revalidates it, Avalonia owns native
Markdown/source presentation and the bundled language-highlighting service,
and Windows owns isolated browser integration.
Shared passive presenters do not turn session, skill, settings, or permission
management into web content; native approvals, mutations, validation, and trust
indicators remain outside rendered content.
Native questions and first-run/device-loss recovery follow [Interaction Fallback](Interaction_Fallback.md) and require no working microphone, model, or rich renderer.

## Activity Tracing and Evidence Correlation

Use `System.Diagnostics.ActivitySource` throughout Kora's host-owned code. The
stable sources are `Kora.Core`, `Kora.Application`, `Kora.Windows`, and
`Kora.Desktop`, versioned with their assemblies. Configure W3C IDs before
application composition. Activity names are stable low-cardinality operation
names such as `session.request`, `task.dispatch`, `approval.decide`,
`tool.invoke`, `storage.commit`, and `evidence.query`; never place user text,
paths, session titles, targets, or model output in an activity name.

Activities describe causal operations, not durable business identity:

- Create a root activity for each accepted UI/voice/system request, startup or
  recovery operation, scheduled dispatch, retention run, and explicit evidence
  query/reasoning request. Use child activities around meaningful policy,
  runtime/provider, tool, storage and presentation boundaries rather than every
  method.
- Do not keep one activity open for the lifetime of a Kora session. A session
  spans many traces and restarts. Stamp the stable host-owned `SessionId` on
  every session-bound activity and logging scope, with `TaskId`, `InvocationId`,
  `ApprovalId`, and audit `CorrelationId` where applicable.
- Use parent/child context for nested work. Capture context at enqueue/admission
  and use `ActivityLink` for deferred work, fan-out, reconciliation, or work
  caused by multiple prior operations; do not fabricate a parent or keep a
  completed activity open. Restart creates a new trace linked through durable
  Kora IDs, never by reviving an old span.
- `Activity.Current` flows through ordinary async calls. Queue/IPC/runtime
  adapters must capture and restore or link the admitted context explicitly.
  Propagate W3C context outside the process only through an admitted adapter and
  normal destination/egress policy. Incoming trace data is correlation only,
  never identity, intent, permission or authority.
- Do not use `Activity.Baggage` for Kora/session identities or content because
  baggage may cross provider boundaries. The host stamps allowlisted typed tags
  and `ILogger` scopes from trusted context at each boundary.

Every diagnostic and audit `ILogger` call captures the current activity at call
time, before asynchronous sink buffering. Both database tables and daily JSON
contain `TraceId`, `SpanId`, optional `ParentSpanId`, trace flags, activity
source/name/kind, and host-owned session/task/invocation/approval/correlation
IDs where applicable. `CorrelationId` remains the durable domain identity that
pairs audit request/terminal records; it does not replace W3C trace identity.

Persist completed local span metadata in private-profile `activity_spans` and
`activity_links` projections under diagnostic retention: trace/span/parent,
source/name/kind, start/end, status, allowlisted typed tags and durable Kora
IDs. Evidence records retain their own trace/span/session fields even if the
span projection expires. The UI then reports an expired/missing trace segment
rather than inventing it. No remote telemetry exporter is enabled by this
contract; adding one requires a separate destination/privacy decision.

The R04 partial foundation implements the four versioned sources through
[HostActivity](../src/Kora.Core/Diagnostics/HostActivity.cs), fresh host-resolved
request routing, audit policy spans and deferred request/terminal audit links.
[EvidenceLoggerProvider](../src/Kora.Application/Diagnostics/EvidenceLoggerProvider.cs)
captures a bounded formatter-independent envelope and trusted context at call
time; arbitrary tags, scopes, provider IDs and lookalike markers cannot select
host columns or the typed audit route. Missing context is reported as an
explicit gap, not silently promoted to a bootstrap classification. Completed
spans/links have separate contracts and daily-file copies.

This is not the complete instrumentation or persisted graph. R04 now composes
the private standard-SQLite task and evidence partitions for exact local
`ShowVersion` requests from typed or activated-voice input. Independent typed
log/audit/span/link records retain call-time host/W3C context and independent
due dates. The daily-file provider remains independent. Required capture,
file or database delivery failures report gaps and propagate rather than
allowing dispatch or a success receipt with missing terminal audit evidence.
The interaction store now supplies bounded session authority/metadata and exact
task controls; full conversation/history UI and a general durable executor remain open.
Ordinary diagnostics that lack host context are retained only with an explicit
capture-owned `MissingHostContext` gap and `kora.bootstrap=false`; their trusted
host/W3C/business columns remain null. This preserves existing content-free
startup/UI/audio diagnostics without fabricating a request or bootstrap
classification. Caller marker properties cannot choose this classification.
They cannot produce an audit, span or task receipt. Host-bearing mismatches and
required audit/task admission still fail; they are never downgraded to gaps.
The [R04 delivery inventory](Implementation_Roadmap.md#r04-foundation-delivery)
tracks actual source/tests and downstream boundaries. File audit copies
remain diagnostic evidence, never authorization or durable receipt proof.

The bounded R04/R14 [durable evidence query](../src/Kora.Application/Diagnostics/DurableEvidenceQuery.cs)
now composes one host-owned read-only service over those actual SQLite
log/audit/span/link tables. The native current-user inspector requires a live
local-UI request, proven desktop ownership and private-presentation admission.
Session/task/trace fields are correlation filters, not a way to select host
authority. Signed continuations bind the original query, viewer session,
15-minute expiry and per-table snapshot ceilings bound to their original ordinary
evidence identities; later query diagnostics and completed spans cannot expand
an in-progress snapshot. Removal/reuse of a ceiling invalidates its cursor
explicitly and requires a fresh query.
The [Windows reader](../src/Kora.Windows/Storage/WindowsSqliteEvidenceReader.cs)
reuses the sink's exact envelope/projection validation and private database
schema/ACL/reparse/journal admission, then opens SQLite read-only. It creates
no store, repairs no permissions, recovers no hot journal and changes no schema.
Up to 50 records and 64 KiB of the actual serialized page include citations,
cursor, source availability and disclosure. Selective text/property searches
scan at most 4,096 candidates per page, with explicit continuation/scan-limit
status and the existing five-second SQLite progress deadline.
Expired-but-present backlog and missing-or-removed segments are distinct.
Bounded ordinary diagnostic pruning is implemented below; a complete retained
graph/history is not claimed. Session/conversation
sources and interaction-audit receipts are not supplied by this projection.
Model tool exposure, Ask Evidence, export and remote transmission remain gated.

Explicit **AuthorityAudit** instead reads actual committed typed schema-v3
interaction-store audit rows through the shared connection lease and immutable
sequence ceiling. That consolidated store owns task/question/required-audit
transactions; its validated frozen legacy ledger is not queried as live authority.
These passive reads neither mutate authority nor reconstruct historical payloads,
a causal graph or forensic tamper resistance. **All** remains SQLite diagnostic
evidence-only and **CombinedLog** remains ordinary diagnostics-only. See
[the exact committed source contract](Information_Display.md#delivered-bounded-native-evidence-inspection).

The independent **DailyLog** source now reads existing daily JSON diagnostic
envelopes beneath `IApplicationDataPaths.LocalRoot/Logs`, with the writer's
shared exact daily-name policy and version-1 diagnostic serializer/validator.
`All` still means the existing SQLite projection; it does not merge file copies
into database counts or claim an atomic cross-source ledger. Explicit opt-in
**CombinedLog** selects only SQLite ordinary logs plus DailyLog ordinary
records, preserving each original source-qualified citation, envelope ID and
provenance. It pairs the existing immutable SQLite ceiling with the independently
captured host-held daily prefix, never an atomic cross-sink snapshot. Ordering
is source-major: SQLite commit time/evidence ID, then exact daily name/byte
offset. Observation time is not reinterpreted as database commit time; no
deduplication, causal ranking or file-derived span/link graph is introduced.
List/search/cited reads share the existing filters, original expiry and complete
50-record/64-KiB serialized budget. Time filters retain source semantics
(SQLite commit time versus daily observation time). Both original sources are
admitted and verified on each page, even when only one contributes that page;
an unavailable, corrupt, changed, removed or expired included source yields
no fallback content. Its source status is explicit; permission/identity and
malformed/foreign/tampered cursor failures remain fail-closed and require an
explicit fresh search after recovery. Independent retention still applies.
**All**, individual sources, audit citations and old continuations are unchanged.
File names are discovered by the trusted
store, never accepted as query paths. Read-only file handles reuse current-user
owner/ACL and reparse admission, validate their final path, and retain volume/
file identity. There are no directory/file/lease writes or permission repairs.

Each daily snapshot admits at most 32 files, an 8-MiB earliest byte prefix
ordered by exact daily name and byte offset, 4,096 physical lines, and 256-KiB
lines excluding LF. A five-second cancellation/deadline bounds each read.
Prefix capture and final verification each read at most 8 MiB (16 MiB total
source I/O); no unbounded tail or full-file read is implied. A byte ceiling
stops at the last complete LF, reports `ScanLimitReached`, and does not page
beyond that ceiling. Too many files report the same limit without a subset
success. Eight host-held manifests expire after 15 minutes or earlier bounded
cache eviction; signed continuations retain the original query/viewer binding.
Every page reopens the original names and verifies file identities and prefix
SHA-256 digests before returning any content. Appends/new days cannot expand
that snapshot; changed/replaced, pruned/rotated, expired, corrupt, truncated,
unavailable and timed-out sources are explicit, with no partial-success
fallback. Snapshots are not an OS-atomic filesystem ledger.

Daily citations are source-specific hashes of file identity, byte offset and
exact line digest, not aliases for SQLite citations. Provenance also retains
the envelope evidence ID. Typed fields and admitted envelope correlation are
validated, not reconstructed from outer rendered Serilog properties.
Observation time, event name, exception type and redacted typed scopes are
preserved; database commit/due times are absent and
retention is `RetentionUnknown`. File audit mirrors are unsupported and counted
separately, never returned as authoritative audit rows. Legacy/unstructured/
activity copies and ingestion-gap markers have explicit counts and `Partial`
status; file trace navigation keeps DailyLog selected and reports the activity
graph unavailable. User-modifiable file correlation never establishes identity,
intent, permission, an audit commit, execution outcome or a complete history.

## Independent Management and Concurrent Sessions

The work manager stays responsive independently of the task runtime's event loop, tool calls, and approval waits.
Management uses a separate session and cancellation/deadline scope; it does not inherit executable tools or ambient task context.
It proposes typed ledger operations that the host validates and commits atomically.
The admitted M/E power tools submit host lifecycle proposals only.
The deterministic host lifecycle controller owns all-session review,
approval/countdown and fixed-action dispatch through the admitted gateway/worker
under [Management Power Proposal Authority](Security_Data_Flows.md#management-power-proposal-authority);
neither model lane owns approval or gains task execution from a proposal.
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
First-launch consent, ordinary restart, unlock/resume and loss recovery use the
[canonical microphone matrix](Security_Data_Flows.md#microphone-consent-and-enablement-matrix);
saved consent and live capture enablement are separate.
A local detector recognises the configured active names ("Kora" initially) without sending ambient audio to transcription or a model.
The host-owned profile/alias registry and atomic detector switching follow [Custom Activation Names](Activation_Name.md); names are data-only settings, not replacement executable code.

On detection, the controller emits activation feedback and captures the command, preserving words spoken immediately after the wake word.
Local voice activity detection ends the command. Only activated command audio
or one answer captured in a host-opened bounded conversational reply turn
reaches the local transcription engine.
The wake word is excluded from the task request.
Optional push-to-talk enters the same capture path without requiring the wake word.

The host question service can request an Awaiting Conversational Reply
generation only for one explicitly presented foreground question/revision and
only while all microphone/call/privacy/session gates remain valid. That
generation expires after the configured speech-start wait and cannot route a
new general request. See [Interaction and
Sessions](Interaction_And_Sessions.md#conversational-voice-turns).

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

### Local Inference Qualification

The [R02 technical outcomes and plan](Local_Inference.md) are the qualification
input for the local adapter, not an implementation of this runtime protocol.
The pinned Ollama/qwen candidate has public identity/licence/download metadata
and observed missing-runtime/unavailable behavior; actual answer quality,
CPU-floor resources, effective context, server cancellation and no-egress
success remain unproved. Preserve explicit unavailable states and no remote
fallback while those gates are open.

The bootstrap identifies a responding Ollama version and pins the model digest;
the experiment additionally requires exact runtime `0.35.1`. R06/R10 must
record a tested compatibility envelope rather than treating any version string
as qualification or replacing a pre-existing runtime automatically.
The host must budget the entire approved request against the tested context
window: the current 4,096-character cap is not a token limit, and advertised
32,768-token model capacity is not an accepted Kora envelope.

The current reasoner buffers one JSON response with a 512-token prediction
bound and a two-minute failure deadline. Experimental streaming/CPU/context
overrides do not change production capabilities or prove user-visible latency.
R08 must implement and qualify answer deltas, backpressure, thinking/output
boundaries, cancellation and late-event rejection under this protocol.
R06/R07/R08 use the [R02-L5 envelope and L6 handoff](Implementation_Roadmap.md#r02-local-inference-continuation),
then repeat affected proofs on the actual host. Loopback transport confinement
is supplementary to independent OS-enforced offline evidence, not its substitute.

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

The store uses standard SQLite for configuration, session/events, and task/action metadata under verified private user-profile permissions.
The owner-approved D-009 baseline supersedes mandatory page encryption and database-key/rekey admission.
Transactional persistence, schema validation/migration, journal/backup deletion and failure behavior remain delivery gates.
Machine-local configuration/enablement/audit storage is under `%LOCALAPPDATA%\Kora`.
Kora-specific declarative skill packages are under the Windows Roaming AppData folder at `%APPDATA%\Kora\Skills`.
Shared profile skill roots are registered read-only sources, not writable storage.
Credentials are represented by opaque references to an OS-protected credential store.
Large retained artifacts use a private managed store linked by immutable digest, session ID, and deletion ownership.
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

The [R02 worker outcomes and continuation gates](Security_Data_Flows.md#r02-windows-containment-outcomes)
reject Job-only restricted execution and retain capability-free AppContainer as
a partial candidate. The separate [W2 outcome](Security_Data_Flows.md#r02-w2-dependency-and-fixed-script-outcome)
rejects ACL/no-child exact-dependency profiles while demonstrating a synthetic
in-memory helper/entry effect. The owner accepts best-effort transitive tracking
for bundled/future scripts and responsibility for script actions; manifest-listed
internal files remain exact, grant-bound and reviewable in read-only tabs.
This is not OS containment. Network denial, protected runtime/deployment,
complete helper/receipt/lifecycle admission and real fixed controls remain
unresolved under D-013; no production worker or native broker is composed.

### Windows Durable Storage Direction

The owner-approved baseline is **standard SQLite with verified private
LocalApplicationData permissions**, using the existing pinned Microsoft.Data.Sqlite
and SQLitePCLRaw `e_sqlite3` closure. Database encryption, database DPAPI keys
and rekey workflows are not required. This protects the Windows account
boundary, not same-user/admin access or copied files. Credentials remain in
OS-protected credential storage. Copies outside the private location are
readable and must be disclosed as such.
SQLite is a required application component, not an optional provider, external
server, user-installed prerequisite, or capability that can be disabled. Every
supported binary package must carry its admitted managed provider and native
engine for the package architecture. Source builds acquire the pinned
packages during restore; end users are never asked to locate or install SQLite.
The startup storage probe validates the bundled component, permissions, database and
schema. It is a health/migration gate, not an optional dependency setup task.
Missing or unloadable native SQLite assets are a broken installation and fail
durable session/evidence capabilities explicitly; Kora never downloads a
replacement at runtime, searches the machine for an ambient SQLite library, or
falls back to an ambient/system engine.
Windows is the only supported product OS; Linux runtime support and local Linux-host validation are outside this storage proof.
Existing Linux-hosted CI building Windows artifacts remains unchanged and does not imply Linux product support.
[D-009](Decision_Register.md#d-009-session-persistence-and-retention) owns selection status and remaining gates; the [reproducible R02 evidence](../experiments/r02-storage-proof/README.md) supports, rather than replaces, this contract.

R04 adds host contracts and a bounded standard-SQLite task-store implementation
with actual private-folder/file checks and transactional versioned host records.
Its original `HostStorageV1` partition has no Keys directory or DPAPI dependency.
The bounded task-control continuation migrates and freezes that ledger into
the existing interaction authority store; it is then an inert required handoff
receipt, not a second authority or worker execution source.
New managed files receive explicit current-user ownership and a protected,
user-only DACL at atomic creation, before any content is written; token-default
ownership is not trusted, including on elevated Windows runners. Existing
files are verified, never silently repaired. The database and rollback journal
are privately pre-created; each connection uses `PERSIST` journaling with
`synchronous=FULL` so normal commits/reopens retain the owned journal rather
than recreating it with a different default owner. A missing managed journal
requires explicit recovery, not automatic replacement. Maintained
[production interruption/reopening tests](Implementation_Roadmap.md#r04-production-store-interruption-and-reopening---2026-10-07)
now exercise actual PERSIST/FULL hot-journal and owned-process interruption
semantics, not physical power-loss or installed acceptance. Earlier uncomposed
prototype databases without this journal are not silently migrated.
The store is composed for the bounded exact local version-query milestone,
not for arbitrary model/skill/OS effects. Earlier
internal key/artifact primitives remain uncomposed and are not database prerequisites.
The production composition does not use the unavailable store. The actual
task and evidence stores fail explicitly; they do not replace missing/corrupt existing data, use application
envelopes as a SQLite substitute, or open/migrate the user's legacy database.
The bootstrap setup probe remains unchanged and must not receive content or
authoritative host evidence. Ordered intent/dispatch/receipt and bounded
Interrupted/Unknown recovery are defined by
[HostTaskCoordinator](../src/Kora.Application/Hosting/HostTaskCoordinator.cs);
store commits require a live matching host request and there is no replay/
executor callback. [DurableVersionQuery](../src/Kora.Application/Hosting/DurableVersionQuery.cs)
orders intent, required request evidence, dispatch, local query return,
required terminal evidence and receipt. This proves only the bounded local
query returned, not an OS effect or completion of subsequent speech playback.
[DurableHostRecovery](../src/Kora.Application/Hosting/DurableHostRecovery.cs)
admits correlated recovery evidence before committing Interrupted/Unknown,
uses fresh traces joined by durable host IDs and never invokes an executor.
Startup processes at most 100 incomplete records; remaining work fails startup
explicitly rather than silently ignoring the excess.
First-use greeting, settings and version response disclose the readable-copy,
same-user/admin and independent 30/90-day due dates, bounded ordinary startup
pruning and remaining audit/task/session deletion limitations. The new
version-query records contain no transcript or answer body.

#### Bounded Ordinary Diagnostic Retention

[WindowsSqliteDiagnosticRetention](../src/Kora.Windows/Storage/WindowsSqliteDiagnosticRetention.cs)
runs one batch per admitted owner startup, after exact storage admission and
durable task recovery, before the UI lifetime opens. It requires a live
host-system activity and opens only the existing `EvidenceStorageV1` partition.
There is no timer, passive-query refresh, automatic backlog drain, storage
replacement, permission repair, schema change or retention-setting rewrite.
No new prerequisite is needed: existing policy-assigned effective `due_utc`
values, due indexes, bounded span/link envelopes and the actual shared
writer/reader lease supply admission and serialization.

At a fixed UTC cutoff, `due_utc <= cutoff` selects at most 128 ordinary
`application_log_events` and 32 `activity_spans`, ordered by due date/rowid.
Their at-most-1,024 validated owned `activity_links` are removed before their
spans in the same PERSIST/FULL transaction. An unexpired span's links are never
independently removed. The existing five-second lease/SQLite progress bounds,
cancellation before COMMIT, private ACL/reparse/schema/integrity/envelope checks
and deterministic resource disposal remain enforced. Existing full-store
validation also runs under that deadline; large or invalid stores can fail
admission rather than bypass validation. A verified COMMIT returns exact
removed counts and a due-backlog flag; subsequent startups can continue.
Failures propagate to the independent startup/file error path. The generated
structured completion event is emitted after releasing the storage lease
under a typed Windows `retention.run` child activity, never an audit.

This is logical ordinary-row pruning, not forensic erasure or a strict
at-all-times 30-day cap. Retained references distinguish expired-but-present
backlog from missing-or-removed targets without inventing deletion provenance.
Audit due dates and sequences remain unchanged, including expired audit rows;
`security_audit_events`, interaction audit/hash chains, tasks, questions,
sessions and all grants/Perpetual records are outside this operation.
Audit continuation anchors/pruning, session retention/deletion, configurable
apply-now, artifact/backup disposal and full R04/D-009 acceptance remain open.
The later [future-only diagnostic setting](User_Configuration.md#delivered-bounded-future-only-sqlite-diagnostic-retention-r10r04)
admits integer 1–365/default-reset30 after required audit/atomic readback/receipt.
It supplies one coherent deadline per new ordinary SQLite transaction only.
Semantic schema v2 migrates validated legacy 30-day rows without rewriting
rows/deadlines; writer/private-reader validation agree. The writer remains
insert-only, so old span IDs cannot upsert fresh retention. Audit90/domain,
files30/30, all authority and pruning triggers are unchanged; unavailable
ordinary policy reports explicit independent gaps rather than becoming default
or blocking required trusted audit on activity disposal.

The independently delivered [future-only audit setting](User_Configuration.md#delivered-bounded-future-only-audit-retention-r10r04)
adds a domain-owned 30–365/default-reset90 snapshot to both real
`WindowsSqliteHostInteractionStore.AppendAudit` commits and
`WindowsSqliteEvidenceSink` diagnostic audit projections. Composition shares
one confirmed current-run audit snapshot; no authority is inferred from the
projection. Separate audit-control admission reuses the original-input/durable-
intent mechanism and existing committed-intent connection, never nested leases.
Requested/terminal preference receipts retain the prior policy; atomic
save/readback and durable intent outcome precede marker confirmation/activation.
Invalid/unconfirmed audit configuration holds required new commits and refuses
startup authority recovery/writes, not an implicit 90-day fallback.
Schema-3 authority and independently qualified evidence readers validate
original integral audit-domain deadlines, preserving serialized payloads,
ordered hashes/head, revisions and citations across reopen/legacy migration.
No deadline rewrite, audit pruning, session/history/grant/task/question/approval
deletion, ordinary/file-policy change or janitor scheduling is introduced.
This does not complete R04/R10 or forensic/encryption/disposal qualification.

The subsequent bounded [interaction/session-authority slice](Implementation_Roadmap.md#r04r05-durable-interaction-and-minimal-session-authority---2026-10-06)
adds `InteractionStorageV1/interaction.db` using the same private owner/ACL/
reparse, PERSIST/FULL and exact-schema checks. It stores only host-admitted
typed questions/options/drafts/answers, exact proposals/grants and minimal
session authority, not general conversation/context content. Production
registration binds the existing R05 services to
[WindowsSqliteHostInteractionStore](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.cs).
No native input route, generic dispatcher or OS effect is activated.

Task intent is a committed prerequisite, not an eventual diagnostic.
The bounded task-control continuation consolidates task/event, question,
session/grant and authoritative audit authority into schema v3 of
`InteractionStorageV1/interaction.db`. Task and interaction adapters share
one existing private lease and connection through COMMIT. Interaction state and its authoritative
typed security audit commit in one database, with ordered hashes/head and
record-digest bindings. There is no attached multi-database write, fallback
receipt or authority inferred from the independent evidence/file projections.
There is no nested task/interaction lease acquisition. Fresh host snapshots
are admitted under the shared lease, with optimistic
revision checks and transactional content revocation. Previous adapter-run
observations confer no restart authority.

Validated v1 metadata maintenance precedes v2-to-v3 consolidation. The legacy
`HostStorageV1/host.db` ledger is validated and durably frozen before copying
its complete identities/events into one destination schema transaction.
It is retained as an inert migration receipt, never an alternate execution
source. Interrupted consolidation leaves the old destination schema intact;
reopening revalidates the frozen source and completes only storage migration.
Missing/corrupt destination authority cannot be rebuilt from that retired
snapshot. Evidence projections and retention remain independent. See
[the bounded control contract](Interaction_And_Sessions.md#bounded-authoritative-task-observation-and-pre-dispatch-cancellation).

Session generation persists across Active restart and advances on Done,
resume and authority removal. Perpetual records are a separate table without
a session foreign key or grant due/retention field. Removed session identities
remain tombstoned; this is not full recoverable-copy content deletion.
The [bounded logical disposition](Interaction_And_Sessions.md#delivered-bounded-exact-id-logical-disposition---2026-10-08)
uses host-held native preview/confirmation and the authoritative shared
transaction, never view-model-only removal. Exact idle-state/revision checks,
live-row removal, generation/tombstone, terminal control receipt and trusted
audit share one COMMIT. The task writer and interaction admission reject
late appends; no recovery receipt is needed after a committed disposition.
Latest-session audit validation rejects missing/stale session rows, including
lost tombstones. Metadata validation permits absent previously committed names
only behind validated Removed authority. Task/events, independent Perpetual/audit/evidence
and inert legacy storage survive. No artifact/backup inventory, journal/free-page
rewrite, full conversation deletion or forensic erasure is delivered.
Audit due times use the existing independent audit policy; pruning/anchors,
whole-store rollback detection, installed/power-loss acceptance and broader
R12 lifecycle/UI/retention remain open. Missing/corrupt schema, journal, audit
or private permissions fail explicitly without replacement or repair.

The bounded [production interruption continuation](Implementation_Roadmap.md#r04-production-store-interruption-and-reopening---2026-10-07)
originally used those adapters and exact version-1 schemas; current maintained
tests also qualify consolidated schema v3. Maintained disposable
Windows tests terminate only their owned helper processes before or after
task intent/dispatch/terminal/recovery, evidence envelope/span-link, and
interaction approval/Once-consume/Done commits. Precommit checkpoints force
real pager writes and verify the rollback journal's hot header, not merely
its retained PERSIST file size. Reopening preserves prior receipts and private
journal permissions; a committed receipt is not converted to cancellation.
Interrupted recovery may leave another truthful Unknown audit attempt, but
never replays work or invents a successful effect. Missing/permissive journals
and inaccessible or unowned storage remain explicit admission failures.
These are process-interruption tests, not installed or physical power-loss
guarantees, general artifact/backup recovery, or complete R04 acceptance.

Implementation requirements:

- Use the maintained pinned standard-SQLite distribution with reviewed provenance/licences and release-native closure. The rejected encrypted candidates are historical evidence, not admission blockers for this baseline. Installed native loading remains distribution evidence, not proof of database confidentiality.
- Pin the managed provider, native engine and initialization mode as one release-owned dependency closure. Publish and installer manifests must include the exact native asset for each offered architecture; package restore/build tools are development inputs, not runtime acquisition paths.
- Keep content-bearing FTS, summaries, indexes, journals and managed backups inside the private storage boundary. Use memory-only temporary storage and never trace SQL parameters or record content into diagnostics. Backups/exports outside that boundary are readable, not encrypted.
- Fan every content-minimising `ILogger` event to two independent providers: the retained daily JSON file sink and the private SQLite logging provider. The file stream remains available for database open/migration/permission/commit failure, fatal crash and recovery diagnosis. Failure in one provider must not recursively invoke it or silently suppress delivery to the other.
- Both SQLite tables are structured logging stores, not rendered-line archives.
  Preserve a common formatter-independent `ILogger` envelope: stable evidence
  ID, UTC observation time, numeric/name event ID, level, logger category,
  original message template, bounded typed property object, bounded structured
  scopes, W3C trace/span/parent context, activity source/name/kind, host-owned
  session/task/invocation/approval/correlation IDs, and optional approved
  exception type/code fields. Preserve property
  kinds such as null, Boolean, integer, real, string, GUID and timestamp rather
  than coercing every value to text. A rendered message may be retained for
  display/full-text search, but it is derived and never replaces the template
  or typed values.
- Route ordinary `ILogger` records into a dedicated `application_log_events`
  table with promoted/indexed correlation, session, task, invocation and other
  admitted high-value fields in addition to the structured envelope. Route
  records carrying the host-owned `SecurityAudit=true` marker into a separate
  `security_audit_events` table, not the ordinary log table. Audit rows preserve
  the same structured logging envelope and add fixed typed columns for append
  sequence, correlation/category/action/outcome/initiator/target, optional
  approval ID and bounded reason code. Do not reconstruct properties or
  authoritative audit columns by parsing rendered message text.
- Canonical structured-property serialization is schema-versioned, bounded and
  culture-invariant. Unsupported values fail or use an explicitly registered
  safe projection; they are not silently stringified. Apply the same
  content-minimisation/redaction policy before either file or database
  serialization, and never persist duplicate raw objects outside that policy.
- Use common W3C activity and durable Kora identity projections across
  `application_log_events`, `security_audit_events`, `activity_spans`,
  `activity_links` and session records so queries can join causally related
  observations and all evidence for one session without collapsing schemas or
  authority. Preserve source-specific payloads, indexes, access rules and
  retention rather than flattening every record into one generic table.
- Apply independent database retention policies: diagnostics default to 30 days under their configurable bounded schema; audits default to 90 days and permit 30-365 days. Persist each row's effective due time. Session deletion, search and reasoning do not refresh or collapse these policies; perpetual grants remain independently retained.
- Query retained tables/files through one host-owned evidence service with bounded list/read/search, stable citations and explicit unavailable/expired/gap markers; never expose arbitrary SQL or paths. Search uses permitted indexed fields inside private storage.
- “Ask Evidence” is an application workflow over that service, not unrestricted database access. It retrieves a bounded, reviewable set of permitted log/audit/session records through the context broker, invokes the selected eligible reasoning runtime, and returns claims with exact evidence citations and explicit inference/uncertainty. Local reasoning is the default. A remote runtime requires preview and approval of the exact selected records under normal egress policy. Missing reasoning does not disable deterministic list/read/search.
- The security audit `ILogger` provider commits `security_audit_events` synchronously at the policy boundary and reports failure to the host-owned audit service; a daily-file copy alone cannot permit consequential dispatch. Ordinary `application_log_events` ingestion may batch asynchronously, but bounded-buffer exhaustion, write failure or recovery must produce an explicit loss marker in the file stream and later database gap evidence rather than silent loss or pressure that prevents audit commits.
- Transactionally commit ordered intent/decision evidence before consequential dispatch and link observed receipts afterward. Use FULL-synchronous durability, then prove recovery on the admitted engine. Recovery restores readable interrupted/unknown evidence, never fresh execution authority or automatic replay.
- Validate versioned identity/role-bound artifacts and immutable digests. Stage, flush and publish before committing references; recover staged, orphan and missing/corrupt referenced files explicitly. Bound sizes. Digests detect accidental corruption, not same-user tampering.
- Verify supplied profile-local paths and restrictive effective folder/file ACLs for every managed copy. Missing/corrupt data or permissions fail visibly without replacement data, permission repair or shared-path fallback.
- Migrate supported schemas transactionally or through a separately verified candidate, preserving recoverable originals on failure. Reject unknown schemas; the existing bootstrap setup ledger is not a legacy conversation database.
- Define deletion ownership across rows, indexes, caches, staging, artifacts, journals and managed backups. Prevent late appends and preserve unrelated content. Checkpoint, VACUUM and `secure_delete` are hygiene, not forensic-erasure guarantees.

The approved standard-SQLite choice is explicit, not a silent downgrade.
Independent tamper-evident security evidence remains D-008 work. SQLite/ACLs
do not detect an authorized user's restoration of an old valid database.
Local unlink cannot erase exports, provider copies, filesystem snapshots,
SSD remnants, OS paging or third-party dumps. Historical encrypted R02
fixtures do not prove the current implementation or hardware power-loss durability.
The [acceptance criteria](Acceptance_Criteria.md#persistence-configurable-lifecycle-and-deletion) govern admission and later integration.

### Profile Boundary and Validation Responsibility

Durable content, managed backups and staging belong under the supplied running-user LocalApplicationData root, not shared/machine storage or the roaming declarative-skill directory.
The [bootstrap path resolver](../src/Kora.Core/Dependencies/ApplicationDataPaths.cs) already resolves LocalApplicationData for the current user; R04 must verify the entire production persistence path rather than assume every copy follows that resolver.
The storage proof deliberately uses owned synthetic scratch instead of opening that application directory.

For this profile-local architecture, ordinary cross-profile access isolation is a trusted Windows facility.
A second-account denial trial primarily corroborates that OS boundary and is optional, not a routine R02/R04 completion or PR-merge gate.
Kora's required evidence is its own integration: reject shared-path fallback, inspect effective folder/file ACLs, keep every managed copy scoped and fail visibly when the expected profile/permissions are unavailable.
Profile location alone is insufficient if the application creates permissive ACLs or exposes copies outside that boundary.

Revisit real multi-account testing before introducing shared storage, impersonation/service identities, cross-profile import/migration or custom cross-user authorization, or when observed permissions contradict the supported boundary.
Any such protection claim requires actual approved identities; neither mock identities nor a same-user roundtrip proves cross-user denial.
Same-user tools/workers remain a separate D-013 containment boundary; private-profile storage does not protect against code running as the same user or an administrator.
The file/database-only proof needs a loaded Windows user profile, not an unlocked physical desktop or local interactive input; it may run through a remote session while the console is locked.

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

### R02 Outcome and Candidate Integration Direction

The [R02 runtime/provider proof](../experiments/r02-runtime-proof/EVIDENCE.md)
exercised Node SDK 1.0.16 with runtime 1.0.90. Initial-context mediation,
successful-result mediation and isolated loopback lanes passed; **hook-only
failed-result mediation failed**. A supported experimental final model-request
handler blocked that unredacted continuation. The separate
[actual .NET RT1 fixture](../experiments/r02-dotnet-control-proof/EVIDENCE.md)
passes 45/45 real-runtime tests for the explicitly approved unmodified
exact-tag v1.0.16 source-built minimal HTTP/stdio profile with runtime
1.0.90/protocol 3. Final initial/history/all-result/exception mediation,
pre-effect denial, volatile session I/O and failure, streaming/auth errors,
truthful cancellation and execution/management isolation are measured.
Hook-only FAIL and Node evidence remain unchanged. Released NuGet byte
parity, global lifecycle egress/storage/diagnostics observation and
hosted-account eligibility remain blocked/open.
D-001/D-004 are not closed and no production adapter is enabled.

The separate [RT2 lifecycle fixture](../experiments/r02-runtime-lifecycle-proof/EVIDENCE.md)
records exact final SDK/native byte verification/reproduction, 45/45 unchanged
staged RT1 regressions and 20/20 bounded RT2 tests plus two locale contracts.
Its all-path gate is **Blocked**: native PowerShell/console-host initialization
and transient file activity are visible, but user-mode snapshots/watchers
cannot attribute/control every native network, file or diagnostic path.
Observed shutdown and positive detection controls are not a sandbox or
zero-native-egress/persistence proof. The user retained this fail-closed
boundary and deferred privileged tracing to separately approved dedicated-host
work. R08 remains disabled; MG1/PV1 and R04 host authority/audit integration
retain their independent handoffs and gates.

The next candidate must combine pre-effect tool authorization, host-sanitized
results for every outcome, and a final gate over each complete serialized
model request before transmission. Prompt/tool hooks alone are insufficient.
Minimal runtime configuration and host-owned session I/O require observed
enforcement; an empty tool list does not prove no ambient collection or
persistence. Abort acknowledgement is not physical stop or rollback.

[Runtime and Provider Feasibility](Runtime_Provider_Feasibility.md) owns the
technical continuation: preserve scoped RT1 pins and regressions, then RT2
lifecycle network/storage/diagnostic observation and MG1 .NET management
envelope, followed by approved provider trials, with explicit
stop/decision paths for unsupported controls.
The [separate MG1 released-profile proof](../experiments/r02-dotnet-management-proof/EVIDENCE.md)
now repeats all 45 RT1 controls on explicitly approved released SDK 1.0.16
bytes, then passes the .NET envelope against unchanged RT1 native bytes.
It does not rewrite source-built RT1 evidence or assert artifact equivalence.
RT2 must qualify the applicable profile; account eligibility, R04 durable
authority/storage and R13 task-slot/resource admission remain open.
The [roadmap gates](Implementation_Roadmap.md#r02-runtimeprovider-follow-up-gates)
must pass before R08 remote exposure or R13 model-assisted management.

## Evolution

Use dependency-injected interfaces and typed protocols internally; do not expose private application services directly to extensions.
Version adapter protocols and skill schemas independently.
Add providers and connectors only after they pass the same applicable lifecycle, egress, and tool tests.
