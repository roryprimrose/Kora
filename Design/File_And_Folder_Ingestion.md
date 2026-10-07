# File and Folder Ingestion and Grounded Reasoning

Status: proposed design and roadmap refinement for R26. No file/folder
ingestion, managed knowledge source, index, retrieval tool, or hosted-model
transmission described here is implemented or advertised by the current
bootstrap.

Related: [Architecture](Architecture.md), [Security and Data Flows](Security_Data_Flows.md),
[Interaction and Sessions](Interaction_And_Sessions.md),
[User Configuration](User_Configuration.md),
[Internal Model Tools](Internal_Model_Tools.md), and
[Implementation Roadmap](Implementation_Roadmap.md#r26-file-and-folder-ingestion-delivery-plan).

## Product Outcome

A user can deliberately give Kora a file or folder containing material such as
team guidance or troubleshooting guides, see exactly what Kora admitted, and
ask questions whose answers are grounded in cited source content. The same
source-selection and retrieval contract serves qualified local models and
explicitly enabled hosted models.

Kora must distinguish:

- **Attach for this session**: capture an immutable revision for one Kora
  session. It follows that session's retention and deletion policy.
- **Add as a knowledge source**: register a named device-local source whose
  admitted revisions can be refreshed and used by selected sessions until the
  user disables or removes it.

Adding a source is not blanket permission to transmit its contents, execute
anything it contains, monitor the whole filesystem, or keep inaccessible
content indefinitely. Documents are untrusted data, including text that looks
like system instructions, commands, approvals, or skill definitions.

## User Experience

### Adding a source

The user starts from **Settings > Knowledge**, a session's **Add context**
control, a file/folder picker, an exact typed command, or an activated voice
request. Kora then:

1. Resolves the user-selected path through a trusted picker or a bounded path
   proposal. Relative paths and environment-variable expansion are not
   accepted.
2. Shows the canonical path, source kind, scope, supported file types,
   discovered file count/bytes, exclusions, and whether the source will be a
   session attachment or managed knowledge source.
3. Requires explicit confirmation before reading. A folder confirmation covers
   only the previewed canonical root and declared recursion policy.
4. Reads through a bounded snapshot operation, reports each unsupported,
   inaccessible, unstable, oversized, or failed item, and commits only an
   internally consistent admitted revision.
5. Shows source state, last successful refresh, indexed item count, exclusions,
   and recovery actions. Partial admission is never presented as complete.

A spoken path is a proposal, not authority. Kora reads it back and displays the
canonical result before confirmation. For long, ambiguous, punctuation-heavy,
UNC, or otherwise low-confidence paths, Kora asks the user to choose the path
with the native picker or explicitly use selected clipboard text. It never
guesses a path from unrelated clipboard, conversation, recently opened files,
shell history, or model output.

Initial voice examples:

- "Kora, attach a file to this session."
- "Kora, add a knowledge folder."
- "Kora, use `C:\Team\TSGs` as a knowledge source."
- "Kora, show my knowledge sources."
- "Kora, refresh the Team TSGs source."
- "Kora, stop using Team TSGs."
- "Kora, remove Team TSGs and delete Kora's indexed copy."
- "Kora, answer using Team TSGs only."

The assistant name remains configurable. These are semantic examples rather
than hard-coded phrases tied to the literal name "Kora". Native path selection,
review, removal, and recovery remain usable without voice or a model.

### Asking questions

Each question makes its source scope visible. The user can select one or more
attachments/knowledge sources, say "use Team TSGs", or accept a host-proposed
source set. Kora does not silently search every registered source.

The host retrieves bounded excerpts before invoking the selected runtime. An
answer must:

- cite the source, stable admitted revision, and human-meaningful location
  such as file-relative path plus heading, page, or line range;
- distinguish cited source claims from model inference;
- say when no admitted excerpt supports the answer;
- disclose stale, partially indexed, excluded, changed, or inaccessible source
  state that materially affects the result; and
- keep citations bound to the exact excerpts used for that answer.

Follow-up questions may reuse the same immutable admitted revisions while they
remain valid and in scope. Switching runtime, session, source revision, account,
or processing destination triggers a new context and egress decision rather
than silently continuing with the previous envelope.

### Managing sources

**Settings > Knowledge** and the session context surface show:

- stable source name and Kora-assigned ID;
- canonical root disclosed to the local user, but never logged or used as a
  model identity;
- attachment/managed kind and session binding;
- enabled/disabled state and permitted session scope;
- last attempted and successful refresh, current revision, file/byte counts,
  parser/index version, and freshness;
- excluded, failed, changed, deleted, or access-denied items;
- storage used by snapshots, extracted text, and indexes;
- **Refresh**, **Disable**, **Remove**, and **Remove and delete Kora copies**.

Disablement immediately prevents new retrieval. Removal revokes new use and
deletes derived copies through an inventoried background cleanup. Existing
answers remain historical evidence with citations marked unavailable when the
underlying retained excerpt is deleted. Removal does not delete the user's
original files.

## Initial Format and Scope Boundary

The first useful delivery is intentionally narrow:

- local fixed drives and explicitly entered UNC paths;
- one file or one canonical folder root;
- UTF-8 plain text and Markdown (`.txt`, `.md`, `.markdown`);
- deterministic decoding, heading/paragraph/line-aware chunking, lexical
  search, and bounded exact excerpts;
- manual refresh only; and
- local-model reasoning first, after the local runtime/context gate is
  qualified.

Folder discovery is recursive only after the preview states that fact. It does
not follow reparse points, symbolic links, junctions, mounted folders, or paths
that escape the selected canonical root. Hidden/system files, Kora protected
storage, credentials, source-control metadata, binaries, generated build
trees, and unsupported extensions are excluded by policy and reported.

Later format adapters may add PDF, Office Open XML, HTML, JSON, source code,
images/OCR, or email only after each parser has fixed identities, resource
bounds, hostile-input tests, licence review, and truthful location citations.
Renaming an unsupported file does not make it supported. Macros, scripts,
embedded objects, links, images, and document actions are never executed or
automatically fetched.

This initial boundary avoids making an embedding model, filesystem watcher,
Office installation, browser renderer, or cloud parsing service a prerequisite.
Semantic/vector retrieval is a later optional index strategy after its model,
storage, privacy, compatibility, and quality gates pass. Lexical retrieval
remains available and indexes never become the only retained provenance.

## Source Identity, Snapshot, and Refresh

A source ID is Kora-assigned and independent of its display name or path.
Each admitted revision records content-minimizing metadata:

- source ID, revision ID, session binding when applicable, and source kind;
- canonical root identity held in the private source registry;
- relative item identity, media type, byte length, last observed write data,
  and a host-computed content digest;
- extraction/chunking/index schema and implementation versions;
- admission time, refresh attempt/result, and access observation;
- exclusions and item-specific failures; and
- derived excerpt/chunk identities linked to the source item digest.

The snapshotter opens each admitted file without executing it, applies per-file,
per-source, count, depth, time, text, and chunk bounds, and verifies that the
file identity/content did not change across the read. A folder refresh builds a
candidate revision and atomically publishes it only after enumeration and
admitted-item extraction complete under the declared policy. If content keeps
changing, Kora reports an unstable source instead of combining inconsistent
files.

Refresh never expands beyond the registered canonical root or automatically
admits a new format. New and changed files are reprocessed; deleted, excluded,
or inaccessible items are removed from the active revision and queued for
derived-data deletion. A refresh does not extend session inactivity or source
retention merely because Kora observed filesystem activity.

No always-on watcher is part of the first delivery. A later scheduled refresh
setting must be finite, resource-bounded, disabled by default for network
paths, suspended during privacy/resource restrictions, and visible as Kora
background work. Watcher notifications are hints to rescan, never trusted
content or permission evidence.

## Retrieval and Context Budget

Retrieval is host-owned. The model receives neither arbitrary path access nor
an index handle that can widen scope.

1. Resolve exact admitted source and immutable revision IDs.
2. Revalidate source availability, session binding, enablement, access policy,
   and current user/profile ownership.
3. Retrieve candidates using the admitted index strategy.
4. Enforce diversity, excerpt, per-source, total-byte, and selected runtime
   context budgets.
5. Build an immutable context envelope containing excerpts and citation IDs.
6. Apply destination-specific egress policy.
7. Invoke the runtime and validate returned citation references.

Default numeric file, source, excerpt, and context limits remain unresolved
until R26 implementation measures them against the qualified local and hosted
runtime envelopes. They must be centralized typed policy, surfaced in preview
and settings, and tested at/over every boundary. The implementation may lower
limits under a verified device/runtime profile, but never silently truncate a
source while claiming complete ingestion.

The local runtime gets only selected excerpts, not a whole folder by default.
A hosted runtime additionally requires hosted models to be enabled, a
configured admitted destination, remote-enabled processing, and approval for
the exact outbound source revisions/excerpts under the egress policy. Enabling
a hosted model or registering a source is not standing consent to upload it.
Kora does not silently fall back from local to hosted reasoning.

## Security, Privacy, and Failure Behavior

- Treat file names, headings, metadata, links, and all extracted text as
  untrusted content, never user intent, a tool call, approval, grant, setting,
  or instruction hierarchy.
- Do not log raw paths, file names, excerpts, queries, answers, embeddings, or
  parser exception text. Structured diagnostics use safe source/revision/item
  IDs, counts, versions, stages, outcomes, and bounded reason codes.
- Typed security audit records cover source registration, scope change,
  hosted transmission decisions, disablement, removal, and terminal cleanup
  outcomes without storing content.
- Reject Kora application, installation, credential, private preference,
  session, audit, temporary, staging, or protected skill storage as source
  roots. A parent root that would include them is denied rather than silently
  carving out sensitive application state.
- Apply secret/sensitive-content detection before model context construction.
  Detection is a warning/denial control, not proof that remaining content is
  non-sensitive.
- Access failure, unknown ownership, privacy closure, changed identity, stale
  session binding, parser crash, corrupt index, or cleanup failure is explicit.
  Retrieval fails closed for affected items; it does not use an older copy
  unless an explicit source policy permits that exact retained revision.
- Cancellation stops enumeration, extraction, indexing, retrieval, and model
  transmission at their owned boundaries. A possibly transmitted remote
  payload is reported as unknown, not rolled back.
- Index corruption triggers explicit source unavailability and a reviewed
  rebuild from still-admitted source revisions. It does not return plausible
  empty results.

Local ingestion is not equivalent to local-only reasoning. Local-only mode
blocks hosted transmission and remote parser/embedding services, but still
requires the ordinary source, privacy, retention, and resource controls.

## Storage and Retention

Source registry metadata, immutable admitted text snapshots, extracted chunks,
lexical/vector indexes, and cleanup journals live under the private paths
provided by `IApplicationDataPaths`. Configuration uses the shared atomic
preference store; content-bearing source state belongs in the durable storage
and artifact partitions, not preference JSON.

Session attachments follow session deletion, source revocation, and live-work
holds. Managed sources persist until explicitly removed, subject to a
separately disclosed optional inactive-source cleanup policy. Source refresh,
retrieval, indexing, and browsing do not reset session inactivity.

Deletion inventories registry rows, snapshots, extracted text, embeddings,
indexes, temporary files, journals, managed backups, pending work, and cached
context envelopes. Completion is claimed only after all owned copies are
removed or each remaining failure is disclosed with a retry/recovery action.
Credentials and perpetual grants remain separate.

Embeddings are sensitive derived content. If later enabled, their source
revision, embedding model identity/digest, dimensions, normalization, and
index schema are recorded. Incompatible changes require an explicit bounded
rebuild; vectors are deleted with their source content and never treated as
anonymous telemetry.

## Settings and Voice Configuration

All options use the shared typed configuration service and the same UI, exact
command, and model-proposal policy described in
[User Configuration](User_Configuration.md). Proposed initial options are:

| Option | Default / boundary | Example verbal operation |
|---|---|---|
| Knowledge sources | None | "Show my knowledge sources" |
| Source enabled state | Enabled after successful confirmed admission | "Stop using Team TSGs" |
| Default new-source scope | Ask every time: session attachment or managed source | "Attach this only to the current session" |
| Folder recursion | On only when explicitly shown in folder preview | "Include subfolders for this source" |
| Refresh mode | Manual | "Refresh Team TSGs" |
| Permitted formats | Implemented fixed allowlist; not user-extensible | Read-only discovery, no verbal arbitrary extension admission |
| Per-file/source/context limits | Host defaults within verified bounds; user may lower, not exceed safety maxima | "Limit knowledge files to one megabyte" |
| Retrieval strategy | Lexical in the initial slice; later verified hybrid option | "Use lexical retrieval for this source" |
| Hosted source use | Ask for each exact outbound envelope; cannot be made Always by a convenience setting | "Use this source with the configured hosted model for this question" |
| Citation detail | File and heading/page/line location | "Show detailed citations" |
| Scheduled refresh | Off; later bounded interval only after watcher/background gates | "Refresh knowledge sources daily" |

Voice may propose registration, refresh, enable/disable, removal, source
selection, and safe limit changes. Destructive removal requires the same
reviewed source/revision scope through voice or UI. Voice-originated changes
remain subject to protected-call setting-origin restrictions. Voice cannot
add arbitrary file extensions, make Kora storage readable, disable secret or
egress controls, approve its own hosted transmission, or convert a displayed
document path into a source without a new deliberate request.

## Component and Tool Contract

The feature follows existing dependency direction:

- `Kora.Core` owns source/revision/item identities, format and limit policy,
  snapshot/retrieval results, citation contracts, and validation.
- `Kora.Tools` owns one action per admitted source/list/refresh/retrieve
  operation over consumer-focused Core contracts. It does not parse paths with
  Windows APIs or read the filesystem directly.
- `Kora.Application` orchestrates proposals, review, settings, indexing,
  retrieval, context budgeting, egress, lifecycle, audit, and presentation.
- `Kora.Windows` owns canonical Windows path/file identity, picker integration,
  reparse-point and ACL/access observations, bounded file reads, and native
  parser mechanisms that cannot remain portable.
- `Kora` owns Settings/Knowledge, session context, review, progress, recovery,
  and citation presentation.

Proposed execution-lane tools are:

| Tool ID | Purpose and boundary |
|---|---|
| `context.sources_list` | List permitted source descriptors and state, never raw content or an unrestricted filesystem view |
| `context.source_inspect` | Inspect one exact source/revision, exclusions, freshness, formats, bounds, and recovery state |
| `context.propose_source` | Create a host-reviewed file/folder proposal from deliberate user lineage; the model cannot confirm it |
| `context.refresh_source` | Refresh one exact registered source under current scope and access policy |
| `context.disable_source` / `context.remove_source` | Reviewed state change or inventoried deletion; no original-file deletion |
| `context.search` | Return bounded candidates and citations from exact admitted sources/revisions |
| `context.read_excerpt` | Read only a cited bounded excerpt selected by the host retrieval plan |

Path selection and source confirmation are host interactions, not model tools.
Management inference may receive content-free source readiness/count summaries
for routing but cannot search or read content. Provider-native file upload,
vector stores, retrieval, or conversation attachments do not replace Kora's
source identity, egress, retention, citation, and deletion controls.

## Acceptance Criteria

Before advertising the initial file/folder capability:

- UI-only, voice-only, typed, and mixed-channel flows register the same exact
  source through shared review and produce the same source/revision identity.
- File/folder/path limits, encoding, recursion, depth, item count, byte count,
  extraction, chunk, query, result, context, duration, and cancellation
  boundaries pass at and over each threshold.
- Traversal, reparse/junction/symlink escape, path alias/case, device/UNC
  changes, inaccessible files, files changing during read, partial folder
  failure, parser bombs, and restart/crash recovery fail visibly and safely.
- Prompt-injection fixtures cannot change settings, source scope, tools,
  approvals, system instructions, runtime, or destination.
- Answers cite exact admitted revisions and locations; unsupported answers,
  citation mismatch, stale/deleted items, and insufficient retrieval are
  visibly distinguished.
- Two sessions and two sources cannot cross content, citations, grants,
  context envelopes, or cleanup.
- Disable/remove/access loss prevents new retrieval immediately and deletes
  all owned derived copies through tested interrupted cleanup/recovery.
- Local-only trials produce no remote traffic. Hosted trials prove exact
  destination review, payload bounds, cancellation/unknown outcomes, and no
  silent local/hosted fallback.
- Index/parser upgrades preserve source identity, rebuild deliberately, and
  never serve mixed incompatible revisions.
- Logs, audit, activities, crash paths, and UI errors contain no raw content,
  path, query, answer, embedding, credential, or secret.

## R26 File and Folder Ingestion Delivery Plan

| Stage | Value | Dependencies | Completion evidence |
|---|---|---|---|
| R26.0 - Finalize policy and limits | Agree initial formats, numeric limits, storage/retention, citation shape, local-runtime envelope, and hostile-document fixtures | R04 storage semantics, R06 tool/result bounds, R10 configuration registry, R12 session retention | Approved typed contracts and threat/acceptance fixtures; no runtime capability |
| R26.1 - Native selection and immutable text snapshot | Add picker plus reviewed absolute path proposal, safe Windows enumeration/read, `.txt`/Markdown extraction, source registry, revision identity, native preview and deletion | R03 ownership/privacy, R04 durable storage/recovery, R05 review/questions, R10 settings | Real Windows file/folder/reparse/access/change/cancel/restart tests; no model exposure |
| R26.2 - Lexical retrieval and citations | Deterministic chunk/index/search/read-excerpt, source selection, context budget, citation presentation, refresh/disable/remove cleanup | R26.1, R06 admitted descriptors, R12 session/artifact lifecycle, R14 source/citation UI | Grounding, hostile-content, cross-session/source isolation, stale/revoked and interrupted-cleanup evidence |
| R26.3 - Qualified local-model reasoning | Send only selected cited excerpts through the admitted local runtime/tool loop and produce grounded answers | R26.2, R02-L5/L6, R07 context broker, R08 tool loop/streaming | Local-only network-blocked quality, context-limit, citation, cancellation and resource evidence on supported hardware |
| R26.4 - Hosted-model reasoning | Apply exact destination/source revision/excerpt review and hosted adapter egress; preserve citations and deletion boundaries | R26.3, admitted hosted provider under R08, remote-enabled policy and credentials | Real destination/account tests, payload capture/bounds, denial/revocation, timeout/cancel/unknown and no-fallback evidence |
| R26.5 - Additional formats and hybrid retrieval | Admit parser/model packages independently; optional embeddings and scheduled refresh | R26.2 plus R25 for rich formats and format-specific dependency/licence/security gates | Per-format hostile corpus and citation proof; embedding identity, quality, privacy, migration, deletion and offline evidence |

R26.1 is the earliest reasonable implementation target, but it is not an
independent quick feature in the current repository. The roadmap currently
marks required storage/session/configuration/tool-loop work as partial or
outstanding. Implementing path reads directly in a view model or inserting
whole files into the existing 4,096-character local-model request would bypass
the required source identity, context budget, retention, egress, and
cross-session controls. Documentation can land now; implementation should
begin only when the named prerequisite contracts are available or when the
work is explicitly limited to an unadvertised native snapshot foundation.
