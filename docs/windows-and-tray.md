# Windows, tray, and appearance

**Product realignment:** conversation input is moving from saved-draft document management to per-conversation in-memory unsent text. The saved-draft catalogue/comparison and engineering-heavy normal workspace controls below describe current implementations approved for retirement or simplification, not the target workflow.
Existing saved content remains governed by retention and private recovery. See the [approved scope](../Design/MVP_Scope.md#approved-retirement-and-simplification).

## Native app provider field review

**Settings > Providers** retains the complete app tuple workflow and adds individual exact model/reasoning/context review. Inspect current catalogue, select the service, review one field, inspect the whole resulting tuple/account/revision/provenance, then confirm or cancel separately. Individual edits retain the app default, other service tuples and session overrides.

Reasoning/context pickers use the saved model. Model review retains both dependent IDs only if supported; missing, stale or incompatible complete bases require the existing full draft/save. No defaults or individual resets are invented. Current production catalogues have no models; these field controls are unavailable. [Settings and recovery](settings.md#app-default-service-and-desired-profiles) describe the exact boundaries.

## Owned instructions and prompts

Open **Tray > Owned instructions and prompts (native editor)**. Choose Instruction
or Prompt in one model-free text editor, then list/inspect or create an exact ID.
Save/Delete use native exact-source/diff review; Save does not enable use.
Changed/missing/replaced bases offer Reload/Overwrite/Cancel, including the
disclosed narrow optimistic post-comparison content race. Dirty close/shutdown
offers Save/Discard/Cancel; privacy closure clears drafts and retires reviews.
Prompt bodies are literal saved user requests, not parameters or system guidance.
See [native editor commands, bounds and recovery](instruction-editor.md).

## Authoritative Sessions work

### Workspace surface navigation

Type `open all work` or `open compact conversation`, or use the current assistant-name activated prefix. These exact routes activate the existing tray/UI singleton controllers. The legacy `open sessions` route is unchanged. No model window-focus route is available.

Opening the overview reads nothing. Opening compact retains the exact existing Sessions selection and memory buffer; it does not reload saved content or create a session when no target is selected. Dirty drafts and pending questions/approvals remain with their original target. There is no search, submit, resume, dispatch, implicit selection or all-clear claim.

Missing native composition or changed original/current ownership, private/call/input epochs or compact source refuses late opening/focus; issue a fresh command after recovery. See [exact syntax](commands.md#open-existing-workspace-surfaces) and [deterministic evidence](../Design/UI_Workspace_And_Windows.md#workspace-surface-command-evidence).

### Compact selected-session conversation

Open **Tray > Compact selected-session conversation** or **Sessions > Compact conversation**.
Reopening activates the same compact window. It shares the exact selected session and memory composer with the workspace, not a copied buffer or an inferred recent target ([native surface](../src/Kora/CompactSessionWindow.axaml)).

With no selected target, open **Sessions / explicit selection**. Selecting metadata does not automatically read conversation bodies or reload a saved draft.
Use **Read exact conversation**, bounded Next, and **Refresh observed work** explicitly. History reads do not read legacy saved text. Original USER, any separately retained genuine ASSISTANT answer and typed HOST status have distinct roles ([shared actions](../src/Kora/SessionsViewModel.Conversation.cs)).

Unsent text is memory-only and separately retained per session while switching topics. Both windows display edits to the same buffer; only explicit Submit retains a new message ([buffer ownership](../src/Kora/SessionsViewModel.Unsent.cs)).
New request remains a separate fresh-target workflow; opening it never transfers the shared text ([shared controller](../src/Kora/SessionsWindowController.cs)).

Closing one view leaves the other view and unsent text intact. Closing the last view protects nonempty input across all topics with Discard/Cancel, even if the selected input is empty. Application exit uses the same protection. No Save-as-document is offered.
Privacy, ownership or session retirement revokes both views without saving or stopping unrelated work ([presentation owner](../src/Kora/SessionsPresentationOwner.cs)).

Questions, approvals, voice focus and legacy response presentation remain in their existing surfaces. Explicit Submit uses the same qualified local host execution as Sessions, not legacy inference. No relatedness, voice routing, history assembly, clipboard or export authority is added.
Installed accessibility, physical storage and provider qualification remain separate ([delivery boundary](../Design/UI_Workspace_And_Windows.md#delivered-compact-selected-session-projection)).

### Wrap editable draft text

**Wrap draft text** is a native keyboard-focusable checkbox beside the editable
draft in New request, Sessions and compact Sessions. Each starts checked, as
before. Uncheck it to scroll long lines horizontally; check it to soft-wrap to
the available width. Only visual rows change, not logical lines or token counts.

The choice is transient and independent in each view. Sessions and compact
still edit the same authoritative body; changing wrapping in one does not change
the other's choice. Closing a view or retiring its private source resets the
choice; changing the selected Sessions source starts with wrapping again.
It is not a saved preference.

Wrapping, resizing and existing reader scaling do not change exact text,
newlines, caret/selection offsets, undo history, dirty state or Save/Submit
reviews. They do not save, read, submit, answer a question or run a model.
Invalid or oversized drafts remain editable with the same limits and rejection
policy. Reviewed bodies, comparisons, ASSISTANT answers, questions, approvals
and source Details are unaffected.

### Exact retained original message Details

After **Read exact conversation** or explicit conversation Search, choose **Details - exact original USER / separate HOST** on one row. The shared row uses the same action in Sessions and compact projections; selecting a session or opening either window does not read/open Details automatically ([shared action](../src/Kora/ConversationPresentation.axaml)).

The separate native window shows the complete unchanged **USER** body as inert plain text. Original session/request/task/sequence, revision, generation/time and USER-source SHA-256 identify that exact retained record. Any genuine finalized **ASSISTANT** answer appears separately with its host turn identity. **HOST** outcome, reason and task state are separate observations, not assistant prose or permission to execute anything ([native chrome](../src/Kora/DetailWindow.axaml.cs)).

Find and exact-source viewing stay passive. Reopening the same unchanged record activates its viewer; eight viewers are allowed. Stale selection/body/source refuses with Refresh rather than opening a newer message. Done reading does not Resume or renew activity. Privacy, owner/source/selection loss, deletion, retention or closure clears private viewer state ([controller](../src/Kora/DetailWindowController.cs)).

These conversation Details grant **no clipboard/export disclosure authority**. Copy exact source, selection, Ctrl+C and automatic copy remain blocked, even after disclosure confirmation; other admitted viewer origins retain their existing policy. Opening Details never generates content, invokes a provider/tool or accesses the network. Exact source, literal USER Find and original-text reuse remain USER-only; they do not silently include assistant output ([copy policy](../src/Kora.Application/Presentation/DetailViewerState.cs)).

### Find a literal in open Details

Type a query in **Literal Find** (`Ctrl+F`), then choose **Find first** or press Enter. **Next** (`F3`) starts after the match end; **Previous** (`Shift+F3`) returns to the previous match in that same nonoverlapping sequence. Find is case-sensitive with unchanged spaces and **does not wrap**. The count status shows the selected match and complete nonoverlapping count, for example **Match 2 of 5**. At a direction boundary the match hides but its ordinal remains; you can reverse direction using the retained cursor. **0 matches** means a completed search found none, not an unavailable or rejected query.

Queries must wholly fit 256 UTF-16 characters / 512 UTF-8 bytes: nonempty well-formed one-line text without unsafe controls. Nothing is normalized or truncated. Only one inert match with an exact end-exclusive UTF-16 location is focused; the complete displayed text and origin stay unchanged.

Find counts only the current frozen, bounded in-memory reader projection, never another source read or the complete source/corpus/history. **Exact source** switches to the admitted source and resets Find. In retained conversation Details, **Find separate typed HOST observation** counts only the separate already-displayed HOST chrome, not the original USER body or ASSISTANT answer. No targets or hidden fields are combined. Switching targets resets Find.

**Cancel Find** releases the match/cursor, count and ordinal and retains the query; **Clear literal Find** also releases the query. Both return focus to the query. Query edits and rendering changes reset navigation and make the count unavailable until an explicit Find. Escape closes Details; private/source loss, replacement or closure clears text and prevents old controls from publishing a match or stale count. Failed match focus retires the result. The match follows the reader text scale; the separate accessible count status is not a live region repeatedly announcing query edits.

There is no Find clipboard/paste/export, logging, persistence, provider or network operation, broader Detail opening authority or installed/accessibility/hardware qualification. See the [admitted-text contract](../Design/Information_Display.md#native-admitted-text-literal-find).

### New request with a fresh session

Open **Tray > New request (fresh session only)** or **Sessions > New request (fresh target)**. Reopening activates the same window and preserves its isolated memory draft. Relatedness inference is unavailable: this entry never continues the selected, named, recent or Done session and never changes question/approval/voice focus.

1. Type the original request; typing saves nothing.
2. Choose **Create new empty target**. Inspect the accepted **New conversation** ID, name, generation and metadata revision. This commits only an empty Active session, not the body.
3. Choose **Review frozen body for this target**, inspect the exact body/target, then separately **Submit reviewed exact body**. Edits or Cancel retire that review, even if the edited bytes later match.

Submit uses the same body bounds, best-effort credential rejection, exact USER retention and admitted execution as the [named-session composer](#exact-session-conversation-and-composer). A separately qualified local host turn may use admitted read-only tools and retain its genuine bounded ASSISTANT answer, displayed separately from HOST/task status. Production profiles remain unqualified and explicitly unavailable; no provider setup, hosted egress, fallback or implicit routing is performed. Cancellation or uncertain commit/readback holds the original attempt without retry; no answer is inferred from admission alone.

Unsent input is memory-only. Creation and Submit remain separate operations; Cancel or close does not roll back the disclosed session or mark it Done. Nonempty close and application exit offer Discard/Cancel. There is no ordinary draft Save/Reload, autosave, catalogue or automatic transfer ([composer](../src/Kora.Application/Hosting/NewRequestComposer.cs), [native close](../src/Kora/NewRequestWindow.axaml.cs)).

**Open this exact session** is explicit navigation only. Other sessions' unsent buffers are preserved without a Save/Discard decision, and this New request body is not transferred.
**Inspect exact attempt (no retry)** reads the disclosed original session/request/task identity without renewing activity. Storage/audit/admission uncertainty keeps the recoverable draft and exact hold; do not blindly create another target or resubmit.

#### Find in the current memory draft

In New request, **Ctrl+F** focuses **Find in current memory DRAFT BODY only**.
Enter a case-sensitive one-line literal, then **Find first in draft** (Enter).
**Find next** (F3) and **Find previous** (Shift+F3) follow the complete forward
nonoverlapping sequence without wrapping. Total and ordinal describe only this
exact current editable buffer, including dirty or invalid drafts. A match selects
and focuses its exact UTF-16 span in the native editable source; unavailable
focus is reported explicitly.

The whole query must fit 256 UTF-16 characters / 512 UTF-8 bytes and be
well-formed, nonempty one-line text without unsafe controls. Spaces and Unicode
are not normalized or truncated. This does not search reviewed text, the
assistant answer, saved drafts, Sessions buffers or hidden sources.

**Clear draft Find**, query edits, body edits (including any future dictation
insertion), reset, review/target/source changes, host input/control changes and
closure retire navigation/count. Private closure clears the volatile query.
Retirement never restores or collapses a potentially user-owned selection.
Find does not edit, save/reload, create, review, submit, capture, invoke a model,
copy or grant new clipboard authority. Existing native editing/copy/paste is unchanged.

### Search one exact retained conversation

Select a session, enter lexical words and choose **Search this conversation** in Sessions or its compact projection.
Both views share query, role, results and continuation.
**All**, **USER** and **HOST** select the fields to search; generation/revision/sequence/time, request/task IDs and terminal outcome/reason predicates are not normal browsing controls ([shared search](../src/Kora/SessionsViewModel.ConversationSearch.cs)).

Search returns chronological retained messages, not generated summaries. Enter searches; Cancel, Clear or Escape in the query retires search without changing the composer. Changing the exact source or private ownership rejects late results. Opening a view performs no search ([native tests](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.CompactSearch.cs)).

The bounded reader validates original identity, revision and snapshot continuity. Queries use whole lexical words, not executable instructions; results never add model context, resume work or establish execution authority ([reader](../src/Kora.Application/Hosting/SessionWorkspaceService.ConversationSearch.cs)).

### Exact-session conversation and composer

Open **Sessions**, select the exact session and use **Exact-session conversation and composer**. Its header shows the readable conversation name (or **Unnamed conversation**) and full stable session ID to distinguish identical names. Sessions and compact share this label; internal generations, revisions and private-control epochs are not ordinary chat chrome ([target label](../src/Kora/SessionsViewModel.Conversation.cs)).

Read/Next show bounded chronological **USER** messages and separate **HOST** statuses; no model answer is invented. Done history stays readable, but new work requires explicit Resume. Exact authority checks and target/effect disclosures for consequential reviews remain unchanged ([submission admission](../src/Kora.Application/Hosting/SessionConversationService.cs), [disposition review](../src/Kora/SessionsViewModel.cs)).

Typing writes nothing. Switching topics preserves a separate memory-only unsent message per exact session, shared by Sessions and compact views. No ordinary Save/Reload/Clear or saved-draft management exists ([buffers](../src/Kora/SessionsViewModel.Unsent.cs)).

**Recover existing legacy text into empty input** is a separate read-only action for the selected exact session. It verifies stable source/revision before importing, refuses nonempty input and never saves, deletes or submits. Missing/unavailable legacy text leaves history and unsent input unchanged. Retention and private authority checks remain; no migration or purge occurs ([recovery](../src/Kora/SessionsViewModel.Unsent.cs)).

**Discard this unsent message...** requires confirmation and affects only that session's memory input. Last-view/application close protects all nonempty topic buffers with Discard/Cancel. Privacy/ownership/session retirement revokes affected private text without saving ([presentation owner](../src/Kora/SessionsPresentationOwner.cs)).

Deliberate Kora **Exit** and **Restart** review all topic buffers and New request using Discard/Cancel, retaining the instruction-editor guard. Cancelled/failed prompts or changed input/private source do not confirm shutdown; state is rechecked after async work ([host guard](../src/Kora.Application/ViewModels/MainViewModel.ConversationShutdown.cs)).

Mandatory privacy/ownership/session-lock retirement and application/OS-required shutdown do not wait for discard review. Unsent text is volatile: no disk autosave, restart restoration or protection against forced termination is promised ([native lifetime policy](../src/Kora/NativeShutdownPolicy.cs), [native retirement](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.Shutdown.cs)).

**Submit exact message — unavailable provider** retains your original exact
text and fresh request/task receipt privately. Today the configured production
catalogue has no qualified model: you receive truthful **HOST Unavailable /
UnregisteredModel** and a terminal **Unavailable task**, not an AI answer.
No rejected-local-runtime fallback, HTTP/provider/authentication, automatic
retry, history context or tools run. Only a known receipt clears its unchanged
memory buffer; saved drafts remain independently stored. Failure/uncertainty
holds the buffer and requires exact conversation/task inspection, not retry.

Bodies must fit 4096 UTF-16 characters **and** 16384 UTF-8 bytes, with exact
valid Unicode/whitespace/newlines. Invalid, empty or oversized submissions are
rejected, never normalized/truncated. Each session admits 100 submitted user
messages; there is no aggregate retained-session, draft-bearing-session or
message storage quota and no automatic eviction. Read pages have at most
50 records/64 KiB complete output. These are predictable work/UI bounds,
not a total database-size or unlimited physical-disk promise.

**Current draft size and shape** updates beside the editable buffer in New
request, Sessions and the expanded conversation window. It shows actual UTF-8
bytes, Unicode scalars and UTF-16 code units, with the same submission bounds
above. There is no separate scalar-count maximum: an astral character counts as
one scalar but two UTF-16 units. Empty/whitespace input cannot
be submitted. Malformed Unicode has no byte/scalar total; the display reports it
explicitly rather than counting replacement characters. Unsupported control
content and over-limit drafts remain editable, never shortened or repaired.
This is size/Unicode feedback only, not credential clearance, model readiness
or permission to submit. It measures only the current buffer, not saved or
reviewed bodies, questions, approvals or assistant answers. Reading it does not
save, submit, renew activity, log content or start any model/device/network work.
Private presentation revocation removes the metrics along with private content.

Submit also performs bounded **local best-effort credential-like checks**:
private-key PEM blocks/candidates, Authorization Bearer/Basic values, explicit
password/client_secret/api_key assignments, declared GitHub token shapes and
JWT-like strings. No model/network/credential verification occurs. Clear
placeholders and inert parameter references are not evaluated; discussion is
not rejected just for mentioning passwords/tokens. Arbitrary passwords,
short values and undeclared formats can be missed; this is not DLP or a
secret-free guarantee. Detected material is refused without persisting a body
or reusable body hash. Correct the memory-only draft or use supported secure
credential flows; there is no Save anyway or silent redaction.

Saved input never auto-submits after restart. Original incomplete intent
recovers as Interrupted without replay. Existing Keep/ordinary retention and
exact disposition include these database cells/journal; uninventoried copies
and Unknown/live work still hold deletion. No new export/clipboard/rich content
action or forensic-erasure claim is supplied. Full generation/global routing
and installed/native/provider qualification remain separate.

The selected work surface shows trusted local events with ordinary **category** and **delivery reason** filters. Editing or clearing a filter retires exact event selection; Review, Dismiss and Defer require a fresh visible selection. Engineering source/profile/target/ID/revision/time queries are not normal Sessions controls ([event presentation](../src/Kora/SessionsViewModel.Events.cs)).

**Quiet routine notices for this run** affects Work and Maintenance notice deliveries/rows only, not failures, required questions, work status or recovery. It does not dispatch, change deadlines or create consent. Clear permits future notices, never backlog replay ([quiet controls](../src/Kora/SessionsViewModel.Events.cs)).

Selected work retains exact task and queue records, explicit unavailable/Unknown states, captured deadlines and omission markers.
**Refresh selected work**, **Inspect selected work ID**, eligible **Cancel**, **Remove pending ID** and **Confirm clear displayed pending queue** remain deliberate exact-source operations.
Existing queued records are not dropped or automatically replayed ([work actions](../src/Kora/SessionsViewModel.Work.cs), [queue controls](../src/Kora/SessionsViewModel.Queue.cs)).

Diagnostic-only enqueue, capacity, prerequisite, reorder, expired replacement and dispatch product controls are retired.
Direct version, capability, readiness and runtime observations remain separate useful queries.
This retirement does not qualify a general worker/provider scheduler or change actual task lifecycle/cancellation authority ([command boundary](../src/Kora.Application/Hosting/SessionWorkspaceService.Commands.cs), [read-only capabilities](../src/Kora.Core/Tools/ReadOnlyCapabilityCatalog.cs)).

## All work / attention overview

Open **All work / attention overview**, then explicitly Refresh.
Browse by saved session name or exact ID, stored **All / Active / Done**, ordinary task state or **Observed needs answer**.
Opening grants nothing; unavailable observations remain explicit rather than an empty success.
Authority/queue/count ranges and retained task/request-ID query machinery are retired ([overview](../src/Kora/AllWorkViewModel.cs)).

Each read consumes a bounded session page, preserving complete-record byte limits, omissions and continuation.
Order affects only the loaded page; Find searches only selected displayed metadata.
Neither grants navigation or execution authority.
Select an observed exact row before explicit navigation; source/private changes retire selection and late callbacks ([overview service](../src/Kora.Application/Hosting/SessionWorkspaceService.WorkOverview.cs), [Find](../src/Kora/AllWorkViewModel.Find.cs)).

## Search the session list

Choose **Browse**, **NameSubstring**, **ExactName** or **ExactId**, then explicitly Search.
Names remain labels, never action authority.
Name availability and stored lifecycle remain useful filters; generation, name-revision and Unicode-name-length bounds are no longer browsing controls ([query policy](../src/Kora.Core/Storage/SessionListSearch.cs), [native controls](../src/Kora/SessionsWindow.axaml)).

Browse requires empty query text. Name queries reuse authoritative name validation; substring/whole-name matching is literal ordinal and case-sensitive. ExactId requires the complete canonical nonempty session GUID. Duplicate names remain independent sessions, while invalid or missing exact sources never fall back to an inventory ([query policy](../src/Kora.Core/Storage/SessionListSearch.cs)).

Search and Next retain bounded scanning and complete output, including empty nonmatching pages with continuation.
**Source**, **NameAscending** and **NameDescending** order only a loaded search page, with unnamed rows last for name ordering.
Query/source/private changes retire stale results; all internal ID, generation, name and metadata-revision validation remains ([search service](../src/Kora.Application/Hosting/SessionWorkspaceService.ListSearch.cs), [page ordering](../src/Kora.Core/Storage/SessionListPageOrdering.cs)).

## Native local file inspection

**Tray > Preview file (local inspection only)** opens the trusted Windows
single-file picker. A separate metadata-only review shows the canonical path,
identity, scope, original host session/origin, byte count and limits. Confirm
that exact review locally before content is read; it expires after two minutes.
The result is inert, immutable plain text with exact-byte digest and provenance,
not a Markdown renderer, attachment or model submission. Close revokes it.
Exact typed/current-name ACTIVATED `preview file` / `clear file preview` use the
same host workflow. Exact **search file** / **inspect file** focus native
lexical search of that admitted revision. Queries execute only in this
native control; bounded exact excerpts carry revision/digest/line citations,
observation time and truthful truncation/failure states. Results stay inert,
volatile and host-only. **Tray > Preview folder** reviews all 1–32 immediate text/Markdown files (1 MiB combined, 256 KiB per file); any subdirectory or inadmissible item rejects the whole selection.

Use native **Refresh this file preview** / **Refresh this folder preview**, or exact typed/current-name ACTIVATED `refresh file` / `refresh folder`, for a fresh complete metadata review of that exact admitted canonical physical source. **Confirm again** before new content reads; an old confirmation cannot be reused. Folder additions/removals/metadata changes are visible in review. Starting refresh retires the old immutable preview/citations. Failure/cancel leaves no admitted preview; missing/replaced/aliased roots require a new native picker selection. Original files are never changed.

There is no automatic refresh, watcher, recursion, registry, durable attachment, knowledge/persistent-index/reasoning authority. Same privacy/ownership/call/control/lifetime and native release fences apply; unverified release blocks clean handoff/exit until recovery.
See [commands and limits](commands.md#explicit-local-file-preview).

Original typed/current-name ACTIVATED `prepare file search for "<query>"` / `prepare folder search for "<query>"` may pre-populate this same exact preview's lexical query. **PREPARED / REVIEW NEEDED** is not a search result: review the exact source and click **Search selected immutable revision locally** separately ([native preparation](../src/Kora/LocalFilePreviewWindow.PreparedSearch.cs)).

Quoted literals use doubled quotes, keep exact case/punctuation/Unicode/interior spaces, and reuse the existing bounded lexical validation. Preparation performs no metadata/content read, refresh, search, clipboard, model, attachment, retention or consent operation.
Missing/wrong-kind/stale/closed previews or changed admission refuse rather than choosing another source; retained attachments and Literal Find are not targets ([host handoff](../src/Kora.Application/ViewModels/MainViewModel.PreparedLocalSearch.cs)).

### Wrap admitted original text

**Wrap admitted text** changes only the presentation of the complete admitted source
in a file preview, selected captured-folder file or already inspected retained
Session attachment. It starts unchecked, preserving each view's original unwrapped
layout. Check it to fit visual rows to the available viewport; uncheck it to scroll
horizontally. Window resizing and reader text scale do not change the source.

The choice is volatile and belongs only to the current source in this window.
Selecting another folder file, replacing/reopening the view, privacy revocation or
closing resets it; returning to an earlier file starts unchecked again. Metadata
and consent reviews have no wrap control. This does not read or revalidate an
attachment, renew activity, save a preference, copy, or grant source-use permission.

Original bytes, decoded text, newlines, spaces and tabs remain unchanged. Literal
Find query, count, ordinal and UTF-16 offsets and lexical citation positions remain
unchanged, without an automatic search. Soft-wrapped visual rows are **not logical
source lines or citation positions**. Notices, excerpts and exact match spans are
not reformatted by this control.

### Decoded/admitted display text size

The native preview passively shows **UTF-16 code units**, **Unicode scalars** and
**strict UTF-8 re-encoding bytes** for only the exact currently displayed admitted
text. Combining marks count separately; an astral scalar uses two UTF-16 units.
Newlines and an actual displayed BOM character count as text, unchanged.
An admitted empty text has zero counts; missing or retired text is unavailable,
not an empty-file claim. Invalid Unicode makes scalar/byte measurement explicitly
unavailable, never replacement text or a guessed zero.

These are **decoded/admitted text metrics**, not the original file's encoding,
on-disk size, retained storage bytes, folder totals or model tokens/capacity.
The separate **Original source bytes at admission** comes from existing admitted
metadata and includes a stripped leading UTF-8 BOM, so it can differ from the
re-encoded text. It is not a fresh filesystem observation.

Changing the folder item changes the measurement subject; no other item is
counted. Retained inspection measures the already loaded historical text on its
existing read channel, without recapture or storage reads. Source/window,
privacy, host input and admission retirement clear the derived values.
This native-only feedback does not search, edit, copy, save, refresh, renew
activity, authorize model use or send text/counts to a response, speech, log or
clipboard. No new text quota or model capacity is inferred.

### Find a literal in the currently inspected original file

In an admitted file preview, selected captured-folder file or retained Session attachment, enter a one-line literal in **Literal Find** and explicitly choose **Find first**. This searches only the displayed immutable original, not every folder file, the current filesystem, a provider or a conversation.

**Match case** defaults checked: matching uses case-sensitive ordinal comparison. Uncheck it for explicit **ordinal ignore case** in this original-file Literal Find only. Neither mode uses the current culture, accent folding, normalization, regex, word stemming or fuzzy matching. Spaces, punctuation, original query and original source stay unchanged. Limits are 256 UTF-16 characters / 512 strict UTF-8 bytes; empty, malformed or unsafe control-bearing input is unavailable. Nothing searches on keystrokes or checkbox changes.

Editing the query or changing **Match case** immediately retires the match, total, ordinal, navigation and focus claims without I/O. Choose **Find first** separately to search again. Reopened/replaced views default checked; no mode is persisted. This option does not change clipboard, metadata, Details, draft Find or model lexical retrieval/citation rules.

**Find next** starts after the selected match's exclusive end. **Find previous** reverses the same complete forward nonoverlapping sequence under the selected comparison, including near-overlapping literals. The total and ordinal describe that sequence only. There is no overlapping cursor step or wrap. End-of-direction is not a claim that the whole file has no match. **Clear literal Find** cancels work and clears its query, cursor and span.

The focused match is inert, non-selectable **original-source text**, even when its case differs from the query, with exact source/revision/item/digest and original UTF-16 `[start, end)` metadata; it is not a ranked citation. Full original source remains below. Folder selection/citation navigation clear the old query and cursor; query/mode/search/cancel/source/private/window changes suppress late Find results.

Retained Find validates the exact private historical Session/storage source after original-file removal, without rereading that path or renewing session activity. No clipboard, export, links, speech, history, query persistence, model or new read permission is added. Native-only Find is separate from existing inspect/search focus routes and unchanged ranked lexical/Markdown search.

### Opt-in Markdown search in an exact file view

In an admitted file/folder preview or **Inspect retained attachment**, choose **Markdown structure-aware (opt-in, this view only)**, then **Search selected immutable revision locally**. New/reopened/replaced views always start at **Lexical lines (default)** / `lexical-lines-v1`; no preference/global mode is saved.

The second mode reports `markdown-structure-v2`. Markdown files use bounded top-level ATX/standalone Setext sections and backtick/tilde fences; `.txt` keeps original line chunks. Lists, tables, code, links and authored image alt text stay exact inert text. It is not a full CommonMark parser, renderer, image reader, download or AI answer.

Oversized/incomplete sections/code are labelled **structural fragments**. Citations still show the original exact revision/digest and UTF-16 locations; bounded authored heading locations are separate context. Queries are unchanged and remain native-only, never history/model/log/clipboard data.

Changing query/mode or **Cancel local search and clear citations** clears results and suppresses late old work. Source replacement, privacy/ownership loss and close retain the same revocation fences. Unknown policies fail explicitly, not into defaults. Retained files are historical snapshots, not a new original-path observation.

Unchanged limits: 256 UTF-16 characters / 512 UTF-8 query bytes, 32 unique terms / 64 characters per term; 2,048-character / 128-line chunks; eight citations / 16 KiB combined UTF-8 excerpts **once for a whole folder**. No overlap/duplicate-count inflation or new read/retention/egress permission is added.

See the [precise supported subset, unsupported formats and maintained evidence](../Design/File_And_Folder_Ingestion.md#delivered-opt-in-markdown-structure-aware-retrieval---2026-10-11). Model grounding, managed knowledge, OCR/vector retrieval and installed/accessibility acceptance remain unavailable or separately gated.

### Show an exact cited source span

After a successful search, select a citation row and press **Show exact cited source span**. Kora reveals and focuses an accessible inert text span containing exactly those original characters; its identity and one-based UTF-16 line/column range (end exclusive) are shown above it. The complete original source remains below. For a folder, the actual exact admitted file selector changes to the cited item, not another item with a similar name.

The action uses only this current successful search generation and exact source/revision/item/digest. It does not open links, render Markdown, fetch images, read the original path, refresh, copy to the clipboard or send content to a model. Retained Session files are revalidated against the current private stored revision; they remain historical snapshots, not live filesystem observations. Truncated results omit matches honestly; navigation never fetches omitted files or expands the admitted scope.

Search completion does not move focus. Query/mode changes, Cancel search, another citation selection, source refresh/replacement/removal, privacy/lifecycle changes and closing the view clear the target and prevent late old callbacks from revealing content. If the exact source is unavailable, inspect the current source and search again; no newer body or cached fallback is substituted.

The span is non-selectable plain text, without clipboard-copy shortcuts. Heading context describes an authored label, not executable navigation; a structural fragment is still a bounded fragment, not an intact section. Full CommonMark/rendering, managed sources, AI grounding and installed accessibility qualification remain separate. See [exact navigation scope and evidence](../Design/File_And_Folder_Ingestion.md#delivered-passive-exact-citation-to-source-navigation---2026-10-11).

## Kora's presence

Kora's presence is the animated group of dots that communicates the assistant's
current state. Its particle cloud appears on a small, borderless, transparent
surface near the bottom-right of the primary display's working area. It floats
above ordinary windows without a rectangular application frame.

The presence is **click-through by default**: clicks, scrolling, and other mouse
events reach the window underneath it, even over the visible dots.
To reposition it, **hold Ctrl, then left-click and drag the visible presence**.
While Ctrl is held, the presence receives mouse events instead of passing them
through, and the pointer over it changes to the **four-way move cursor**, including
when it was already resting over the presence. Release the mouse button and Ctrl
to return to click-through mode.
Either Ctrl key works, and Kora does not need keyboard focus.
An in-progress mouse gesture stays with the window where it began; pressing or
releasing Ctrl mid-gesture takes effect after the mouse buttons are released.

Kora stores the position on
this device and restores it after restart when that display remains connected.
If the saved display is unavailable, the presence returns to the
bottom-right of an available working area.

The presence can be hidden while push-to-talk is armed without recording. It appears
when you interact with Kora, while Kora is processing work, or when a result
requires attention. Colour and motion reflect whether Kora is calculating,
waiting, executing, reporting information, succeeding, or failing.

The presence communicates state, not percentage complete. Hiding it does not
turn off listening or stop ongoing work. Microphone status is separate from
the presence's visibility.

While idle or listening without a prompt needing attention, presence hides
automatically after **10 seconds** without Kora interaction. It does not ask
before hiding. Change **Presence timeout** in the **Presence** group under **Settings > Appearance** to any value from 1 to 60 seconds ([settings layout](../src/Kora/SettingsWindow.axaml)). New interaction
restarts the timer, including Ctrl-dragging the presence.
Active work, speech, approvals, questions, recovery actions, grant editing,
and unacknowledged failures keep presence visible. After these finish, the
timer starts fresh. Hiding presentation never disables listening.

When visual text is enabled or forced by a voice-output problem, Kora opens a
separate compact response surface near the presence. That surface contains
the response, last transcript, typed-command fallback, and a **Dismiss** action.
Drag its title area to reposition it. Kora remembers the position when it is
still on a connected display.

In the **Appearance** group under **Settings > Appearance**, configure ([settings layout](../src/Kora/SettingsWindow.axaml)):

- **Always show** - keep the current response visible until dismissed;
- **Stay on top** - keep the response above other windows; enabled by default;
- **Response timeout** - change the response's 1-60 second inactivity timeout,
  default **5 seconds**.

Presence and response timeouts are independent. **Always show** keeps only
the response window visible; idle presence still uses its own timeout.

## Retained session attachments (metadata only)

### Order this loaded page

After explicitly reading a page, use **Order this loaded page** to choose
**Source order (default)** or **Retained byte count ascending/descending**
(unknown last). This is **page-only, metadata-only**
presentation: it never reads a file, body or path, refreshes a source, changes a
query, scans more rows, changes storage order or gives a global order/count.

This catalogue does not admit display names, so no display-name ordering is
offered. Equal byte counts use the canonical exact retained-slot session ID,
ascending ordinal, as a stable tie-break in both directions. Unknown byte
counts remain last in either direction, not zero. Removed/unavailable provenance
never recovers former names, counts, digest or source metadata.

Changing order erases selection, details, literal Find and native open readiness,
including pending open callbacks. Select a current row again explicitly.
**Clear order (Source order)** restores the original loaded-page sequence
without a read. **Next bounded page** still uses that page's original semantic
continuation, not its last displayed row. The chosen order applies separately to
the next admitted page. Clear query, privacy/source/control revocation and close
restore Source order; existing lifetime and private-admission checks remain.

### Find within selected displayed metadata

Read a catalogue page and select a row. Use **Private one-line Find** in its details pane: **Find first** (Enter), **Find next** (F3), **Find previous** (Shift+F3). Only that frozen displayed text is searched: exact case/spaces, no overlap or wrap, explicit invalid input ([state](../src/Kora.Application/Presentation/AdmittedMetadataFindState.cs)).

Status gives UTF-16 start/end-exclusive coordinates and a focusable inert exact match span. At a boundary the span hides; reverse using the last cursor without wrapping. **Clear Find**, or Escape here, clears query/match/status without closing or reading ([controls](../src/Kora/AttachmentCatalogueWindow.axaml), [gestures](../src/Kora/AttachmentCatalogueWindow.axaml.cs)).

Find cannot change filters/selection, open/resume a session, read files/bodies/paths or copy/paste/export metadata. Hidden and tombstone-suppressed metadata are excluded. Selection/filter/page/refresh/operation/cancellation, privacy/control/source revocation and close erase old Find state; each row needs a new Find ([lifecycle](../src/Kora/AttachmentCatalogueViewModel.cs)).

### Catalogue reading and filters

Open **Retained session attachments**, then explicitly **Search / refresh metadata**.
Browse or prepare one canonical exact session ID; opening and typing perform no read.
Stored lifecycle, inclusion of removed slots, copy status, retained media type and inclusive stored-byte bounds remain available ([native catalogue](../src/Kora/AttachmentCatalogueWindow.axaml), [query contract](../src/Kora.Core/Storage/SessionAttachmentQuery.cs)).

Generation/revision, authority/profile/source correlation, digest, observation-time and file-reference query fields are retired.
Selected details still display admitted source/provenance/integrity metadata; ordinary loaded-page size ordering and literal Find remain.
Displayed metadata is not a body-integrity receipt or execution authority ([presentation](../src/Kora/AttachmentCatalogueViewModel.cs)).

Each bounded page validates authoritative storage and retained row shape before applying ordinary filters. Signed continuations bind the exact query, viewer, source and authority; retired cursor versions are rejected rather than broadened. An empty page with Next does not mean no retained copy exists ([store](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.AttachmentCatalogue.cs)).

Select a confirmed row and explicitly **Open exact session/attachment**.
The existing private inspection re-resolves the exact source and validates bytes, digest and provenance.
Held, removed, stale or unavailable sources never become a different file or a successful fallback.
Browsing does not resume a Done session or add model context/consent ([catalogue service](../src/Kora.Application/Hosting/SessionAttachmentCatalogueService.cs)).

## Tray icon

**Exact operation grants** opens a distinct native retained-metadata inventory, exact-ID inspection, one-ID revocation and separately reviewed atomic revocation of an explicitly selected bounded batch. It is not the legacy Approvals list and creates no execution authority. [Instructions and limits](settings.md#exact-operation-grants) cover bounds, safe provenance, stale confirmation and unknown running work.

**Evidence (read-only)** offers an explicit **AuthorityAudit** source for
committed typed interaction-store audits, separately from diagnostic **Audit**,
**All**, **DailyLog** and **CombinedLog**. It shares the existing bounded native
filter/page/citation display, without copying, export, execution or approval
controls. [Privacy, limits and source distinctions](privacy-safety-and-logs.md#committed-authority-audit-inspection)
describe its snapshot and recovery behavior.

After an explicit successful complete Evidence Search/Next/trace/segment read,
use **Find first**, **Find next**, **Find previous**, or **Clear literal Find**.
This native Find scans only the current serialized page in memory, not retained
sources or bodies. Ctrl+F focuses its query; Enter finds first; F3/Shift+F3 moves
next/previous. One inert exact literal is focused with its UTF-16 location;
case and spaces are exact, matches do not overlap or wrap, and page-local
no-match is not an all-clear. The full page and selected citation stay unchanged.
The volatile query has no copy/paste, export, speech, model or persistence route.
All reads, filter/source edits, replacement, failures/cancellation and private
closure clear it. Failed/unavailable/incomplete pages cannot enable Find.
See [query bounds and retirement](privacy-safety-and-logs.md#logs); installed
accessibility and general Ask Evidence/export remain unqualified/unavailable.

Expand **Advanced filters (optional)** for exact request/invocation/approval/
audit-correlation IDs, inclusive ISO times with `Z` or explicit offset,
diagnostic severity and typed audit outcome. **Clear advanced filters** unsets
them without reading. Any filter edit retires results/selection/Next and
requires a fresh search. [Formats, source support and time semantics](privacy-safety-and-logs.md#logs)
are enforced without broadening authority, copying or export.

The same area exposes exact **Event ID**, **Category** and **Audit action ID**; empty-only unset, canonical signed integer parsing, case-sensitive text and source-specific support are described in [Evidence formats and source semantics](privacy-safety-and-logs.md#logs).
Unsupported sources require explicit recovery rather than an empty-success result; edits, Clear advanced and private closure use the existing retirement lifecycle ([native filters](../src/Kora/EvidenceViewModel.Filters.cs), [viewer lifecycle](../src/Kora/EvidenceViewModel.cs)).

Kora keeps a notification-area icon while running.
Windows may initially place it under **Show hidden icons**. You can drag or pin
the Kora icon into the always-visible notification area using normal Windows
taskbar settings.

- Single left-click: request Kora's presence immediately, without the Windows double-click
  interval.
- Double-click: also open or activate Settings; the first click has already requested the presence.
- Right-click: open the tray menu.

The right-click menu contains:

- **Show Kora**
- **Kora Settings**
- **Documentation**
- **Skill packages (inspection only)**
- **Sessions**
- **Retained session attachments** (metadata only; explicit Refresh and exact historical navigation)
- **Preview clipboard (local plain text)**
- **Release maintenance (notify-only)**
- **Evidence (read-only)**
- **Review local version (native question)**
- **Return to current question** (only the still-live current-run version question)
- Read-only generic **input status** (no endpoint ID/name or transcript in the tooltip)
- **Listening controls > Enable listening / Disable listening**
- **Voice consent / push-to-talk**
- **Microphones** (friendly endpoint labels, native selected-preference marks and availability)
- **Choose microphone (native recovery)** (the same passive card as speech Settings)
- **Refresh microphones** (metadata only; five-second deadline; no model/network dependency)
- **Stop speaking**
- **Exit Kora**

Tray labels use the configured assistant name except for the fixed
Documentation label.

For manual call protection use **Kora Settings > Calls** from the tray. The
native **I'm in a call**, **Clear manual call mode**, **Reset manual off** and
passive **Cached call status** share the [exact current-run commands](commands.md#exact-current-run-manual-call-control).
This adds no ambient detector or separate tray mutation authority. Manual
state is not restored across restart; clearing never clears automatic Unknown/
Active evidence, changes saved flags, speaks, replays output or opens capture.

**Release maintenance (notify-only)** shares the existing native cached
review/snooze workflow with exact typed/activated **maintenance status**,
**maintenance review** and **maintenance snooze**. Commands never check,
refresh, grant/renew network consent or silently open a browser. Review
reveals only the exact fresh native record; snooze affects only its eligible
notice in this run, not pending questions/approvals or security prompts.
Unknown/stale/error state stays explicit. [Command bounds and exclusions](commands.md#exact-cached-release-maintenance)
apply; this is not a general notification broker or installed acceptance.

Selection does not release a privacy/manual-disable recovery hold. Tray clicks
never implicitly activate capture. Windows privacy events hide sensitive
Kora surfaces; unlocking alone does not reveal them or reopen input. Use the
launcher/tray to return to native status and recovery.

### Microphone and listening recovery

The native selections share the same audited preference seam as the bounded
[`speech.input-device` commands](commands.md#exact-input-device-preference).
Commands do not combine selection with Enable or renew consent/permission;
reset explicitly selects System without releasing manual/run holds.

**Choose microphone (native recovery)** opens a non-topmost card with real current
endpoint names/IDs, System/default availability and the retained unavailable pin.
**Refresh devices** is metadata only. Highlight a current endpoint, then choose
**Save preference only**; highlighting/closing is not consent or enablement.
Use a separate fresh **Enable listening (PTT readiness only)** for the displayed
saved endpoint after all current host gates pass. No model, network, working
microphone, test capture or durable question bridge is required or claimed.
Close/Escape does not cancel a task or mark a session Done. Speech Settings
opens the same card and retains its separate consent and held PTT controls.

Open the right-click menu to refresh microphone metadata without recording.
Refresh is single-flight and has a five-second caller deadline. If Windows
enumeration is still finishing after a timeout, retry later; repeated clicks
do not start more workers. Failure reports **Microphone recovery needs
attention** with Refresh/Settings recovery, and capture stays closed.

The tooltip distinguishes PTT-ready (**microphone closed; wake unavailable**)
from actual PTT capture, disabled input, unavailable selection and denied/
unknown Windows privacy. It contains no device identity or private content.
The microphone submenu shows **System** and active named endpoints. A native
selection mark means saved preference, not recording. A missing saved pin
remains marked **unavailable; preference retained**, not replaced. System can
explicitly clear an unavailable pin even when Windows has no usable default.

Choosing an endpoint saves only that current revision's preference. Changed
selection closes input and requires explicit **Enable listening** afterwards;
it does not test the device, record audio or grant consent. A stale menu click,
changed Windows topology, lost ownership, locked/disconnected/unknown privacy
or a failed save reports refusal/recovery. Refresh and choose again.

**Enable listening** requires saved consent and fresh existing host gates; it
only arms PTT. **Disable listening** is always an explicit close, never a toggle
that can accidentally enable from an old menu. **Stop speaking** stops only
playback. **Voice consent / push-to-talk** opens existing Settings for consent
and readiness only, not held PTT or command capture. Use the response window's
push-to-talk control when offered. These actions do not cancel tasks or approve work.
No microphone test or ambient wake is provided by this recovery slice.

Exact typed/current-name activated `stop listening` and `disable listening` use this same native/tray [Disable action](../src/Kora.Application/ViewModels/MainViewModel.VoicePrivacy.cs), not Stop speaking or Cancel task.
Acceptance closes input admission; native release is awaited and unconfirmed release remains explicit. Repeated Disable never toggles back to Enable ([shared stop action](../src/Kora.Application/Voice/ListeningStopAction.cs)).

After manual Disable, queued PTT/transcription, device/default refresh and unlock recovery cannot automatically reopen input. Saved consent and device preferences remain unchanged ([voice stop and recovery](voice-and-audio.md#exact-stop-listening)).
A new deliberate **Push to talk** can capture one command after confirmed release and current gates; native **Enable listening** restores readiness separately. Opening the recovery card alone does neither ([deterministic tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.ListeningStop.cs)).

## Passive document details

### Minimal durable Sessions

Choose **Sessions**, use exact **open sessions**, or press **Ctrl+Shift+S**
in the compact response. **Refresh** lists existing Active/Done durable IDs
and generations with bounded user names and metadata revisions, 25 per page
(50 maximum at the store). Preserved legacy sessions are explicitly unnamed.
Selection reads actual typed question history and current task records, never
a fabricated conversation or progress.
Next session/question/task/evidence controls are independently bounded.
Select an exact task from the current page and choose **Inspect exact selected
task** for its current durable source/state/question and conflict tokens.
Selection alone does nothing. **Cancel inspected pre-dispatch wait** is a
separate deliberate action available only for an admitted current-run
local-version question still waiting before dispatch. It shares the exact
typed/activated-voice task workflow and commits task/question/audit atomically.
Stale, expired, foreign, prior-run, answered, dispatched and Unknown work
refuses; no worker termination or replay is claimed. The native question's
Cancel button uses that same host workflow, and committed exact-text
cancellation closes its outstanding native wait without a late answer.
**Read selected evidence** uses the separate diagnostic/audit projection;
missing conversation/session sources remain unavailable.

Selection and browsing do not redirect questions, approvals, voice input or
the global command box, and never resume. **Mark selected ID Done** and
**Explicitly resume selected ID** are explicit actions on the displayed ID
and expected generation. Live/Unknown work and unresolved questions block
them; Kora does not cancel or abandon work to make them eligible. Ownership,
privacy, call policy/revision, committed control intent and atomic authoritative
audit are checked by the writer, not inferred from the display. A conflict or
failure reports refusal/recovery; refresh before retrying.

Enter a name and choose **Create empty Active session** to deliberately create
a new immutable session ID, Active generation 1 and metadata revision 1.
This creates no execution task, executor, model context, question, approval or
permission; Kora retains an administrative control intent/receipt for audit.
**Rename selected ID** applies only to the displayed exact ID and its observed
generation/metadata revision. Duplicate names are permitted, never ambiguous
authority. A stale edit is refused: refresh and select the exact ID again.
Rename is allowed on Done or unresolved-work sessions without resuming,
abandoning or resolving work, changing grants or extending meaningful activity.

Names must be nonblank NFC Unicode with no surrounding whitespace, at most
**120 Unicode scalars / 480 UTF-8 bytes**, with no control/format characters
or line/paragraph separators. Invalid names are refused, never silently
normalized/truncated. Names are intentional private content, stored only in
the existing private interaction partition, not raw audit/log/activity content.
The validated transactional v1-to-v2 metadata upgrade and v3 task consolidation preserve existing
authority/history and does not invent names. Missing/corrupt/unsupported data
is unavailable, never replaced with empty authority. Create/Rename require
the same fresh original-user ownership/privacy/call admission and required
atomic audit as lifecycle controls; protected/unknown call state denies them.

Done/resume each advance generation and invalidate old scoped authority.
Perpetual records remain independent. Resume does not rerun tasks, revive
approvals or restore/transmit old context. Privacy closure clears this window.
Full conversations, general effect work queues, name-based voice targeting, automatic archive,
full recoverable-copy deletion/retention, export and scheduling are not delivered. No live visual,
screen-reader or OS-session acceptance is claimed by fixture tests.

### Bounded passive interaction history

Select a session or enter its canonical exact ID, then explicitly **Read exact history**.
Next continues the bounded original snapshot; later appends require a fresh read.
Select a receipt and **Open immutable receipt details** to freshly resolve its exact retained provenance into the passive native viewer ([history workflow](../src/Kora/SessionsViewModel.cs), [details](../src/Kora.Application/Hosting/SessionWorkspaceService.cs)).

**Search exact history** accepts lexical words in the volatile query. Enter searches, Next continues, Cancel suppresses pending results, and Clear/Escape clears the query. Engineering receipt/task/request/source IDs, revision/generation/sequence ranges and typed decision filters are retired from normal browsing ([search presentation](../src/Kora/SessionsViewModel.Search.cs)).

History remains passive: no question answer, approval, replay, task execution, activity renewal or implicit source repair. Unavailable/redacted citations remain explicit; privacy/source/lifetime changes fence late content. Internal exact receipt identity and snapshot validation remains authoritative ([history service](../src/Kora.Application/Hosting/SessionWorkspaceService.Search.cs)).

Existing queued records remain inspectable and cancellable under their current exact-source policy.
There is no normal enqueue, dispatch, reorder or expired replacement route; retained deadlines and Unknown quarantine are not rewritten or treated as success ([queue grammar](../src/Kora.Core/Commands/SessionCommand.Queue.cs), [admission boundary](../src/Kora.Application/Hosting/SessionWorkspaceService.Commands.cs)).

### Logical session disposition

For explicit removal from the live workspace, select the exact existing ID and
choose **Preview logical disposition**. Review the displayed ID, name (only a
label), generation, metadata revision, row counts and retained-data warning.
Nothing changes during preview. **Confirm logical disposition** is a separate
deliberate native action using that single-use host-held preview. There is no
voice, typed `session delete`, model, name-based or automatic deletion route.

Original typed/current-name activated voice may open this **same preview only** with `review session disposition <canonical-D-guid> <positive-generation> <nonnegative-metadata-revision>`. It never follows newer observed revisions or infers a selected ID. Command confirmation is unavailable; the private native owner and existing separate native confirmation remain required ([command contract](../src/Kora.Core/Commands/SessionDispositionReviewCommand.cs), [native presenter](../src/Kora/SessionsWindowController.cs)).

Both launch paths show questions, scoped grants, observations, waits, durable attachments, provider overrides, submitted conversation messages and legacy saved conversation drafts, plus retained-data/exclusion disclosure. Opening the review preserves unsent input without a Save decision; only confirmed disposition retires affected memory ([presentation](../src/Kora/SessionsViewModel.cs), [scope](../src/Kora.Core/Storage/SessionDispositionPreview.cs)).

Confirmation removes only this session's live name/metadata, questions
(including drafts/answers), host observations, admitted wait bindings and
scoped grants, and atomically redacts durable history source/content. Exact
event IDs, sequence, source revisions and provenance digests remain readable.
It advances generation and retains a Removed identity tombstone
that cannot be reused or resumed. Other sessions and independent Perpetual
grants are preserved. Live or Unknown work and unresolved questions block
disposition, including expired questions. Kora does not abandon work to permit
removal. An intervening record/task change, stale generation/name revision,
privacy/ownership/call change or unavailable/corrupt store requires a fresh
preview after resolving the blocker.

**This is not full or forensic deletion.** Task/event and content-minimising
authority audit provenance, independent diagnostics, Perpetual provenance,
inert legacy migration storage, SQLite journals/free pages and copied databases
remain outside the specifically inventoried owned SQLite cells and committed journal scope. Submitted conversation bodies, saved drafts and session provider overrides are inventoried; general managed session artifacts, source
snapshots/indexes/caches and managed-backup deletion are not delivered ([authoritative scope](../src/Kora.Core/Storage/SessionDispositionPreview.cs)). Local
disposition cannot erase user exports or provider copies. No inactivity timer,
automatic purge or session retention setting is added.

The receipt says **Committed logical disposition** only after the writer's
single transaction commits removal, tombstone, terminal control success and
required audit. Errors never imply success, abandoned work or rollback of a
possible commit. Refresh to inspect durable state; do not automatically replay
confirmation after an uncertain error. A successful Removed ID disappears from
the list. Closing or switching selection discards the displayed confirmation;
it never performs disposition.

The compact command box and activated voice also expose bounded
[exact-ID session commands](commands.md#bounded-exact-id-session-commands).
They share this host service/store, never inherit the selected row/window,
and require explicit generation/metadata revisions for mutations. Structured
results are observations rather than runtime progress or conversation history.
Protected-call voice mutations remain unavailable; pending bootstrap questions
and approvals are preserved and must be resolved explicitly.

### Local clipboard preview

The separate [clipboard preview](commands.md#explicit-local-clipboard-preview)
uses inert native plain text, not Markdown/HTML parsing or the document viewer's
copy controls. It has exact-ID reuse, revoke/clear and native literal Find. Closing it revokes
the selected snapshot. It does not authorize explanation or model transmission.

**Wrap snapshot text** soft-wraps only the original source body to the available
width. It starts unchecked (the existing unwrapped view); uncheck it to scroll
long lines horizontally. Resizing and reader text scaling change visual lines,
not snapshot text, tabs, line endings, identity, Find offsets/counts or explanation
policy. This is a volatile choice for this source view only: replacement,
revocation, privacy/ownership/call loss, close and disposal clear it. It never
rereads or writes the clipboard, inserts newlines, parses text, grants model use,
renews snapshot lifetime or saves a preference.

Enter an unchanged one-line query and choose **Find first** (or Enter in the query). **Find next** (F3) starts at the selected span's exclusive end; **Find previous** (Shift+F3) returns to the preceding span in that same forward nonoverlapping sequence ([clipboard Find policy](../src/Kora.Core/Context/ClipboardLiteralFind.cs)).
Ctrl+F focuses the query without retargeting questions or voice. Case/spaces/punctuation remain exact; limits are 256 UTF-16 characters / 512 strict UTF-8 bytes, with visible rejection rather than truncation.
Explicit Find reports the complete `n matches; match k/n` count for this snapshot, with one inert exact span and original end-exclusive UTF-16 offsets. There is no wrap: a boundary hides the span but retains the ordinal for reversal. Zero matches clears selection; rejected queries are not zero ([Find implementation](../src/Kora/ClipboardPreviewWindow.Find.cs)).
Editing immediately clears count, ordinal and match without I/O. **Clear literal Find** also releases the query/cursor, not the snapshot or Windows clipboard. Count grants no source, disclosure or model permission ([clipboard boundary](../Design/Security_Data_Flows.md#delivered-r07-local-clipboard-preview---2026-10-07)).
Reader text scaling applies to the snapshot body and exact span without changing query text, offsets or focus; query/status controls remain unscaled ([headless scaling contract](../tests/Kora.Windows.IntegrationTests/NativeReaderTextScaleTests.cs)).
Cut/copy/paste/context routes are blocked, including query shortcuts. Private/owner/call loss, replacement, revoke, close and disposal clear volatile state and fence callbacks. Find never rereads the clipboard or invokes reuse/revoke, inference, persistence, history, activity renewal or a new timer.
See the [canonical contract and automated evidence](../Design/Security_Data_Flows.md#delivered-r07-local-clipboard-preview---2026-10-07); installed-native accessibility/privacy qualification remains separate.

The separate **Review local version (native question)** tray entry opens one
owned native card bound to the original durable host question, not whichever
window is focused. It shows exact target/revision/expiry, an unselected choice,
Review exact record, Save draft, Submit answer, Cancel question and Close.
Editing is not submission; closing/Escape is not approval or grant use.
Expiry, revised/closed targets and unknown privacy/ownership disable the
affected card. Query/audit failures report recovery without a success receipt.
See [the bounded version route](commands.md#show-the-running-version).
This question card is separate from passive Sessions/history and is not a
new side effect or general approval dispatcher.

### Return to the current version question

Use **Tray > Return to current question** after closing/Escape on the native local-version card. This returns to the **same still-live original request, session, task and question**, not a replacement version check. If its exact window is already open, the route activates it; selecting another Session does not retarget it.

Close clears the window's text, unsaved choices and review. Only **Save draft** preserves an answer for return; it does not submit it. A saved draft returns at its current revision; otherwise no affirmative option is selected. **Submit answer** and **Cancel question** remain separate deliberate actions. Cancel commits the original task and question as Cancelled before dispatch, not termination of a running effect.

The original five-minute expiry keeps running while hidden. Reopening does not extend expiry or inactivity, and never creates another intent/question, approval, grant or dispatch. Privacy/ownership loss, changed authority, expiry, cancellation, completed/Unknown work, storage/audit failure and host disposal withdraw recovery and end that wait.

The live foreground card also offers **Answer by voice** and the separately
chosen **Hear question, then answer by voice**. These require existing voice
consent/readiness and do not enable listening. The accessible status/countdown
and **Cancel reply** remain visible during the one-question turn. The microphone
stays closed during playback; 15 seconds are allowed to begin speech afterwards.
Timeout/Cancel reply leave native answer controls available. Closing, hiding,
editing, duplicate presentation or changing the admitted target retires capture;
returning never resumes it. Only **Show local version** or its schema option
number answers this card. See [bounded voice reply behavior](voice-and-audio.md#answer-one-native-question-by-voice).

After restart or completion, this route reports **Unavailable**. Inspect retained read-only status and start a fresh review; Kora never replays the old task or resumes Done. Pending/history metadata cannot turn another saved question into active controls. General question recovery, approvals, provider handoff and full composer remain separate. Headless checks are not installed screen-reader/DPI acceptance.

Choose **Documentation**, select a guide page, then **Open details** to read
that exact page in a separate native window. Its host-owned title, provenance,
revision, digest and sensitivity stay outside the document. Opening the same
page again activates its existing viewer; it does not regenerate the page or
silently replace an older revision.

Use the native Rendered/Source and search controls to inspect the complete
admitted item. Native headings and code are styled; emphasis is plain text,
and tables, images, HTML and diagrams use labelled exact-source fallback.
Use **Continuous text** for cross-block selection/search. **Ctrl+F** finds,
**F3** / **Shift+F3** navigates matches, **Ctrl+U** switches source, and
**Escape** closes only the viewer. **Ctrl+A** selects current continuous
content. **Copy selection** / **Ctrl+C** copies just the selected nonempty
range after the same access/disclosure checks; cut/paste are unavailable.
**Copy exact source** deliberately copies Unicode plain text
with the original Markdown, whitespace and line breaks. It is not rich HTML
copy. Copying discloses the text to other applications and possibly clipboard
history/sync. No page can initiate copy, fetch images, follow external links,
approve an operation or capture model context.

The native-text-v1 source limit is **256 KiB UTF-8**, with **512 blocks**,
**4,096 nodes**, **32 nesting levels**, and **8 open viewers**. Unsupported
content, missing rendering or structural limits show labelled exact-source
fallback; oversized/invalid input reports rejection without truncating it.
Search accepts at most **256 characters**. Closing/hiding details is
presentation only; it does not stop work, finish a session or delete source.
Privacy closure clears and closes details rather than restoring them on unlock.

This is an embedded-document reader, not durable conversation history or
general response routing. Full conversation/history, verbal detail offers, rich HTML,
diagrams, export and full script/diff review remain planned. Native visual and
assistive-technology trials remain pending.

Closing Settings, Documentation, details, or the visual response closes or hides only
that surface. Kora remains available in the background until you choose
**Exit Kora** or use the supported exit command.

## Launching another build and returning

Installed and developer builds share a SID-scoped ownership domain. Ownership
is checked before application services, migration, tray or audio initialization.
A Release launch activates the existing owner, even for a different version or
Debug build, and exits after acknowledgement. It never requests a takeover.
A Debug launch activates the same build. Neither forwards task
arguments. An unreachable, incompatible or unverifiable owner is a blocker,
not permission to run a second assistant.

A different Debug build remains a waiting candidate without microphone, model,
network work or a second assistant tray. The original presents a native
question with independently verified build/content and process identities.
Decline, expiry, lock or candidate death leaves ownership unchanged. Current
setup/reasoning/provider work must finish before handoff; it is not silently
killed. Approved transfer waits for actual desktop/service/audio quiescence.

After a safely released replacement exits, the original process acts only as a
native lifecycle supervisor and offers an explicit return. It has no assistant,
speech or capture services. Accept revalidates the exact original bytes and
normal ownership/readiness; decline or dismissal never restarts automatically.
Release retains the strict unclean-owner blocker after an unverified exit.
Debug-only automatic recovery can proceed on an explicit launch when its
versioned recovery record proves the previous owner is dead and no external work
remains. Legacy markers without that record, unknown process lifetime and
uncertain external work still block recovery. Process death alone is not proof of
worker quiescence. Follow the native blocker rather than deleting files without
checking interrupted work. Recovery never replays tasks or automatically
relaunches Kora. An older owner without activation-only protocol support reports
an error instead of a Release takeover.

Developer builds use `%LOCALAPPDATA%\Kora\Development`, separate from installed
release data and microphone consent. Handoff does not transfer consent,
approvals, tasks or audio.

The bounded 2026-10-05 interactive trial verified same-build Release x64
activation, Debug x64 decline, accepted Debug/Release takeover and
exact-original Release return with one active tray/UI throughout. Exit during
active speech also completed cleanly after asynchronous provider shutdown was
made independent of Avalonia's retired UI context. Active-work refusal,
expiry, candidate death, lock during approval and crash recovery remain
unverified. A framework-dependent Release x86 candidate was blocked before
handoff because the x86 .NET Desktop Runtime was not installed; installer
acceptance must provision and verify that dependency before cross-architecture
takeover/return can close.

## Themes

Kora supports:

- **System** - follows Windows and changes while Kora is running;
- **Light**; and
- **Dark**.

The selected theme applies to Kora-owned visual surfaces, including this
Documentation window. Light and Dark are explicit overrides and do not change
the Windows system theme.

## Assistant identity

Renaming Kora changes visible and spoken assistant identity immediately:

- presence, response, and Settings titles;
- tray labels and tooltip;
- command prefix and recognition grammar;
- responses; and
- voice preview.

The program identity stays fixed as Kora. The executable remains `Kora.exe`.
