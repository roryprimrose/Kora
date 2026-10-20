# File and Folder Ingestion and Grounded Reasoning

Status: bounded R26 foundations implement **exact-session durable single-file attachment, retained-slot metadata catalogue and exact navigation, local file inspection and
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

## Delivered passive native decoded-text measurement

The [native measurement partial](../src/Kora/LocalFilePreviewWindow.Measurement.cs)
delegates pure counting to [Core `DecodedTextMeasurement`](../src/Kora.Core/Presentation/DecodedTextMeasurement.cs).
Only the actual displayed immutable admitted item is measured: UTF-16 code units,
Unicode scalars and strict UTF-8 re-encoding bytes. No normalization, replacement,
line/word policy, model tokens, text quota or folder aggregate is inferred.
Strict invalid UTF-16 retains only the known code-unit count; scalar and byte
measurements are unavailable. Existing `LocalFilePolicy.Decode` admits only
strict UTF-8 (not UTF-16 source files), strips one optional leading BOM and
guarantees well-formed admitted display text. An actual remaining BOM counts.
Original byte length is separately labelled from existing admitted metadata,
never inferred from decoded text or presented as current filesystem/storage size.

Measurement occurs once when binding each exact displayed source/item/revision,
not from getters, queries, layout, timers or notifications. Publication rechecks
the source object, unchanged display text, window generation and existing
controller/read-channel predicate, plus the host's captured input, privacy,
call and ownership lifetime. Source changes clear before replacement; retirement,
reentrancy, privacy/host changes and closure clear old controls and references.
Queued retirement is bound to its old control, not a replacement view.

The same view covers volatile singleton/folder-item and already loaded retained
historical inspection. No original-path or retained-store read, decoding,
recapture, renewal, I/O, copy/edit/save action or new authority is introduced.
Find, ranked retrieval, citations and retained historical read guards are unchanged.
Counts and text remain native-only, outside conversation, speech, diagnostics,
Activities, persistence and clipboard. This bounded I increment makes no storage
completeness, installed accessibility, provider/model, E-enablement or Q-RC claim.
See [user labels and scope](../docs/windows-and-tray.md#decodedadmitted-display-text-size),
[portable measurement tests](../tests/Kora.Core.UnitTests/Presentation/DecodedTextMeasurementTests.cs)
and [native tests](../tests/Kora.Windows.IntegrationTests/LocalFileTextMeasurementWindowTests.cs).

## Delivered Durable Single-File Session Attachment - 2026-10-10

### Delivered Retained Attachment Metadata Catalogue - 2026-10-11

#### Selected displayed metadata literal Find - 2026-10-18

Select an admitted row, then use **Find first / next / previous / Clear Find** in its details pane. Unlike catalogue filtering, Find searches the entire displayed `SelectedDetails` JSON, frozen on selection. It excludes other rows, hidden backing fields, descriptors, names, paths, bytes and current sources ([native owner](../src/Kora/AttachmentCatalogueViewModel.cs)).

The [shared admitted-metadata navigation state](../src/Kora.Application/Presentation/AdmittedMetadataFindState.cs), renamed without behavior changes for reuse by skill identity inspectors, reuses [Core `SnapshotLiteralFind`](../src/Kora.Core/Context/SnapshotLiteralFind.cs) unchanged: bounded well-formed one-line query, ordinal case/space-sensitive literals, one nonoverlapping UTF-16 span, no normalization, count or wrap. Invalid queries are explicit, not successful no-match results.

Only the existing content-free projection is searchable, within the existing page budget. Unavailable `StoredBytes` is removed before display/snapshot capture; tombstone-suppressed metadata stays suppressed. Raw backing fields are not searched ([projection](../src/Kora/AttachmentCatalogueViewModel.cs), [retained fields](../src/Kora.Core/Storage/SessionAttachmentSummary.cs)).

Typing and Clear retire match, coordinates and status without I/O. Selection, filter/page/refresh/operation start, cancellation, privacy/admission/control/source changes and disposal retire old Find state and private strings. Find queues no work. Stale controls cannot recover retired snapshots ([native tests](../tests/Kora.Windows.IntegrationTests/AttachmentCatalogueWindowTests.Find.cs)).

The [controls](../src/Kora/AttachmentCatalogueWindow.axaml) expose query, buttons, status/coordinates and an inert exact match span. Enter finds First; F3/Shift+F3 navigate; Escape clears Find without closing. Transfer/context gestures are blocked here. Find cannot select/open/resume a session or change catalogue queries, storage, capture, retention, clipboard or action authority.

This bounded I increment has [portable tests](../tests/Kora.Application.UnitTests/Presentation/AttachmentMetadataFindStateTests.cs) and the native tests above. It makes no installed accessibility, runtime/provider, egress, wake, profile-writer, E-enablement or Q-RC qualification claim; unavailable capabilities stay unavailable.

**Tray > Retained session attachments** opens a separate native, content-free catalogue. Opening performs no read; explicit Refresh/Next/Cancel observes already retained private Session slots. It never opens a picker, reopens an original path, reads new file data, captures, replaces, removes, exports or ingests model context.

The native **Browse / ExactId** query increment reuses `SessionListSearch` as the sole nonempty canonical lowercase D-GUID policy. ExactId filters only fully validated slots; it never selects session authority or skips irrelevant/corrupt records or audit-prefix admission.
The exact ID, stored session lifecycle filter and removal flag are bound together in authenticated source/profile/viewer/authority continuations. Nonmatching scans may return empty pages with Next; changed ID/mode/filter/flag retires rows, selection and pending work. Clear returns to Browse without I/O; no automatic selection/navigation or saved query exists.
See [native query interaction and recovery](../docs/windows-and-tray.md#retained-session-attachments-metadata-only). Names, paths, bodies and excerpts are not searched, and no session-name join/read, persisted schema/index/layout/copy/cache or timeout change is introduced.

The optional **literal metadata** field composes with Browse/ExactId, stored lifecycle and the removal flag. It searches only the already exposed canonical lowercase D-format Session/source/revision/item IDs, original digest and media type of nonremoved slots, using case-sensitive `StringComparison.Ordinal` substring matching. It never searches profile/observation IDs, hidden descriptor names/paths, numeric/date/lifecycle fields, original bytes or removed metadata. Held slots retain their existing metadata disclosure but never gain navigation authority. Removed tombstones never match a nonempty literal, even with Include removed slots; clearing the literal restores the ordinary provenance catalogue.

The optional **Exact retained media type: All (default) / text/plain / text/markdown** composes by AND with exact ID, lifecycle, Include removed slots and literal metadata. Core `LocalFileMediaType` owns the existing strict two-type rule shared by query, summary and descriptor validation; case, whitespace, parameters, undefined types and malformed input are rejected, never normalized into All. The predicate uses only validated `SessionAttachmentSummary.MediaType`, never extensions, filenames or paths. Removed tombstones have no retained media type and match only All, regardless of their historical provenance header. Held slots may match but gain no body navigation. Every scanned slot and complete bounded audit prefix is still validated before disclosure, including nonmatches. Authenticated continuations bind the exact type alongside the literal fingerprint and all existing query/source/profile/viewer/authority ceiling/digest limits. Editing the picker performs no read and immediately retires rows, selection, navigation, continuation and late callbacks. Clear query preserves this filter; disposal resets it. Zero matches are truthful, not a global total.

`LiteralMetadataQuery` is the authoritative single-line strict-Unicode policy: at most 256 UTF-16 units / 512 UTF-8 bytes; no controls, format or line/paragraph separators. Spaces and punctuation remain exact literals, with no trimming, normalization, wildcard, ranking or fallback. The native empty field disables literal filtering; a supplied empty query is invalid. Typing does no I/O; explicit Search/Enter and Next retain full validation of nonmatching slots and bounded audit segments. Text stays memory-only, is excluded from query/page serialization and never enters persistence, audit, diagnostics, Activity tags/Baggage, exports, transcripts or model/network context. Run-local authenticated cursors bind a keyed fingerprint, not the text. Any literal edit retires rows, exact selection, continuation and pending work; Clear, revocation and close clear the literal too. End-of-observation no-match is labelled without claiming a global total.

The optional **Exact SHA-256** predicate composes by AND with Browse/ExactId,
literal metadata, lifecycle, exact media type, copy status, stored-byte bounds
and removed-slot inclusion. Core `LocalFileDigest` owns the strict original-byte
digest rule, reused by `LocalFileReference.Validate` and catalogue admission:
exactly 64 lowercase ASCII hexadecimal characters, not all zero. No trimming,
case folding, prefix, wildcard or repair is admitted; the native empty field
disables the predicate, while an explicitly supplied empty query is invalid.
Only **Confirmed** retained copies can match; Held and Unavailable never match.
This is stored copy certification, not a fresh body/hash-integrity check.
Every candidate, including nonmatches and tombstones, is validated first.
Removed provenance never discloses its historical digest in catalogue rows or
serialized/detail projections, even if a malformed retained object carries one.
The digest query is volatile and excluded from query/page/cursor serialization;
the signed continuation binds a separate run-local keyed fingerprint alongside
all existing source/profile/viewer/query/authority fences.
The focusable, accessibly named native field supports explicit Search/Enter;
edits perform no read and synchronously retire page, continuation, selected
details/navigation and late callbacks. Clear query, revocation and disposal
erase the digest field. Empty pages with continuation and bounded no-match
observations remain truthful. No schema/index/cache, body/path read, persistence,
audit/log/tag payload, egress, activity renewal or authority change is added.
Deterministic [portable contract tests](../tests/Kora.Core.UnitTests/Storage/SessionAttachmentDigestTests.cs),
[private SQLite tests](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionAttachmentTests.DigestCatalogue.cs)
and [headless native tests](../tests/Kora.Windows.IntegrationTests/AttachmentCatalogueWindowTests.Digest.cs)
qualify this bounded I subset only, not installed accessibility or broader R26/E/Q.

The optional **exact local-file source ID**, **revision ID** and **item ID**
compose by AND with every Browse/literal/exact-session/lifecycle/media/status/
bytes/digest/observation-source/profile/time/generation/revision predicate.
Each independently reuses `SessionListSearch` strict nonempty canonical lowercase
D-format GUID admission. Empty native fields mean Any; explicitly supplied
empty strings, zero GUIDs, aliases, case changes and whitespace are rejected,
never trimmed, normalized or repaired. Only **Confirmed** retained copies match.
Held and Unavailable/Removed slots never match, including queries equal to
former headers; exact predicates never reconstruct or disclose former IDs from
tombstones. Existing unfiltered content-free removal provenance is unchanged.
These are retained correlation metadata only, never source consent, current
file identity, path disclosure, body integrity, file-read authority or proof a
copy exists. Every complete summary is validated before filtering, including
nonmatching and removed candidates. All three volatile IDs participate in query
identity and the keyed authenticated continuation fingerprint without serializing
query values. No SQL exclusion, body/path read, schema, index, cache, persistence,
audit/log/tag payload, model/provider call or authority is added.
Focusable, accessibly named native inputs support explicit Search/Enter.
Edits perform no read and retire rows/details/selection/navigation/continuation
and pending callbacks; Clear query, cancellation/revocation and close erase
the fields. Existing bounded complete pages, timeouts, exact navigation and
stale/cancellation fences remain unchanged.
Deterministic [portable contracts](../tests/Kora.Core.UnitTests/Storage/SessionAttachmentFileReferenceTests.cs),
[private SQLite checks](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionAttachmentTests.FileReferenceCatalogue.cs)
and [headless controls](../tests/Kora.Windows.IntegrationTests/AttachmentCatalogueWindowTests.FileReference.cs)
qualify only this consumed I subset; installed accessibility, broader R26 and
E/Q remain separate and unclaimed.

The optional inclusive **session-generation**, **captured-generation** and
**storage-revision** ranges compose by AND with Browse/literal/exact-session/
lifecycle/media/copy-status/bytes/digest/source/profile/captured-time predicates.
Each endpoint is independently optional; empty native text means Any. Nonempty
input uses `OptionalUnsignedDecimal` and the authoritative positive `HostRevision`
rule: ASCII invariant decimal digits, 1 through 9223372036854775807, minimum <=
maximum. Signs, whitespace, separators, zero, overflow and inverted ranges fail
closed without repair or Browse fallback. Equal endpoints are inclusive.
Only **Confirmed** retained copies match any version bound; Held and
Unavailable/Removed never match, even when a former header equals the query.
Tombstone serialization, details and native rows suppress all three former
version values, including malformed retained objects. Validating internal
provenance does not disclose or reconstruct it for this query.
Generations/revisions are retained correlation/version metadata only, never
source consent, session authority, currentness, file-read authority or proof a
copy still exists. Every candidate is validated before exclusion. All six
volatile endpoints participate in query identity and the keyed authenticated
continuation binding without serializing their query values. The metadata-only
projection, complete bounded pages, audit validation and stale/cancellation
fences are unchanged. Accessible native fields support explicit Search/Enter;
editing performs no read and retires selection/details/navigation/continuation
and pending work. Clear query, revocation and disposal erase all six fields.
No schema, index, body/path read, persistence, provider or authority is added.
Deterministic [portable](../tests/Kora.Core.UnitTests/Storage/SessionAttachmentVersionRangeTests.cs),
[SQLite](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionAttachmentTests.VersionCatalogue.cs)
and [headless native](../tests/Kora.Windows.IntegrationTests/AttachmentCatalogueWindowTests.Versions.cs)
checks qualify only this bounded I subset, not installed accessibility or R26/E/Q.

The explicit **Any (default) / Same observed generation / Earlier observed generation** predicate AND-composes with every existing retained metadata predicate. Same compares retained `CapturedGeneration == SessionGeneration`; Earlier compares `CapturedGeneration < SessionGeneration`.
Both constrained choices require the existing **Confirmed** copy admission; Held and unavailable/removed projections never gain matches or reconstruct former provenance.
This is a passive historical metadata relationship, not current session/device identity, freshness, staleness, completeness, consent, authority, source integrity or proof a copy exists.
The authoritative [summary validation](../src/Kora.Core/Storage/SessionAttachmentSummary.cs) still requires positive generations and `CapturedGeneration <= SessionGeneration` before any [query exclusion](../src/Kora.Core/Storage/SessionAttachmentQuery.cs).
Any preserves the existing unfiltered defaults. Undefined values, null/missing JSON choices and invalid native selection fail closed with explicit correction guidance. JSON carries the defined choice; authenticated continuation format 2 binds every choice, including Any, alongside all existing query/provenance/private-source/authority identities.
A changed relationship requires fresh Search.
The [private store](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.AttachmentCatalogue.cs) validates the complete existing bounded metadata batch, including envelopes/provenance after the sixteen-result limit.
Its cursor advances only through the disclosed prefix, so later slots remain reachable; the original 64-slot scan, 16-row/64-KiB result, audit, timeout and expiry bounds remain unchanged.
The [native picker](../src/Kora/AttachmentCatalogueWindow.axaml) visibly selects non-null Any, has an accessible name and performs no read on edits. Selection/details/navigation/actions/cursors and pending callbacks retire immediately. Clear query, Cancel, source/privacy/ownership revocation, Escape and disposal reset Any without a read.
Deterministic [Core tests](../tests/Kora.Core.UnitTests/Storage/SessionAttachmentGenerationRelationshipTests.cs), [SQLite tests](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionAttachmentTests.GenerationCatalogue.cs) and [headless native tests](../tests/Kora.Windows.IntegrationTests/AttachmentCatalogueWindowTests.Generation.cs) cover this bounded I consumer.
No schema/index, reader authority, body/path read, picker, copy/remove/open/export/model/network route or E/Q enablement is added.

The optional **exact observation-source GUID**, **exact device-profile GUID**
and inclusive **Captured From / Until UTC** compose by AND with every existing
predicate, including exact digest. IDs reuse `SessionListSearch` canonical
nonempty lowercase D admission. UTC input reuses the strict round-trip parser
extracted from memory metadata into `OptionalCanonicalUtc`:
`yyyy-MM-ddTHH:mm:ss.fffffff+00:00`. No trimming, case conversion, timezone
conversion, abbreviated fractions, `Z` alias or repair is admitted. Empty native
fields mean Any; explicitly supplied empty IDs are invalid. Either time bound
may be unset; equal endpoints include the captured instant and From must not
exceed Until. Typed bounds must have zero offset.
Only **Confirmed** retained copies match any of these predicates. Held and
Unavailable/Removed slots never match, even when an old header equals the query.
Tombstone serialized/detail/row projections suppress observation-source,
device-profile and captured-time values, including malformed retained objects.
These fields are retained correlation metadata only: never source consent,
profile ownership, session authority, file-read authority or proof of current
availability. Selection and full byte inspection retain their existing fences.
Every candidate is fully validated before filtering; no SQL predicate skips
nonmatches or corrupt slots. The complete new filter is volatile, excluded from
serialization and bound by a separate run-local keyed continuation fingerprint
alongside the existing query/source/profile/viewer/head/expiry fences.
Focusable, accessibly named native controls support explicit Search/Enter.
Edits perform no read and retire rows, details, selection, navigation,
continuation and pending callbacks. Clear query, revocation and disposal erase
all four fields. Bounded complete empty pages and cancellation remain unchanged.
No schema/index/cache, original path/body read, audit/log/tag payload, persistence,
model/network call or new authority is introduced. Deterministic
[portable contracts](../tests/Kora.Core.UnitTests/Storage/SessionAttachmentCorrelationTests.cs),
[SQLite metadata/continuations](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionAttachmentTests.CorrelationCatalogue.cs)
and [headless native controls](../tests/Kora.Windows.IntegrationTests/AttachmentCatalogueWindowTests.Correlation.cs)
qualify this bounded I subset only; installed accessibility and broader E/Q are
not claimed.

The optional **Exact copy status: Any (default) / Confirmed / Held / Unavailable**
and inclusive **minimum / maximum retained stored bytes** compose by AND with
every existing predicate. Status matches validated typed metadata exactly;
Any is a non-null, visibly/accessibly selected native choice. Empty byte fields
mean Any. The shared `OptionalUnsignedDecimal` parser (also used for saved-draft
metadata) accepts only invariant ASCII decimal digits: no signs, whitespace,
separators, overflow, clamping or repair. Typed query validation enforces
**0 through `LocalFilePolicy.MaximumBytes` (262,144), inclusive, minimum <= maximum**.
Open-ended and equal-ended ranges are supported. Confirmed and Held retained
metadata can match byte ranges, without granting Held navigation or claiming
current body integrity. Unavailable/removed tombstones never match any byte
predicate, including 0..0: their validated zero is a sentinel, not an original
size. Native rows label that size unavailable and selected details omit it.
Tombstones can match exact Unavailable only when removal/lifecycle and all other
predicates allow them. Every scanned summary and bounded authority prefix is
fully validated before exclusion. Continuations bind all three typed fields
alongside every existing query/source/private/control/head fence.
Editing performs no read and synchronously revokes page, selection, navigation,
continuation and pending callbacks. Clear query preserves these filters;
source/private/control revocation and disposal clear them. Zero matches remain
truthful bounded observations, never fallback or aggregate totals.
This increment changes no schema/index/cache, persistence, bytes/path/content
read, copy/delete/recovery operation, logs/tags/audit payload, model/tool/speech/
export authority or meaningful-activity renewal.

- Each page returns at most **sixteen summaries / 64 KiB**, scanning at most **sixty-four slots**. Retained-only is the default; **Include removed slots** shows content-free removal provenance. Historical slots are not the sixteen-live-body cap. A retained-only page can have zero matches and a valid Next.
- **Stored session lifecycle: All (default) / Active / Done / Removed** filters only fully validated current metadata, independently of Browse/ExactId and **Include removed slots**. All preserves prior catalogue behavior. Removed sessions can have only content-free removed slots, so Removed without Include removed slots yields no rows; Active/Done can also have removed slots.
- Complete typed audit authority is checked in bounded **4,096-commit segments**, before attachment rows are disclosed. Pending-authority pages are explicitly labelled and contain no attachment summaries; Next continues validation. The existing five-second private lease/SQLite progress budget and five-second cancellation deadline remain finite.
- Authenticated, run/store/profile/query-bound continuations expire after **two minutes**. Any committed authority-head change rejects Next and requires Refresh; this conservative rule includes unrelated authority changes. No global count, whole-inventory atomicity, physical-deletion or forensic claim is made.
- Summaries show exact Session ID and current stored Active/Done/Removed state, retained current/captured generations, storage revision, source/revision/item/original digest headers, stored original byte count, type, captured UTC and **Confirmed / Held / Unavailable** copy certification. Unavailable projections suppress former generation/revision, digest, source/profile and captured-time values. No body, excerpt, filename, original path or path-bearing capture metadata is returned/displayed.
- **Confirmed** means stored capture/replacement copy certification, **not current body-integrity verification or present filesystem access**. Held replacement metadata remains inspectable without reading either body; removed slots are Unavailable. Unknown descriptor/type/scope/profile/session/audit lineage and missing committed provenance fail closed, not into empty success.

The reader uses only the initialized private interaction database, shared read-only lease and bounded descriptor/length/type/audit/current-metadata projections. It never calls the full-body attachment reader or broad `Open`/`ValidateAuthority` path to populate a page. One shared descriptor decoder preserves format/projection/source rules for both cheap metadata and full original-byte reads; current session/metadata/latest-row proofs and passive source-copy/retention gates remain mandatory.

**Open exact session/attachment** re-resolves the original selected Session, current generation, storage revision and source/revision/item/digest through the existing private workspace and `SessionFileAttachmentService.Read` / attachment-window path. Actual original bytes, hash, strict UTF-8 and provenance are verified there. Replacement/removal/held certification or changed lineage refuses; no newer attachment is silently opened.

Retained Done inspection does not resume, renew meaningful activity, dispatch, retarget a current question, consume/create a grant or trigger cleanup. Outstanding native file review/control must finish or be discarded first. Query/selection/source/lifecycle/disposition/retention/replacement/removal/private-owner changes and cancellation/close/disposal clear the page, continuation and exact navigation; late callbacks cannot disclose newer private content.

Schema **8**, attachment format **1**, one-file-per-session / **262,144 original UTF-8 bytes / sixteen retained profile bodies / no eviction**, capture/copy-certification/removal rules and existing exact citations are unchanged. There is no second database, index, preference file, copy cache, managed knowledge source, extra format, modelling or installed/accessibility qualification.

Evidence: [strict summary/page bounds](../tests/Kora.Core.UnitTests/Storage/SessionAttachmentPageTests.cs), [passive host/exact navigation](../tests/Kora.Application.UnitTests/Hosting/SessionWorkspaceServiceTests.AttachmentCatalogue.cs), [actual SQLite SELECT projection, tombstones/cursors/corruption/held status](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionAttachmentTests.Catalogue.cs), and [headless native lifecycle/source navigation](../tests/Kora.Windows.IntegrationTests/AttachmentCatalogueWindowTests.cs).

Exact-ID evidence: [shared query policy and page admission](../tests/Kora.Core.UnitTests/Storage/SessionAttachmentQueryTests.cs) and [private SQLite match beyond a full scan, removed slots, query/source/profile/cursor denial and malformed irrelevant-row refusal](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionAttachmentTests.ExactCatalogue.cs).

Lifecycle-filter evidence: [shared query/page policy](../tests/Kora.Core.UnitTests/Storage/SessionAttachmentQueryTests.cs), [passive Application admission](../tests/Kora.Application.UnitTests/Hosting/SessionWorkspaceServiceTests.AttachmentCatalogue.cs), [mixed current states, independent tombstones and validated empty scans](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionAttachmentTests.LifecycleCatalogue.cs), and [native explicit filters and late-read/navigation fencing](../tests/Kora.Windows.IntegrationTests/AttachmentCatalogueWindowTests.cs).

Literal-metadata evidence: [portable policy/composition/serialization](../tests/Kora.Core.UnitTests/Storage/SessionAttachmentQueryTests.cs), [passive Application forwarding](../tests/Kora.Application.UnitTests/Hosting/SessionWorkspaceServiceTests.AttachmentCatalogue.cs), [actual private SQLite projection, keyed continuations and tombstone exclusion](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionAttachmentTests.MetadataCatalogue.cs), and [headless focus, explicit Enter, error minimization and late-read/navigation fencing](../tests/Kora.Windows.IntegrationTests/AttachmentCatalogueWindowTests.cs). This does not qualify installed accessibility, broader R26, file-content/OCR or model-grounded search.

Experiment assessment: these assertions qualify only the metadata/exact retained-source consumer. Unique native/runtime, encrypted-storage/rekey/backup, legacy migration, privacy/resource and historical failure receipts, RT1/RT2/MG1 and containment proofs remain retained. No experiment or proof gate is retired or weakened; full R26 and managed/model-grounded knowledge remain open.

### Native Capture and Retention

**Sessions > select an exact Active session > Attach text file to this exact Session** uses the trusted native picker and the same host-owned verified capture broker as local preview. It has separate metadata and durable-copy disclosure, followed by **Confirm: capture and retain this exact file for this Session**. Volatile preview consent never permits persistence.

The fixed product boundary is **one immutable file per exact session**, `.txt`, `.md` or `.markdown`, **262,144 original UTF-8 bytes including an optional BOM**, and **sixteen retained attachments across the current private profile**, with no implicit eviction.
Attach still refuses a second file. The separately reviewed replacement workflow below replaces only its exact retained slot; it is not silent overwrite. There are no folders, extra versions, managed sources, automatic refresh/watchers or model ingestion.

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

## Delivered Reviewed Retained Attachment Replacement - 2026-10-10

**Sessions > exact Active session > Inspect retained attachment / remove Kora copies > Replace retained attachment** opens a fresh trusted native picker. It never automatically reads the old original path. The old source is historical, not present filesystem authority.
The native metadata review binds the old session/generation, storage revision, source/revision/item/digest and complete copy inventory to the new metadata-only review, original native request/task, private control and window lifetime.
It shows both origins, identities, old captured time and the new bytes/last-write observation, plus persistence and old-copy removal consequences. **Confirm: replace this exact retained attachment with the newly selected file** is separate consent, expiring after two minutes.

The same `LocalSessionFileAttach` / `LocalFilePreview` broker owns capture, strict UTF-8/BOM/exact bytes, two-pass consistency and native release. All fixed-drive canonical/reparse/hard-link/protected/generated/unstable and resource bounds remain unchanged.
Every fresh picker selection receives a new source identity, including picking the same path. No name/path inference preserves source consent; revision and item identities are also new. The old references cannot resolve into new bytes.

One transaction swaps exactly one body with its validated new snapshot; it never counts two retained slots, evicts another session or performs remove-then-attach. A profile with sixteen retained bodies may replace its own slot.
Schema **8**, snapshot format **1**, the original audit hashes and content-free old provenance remain unchanged. A content-free requested receipt precedes the swap attempt; commit uncertainty holds disclosure even if the still-current row is old.
The requested swap authority commit also holds new-body disclosure; neither requested phase is completion. A certified rollback terminal receipt permits only a fresh inspection of the unchanged old record.
While retaining the same task/database lease, Kora closes its SQLite connection, verifies/truncates only the owned committed PERSIST journal, checks the artifact inventory, then commits certified authority and the successful native task receipt before publication.
The inventory hash covers the current attachment descriptor, body and storage/audit revisions, so competing replace/remove/disposition/retention and work/lifecycle changes cannot reuse stale review.

Old native views, inspection/search work and citations retire before capture/publication. No old content, path or extra version is copied into audit, logs, preferences, context, speech, clipboard, artifacts or backups; no original file is written or deleted.
The resulting source is another **captured immutable historical revision**, with its exact captured time and origin, not a continually refreshed source or live access observation. Full inert text and `lexical-lines-v1` exact bounds/citations remain local-only.

Capture/cancellation or transaction rollback before mutation preserves the exact old durable record, not a refreshed view; explicitly reopen it after failure. Once the swap commits, failed journal/inventory/certification or revoked admission leaves a durable disclosure hold, including after restart, never fallback to either body.
**Inspect retained attachment** then offers metadata-only **Remove attachment and Kora copies** recovery. Its separate confirmation removes the exact held new snapshot, retires only its proven unfinished replacement intent and verifies owned copies; it neither restores old content nor certifies a failed replacement.
Unrelated live/unresolved/Unknown work, unregistered copies, stale inventory and uncertain journal/storage remain holds. There is no automatic completion retry, replay, source history or version browser.

Evidence: [host old/new consent, quiescence and cancellation](../tests/Kora.Application.UnitTests/Hosting/SessionWorkspaceServiceTests.AttachmentReplacement.cs),
[real native capture, exact-slot capacity and transactional hold/recovery](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionAttachmentTests.Replacement.cs),
and [native controller/picker/review/reopen/removal](../tests/Kora.Windows.IntegrationTests/SessionAttachmentWindowTests.cs).
The maintained tests assert only these exact equivalents of the existing capture/storage/native consumers. No experiment is retired: unique native/runtime, legacy migration, encrypted-storage/rekey/backup, failure/recovery receipts and RT1/RT2/MG1/containment proofs remain retained. Full R26 and installed/accessibility acceptance remain open.

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
artifacts, logs, speech, transcripts, model requests or the clipboard by preview admission alone. The separate explicit
single-file local-answer request below does not inherit model-use authority from
this read consent. Raw
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
This remains the unchanged default. The separate explicit run-only Markdown mode below does not alter `lexical-lines-v1`.

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
commands only focus that control; query text executes natively and never
enters conversation transcripts/history, model routing, speech, logs, audit
identifiers, clipboard or durable storage. All result content stays native/
volatile and is cleared on close. The current R06 six-ID read-only registry
cannot safely describe an unavailable content-bearing model operation; it is
unchanged, with no model tool or result egress. Required typed audit failure
blocks result publication. Source-generated logs report only bounded outcome,
count/truncation and failure type; child host activities preserve session/task
correlation without content/path tags.

### Delivered preparation-only native lexical query - 2026-10-20

Original typed/current-name ACTIVATED `prepare file search for "<query>"` and `prepare folder search for "<query>"` only prepare the existing native inspection query. Final-quoted doubled-quote escaping preserves case, punctuation, Unicode and interior spaces before shared normalization ([grammar](../src/Kora.Core/Context/PreparedLocalSearchCommand.cs)).

The parser reuses strict single-line Unicode validation and canonical lexical limits, without truncation or repair.
Reserved malformed `prepare file/folder` input is consumed before questions, approvals, transcript presentation or model fallback; inline `search file <query>` stays invalid ([raw dispatch](../src/Kora.Application/ViewModels/MainViewModel.cs), [parser tests](../tests/Kora.Core.UnitTests/Context/PreparedLocalSearchCommandTests.cs)).

Admission requires exactly the requested already-confirmed volatile file or complete folder revision. The host freezes that revision object, exact references and original origin, then rechecks current name/input, privacy topology, host/ownership, call revision and voice consent/recovery at queued dispatch.
No newest-source substitution, path/name selector or retained-attachment targeting exists ([host](../src/Kora.Application/ViewModels/MainViewModel.PreparedLocalSearch.cs)).

A scoped one-use handoff expires after synchronous delivery. The native controller checks UI-thread ownership and exact window revision; the window sets lexical mode/query, invalidates old citations and reports **PREPARED / REVIEW NEEDED**, never search success.
Text changes/focus/late callbacks cannot execute retrieval ([handoff](../src/Kora.Application/Presentation/PreparedLocalSearchEventArgs.cs), [controller](../src/Kora/LocalFilePreviewWindowController.cs), [native hook](../src/Kora/LocalFilePreviewWindow.PreparedSearch.cs)).

Only the user's separate existing native Search action invokes the same audited `LocalFileSearch`; `SearchFileAsync` / `SearchFolderAsync` retain their LocalUi-only exact-source guards.
Preparation adds no read/search/refresh, metadata capture, Attach, consent, model/provider, clipboard or retention authority; query/source bodies never enter response/transcript, logging, audit, tags/Baggage or persistence ([guards](../src/Kora.Application/ViewModels/MainViewModel.Files.cs)).

Deterministic [portable admission tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.PreparedLocalSearch.cs) and [headless native tests](../tests/Kora.Windows.IntegrationTests/PreparedLocalSearchWindowTests.cs) cover preparation versus separate execution.
This is bounded I only: existing preview consent, retained attachment and unavailable source-use policy stay separate; no unrelated E/RC-Q, physical/audio/provider or installed-accessibility qualification is claimed.

Experiment disposition: maintained deterministic chunk/citation, bounds,
revocation/cancellation/isolation and no-egress tests now cover this exact
production slice. No executable experiment is superseded or removed. In
particular R02 inference answer quality, actual resource/offline/cessation,
RT1/RT2/MG1 and durable storage/enterprise-cache trials remain distinct and
retained. This slice does not qualify grounded reasoning or installed native
accessibility acceptance.

## Delivered Opt-In Markdown Structure-Aware Retrieval - 2026-10-11

R26.1g adds **Markdown structure-aware (opt-in, this view only)** in the exact native volatile file/folder preview and retained Session attachment viewer. Each new/replaced/reopened view starts at **Lexical lines (default)** / `lexical-lines-v1`; nothing is saved or changed globally. Choose the second mode, then explicitly Search. Queries remain unmodified native-only text.

The stable selected version is **`markdown-structure-v2`**. It uses the same `ILocalFileRetrieval`, `LocalFileSearch` action and already-admitted exact snapshot, not a new read, path lookup or model route. Retained search still resolves and rechecks the validated historical storage snapshot, owning session/storage revision and native inspection authority; it does not re-read the old original path.

Mode/query generation, exact callback source and result policy metadata remain bound together. Changing mode/query, Cancel local search, replacing/revoking a source or closing the view clears citations and cancels/suppresses late work.
Unsupported enum values or returned policy versions deny explicitly; there is no fallback to v1. Mode selection/restoration grants no model, transcript, audit or retention authority.

The deterministic scanner operates only on the original exact Unicode projection, without regex evaluation, renderer, parser package, resource loading or execution. `.md` / `.markdown` use this documented **bounded subset**, not full CommonMark:

- Top-level ATX headings: one to six `#`, followed by space/tab or end of line; at most three leading spaces. Labels retain authored source characters, including closing markers, bounded to 64 UTF-16 characters without splitting a scalar.
- Standalone single-line Setext headings: an eligible text line at a block start followed immediately by a uniform `=` or `-` delimiter with at most three leading spaces and optional trailing space/tab. Multiline Setext, list/quote/indented/table/raw-HTML/inline-code candidates are not promoted to headings.
- Backtick or tilde fences: at least three identical markers, at most three leading spaces; backtick opening info cannot contain backticks. Closing markers must match, be at least the opening length and have only trailing spaces/tabs. Short, mixed or info-bearing apparent closers stay code text.
- Heading/delimiter/fence-looking lines inside a fence never create section authority. Unclosed fences stay opaque through EOF. List containers and raw HTML are conservatively opaque until a blank line; unfinished inline backtick spans suppress subsequent heading recognition until the matching run. Nested/container/full inline Markdown semantics are unsupported.
- Small whole sections, paragraphs and code blocks stay contiguous when within 2,048 UTF-16 characters and 128 physical lines. Larger sections split at authored block boundaries; oversized blocks/long lines are bounded with the original scalar/CRLF-safe cuts. These and incomplete formats are labelled **structural fragments**, never falsely advertised as intact blocks/sections.

Lists, tables, code bodies, links, image references and authored alt text remain exact inert source text and are lexically searchable. There is no rendering/normalization of excerpts, executable-link navigation, URL fetch, download, OCR or generated image description. Passive exact citation-to-source navigation is delivered separately below.
Heading context separately cites an actual authored bounded label span/location; excerpt start/length and one-based UTF-16 line/column end-exclusive citations still reference the original projection. BOM bytes remain in the unchanged source digest/reference.

`.txt` explicitly uses the original line chunks under the selected v2 result policy. Other formats remain inadmissible under `LocalFilePolicy`; filenames never bypass strict UTF-8/control/size admission. A folder chooses structural or plain chunks per supported file, with **one global ranking/budget**, not per-file top-eight merging.

There is **no overlap** or duplicate chunk inflation. Existing canonical OR ranking, NFC/invariant terms, ordinal canonical file order/source-offset ties, frequency saturation at 16, 256 UTF-16 / 512 UTF-8 query bytes, 32 unique terms / 64 term characters and eight citations / 16 KiB combined UTF-8 excerpts are unchanged.
Total matching chunks/truncation and cancellation checks remain explicit. Forced cuts cannot invent matching token prefixes/suffixes.

Source/session/task/privacy/ownership/control/call generations, native release, trusted requested/terminal audit and resource-quiescence fences remain authoritative. No database schema, index, derived body, preferences, provider/egress, clipboard, history, speech or durable knowledge authority is added. Pure lexical citations do not qualify reasoning or full Markdown/managed-source support.

Maintained evidence: [policy and shared ranker](../src/Kora.Core/Context/LocalFileLexicalRetrieval.cs), [bounded scanner](../src/Kora.Core/Context/LocalFileLexicalRetrieval.Markdown.cs), [v1 regression](../tests/Kora.Core.UnitTests/Context/LocalFileRetrievalTests.cs).
[V2 exact golden/boundary corpus](../tests/Kora.Core.UnitTests/Context/LocalFileMarkdownRetrievalTests.cs) and [shared broker](../tests/Kora.Tools.UnitTests/Files/LocalFileSearchTests.cs) prove the selected algorithm and publication route.

[Run selection and late discard](../tests/Kora.Application.UnitTests/Presentation/LocalFileSearchSelectionTests.cs) and [retained validated workflow](../tests/Kora.Application.UnitTests/Hosting/SessionWorkspaceServiceTests.Attachments.cs) cover the admitted host consumers.
[Native selector/cancellation/source replacement](../tests/Kora.Windows.IntegrationTests/SessionAttachmentWindowTests.cs) covers the native consumer, not installed OS/accessibility acceptance.

Experiment assessment: this corpus is exact equivalence only for deterministic chunk/rank/citation and admitted native search consumers. No detector or experiment is retired.
The [local-inference proof](../experiments/r02-local-inference-proof/README.md) consumes the real reasoner/probe, not this retrieval contract; selected synthetic-context answer quality, CPU/context/resource, offline/cessation witnesses remain unique.
The [runtime proof](../experiments/r02-runtime-proof/README.md) retains actual SDK/provider mediation and failed-hook witnesses. The [RT1 .NET receipts](../experiments/r02-dotnet-control-proof/EVIDENCE.md) retain selected-profile runtime/session-I/O/containment observations and rejected or unexecuted candidate evidence; lexical source scanning is not their equivalent.
Unique storage/migration/recovery, native/offline/resource, RT2/MG1, quality and historical failure witnesses remain retained. Full R26, model grounding/provider qualification and installed-native gates remain open.

## Delivered Passive Exact Citation-to-Source Navigation - 2026-10-11

R26.1h adds selectable native citation rows and **Show exact cited source span** in admitted text/Markdown file/folder previews and retained Session attachment inspection. Only that explicit action focuses an accessible, non-selectable inert source-span control. Search completion and row updates never steal focus. The full original source remains below; the span shows exact end-exclusive line/column and UTF-16 offsets with host-held source/revision/item IDs and the original-byte SHA-256 digest.

The current successful admitted query/mode/result generation owns each selectable row by reference. A serialized/equal-but-foreign citation cannot select a source. Folder navigation switches the actual admitted item by its exact complete reference, never display name, path, list index or query text. Identical text/digests or duplicate display labels do not confer identity.

One [Core resolver](../src/Kora.Core/Context/LocalFileLexicalRetrieval.Citations.cs) checks exact source/revision/item/digest, nonnegative bounded range without addition overflow, scalar/CRLF boundaries, exact original excerpt and one-based UTF-16 start/exclusive-end locations. It reuses the authoritative v1/v2 chunk scanner to validate declared heading context and structural-fragment meaning; no second Markdown parser or normalized/rendered projection is introduced.

Navigation revalidates current host admission immediately before resolution and after awaited work. Retained inspection re-reads only the authoritative current private stored snapshot through `SessionFileAttachmentService.Read`, checking the original exact session/storage revision and file reference; no cached-body fallback or original-path lookup occurs. This is passive disclosure of the same historical retained admission, not filesystem content acquisition, live freshness, new attachment consent or renewed session activity/cleanup.

Query/mode changes, Cancel local search, citation selection replacement, source replacement/refresh/revocation, privacy/call/ownership/lifecycle changes and close/disposal clear navigation and cancel/suppress late callbacks. Removed/replaced retained references never resolve newer bodies. Failure is explicit unavailable/recovery; no wrong-file/default fallback. Native work/handlers are bounded by existing original file/folder and citation limits, with one span control rather than a control per source line.

Navigation adds no filesystem read/picker/refresh, source attachment mutation, executable link/script, URL/image/browser fetch, clipboard selection/copy, model context/egress, speech/history/export, preferences/index or body logging. BOM/original byte provenance, unchanged default `lexical-lines-v1`, opt-in `markdown-structure-v2`, ranking/truncation and trusted search/audit publication remain unchanged.

Evidence: [exact/forged Unicode/BOM/range/heading policy](../tests/Kora.Core.UnitTests/Context/LocalFileCitationTests.cs), [generation and row-reference admission](../tests/Kora.Application.UnitTests/Presentation/LocalFileCitationSelectionTests.cs), [actual native single/folder span, viewport, focus/accessibility and stale callback proof](../tests/Kora.Windows.IntegrationTests/LocalFileCitationWindowTests.cs), and [retained real-store/native span with no recapture](../tests/Kora.Windows.IntegrationTests/SessionAttachmentWindowTests.cs).

Experiment equivalence is limited to deterministic exact citation resolution and these native passive consumers. No experiment or historical receipt is retired: native/runtime/offline/resource, privacy/storage/migration/recovery, containment, RT1/RT2/MG1 and model-quality proofs remain unique. Full CommonMark/rendering, managed sources, model grounding/providers, installed/OS-accessibility acceptance and broader R26 qualification remain separate and open.

## Bounded Current Immutable Single-File Local Answer Implementation - 2026-10-20

Exact typed/current-name ACTIVATED **`answer file`** opens the native question control for the already admitted current **single-file** volatile snapshot.
It does not choose a source, read a path, reuse a folder/retained attachment, or send a question to a model.
Inline questions/paths after the fixed command are rejected ([command](../src/Kora.Core/Context/LocalFileCommand.cs), [routing](../src/Kora.Application/ViewModels/MainViewModel.Files.cs)).

The native **Use this exact snapshot once to answer locally** control is a separate explicit source-use request for the displayed exact revision and unchanged native question. Preview consent is not model-use consent.
Question/excerpts/claims stay native and volatile, outside conversation history, speech, clipboard and retained bodies ([native control](../src/Kora/LocalFilePreviewWindow.Answers.cs), [host continuation](../src/Kora.Application/ViewModels/MainViewModel.FileAnswers.cs)).
The control separately discloses creation of the exact displayed `Local context` session and one-shot task, distinct from volatile file-preview lineage.
Only content-free host lifecycle receipts are durable; preview consent never authorizes that creation implicitly.

The registered per-action [LocalFileAnswer](../src/Kora.Tools/Files/LocalFileAnswer.cs) holds the existing [immutable preview broker](../src/Kora.Tools/Files/LocalFilePreview.cs) generation/cancellation/terminal-audit boundary through retrieval and answer resolution.
It uses `ILocalFileRetrieval` / `lexical-lines-v1`, the unchanged 256-character / 512-UTF-8-byte question, 32 unique terms / 64-character term and eight-citation / 16-KiB-excerpt limits ([policy](../src/Kora.Core/Context/LocalFileRetrievalPolicy.cs)).

Exact source/revision/item/digest, original session/task, deliberate origin and source generation are revalidated.
Every excerpt uses the authoritative immutable citation resolver before context construction and again before publishing; source text is never reconstructed or reread.
Cancel/clear/refresh/revocation, native question edit/close and private/call/ownership changes retire the owned work and suppress late claims ([action](../src/Kora.Tools/Files/LocalFileAnswer.cs), [generation owner](../src/Kora.Application/Presentation/LocalFileAnswerSelection.cs)).

The [file-specific binding](../src/Kora.Core/Context/LocalFileAnswerContext.cs) creates only `LocalOnly` `ModelContextEvidence` with host-owned opaque evidence IDs and exact unmodified excerpts.
It uses the existing [ModelContextEnvelope serializer](../src/Kora.Core/Dependencies/ModelContextEnvelope.cs) with `ModelToolMode.None`; complete JSON and escaping must fit 32,768 UTF-8 bytes.
The authoritative serializer emits an empty tool catalogue; file code does not duplicate envelope serialization.
Over-limit input is explicitly rejected, never shortened. Lexical matching count/truncation is disclosed separately from complete envelope admission.

The [file action](../src/Kora.Tools/Files/LocalFileAnswer.cs) directly consumes the shared Core [IModelContextRunner](../src/Kora.Core/Dependencies/IModelContextRunner.cs), not a file-specific executor interface, Application wrapper, durable-task bridge, direct reasoner/transport or hosted fallback.
After affirmative file source-use admission, it submits the exact explicitly reviewed target for genuine durable admission.
Only an issued [IAdmittedModelContextTurn](../src/Kora.Core/Dependencies/IAdmittedModelContextTurn.cs) may supply the exact request used to construct the envelope/evidence.
The proposed target and freshness callback alone confer no host authority. Rejected admission constructs no envelope; an abandoned, foreign or unknown-generation lease is disposed without model work.
The shared executor alone records session/control intent, typed local-context dispatch and truthful terminal receipts, calls `ModelTurnHost` with explicit Local selection and waits for physical quiescence.
The file action requires an exact original-request receipt with `Completed` / durable `Succeeded` before publishing successful cited claims; absent, mismatched or uncertain receipts never produce an answer.
Successful host responses alone may resolve the strict file-answer JSON schema.
Each supported claim needs at least one exact admitted evidence ID; unknown, duplicate, malformed, foreign-request or hosted references produce no claims.
Each returned ID resolves through the host-held source/revision/location map and exact `ResolveCitation` immediately before claim disclosure.
Unsupported and no-lexical-evidence are explicit distinct outcomes ([binding](../src/Kora.Core/Context/LocalFileAnswerContext.cs)).
Each explicit native answer request consumes the snapshot's one-shot target, including invalid, unavailable, cancelled or uncertain outcomes.
The answer control disables further dispatch before awaiting work; edits still clear claims/citations but never silently create another session/task, retry the old target or choose an existing target.

Claim references map back to host-held original citation objects, not model-supplied paths/locations.
Native rows and per-claim descriptions retain original source/revision/item/digest and UTF-16/line/column spans; explicit **Show exact answer citation** reuses inert exact-source navigation.
Citation identity validation is not a claim of semantic truth or answer-quality qualification ([native answer view](../src/Kora/LocalFilePreviewWindow.Answers.cs), [shared navigation](../src/Kora/LocalFilePreviewWindow.Citations.cs)).

**Exact consumed I boundary:** the required sensitive-content/source-use admission service is not composed.
[ILocalFileModelUsePolicy](../src/Kora.Core/Context/ILocalFileModelUsePolicy.cs) is distinct from read consent.
The production [UnavailableLocalFileModelUsePolicy](../src/Kora.Application/Dependencies/UnavailableLocalFileModelUsePolicy.cs) reports unavailable with the explicit `SourceUsePolicyMissing` decision, surfaced as `SourceUseUnavailable`, before shared admission, evidence/envelope construction or dispatch.
Deterministic fakes exercise admitted and denied paths without asserting that source contents are safe.
Awaited source policy receives the exact immutable revision, unchanged question and selected citations with original host correlation.
Revocation/cancellation while classification is pending suppresses context construction and execution; a source-policy I/O/state failure records a failed terminal file audit, logs only its failure type and publishes no claims.
Evidence IDs from another question are rejected even when both questions use the same immutable source revision.

**Shared execution integration:** consumes the peer-owned two-phase Core contract at `470a03444d07a07a776f82572ce87dff90cf4bbf` plus the explicit source-policy boundary at `d881bd2d85d99b0a554625213b3e5576bc224b1c` and their committed dependencies.
The reviewed-target refinement at `4109d2857a9ae746ec5fbfc7ba92e02037ad91c1` keeps original volatile source lineage separate from the distinct durable execution target.
The native view displays the fixed target before consent; file code never treats the source's transient session/task as durable execution authority, and evidence binds only the target request returned by actual admission.
The source gate and broker/native cancellation remain attached through the shared executor and [model-host consumer fence](../src/Kora.Application/Dependencies/ModelTurnHost.Execution.cs), including asynchronous revalidation, tool rejection and final disclosure.
This consumer does not edit shared execution, serializer, transport, protocol or durable writers ([file action](../src/Kora.Tools/Files/LocalFileAnswer.cs), [shared execution contract](Local_Inference.md)).

Production model registrations remain unqualified/unavailable ([model registrations](../src/Kora.Application/Dependencies/ModelTurnHost.cs)).
This bounded implementation does not enable or qualify R26.3, hosted egress, managed libraries/corpora, embeddings, folders, generic content tools, provider/containment or installed/hardware trials ([I/E/Q policy](Implementation_Roadmap.md#delivery-and-qualification-dependency-policy)).

Maintained deterministic evidence:

- [Exact binding and malformed output](../tests/Kora.Core.UnitTests/Context/LocalFileAnswerContextTests.cs).
- [Action/read/limits/revocation/audit](../tests/Kora.Tools.UnitTests/Files/LocalFileAnswerTests.cs).
- [Selection ownership](../tests/Kora.Application.UnitTests/Presentation/LocalFileAnswerSelectionTests.cs).
- [Shared durable executor consumer](../tests/Kora.Application.UnitTests/Dependencies/LocalContextTurnWorkflowTests.FileAnswers.cs).
- [Exact command routing](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.FileAnswers.cs).
- [Headless native controls](../tests/Kora.Windows.IntegrationTests/LocalFileAnswerWindowTests.cs).

No experiments or E/Q gates are retired or rerun by this increment.

The earlier `d5cfa31` disposal-outcome failure is superseded by the peer's two-phase revision and maintained [shared lifecycle checks](../tests/Kora.Application.UnitTests/Dependencies/LocalContextTurnWorkflowTests.cs).
The consumed workflow additionally tests malformed dispatch readback, exact terminal identity, abandonment, queued cancellation, source-validation failures and exception-safe terminal cleanup ([boundary tests](../tests/Kora.Application.UnitTests/Dependencies/LocalContextTurnWorkflowTests.Boundaries.cs)).
The unchanged [100% portable line/branch gate](../.github/workflows/ci.yml) remains required, alongside the Release build and maintained deterministic native/SQLite consumers; passing those checks is not production source-use qualification.
Production sensitivity/source-use admission and model qualification remain unavailable. Passing fake/source-identity checks do not qualify semantic answer grounding, an empty-tool transport or real source classification.
This is a bounded production-path implementation, not a shipped functional file-answer capability: the missing production contract must classify/detect secrets and sensitive content for the exact immutable revision, question and selected excerpts before use.
Preview consent, narrow credential screening, a fake allow, local-only disclosure and generic durable admission do not fulfill that source-use detection contract. No permissive detector or automatic fallback is composed.

## Native admitted-source soft wrapping

The native **Wrap admitted text** checkbox is presentation-only for the existing
complete original source control shared by single-file, captured-folder item and
already admitted retained-attachment views. All three retain their actual default
`NoWrap` / horizontal `Auto` scrolling. Checked selects `Wrap` / horizontal
`Disabled` scrolling so the source measures against the current viewport, including
resize and reader scale; vertical scrolling stays unchanged. No text-length
restriction or source transformation is introduced.

[One current-source policy](../src/Kora/LocalFilePreviewWindow.Wrapping.cs) resets
the volatile choice on source replacement, folder selection (including returning
to a previous item), review, private revocation and close. No per-source cache,
preference, saved/global setting or new authority exists. Retired handlers detach
and cannot affect a replacement, including identical content. Metadata review has
no checkbox. Retained source admission and its original read channel are unchanged:
toggling performs no read, revalidation, capture, consent, activity renewal or I/O.

Only `TextWrapping` and source horizontal scrollbar behavior change. Original
bytes/digest, decoded text, Unicode, tabs, newlines, literal query/case/count/ordinal/
UTF-16 matches, ranked results and exact citation coordinates remain untouched.
Visual rows must never be described as logical lines or citation locations.
Wrapping does not rerun Find/search, reset its cursor or change the span/notices.
Native layout/BringIntoView continuations also require the same current source,
span and layout generation before focus; wrapping cannot complete stale focus.

[Headless tests](../tests/Kora.Windows.IntegrationTests/LocalFileWrappingWindowTests.cs)
cover actual profile defaults, both directions, long-line viewport measurement,
resize/scale, accessibility/keyboard and source/focus revocation. This consumes only
the existing admitted-source/native presentation I subset. Portable production is
unchanged; its existing raw 100% line and branch gate remains required with no new
exclusions. Installed accessibility, provider, hardware and unrelated E/Q evidence
are not implementation prerequisites or newly claimed qualifications.

## Delivered Native Current-Source Literal Find - 2026-10-12

**Find first / Find next / Find previous / Clear literal Find** navigate only the currently displayed exact immutable text file: volatile single-file preview, the current captured-folder selector item, or an exact retained Session attachment. This native presentation control is separate from ranked lexical/Markdown retrieval and citations. Typing does not scan, change files or move focus.

The one-line literal uses case-sensitive `StringComparison.Ordinal`, without trimming, normalization, token OR, regex, wildcard, fuzzy or semantic interpretation. Spaces and metacharacters are preserved. Shared 256 UTF-16 / 512 strict UTF-8 bounds apply; malformed Unicode, control/format characters and line/paragraph separators reject explicitly. No query enters typed/PTT transcripts.

First scans from zero. Next starts after the selected match's exclusive end; Previous reverses the same counted forward nonoverlapping sequence, not an overlapping alternative. Next/Previous require a canonical match; exhaustion retains the anchor ordinal and total but clears the span and claims no current match or focus. First alone reports zero matches ([shared policy](../src/Kora.Core/Context/SnapshotLiteralFind.cs)).

One span is selected per action, with the unchanged query's UTF-16 length. No all-occurrence collection or index is built. The inert panel shows literal labels, original end-exclusive UTF-16 offsets and exact source/revision/item/original-byte digest, not a ranked citation or heading. Complete source remains unchanged and non-selectable below ([window](../src/Kora/LocalFilePreviewWindow.Find.cs)).

### Exact immutable LocalFile occurrence count and ordinal - 2026-10-18

Explicit Find now reports an accessible exact total and selected ordinal for only the admitted, currently displayed immutable `LocalFileRevision`. It does not count other folder items, retained attachments, corpus/retrieval results or live disk. Empty and no-match originals report total 0 and selected ordinal 0 ([window tests](../tests/Kora.Windows.IntegrationTests/LocalFileLiteralFindWindowTests.cs)).

The [LocalFile adapter](../src/Kora.Core/Context/LocalFileLiteralFind.cs) and [clipboard adapter](../src/Kora.Core/Context/ClipboardLiteralFind.cs) reuse one [authoritative counted rule](../src/Kora.Core/Context/SnapshotLiteralFind.cs). For `aaaaaXaa` / `aa`, the forward sequence starts at 0, 2, 6 and Previous from 6 selects 2, not 3. Ordinary uncounted Find and ranked retrieval remain unchanged ([policy tests](../tests/Kora.Core.UnitTests/Context/LocalFileLiteralFindTests.cs)).

Totals are scanned from displayed immutable text without an index, cache, count persistence or an extra read. Query and canonical cursor admission precede retained reads. Publication still awaits the existing asynchronous full-source validation, exact identity/body, generation, privacy, cancellation and focus fences ([window](../src/Kora/LocalFilePreviewWindow.Find.cs), [tests](../tests/Kora.Windows.IntegrationTests/LocalFileLiteralFindWindowTests.cs)).

This consumes the existing immutable-source, shared literal policy and native Find I subset only. It adds no model/provider/device/clipboard-write/authority path. E/Q and installed accessibility qualification remain separate; unqualified runtime/provider/egress/wake/profile-original writers remain unavailable ([delivery policy](Implementation_Roadmap.md#delivery-and-qualification-dependency-policy)).

The shared handler captures source instance, exact query, cursor and view generation before work. Each action reuses citation navigation's authoritative resolver and current-admission predicate before disclosure and in-window focus. Retained actions revalidate only the private SQLite original-body/session/storage/source snapshot, never the original filesystem path, capture, attach or replace.

Folder selection and citation navigation switch the actual Find subject and clear the old query/cursor. Query edits, ranked query/mode/search/cancel, citation selection, source/review recomposition, Clear and close retire pending Find.

Existing ownership/call/session/retention/replacement/removal shutdown clears content and query. Late callbacks cannot retarget identical bytes or a newer view. Stale/unavailable/invalid inputs disclose no match or fallback.

No new command/model-tool descriptor, data-read authority, publication, activity renewal, clipboard/drag/export, HTML/link/browser/image execution, speech/history, preferences, cache, model context or egress is added. Existing inspect/search commands still focus ranked native inspection; literal navigation is native-only.

Original 256 KiB file bounds, sixteen retained bodies, storage schema/format, citation ranking/eight-result/16 KiB limits and private audit fences are unchanged.

Evidence: [portable span/query tests](../tests/Kora.Core.UnitTests/Context/LocalFileLiteralFindTests.cs), [native surfaces, selection, cancellation and focus](../tests/Kora.Windows.IntegrationTests/LocalFileLiteralFindWindowTests.cs), and [retained Done/private SQLite after original deletion](../tests/Kora.Windows.IntegrationTests/SessionAttachmentWindowTests.Find.cs).

These fixtures qualify only this bounded native presentation path. No experiment, historical failure receipt or RT1/RT2/MG1/PV1, offline CPU/inference, containment, hardware, installed-native/accessibility, physical-storage or physical STOP proof is retired. Broader R26/managed knowledge/model answers remain unqualified; profile-original editing remains security-boundary deferred.

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
| R26.1f - Reviewed retained attachment replacement | Delivered: fresh picker and separate exact old/new review; atomic own-slot swap, unchanged limits | R26.1e capture, source revocation, copy inventory and native lifecycle | Native controller, SQLite capacity/CAS/rollback/certification-hold/recovery tests; no versions, watchers or model authority; installed acceptance open |
| R26.1g - Opt-in Markdown structure-aware retrieval | Delivered: run-only native selector; `lexical-lines-v1` default unchanged, `markdown-structure-v2` bounded inert subset over the same admitted file/folder/retained snapshots | Existing exact preview broker and retained inspection authority; no new format/read/storage/model authority | [Golden scanner/bounds](../tests/Kora.Core.UnitTests/Context/LocalFileMarkdownRetrievalTests.cs), [mode generations](../tests/Kora.Application.UnitTests/Presentation/LocalFileSearchSelectionTests.cs), [actual native consumer](../tests/Kora.Windows.IntegrationTests/SessionAttachmentWindowTests.cs); full CommonMark/model/installed acceptance remains open |
| R26.1h - Passive exact citation-to-source navigation | Delivered: selectable current rows and explicit exact original-source span reveal/focus; correct exact folder item and authoritative retained-revision revalidation | R26.1b/c/e/g exact immutable sources and native ownership/privacy/lifetime fences; no new acquisition or model authority | [Exact policy](../tests/Kora.Core.UnitTests/Context/LocalFileCitationTests.cs), [generation ownership](../tests/Kora.Application.UnitTests/Presentation/LocalFileCitationSelectionTests.cs), [native measurable span/focus](../tests/Kora.Windows.IntegrationTests/LocalFileCitationWindowTests.cs), [retained real-store consumer](../tests/Kora.Windows.IntegrationTests/SessionAttachmentWindowTests.cs); installed/accessibility/model gates remain open |
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
