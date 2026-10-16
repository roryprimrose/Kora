# Human Interaction and Persistent Sessions

Status: agreed product direction; bounded durable question/grant/session metadata,
exact session/task controls and consolidated schema-v9 task/question/queue/provider-policy/required-audit
authority with bounded ordered interaction history and inventoried configurable
session retention implemented. The deterministic
local-version queue and its exact pending cancellation are delivered; cancellation
of the separate genuine current-run local-version pre-dispatch question wait
remains gateway-bound. Native exact-session composer, explicit saved drafts and
immutable submitted user messages with truthful unavailable host outcomes are
delivered below. Full conversation generation, effect and model-assisted routing
integration remains proposed.

Related: [Architecture](Architecture.md), [Work Management](Work_Management.md), [Information Display](Information_Display.md), [Security](Security_Data_Flows.md), [User Configuration](User_Configuration.md), [Acceptance Criteria](Acceptance_Criteria.md).

This is the canonical interaction/session contract. It replaces the former single-conversation, single-executor, memory-only-history direction.
Windows login sessions, voice capture generations, provider/SDK conversations, and Kora work sessions are different identities; none substitutes for another.

## Delivered passive exact-conversation lexical search — 2026-10-13

**Sessions > Exact-session conversation and composer > Search this conversation** reads original retained USER bodies and separate typed HOST outcome/reason/task-state fields in only the explicitly selected session. Active and retained Done are readable without Resume.
This is not the older Question/Answer receipt `HistorySearch`, model history reasoning, global body search, an FTS index or retrieval upload.

The optional exact **All / USER / HOST** field filter defaults to All. All searches the
union of immutable original USER body and typed HOST outcome, reason and expected
task-state strings; USER searches only the original body; HOST searches only those
typed fields, never assistant prose or status-looking words in the USER body.
The filter composes with the same literal query, selects matches rather than
stored rows or page admission, and never rewrites a message's role. Every consumed
chronological source row is validated before matching or exclusion. Zero matches
are an exhausted observation when Next is absent, not unavailable content;
undefined role values fail explicitly without reading.

Optional exact typed **ModelTurnOutcome**, **ModelTurnReason** and expected
**HostTaskState** selectors each include **Any**. The portable
[terminal filter](../src/Kora.Core/Conversation/ConversationTerminalFilter.cs)
owns validation and matching once. All selected predicates AND together and
with the lexical query/role; only terminal HOST metadata can satisfy them.
Nonterminal USER rows retain null outcome/reason, and status-looking USER text
cannot supply metadata. USER-only plus any terminal predicate is valid and
truthfully exhausts with zero, without changing role or clearing predicates.
Every defined enum value is selectable, including combinations with no possible
retained terminal; undefined values explicitly fail before reading. All scanned
rows are fully validated before exclusion, even for valid zero-match criteria.

Optional inclusive **Minimum message sequence**, **Maximum message sequence**,
**Submitted from UTC** and **Submitted through UTC** predicates AND with the
lexical query, role and every terminal predicate. The portable
[range filter](../src/Kora.Core/Conversation/ConversationRangeFilter.cs) owns
parsing, validation and matching. Empty means Any. Sequences use strict invariant
unsigned decimal input, bounded by 1 through `ConversationText.MaximumSessionMessages`
(100); submitted times use exact round-trip `yyyy-MM-ddTHH:mm:ss.fffffff+00:00`,
without trimming, offset conversion, `Z`, relative dates or timestamp repair.
Each minimum must be <= its maximum; equal endpoints include the original
message. Time bounds use the full representable UTC `DateTimeOffset` domain,
not the current wall clock or a derived session timestamp. Sequence bounds
beyond a snapshot's ceiling validly yield zero, not a wider or different read.
Every consumed message is validated before range exclusion, including its body,
original session/generation/sequence and typed HOST status.
All four bounds join role/terminal predicates and normalized lexical terms in
the semantic query digest and explicit page/cursor identity. Changing any bound
refuses continuation before reading. Metadata is passive: it cannot establish
chronology authority, permission, ownership, model output or session read access.

The [conversation matcher](../src/Kora.Core/Conversation/ConversationSearch.cs) reuses `SessionHistorySearch` and the authoritative `LocalFileRetrievalPolicy` tokenizer: 1–256 UTF-16 characters / 512 strict UTF-8 bytes, 1–32 distinct lexical terms of at most 64 characters, NFC/invariant whole-word OR.
Punctuation is a separator, not regex, wildcard or instruction syntax. Blank/malformed/unsupported queries fail explicitly, never inventory all bodies.

The [passive workflow](../src/Kora.Application/Hosting/SessionWorkspaceService.ConversationSearch.cs) delegates to existing admitted conversation pages; no SQL, schema, body representation or persisted query is added.
Each call scans at most the existing 100-message session bound, returns default 25 / maximum 50 complete messages and caps the complete serialized result, counters and continuation at 64 KiB. Bodies, original request/task IDs, source generation and chronological sequence stay unchanged.

Continuations and pages bind the exact role and every terminal predicate, normalized query digest and original session/generation/sequence ceiling/head revision under existing private/retention admission. Nonmatches advance; a byte-deferred match remains for Next. `Scanned` counts consumed records.
`OmittedMessages` and `UnavailableMessages` are zero because valid complete bodies fit and inaccessible/invalid sources fail the entire read rather than becoming success-shaped omissions. Exhausted snapshots are freshly revalidated through the provider under the same private admission/retention epoch before publication.

Later append cannot expand a continuation's ceiling; terminal updates invalidate its original head revision and require fresh Search. Lifecycle, removal, retention/private/owner changes, query or any role/terminal-filter edits and Cancel/Clear revoke results, selected match/navigation, continuation and late callbacks. Editing performs no read; explicit Search is required. Clear resets the role to All and terminal predicates to Any.
Range edits have the same cancellation, selected-detail retirement and stale
callback fences. Clear, source/private revocation and last-owner close also
empty all four range fields without touching composer or saved drafts.
Search does not alter composer body/baseline, dirty-draft decisions, held submissions, saved drafts, question/voice focus, meaningful activity, cleanup, Keep, provider preferences or task/approval authority.

Only admitted original messages appear as inert native USER/HOST rows, with no generated summary, semantic ranking, rich actions, copy/export, clipboard/speech, network/model calls or hidden context assembly.
Raw logs, attachments, memories, templates, accounts and credential-rejected bodies are not search sources. Query/body/excerpt/name/path never enter diagnostic messages, audit or activity tags. Installed accessibility, hardware, inference and physical-storage/STOP qualification remain gated.
Query and filter values are volatile only: no persistence, logging, activity tags,
audit payload, model context, speech, clipboard/export or activity renewal.
Typed HOST chrome and original bodies remain unchanged; no generated output,
receipt-history search, schema/index/cache or provider/runtime authority is added.

## Delivered exact-session conversation/composer foundation — 2026-10-12

### Saved conversation draft catalogue — 2026-10-13

**Tray > Saved conversation drafts (metadata only)** opens without reading. Explicit Refresh/Next/Cancel observes only saved nonempty composer draft metadata across retained private sessions.
All (default), Active and Done filters show exact immutable session ID, current stored state/generation, optional inert private name/metadata revision, saved revision and SQL UTF-8 byte/Unicode scalar lengths. No body, excerpt, body digest or invented saved timestamp is projected.

Browse remains the default. **NameSubstring** and **ExactId** reuse the authoritative [session metadata policy](../src/Kora.Core/Storage/SessionListSearch.cs): ordinal case-sensitive literal name matching, NFC/no-control/format/no-surrounding-whitespace, 120 scalars / 480 UTF-8 bytes; or a separate nonempty canonical lowercase D-format ID.
An ID-shaped name is only a name. Unnamed sessions cannot match names but can match exact IDs. Invalid/empty queries never trim, normalize or fall back to Browse; **Clear query (Browse)** resets explicitly without reading.
Optional **Exact saved revision**, **Minimum UTF-8 bytes**, **Maximum UTF-8 bytes**,
**Minimum Unicode scalars** and **Maximum Unicode scalars**
compose with Browse/name/exact-ID and All/Active/Done. The portable
[metadata filter](../src/Kora.Core/Storage/SavedDraftMetadataFilter.cs) owns parsing,
validation and matching: empty means Any, revision is a positive Int64, and
inclusive byte bounds are 0 through `ConversationText.MaximumUtf8Bytes` (16384),
with minimum <= maximum. Inclusive Unicode scalar bounds are 0 through
`ConversationText.MaximumUtf16Characters` (4096), also with minimum <= maximum.
Scalar matching uses only already exposed validated `SavedDraftSummary.UnicodeScalars`,
never body reads, inference or code-unit filtering. Nonempty input accepts only invariant decimal digits;
no signs, whitespace repair, separators, truncation or clamping. Invalid fields
visibly refuse Search/Refresh. Zero byte or scalar bounds can truthfully match no nonempty draft.
Optional **Exact observation source GUID** and **Exact device profile GUID** compose
with every existing predicate using AND. Empty fields mean Any; nonempty fields
must be complete nonempty canonical lowercase `D` GUIDs. Braces, uppercase,
whitespace, trimming, partial matches, repair and reinterpretation are refused.
The portable metadata filter owns this parsing once and reuses host identity
validation for the typed profile. Source matches `SavedDraftSummary.Source`;
profile matches `SavedDraftSummary.Profile.Value`, both exact. These IDs are
metadata/correlation only, never device ownership, identity or authority.
Optional inclusive **Minimum/Maximum session generation**,
**Minimum/Maximum name metadata revision**, **Minimum/Maximum control revision**
and **Minimum/Maximum authority ceiling** ranges AND with all these predicates.
Empty means Any. The shared strict decimal admission accepts positive Int64
generation, name revision and ceiling, and nonnegative Int64 control revision;
each supplied minimum must be <= its supplied maximum. Leading decimal zeros
retain their existing semantic value; no signs, whitespace, separators, overflow,
clamping or repair are accepted. Either name-revision bound requires validated
name metadata: unnamed rows never become revision-zero or unknown matches.
Every `SavedDraftSummary` is validated before matching, including nonmatches.
Session generation, control revision, ceiling and digest are observed provenance
only, never current authority, permission, body integrity, device ownership or
navigation authority. Exact selection still requires independent current admission.
The filter digest binds all numeric and GUID predicates; the signed cursor binds
the complete filter alongside every existing query/source/private/control/head
and authority fence. Nonmatching source/profile criteria do not bypass validation
of any scanned saved-draft summary, metadata name or authority prefix.
Metadata predicates apply only after existing source/authority-prefix and candidate
metadata validation; irrelevant authority commits are still validated before disclosure.
No body decoding or inference is added.
Typing, query mode, lifecycle and revision/byte/scalar/source/profile/provenance-range filter edits immediately invalidate the catalogue page/cursor/selection/exact navigation and cancel pending callbacks without I/O or changing the current Sessions target/composer. **Search / refresh metadata** explicitly observes the new query; no first match is automatically selected. Clear and private/source/close revocation also clear the volatile metadata fields.
Exact ID seeks the existing draft-row primary key after the same mandatory private source/authority validation. Missing/cleared/unretained/Removed/nonmatching targets end without falling back to a list; unavailable or broken authority remains a recovery failure, not a healthy empty inventory.

Each click consumes at most 50 candidate draft slots and returns at most 25 complete rows / 64 KiB. Cleared/null/empty slots consume scan positions but are not saved-content results; never-saved sessions and question/parameter drafts are not sources.
Empty pages can have Next. End is an observation, not a global count or immutable inventory. Canonical-UUID keyset pages are mutable; changes behind the cursor require Refresh.

Private signed continuation binds the store/profile/current run, native viewer, private control revision, query kind/digest (or Browse), exact saved revision/byte/scalar bounds, state filter and last consumed exact ID. Complete typed audit-chain validation precedes disclosure in bounded existing 4096-commit segments; pending pages contain no draft rows.
Any committed authority change invalidates continuation. Existing consolidated whole-store SQL provenance/retention checks remain costs, not per-click body decoding or a performance qualification.

Metadata observes saved revision, body presence/length and current lifecycle; it does **not** verify body integrity or admit Reload/Submit. Exact navigation re-resolves the original metadata, generation, saved revision/presence and private source authority.
Changed/cleared/retired/held sources refuse with Refresh, never follow newer bodies or names. Navigation selects only the existing Sessions metadata target; Done stays Done, without automatic body import, Resume, work/question/voice retarget or activity renewal.

The same Sessions owner protects its existing dirty buffer with Save/Discard/Cancel. Cancel leaves its target unchanged. An explicitly chosen Save can change the conservative authority head; then Refresh is required before catalogue navigation.
After selection, **Reload saved draft** is a separate explicit body read under exact revalidation; Save, reviewed Clear and Submit remain distinct. Catalogue close/source/control/privacy/lifecycle/draft revocation clears its volatile private query/rows/continuation/selection without closing unrelated Sessions content.

No schema/layout/index/file-copy change or aggregate session/draft quota is added. Existing private admission, credential rejection, required audit, retention/copy inventory, migration IDs and restart no-replay rules remain authoritative.
Names and query text/digests grant no authority; queries are never saved to preferences/history or sent to models, speech, clipboard or logs/activity tags. This metadata query is distinct from selected-session submitted-message body search and ordered interaction-receipt history search.
Native-only metadata delivery is not model retrieval, generated conversation/history assembly, provider qualification or installed accessibility acceptance.

**Sessions > Exact-session conversation and composer** is a real native
consumer of the private authority and configured provider-neutral
[turn admission](../src/Kora.Application/Hosting/SessionConversationService.cs),
not a fixed-read queue entry, question answer, model-facing tool or bootstrap
request. It always names the exact selected ID, name label, authorization
generation, metadata revision, saved-draft revision and private control epoch.
Only a fresh explicit native submission to that existing Active session is
admitted; Done is readable and requires explicit Resume before new work.

Drafts are memory-only until **Explicitly Save draft**. Read observes the saved
revision; **Reload saved draft** explicitly restores its data without replay.
One saved draft belongs to each exact session. Overwrite requires the complete
unchanged saved snapshot; **Review Clear saved draft** shows its exact body
and revision before separate confirmation. Clear removes only that saved
body, not the current memory buffer or submitted messages. Switching/closing
dirty input uses Save/Discard/Cancel; privacy, owner/control/source retirement
clears private presentation and callbacks without an implicit save.

One authoritative [content policy](../src/Kora.Core/Conversation/ConversationText.cs)
checks **4096 UTF-16 characters and 16384 UTF-8 bytes** independently, preserving
valid Unicode, combining characters, spaces, tabs and exact CR/LF/newlines.
Malformed Unicode, binary/control content, oversized bodies and empty
submissions are explicit failures, never normalization or truncation. With
valid Unicode the character bound can be reached before the byte bound;
neither is a whole-database size guarantee.

The first native composer applies one local deterministic **best-effort,
reject-only** Core screening rule before draft/message writes or model
admission. It checks structurally delimited private-key PEM candidates,
nonplaceholder Authorization Bearer (at least 24 characters) / Basic values
(bounded Base64 credentials), explicit password/client_secret/api_key
assignments (at least eight characters), GitHub's declared classic/fine-grained
token shapes, and JWT-like three-part shapes. Structured malformed candidates
are refused rather than repaired. Known placeholder examples, parameter/
environment references and ordinary credential discussion are not expanded,
evaluated or rejected merely for a word mention.

This is not a model, network verification, entropy classifier or comprehensive
DLP/secret-free guarantee. Arbitrary passwords/random strings, short values
and undeclared token formats may not be recognized; false positives remain
possible. Rejected text stays only in the user's memory draft for correction.
There is no Save anyway or silent redaction. A required content-free
requested/denied audit records only host identity and the fixed
credential-like-content reason, never the candidate, excerpt or body hash.
Audit/storage failure refuses rather than bypassing the policy.

There is **no retained session-count, aggregate saved-draft or aggregate
submitted-message quota**. Each exact session admits at most **100 submitted
user messages** in this initial UX; capacity is an error, not eviction.
Active and retained Done messages consume that session's allowance. Explicit
session disposition/deletion removes its content and ends its non-reusable
identity; it never recycles sequence IDs within a live session. Clearing a
saved draft leaves a revision tombstone and frees only that body's retention.
Pages/transactions may be bounded without limiting how many small sessions
are stored. Finite disk/required-audit failure remains explicit and fail-closed.

Schema **10→11** adds conversation tables to the existing private consolidated
session/task/audit transaction domain. It preserves old immutable queue,
history, task, grant, attachment, provider-policy and audit bytes/digests.
Missing legacy draft means none; missing audited, unknown, malformed,
noncanonical or mismatched source is `InvalidDataException` recovery, not
default empty. Exact content/source digests bind typed required requested and
terminal audit; raw bodies never enter audit envelopes, logs, activity tags,
baggage, preferences, Work/AllWork or diagnostic history.

Submit atomically commits the exact frozen body, fresh original host request
and task intent, current-run source binding and correlated typed requested/
retention receipt. It then builds one bounded envelope from the trusted
versioned host policy and **this original body only**, with no conversation
history, attachments, memories, evidence or tool results. Actual complete
serialization must fit 32768 UTF-8 bytes; the independent output budget stays
4096. Existing registered read-only descriptors are host framing, not new tool
execution permission. It reads fresh exact-session/app policy and qualification
through `ModelTurnHost` while the issuing activity is still live.

Production advertises no qualified models/adapters. The exact message remains
retained with **HOST Unavailable / UnregisteredModel** and a terminal
**Unavailable task**, not an invented assistant answer. Other admission
denials keep their actual reason/task outcome. A future qualified admission is
still **Unavailable / GenerationNotEnabled**: this increment does not run an
adapter, parse raw provider JSON, authenticate, send HTTP, or enable arbitrary
generation/tools. Rejected Ollama 0.35.1/qwen3:1.7b and 0.40.2 are never a
fallback; installed-local initial desired service is not inference consent.
Saved drafts stay independent after Submit; only the unchanged memory buffer
clears after a known terminal/readback receipt.

Read uses chronological per-session host sequence, at most **50 records /
64 KiB complete serialized page**, with an exact generation/cursor, original
sequence ceiling and observed head revision. A byte-deferred row remains for
Next; no body is truncated and a maximum body fits one complete page.
Later submissions cannot expand an existing ceiling. A terminal update within
that ceiling invalidates its captured head revision and requires Refresh
rather than mixing snapshots. Terminal status is a separate host observation,
not model content. Native retained bodies
are inert plain text with no new copy/export/HTML/Markdown-link action.
The existing `SessionHistoryEvent` remains its original receipt projection,
not a fabricated conversation or source for these bodies.

Explicit [retained original USER / typed HOST Details](Information_Display.md#delivered-retained-original-user--typed-host-details---2026-10-14) now resolves one actual observed record through the same passive conversation authority. Exact request/revision/body and selected session metadata are revalidated before publication; stale rows refuse without following newer bodies. HOST status stays typed chrome, not USER-body rewriting or assistant output.

Details does not add copy/export disclosure authority, generated history, model context or new work. Only explicitly opened records undergo bounded passive source checks; private/control/owner, selection/source, deletion/retention and disposal revoke their viewers and delayed callbacks. Existing body/page/storage/audit/retention semantics remain authoritative.

Passive reads never renew meaningful activity, resume Done, run cleanup or
create provider/question/grant authority. Submission is substantive original
input; saving/reloading/clearing drafts is not. Failed/cancelled or uncertain
commit/readback is held for explicit inspection, never duplicate Submit,
rollback/success inference or automatic retry. Duplicate request IDs cannot
append another row. Startup recovery preserves the original content and
records Interrupted for undispatched intent (Unknown for actual dispatch
uncertainty); it never submits a restored draft or replays a request.

Disposition previews/hash inventory include actual conversation/draft cells.
Logical disposition and retention deletion use the existing source-revocation,
live/Unknown/question holds, secure-delete/free-page, committed-journal and
copy-inventory checks. Uninventoried copies hold completion. Unrelated/kept
sessions, independent Perpetual grants and content-minimized required audit
remain. No forensic/media, export/provider-copy or unlimited-disk claim is made.

Maintained [strict content tests](../tests/Kora.Core.UnitTests/Conversation/ConversationTextTests.cs),
[real admission consumer tests](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.Conversation.cs),
[private SQLite migration/recovery/copy tests](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteConversationTests.cs)
and [native event binding/dirty/privacy tests](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.cs)
qualify this foundation only. [Engineering progression](Deferred_Validation.md#engineering-progression-policy)
allows these host contracts/orchestration/UI without reviving retired proof
scaffolding or rejected runtimes. Generic conversation generation, global/
voice routing, model history assembly, execution, hardware/native accessibility,
provider/authentication and full R12/R14/RC acceptance remain separately gated.

## Delivered explicit expanded-prompt submission — 2026-10-13
The native owned-v2 prompt form can independently review an exact existing Active target and explicitly retain its current expanded USER text through the same `SessionConversationService`, screening, atomic message/task/audit and provider-neutral admission. Preview and old Use remain nonpersisting. No new schema, separate form-value storage, source-authority inheritance or draft import is introduced.
The final confirmation names immutable target identity/revisions and displays the entire body, with private readable SQLite/retention and unavailable-generation disclosure. Source provenance is volatile only. Cancel writes no message; stale target/source/private admission refuses, and uncertain COMMIT requires exact receipt inspection without replay. See [prompt controls](../docs/instruction-editor.md#submit-an-expanded-request-to-one-exact-session).

## Exact-session provider policy

The [durable sparse provider override](Model_Providers_Memory_And_Knowledge.md#session-overrides-and-effective-resolution) belongs to one exact existing eligible session. Schema 9 adds only its audited private policy table; legacy sessions inherit without fabricated choices and old immutable history/audit bytes remain unchanged.

Native selected-session and exact typed/current-name activated review/confirmation share original-user/private admission, generation/policy/app/catalogue/account fences. Session retention/disposition inventory and remove the policy; passive status does not renew activity or create dispatch authority. Provider-neutral turn admission captures immutable resolution and policy revision; later policy edits never mutate an admitted turn. Generic routing, one-turn directives, authentication/providers and buffered-bootstrap rerouting remain unavailable.

## Durable Single-File Session Attachment

The [bounded R26.1e attachment](File_And_Folder_Ingestion.md#delivered-durable-single-file-session-attachment---2026-10-10) belongs to one exact host-owned session, not the picker request's random session, a selected display name, provider conversation or model context. Native metadata review and separate durable-copy confirmation admit only one immutable UTF-8 text/Markdown file, up to 256 KiB original bytes, within the sixteen-retained-file private-profile bound; no eviction or silent replacement.

Interaction schema 8 preserves existing memory/grant, queue/admitted-deadline, history/retention rows and old audit hashes. Source format 1 stores exact original bytes and validated historical source/revision/item/digest, profile/session/generation and capture provenance. Neither the retained descriptor nor its old path can authorize dispatch, fresh filesystem reads or egress.

Native full-source inspection and the same lexical-lines citations remain available after restart and for retained Done sessions, without resuming them. Browsing does not renew meaningful activity or invoke cleanup. Existing passive-inspection/source-copy/retention fences, original/current privacy/control generations and actual quiescence remain mandatory.

Exact reviewed attachment removal revokes native view/search before atomic body/path-metadata removal and owned-journal verification. Session disposition and ordinary deletion inventory the same copies; live/Unknown work and uninventoried copies hold completion. Content-free provenance remains, original user files and unrelated/kept sessions/Perpetual grants do not change. No body is added to history, memory, preferences, models, logs or audit text.

The separate [retained metadata catalogue](File_And_Folder_Ingestion.md#delivered-retained-attachment-metadata-catalogue---2026-10-11)
also composes optional exact copy status and inclusive retained-byte bounds with
ID/lifecycle/removal/type/literal predicates. Unavailable provenance has no
original-size claim and never matches byte bounds. These volatile filters grant
no session authority, source access or activity renewal; editing reads nothing
and retires the original observation/navigation.

An optional exact canonical SHA-256 predicate composes with the same catalogue
filters but matches only Confirmed retained-copy metadata, never Held or removed
slots. Unavailable provenance has no displayed/serialized historical digest.
The volatile query is fingerprint-bound to signed continuations, not serialized
as text; it grants no body integrity, session authority or new source access.

Optional exact observation-source and device-profile GUIDs plus inclusive
canonical UTC Captured bounds compose with those predicates only for Confirmed
retained copies. Unavailable/Removed tombstones never match or disclose their
prior source/profile/time in catalogue projections. These volatile, signed-
continuation-bound values are correlation only, never consent, profile
ownership, session/file-read authority or current availability. The linked
catalogue contract owns canonical parsing and bounded validation.

Optional inclusive session-generation, captured-generation and storage-revision
ranges also AND-compose with every retained attachment predicate. Positive
`HostRevision` bounds require minimum <= maximum and match only Confirmed
retained copies. Unavailable/Removed projections suppress former versions;
these are correlation/version metadata, not source consent, session authority,
currentness, file-read authority or proof a copy still exists. No session-list,
saved draft or Evidence behavior changes. The linked catalogue contract owns
strict parsing, full candidate validation and complete continuation identity.

Optional exact local-file source/revision/item IDs also AND-compose with every
retained attachment predicate. Nonempty canonical lowercase D GUIDs match only
Confirmed retained copies, never Held or unavailable/removed tombstones, even
when former IDs agree. They are retained correlation metadata only, not source
consent, current file identity, path disclosure, body integrity, file-read
authority or proof a copy exists. Complete validation, private continuation
binding, no-read edits and selection/detail retirement remain unchanged.
Clear query, revocation/cancellation and close erase these volatile fields.
The linked catalogue contract owns admission and deterministic evidence.

## Current Implementation and Design Gap

The [optional native enqueue prerequisite](Work_Management.md#delivered-optional-native-enqueue-prerequisite---2026-10-12) adds same-session bounded metadata-only choice to all six fixed profiles. It combines nonqueue tasks with already-observed queued pending/running/recent terminal tasks using actual host task metadata from the same atomic work read, never queue-entry revisions or history scans. None is the initial default; exact selected ID/revision flows through the original command/service/store with final stale-source refusal.
Choice clears after enqueue and never aliases question/work selection. Typed `[after <task-id>]`, normal Succeeded dependency policy, private admission, clocks and no-replay behavior remain unchanged.

The [reviewed expired current-run fixed-read replacement](Work_Management.md#delivered-reviewed-expired-pending-removal-and-fresh-tail-decision---2026-10-12) uses the same native/exact/current-name activated service: review old removal with retained history plus separate host-issued fresh IDs/profile/prerequisite/lifetime/deadline, then confirm exactly.
The one atomic transaction places the new decision at session tail. Cancel/stale input admits nothing; original source remains unchanged on pre-commit failure. Dependencies never retarget, passive viewing never renews activity, and previous-run Interrupted/Unknown work is not replayable.

The [atomic selected pending cancellation](Work_Management.md#delivered-atomic-selected-pending-cancellation---2026-10-12) is exact one-session/current-run original-user control. Native independent pending selection and typed/current-name activated full-set review/confirmation share host-issued membership and the consolidated audited physical commit fence.

One changed member/queue/run/generation/source/private receipt refuses all. Cancel review preserves selection without writes. Cancellation retains history and dependents, leaves pause/deadlines/relative order unchanged and cannot cancel current work, replay prior runs, retarget voice/questions, delete content or create provider/effect authority.

The bounded [current-run native question recovery](Interaction_Fallback.md#current-run-dismissal-recovery---2026-10-11) keeps the original local-version continuation live after Close/Escape. **Tray > Return to current question** resolves only that host's still-live admitted wait, exact current key/revision and generation.

It restores committed drafts, not unsaved edits, and never creates a task/question/grant or dispatches by reopening.

The original five-minute finite lifetime remains enforced while hidden. Privacy/ownership/input closure, expiry, cancellation, terminal/Unknown task, stale generation/revision, callback failure, storage/audit uncertainty and disposal withdraw recovery.

Restart has no registration: retained questions remain read-only, with fresh-request guidance and no replay or Done resume. Sessions selection/history metadata is not recovery authority; generic approvals, handoff and voice focus are unchanged.

The [R18 local visual broker](Proactive_Interaction.md#delivered-r18-trusted-local-visual-broker---2026-10-09)
now observes only fixed authoritative local-version queue/question state and
already-verified cached maintenance in the already-open selected Sessions
surface. Its exact event IDs/revisions, fixed summaries, independent fatigue,
dismiss/defer/expiry and atomic bounded suppression are not new conversation,
question, execution or grant authority. Pending original questions remain
separately bound; passive delivery/status never resumes, dispatches, retargets
voice or updates meaningful activity. Session retirement removes owned broker
receipts before deletion; restart reconstructs no source or effects from them.

Native [loaded-event category/reason/source/type filters](Proactive_Interaction.md#native-content-free-loaded-event-filters)
compose only over these bounded content-free rows. Editing or clearing a
filter revokes exact event selection and review detail immediately; Review/
Dismiss/Defer remain disabled until separate selection of a visible current
event. Refresh and source/private/session/workspace changes retire selection,
with no late-callback restoration or silent retarget. Matching/empty results
are separate from authoritative source omissions and routine quiet status.
All four exact enum predicates compose with AND; impossible source/type
combinations show zero without changing the choices.
The same contract now AND-composes explicit Any/Absent/exact retained queue
profile and optional exact catalogue capability target with origin, queue
reason, exact correlation IDs and revision/generation/lifetime ranges.
Complete event/profile/target validation precedes exclusion; absence is never
inferred from omitted output. Every predicate joins the versioned continuation
digest and existing edit epochs. These are historical presentation metadata,
not session selection, queue admission, readiness, ownership, permission or
capability dispatch.
No additional source read, persistence, activity renewal or authority is added.

The [R13 four fixed-read profiles](Work_Management.md#delivered-fixed-first-page-read-profiles---2026-10-12)
addresses existing immutable session IDs directly. Pending capacity, FIFO,
fair manual admission, revisions, dependencies and restart interruption are
host-owned; names/selected windows never substitute for IDs. Queue slots cover
only these no-argument read-only profiles, not providers, workers, audio or arbitrary resources.
Native **Refresh selected work**, the four explicit **Enqueue** buttons, **Dispatch ready fixed
reads fairly**, exact pending cancellation and separately labelled confirmed
clear use the same workflow as typed/current-name activated commands. Selection
alone neither enqueues nor dispatches. New work in Done/Removed sessions is denied.
Passive history/queue inspection never renews activity or restores authority.
Exact status/Inspect selected work retrieves successful retained page metadata;
it never reruns an observation. Work/AllWork exclude result data. The unchanged
version default keeps its original transient Version receipt and formats 2/3.
The [exact current-run Pause/Resume controls](Work_Management.md#delivered-exact-current-run-dispatch-pause---2026-10-11) address only that session's new dispatch admissions, not its active callback or lifecycle.
Native/typed/activated routes share the physical store admission fence and current run/generation/control revision receipt. Paused/held reasons are observational in Work/All work/status.
Resume neither dispatches nor replays Interrupted work; stale private epochs cannot recover old eligibility.
No persisted pause preference, grant consumption, deadline/retention extension, question retargeting or physical-stop claim is added.

The [future-only pending lifetime option](User_Configuration.md#delivered-bounded-fixed-local-version-queue-settings-r10r13) adds canonical integer 1-120 minutes, default/reset 30, only for new admitted fixed local-version entries after confirmed activation.
The independent future-admission active budget adds canonical integer 1-60 minutes, default/reset 5. Native/exact settings retain original-input/session/generation/control/call/preference revision gates, audit and durable save/readback receipts. No edit dispatches, cancels or extends activity; admitted deadlines and independent question clocks never change.
Old pending entries use the newly confirmed budget only when later admitted; their original pending metadata remains exact. Format 3 binds active budget/precise UTC deadline to the original dispatch and monotonic enforcement; Work/status and current notices use that active clock. Equal/late versions are suppressed, not physically terminated.
Known legacy fixed-30 and format-2 bytes/audit digests remain preserved; already-admitted old records retain five-minute deadlines from original admission audits. Schema-1/2 preferences validate prior fields and supply new defaults without writing; explicit edits save schema 3. Corrupt/unknown/unconfirmed state fails closed without replay or repair.

The [R14 coordinated native work increment](UI_Workspace_And_Windows.md#delivered-authoritative-sessions-work-surface---2026-10-09)
delivers list-plus-selected-session work, atomic bounded queue/task/question
observation, stable identity/revision/order, observed eligibility/deadlines/
capacity and explicit recovery/gaps. Native pending cancel/remove/clear and
manual dispatch reuse exact command workflows; in-flight selection changes
invalidate admission. Pending question identities stay visible independently
of history/evidence and never gain a workspace reply/review target.
Five-second passive work refresh preserves focus and does not disable native
input, renew cancellation inspection, extend activity, resume or dispatch.

The runnable bootstrap has one response title/body and latest transcript in `MainViewModel`, displayed by `ResponseWindow`.
Typed and recognized spoken commands converge on the deterministic built-in command router.
The updated bootstrap also handles unmatched requests with a verified local model, including clarifying questions and host-validated registered action/grant proposals with once/session/always choices.
Clarifying-question options are suggestions rather than a closed enum: every
such question also exposes a bounded custom-answer path by default. Mouse choice,
typed custom text and activated voice dictation converge on the same continuation
validation and retain their input lineage. A custom answer remains clarification,
not approval, permission, or execution authority; recognized built-in commands may
still explicitly supersede the pending question.
These named model-action preferences are not the future executable/script digest-bound grants; direct exact lock is still ungated while model-suggested lock asks for approval.
The compact surface has a text field, Run, and Dismiss, with positioning and auto-hide preferences.
The separate Documentation window renders trusted embedded documentation, not arbitrary session artifacts.
Its explicit **Open details** action now opens a bounded passive native viewer
for the selected immutable embedded page. The viewer's process-local reference
is not a durable session/history identity. A typed finalized-response handoff
uses existing session/request/task IDs but is not composed into bootstrap
response dispatch. See the
[delivered native profile and authority handoff](Information_Display.md#delivered-native-profile---2026-10-06).
Viewing/search/copy/close never changes session activity, lifecycle, pending
questions or grants; privacy closure clears the viewer independently.

The R05 [question service](../src/Kora.Application/Interaction/HostQuestionService.cs)
now implements bounded single/multiple-choice and text questions, explicit
draft/submit/cancel, host owner/revision checks and expiry. Its
[authorization service](../src/Kora.Application/Interaction/HostAuthorizationService.cs)
uses exact host-resolved proposals and a separate atomic storage/audit seam;
it does not use legacy action-name preferences.
The bounded [production SQLite adapter](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.cs)
now persists typed questions/options/drafts/answers, exact proposals/grants and
minimal session authority. The existing services are registered with that
adapter. A bounded native question/review route now composes the existing
durable local-version query; no effect dispatcher is activated. See the
[native question boundary](Interaction_Fallback.md#delivered-bounded-native-question---2026-10-07).
There is no general typed form service,
full conversation/composer UI, general concurrent provider/effect scheduler,
or model-facing session tool API. The bounded history, native work ledger,
question and passive detail slices do not qualify those broader capabilities.

The [exact native handoff consumer](Model_Providers_Memory_And_Knowledge.md#delivered-bounded-exact-native-handoff-review---2026-10-10) reads existing successfully audited host-held offers, not generic answers or serialized/model-supplied offers. It binds the immutable original request/session/task/question/offer and all revisions throughout its volatile lifetime.

Approve/decline/cancel and reduced-envelope removal reuse the same workflow and genuine question transaction. Generic submission cannot mint the exact-review capability. Unrelated questions remain unchanged; browsing another session cannot retarget review. Close, expiry, privacy/ownership closure, stale policy/task/control and disposal suppress late replies, never granting runtime or egress authority.

### R12 Bounded Session Retention Delivered - 2026-10-09

Schema v6 preserves v5 queue authority and adds one durable per-session meaningful-activity timestamp. Default
archive is 24 hours and deletion 30 days from that same timestamp, not from
creation/archive. Create/resume, admitted substantive original-user work,
accepted final answers/cancellation and real dispatch/terminal progress refresh
it monotonically. Drafts, passive browse, rename and control/configuration
bookkeeping do not. Unknown historical activity gets a conservative migration
baseline; no chronology is fabricated. Native future-only
[configuration](User_Configuration.md#r12-bounded-session-retention-delivered---2026-10-09)
does not alter existing due dates without subsequent meaningful activity.

Startup checks and an owned one-minute host-only maintenance timer
process at most 32 candidates, never execute or schedule work. Intent-recorded,
dispatched, Unknown work, unresolved questions and current-run control
authorities hold rather than being abandoned; held controls cannot starve due
idle sessions. Exact-generation audited Perpetual session marking
holds the whole session, independently of Perpetual permission grants. There
was no marking UI in this original slice; the native increment below supplies it. Archive revokes source/generation/scoped authority but
preserves content and does not reset the activity clock.

Deletion awaits source/presentation revocation, removes owned history,
question/draft/answer, task/event/wait/run, terminal queue, observation, scoped-grant and metadata
content, legacy task copies and authenticated owned artifacts/staging, and
clears SQLite row/index/free-page and committed rollback-journal copies.
Unrelated sessions, independent artifact deletion owners and independently
Perpetual grants survive. Only exact-ID content-free authority tombstones,
redacted history gaps and content-minimised independent required audit remain;
late append/publication is denied. Removing authority alone is not acceptance:
a separate final inventory receipt is required and interrupted acceptance
retries storage cleanup without execution.

Unrecognised ownership, malformed envelopes, uncertain key publication,
uninventoried storage/backup entries or hot/nonempty journals fail closed.
No managed-backup publisher/ownership format is delivered; arbitrary copies
are held, not guessed or claimed erased. No provider/user-export, forensic or
media-erasure acceptance is claimed. This entry supersedes older statements
below that retention/timers were absent from their historical bounded slices.
Full R12 remains partial; blocked R11 is required only for the remaining
general execution/queue integration, not the delivered fixed local-version queue
or this non-executing maintenance increment.

Validation after rebase onto `5755aa7d` (#118): Release 0 warnings/errors using
existing locked dependency assets; no restore/feed change needed.
Core 1,028, Application 2,860, Tools 69, Definitions 6, Windows 1,215 passed,
zero failures/skips;
portable coverage 100% lines/branches. Tests check actual plaintext absence
from database/journal, owned file removal, preservation, holds, cancellation,
invalid persistence/migration, interrupted acceptance, bounded fairness,
queue holds/inventoried deletion, restart no-replay and retained-source
invalidation of #118's selected-session work surface without losing unrelated
work or its passive-refresh/question coexistence contracts.
See the [dated roadmap receipt](Archive/Implementation_Roadmap_2026-10-11.md#r12-bounded-session-retention-delivered---2026-10-09);
installed/native/RC and full R12/A3 qualification remain open.

### R12 Native Exact-Session Retention Controls - 2026-10-10

The native Sessions panel passively reads the selected immutable ID's actual UTC meaningful-activity, archive due and delete due dates, generation, exemption audit revision and kept/purged state.
It reports only observed work/control holds; copy inventory and broader maintenance availability are explicitly unobserved, never inferred eligible. Names, traces, history, grants and old previews supply no authority.

**Keep this session** and **Use ordinary retention** each stage a native review, followed by a separate exact confirmation. Ordinary review displays the unchanged recorded dates, labels already-due observations and warns that background archive/delete may follow soon or already be due.
This is a SESSION retention hold, not a Perpetual OPERATION grant, consent, durable memory, cross-session recall or execution.

Fresh original native input, existing `SessionWorkspaceService` committed control intent, current ownership/privacy/call/input lifetime and exact nonremoved session admit the setting.
The private SQLite transaction compares generation, all displayed recorded state and the last exemption's committed audit sequence, including hold-change/restore ABA. Clock/activity, policy or another hold mutation conflicts rather than silently changing reviewed consequences. Done sessions use supported metadata-control authority without resume.

Required typed audit and hold update share one commit; terminal bookkeeping is non-substantive, followed by exact readback. Commit/receipt/readback uncertainty is not rollback or success: refresh durable state and review again, never automatically replay.
Selection changes, closure, privacy loss, purged/removed/unknown or corrupt state refuse and clear previews. Independent grants, memory bodies, source/copy inventory, unrelated sessions and the private timer are unchanged.

Inspection no longer runs maintenance. The replacement passive pre/post admission preserves confirmed retention-policy, ownership/privacy/call and verified copy-inventory checks.
Due ordinary candidates without existing live/Unknown/question/control holds deny content inspection pending authoritative background maintenance; source authority revision changes suppress late results. Exact retention-status readback is non-content observation, not a cleanup bypass.
No new apply-now, archive/delete, global default, typed/voice mutation or model descriptor is available.
The existing exact-ID field also supports native **Read exact ID retention**, using canonical immutable-ID validation. It keeps retention status/review available when due-content inspection is held, without reading names/history/work or fabricating active/resume authority; removed/purged status remains nonmutable.

Maintained [actual SQLite controls](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionRetentionControlTests.cs), [portable admission](../tests/Kora.Application.UnitTests/Hosting/SessionWorkspaceServiceTests.Retention.cs) and [headless native review](../tests/Kora.Windows.IntegrationTests/SessionsRetentionViewModelTests.cs) cover this subset.
This does not qualify installed native/accessibility, forensic/full-copy deletion, general execution or full R12/RC acceptance.

### Delivered Bounded Ordered Interaction History - 2026-10-09

The authoritative `SessionHistoryEvent` projection records only actually
host-committed question snapshots/final answers, decision metadata and task-state
receipts. It is **not a full conversation store**: bootstrap user/model messages,
response bodies and general conversation composition were never durably admitted
and are unavailable, not fabricated from diagnostics. A successful task receipt
does not prove an external effect. Draft transitions have decision metadata, not
final-answer text; migrated current records are explicitly `Baseline` snapshots.

Schema v4 adds per-session history heads and immutable event/source projections
under the existing authority lease and transaction. Store transitions, not a
model, name, diagnostic mirror or arbitrary append API, author the events.
Stable event GUIDs, session-local monotonic sequences, source IDs/revisions,
session generations and SHA-256 provenance survive restart. Required typed
audit/task provenance is checked; audit is not mined to synthesize turns.
Validated v1/v2/v3 migrations preserve original authority/audit/task bytes.
The v3-to-v4 transaction adds an explicit gap followed by labelled current
snapshots; GUID sorting is not presented as pre-migration chronological order.
Unknown schemas, inconsistent projections/heads and failed migrations are
unavailable; a rollback/retry performs storage maintenance, never execution.

`session history <exact-id>` and `session get <exact-id> <event-id>` share
`SessionWorkspaceService` with the native Sessions viewer. Typed and currently
configured-name activated command prefixes select only the grammar; names never
resolve sessions. These routes write no control intent, terminal receipt, audit,
meaningful-activity timestamp or lifecycle state. Private ownership/inspection
and originating channel/revision are rechecked before presentation.
Native history uses an explicit exact-ID field, bounded inert text, keyboard
buttons and accessible names; it changes no question, approval, focus or voice
target and provides no reply/playback controls.

Pages default to 25, at most 50 records/64 KiB complete output. Continuations
bind the exact session, authorization generation, original sequence ceiling and
last returned sequence; later appends cannot expand a snapshot. Lifecycle
changes invalidate continuations. Oversized individual content is explicitly
`Unavailable` with stable metadata/citation retained, never silently truncated.
Exact event lookup cannot cross sessions. Gaps, metadata-only, unavailable and
redacted records are distinct. Passive reads do not resume a Done session.

Logical disposition retains readable exact history citations but atomically
redacts history source/content alongside the existing removal of live question/
answer/name/observation/scoped-grant rows. Event ID, sequence, source revision
and provenance digest remain stable; the Removed identity remains excluded from
the live workspace and cannot resume or gain meaningful appends. Its known ID
can inspect only redacted history. The preview digest includes history, excluding
only the confirmation's own fresh control receipt; concurrent addressed work
invalidates the preview. Cancellation/audit/admission failures roll back together.
No history timer, automatic purge, apply-now or physical-copy erasure is added.

Shared-profile skill inspection, immutable local file previews and clipboard
inspection remain volatile/read-only control content, not conversation turns,
attachments or model input. Their paths/text/provenance are never imported.
History never reconstructs captions or authorizes replay: only fresh separately
admitted actual playback can create a caption. History content never enters logs,
activity tags/baggage or model context, and retrieval has no network/model
dependency. Full composer, model history reasoning, Ask Evidence, broad
export, queues and scheduler remain unavailable. Unique storage/interruption/
capacity/copy evidence in the storage experiment is retained, not deleted or
claimed replaced by this increment.

### Delivered Passive Session-List Name and Exact-ID Search - 2026-10-10

The [metadata-only policy](../src/Kora.Core/Storage/SessionListSearch.cs) and [Application read path](../src/Kora.Application/Hosting/SessionWorkspaceService.ListSearch.cs) support explicit bounded Browse, names or an explicitly selected exact immutable ID.
The separate [saved composer catalogue](#saved-conversation-draft-catalogue--2026-10-13) reuses this policy on authoritative saved-draft session metadata, not body content; it retains its stricter committed-head continuation invalidation and mandatory audit-prefix validation.
The [All work overview](Work_Management.md#delivered-passive-cross-session-work-observation---2026-10-11) also reuses this exact policy, composing lifecycle and needs-answer filters over its own ten-session bounded canonical-ID scan. Both query modes scan rather than directly resolving an ID there; zero-match pages can have Next. Its volatile digest-bound continuation additionally binds needs-answer and the overview source epoch. Query editing never reads, and clear/close revoke private query/results without adding content/model or execution authority.
They reuse the persisted private metadata reader, authoritative name/audit validation and unchanged admission revision, without task intent, activity renewal, grants, model calls, memory use or new storage/index authority.

Browse requires exactly empty query text and uses the same canonical-ID ordered bounded source scan, not an unbounded inventory or implicit selection. Nonempty Browse text is rejected, not ignored or reinterpreted. NameSubstring still requires nonempty validated text; an empty name query never becomes Browse.
Names use literal ordinal case-sensitive substring matching, including punctuation as ordinary characters. Queries reuse SessionName's NFC, single-line, no-control/format, no-surrounding-whitespace, 120-scalar/480-UTF-8-byte validation. Invalid text is rejected, never normalized, trimmed, truncated or reinterpreted as All.
ExactId accepts only a nonempty canonical lowercase D GUID; a name that resembles an ID never supplies authority.

Name scans consume at most 50 metadata rows per click, with the existing bounded reader lookahead and five-second lease/SQLite progress and lock boundaries. Native results are limited to 25 records; the reusable workflow accepts 1–50 and caps the complete serialized page at 64 KiB.
A byte/result limit preserves continuation before any deferred match. Scanned, unnamed and nonmatch counts and remaining/end scope distinguish an empty bounded result from exhaustion.

All/Active/Done scopes search across pages. Native session-list search additionally
AND-composes explicit name-metadata availability **Any / Named / Unnamed** and
optional inclusive authority-generation and name-metadata-revision
minimum/maximum predicates. Availability describes only whether validated name
metadata was observed: Unnamed is not an empty name, missing authority, revision
zero or evidence of historical deletion. Named requires metadata; Any adds no
availability restriction. NameSubstring naturally matches only Named, so
NameSubstring + Unnamed is an empty conjunction, not a query-shape fallback.
The portable query owns positive `Int64` admission
and minimum <= maximum ordering. Native text uses invariant ASCII decimal digits
only: empty means an absent bound, leading zeroes are admitted semantically,
and zero, signs, whitespace, separators and overflow fail closed without searching.
Every complete candidate is validated before predicates can exclude it, including
rows beyond the result limit in the bounded source page.
Generation applies to named and unnamed rows. Either name-revision bound requires
validated name metadata; unnamed is never revision zero or an unknown match.
Unnamed metadata never matches a name but remains available by Browse or exact ID without
name-revision predicates; Removed sessions never browse. Exact lookup still
rejects a different returned session before applying any predicate.
Canonical-ID ordering is deterministic and culture-independent.
Continuations bind the exact query/mode/lifecycle/name availability and all four semantic numeric
bounds, process-local service/store identity and private admission revision.
They cannot silently restart on changed predicates, host/source or admission.

Names, name availability, generations and name revisions in this search are volatile passive
metadata only: not session selection, ownership, permission, freshness,
historical completeness or action authority. The Browse, availability and numeric native controls do not
extend the separate All Work or saved-draft catalogue interfaces.

Mutable metadata has no committed historical sequence ceiling. Later renames/state/removal are observed only in subsequently read rows; earlier changes and additions behind the cursor require Search again. Later appended IDs can appear, and end describes only that observation. No finite atomic snapshot is fabricated.
Exact missing/Removed IDs and unavailable/corrupt/ownership-denied/cancelled reads are explicit recovery states, never empty-success unfiltered inventories.

The [native surface](UI_Workspace_And_Windows.md#delivered-passive-session-list-metadata-navigation---2026-10-10) distinguishes metadata search from history and Evidence. Typing in ordinary list mode preserves selected work; unchanged exact visible results retain work/history/memory presentation without renewing inspected cancellation authority.
Invalidated/changed selections clear their targets. Query/selection/source/privacy epochs, cancellation and close fence late content.

[Core rules/cursors/bytes](../tests/Kora.Core.UnitTests/Storage/SessionListSearchTests.cs), [bounded workflow failures](../tests/Kora.Application.UnitTests/Hosting/SessionWorkspaceServiceTests.ListSearch.cs), [actual private SQLite boundaries](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionListSearchTests.cs),
[late callbacks](../tests/Kora.Windows.IntegrationTests/SessionsViewModelTests.ListSearch.cs) and [headless native preservation](../tests/Kora.Windows.IntegrationTests/SessionsListSearchTests.cs) are the maintained evidence.

This delivers only passive session-list metadata navigation, not conversation/composer, history lexical expansion, diagnostic Evidence filters, name-based model/voice routing, caption-monitor selection, providers or installed-native R14/A4 acceptance.

### Delivered Passive Exact-Session Lexical History Search - 2026-10-09

The [Application scan](../src/Kora.Application/Hosting/SessionWorkspaceService.Search.cs) consumes the delivered private ordered history reader, without a new schema, index, content capture or authority write. The [Core matcher](../src/Kora.Core/Storage/SessionHistorySearch.cs) reuses the selected-file lexical query/token rules: NFC invariant-case whole-word OR, 256 characters/512 UTF-8 bytes, 32 distinct terms/64 characters per term. Only committed question/option labels, final answer/choices and typed kind/task/decision/question-status metadata are searchable; bootstrap bodies, reasoning, full conversations/artifacts and volatile previews remain unavailable.

The [result contract](../src/Kora.Core/Storage/SessionHistorySearchPage.cs) returns complete immutable receipts ordered by host sequence, at most 50 results/200 scanned receipts/64 KiB including command JSON overhead. Input reads remain bounded by the existing 64 KiB history page and 200 consumed receipts; no unbounded materialization occurs. Query-bound continuations retain exact session, generation and original sequence ceiling. Empty bounded pages may still have a continuation. Baseline/gap/redacted/unavailable counts and oversized matching omissions are explicit; missing/corrupt storage is not an empty success.

Original typed/current-name activated `session search` and [native controls](../src/Kora/SessionsViewModel.Search.cs) share private admission and content-free Activity instrumentation. Fresh end-of-scan snapshot validation, cancellation and unchanged host control/privacy revision precede publication. Done is readable without renewal; Removed content does not match. Query/subject edits and privacy/source retirement suppress late presentation. Selection opens the existing freshly re-resolved immutable receipt viewer, not a duplicate resolver. No resume/replay, question/voice retarget, model call, grant consumption or persisted/logged query is admitted.

Maintained [scratch SQLite tests](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteHistorySearchTests.cs), [portable workflow tests](../tests/Kora.Application.UnitTests/Hosting/SessionWorkspaceServiceTests.Search.cs) and [native recovery tests](../tests/Kora.Windows.IntegrationTests/SessionsHistorySearchTests.cs) cover delivered behavior only. Broader R12/R14, model history reasoning, Ask Evidence, general artifact search and installed accessibility qualification remain open.

### Delivered Minimal Sessions Workspace - 2026-10-07

The bounded deterministic command extension now shares this workspace's host
service and guarded transactions: typed and activated voice expose
`session help/list/status/inspect/create/rename/done/resume`. The single
[typed grammar](../src/Kora.Core/Commands/SessionCommand.cs) owns quoting and
input/page/result limits; [user syntax and recovery](../docs/commands.md#bounded-exact-id-session-commands)
describe actual availability. Exact immutable IDs and explicit generations/
metadata revisions are required; names remain labels only. No implicit
selected-window, title or approval target exists. Pending bootstrap questions
and approvals block these commands without cancellation or retargeting.
Every accepted command owns fresh deliberate lineage and a durable control
intent; observational reads never change authority. Voice enablement/consent,
origin, private presentation and call/recovery revisions remain checked.
Protected-call voice mutations are explicitly unavailable, not deferred.
Full session routing, transcript persistence, scheduler, general effect cancellation, recoverable-copy deletion,
retention and model-facing session tools remain unimplemented.

### Delivered Bounded Exact-ID Logical Disposition - 2026-10-08

The native Sessions workspace has separate **Preview logical disposition** and
**Confirm logical disposition** actions. This is explicitly not full R12
deletion. Preview resolves the exact existing ID, displayed authorization
generation and metadata revision under private admission, enumerates live rows
and discloses retained data. It records no control intent and changes nothing.
The Application host holds the single-use preview; confirmation must be fresh
LocalUi input with the same proposal and unchanged control revision. Voice,
typed `session delete`, models, title matching and implicit selection are not
admitted deletion routes in this increment.

The Windows writer revalidates authoritative task/question state and a framed
digest of all addressed live authority records and task revisions, not a
bounded UI page. Unknown IDs, stale generations/metadata, any intervening
addressed work (even if completed), pending questions including expired ones,
nonterminal or Unknown work, lost ownership/privacy/call admission, missing or
corrupt storage fail closed. It never cancels, reconciles or abandons work to
make disposition eligible. Resolve supported pre-dispatch waits explicitly;
general uncertain-effect disposition is unavailable.
The desktop control gate consumes the existing `IsProtected` call policy,
including uncertain manual-call evidence even when automatic state reads
Clear/Unavailable; independent passive inspection does not become mutation
authority.

Under the shared authority lease and one transaction, disposition advances
generation, writes a non-reusable Removed tombstone, removes only that session's
live metadata/name, host observations, questions/drafts/answers, admitted wait
bindings and scoped grants, and commits both fresh control terminal success
and required typed audit. Independent Perpetual records, unrelated sessions,
task/event provenance and audit chain remain unchanged. Removed sessions are
not live-workspace browsable or resumable; the later bounded history increment
permits exact-ID inspection of redacted citations only. The task writer rejects new intents and late
outcomes; interaction/snapshot/lifecycle paths cannot append to or recreate a
Removed identity. A competing append wins before disposition and blocks or
invalidates it, or loses after the committed tombstone.

**Retained/unavailable:** opaque tombstone and task/event IDs/states/revisions,
content-minimising authority audit/digests, independent evidence/diagnostics,
Perpetual provenance, inert legacy migration storage, SQLite journals/free pages
and any copied database remain. General conversations/history, managed session
artifacts/snapshots/indexes/caches and inventoried managed-backup deletion are
not implemented. User exports/provider copies are outside local deletion.
No forensic, cryptographic-erasure or full R12/A3 deletion acceptance is claimed.
No retention setting, inactivity timer, automatic purge or apply-now behavior
is added. See [the user workflow](../docs/windows-and-tray.md#logical-session-disposition).

Precommit audit/gate/cancellation failures roll back the tombstone, live rows
and terminal success together; an incomplete control intent is recoverable
without execution. After COMMIT, terminal success is already durable, so
restart never appends a recovery receipt into a Removed session. Receipt or
commit uncertainty remains an explicit error, not rollback or automatic retry.
Owned-process interruption, actual production SQLite, native-state and portable
host tests maintain these boundaries; installed/live visual/accessibility and
physical power-loss acceptance remain open.

### Bounded Authoritative Task Observation and Pre-dispatch Cancellation

The independent native evidence inspector's opt-in **AuthorityAudit** source
reads this same committed schema-v3 interaction authority, not a diagnostic
copy. Task/question/grant changes are shown as their committed typed revision/
digest references and recorded outcomes; no historical payload or effect is
fabricated. It never records control intent, answers questions, retargets
approvals, refreshes meaningful activity or queries the frozen legacy ledger.
See [bounded source and cursor semantics](Information_Display.md#delivered-bounded-native-evidence-inspection).

Exact typed and activated voice `task status <session-id> <task-id>` and
`task inspect <session-id> <task-id>` expose only the addressed existing
durable task, session generation, admitted source/current-run distinction
and complete bounded question record when present. Unknown/foreign IDs do
not resolve by title, window, model output or trace. States are receipts,
not inferred progress, planned steps, ETAs or physical effect cessation.
The single session-command grammar owns the 1,024-byte input and 64 KiB
complete-output bounds; inspection does not extend meaningful activity.

`task cancel <session-id> <task-id> <task-revision> <generation>
<question-id> <question-revision>` and the native workspace's **Inspect exact
selected task** / separate **Cancel inspected pre-dispatch wait** share one
host service. Fresh deliberate original-user control intent, current
ownership/privacy/channel/call-revision admission and all exact conflict
tokens are required. Safe cancellation introduces no effect and does not
promote unknown user IDs into a fabricated host session: the actual existing
session is resolved under private admission before recording fresh control
intent. Admission is checked again after resolution and at COMMIT. It does not
require the lifecycle mutation's unprotected-call permission; voice still
requires admitted activation. Legacy bootstrap questions/approvals keep their
existing targets and blockers. Always-available stop/recovery is unchanged.

Only an already admitted current-run native local-version wait is cancellable.
Its question precedes dispatch. A separate host-owned run/wait record, not
question purpose/source text, proves the admitted source. The answered exact
key alone reaches the pre-dispatch gateway. Task terminal cancellation,
question revision/status, target observation revocation and required trusted
typed audit share one transactional store and COMMIT. No grant is created or
consumed; unrelated work/sessions and independent Perpetual grants survive.
Answer/cancel/dispatch/revision/expiry races have one truthful winner; stale,
foreign, expired or previous-run targets refuse. Late/disposed input cannot
resume or dispatch. A possible committed outcome followed by receipt failure
requires inspection, never a rollback claim or automatic retry.

The [native presenter](Interaction_Fallback.md#synchronized-native-terminal-publication---2026-10-12) now publishes terminal state and completion together. The original and external cancellation channels retain the same committed receipt without overriding an earlier denial/failure. Private clearing remains mandatory after completion; session/task/question/revision/generation admission and the durable atomic cancellation/audit contract remain unchanged.

Schema v3 consolidates the existing ordered task ledger into the private
interaction partition, preserving identities/events, session generations,
metadata, questions/grants and exact audit bytes. The fully validated legacy
task ledger is frozen before a complete destination schema transaction and
retained inert. Interrupted migration can revalidate/retry storage maintenance
only; lost/corrupt authority is not reconstructed as empty state. Evidence
projections/retention remain independent. Previously dispatched/Unknown work
stays uncertain/quarantined and is never relabelled Cancelled. This bounded
slice closes no full R12/R13/A acceptance, scheduling, queues, workers, content
retention/deletion, runtime/model-management execution or model task tools.

**Sessions** in the tray, exact **open sessions** (configured-name prefix
supported), and **Ctrl+Shift+S** in the compact response open a native
workspace over the existing production authority partition. Refresh and
keyset Next expose Active/Done IDs and authorization generations, not
invented titles or general conversations. The selected passive detail reads
actual typed questions/options/drafts/answers, request/task IDs, revisions,
channels, generations and current durable task records. Separate selected-ID
Evidence uses the existing bounded diagnostic/audit/span/link reader; missing
session/conversation evidence remains explicitly unavailable. Pages default
to 25 records and the store accepts at most 50. They are observations across
separate bounded reads and independent evidence projection, not an atomic runtime ledger; refresh for concurrent
changes. Passive pages require existing private partitions and never create a
replacement when storage is missing. Removed authority tombstones are not
offered as browsable sessions.

Reading, selecting and keyboard navigation never admit a reply, select an
approval target, update activity, resume, or restore model/provider context.
The original minimal slice had no composer, rename/create metadata, scheduling, archive timer, full deletion,
retention, export or model lane is added. Full R12 conversation/work/queue/
retention/delete and full coordinated R14/native acceptance remain open.

**Mark selected ID Done** and **Explicitly resume selected ID** are distinct
trusted local-user initiations. The host creates a fresh exact subject-bound
control intent and activity; the expected generation from the displayed
observation is only an optimistic conflict token. Control intent requires an
existing private task ledger, and lifecycle requires existing session authority;
neither recreates missing storage and guesses that forgotten work is idle.
Current private desktop
ownership, unlocked privacy admission and unprotected known call policy,
including its revision, must still hold at the authoritative commit.
The consolidated authority lease/connection stays held through the single
COMMIT; no second nested interaction lease is acquired. A bounded maintained task query rejects any other
nonterminal or Unknown task (including recovered outcome-unknown work);
only the exact fresh revision-1 control intent is excepted. Pending questions
also block, even when their deadline passed. Nothing is auto-abandoned or
synthetically resolved to permit Done/resume. Unsupported work state or
corrupt/overflowed authority is refusal, not an empty idle result.

The existing lifecycle transaction commits generation advancement, scoped
grant invalidation, fresh-observation removal and required typed authoritative
audit together. Each Done/resume advances generation. Independent Perpetual
records and typed history are preserved. Resume never replays tasks, revives
old questions/approvals or transmits old context. Commit/audit/receipt failure
is visible; cancellation is not a claimed rollback after a possible commit.
Restart reads durable state and performs existing no-replay recovery, not
automatic lifecycle completion. Privacy closure cancels and clears the
workspace, without revealing old content on unlock.

#### Bounded validation and experiment disposition

On the original isolated `281393c` baseline, root Release/analyzers completed with zero
warnings/errors. Maintained suites passed: Core 411, Application 1,322 and
Windows 728, with fresh-only portable **100% line and branch coverage**.
Actual private production SQLite tests cover bounded pages, typed history,
cross-session isolation, Pending/live/Unknown blockers, stale generations,
call/ownership revision changes at commit, required audit failure, concurrent
lifecycle/intent leases and before/after-COMMIT owned-process kill/reopen for
Done/resume. Fake native state covers close/privacy and cancellation-ignoring
late reads. No live app, user-data, microphone, clipboard or OS-session trial
was run; native visual/screen-reader/DPI acceptance remains open.

After rebasing onto peer-merged `1506b7e`, the combined maintained suites passed
Core 450, Application 1,327 and Windows 731, again with fresh-only portable
100% line/branch coverage. Missing-ledger lifecycle admission is now tested:
control cannot initialize a replacement task ledger and infer forgotten work
is idle, or recreate missing session authority.

R02's generic atomicity and intent/no-receipt expectations are maintained
against production semantics, rather than treated as production admission.
No executable proof file is removed: its intertwined SQLCipher/envelope,
WAL engine comparison, crypto/artifact/keyed-backup/rekey/leakage/native-provider
cases remain unique. See the
[equivalence assessment](../experiments/r02-storage-proof/README.md#maintained-minimal-sessions-equivalence-assessment---2026-10-07).

### Delivered Bounded Session Metadata and Explicit Creation - 2026-10-07

On dispatch baseline `d0a8e82` (#68), the native **Sessions** surface adds a
name draft, **Create empty Active session**, and **Rename selected ID** over
the shared [workspace service](../src/Kora.Application/Hosting/SessionWorkspaceService.cs).
Create deliberately allocates a new immutable ID, Active generation 1 and
metadata revision 1. It creates no executor, execution task/context, model
conversation, question, approval or permission. The fresh administrative
control intent/terminal receipt is retained by the existing task ledger for
audit and no-replay recovery; it is not dispatched work.

Names are intentional private user content, persisted only in the interaction
partition, never diagnostic messages, activity names/tags or raw audit
envelopes. The authoritative [domain rule](../src/Kora.Core/Hosting/SessionName.cs)
requires nonblank NFC Unicode, no surrounding whitespace, valid UTF-16,
at most **120 Unicode scalars / 480 UTF-8 bytes**, and no control, format,
line-separator or paragraph-separator characters. Invalid input is rejected,
not truncated, trimmed or normalized silently. Duplicate names are allowed:
neither a title nor the selected window resolves authority. Bounded keyset
pages expose name, metadata revision, exact ID, generation and lifecycle;
selection remains passive and retains the existing separately paged typed
question/task detail. No transcript, summary or arbitrary history schema is added.

Rename requires the exact subject ID, expected authorization generation and
expected metadata revision (0 only for explicitly unnamed legacy records).
Its atomic transaction changes only metadata/revision and the required
content-minimizing typed audit commitment. It does not resume Done sessions,
alter generation/grants, revive questions, dispatch work or extend a meaningful
activity clock. Rename can label a session with Pending/Unknown work without
resolving it; those records still block Done/resume. No inactivity clock or
automatic retention behavior is delivered by this slice.

Create and rename require fresh original LocalUi/ActivatedVoice lineage,
existing private task/interaction stores, current ownership/privacy and known
unprotected call policy/revision, rechecked immediately before COMMIT. Native
controls use the same host service; only **open sessions** is currently admitted
as a typed/voice session command. Name-based targeting, spoken Create/Rename,
model routing and a session composer remain unadmitted. Selection never
retargets global input or approval. Privacy closure clears the draft and all
late content. Failure/cancellation is visible; a possible committed outcome
is inspected before retry, not replayed or claimed rolled back.

The private interaction application ID and partition remain unchanged.
Schema **v1 -> v2** validates exact legacy schema, integrity and all existing
authority/audit records before transactionally adding the bounded metadata
table and version. Legacy IDs remain explicitly unnamed, not fabricated
titles. The migration preserves generations, questions, observations, scoped
and independent Perpetual grants, task records and audit bytes. Unknown/old
unsupported versions, malformed schema/content, downgraded metadata commits,
missing committed metadata, stale row/audit commitments and missing files
refuse without initializing replacement authority. Schema maintenance is
not session admission or meaningful activity.

Maintained tests cover actual owned SQLite durable reopen/migration,
preservation, stale/competing revisions, duplicate-name exact-ID isolation,
privacy/call revision races, cancellation and required audit failure; native
VM/static composition covers explicit controls, passive selection and content
clearing. Owned-process kills before/after Create, Rename and migration COMMIT
verify atomic reopen and no automatic replay. Release/analyzers and portable
**100% line/branch** coverage pass. Native visual/screen-reader/DPI and installed
privacy trials remain open. This is not full R12, R14 or A3 acceptance.

No storage experiment executable is removed: production metadata/migration
and kill/reopen checks supersede only the matching generic expectations,
not unique SQLCipher/envelope, WAL comparison, artifact/backup/rekey/leakage,
DPAPI or native-provider evidence. Historical receipts remain unchanged.

### Bounded R05 Foundation Boundary

The [native exact-grant management subset](User_Configuration.md#delivered-native-exact-operation-grants--2026-10-10) now adds bounded passive inventory/current inspection and explicit one-ID/revision revocation. Reads create no task intent or meaningful-activity extension. Revocation uses a fresh [original-user control admission](../src/Kora.Application/Interaction/ExactGrantControlAdmission.cs), never the stored proposal/request/trace or a retired origin session as authority.

The [narrow store seam](../src/Kora.Core/Storage/IExactGrantStore.cs) uses the shared initialized private store/read lease. The [SQLite implementation](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.ExactGrants.cs) updates only the exact unchanged active record under the same committed-intent lease and transaction used by consume/lifecycle. It increments revision, retains safe revoked provenance and preserves unrelated records and all independent Perpetual records.

One atomic authority envelope retains the correlated typed **RequestedAudit** and terminal **Audit**, with the same host request/task/session, approval and W3C lineage. This optional content-free pair is omitted for historical envelopes, preserving their exact bytes/digests; tables/schema and legacy preferences are unchanged. Committed-authority inspection carries the pair in typed provenance; diagnostic sinks are not fallback authority. Cancellation before commit rolls back both state/audit, while uncertain commit/readback is explicit recovery.

The native presenter invalidates immutable preview/confirmation on selection, refresh, close, privacy or control-generation changes and rejects late output. Current applicability/work status is unknown without evidence. This management path neither dispatches nor physically cancels/rolls back effects; general approval creation, source/resource review, generic voice targeting and immediate pre-effect dispatch qualification remain gated.

Questions, answers and drafts use existing host session/request/task/question
IDs and a positive revision. Every accepted draft edit advances that revision;
submission of an earlier draft conflicts instead of overwriting newer input.
The service accepts explicitly addressed UI or activated-voice replies against
the same key. It does not implement generic spoken-reply focus or a speech/UI
parser or generic voice focus. Trusted local native input and exact-record
review are composed for the bounded version query; general channel acquisition,
voice targeting and native/speech acceptance remain integration gates.

Clarification submission never creates a grant, including when text, purpose,
source labels or option labels claim approval. Only the authorization service
can bind a question to the transaction's current host operation and accept the
exact scope decision. Nothing calls an action handler or dispatches an effect.
Expired/closed questions and changed question/proposal/session generations
reject late replies; explicit cancellation never approves anything.

The [atomic seam](../src/Kora.Core/Storage/IHostInteractionStore.cs) requires
committed matching intent, Active durable work-session identity, live host
activity, authoritative current policy/operation snapshots, and atomic typed
audit plus question/grant commits. A nonterminal task's root intent may omit
invocation ID; exact proposals/questions carry their own host invocation while
preserving root request/session/task/origin. A supplied intent invocation must
match, and terminal/cancelled task records cannot admit new decisions.
Portable tests retain the serialized adapter; Windows tests additionally
exercise the existing services with actual private SQLite transactions,
reopen, owned-child interruption and races. Done/authority removal/resume
must advance the session authorization generation; restart of an Active
persisted session preserves it. Pending questions and Once/Session grants
cannot regain eligibility after resume. Perpetual records are independent
and have no expiry/retention/eviction field.

The [durable slice handoff](Archive/Implementation_Roadmap_2026-10-11.md#r04r05-durable-interaction-and-minimal-session-authority---2026-10-06)
defines schema/lease/audit ownership. The native presenter adds trusted
local-UI review/input over those services without schema or recovery changes.
Immediate pre-effect dispatch revalidation, complete source/resource review
and general authority composition are still required. This slice does not
alter ordinary bootstrap dispatch, microphone capture, direct lock, power
operations or execution.

### Durable Authority and Typed Presenter Handoff

`InteractionStorageV1/interaction.db` is a separate private, standard-SQLite
authority partition. Its single transaction commits changed questions/grants,
session generation or host observations and typed, hash-sequenced audit plus
the audit head. Audit payloads contain host/W3C identities, intent/session
revisions, decision references and state digests, not question/answer content.
The task partition lease is held while committed matching nonterminal intent
is read and until the interaction COMMIT completes. Cancellation/terminal
task writes use that same lease. There is no cross-database write or diagnostic
sink success fallback. The independent evidence/file sinks are not a source
of grant authority.

The host explicitly creates the durable session, then publishes a fresh
trusted policy/exact-operation snapshot with an optimistic observation revision.
Content observations revoke affected grants and close affected pending
approvals in that same commit, before the changed snapshot is exposed.
Snapshots from a previous adapter lifetime are not eligible until freshly
host-resolved and re-admitted. Active session generation survives restart;
Done/resume each advance it and never revive old scoped authority. Authority
removal advances generation and leaves a non-reusable session tombstone.
Perpetual records have no session foreign key, expiry or retention/eviction;
their approved-proposal deadline is provenance, not a grant lifetime.

`ReadQuestionsAsync(sessionId)` returns passive `HostQuestionRecord` history:
immutable host key/revision, typed spec/options, bounded draft/answer, channel,
generation, status and optional exact proposal. It neither refreshes activity
nor authorizes a reply. A future presenter must pass the returned exact key
to the existing services under a live matching host request, reacquire trusted
input and handle Conflict/Expired/Denied. Rendered labels, detail artifacts,
viewer navigation, provider IDs and trace headers cannot create authority.
No R14 chrome changes or generic foreground voice parser are required by
this handoff. Native immutable review/readback remains a later R05 gate.

The removal operation here removes authority rows, not every recoverable
content copy; PERSIST journals/free pages and future backups still require
the full R12 inventory/deletion contract. No inactivity clock, history/search
UI, queue scheduler, automatic purge, arbitrary dispatcher or legacy-grant
migration is delivered. Audit due dates are recorded, but pruning/anchors
and protection against coherent whole-store replacement remain D-008/R04
work; a local hash chain is not same-user/admin tamper prevention.

## Interaction Principle

Every supported Kora workflow has equivalent verbal and mouse/keyboard-accessible operations.
Users can interact entirely by voice, entirely through UI, or change channels at any step without restarting the workflow.
Voice-first means convenient hands-free operation, not reduced UI capability or compulsory speech.
Input and output choices are independent: typing does not require speech output; visual-only output does not mute input.

Questions, answers, summaries, detail navigation, approvals, session management, and settings all use the same host services and validation.
No UI action requires a spoken acknowledgment, and no ordinary Kora approval requires a click merely because it is high risk.
The physical limits of a closed/unavailable microphone and mandatory OS/provider authentication, credential entry, and secure-desktop prompts remain explicit exceptions.
The [in-call settings origin gate](Call_Aware_Speech.md#in-call-settings-origin-gate) is also explicit: during protected calls, voice-initiated voice-setting and in-call-option changes are rejected and require a new UI request; operation approvals, read-only inspection, and deterministic safety controls follow their independent policy.
Bounded manual call controls now enforce that original origin and observed call
revision immediately before mutation, together with live desktop ownership/
privacy. A later local confirmation cannot relabel voice lineage, and denied or
stale changes are not queued for clearance. Legacy action-name grants are ignored
during protected calls without acquiring R05 exact authority; pending approvals
and undispatched reuse are invalidated on call revision changes. New protection
downgrades remain unavailable until the shared complete exact-review path can
support them safely.
Kora neither bypasses these requirements nor dictates credentials into a model.

## Conversational Voice Turns

### Delivered bounded native-question increment

The current implementation provides **Answer by voice** for the authoritative
host's exact live foreground native question, plus the deliberately selected
**Hear question, then answer by voice** action. The latter opens one reply only
after successful playback of that same question. It is not a global automatic
conversation mode or a Settings preference. Existing consent, listening hold,
unlocked interactive Windows session, ownership/private-control revision,
exact device/readiness, local recognizer and call-policy gates remain required;
unknown state denies capture.

One application-wide turn binds the original question key/revision,
session/generation and unique visible presenter. Its accessible countdown allows
15 seconds to start speech without extending on silence. The existing local
single-utterance recognizer supplies one-second trailing silence and a 60-second
maximum utterance; no PTT button-release substitute is used. Cancel/timeout leave
the native question answerable. Target/foreground/session/control changes,
question edits/expiry/answer, privacy/ownership/call/device loss, failures and
closure synchronously retire admission and fence delayed callbacks. Capture
returns to closed/ordinary explicit PTT readiness, not Wake Listening.

Answers match only the exact schema, with no optional assistant-name stripping
and no command, tool, composer or session-routing fallback. Proposal/approval
questions are refused; approval readback specificity and grants are unchanged.
No audio or raw-transcript archive, transcript/content logging, model/network
effects, wake integration or during-TTS capture is added. Accepted structured
answers retain the existing question persistence/audit policy. Maintained
deterministic production-path tests validate this implementation independently
of later hardware/acoustic/installed qualification.
Host-resolved traces preserve same-request presentation parentage and link
deferred reply/recovery causes; incoming correlation never selects the question
or session and carries no transcript content.

### Broader target behavior

The activation name starts an unsolicited user turn; it is not required for
every statement in an already established exchange. When Kora asks a
host-owned question and that question becomes the unique foreground voice
target, Kora may open one bounded **conversational reply turn**. During that
turn, an answer matching the question schema is accepted without the assistant
name or push-to-talk. For example:

> Kora: "This is a large response. Would you like me to show you the details?"
>
> User: "Yes."

`Yes`, `No`, `Not now`, an option name/number, a bounded field value, or a
permitted custom answer is interpreted only against the exact foreground
question/revision. It is not treated as a new general request. The activation
name remains optional inside the reply turn, so "Kora, yes" also works. Speech
that does not satisfy the question schema cannot silently become a command,
tool request, approval, session switch, or answer to another prompt; Kora
reports the mismatch or asks a new clarification turn.

The default prefix-free speech-start window is 15 seconds after Kora finishes
speaking the question. Show an accessible visible listening state/countdown and
use a short non-speech cue when output policy permits. Once speech begins, use
the normal trailing-silence and maximum-utterance bounds. On accepted answer,
explicit cancel, mute, timeout, target change, lock, call-policy loss, device
loss, or question expiry, close the turn, invalidate its audio/transcript
generation, clear buffered audio, and return to Wake Listening when eligible.
Timeout ends only prefix-free eligibility; the unanswered native question
remains available, and the user can later say "Kora, yes to showing the
details" or answer through UI.

Open a conversational reply turn only when all microphone consent, ownership,
unlocked-session, device, local-recognizer, playback-rejection, privacy, and
call gates pass, conversational replies are enabled, Kora actually presented
the exact question, and no higher-priority voice target exists. A visually
displayed but unspoken background question does not silently open transcription.
An explicit native **Answer by voice** action may present/revalidate that exact
question and then open the same bounded turn. During TTS, accept prefix-free
barge-in only after verified self-playback rejection can distinguish the user;
otherwise begin after playback completes.

Only one conversational reply turn exists application-wide. New foreground
questions, session changes, duplicate card hosts, and delayed callbacks cannot
retarget an open microphone generation. Background questions remain UI
answerable but cannot listen. If two possible targets exist, close prefix-free
capture and require an explicitly addressed activation or UI selection.
Generated prose, HTML, Markdown, tools, and skills cannot open a reply turn;
only the host question service can.

Question policy still controls what words are sufficient. Ordinary yes/no
questions such as opening details accept `Yes` or `No`. A high-risk approval
that requires the action and target to be spoken does not become approvable by
generic `Yes` merely because Kora opened a conversational turn. Prefix-free
speech changes input routing, not authorization, confirmation specificity,
speaker confidence, or grant policy.

Conversational turns do not create an unbounded open microphone. After one
answer the capture closes while Kora processes it; if Kora asks another
question, that newly identified prompt may open a fresh bounded turn. Silence
never keeps extending the deadline. The first-run voice explanation and live
presence distinguish ambient Wake Listening from prefix-free Awaiting Reply
and active Capturing Reply.

## Three Connected UI Surfaces

The delivered [compact selected-session projection](UI_Workspace_And_Windows.md#delivered-compact-selected-session-projection) shares one authoritative presentation selection and memory draft with the Sessions workspace.
Explicit reading and canonical draft/submission actions remain separate from passive navigation, New request and standalone question/approval/voice authority.

1. **Compact session interaction:** evolve the response window into the latest interaction for the selected session, with its name, actual work state, concise answer/progress, structured question or approval, Details/History, and typed composer.
2. **Sessions workspace:** reachable from tray, keyboard, compact UI, and verbal commands; Active/Done list and search beside the selected session's full conversation/history, pending cards, and work/queue. A bounded Evidence mode searches and correlates permitted session, diagnostic and audit records without making diagnostics part of the conversation. Shows unread/attention, meaningful activity/due dates, and actual work state, with rename/Done/resume/delete and All work/evidence views.
3. **Detail/artifact viewer:** expands immutable Markdown, static HTML, plain text, diffs, `.ps1` source and task evidence without losing the workspace conversation/current question. Explicitly opened items retain their own session/artifact identity when selection changes; viewing is not execution.

[Session Workspace and Coordinated Window Design](UI_Workspace_And_Windows.md) owns concrete layouts, window roles/navigation, structured cards, focus/drafts, concurrent-session UX, supporting settings/grant/setup surfaces, and original-requirement coverage.

The compact view is a projection of durable session events, not the only copy of the conversation.
A concise answer retains a link to its fuller content; expanding it never reruns work.
The host applies the versioned detail-routing policy after response
finalization. Detail-recommended compact responses expose an exact native Open
details action. For a voice-origin request with no subsequent UI interaction,
or effective voice-first/voice-only operation, the default Offer preference
asks once whether to open that response. The offer is a response-bound
non-consequential question, lower priority than approvals/clarifications; a
unique accepted voice/UI answer opens without a model round trip, while
silence/Not now retains the link. Generated prose cannot create the offer or
become a clickable authoritative control.
Streaming output is provisional until finalized, and summaries cannot claim observed success without receipts.
All surfaces share theme, accessibility, safe rendering, and provenance rules in [Information Display](Information_Display.md).

Closing/dismissing a window changes presentation only, not session lifecycle, work, microphone consent, or approval.
Passive updates do not steal focus. Pending questions, grants, errors, and unknown outcomes remain reachable even when a compact surface auto-hides.
Only ordinary non-interactive feedback uses the response timeout; an actively edited question or presented approval does not disappear under that timer.
Prompt expiry still applies independently and is shown honestly.
Switching sessions does not cancel background work. Content and delayed callbacks remain attached to their originating session.

## Structured Questions and Mixed-Channel Replies

Support single-choice options, multi-choice checkbox lists, yes/no decisions, bounded text, and typed forms where appropriate.
Include free-form clarification when predefined options cannot express the user's answer; do not force every question into a yes/no approval.
Each question declares:

- Session, task, question, and revision IDs; purpose and source.
- Question text, stable option/field IDs, labels, types, constraints, and minimum/maximum selections.
- Whether a custom answer is allowed; required fields; submit/cancel meaning.
- Creation/expiry, consequences, and any changed context/proposal digest.

Voice can list/explain options, select or deselect by unambiguous name/number, supply text, review the draft, and submit/cancel.
Mouse and keyboard edit the same draft; a user can speak one selection, click another, and submit either way.
For multi-select/forms, changing a selection is not submission. No consequential affirmative option is preselected.
Submission validates the complete typed answer and commits once against the question revision.
Corrections and stale answers report their conflict; cancel/dismiss/expiry never means approval.
Sensitive credentials use supported secure flows, not ordinary forms, history, or clipboard-answer shortcuts.

Several sessions may await answers. Explicitly addressed visible UI cards can be answered independently.
Only one spoken prompt is the foreground voice-reply target; it is distinct from the selected chat and executing tasks.
A generic spoken reply needs a unique current target. Naming another session requires presenting/revalidating that question before accepting an answer.
Changing voice focus withdraws the old generic voice-reply eligibility, not the old card's explicitly addressed UI eligibility.
Presentation and answer receipts record the channel and exact IDs; model-generated prose or rendered buttons cannot submit answers.

## Approval and Risk: Session Trust, Not Mouse Superiority

The proposed [Session Permission Levels](Session_Permission_Levels.md) contract adds grant-first Review all / Model review / Approve all to each admitted action route. The risk treatment below describes review when required, not an unconditional prompt under every level. Mandatory prohibited-effect, consent, OS/provider and execution boundaries always apply.

The baseline is the active unlocked Windows user session and the user's choice to enable verbal instructions, not protection against someone controlling that already-unlocked account.
Deliberate activated speech and deliberate native UI interaction are both supported expressions of user intent.
Who speaks an activated command is not itself a baseline authorization condition; residual indistinguishable external speech/playback is accepted without compulsory speaker authentication or blanket voice blocking.
Kora remains responsible for self-playback rejection, supported playback discrimination, ambiguous targets, prompt injection, stale proposals, expanded scope, and unintended dispatch.
It is not endpoint security and cannot secure a compromised Windows account.

Optional locally enrolled speaker verification adds confidence and can enforce an explicitly selected owner-voice preference.
It is not guaranteed authentication, and an ordinary click is not reauthentication either.
Optional separately consented [frequent-speaker learning](Security_Data_Flows.md#optional-local-frequent-speaker-learning) may adapt recognition/personalization from new activated commands only, never chat history or ambient audio.
It is not authenticated-owner identity, cannot grant authority, and cannot silently replace a protected verification enrollment.
Uncertain verification explains the limitation and offers an explicit alternate channel; it cannot manufacture consent.
Required Windows/UAC/provider authentication remains mandatory regardless of channel or voice match.
These are accepted design controls, not claims of completed enforcement; [Security](Security_Data_Flows.md#accepted-controls-and-verification-boundary) defines the implementation/acceptance boundary.

Risk is assigned by host policy to an execution proposal, not by a model describing a script as safe.
Evaluate effects, canonical targets/blast radius, reversibility, environment, data exposure/destination, privileges, and confidence in enforced execution constraints.
Use categorical thresholds rather than claiming a numerical probability from script text:

| Class | Typical proposal | Required treatment |
|---|---|---|
| Informational/safety | Help, factual non-sensitive status, stop speech, pause dispatch | Direct deterministic operation under its fixed policy |
| Low | Registered bounded non-sensitive local read without egress | Explicit request/confirmation as defined for the action |
| Medium | Private capture/egress, exact reversible user-file change, skill save/enable, privacy-affecting preference | Exact scope/destination/change review and deliberate voice or UI confirmation |
| High | Irreversible or broad deletion, production/shared-resource change, security/credential change, software installation, arbitrary code with unverified effects | Explicit consequences, targets, source/diff and recovery limitations; action-specific confirmation through either channel; mandatory OS/provider checks |
| Prohibited | Protected Kora mutation, bypassing lock/containment, exposing credential-store secrets, unsupported execution | Reject; approval cannot override policy |

Unknown or mixed effects receive the highest applicable permitted review class.
Administrator elevation is neither necessary nor sufficient for high risk.
Registered constrained actions may have lower risk only when their effects are actually enforced.
Static analysis/model review is advisory; arbitrary PowerShell may import code or construct effects dynamically.
In Model review, a valid separate tools-disabled reviewer Safe verdict may satisfy ordinary action authority for that exact invocation; it cannot lower host risk, prove arbitrary code safe, create a grant, or supply independent consent. Approve all skips discretionary analysis, not mandatory boundaries.
General script execution remains separately gated, not newly enabled by adding a `.ps1` viewer.

A high-risk spoken confirmation names the action and target, not just "yes".
The equivalent UI explicitly identifies the same proposal and requires a fresh deliberate gesture.
Bind each decision to session/task/invocation, reviewed script/dependency digests, parameters, resolved resources, identity, destination, policy revision, and allowed use.
Approval scope is single-use, that operation for the identified Kora work session, or perpetual, as defined in [Grant Types and Inheritance](Security_Data_Flows.md#grant-types-and-inheritance).
Single-use grants are consumed; session grants end with that session. Perpetual grants have no expiry, retention, or eviction and remain until explicitly removed or edited; archive/deletion/restart do not remove them.
Material changes make a grant inapplicable to the changed operation, without deleting its record. No implicit inheritance across sessions or tasks.
Current once/session/always preferences are not blanket trust of future implementations; the future schema binds session grants to durable work-session identity and retains perpetual grants independently.
Future reusable execution grants bind the exact task/invocation and complete host-computed script/executable/dependency digest set; each dispatch revalidates applicability under current policy.
Protected lock uses the same future version-bound action approval gate for direct and suggested requests; priority routing does not waive it.
See [Skill and task execution design](../docs/skill-and-task-execution-design.md) for these execution gates; arbitrary or prohibited effects cannot become a reusable grant.
The host owns trusted question/approval controls and readback; Markdown/HTML/script comments and model/tool statements are not grants.
A historical grant reference is not a live dispatch token; inspect the independent grant store and fresh host policy.
See [Internal Model Tools](Internal_Model_Tools.md#grant-and-feedback-rules) for the shared grant and in-call feedback boundaries.

Show the session level and spoken-approval eligibility separately. Calls default to UI approval without ignoring grants or changing the level. Changes need exact user confirmation; Active restart preserves the level, Done resets it, and resume restores no old level. [Lifecycle/race rules](Session_Permission_Levels.md#session-ux-lifecycle-and-races) bind pending decisions to policy revision.

## Session Identity, State, and Contents

A Kora session is a durable user work stream containing conversation, tasks, and evidence, not an execution slot or provider memory.
Its immutable ID survives renaming, selection, archive, and explicit resume.
Lifecycle is **Active** or **Done**; work state is separate: idle, queued, running, waiting for user/approval, blocked, failed, cancelled, interrupted, or outcome unknown.
Done means archived, not "every action succeeded". Deleted is irreversible removal, not another browsable status.
A terminal task result does not automatically mark the whole session Done.

The [bounded reviewed-memory store](Model_Providers_Memory_And_Knowledge.md#delivered-session-only-durable-memory-storage---2026-10-09) retains only exact admitted session-owned candidates. Authoritative Done/retirement, disposition and retention invalidate their bodies/receipts with typed audit in the existing transaction and retain non-reusable tombstones. Restart reads data, not review/use or egress authority; broader scope storage remains unavailable.

The [native selected-session memory filters](Model_Providers_Memory_And_Knowledge.md#delivered-native-content-free-session-memory-filters---2026-10-14) combine exact GUID, Scope kind, Review and Retention with AND over the explicit bounded List snapshot. Scope is metadata, not permission or a session/provider selector. Any is initially selected; defined non-session kinds truthfully yield zero rows, never cross-session reads. Every row's full metadata is validated before exclusion. Filter edits revoke selection, inspection, draft, mutation readiness and pending presentation under the existing source/session/control fences, with no store read or activity renewal. Explicit List refresh and separate fresh inspection remain necessary.

Host-owned records include:

- Session ID/title, lifecycle, selected state, creation/last meaningful activity, archive time/reason, retention-policy revision, and deletion due time.
- Ordered user/assistant messages with voice transcripts or typed content, channel, timestamps, revisions/corrections, provenance, and incomplete/final status.
- Questions, options, drafts/accepted answers, decisions, denied/cancelled/expired proposals, and routing decisions.
- Tasks, planned versus observed progress, dependencies, action requests/results/receipts, errors, cancellation certainty, and late reconciliation.
- Reviewed script/diff/artifact snapshots, digests, citations, relevant tool exchanges and context snapshots allowed by source/security policy.
- Approval/grant scope and creation/use/denial/edit/removal evidence, plus expired unanswered proposals, linked to the separate security service without persisting reusable dispatch tokens.

Full retained history is the evidence source. Derived summaries/indexes aid discovery, never replace it or silently evict old entries.
Never store raw ambient/command audio, credentials, tokens, enrollment material, or hidden model reasoning in session history.
Secret screening is best-effort: redact detected secrets before durable storage and identify omitted/redacted/restricted content.
The bounded first native composer is an explicit reject-only exception:
detected credential-like material is refused before draft/message persistence
or admission, with user correction and secure-flow guidance, never silent
redaction or a Save anyway bypass. Accepted text stays exact; this does not
weaken the prohibition on credentials/tokens in retained history or qualify
comprehensive detection.
Source/account restrictions can require removal or prevent retention; record an explicit content-unavailable reason rather than promising an unrestricted copy.
Diagnostics remain content-minimising and separate from intentional session history.

## New Requests and Routing

### Delivered native New request fresh-session fallback — 2026-10-13

**Tray > New request (fresh session only)** and **Sessions > New request (fresh target)** activate one native memory-only composer. Relatedness inference remains unavailable: this bounded route offers only an explicit fresh **New conversation**, never the selected, named, recent or Done session.
The generic host label is not inferred from the candidate body, model or background content.

**Create new empty target** owns one exact original native creation attempt and commits through the audited workspace creation seam. Acknowledge the actual ID/name/generation/metadata revision before separate **Review frozen body** and **Submit reviewed exact body**.
Creation and body retention are separate authority operations, not atomic or rollback-capable; cancellation after creation leaves the disclosed empty session Active.

Submission reuses `SessionConversationService`: reject-only credential screening before any body write, exact immutable USER text, retained original task/request and separate truthful HOST outcome through configured provider-neutral admission. Current production generation stays unavailable.
No model answer, automatic routing, context assembly, adapter/bootstrap fallback, network/auth/setup or legacy typed/voice expansion is delivered.

Edits, including same-byte reassignment, cancel and control/private/source changes retire both review authorities. The original attempt and epoch fence physical COMMIT. **Open this exact session** uses existing explicit navigation and dirty named-buffer protection, never transfers the New request body.

**Review exact saved revision and new body** is available only for the same confirmed created Active target under its original exact generation, metadata and private control epoch, with no creation/submission/save uncertainty.
The [draft workflow](../src/Kora.Application/Hosting/NewRequestComposer.Drafts.cs) observes the canonical snapshot and displays its revision, the confirmed memory baseline and complete old/new bodies. Separate **Confirm Save reviewed draft only** consumes that frozen proposal.

Saving reuses `SaveDraftAsync` and the canonical `ConversationDraftSnapshot`; an optional exact reviewed target is checked inside the existing [private draft transaction](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.Conversation.cs).
A known snapshot is never silently updated to follow a concurrent named-window change. Stale target/revision, failed audit, COMMIT or readback uncertainty holds the unchanged memory buffer without retry; observation never restores Save or Submit authority.

Save uses the existing reject-only screening and required content-free draft audit. It retains accepted bytes exactly without clearing the current body, submitting USER data, invoking admission, executing a conversation task, renewing meaningful activity or changing provider/approval authority. Legitimate native control receipts remain distinct from conversation outcomes. Submit requires its own new review and leaves the saved draft independent.

Dirty means exact body inequality against the last confirmed saved memory baseline, not merely nonempty text. The [native close workflow](../src/Kora/NewRequestWindow.axaml.cs) offers eligible **Save/Discard/Cancel** and a separate frozen revision/body confirmation.
It closes only after an exact acknowledged receipt with no newer edits/revocation. Before confirmed creation or while held, only Discard/Cancel is offered. Failed/cancelled Save keeps the recoverable window; private/owner/call/lifecycle/source closure clears private text and fences callbacks without saving.

The existing saved-draft catalogue discovers these records through its unchanged metadata source. Reload is still a separate explicit Sessions action. No autosave, New request Reload, implicit target creation, profile-global draft, body transfer, serializer/schema/layout/index or aggregate quota is added.

Unknown creation, retention, terminal or receipt outcomes hold the exact attempt without a second target or submission retry. **Inspect exact attempt** reads its retained target/task under current private/retention authority, changes no meaningful activity and grants no submission authority. A changed observed target remains held. Known or possible partial success stays disclosed; no automatic Done/delete is claimed.

Private SQLite and headless native tests are implementation evidence, not installed accessibility, hardware, provider/inference, profile-original, physical-storage or full R12/R14 qualification. Those gates and the storage/profile deferrals remain unchanged.

### Proposed relatedness routing

Explicit targeting wins: input in a session composer continues that session; "new session" starts one; naming a session targets it.
An untargeted general voice/global-composer request starts a new session unless clearly related to an Active session.
Model-assisted matching uses bounded, permitted session descriptors/summaries and the current request, not every transcript or tool result.
Return a typed route proposal with candidate IDs, evidence references, and expected registry revision.
The host validates lifecycle, identity, permissions, and revision before admission.

- One clear active match: continue it and acknowledge the session name, with an immediate correction route.
- Several plausible matches: ask a targeted session-choice question, including New session.
- No clear match or unavailable routing inference: create a new session; offer deterministic session selection rather than guess a relationship.
- Archived match: offer discovery/history; only an explicit resume decision reactivates it.

The selected UI session does not silently redirect an untargeted voice request or authorize a background action.
The priority standalone lock exception uses deterministic
[control-session binding](Built_In_Skills.md#standalone-lock-work-session-binding):
an unaddressed request creates a new durable Active control session without
relatedness inference. Commit/present its identity before approval/dispatch;
Session requires that binding. This neither guesses another Active target nor
automatically marks the control session Done after its lock task.
Exact session list/switch/new/Done/delete controls and basic factual status work without a model or network.
A status/history question reads the addressed records and does not resume the subject sessions.
Relatedness never transfers grants, provider state, sources, or permissions between sessions.

## Concurrent Work and Focus

Independent Active sessions can execute concurrently, including writes to different resources.
Initial proposed default: two executing session tasks, with a configurable limit admitted only within verified provider/hardware capabilities.
One execution slot per session preserves its ordering unless a later explicit design enables intra-session parallel tasks.
Resource holds, dependency readiness, provider budgets, and fair scheduling govern admission; selected session gets no implicit execution priority.
Management and exact local controls remain responsive while all slots are occupied.

Canonical resource declarations and host-owned shared/exclusive leases prevent conflicting writes and read/write races.
The relevant resource may be a repository/worktree, file tree, remote environment, account-scoped object, or OS lifecycle.
Unknown effects or incomplete resource sets cannot claim safe parallelism; serialize in an enforced exclusive execution domain or reject unsupported execution.
Coordinate with external version changes by revalidating the reviewed base immediately before effects; Kora leases cannot lock out unrelated external applications.
A dependency or unresolved remote outcome blocks conflicting/dependent dispatch, not unrelated sessions.

Each session has isolated context, runtime conversation, cancellations, queues, grants, and event sequence.
Provider session IDs are adapter-owned subordinate references, never the canonical history.
TTS is shared and serialized, with session-labelled readback and one foreground voice question.
Background progress remains in its session; eligible notifications follow proactive/call/privacy policy and never replace an approval.
App exit/restart/power preparation coordinates all sessions, not just the selected one.

## Inactivity, Done, Resume, and Delete

Defaults: **archive after 24 hours of inactivity; delete after 30 days of inactivity**.
Both are device-local configurable settings, not hard-coded safety constants.
Use the same last-meaningful-activity clock for both; deletion is not 30 days after creation or archive.
Validate finite positive durations with deletion later than automatic archive; invalid combinations are rejected, never silently clamped.

Accepted substantive user input, an accepted answer/decision, explicit resume, and actual task progress/completion update meaningful activity.
Selection, browsing, search, history questions, polling/heartbeats, and synthetic reminder events do not.
Record durable UTC timestamps and due times; clock rollback cannot trigger premature expiry.
Automatic lifecycle changes do not themselves reset meaningful activity.

Live queued/executing work or an action awaiting resolution is never silently abandoned by archiving/deletion.
Expired questions and queued-task execution lifetimes follow task policy; expiry releases execution eligibility, not retained history.
Unknown side effects remain visibly flagged and block automatic removal pending reconciliation or an explicit informed disposition.
Evaluate lifecycle on a timer and at startup/access, so overdue records are not exposed merely because Kora was closed.
Record archive reason and outcomes; an automatic Done transition is not success.

"This session is done" archives the identified session after resolving live work: wait, explicitly cancel, or decline.
Archive preserves all permitted history and ends active session work and session-grant eligibility; it does not remove perpetual grants, artifacts, or saved skills.
"Resume that session" explicitly reactivates it and resets activity, but does not rerun tasks, restore approvals, or transmit old context.
Previously Done sessions remain readable without resume.

Explicit deletion previews the exact session/data and work impact and requires action-specific voice or UI confirmation.
Automatic purge uses the disclosed configured retention policy without asking again for every eligible expired session; live/uncertain-work checks still apply.
Cancel/reconcile live work first; prevent further appends and remove messages, artifacts, source snapshots, summaries, search indexes, caches, and recoverable database/journal copies according to the store's deletion contract.
Independent perpetual grants survive session deletion, with minimal scope/provenance retained outside chat history; a missing source can make them inapplicable without removing them. Session grants end with the deleted session.
Content-minimising independent security/diagnostic records follow their own disclosed retention and cannot reconstruct deleted conversation.
User exports and provider-side copies are outside local deletion; disclose that limitation.
Do not promise forensic secure erasure or remote cancellation.

Changing either retention setting shows affected due dates; shortening it previews records that would immediately archive/delete and requires a separate explicit apply-now decision.
Without that decision, retain existing sessions' due dates and apply the new policy to new sessions or subsequent meaningful activity; do not defer an unconfirmed immediate purge to the next timer tick.
No setting change silently purges sessions. Storage pressure reports failure/options, never undocumented eviction.

## Persistence and Restart

Persist accepted input/decisions and finalized artifacts as ordered host events; write intent before dispatch and link observed receipts afterward.
Use transactional revision/sequence updates, standard SQLite and managed artifacts/indexes under verified private LocalApplicationData permissions, with schema/backup/deletion recovery.
Use the pinned Windows provider/native closure, not ambient DLLs or optional database downloads.
The [owner-approved storage baseline](Architecture.md#windows-durable-storage-direction) supersedes mandatory page encryption and database-key/rekey workflows; copied/exported files are readable. Credentials remain OS protected.
Recovery reconciles artifact/backup publication as well as SQLite transactions.
Deletion owns managed recoverable copies and rejects late appends; deleting only current rows is insufficient.
Storage failure is visible and blocks consequential dispatch whose required intent/decision cannot be recorded.

Restart restores Active/Done history and UI selection, not action execution, queue dispatch, or consumed/expired approval tokens.
Independent perpetual records and applicable Active work-session grants survive restart; exact implementation/invocation identity and fresh host revalidation determine applicability, while retained chat alone never restores consumed or ended authority.
Recovered non-terminal work becomes Interrupted or Outcome unknown with an explicit resume/replan/reconciliation route.
Every future dispatch revalidates current policy, source access, context freshness, identities, dependencies, and resources.
Preserved readable content is not permanent authorization to use or transmit it.

## Model-Facing Host Tools

The [Internal Model Tool Catalogue](Internal_Model_Tools.md) owns the complete inventory, including current bootstrap actions versus proposed/deferred tools, typed inputs/results, caller lanes, bounds, and host-only exclusions.
Session/history/artifact, structured interaction/presentation, grant, work, and configuration tools use those canonical IDs; this document owns their session behavior, not a second partial tool list.
History and evidence pages contain at most 50 events within the 64 KiB model-bound limit, with continuation/range references and explicit omissions.

Answer submission and approval consumption are trusted host input events, not model-callable self-answer/self-grant tools.
Models propose mutations; the host checks direct user intent, scope, revision, and required confirmation.
Session-history and cross-source evidence retrieval are context-broker operations subject to source/account access, secret filtering, local-only mode, and exact remote-egress review.
Remote-enabled mode is not consent to upload all sessions, even to classify relatedness.
Retrieved scripts/chats/tool results are historical untrusted data, never current instructions.
Questions spanning sessions, diagnostics and audit records return citations to source/event/artifact and separate confirmed facts from model inference.
The Evidence workspace exposes model-independent list/read/search for both log
and audit records. A user-requested Ask Evidence interaction creates a visible
read-only reasoning request over the selected source filters/records; local
reasoning is preferred, remote use previews the exact payload, and every answer
links claims to stable evidence IDs. Follow-ups cannot silently widen the
selection, refresh retention, execute an evidenced action or treat record text
as current intent.
Each durable session exposes a direct Evidence view filtered by its stable
host-owned session ID. It includes every retained diagnostic event, activity
span/link and audit record across all traces associated with that session, with
pivots to trace, task, invocation, approval and audit correlation. Session
selection is navigation only; the host stamps session identity when accepting
or dispatching work. Deleting session content leaves only independently
retained content-minimising audit/session-reference metadata under its own
retention and never reconstructs the conversation.

## Implementation Discussion and Gates

Implement coherent checkpoints: session/event persistence and migration/deletion; shared structured interaction/presenter; session UI and deterministic controls; isolated concurrent scheduler; then bounded model routing/history tools.
These extend Slice A3/A4 rather than claim that the current bootstrap already implements them.
General scripts, browser isolation, biometrics, and provider concurrency retain their dedicated gates.
Library selection, enforced resource profiles, retention clock/recovery behavior, and actual SDK isolation require evidence in the [Decision Register](Decision_Register.md).
Acceptance must exercise pure voice, pure UI, mixed-channel, concurrent-session, restart, expiry/deletion, and hostile-history scenarios in [Acceptance Criteria](Acceptance_Criteria.md).
