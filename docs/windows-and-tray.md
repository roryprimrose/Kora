# Windows, tray, and appearance

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

Type `open all work`, `open saved conversation drafts` or `open compact conversation`, or use the current assistant-name activated prefix. These exact routes activate the existing tray/UI singleton controllers. The legacy `open sessions` route is unchanged. No model window-focus route is available.

Opening the overview or saved draft catalogue reads nothing. Opening compact retains the exact existing Sessions selection and memory buffer; it does not reload saved content or create a session when no target is selected. Dirty drafts and pending questions/approvals remain with their original target. There is no search, submit, resume, dispatch, implicit selection or all-clear claim.

Missing native composition or changed original/current ownership, private/call/input epochs or compact source refuses late opening/focus; issue a fresh command after recovery. See [exact syntax](commands.md#open-existing-workspace-surfaces) and [deterministic evidence](../Design/UI_Workspace_And_Windows.md#workspace-surface-command-evidence).

### Compact selected-session conversation

Open **Tray > Compact selected-session conversation** or **Sessions > Compact conversation**.
Reopening activates the same compact window. It shares the exact selected session and memory composer with the workspace, not a copied buffer or an inferred recent target ([native surface](../src/Kora/CompactSessionWindow.axaml)).

With no selected target, open **Sessions / explicit selection**. Selecting metadata does not automatically read conversation bodies or reload a saved draft.
Use **Read exact conversation / saved revision**, bounded Next, and **Refresh observed work** explicitly. Original USER, any separately retained genuine ASSISTANT answer and typed HOST status have distinct roles ([shared actions](../src/Kora/CompactSessionWindow.axaml.cs)).

Save, explicit Reload, reviewed Clear and Submit use the existing exact-session services. Both windows display edits to the same buffer.
New request remains a separate fresh-target workflow; opening it never transfers the shared text ([shared controller](../src/Kora/SessionsWindowController.cs)).

Closing one view leaves the other view and draft intact. Closing the last dirty view offers Save/Discard/Cancel; failed or stale Save keeps the recoverable original target open.
Privacy, ownership or session retirement revokes both views without saving or stopping unrelated work ([presentation owner](../src/Kora/SessionsPresentationOwner.cs)).

Questions, approvals, voice focus and legacy response presentation remain in their existing surfaces. Explicit Submit uses the same qualified local host execution as Sessions, not legacy inference. No relatedness, voice routing, history assembly, clipboard or export authority is added.
Installed accessibility, physical storage and provider qualification remain separate ([delivery boundary](../Design/UI_Workspace_And_Windows.md#delivered-compact-selected-session-projection)).

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

To retain a draft instead, choose **Review exact saved revision and new body** after confirmed creation. Inspect the saved revision, confirmed memory baseline and complete old/new bodies, then separately **Confirm Save reviewed draft only**. Saving leaves the body in memory; it does not submit, run a model, renew activity or grant approval. Submit always needs its own fresh review and leaves saved data independent.

Save is only available for that same confirmed created Active target under its original generation, metadata and private control epoch. A concurrent saved change fails stale; Kora never silently follows/overwrites it. Failed/uncertain saving retains the buffer with inspection guidance, not retry. The saved-draft catalogue can discover it; **Reload saved draft** remains an explicit Sessions gesture.

Creation, Save and Submit are separate durable operations, not an atomic transaction. Cancel or close leaves the disclosed session and saved data; no automatic Done/delete. Eligible dirty close offers Save/Discard/Cancel and a separate exact Save review. Save closes only after a confirmed receipt with no newer edits/revocation. Before confirmed creation or while held, only Discard/Cancel is offered.

Dirty means the current body differs exactly from its last confirmed saved memory baseline, including an empty replacement for a nonempty saved body. Edits, even same-byte reassignment, retire reviews. Privacy closure clears the buffer without saving. This window has no autosave/Reload or automatic transfer to named buffers.

**Open this exact session** is explicit navigation only and protects an unsaved named composer with its existing Save/Discard/Cancel decision. It does not transfer this New request body.
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

Select an exact session in **Sessions**, then use **Search this conversation** in its conversation pane or **Compact selected-session conversation**. Both views share one private query, result page, continuation and cancellation state; edits and actions in either are reflected in the other ([shared search state](../src/Kora/SessionsViewModel.ConversationSearch.cs), [compact controls](../src/Kora/CompactSessionWindow.axaml)). Search matches whole lexical words in original retained **USER** messages or separate typed **HOST** outcome/reason/task-state fields.
It never searches other sessions, saved draft bodies, Question/Answer receipts, logs, attachments, memories or accounts ([admitted reader](../src/Kora.Application/Hosting/SessionWorkspaceService.ConversationSearch.cs)).

Choose **All**, **USER** or **HOST** for the lexical fields, then optionally select
an exact **Terminal outcome**, **Terminal reason** and **Expected terminal task
state**. Each terminal selector defaults to **Any**. Selected predicates all AND
with the query and role, matching only terminal HOST metadata, never status-like
words in USER text. USER-only with any terminal predicate is valid and yields
zero without changing your selections; incompatible defined combinations also
truthfully yield zero. Editing any field immediately clears results, selected
navigation and Next in both views and fences late callbacks without reading.
Explicit Search is required. Clear resets the role to All and terminal fields
to Any; no query or filter is saved, logged or sent to a model.

Optionally narrow further with inclusive **Minimum message sequence** and
**Maximum message sequence** (decimal 1–100), and **Submitted from UTC** /
**Submitted through UTC** (`yyyy-MM-ddTHH:mm:ss.fffffff+00:00`, for example
`2026-10-16T00:00:00.0000000+00:00`). Empty fields mean Any. Minimum must not
exceed maximum; equal endpoints include that original message. Use exact UTC
format, not `Z`, local offsets or relative dates. Invalid ranges explicitly
fail before reading, never silently broaden search. All ranges AND with the
query, role and terminal predicates; Next binds the original ranges. Editing
revokes results, selected Details and late callbacks without reading. Clear or
private/source loss empties these fields. These are passive original-message
metadata, not generated output, session authority or access to hidden content.

Both views also expose **Minimum observed message generation**, **Maximum observed message generation**, **Minimum observed message revision (1-2)** and **Maximum observed message revision (1-2)**.
Use positive unsigned decimal Int64 generation and revision 1 or 2; empty means Any. Bounds are inclusive, minimum cannot exceed maximum, and signs, whitespace and overflow fail without repair before any body read.
All four AND with the other predicates and bind Next. Edits, Clear and Escape retire results, selection/Details, continuation and stale callbacks without reading; Clear or private/source loss empties the fields without changing the composer or saved draft.
They describe observed retained message metadata, not current session authority or proof of absent unknown/omitted evidence ([range policy](../src/Kora.Core/Conversation/ConversationRangeFilter.cs), [shared controls](../src/Kora/SessionsViewModel.ConversationSearch.cs)).

Optional **Exact request ID** and **Exact task ID** further narrow this same
selected session, AND with every other predicate. Enter complete canonical
lowercase nonempty D-format GUIDs, for example
`aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa`, or leave empty for Any. Uppercase,
whitespace, braces, partial IDs and the all-zero GUID explicitly fail before
reading; input is never repaired. Next binds both IDs. Editing retires rows,
selection/Details, continuation and late callbacks without reading; Clear or
private/source loss empties both fields. These are passive correlation metadata
only: they cannot retarget a session/task or establish identity, intent,
ownership, permission, approval or execution authority. Composer and saved
draft remain unchanged.

Queries use 1–256 UTF-16 characters / 512 UTF-8 bytes and 1–32 distinct terms of at most 64 characters. NFC/invariant case and whole-word OR apply; punctuation separates words rather than becoming regex/wildcard/instruction syntax. Blank or invalid queries explicitly fail, never show every message. Typing alone does not search or save the query.

Results are chronological exact messages, not generated answers, summaries or relevance ranking. Each read scans at most the existing 100-message session bound; default 25 / maximum 50 complete results, counters and cursor fit 64 KiB. **Next conversation matches** stays on the original generation/sequence ceiling/head; later append is excluded and changed terminal status requires fresh Search.

Enter in the query searches. **Cancel conversation search**, **Clear private conversation search** and Escape in the query clear only shared private query/results/cursor, leaving both views open and the memory composer, saved baseline and current question/voice focus untouched. Opening or activating a view performs no search; closing one view preserves the surviving view's shared search. Exact selection, lifecycle/source, private control epoch or last-owner loss clears query/results and fences late callbacks ([shared-search tests](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.CompactSearch.cs)).
Reading retained Done never resumes or renews activity, creates task authority, runs cleanup or sends context to a model. No new copy/export/speech/network action is offered.

### Exact-session conversation and composer

Open **Sessions**, select the exact session and use **Exact-session conversation
and composer**. Its header names the immutable ID, name label, generation,
metadata/saved-draft revision and private control epoch. Read/Next show bounded
chronological **USER** messages and separate **HOST** statuses; no model answer
is invented. Done history stays readable, but new work requires explicit Resume.

Typing writes nothing. **Read exact conversation / saved revision** observes
the stored revision. **Explicitly Save draft** retains one draft for this
session; **Reload saved draft** restores only data. **Review Clear saved draft**
shows the exact saved body/revision; its separate confirmation removes that
copy only. **Discard memory buffer only** leaves saved draft/messages unchanged.
Switching/closing unsaved text offers Save/Discard/Cancel. Lock/privacy/owner
closure clears private text and callbacks without silently saving.

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
bytes, Unicode scalars and UTF-16 code units, with the same Save/Submit bounds
above. There is no separate scalar-count maximum: an astral character counts as
one scalar but two UTF-16 units. Empty/whitespace drafts may be saved, but cannot
be submitted. Malformed Unicode has no byte/scalar total; the display reports it
explicitly rather than counting replacement characters. Unsupported control
content and over-limit drafts remain editable, never shortened or repaired.
This is size/Unicode feedback only, not credential clearance, model readiness
or permission to Save/Submit. It measures only the current buffer, not saved or
reviewed bodies, questions, approvals or assistant answers. Reading it does not
save, submit, renew activity, log content or start any model/device/network work.
Private presentation revocation removes the metrics along with private content.

Save/Submit also perform bounded **local best-effort credential-like checks**:
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

The already-open selected work surface also has a **trusted local events**
status list. Only fixed authoritative local-version work/question facts and
fresh cached maintenance can create a row. Fixed summaries have no titles or
question/remote text. Delivery is visual, focus-preserving and rate-limited;
PresentedNoReplay/dismissed/deferred/expired/category-limited rows are passive
status, not repeated notifications. Exact native review/dismiss/fifteen-minute
defer uses the [same broker as commands](commands.md#trusted-local-events).
No question target, voice output, task/approval deadline, grant or dispatch
changes. Maintenance Check/Open remains in its native window.

**Local event category** offers All, Work, Failure, Attention and Maintenance.
**Local event reason** offers All and each exact delivery reason shown by the
broker. **Local event source** offers All, LocalVersionQueue,
LocalVersionQuestion and CachedMaintenance. **Local event type** offers All,
Queued, Current, Completed, Failed, Blocked, Unknown, UserAttention and
MaintenanceAvailable. **Local event request origin** offers Any, LocalUi,
ActivatedVoice and HostSystem, matching the original request origin exactly.
**Local event queue eligibility reason** offers Any, **No queue reason / not
applicable**, and each exact queue eligibility reason. Questions and maintenance
have no queue reason; this absence is not queue success, eligibility or authority.
**Local event retained queue profile** offers Any, explicit **Absent retained
queue profile**, or an exact catalogue read (`application.get_version`,
`capabilities.list`, `readiness.get`, `runtime.list`, `capabilities.get`, or
`runtime.get_status`). **Local event retained exact capability target** offers
Any or an exact catalogue descriptor ID. An exact target matches only retained
target metadata, never an inferred runtime target. Absent means a complete
validated event has no profile, not missing/truncated/quiet-hidden output.
These historical choices never select a session, enqueue or dispatch a read,
or establish current readiness, ownership or permission.
All filters combine with AND, including exact IDs and revision/generation/
observed/expiry ranges, over only the
already-loaded bounded rows; impossible Source/Type pairs show zero without
changing either choice. Use Tab/Shift+Tab to move between filters and arrow
keys to choose an exact value.
They do not read more sources or change quiet mode. **Clear local event
filters** restores All/Any and clears optional values for every filter. Each edit or clear removes your event selection
and event review detail, even if that event still matches. Select a visible
exact event again before Review/Dismiss/Defer. Refresh and source/privacy
changes also retire selection; no replacement is selected automatically.

**No matching loaded events** means only that these filters match no loaded
row. Broker omissions and routine quiet hidden/suppressed counts remain
separate; this does not mean there are no other events or pending questions.
Filters are native, content-free and memory-only: no new command, speech,
model/tool route, saved preference or meaningful activity renewal.

**Quiet routine notices for this run** turns off first **Work and Maintenance** notice deliveries and hides their notice rows together. **Turn routine quiet off** or **Reset routine quiet to Off** clears it; Kora also starts Off after restart. Status shows the choice, all omitted rows, routine rows hidden and sources suppressed without presentation. Nothing is saved as a quiet preference.

Failures, Attention, required questions/approvals, authoritative work/status, citations and recovery stay available. Quiet never speaks or changes focus, capture, grants, dispatch or deadlines.
Clear permits future new notices only; muted records may return as `RoutineSuppressedNoReplay` passive status, never a backlog. Admission/audit/storage failures show recovery instead of claiming success. Scheduled quiet hours, multi-session reminders and proactive speech remain unavailable.

Open **Tray > Sessions**, **open sessions**, or the existing compact keyboard
shortcut. The bounded All/Active/Done list sits beside selected exact-session
work: queued/current/waiting/blocked/cancelled/removed/Unknown receipts, stable
task/request IDs, revisions/generation, original arrival and explicit pending dispatch order, observed deadlines, pending
capacity and dispatch eligibility/reasons. Ready is an observation, not
permission; failures/recovery and omitted records remain explicit. There are
up to 25 recent queue receipts and 50 nonqueue task/pending-question records
each; use exact history/status for omitted retained records.

The selected-work pane scrolls vertically at the minimum window size. Work actions, required questions and the History/Memory tabs retain their order; Tab/Shift+Tab and scrolling reach off-screen controls without invoking them ([native layout](../src/Kora/SessionsWindow.axaml)). See [session memory controls](commands.md#create-and-manage-reviewed-session-memories) for Source-default snapshot ordering.

Selection and five-second idle refresh are passive and preserve focus.
Use **Inspect selected work ID**, eligible **Cancel**, **Remove pending ID**
or **Confirm clear displayed pending queue** for the exact displayed revisions.
Stale/session/selection changes refuse. **Enqueue local version** adds only
the fixed harmless read; **Dispatch ready local versions fairly** is a separate
manual decision, never automatic or preferential to the selected session.

Pending question metadata stays visible beside history/evidence; answer only
in the original separate Questions window. A question wait has its own expiry,
no scheduler slot and no active-task clock to extend. Browse/filter/close never
resumes work, changes voice/approval targets, consumes grants or extends activity.
Done/disposition/redacted history and restart Interrupted/Unknown quarantine
retain existing gates. Details/files/skills/captions remain separate immutable
or volatile inspections, not queue payloads.

This is not a conversation composer, general effect cancellation, provider/
worker qualification, Ask Evidence, export or rich browser. Real installed
screen-reader/DPI acceptance remains open.

## All work / attention overview

Open **Tray > Sessions > All work / attention overview**, then **Refresh overview**. Opening alone grants nothing and starts no collection. **Next session page** and **Cancel read** are explicit; there is no automatic refresh.

- Each click scans at most ten session identities. The complete page stays within 64 KiB; each independent session source retains the existing fifty task/question bounds and recent queue receipt limit. Scanned/nonmatching/unavailable groups, omitted records and end/byte-limit markers are visible. Next works after zero matches and never skips a byte-deferred session.
- **All / Active / Done** is the stored session lifecycle, not a task state. **Observed needs answer** means pending-question metadata at that snapshot; unavailable sources remain visible. Question IDs are not freshly admitted reply targets.
- Optional **NameSubstring / ExactId** and **Search metadata** (or Enter in the field) narrow the bounded scan using the same [session-list query rules](#search-the-session-list). Names are literal case-sensitive saved-name substrings; exact GUIDs require nonempty canonical lowercase D format. Both modes scan at most ten session identities per click here, not the Sessions direct-ID lookup. Empty query browses; invalid nonempty text is rejected without fallback. Typing performs no read. Search and Refresh start fresh with the current query, All/Active/Done and Needs answer filters; Next continues that exact admitted query/filter/source epoch, even after zero matches.
- **Clear query** (or Escape in its field) discards volatile query/results/selection/continuation without reading and keeps keyboard focus in the field. A second Escape outside the field closes the window. Query/mode/filter edits revoke pending reads and stale navigation; names are never routing or permission authority. Query text is not saved, logged or sent to a model.
- Expand **Optional inclusive observed work metadata ranges** to set minimum/maximum authority revision, queue revision, pending count/capacity, execution slots, observed current fixed reads, or omitted queue-receipt/task/pending-question counts. Empty means Any; endpoints are inclusive and minimum must not exceed maximum. Enter ASCII decimal Int64 only, without signs, whitespace or separators. Authority revision, capacity and slots must be positive; queue revision and counts may be zero. Invalid bounds show recovery and disable Search/Refresh/Next without reading or falling back to Any. **Clear work metadata ranges** clears just these bounds and results without reading.
- Ranges AND-compose with lifecycle, Browse/name/exact-ID, Needs answer, **Observation availability** and positive **Observed content** choices. Only validated **Observed** work matches an active range; Source changed, Read unavailable and Output bound never do. Matching an omission count supplies no retained records and proves no absence, completeness, success or authority. Zero is just the observed scalar, not all-clear. Edits retire rows/selection/continuation and late callbacks, remain available during reads, and clear on close/private closure.
- Select a group to read exact IDs/generations, source revisions/UTC time, fixed-profile slots/capacity, observed current fixed reads, queue/task states, scheduler reasons and captured deadlines. Full private bodies, history, attachments, memory and diagnostic text are not collected.
- **Ctrl+F** focuses native Find for that selected group's **displayed metadata details only**. Enter or **First** finds the first exact case-sensitive literal; **Next / F3** and **Previous / Shift+F3** follow the complete nonoverlapping sequence without wrapping. A native inert match and selected ordinal/count describe only this one captured details string, never page/global inventory, completeness, trust, safety or authority. Match focus requires native acknowledgement; focus failure clears the match/navigation and offers First to retry.
- Find queries are private one-line, well-formed text, at most 256 UTF-16 units / 512 UTF-8 bytes, without control/format/line/paragraph separators. No case folding, normalization, regex, trimming or truncation occurs. Query edits and **Clear** (or Escape within Find) clear the match/count/navigation without reading; Escape keeps the overview open. Find supplies no new clipboard/copy/paste action and does not change existing metadata-query editing.
- Find never searches other rows, row labels, hidden metadata, bodies, conversation, original task input, source paths or retired observations. If an unavailable row has only a suppressed safe projection, only that currently displayed text is searchable; old observed details are not recovered. Selection/page/filter/query/source/control/private changes, read/cancel, selection retirement and closure retire its capture and private query. It performs no store read, refresh, scan, session/task navigation, action, persistence, model/device/network work or activity renewal. A Find match is not a row selection or action target.
- **Observed task state** offers **Any** plus every defined task state, independently AND-composed with all filters above. Only a validated retained task with that exact state matches, including Unknown/Unavailable.
  Missing, unavailable, empty or omitted-only observations cannot match a positive state or prove absence, completion, current dispatch, ownership or authority ([contract](../Design/Work_Management.md#delivered-passive-cross-session-work-observation---2026-10-11)).
- State edits perform no read and retire results/details/navigation/Next and late callbacks. **Clear query** and Escape in its field retire those results without changing independent filters; close clears the volatile state to Any. The labelled accessible selector grants no task/provider/device action ([native controls](../src/Kora/AllWorkWindow.axaml)).
- Optional **Retained TaskId / Retained RequestId** are exact positive predicates. Empty means Any; enter a nonempty canonical lowercase D-format GUID, without whitespace, case repair, zero GUIDs or alternate formats ([query admission](../src/Kora.Core/Storage/SessionWorkOverviewTaskQuery.cs)).
- Both IDs, and **Observed task state** when constrained, must match the **same validated retained task**, not separate tasks. They AND-compose with every other filter/range; missing, unavailable and omitted tasks cannot match or prove absence/exhaustive search ([portable rule](../src/Kora.Core/Storage/SessionWorkOverviewFilter.cs)).
- Matching grants no current task/session selection, ownership, permission, approval or execution authority ([contract](../Design/Work_Management.md#delivered-passive-cross-session-work-observation---2026-10-11)).
- ID edits perform no read and retire rows/details/selection/navigation/Next and stale callbacks. **Clear task correlation** or Escape in either ID field clears both IDs without changing independent filters ([native controls](../src/Kora/AllWorkWindow.axaml)).
- Invalid IDs disable Search/Refresh/Next without fallback; close/private closure clears both volatile fields. No extra task/body read is added ([retirement tests](../tests/Kora.Windows.IntegrationTests/AllWorkWindowTests.TaskQuery.cs)).
- Pages are **not globally atomic**. No global counts, all-clear, ETA, unread tracking or actual provider/resource contention is claimed. Refresh for mutations/new IDs behind the cursor.
- **Open exact session with fresh passive read** navigates the immutable ID through fresh private workspace reads. It never resumes, dispatches, enqueues, reprioritizes, cancels, renews activity, consumes grants or retargets voice/questions. Inspect/cancel authority is not renewed; reply only in the original Questions window.
- Storage has a ten-second total timeout. Privacy, ownership, retained-source, lifecycle/disposition or retention/hold changes reject or clear observations; stale work is never relabelled current. Close clears query text/mode, rows, filters, IDs and continuation and suppresses late callbacks. Browsing causes no cleanup/background heartbeat.

This is a passive bounded overview, not a conversation/composer, general provider manager, Stop all, permission-tier feature or installed-native acceptance proof.

## Search the session list

In **Sessions**, choose **Browse**, **NameSubstring**, **ExactId** or **ExactName**, enter the metadata query, and use **Search session metadata** (or Enter). This searches the persisted list across bounded pages, not just the already loaded rows. It is separate from **Search exact history** and diagnostic Evidence.

- **NameSubstring** is literal, case-sensitive ordinal containment. `%`, `_` and punctuation are ordinary characters, not wildcards or SQL. Use NFC single-line Unicode, no control/format characters or surrounding whitespace, at most 120 scalars / 480 UTF-8 bytes. Invalid input is rejected without trimming, normalization or truncation.
- **ExactId** requires the complete nonempty immutable ID in canonical lowercase `00000000-0000-0000-0000-000000000001` format. It looks up that one session, including unnamed metadata. Unknown/Removed IDs are unavailable, not a fallback list.
- **ExactName** requires the complete canonical name with the same validation as NameSubstring, but matches only whole-name ordinal case-sensitive equality. It never trims, folds case or normalizes input. Duplicate names may return different session IDs; a GUID-shaped name is still only a name, never an ID or action target.
- **Browse** requires exactly empty text and an explicit Search; it never selects implicitly. **Any / Named / Unnamed** filters observed name availability in every mode. Either name mode + Unnamed matches no rows; unavailable or malformed metadata fails closed, not as a fallback inventory.
- **All / Active / Done** applies to the whole metadata search. Outside search it continues filtering only the ordinary current page.
- Optional **Generation minimum/maximum** and **Name revision minimum/maximum**
  are inclusive and must all match alongside the query and lifecycle. Empty
  means Any. Enter positive decimal integers up to `9223372036854775807`;
  signs, whitespace, separators, zero, overflow or reversed ranges are invalid.
  Generation can match named or unnamed rows. Either name-revision bound
  excludes unnamed rows: unnamed is never revision zero. These native
  session-list controls do not extend All Work or the saved-draft catalogue.
- **Next metadata search** continues even after zero matches. Each click scans at most 50 rows plus one bounded lookahead, displays at most 25 matches and retains complete records within 64 KiB. Counts and output/scan limits are explicit; deferred matches are not silently skipped.
- Optional **Name scalar minimum/maximum** count Unicode scalars, not UTF-16 units or displayed grapheme clusters. Empty means Any; enter positive ASCII decimal values within the authoritative `SessionName.MaximumScalars` limit (currently 120) ([name policy](../src/Kora.Core/Hosting/SessionName.cs)).
  Both ends are inclusive; zero, signs, whitespace, separators, overflow, values beyond the name limit and reversed ranges are invalid ([query validation](../src/Kora.Core/Storage/SessionListSearch.cs)).
- Either scalar bound excludes unnamed metadata: unknown is not zero. These bounds must match alongside mode, lifecycle, availability, generation and name revision; they do not extend All Work or saved drafts ([combination tests](../tests/Kora.Core.UnitTests/Storage/SessionListSearchTests.cs)).
- **Cancel metadata search** suppresses late results. **Clear metadata search** (or Escape in its field) discards the volatile query/results. **Refresh** returns to ordinary list paging. Query, mode, state, privacy, source or host changes require a fresh Search.

Enter in any query/range field searches; Escape or Clear erases all bounds.
Editing performs no read and retires prior search results/selection. Numeric
predicates are bound into Next; changed bounds require a fresh Search.
This includes both name-length fields: editing retires prior search rows, detail, exact selection, actions and cursor without reading; Clear/Escape resets both bounds ([native checks](../tests/Kora.Windows.IntegrationTests/SessionsListSearchTests.cs), [read-count checks](../tests/Kora.Windows.IntegrationTests/SessionsViewModelTests.ListSearch.cs)).
Names, generations and revisions are volatile passive metadata, not session
selection, ownership, permission, freshness, historical completeness or action
authority.

Names can duplicate or change. Every result still shows its actual name, immutable ID, Active/Done state, generation and metadata revision; unnamed metadata stays visibly unnamed. Actions use exact observed IDs/revisions, never names.
Unchanged visible selection retains selected work, original question identities, history and memory drafts; a disappeared or changed subject clears stale controls and details.

This is a mutable ordered list, **not an atomic historical snapshot**. Renames/state/deletion and newly added IDs behind the cursor need a fresh Search. Later IDs may appear on Next. End means no later row at that observation, not a complete historical inventory.

Typing, search, Next and selection are passive: no activity renewal, resume, task intent, dispatch priority, voice/question retarget, grant, memory use or model/network operation. Search is not a composer, conversation-history lexical search or caption-monitor control.

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

### Find a literal in the currently inspected original file

In an admitted file preview, selected captured-folder file or retained Session attachment, enter a one-line literal in **Literal Find** and explicitly choose **Find first**. This searches only the displayed immutable original, not every folder file, the current filesystem, a provider or a conversation.

Matching is **case-sensitive**, preserves spaces and punctuation exactly, and treats regex/wildcard-looking text literally. Limits are 256 UTF-16 characters / 512 strict UTF-8 bytes; empty, malformed or unsafe control-bearing input is unavailable. Nothing happens on keystrokes.

**Find next** starts after the selected match's exclusive end. **Find previous** finds the last match ending before or at its start. There is no overlapping cursor step or wrap. End-of-direction is not a claim that the whole file has no match. **Clear literal Find** cancels work and clears its query, cursor and span.

The focused match is inert, non-selectable text with exact source/revision/item/digest and original UTF-16 `[start, end)` metadata; it is not a ranked citation. Full original source remains below. Folder selection/citation navigation clear the old query and cursor; query/mode/search/cancel/source/private/window changes suppress late Find results.

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

Read a catalogue page and select a row. Use **Private one-line Find** in its details pane: **Find first** (Enter), **Find next** (F3), **Find previous** (Shift+F3). Only that frozen displayed text is searched: exact case/spaces, no overlap or wrap, explicit invalid input ([state](../src/Kora.Application/Presentation/AttachmentMetadataFindState.cs)).

Status gives UTF-16 start/end-exclusive coordinates and a focusable inert exact match span. At a boundary the span hides; reverse using the last cursor without wrapping. **Clear Find**, or Escape here, clears query/match/status without closing or reading ([controls](../src/Kora/AttachmentCatalogueWindow.axaml), [gestures](../src/Kora/AttachmentCatalogueWindow.axaml.cs)).

Find cannot change filters/selection, open/resume a session, read files/bodies/paths or copy/paste/export metadata. Hidden and tombstone-suppressed metadata are excluded. Selection/filter/page/refresh/operation/cancellation, privacy/control/source revocation and close erase old Find state; each row needs a new Find ([lifecycle](../src/Kora/AttachmentCatalogueViewModel.cs)).

### Catalogue reading and filters

Open **Tray > Retained session attachments**, then choose **Search / refresh metadata**. Opening alone reads nothing; listing never opens a picker, original file path or attachment body. It shows only already retained private Session slots.

Default **Browse** preserves the removal filter. For one session, choose **ExactId**, enter its nonempty canonical lowercase D-format UUID, then explicitly Search or press Enter in the query field. Uppercase, empty, noncanonical, control-containing or oversized input is invalid, never Browse.

Optionally enter a **literal metadata query** in the separate field, then Search or press Enter there. It matches a case-sensitive literal substring of already displayed Session/source/revision/item IDs, digest or media type, independently of ExactId and lifecycle. For example, `text/` matches retained text types; `TEXT/`, spaces and `*` are not normalized or wildcards. Names, paths, profile IDs, bodies, excerpts and OCR are never searched. Removed slots never match a literal, even with Include removed slots; clear the literal to browse their ordinary provenance. Held metadata can match but cannot open a body.

Optionally enter **Exact local-file source GUID**, **revision GUID**, or **item
GUID**, then Search or press Enter in that field. Empty means Any; otherwise
use a complete nonempty canonical lowercase D-format GUID. No trimming, case
conversion, zero GUID, alias or fallback is accepted. These independent exact
predicates AND-compose with every other attachment filter and match only
Confirmed retained copies, never Held or unavailable/removed tombstones or their
former IDs. They are retained correlation metadata only: not source consent,
current file identity, path disclosure, body integrity, file-read authority or
proof a copy exists. Typing reads nothing and clears rows/details/selection/
continuation and late work. Clear query, Cancel, private/source revocation and
close erase these volatile fields. Exact inspection remains a separate action
with fresh private admission and full stored-byte/hash/provenance validation.

Optionally enter **Exact SHA-256**, then Search or press Enter in that field.
Use exactly 64 lowercase hexadecimal characters, not all zero; empty means Any.
Spaces, uppercase, prefixes and wildcards are invalid, never normalized.
It combines with every other filter and matches only Confirmed retained copies,
not Held or Unavailable. It does not read bytes or reverify body integrity.
Removed provenance never displays its prior digest. Typing performs no read and
immediately clears results, details, selection/navigation and continuation.
Clear query, private/source revocation and close erase the volatile digest.
Signed continuations bind a keyed fingerprint, never the query digest text;
changed predicates or stale authority require a fresh explicit Search.

Optionally enter an **exact observation-source GUID**, **exact device-profile
GUID**, or inclusive **Captured From / Until UTC**, then Search or press Enter
in the field. GUIDs use complete nonempty lowercase D format. Times use exactly
`yyyy-MM-ddTHH:mm:ss.fffffff+00:00` (for example,
`2026-10-16T00:00:00.0000000+00:00`), not `Z` or a local offset. Empty means Any;
either endpoint may be omitted, equal endpoints are included, and From must be
<= Until. Invalid input is not trimmed, converted or repaired.
These predicates combine with every other filter and match only Confirmed
retained copies. Held and removed slots never match; removed rows/details do
not disclose prior source/profile/captured time. The fields are correlation
metadata only, never source consent, profile ownership, session/file-read
authority or proof of current availability. Editing reads nothing and retires
results/details/navigation/continuation/pending work. Clear query, revocation
and close erase all four fields; they are never saved, logged or sent out.

Optionally enter inclusive **minimum / maximum session generation**, **captured
generation** and **storage revision**, then Search or press Enter. Empty means
Any; each endpoint may be omitted. Use positive invariant decimal digits from
1 through 9223372036854775807 with minimum <= maximum. Equal endpoints are
included; signs, whitespace, separators, zero and overflow are invalid, not
repaired. These ranges combine with every other filter and match only Confirmed
retained copies, never Held or removed slots. Removed rows/details do not expose
former versions. Versions are retained correlation metadata only, never source
consent, session/file-read authority, currentness or proof a copy still exists.
Editing performs no read and retires results/details/navigation/continuation/
pending work. Clear query, revocation and close erase all six volatile fields.
Signed continuations bind every endpoint; changing one requires fresh Search.

Choose **Retained captured to observed session generation relationship: Any
(default), Same observed generation or Earlier observed generation**, then
Search. Same compares retained CapturedGeneration with the equal observed
SessionGeneration; Earlier requires CapturedGeneration to be smaller. Both
constrained choices match only Confirmed copies, never Held or unavailable/
removed slots. Any preserves the other filters and existing defaults; all
predicates combine by AND.
This is passive historical metadata, not current session/device identity,
freshness, staleness, completeness, consent, authority, source integrity or
proof a copy exists. Changing reads nothing and immediately clears results,
selection/details/navigation/continuation and pending callbacks. Clear query,
Cancel, source/privacy/ownership revocation and close reset Any without a read.
Invalid selection disables Search and requires a defined choice; no fallback
is applied. Continuations bind the choice and cannot be reused after a change.
See the [owning metadata contract](../Design/File_And_Folder_Ingestion.md#delivered-retained-attachment-metadata-catalogue---2026-10-11).

The literal is limited to 256 UTF-16 units / 512 UTF-8 bytes of valid single-line Unicode without control/format characters. Empty disables literal filtering; invalid input disables Search with recovery guidance and never falls back. Query text is memory-only: not saved, audited, logged, exported or sent to a model/network. No matching retained metadata at the end is reported truthfully; an empty page with Next is not a whole-catalogue no-match.

Choose **Stored session lifecycle: All (default), Active, Done or Removed** independently of Browse/ExactId and **Include removed slots**. All preserves existing behavior. Removed sessions can have only removed-slot provenance: enable Include removed slots to see it. An Active or Done session can also have a removed attachment slot; removing a slot does not remove its session. See the [canonical lifecycle contract and tests](../Design/File_And_Folder_Ingestion.md#delivered-retained-attachment-metadata-catalogue---2026-10-11).

Choose **Exact retained media type: All (default), text/plain or text/markdown** independently of the other filters, then Search. Only the already validated retained type is matched exactly; no filename, extension or path inference occurs. Removed tombstones match only All, even with Include removed slots. Held metadata can match but cannot open a body. Undefined, differently cased, spaced or parameterized types are invalid, never All. No matching retained metadata is reported truthfully at the end of the bounded observation, not as a global total.

Choose **Exact copy status: Any (default), Confirmed, Held or Unavailable** and
optionally enter inclusive **minimum / maximum retained stored bytes**. Empty
means Any; nonempty bounds accept only invariant decimal digits, from 0 through
262144, with minimum <= maximum. Signs, whitespace, separators and overflow are
invalid, never repaired. All filters combine by AND. Held metadata may match but
cannot open a body. Unavailable tombstones can match exact Unavailable with
Include removed slots and a matching lifecycle, but never match byte bounds,
even 0..0. Their size is unavailable, not an original zero-byte-file claim.

Typing performs no I/O. Mode, ID, literal, lifecycle, media-type/copy-status/byte/digest/correlation/version-range/generation-relationship filters and **Include removed slots** edits retire results, selection, navigation and continuation and cancel pending work; use Search again (or Enter in a text field).
**Clear query (Browse)** clears volatile ID, literal, digest, correlation and version-range fields and resets the generation relationship to Any without reading or changing the remaining filters.
Search never auto-selects or opens a match. Escape closes and clears private state. Source/privacy/ownership/control revocation also clears the copy-status/byte fields.

- Each page has at most **16 rows / 64 KiB** and scans at most **64 slots**. **Next bounded page** may continue after zero matches; ExactId does not jump over unvalidated slots. **Include removed slots** shows content-free removal provenance; historical removed slots do not count toward the sixteen-live-file cap.
- Typed authority is validated in bounded segments before rows are shown. **Authority validation pending** pages contain no attachments; use Next to continue. Reads have a five-second budget. Continuations expire after two minutes; any committed authority, storage/profile or query change requires Refresh.
- Rows show exact Session ID, current Active/Done/Removed lifecycle, retained generations, storage revision, source/revision/item/digest headers, byte count/type/captured UTC and copy certification. Unavailable rows/details suppress former generations/revision, digest, source/profile and captured time. No filename, path, excerpt or original source metadata appears.
- **Confirmed** is stored copy certification, not a body-integrity check or current file access. **Held** means replacement disclosure is not certified. **Unavailable** means the slot is removed. Held/removed slots cannot open a body here; use existing exact-session copy recovery where allowed.

Select a confirmed row and choose **Open exact session/attachment**. Kora re-resolves that exact current Session/generation/storage/reference through existing private inspection, then validates actual stored UTF-8 bytes, digest and provenance. A stale/replaced/removed/held selection refuses; it never silently opens a newer file.

A retained Done session remains Done. Browsing does not resume, renew activity, trigger cleanup, create a grant, retarget a question or send model/network content. Finish/discard any outstanding native file review/control first. Query/selection/source/private-owner changes and Cancel/Close clear metadata and exact navigation; late results are discarded.

This is a finite observation, not a global total, whole-inventory snapshot, physical-deletion or forensic claim. Originals and the existing one-file-per-session / 262,144-original-byte / sixteen-retained-body / no-eviction limits remain unchanged. See [native attachment and recovery instructions](commands.md#attach-one-text-file-to-an-exact-session-native-only).

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

After reading history, select a receipt in its bounded sequence/kind/availability/
exact-ID list and choose **Open immutable receipt details**. This requires the
history subject to match the selected session. The host freshly resolves that
persisted event into the existing native read-only details window; it does not
invent an artifact body or recover bootstrap/model response text. Native chrome
shows the retained session, immutable item/sequence, digest and private source
classification. Reopening the same receipt activates its existing viewer;
conflicting snapshots require closing and refreshing, never silent replacement.
Search/source viewing is passive; copying requires explicit private-disclosure
confirmation. Close releases only presentation.

History reading preserves focused controls, selected work and pending questions.
Opening details may activate that owned viewer but never resumes or retargets
work, changes queue priority, extends activity, or creates execution authority.
Privacy/ownership loss clears private presentation. Logical disposition and
automatic retention retire matching open details before deletion. Logical
disposition retains redacted citations; inventoried retention purges old IDs,
which become unavailable, and keeps only a content-free redacted gap/tombstone. File previews,
lexical citations and skill inspection stay separate and volatile.

The same **Sessions** window now also has a bounded
[fixed local-version queue](commands.md#deterministic-local-version-queue).
Select an exact existing session, choose **Read exact queue**, then explicitly
enqueue, manually dispatch ready reads fairly, cancel a displayed pending ID,
or **Confirm clear displayed pending queue**. Generation/entry/queue revisions
are bound to the displayed snapshot; concurrent changes require refresh.
Selection or reading alone never dispatches, changes voice focus, extends
activity or resumes work. The queue requires no model/network/audio and
cannot schedule scripts, arbitrary resources, power or provider effects.
**Pause this session's new dispatch** and **Resume this session's dispatch eligibility** share the exact original-user queue control action. The observed run ID/pause revision, explicit paused/held reason and policy eligibility appear in selected Work and passive All work even when no entries are present.
Pause blocks only future admissions from that session; running reads drain normally and other eligible sessions remain fair. Refresh and exact pause controls remain available during a manual batch; stale displayed revisions require refresh.
Resume never dispatches automatically, changes clocks/retention, clears grants, marks Done or revives previous-run Interrupted work. Fresh runs start unpaused. Routine-notice quiet mode is independent; unknown/audit/storage/private-epoch failures show recovery, not default unpause or physical-stop claims.

Select an existing session to populate its immutable ID, or enter a known exact
session GUID in the history field. Choose **Read exact history**. **Next history
snapshot page** continues only that session's original generation/sequence
ceiling; later appends require a new read. A stale lifecycle requires refresh,
not automatic resume. Pages default to 25, max 50/64 KiB; oversized individual
content is explicitly unavailable with its citation retained. Fields/buttons
have accessible names and keyboard access; history never changes the current
question, approval, focus/voice target or meaningful activity.

Only host-committed questions/final answers, decision metadata and task-state
receipts are available. Migration baselines and gaps are labelled, not an
invented timeline; a task receipt is not proof of an external effect.
Bootstrap user/model messages, response bodies and full conversation composition
are unavailable. There is no model history reasoning, Ask Evidence,
export, replay or playback. File previews, shared-profile skill text and captions
are not imported. Private ownership loss clears pending content.
Exact typed/activated [history/get/search commands](commands.md#bounded-exact-id-session-commands)
use the same service and do not record control intents or mutate sessions.

Done sessions remain readable. Removed sessions do not reappear in the live
list, but their exact known ID can inspect redacted history citations. This
does not restore text, names, authority, approvals or work.

**Search exact history** uses the same exact-ID field. Enter lexical words in the volatile query field and press Enter or the Search button. Results use the existing receipt list; select a match and use **Open immutable receipt details** for fresh resolution. **Next search snapshot page** continues even after an empty page; status reports scanned receipts, gaps and oversized matching omissions. Search defaults to 25 results, scans at most 200 receipts/call and bounds complete output to 64 KiB ([workflow](../src/Kora/SessionsViewModel.Search.cs)).

Optionally narrow receipt **kind**, **task state**, **decision**, **question
status** and **answer channel** using the labelled choices. Each starts at
**Any**; every selected exact value must match along with at least one lexical
word. Missing metadata does not match an exact value, and contradictory choices
can yield no matches. These historical values do not select a question/task,
identify a user, confer approval or answer/replay/mutate anything. Editing a
choice performs no read and clears stale receipts/details/Next; explicitly
Search again. Clear and query-field Escape also restore all choices to Any.
These controls do not change conversation search or typed/voice command syntax.

Use the labelled minimum/maximum **receipt sequence**, **receipt generation**, **source revision** and **audit sequence** fields for inclusive bounds. Empty means Any; source revision accepts zero, while sequence, generation and audit sequence must be positive ([range policy](../src/Kora.Core/Storage/SessionHistoryRangeFilter.cs)).
Enter decimal digits only, with no whitespace, signs or separators; overflow, invalid or inverted bounds are rejected. Every range and typed choice must match alongside a lexical word; absent audit sequence cannot match a constrained audit range. No timestamp is available or fabricated ([receipt policy](../src/Kora.Core/Storage/SessionHistoryEvent.cs)).
Editing a bound clears old rows, selected detail/actions and Next without reading; explicitly Search or press Enter again. Clear and Escape in any query/range field reset all history bounds and choices ([native workflow](../src/Kora/SessionsViewModel.Search.cs), [controls](../src/Kora/SessionsWindow.axaml.cs)).

The **Retained RequestId**, **Retained TaskId** and **Retained SourceId** fields optionally require exact retained receipt IDs. Empty means Any; otherwise enter a nonzero lowercase D-format GUID, with no whitespace, uppercase, braces or other repair ([shared parser](../src/Kora.Core/Hosting/OptionalCanonicalGuid.cs)).
All specified IDs, ranges and typed choices must match alongside at least one lexical word; missing receipt IDs never match a constrained field. These are historical correlation only, not current/session identity, permissions, ownership, approval, task selection/action/replay or source authority ([receipt matcher](../src/Kora.Core/Storage/SessionHistorySearch.cs)).
Editing an ID performs no read and retires results, Next, selection and detail/actions. Clear or Escape in any query, ID or range field resets all predicates. Explicit Search/Enter starts again under the existing cancellation/privacy/source/control fences; no timestamp or unknown current state is added ([native workflow](../src/Kora/SessionsViewModel.Search.cs)).

Escape in the query field clears query/results without closing the window. **Clear query, predicates and results** returns focus to that field; **Cancel search** suppresses pending results. Editing query/subject, closing, privacy/ownership loss or retention retirement invalidates late content. Search does not disable the focused field, replace pending-question targets or renew activity ([native controls](../src/Kora/SessionsWindow.axaml.cs), [deterministic recovery tests](../tests/Kora.Windows.IntegrationTests/SessionsHistorySearchTests.cs), [late-result tests](../tests/Kora.Windows.IntegrationTests/SessionsViewModelTests.Search.cs)). These headless checks are not physical keyboard, installed screen-reader or DPI qualification.

### Logical session disposition

For explicit removal from the live workspace, select the exact existing ID and
choose **Preview logical disposition**. Review the displayed ID, name (only a
label), generation, metadata revision, row counts and retained-data warning.
Nothing changes during preview. **Confirm logical disposition** is a separate
deliberate native action using that single-use host-held preview. There is no
voice, typed `session delete`, model, name-based or automatic deletion route.

Original typed/current-name activated voice may open this **same preview only** with `review session disposition <canonical-D-guid> <positive-generation> <nonnegative-metadata-revision>`. It never follows newer observed revisions or infers a selected ID. Command confirmation is unavailable; the private native owner and existing separate native confirmation remain required ([command contract](../src/Kora.Core/Commands/SessionDispositionReviewCommand.cs), [native presenter](../src/Kora/SessionsWindowController.cs)).

Both launch paths show questions, scoped grants, observations, waits, durable attachments, provider overrides, submitted conversation messages and saved conversation drafts, plus the complete retained-data/exclusion disclosure. The shared composer protection remains Save/Discard/Cancel, with no implicit submission or save ([shared presentation](../src/Kora/SessionsViewModel.cs), [inventory/scope](../src/Kora.Core/Storage/SessionDispositionPreview.cs)).

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
