# Model Providers, Memory, and Grounded Knowledge

Status: accepted product direction, bounded provider-neutral host controls and volatile provider-policy/review workflow
implemented, reviewed user-memory admission policy implemented, and remaining proposed architecture. Provider
qualification, durable user memory, knowledge ingestion, retrieval, and hosted
handoff described here are not complete or advertised by the current
bootstrap.

Related: [Architecture](Architecture.md),
[Local Inference](Local_Inference.md),
[Runtime and Provider Feasibility](Runtime_Provider_Feasibility.md),
[Human Interaction and Persistent Sessions](Interaction_And_Sessions.md),
[File and Folder Ingestion and Grounded Reasoning](File_And_Folder_Ingestion.md),
[Security and Data Flows](Security_Data_Flows.md), and
[Implementation Roadmap](Implementation_Roadmap.md).

## Product Decision

Kora supports both qualified local inference through Ollama and separately
qualified hosted inference through Copilot. It does not require every user or
session to use both.

Provider choice is explicit and host-owned:

| Session mode | Initial provider | Hosted handoff |
|---|---|---|
| Local only | Qualified Ollama model | Prohibited |
| Local first | Qualified Ollama model | Kora may offer an exact, reviewed handoff; the user decides |
| Hosted preferred | Qualified Copilot destination | Used for admitted turns under the configured remote-processing policy |

**Local first** is the recommended default after both providers are qualified.
**Local only** remains a complete supported mode rather than a degraded
hosted session. A user may also choose a provider explicitly for a session or
new task.

One provider processes one model turn. Kora does not silently blend providers,
race them, or switch midway through a request. A local-to-hosted transition is
a new admitted turn with a new context envelope, destination decision, and
visible provenance.

Provider selection never changes tool authority. Ollama and Copilot receive
the same admitted tool descriptions and return proposals through the same host
authorization, approval, execution, result-sanitization, audit, cancellation,
and session-ownership boundaries.

## Local-First Handoff

Kora does not use a model's self-reported confidence as authority to disclose
content or select a hosted provider. Model confidence is not reliably
calibrated and can be confidently wrong.

Kora may offer hosted handoff only after an observable, typed condition such
as:

- the selected local runtime or required capability is unavailable;
- the complete admitted request cannot fit the qualified local context budget;
- required structured output repeatedly fails host validation within a fixed
  retry budget;
- a host-owned acceptance check establishes that the local result is
  insufficient;
- the task requires an explicitly hosted-only capability; or
- the user asks to use Copilot or requests a hosted second opinion.

A handoff offer states why local processing could not continue, identifies the
hosted destination, and previews the exact content classes and knowledge
excerpts proposed for transmission. The user can approve, decline, remove
items, continue locally with reduced scope, or start a separate hosted
session.

Provider unavailability, authentication failure, quota, timeout, cancellation,
and denied egress are explicit outcomes. Kora never converts them into a
success-shaped local or hosted fallback.

## Context Is Not Learning

Kora distinguishes four mechanisms that are often described as "memory":

| Mechanism | Lifetime and authority |
|---|---|
| Turn context | The bounded request sent to one provider for one model turn |
| Session history | Durable user-visible messages, tasks, decisions, and evidence associated with one Kora session |
| User memory | Small, deliberate, inspectable facts or preferences retained by Kora for later sessions |
| Knowledge sources | User-configured files or folders indexed locally and retrieved as cited evidence |

Ollama inference does not update model weights from conversations. Restarting
Ollama does not preserve conversation knowledge unless Kora retained and
selected it. Continuous training or fine-tuning is not the mechanism for
personal facts, preferences, project decisions, or source documentation.

Kora owns context selection for every provider. Provider conversation state,
SDK transcripts, caches, and native memory features cannot become a second
authority or an unreviewed durable store.

## User-Governed Durable Memory

Durable user memory is optional application data, not hidden model learning.
The model may propose a candidate memory, but only Kora can admit it.

Examples include:

- a preferred response style;
- an explicitly named project or role;
- a stable user-approved workflow preference; or
- a user instruction to remember a decision across sessions.

Kora does not automatically convert ordinary conversation, inferred traits,
credentials, health information, secrets, transient tasks, or model-generated
claims into durable memory.

Every memory records a Kora-owned identity, value or typed payload, source
lineage, creation time, scope, review state, and retention/deletion state.
Users can:

- say or select **Remember this**;
- review the exact proposed memory before admission;
- list, inspect, edit, disable, and forget memories without a model;
- see when a memory was used for a request; and
- prevent a local memory from being sent to a hosted provider.

Memory scopes are explicit:

- **Session**: remains part of that session and follows session deletion.
- **Device profile**: may be selected for later sessions under the active
  Windows profile.
- **Project or knowledge-source scope**: applies only when that exact
  Kora-owned scope is active.

Memory retrieval uses host policy and typed scope before relevance. A
semantically similar memory from another session, profile, project, or
revoked source is not eligible. Unknown scope or ownership fails closed.

Memory content lives under paths supplied by `IApplicationDataPaths` and uses
the durable storage/lifecycle boundary, not provider storage or preference
JSON intended for simple settings. Security-sensitive configuration continues
to use its authoritative domain policy and audit path rather than becoming a
free-form memory.

### Delivered Reviewed Memory Admission - 2026-10-09

The first dependency-safe memory package delivers a provider-independent
[Core domain and policy](../src/Kora.Core/Memory/MemoryPolicy.cs) and an internal
[Application admission workflow](../src/Kora.Application/Memory/MemoryAdmissionService.cs).
It is a bounded production-code foundation exercised by deterministic doubles,
**not composed durable memory or a newly exposed command/tool/UI feature**.
It does not modify the active R14 immutable artifact/detail implementation,
desktop presentation, SQLite schemas, storage paths, providers or model turns.

The domain has strong Kora-issued memory/profile/project/source identities,
explicit Session/DeviceProfile/Project/Source scopes, request and session-generation
lineage, exact optional source revision, proposal origin, creation time, memory
revision, review receipt and Pending/Enabled/Disabled/Forgotten retention.
Core is the single authority for candidate validation, scope/lineage eligibility
and exact review-receipt applicability. These records are data, not executable
capabilities or persistence/egress approvals.

The host workflow implements these exact transitions:

| Operation | Required state and result |
|---|---|
| Propose | Host resolves scope/lineage and issues identity/revision 1; User or Model candidate remains Proposed/Pending, without review, use or persistence |
| Review | Original local user input reviews the exact current Proposed revision; accept creates a Reviewed receipt bound to that revision and complete current boundary; reject records Rejected |
| Admit | Exact current Reviewed/Pending revision, receipt and unchanged review boundary become Admitted/Enabled |
| Edit | Exact non-forgotten revision replaces the value and request lineage; clears the receipt and returns to Proposed/Pending, immediately closing prior use |
| Disable | Exact Enabled revision becomes Disabled; neither read nor Admit implicitly re-enables it; explicit edit/review/admit is required |
| Forget | Exact non-forgotten revision removes candidate and review receipt; a content-free identity/lineage tombstone is retained only in this volatile workspace; it cannot be edited/reused |
| Use | Re-resolves exact identity/revision and checks eligibility before any relevance operation; returns exact IDs, revision, scope, candidate, source/request lineage, review receipt and consuming host request for Local only |

Model candidates cannot supply memory identity, scope, lineage, a review receipt,
retention, destination or authority. They carry only an untrusted typed content
class and value. HostSystem/model callbacks cannot review, admit, edit, disable
or forget. The dependent native/control routes must capture fresh explicit
original user input through the existing host control-intent boundary; they
must never convert a model response, apparent approval or candidate class into
a user review. There is no model-facing mutation descriptor in this package.

Only explicitly reviewed facts, response preferences, workflow preferences and
decisions are eligible content classes. Unknown/undefined classes, Credential,
Secret, Health, InferredTrait, TransientTask and ModelClaim have explicit denial
outcomes. Candidate labels are not sensitivity proof: the user must review the
exact content and classification against known lineage. No heuristic scanner
or model self-classification is claimed to establish that text is safe.

Values are never truncated or normalized. The ceilings are **512 UTF-16 code
units**, **1,024 strict UTF-8 value bytes** and **2,048 bytes for the complete
serialized candidate including class, field names and JSON escaping**.
Null/blank/control-bearing/ill-formed Unicode values fail closed. The volatile
workspace has **128 total identities**, including forgotten tombstones, with
no implicit eviction or identity reuse. Capacity is an explicit outcome,
not a durable-retention policy.

All operations require live host-resolved Activity/request identity, current
host ownership and control/privacy revision, exact authoritative active session
and generation, and a host-resolved profile/source observation. Unknown owner,
privacy, scope, session, lineage or destination is denied. An async authoritative
read is followed by cancellation/lifecycle/control/source revalidation and a
serialized in-process publication fence. Concurrent late callbacks re-resolve
the current revision at that fence. Pending proposals/reviews cannot move to
another session/generation; admitted profile memories can be considered in
later sessions only within the same exact profile and still-current source
lineage. Project/source memories require their exact active scope/revision.

The lifecycle callback clears session-scoped candidate/receipt content and
invalidates in-flight publication; device/project/source entries remain
independent. The dependent package must bind this callback to authoritative
session retirement/deletion and resolve scope observations under the existing
native ownership/privacy/session fences. Inactive/changed-generation sessions
already fail admission. Disposal clears the workspace and suppresses late
publication. These are conceptual/process-local lifecycle semantics, not
durable deletion, forensic erasure or removal of snapshots already returned.
Every later use still revalidates the current record; old snapshots confer no
authority.

Required typed requested/terminal audit precedes state/use publication.
Authority/cancellation changes during an audit callback suppress publication
and append a truthful denial/cancellation terminal observation. Audit/read
failures are explicit exceptions, not defaults or successful admissions.
Source-generated diagnostics include only operation/outcome/reason/host memory
ID and fixed failure type. Existing versioned policy Activities preserve host
request/session/task correlation; retirement-related audit/diagnostics use causal
links, not a fabricated live parent. No memory content enters logs, tags or
Baggage.

**Dependent delivery remains open:** durable store/schema/migration/restart,
atomic storage/audit and native authority leases, supplied application-data
paths, lifecycle inventory and copy disposal, original-input CRUD/review/list
UI/tools, current-use/provenance visibility and retention controls. Retrieval
ranking, prompts/context attachment, hosted handoff, knowledge ingestion,
embeddings and providers are not implemented here. Local retention and even a
successful local use receipt never authorize Hosted or an unknown destination.
There is no persistence adapter and no implicit read/write to preferences,
session history, provider memory or a model context.

Experiment disposition: reviewed storage, Node/.NET final-request/runtime
lifecycle and local-inference evidence remains retained. Deterministic admission
tests do not supersede unique actual SQLite atomicity/recovery/capacity/copy
disposal, SDK/native memory/session I/O, failed-result serialization, egress,
quiescence, account or offline/quality measurements. No experiment or historical
receipt is removed, relabelled or claimed wholly migrated.

## Folder-Backed Grounded Knowledge

Users can configure one or more named files or folders as managed knowledge
sources and can attach a source revision to one session. The detailed
admission, identity, refresh, citation, lifecycle, and Windows path contract is
owned by
[File and Folder Ingestion and Grounded Reasoning](File_And_Folder_Ingestion.md).

Large data sets are not copied wholesale into a model prompt. Kora builds and
uses a provider-independent local retrieval pipeline:

```text
Reviewed source roots
    -> bounded local discovery and extraction
    -> immutable admitted source revision
    -> structure-aware chunks and local indexes
    -> lexical and optional semantic retrieval
    -> host reranking, diversity, and context budgeting
    -> immutable cited evidence envelope
    -> selected local or hosted provider
```

The original files remain authoritative. Snapshots, extracted text,
embeddings, and indexes are derived data that retain source and revision
identity and can be rebuilt or deleted.

### Retrieval

The initial useful delivery uses deterministic lexical search over UTF-8 plain
text and Markdown. Lexical retrieval remains available for exact identifiers,
versions, error messages, acronyms, and code-like terms.

Optional semantic retrieval adds a qualified local embedding model and vector
index. Hybrid retrieval merges lexical and semantic candidates, deduplicates
them, applies source and scope filters, and reranks them before enforcing the
runtime context budget. The generation model and embedding model are separate
qualified dependencies.

Embeddings are sensitive derived content, not anonymous metadata. Kora records
the embedding model identity and digest, dimensions, normalization, chunking
and index schema versions. An incompatible change causes an explicit bounded
rebuild; it never silently mixes vectors from different models or schemas.

For broad questions, Kora may perform bounded iterative retrieval: an initial
search, a host-validated request for more evidence, and a constrained
follow-up search. The model never receives an unrestricted filesystem or index
handle.

### Markdown and Images

Markdown extraction preserves headings, lists, tables, code fences, links,
image references, and human-meaningful locations. Structure-aware chunking
uses section boundaries with bounded overlap rather than arbitrary prompt-size
splits.

Images are admitted separately from surrounding Markdown:

- initial support retains authored alt text, captions, and nearby text;
- later OCR may extract text from screenshots and scans;
- later qualified local vision processing may produce a clearly labelled
  machine-derived description; and
- optional image embeddings require their own model, quality, privacy,
  lifecycle, and retrieval evidence.

OCR and generated descriptions are derived evidence and can be wrong. Kora
does not present them as authored document text. Embedded resources, links,
scripts, macros, and remote images are never executed or automatically
fetched during indexing.

### Incremental Indexing

Indexing is asynchronous, cancellable, resource-bounded, and separate from
interactive reasoning. Kora:

- reuses unchanged content by digest;
- reprocesses changed or newly admitted items;
- removes deleted, excluded, inaccessible, or revoked items from active
  retrieval;
- atomically publishes only internally consistent revisions;
- reports partial or failed indexing instead of plausible empty results;
- records parser, chunking, embedding, and index versions; and
- reconciles registered sources because file-watcher notifications, if added,
  are hints rather than authoritative state.

## Local and Hosted Knowledge Use

The local index and retrieval service are shared by both providers. Provider
native uploads, vector stores, conversation attachments, or retrieval plugins
do not replace Kora's source identity, scope, citation, retention, egress, and
deletion controls.

For Ollama, selected excerpts remain on-device:

```text
Local sources -> local extraction/index/retrieval -> Ollama
```

For Copilot, Kora still retrieves locally and transmits only the admitted
evidence envelope:

```text
Local sources -> local retrieval -> destination/egress review -> Copilot
```

Each source has an egress classification:

- **Local only**: no source content or derived content may be sent remotely.
- **Ask for this request**: Kora previews the exact revision/excerpts before
  hosted use.
- **Hosted eligible**: the source may participate in a hosted proposal, but
  every outbound envelope still passes destination, purpose, sensitivity,
  session, and current-policy checks.

Registering a folder, enabling Copilot, or using a source locally is not
standing permission to upload it. If required evidence is local-only while a
session uses Copilot, Kora offers a local turn or reports the policy conflict;
it does not omit the evidence and produce an apparently grounded answer.

## Evidence and Answer Contract

Evidence is structurally separate from instructions and chat history. Each
selected item carries:

- Kora evidence, source, revision, item, and excerpt identities;
- a human-meaningful relative location such as heading, page, or line;
- extracted content and authored-versus-derived classification;
- freshness and access observations;
- hosted disclosure eligibility; and
- the content digest and extraction/index versions.

The model cites Kora evidence IDs. Kora validates cited IDs against the exact
request envelope and renders safe links to admitted local sources. A provider
cannot invent a local path, widen a source, or decide which file Kora opens.

Answers visibly show the provider used, sources searched, citations, and any
stale, excluded, inaccessible, partially indexed, or insufficient-evidence
state. Kora distinguishes source-backed statements from model inference and
does not expose raw similarity scores as answer confidence.

## Component Responsibilities

- `Kora.Core` owns provider-mode policy, provider-neutral request/response and
  evidence contracts, memory/source identities and scopes, eligibility,
  citation validation, context budgets, and lifecycle rules.
- `Kora.Tools` owns one action per admitted memory/source/search/read operation
  over consumer-focused Core contracts. It owns no arbitrary filesystem or
  provider escape hatch.
- `Kora.Definitions` owns bundled provider-neutral grounding instructions and
  prompt resources after their runtime support is qualified. It owns no
  execution, egress, memory, or session authority.
- `Kora.Application` owns provider selection, handoff workflow, context
  assembly, memory and evidence retrieval, indexing orchestration, egress,
  audit, cancellation, and presentation state.
- `Kora.Windows` owns Windows picker/path/file identity, reparse and access
  observations, bounded native reads, and platform-specific parser/OCR
  mechanisms.
- `Kora` composes providers and presents session mode, provider provenance,
  memory review, knowledge management, indexing health, handoff, excerpt
  review, citations, and recovery.

Provider adapters implement qualified mechanisms. They do not decide whether a
request, memory, tool result, or knowledge excerpt may be disclosed.

## Security Boundaries

Memory and retrieved content are untrusted data. Text such as "ignore previous
instructions", an apparent approval, or a tool command cannot become system
policy, user intent, a grant, or execution authority.

Kora applies:

- current Windows profile, session ownership, privacy, source, and provider
  revalidation before selection and transmission;
- strict separation of system policy, user intent, evidence, memory, tool
  results, and model output;
- secret/sensitivity detection as a warning or denial control, never proof
  that remaining content is safe;
- content-minimizing structured diagnostics and trusted typed security audit
  events;
- deterministic deletion inventories for memories, snapshots, chunks,
  embeddings, indexes, temporary files, and cached envelopes; and
- truthful remote outcomes when transmission may have occurred before
  cancellation or failure.

Provider provenance, model confidence, a document statement, or a previous
answer cannot establish identity, permission, approval, or destination
authority.

## Delivery Sequence

This direction is delivered through existing roadmap packages rather than a
parallel model subsystem:

1. **Provider-neutral host controls**: R04-R08 establish durable identity,
   authority, tool/result mediation, context/egress control, qualified Ollama,
   and qualified Copilot adapters.
2. **Explicit session modes and handoff**: R08/R10/R12 expose Local only,
   Local first, and Hosted preferred with provider provenance and no silent
   fallback.
3. **User-governed memory**: R10/R12/R14 add proposal, review, scoped storage,
   retrieval visibility, editing, and deletion.
4. **Text knowledge foundation**: R26.1-R26.3 add reviewed source revisions,
   Markdown/plain-text extraction, lexical retrieval, citations, and qualified
   local reasoning.
5. **Hosted grounded reasoning**: R26.4 applies exact excerpt/destination
   review through the admitted Copilot adapter.
6. **Richer retrieval and media**: R26.5 independently qualifies hybrid/vector
   retrieval, additional formats, OCR, and later vision support.

Each slice remains unavailable until its own implementation, dependency,
licence, privacy, resource, real-provider or real-device, failure-path,
deletion, and acceptance evidence passes.

### Delivered R04-R08 Host Controls - 2026-10-09

The first dependency-safe package is a composed, **adapter-unavailable** host
boundary, not a second model subsystem or provider qualification.
[ModelTurnHost](../src/Kora.Application/Dependencies/ModelTurnHost.cs) reuses
the existing host-resolved Activity/request, authoritative session workspace
and task observations, current ownership/privacy/control revisions, trusted
typed security audit, six-ID read-only registry and `LocalModelResponse`
contract. Core owns its provider/model/turn identities, provenance, typed
outcomes and immutable context/evidence envelope. Application owns admission,
single-use dispatch, revalidation and late-completion observation. No new
Windows mechanism, provider package, credential, UI command or product mode is
enabled.

Implemented boundaries:

- A host-issued turn binds exactly one provider, opaque catalogue model ID and
  exact profile revision to one request/session/task. A caller cannot replace
  the envelope or provider, replay the capability, borrow it across hosts or
  sessions, or reuse its expired/completed issuing Activity.
- Admission requires a live host context, current host/privacy/control state,
  an active matching session generation and exact nonterminal current-source
  task authority. Dispatch, tool requests and response publication revalidate
  those boundaries. Missing, foreign, expired, future-dated or revoked context
  is denied; required audit/storage failures are surfaced, not defaults.
- Context separates host policy, user request, evidence and the existing
  admitted read-only descriptors. Evidence has a unique host evidence ID,
  exact request and revision, immutable text and explicit disclosure class.
  Unknown disclosure fails closed. At most **16** evidence items and
  **32,768 complete serialized host-envelope UTF-8 bytes** are admitted;
  JSON escaping and catalogue overhead count. Nothing is silently truncated.
  The provisional structured response is bounded to **4,096 serialized UTF-8
  bytes**, reusing the existing result limit. These are host transport ceilings,
  not a qualified token window or permission to send a 256 KiB clipboard
  snapshot. Native-provider protocol overhead/final-request mediation still
  needs its own exact-profile qualification.
- A single-use capability dispatches one adapter once. There is no retry,
  alternate-provider lookup, blending, confidence-based handoff, memory write
  or destination parameter. Provider-reported identity never establishes
  provenance. The initial response subset admits bounded answers only;
  action/grant/question payloads cannot acquire authority through this new
  boundary. Existing bootstrap proposal handling is unchanged.
- During an admitted dispatch, tool proposals use the **same** six-ID registry,
  strict input checks, typed handlers and bounded all-status result mediation.
  No new tool, lane authority, content-reading descriptor or effect is added.
  Tool access closes when dispatch completes or cancellation/authority changes.
- Unavailable, authentication-required, quota-exceeded, timed-out, cancelled,
  denied-egress, denied and unknown are explicit typed outcomes without a
  success-shaped response or fallback.
- The host signals a **15-second** dispatch deadline and promptly closes output
  and tool authority on timeout/cancellation. An uncooperative adapter keeps
  the single dispatch slot occupied until actual completion; new dispatch is
  unavailable and disposal waits for quiescence. Late output is suppressed,
  faults are observed and completion uses a causal Activity link, not a retired
  parent. Cancellation is not rollback, provider cessation or remote deletion.
- Requested/terminal typed audit and source-generated structured diagnostics
  carry host turn/provider/model/revision identity, not content, paths,
  provider error text or incoming trace authority. Policy and runtime
  Activities contain only admitted host identifiers and provider class.

**Exact enablement gates remain open.** Production registers Ollama and Copilot
with no adapter and no qualification evidence. The internal mechanism seam is
exercised only by deterministic fakes. Local admission requires the exact
R02-L1-L5 owner-reviewed candidate selection **and** R06/R07/L6 integrated-host
proof for that catalogue revision. Hosted admission requires exact .NET
final-request profile conformance, RT2 all-path lifecycle/egress proof,
execution PV1 account/destination admission and integrated-host proof. Expired
or partial evidence is unavailable. Even a fully attested hosted profile is
denied here because exact destination/content egress approval is not implemented;
`HostedEligible` evidence is not transmission consent.

This package preserves current bootstrap readiness/unavailable messages,
buffered local reasoner and exact offline controls. It does not upgrade that
bootstrap to a qualified neutral adapter. Native Local only, Local first, Hosted
preferred settings, hosted disclosure, durable memory and local/hosted grounded answering
remain disabled planned product modes; the policy/workflow increment below does not enable them.

Experiment disposition: the Node runtime, actual .NET RT1, RT2 lifecycle,
MG1 management and local-inference proofs were reviewed against this boundary.
Maintained tests exercise production host-envelope bytes, identity/isolation,
typed results, audit, registry reuse, revocation, deadlines and late-response
links. They do not exercise SDK-native final JSON, failed-result/history
serialization, runtime/session storage, retry transports, provider accounts,
real answer quality, offline network denial or server cessation. Those unique
assertions, recorded artifacts and rejected hook-only witness remain retained;
no experiment assertion is claimed migrated and no experiment is removed.

### Delivered Provider Policy and Exact Handoff Workflow - 2026-10-09

The [Core policy](../src/Kora.Core/Dependencies/ModelProviderPolicy.cs) and
[host consumer](../src/Kora.Application/Dependencies/ModelTurnHost.Policy.cs)
implement volatile session-bound LocalOnly, LocalFirst and HostedPreferred
semantics, with explicit Default/Local/Hosted choices for each new turn.
LocalOnly never offers hosted disclosure; LocalFirst defaults local and requires
exact review for a hosted choice; HostedPreferred defaults hosted but still
fails closed without independent egress authority.

An original LocalUi/ActivatedVoice host request with a matching active session
and committed nonterminal task can publish the next policy revision. Replacing
policy invalidates old review bindings and admitted turns. Unknown
mode/choice/provider, empty model/session IDs, invalid revisions and unknown
qualification bits are rejected by the
[shared selection validation](../src/Kora.Core/Dependencies/ModelProviderSelection.cs),
[policy](../src/Kora.Core/Dependencies/ModelProviderPolicy.cs) and
[qualification gate](../src/Kora.Application/Dependencies/ModelProviderRegistration.cs).

The [composed workflow](../src/Kora.Application/Dependencies/ModelProviderHandoffWorkflow.cs)
reuses HostQuestionService and its atomic original-intent/question/audit
transaction. A [host-issued offer](../src/Kora.Application/Dependencies/ModelHandoffOffer.cs)
exposes the exact immutable context, destination/catalogue revision, typed
observable reason, question key/revision, session generation, task revision and
privacy/ownership control revision. Approve, decline, cancel, expired and stale
outcomes are typed; removing exact evidence IDs retires the old offer and creates
a reduced envelope requiring fresh review. No confidence field, default answer,
grant or model-selected Kora identity is accepted.

The workflow can offer review after the
[audited local Unavailable terminal result](../src/Kora.Application/Dependencies/ModelTurnHost.Policy.cs)
of a host-issued policy-bound turn; it does not automatically dispatch another
provider. Other [typed reasons](../src/Kora.Core/Dependencies/ModelHandoffReason.cs)
must come from host-owned capability/budget/acceptance or explicit-user decisions,
not model text. One-use review is bound to the original user/request, exact
context object including evidence lineage, exact policy and destination revision,
validity interval and live authority observations. Changes before confirmation,
after question commit, during terminal audit or before hosted admission fail
closed ([focused tests](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.Policy.cs)).

The complete serialized envelope, including JSON escaping, evidence provenance
and tool catalogue overhead, remains bounded to 32,768 UTF-8 bytes and 16 evidence
items; no truncation occurs
([envelope](../src/Kora.Core/Dependencies/ModelContextEnvelope.cs),
[exact-limit tests](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.Policy.cs)).
HostedEligible is classification, not permission; LocalOnly evidence cannot
enter an offer. Required audit failures throw and return no new policy/review
capability; cancellation and late callbacks cannot acquire one
([workflow tests](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.Policy.cs)).

**Boundary:** confirmation is neither runtime qualification nor a final-request
egress receipt. Production still composes no adapters or qualification evidence,
and even qualified test registrations plus approved review return DeniedEgress
at the [turn host](../src/Kora.Application/Dependencies/ModelTurnHost.cs).
No account/model acquisition, network send, RT1/RT2/PV1 proof, memory
storage/retrieval, prompt ranking or native provider-settings/review UI is
delivered. The generic native question surface cannot itself issue this
workflow's review capability. Durable session policy awaits dependent
native/persistence integration; no shared schema is changed. Bootstrap behavior
is unchanged.

Experiment disposition remains **retain**, with no executable or receipt
removal. The [RT1 final HTTP boundary](../experiments/r02-dotnet-control-proof/RequestBoundary.cs),
[RT2 actual native lifecycle observations](../experiments/r02-runtime-lifecycle-proof/LifecycleTests.cs)
and [MG1 actual-runtime envelope/admission](../experiments/r02-dotnet-management-proof/Envelope.cs)
have different subjects from this host policy workflow. The
[runtime preparation consumer](../eng/RuntimeValidation.Common.ps1) still
verifies and derives the historical profiles; RT2/MG1 share RT1 fixtures, and
Node remains the rejected hook-only witness. Unique native/session-I/O/retry/
account/local-floor/offline procedures and outstanding gates remain in the
[inventory](Implementation_Roadmap.md#experiment-disposition-inventory).
Deterministic policy tests do not establish exact SDK/native equivalence or
close R08/D-014 acceptance.
