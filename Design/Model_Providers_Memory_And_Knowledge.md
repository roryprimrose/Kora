# Model Providers, Memory, and Grounded Knowledge

Status: accepted product direction and proposed architecture. Provider
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
