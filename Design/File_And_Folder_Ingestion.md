# File and Folder Ingestion and Grounded Reasoning

Status: bounded R26 foundations implement **exact-session durable single-file attachment, local file inspection and
selected immutable revision, bounded immediate-folder lexical retrieval and explicit volatile preview refresh**, described below. The broader
folder/multiple-version ingestion, recursive folder, managed knowledge source, persistent/vector
index, multi-source retrieval, reasoning and hosted transmission design
remains proposed and unavailable.

Related: [Architecture](Architecture.md), [Security and Data Flows](Security_Data_Flows.md),
[Interaction and Sessions](Interaction_And_Sessions.md),
[Model Providers, Memory, and Grounded Knowledge](Model_Providers_Memory_And_Knowledge.md),
[User Configuration](User_Configuration.md),
[Internal Model Tools](Internal_Model_Tools.md), and
[Implementation Roadmap](Implementation_Roadmap.md#optional-and-deferred-work).

## Delivered Durable Single-File Session Attachment - 2026-10-10

**Sessions > select an exact Active session > Attach text file to this exact Session** uses the trusted native picker and the same host-owned verified capture broker as local preview. It has separate metadata and durable-copy disclosure, followed by **Confirm: capture and retain this exact file for this Session**. Volatile preview consent never permits persistence.

The fixed product boundary is **one immutable file per exact session**, `.txt`, `.md` or `.markdown`, **262,144 original UTF-8 bytes including an optional BOM**, and **sixteen retained attachments across the current private profile**, with no implicit eviction. A second attachment is refused until the existing attachment has been explicitly removed. There are no folders, extra versions, managed sources, refresh/watchers or model ingestion.

The selected exact session, generation, original native user request/task, private control revision and cancellation remain fenced through capture and atomic commit. The existing Windows fixed-drive canonical retained-handle, ancestor/reparse/hard-link, protected/generated/source-control, identity, stability, size, two-pass consistency, strict UTF-8 and release checks are unchanged.

The capture callback borrows authoritative original bytes only during that same admission, after native release. It never reopens a path or reconstructs bytes from displayed text. The existing private interaction database stores the exact BLOB plus validated source/revision/item/digest, original native metadata/request, owning private profile/session/generation, captured time and `strict-utf8-v1` projection provenance.

Interaction schema **8** adds `session_file`; snapshot format **1** is fixed and validated. The source-preserving version-7 migration retains every existing authority hash, memory/grant, queue/deadline, history and retention row. Unknown versions, malformed bodies, digest/scope/lineage disagreement, missing committed rows, downgraded authority or replaced storage fail closed, not into a new database or defaults.

**Inspect retained attachment / remove Kora copies** resolves only that exact session's retained historical snapshot after restart. Full bounded inert source and the same `lexical-lines-v1` exact citations are available locally without models or network. A retained Done session remains Done. Browsing and search never renew meaningful activity or trigger cleanup; the merged passive-inspection, source-copy, retention and ownership/privacy/call fences still apply.

This is explicitly a **historical admitted revision**, not a current filesystem or access observation. Changing, moving or deleting the original file does not refresh, rebind or reread it. Authority is the exact retained private session snapshot, not its old path. Removing/revoking that retained source, session deletion, unavailable/corrupt storage or closed host admission prevents disclosure and cancels late inspection/search.

Removal first reviews **Remove attachment and Kora copies** with exact session/generation, source/revision/item/digest, storage revision and complete inventory revision; a separate original native confirmation is required within two minutes. Native content is revoked before cleanup. The atomic trusted-audit transaction strips the body and path-bearing metadata, retaining content-free provenance only.

Owned copies are the private SQLite row/cells/free pages and owned PERSIST rollback journal. No artifact, staging, recovery export, backup, index or context-envelope copy is created. Uninventoried private artifacts/staging/backups and live/unresolved/Unknown work hold removal. Secure-delete and committed-journal verification reuse the memory deletion rules; a row deletion alone is not copy-removal completion.

Session disposition and ordinary retention include attachments in their existing source-revocation/inventory path. Failure or interruption leaves a revoked source or an explicit hold, never restored body authority; unrelated sessions, kept sessions and independent Perpetual grants remain separate. Original user files are unchanged. Logical owned-copy deletion is not forensic erasure of storage hardware.

No body, query, excerpt, filename or path enters preferences, logs, audit text, activity tags/Baggage, transcripts/history, speech, clipboard or model/provider requests. Native headers and source text are untrusted data. The six-ID read-only model registry is unchanged; no context descriptor is falsely marked Available.

Evidence: [Core exact-byte restoration](../tests/Kora.Core.UnitTests/Context/LocalFileRevisionRestoreTests.cs), [shared capture and release](../tests/Kora.Tools.UnitTests/Files/LocalSessionFileAttachTests.cs), [host fences/quiescence](../tests/Kora.Application.UnitTests/Hosting/SessionWorkspaceServiceTests.Attachments.cs), [real private SQLite/native capture/restart/removal](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionAttachmentTests.cs), and [native inert-source confirmation](../tests/Kora.Windows.IntegrationTests/SessionAttachmentWindowTests.cs).

Experiment assessment: these maintained tests close only this single-file capture/storage/native consumer. They do not supersede unique encrypted-artifact/DPAPI/rekey/backup, actual native/runtime/inference, RT1/RT2/MG1, containment or historical receipt failures. All experiment consumers and proofs remain retained. Full R26, managed knowledge, model use, OCR/vector/egress and installed/accessibility acceptance remain open.

## Delivered Bounded Local File Preview

**Tray > Preview file (local inspection only)** and exact `preview file`
(typed or current-name ACTIVATED input) share `Kora.Tools.Files.LocalFilePreview`.
They open the trusted native single-file picker, never resolve a typed/spoken
path into read authority. Only fixed-drive absolute canonical `.txt`, `.md`,
and `.markdown` selections are supported: 256 KiB source bytes including an
optional UTF-8 BOM, 240 path characters, at most 32 path components, one file,
no enumeration/recursion. These limits live in `LocalFilePolicy`.

The Windows inspector opens **metadata only** and retains the file and all
ancestor handles without delete sharing. The selected file additionally
disallows write sharing. Handle-derived final path, volume/file identity,
link count, size and last-write metadata must agree; no reparse component,
hard link, hidden/system component, device, stream, non-fixed drive, path
alias/escape, generated/source-control metadata or protected root is accepted.
Kora roots come from `IApplicationDataPaths`; application/Windows/program and
common application data roots are also denied. Failures reject the whole
selection, not a success with undisclosed exclusions.

The native review displays canonical path, origin/host session, source/review
IDs, identity, bytes, last write, limits and privacy disclosure. **Confirm:
read this exact selected file locally** is the only admission route. The
review expires after two minutes (metadata-only revalidation); closed/stale
review IDs, another host session/task, voice/system confirmation, expired
origin/privacy/ownership/call generations and cancellation cannot read.
Confirmation starts a fresh linked host operation, not a resurrected trace.
Deferred cleanup uses a fresh linked recovery operation with the reviewed host
identity, including release-failure diagnostics after the selection trace ends.

Two bounded consistency passes through the same retained handle admit only
identical bytes and recheck identity/length before and after capture (at most
512 KiB plus one EOF probe byte read in total). Strict
UTF-8 never guesses an encoding or replaces malformed bytes. An optional BOM
is excluded from displayed text but retained in the SHA-256 digest of exact
source bytes. NUL/binary/control text is denied; Markdown is inert plain text,
without rendering, link resolution or execution. An admitted revision binds
original source/review/session metadata, fresh revision/item IDs, byte digest
and admission time. Empty files are valid. Content is published only after
native release and required typed requested/terminal audit outcomes.

The **volatile preview** is not a durable workspace attachment or registered
knowledge source. Nothing content-bearing is written to preferences, SQLite,
artifacts, logs, speech, transcripts, model requests or the clipboard. Raw
read buffers are cleared; discarded immutable CLR text becomes eligible for
collection, not a claimed cryptographic memory erasure. Close, `clear file
preview`, Cancel task, privacy/lock, generation changes, exit or ownership
loss discard the selected revision and release review handles. Pending
selection/read and unverified release block clean handoff; cancellation does
not fabricate quiescence. No watcher, automatic refresh/retry, ambient collection,
network parsing, inference, egress or document-derived authority exists.

R26.1c below extends this foundation to immediate-folder preview and search.
UNC/removable-drive ingestion, durable folder attachments, managed registry,
managed-source refresh, persistent/vector indexes, local/hosted grounded reasoning,
screens/images and later formats remain unavailable. This slice does not
qualify their gates. Native installed/accessibility acceptance remains
separate from deterministic contract and real Windows filesystem tests.
No experiment is removed: inference, storage, runtime and containment proofs
are not exactly superseded by a volatile file preview.

## Delivered Selected-Revision Lexical Retrieval

R26.1b adds a host-owned `LocalFileSearch` action over the existing admitted
`LocalFileRevision`; no additional filesystem authority is acquired.
The shared preview broker resolves exact source/revision/item/digest,
deliberate origin and original host session/task; captures its generation and
cancellation; and revalidates the same revision, admission/privacy/ownership/
call gates and terminal typed audit under the revocation lock before returning.
Replacement, revocation, source disposition, Cancel task, privacy, ownership
loss or disposal cancel/suppress late work. Outstanding worker work remains
nonquiescent even after clear; no current-path read, refresh, fallback copy,
ambient watching or folder/source expansion occurs.

`ILocalFileRetrieval` and Core's `LocalFileLexicalRetrieval` implement
`lexical-lines-v1`: streaming chunks from the admitted exact Unicode projection,
without content persistence or a cache. Chunks honor blank-paragraph and ATX
heading boundaries, then line/size limits (128 lines / 2,048 UTF-16 characters).
Long lines prefer whitespace/punctuation/scalar token delimiters; unavoidable cuts preserve Unicode scalars and
CRLF pairs. Terms crossing a forced chunk boundary are not indexed as invented
prefix/suffix words. Headings are bounded context labels (64 characters);
the exact source remains in the immutable excerpt, not reconstructed Markdown.
Setext headings, Markdown semantics and byte-offset citations are not claimed.

Queries are inert untrusted text, never instructions or authority. Central
`LocalFileRetrievalPolicy` rejects empty/degenerate/malformed or oversized
queries: 256 UTF-16 characters / 512 UTF-8 bytes, 32 unique terms, 64 characters
per term. Unicode letters/numbers and attached combining marks form terms;
punctuation/format symbols separate them. Form C + invariant uppercase and
ordinal dictionaries are culture-stable. Duplicate query terms do not change
ranking. Oversized source words are skipped whole. OR ranking uses matched
unique terms, saturated per-term frequency (16), then ascending source offset.
There are no embeddings, vector index, stop-word/language inference or models.

Only the best eight candidates are retained; all matching chunks are counted.
Results contain at most eight exact, unmodified excerpts with a combined
16 KiB UTF-8 excerpt budget. The ranking prefix stops before exceeding that
budget; truncation and total matching chunks are explicit. Every citation binds
source/revision/item IDs, digest of admitted original bytes (including BOM),
display filename, heading label, exact UTF-16 start/length and one-based
line/column start and exclusive end. CRLF is one newline, CR/LF also work;
columns are UTF-16, not grapheme/byte positions. No path is citation authority.
Observation time describes this query of the admitted revision, **not** a fresh
observation of the current filesystem. NoMatch, InvalidQuery, Busy, Stale,
Denied, Cancelled and Unavailable are distinct and carry no excerpts.

Native preview exposes exact selected-source search and complete inert text
inspection. Fixed `search file` / `inspect file` typed/current-name ACTIVATED
commands only focus that control; query text is entered natively and never
enters conversation transcripts/history, model routing, speech, logs, audit
identifiers, clipboard or durable storage. All result content stays native/
volatile and is cleared on close. The current R06 six-ID read-only registry
cannot safely describe an unavailable content-bearing model operation; it is
unchanged, with no model tool or result egress. Required typed audit failure
blocks result publication. Source-generated logs report only bounded outcome,
count/truncation and failure type; child host activities preserve session/task
correlation without content/path tags.

Experiment disposition: maintained deterministic chunk/citation, bounds,
revocation/cancellation/isolation and no-egress tests now cover this exact
production slice. No executable experiment is superseded or removed. In
particular R02 inference answer quality, actual resource/offline/cessation,
RT1/RT2/MG1 and durable storage/enterprise-cache trials remain distinct and
retained. This slice does not qualify grounded reasoning or installed native
accessibility acceptance.

## Delivered Bounded Folder-Scoped Lexical Retrieval - 2026-10-09

R26.1c extends the volatile R26.1a/b foundation, not the durable R26.2 lifecycle. **Tray > Preview folder (immediate files, local lexical search)** and exact `preview folder` use the trusted native single-folder picker. Typed/voice paths do not authorize reads ([native selection](../src/Kora/LocalFilePreviewWindowController.cs), [commands](../src/Kora.Core/Context/LocalFileCommand.cs)).

Admission is all-or-nothing: **1–32 immediate files**, **1 MiB (1,048,576 bytes) combined original bytes**, and the unchanged **256 KiB (262,144 bytes) per file**, including any UTF-8 BOM. Empty individual files remain valid; empty folders are rejected. Files are ordered by ordinal canonical path, independent of enumeration order. Any subdirectory, including an empty, generated, hidden or text-named directory, rejects the whole selection; there is no recursion or silent partial admission ([folder policy](../src/Kora.Core/Context/LocalFolderPolicy.cs)).

The Windows inspector applies the same fixed-drive absolute canonical path, verified-handle, sharing, ancestor, reparse, hard-link, hidden/system, protected/generated/source-control and unstable-source policy to the root and every immediate file. Unsupported extensions, including archives/binaries, reject the whole inventory before content reads. Strict UTF-8 and binary/control-text sniffing remain the single-file decoder's authority; renaming binary data does not bypass decoding ([inspector](../src/Kora.Windows/Context/WindowsLocalFileInspector.cs), [file policy](../src/Kora.Core/Context/LocalFilePolicy.cs)).

Metadata-only review lists the complete canonical root identity, original host session/origin, review/source IDs, and every file's path, native identity, bytes and last-write observation, with count/byte/recursion/privacy disclosure. **Confirm: read every exact reviewed immediate file locally** confirms only that inventory within the existing two-minute review deadline. All root/ancestor/file handles remain retained until capture or revocation; each file excludes write/delete sharing ([native review](../src/Kora/LocalFilePreviewWindow.cs), [broker](../src/Kora.Tools/Files/LocalFilePreview.cs)).

Exact inventory and retained identities are revalidated before and after capture. Each file uses the same two consistency passes, EOF probe, strict decoding and original-byte SHA-256 digest as R26.1a. Read work is bounded by 2 MiB plus at most 32 EOF probe bytes. Every item must succeed; any membership, identity, length, encoding, cancellation or release failure discards all staged revisions and clears raw buffers. Publication still requires verified native release and typed requested/terminal audit outcomes ([capture](../src/Kora.Tools/Files/LocalFilePreview.cs), [native consistency](../src/Kora.Windows/Context/WindowsLocalFileInspector.cs)).

The immutable folder reference selects the complete admitted set; each file retains its own fresh revision/item IDs and digest under the common reviewed source ID. `LocalFileSearch` and `ILocalFileRetrieval` search that set through the **same `lexical-lines-v1` implementation**, not independent per-file result merging. Unique matched terms and saturated frequency rank globally, then ordinal canonical file order and source offset break ties. The existing eight-citation / 16 KiB combined UTF-8 excerpt bounds apply once to the whole folder; all matching chunks are counted and truncation is explicit. No terms span files. Citations still bind the correct exact file source/revision/item/digest and unchanged UTF-16/line/column/heading/excerpt semantics ([folder revision](../src/Kora.Core/Context/LocalFolderRevision.cs), [shared retrieval](../src/Kora.Core/Context/LocalFileLexicalRetrieval.cs)).

The native surface inspects each complete inert file and searches only the selected immutable folder. `search folder` / `inspect folder` focus native query entry; queries and excerpts do not enter transcripts, history, models, speech, clipboard, logs or durable storage. `clear folder preview`, close, Cancel task, privacy/lock, call/ownership/generation change and disposal revoke the same shared broker. Single-file and folder selections replace rather than expand one another. Outstanding capture/search still blocks clean handoff until genuinely quiescent ([presentation](../src/Kora/LocalFilePreviewWindow.cs), [host gates](../src/Kora.Application/ViewModels/MainViewModel.Files.cs), [shared revocation](../src/Kora.Tools/Files/LocalFilePreview.cs)).

**This slice is folder-scoped admission + lexical search only, volatile only.** There is no durable cross-session attachment, managed registry, refresh, ambient watching, disable/remove lifecycle, persistent/vector index, embeddings, local-model reasoning, hosted egress or document-derived authority. R26.2 lifecycle and R26.3/4/5 runtime/format gates remain separate; R08 remains unqualified. No executable experiment is superseded. Installed native/accessibility acceptance remains outstanding; maintained portable tests and real Windows sharing/reparse/hard-link/protected-path/inventory tests cover this bounded slice ([Core tests](../tests/Kora.Core.UnitTests/Context/LocalFolderTests.cs), [broker tests](../tests/Kora.Tools.UnitTests/Files/LocalFolderTests.cs), [host tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.Folders.cs), [Windows tests](../tests/Kora.Windows.IntegrationTests/LocalFileTests.Folders.cs)).

## Delivered Explicit Volatile Preview Refresh - 2026-10-10

R26.1d extends only the host-held, currently admitted R26.1a/c file or immediate-folder preview. Native **Refresh this file/folder preview** and exact typed/current-name ACTIVATED `refresh file` / `refresh folder` share [`LocalFileRefresh`](../src/Kora.Tools/Files/LocalFileRefresh.cs) and the existing preview broker. They cannot supply a path, pick another source, confirm a read or submit a document to a model. Reserved refresh suffixes/malformed commands refuse locally, not through inference.

The broker captures the exact previous source/revision reference, original host request/session/task and eligibility/control lifetime. The operation reopens only the original trusted canonical path through the unchanged Windows inspector, requiring the same original physical file or directory identity. The same fixed-drive, ancestor, reparse, hard-link, protected/generated/source-control, hidden/system and stable-sharing policy still applies. Missing, replaced, aliased or unverified roots deny refresh; recovery is a fresh explicit native picker selection, never silent source rebinding.

Changed bytes/size/mtime of that same physical file are eligible for a **fresh metadata-only review** with a new review ID, linked activity and two-minute deadline. For a folder, the complete newly enumerated immediate inventory is reviewed: 1–32 files, 1 MiB combined original bytes, unchanged 256 KiB per file, same formats, no recursion or exclusions. The native review shows new identities/count/bytes and previous metadata; folder members added, removed or changed/replaced are visible. “Metadata unchanged” is not a content freshness or byte-equality claim.

**Separate new native confirmation is required before content reads.** Old confirmations cannot be reused. Strict UTF-8/binary/control failures discovered during capture reject the entire candidate. Both consistency passes, exact complete inventory validation, raw-buffer clearing, native release and required typed terminal audit precede publication. The source ID and original request are retained; every successful revision and item obtains fresh identities and exact original-byte digests. Old exact references/citations never resolve into new content, even if bytes happen to be unchanged.

The existing single-slot broker **retires the old immutable preview and callbacks when refresh starts**, rather than keeping an unlabeled old snapshot visible or relabeling it current. Review, cancellation or failure is not refresh success and leaves no admitted preview. Close/Clear/Cancel task, original or current privacy/call/control generations, ownership loss, disposal, audit failure and late metadata/capture/search all retain the same fail-closed resource-quiescence fences. Unverified native release blocks clean handoff/exit and further admission; restart/recovery is required. Starting a competing refresh/selection/search while work is outstanding returns Busy, not another read.

This remains **volatile only**: original user files are untouched; no preference, source registry, durable attachment/history/artifact/index/body, watcher/scheduler, network, recursive enumeration, egress/model context, clipboard, speech, executable or document authority is added. Full R26.1/2 durable source lifecycle, qualified local/hosted reasoning, OCR/vector/later formats and installed native/accessibility gates remain open. No experiment is exactly superseded: maintained refresh tests cover this bounded contract, not historical/native/inference/storage/runtime/containment proofs. All experiment consumers and unique proofs are retained.

Focused evidence: [Core exact commands](../tests/Kora.Core.UnitTests/Context/LocalFileTests.cs), [broker metadata/identity/audit/revocation](../tests/Kora.Tools.UnitTests/Files/LocalFileRefreshTests.cs), [host no-egress/no-persistence](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.FileRefresh.cs), [actual Windows same-identity and denial fixtures](../tests/Kora.Windows.IntegrationTests/LocalFileTests.Refresh.cs).

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
| R26.1a - Bounded local file inspection | Delivered foundation: native selection, metadata review, exact native confirmation, immutable strict-UTF-8 plain-text/Markdown preview; volatile only | Existing ownership/privacy, activity/audit, exact input routing and passive native presentation seams | Deterministic portable admission/decoding/generation tests and real Windows sharing/reparse/hard-link/protected-path/cancellation tests; no folder, persistence, model or retrieval authority; installed UX acceptance outstanding |
| R26.1b - Selected-revision lexical retrieval | Delivered: exact single-file immutable revision search, shared lexical-lines-v1 chunks/ranking and native citations; volatile only | R26.1a and existing ownership/privacy/call/audit boundaries | Bounded exact excerpts, digest/item citations, cancellation/revocation/isolation and no-egress tests; no additional filesystem, model or persistence authority |
| R26.1c - Bounded folder-scoped lexical retrieval | Delivered: reviewed complete set of 1–32 immediate files, 1 MiB combined bytes, unchanged 256 KiB per file; any subdirectory/inadmissible item rejects the whole selection; native search only, volatile only | R26.1a/b and existing ownership/privacy/call/audit boundaries; no R08/runtime dependency | [Portable admission/retrieval](../tests/Kora.Core.UnitTests/Context/LocalFolderTests.cs), [broker revocation/audit](../tests/Kora.Tools.UnitTests/Files/LocalFolderTests.cs), [host no-egress/no-persistence](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.Folders.cs) and [real Windows denial/inventory/bounds](../tests/Kora.Windows.IntegrationTests/LocalFileTests.Folders.cs); installed UX acceptance outstanding; R26.2 lifecycle remains gated |
| R26.1d - Explicit volatile preview refresh | Delivered: exact host-held original canonical physical file/folder; fresh complete metadata review and separate native confirmation; unchanged bounds and source ID, fresh revision/items/digests; old single-slot preview retired at start | R26.1a/c and original/current host control, session/task, privacy/call/ownership/generation, audit/release boundaries | [Refresh broker](../tests/Kora.Tools.UnitTests/Files/LocalFileRefreshTests.cs), [host](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.FileRefresh.cs), [actual Windows files](../tests/Kora.Windows.IntegrationTests/LocalFileTests.Refresh.cs); no registry/durability/watch/egress, installed UX and broader R26.2 acceptance open |
| R26.1e - Durable single-file Session attachment | Delivered: one exact-session immutable UTF-8 text/Markdown file, 256 KiB original bytes, sixteen retained profile files, no eviction; separate native persistence/removal consent and restart-safe inert source/citations | Existing verified capture, consolidated private interaction authority, passive inspection, session/source retention, native UX | Exact-byte/BOM/hash, original-user generation/control, private SQLite migration/audit/reopen/corruption, native full source and owned-copy removal tests; no folders/versions/managed knowledge/model/OCR/vector/egress, no full R26 or installed acceptance |
| R26.0 - Finalize policy and limits | Agree initial formats, numeric limits, storage/retention, citation shape, local-runtime envelope, and hostile-document fixtures | R04 storage semantics, R06 tool/result bounds, R10 configuration registry, R12 session retention | Approved typed contracts and threat/acceptance fixtures; no runtime capability |
| R26.1 - Native selection and immutable text snapshot | Add picker plus reviewed absolute path proposal, safe Windows enumeration/read, `.txt`/Markdown extraction, source registry, revision identity, native preview and deletion | R03 ownership/privacy, R04 durable storage/recovery, R05 review/questions, R10 settings | Real Windows file/folder/reparse/access/change/cancel/restart tests; no model exposure |
| R26.2 - Lexical retrieval and citations | Deterministic chunk/index/search/read-excerpt, source selection, context budget, citation presentation, refresh/disable/remove cleanup | R26.1, R06 admitted descriptors, R12 session/artifact lifecycle, R14 source/citation UI | Grounding, hostile-content, cross-session/source isolation, stale/revoked and interrupted-cleanup evidence |
| R26.3 - Qualified local-model reasoning | Send only selected cited excerpts through the admitted local runtime/tool loop and produce grounded answers | R26.2, R02-L5/L6, R07 context broker, R08 tool loop/streaming | Local-only network-blocked quality, context-limit, citation, cancellation and resource evidence on supported hardware |
| R26.4 - Hosted-model reasoning | Apply exact destination/source revision/excerpt review and hosted adapter egress; preserve citations and deletion boundaries | R26.3, admitted hosted provider under R08, remote-enabled policy and credentials | Real destination/account tests, payload capture/bounds, denial/revocation, timeout/cancel/unknown and no-fallback evidence |
| R26.5 - Additional formats and hybrid retrieval | Admit parser/model packages independently; optional embeddings and scheduled refresh | R26.2 plus R25 for rich formats and format-specific dependency/licence/security gates | Per-format hostile corpus and citation proof; embedding identity, quality, privacy, migration, deletion and offline evidence |

The broader R26.1 durable attachment/registry and recursive-folder stage is not complete. The roadmap
marks required storage/session/configuration/tool-loop work as partial or
outstanding. Implementing path reads directly in a view model or inserting
whole files into the existing 4,096-character local-model request would bypass
the required source identity, context budget, retention, egress, and
cross-session controls. Delivered R26.1a/b/c/d explicitly stop at volatile native inspection, scoped lexical search and exact explicit preview refresh; those
remaining contracts are not implied by this foundation.
