# Session Workspace and Coordinated Window Design

Status: agreed window structure; bounded native-text-v1 document details
implemented, with a bounded durable native question/review window; coordinated
authoritative per-session work and bounded ordered history delivered as below.
The native exact-session composer/submitted-user-message foundation is delivered
below; full generated conversations and general concurrent execution remain proposed.

Related: [Interaction and Sessions](Interaction_And_Sessions.md), [Work Management](Work_Management.md), [Information Display](Information_Display.md), [Shared Questions and Recovery](Interaction_Fallback.md), [User Configuration](User_Configuration.md), [Internal Model Tools](Internal_Model_Tools.md).

## Delivered selected-conversation lexical search — 2026-10-13

The shared USER/HOST row additionally exposes explicit [exact retained conversation Details](Information_Display.md#delivered-retained-original-user--typed-host-details---2026-10-14), including compact projections of that row. A separate passive native viewer displays the complete original USER body and separately typed HOST chrome; opening the surface or selecting metadata does not read or open Details. Existing private/source/selection revocation and Done semantics apply; no clipboard authority or installed qualification is added.

The existing private selected-session conversation pane has distinct **Search this conversation**, **Next conversation matches**, **Cancel conversation search** and **Clear private conversation search** controls.
Enter searches; Escape clears only search state without closing, moving question/voice focus or touching composer drafts. The native keyboard-focusable **All / USER / HOST** selector composes with the query: All includes original USER and typed HOST fields, USER only the immutable body, HOST only outcome/reason/expected-task-state strings, never assistant prose. Editing either query or filter immediately clears prior rows, selected match/navigation and Next and cancels late callbacks without reading. Explicit Search observes the new criteria; Clear resets to All.

Results use the same inert native original USER/separate HOST row template as chronological pages. Their request/task IDs and exact bodies are not reinterpreted as answers or executable actions. No result navigation to another session or automatic saved-draft reload occurs. Active and retained Done use the same admitted exact-session reader, snapshot continuation and final source validation.

The [canonical search contract](Interaction_And_Sessions.md#delivered-passive-exact-conversation-lexical-search--2026-10-13) owns lexical/work/output bounds, counters and invalidation. Session/source/private changes clear query, rows and cursor and fence in-flight publication.
Both views additionally expose labelled, keyboard-focusable **Terminal outcome**,
**Terminal reason** and **Expected terminal task state** selectors with accessible
names identifying their exact HOST enum and no-read edit behavior. Each defaults
to **Any** and uses the same shared volatile typed filter. Predicates AND with
query/role; USER-only with any predicate remains selected and truthfully yields
zero. Editing any selector immediately revokes rows, selected navigation,
continuation and delayed callbacks in both views without reading. Clear and
source/private revocation reset all terminal selectors to Any. No body rewrite,
assistant prose, generated output or receipt-history search is added.
The [terminal-control tests](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.ConversationTerminal.cs)
cover all defined choices/Any, shared binding, no-read revocation and explicit
invalid-enum refusal; headless controls are not installed accessibility proof.
The [compact projection](../src/Kora/CompactSessionWindow.axaml) exposes these same controls over the workspace's single query/filter/page/cursor and cancellation state. Opening either view, typing or changing the field filter reads nothing; closing one view retains the shared state until the last owner closes. [Headless shared-search tests](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.CompactSearch.cs) cover keyboard actions, retained Done, saved baseline and late-source revocation; [native field-filter tests](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.ConversationRole.cs) cover shared binding, accessible control names, zero/invalid filters and no-read selection/continuation revocation.
Native-only query entry adds no activated/model descriptor or generic-global-search retarget. Headless binding tests do not qualify installed keyboard/accessibility/DPI or full R12/R14 runtime acceptance.

## Delivered exact-session native composer — 2026-10-12

### Separate saved draft catalogue — 2026-10-13

Use **Tray > Saved conversation drafts (metadata only)**, then explicit Refresh. All retained saved-content rows are the default; Active/Done filters and Next consume bounded mutable metadata pages, not bodies.
Choose **Browse**, literal case-sensitive **NameSubstring**, or canonical lowercase **ExactId**, then **Search / refresh metadata**. The private volatile field performs no read on typing; invalid/empty queries never fall back to All. **Clear query (Browse)** resets without reading.
Optional labelled native **Exact saved revision**, **Minimum UTF-8 bytes**,
**Maximum UTF-8 bytes**, **Minimum Unicode scalars** and **Maximum Unicode scalars**
fields compose with every mode and lifecycle filter.
Empty means Any; strict positive revision/inclusive bounded byte and scalar parsing and
visible invalid-field refusal follow the [canonical metadata policy](Interaction_And_Sessions.md#saved-conversation-draft-catalogue--2026-10-13).
Editing any field immediately revokes page, selection, exact navigation, Next
and late callbacks without reading; Clear/source/privacy/close clears the fields.
Query/mode/filter edits cancel pending callbacks and clear the observed page/cursor/selection immediately, leaving the existing Sessions composer/target untouched. Exact IDs remain distinct from ID-shaped names; empty scanned pages can still have Next. No result is automatically selected.
Select one immutable row and **Open exact session (no body load)**. Exact private source/revision revalidation precedes selection in the existing Sessions owner; no second composer or body authority exists.

Saved revision/length readiness is visible, not body-integrity certification. **Reload saved draft** remains a separate Sessions gesture; dirty-buffer Save/Discard/Cancel remains authoritative.
Catalogue privacy/source/control/lifecycle/draft/close revokes its own query/IDs/names/continuation before late callbacks, without cancelling unrelated windows. Done browsing never resumes work. See the [canonical catalogue contract](Interaction_And_Sessions.md#saved-conversation-draft-catalogue--2026-10-13).

**Sessions > Exact-session conversation and composer** shows the original
immutable user messages and separately labelled **HOST** status. It names the
selected immutable session ID/name/generation/metadata and saved-draft revision,
plus private control epoch. Read/Next are bounded passive pages, including
retained Done content without Resume or activity renewal.

The multiline composer is not global activation, a question answer, approval,
queue payload or model tool. **Submit exact message — unavailable provider**
discloses private retention and unavailable generation before the gesture.
Only the exact existing Active target can accept new work. The original body
and host request/task are durably retained through the real configured turn
admission path; production produces a truthful unavailable terminal, no
assistant/model answer and no rejected-local bootstrap fallback.

Draft editing writes nothing. Read observes saved revision; explicit Save,
Reload, reviewed exact Clear and memory-buffer-only Discard are distinct.
One saved draft per exact session is retained across restart without automatic
Submit. Unsaved switching/closing offers Save/Discard/Cancel; private/source/
owner loss clears presentation and late callbacks without saving. Known
submission receipt clears only its unchanged buffer; uncertainty retains/holds
it with exact conversation/task inspection guidance.

The [canonical conversation contract](Interaction_And_Sessions.md#delivered-exact-session-conversationcomposer-foundation--2026-10-12)
owns body/session/page limits, no aggregate session/draft/message quota,
audit/migration, no-replay and copy/deletion semantics. Plain native historical
text adds no clipboard/export/rich rendering or arbitrary response parser.
Question focus, selected work, existing receipt history, provider policy and
voice routing stay independent. Headless tests are not installed Narrator/DPI,
runtime, hardware or full R14/A4 qualification.

## Delivered explicit expanded-prompt submission — 2026-10-13
The existing native required-input prompt window now also offers a distinct exact-session retention review. Refresh/Next navigates admitted bounded metadata, with no default selection. Separate native confirmation shows full expanded USER text and exact target/source observations; only that confirmed body enters the existing conversation authority.
This does not switch the Sessions window, voice focus, attention target or current composer, nor save/clear any existing draft. Review loses validity on edits, source/use/private/owner/target changes or focus loss. Known receipt retires the form; unconfirmed submission is held for conversation/task inspection, never retry. Preview and old Use remain ephemeral and generation stays unavailable.

## Product Goal and Ownership

Kora is a concurrent work-session application with voice, mouse, and keyboard interaction, not a single linear conversation with a transient answer popup.
The compact window stays simple, but is a view into a durable selected session.
The larger **Sessions workspace contains the session list beside the selected session's full conversation**.
A separate detail/script viewer supports deeper review without losing the conversation or current question.
This document owns cross-window layout/navigation; the interaction contract owns lifecycle, routing, concurrency, and typed question semantics.
Security controls constrain these workflows; they are not a substitute for designing the workflows.

Voice-only, UI-only, and mixed operation use the same services and retained state, subject to explicit call-origin/secure-workflow exceptions.
Every session/task/question/artifact control names its actual target; ambient animation, window focus, and selected session never supply execution authority.

The shared local-file inspection window provides native-only [current-source literal Find](File_And_Folder_Ingestion.md#delivered-native-current-source-literal-find---2026-10-12). First/Next/Previous focus one inert exact match in that private window, never activate another window or retarget session/work/question/voice authority.

Captured-folder selection and citation navigation clear the old Find subject/query. Retained Done files stay historical and passively revalidated.

### Exact selected-session provider review

**Sessions > Selected-session provider overrides** exposes saved sparse service/model/reasoning/context, inherited app fields and separately effective resolved/unavailable state with current catalogue/account provenance. Refresh/review and separate confirmation bind the exact selected session ID, generation/policy revision, private control and selection epoch; stale/hide/close changes never save or retarget.

Individual **Inherit app** resets only that field; **Use app defaults for this session** reviews clearing all four. The same [exact typed/current-name activated service](Model_Providers_Memory_And_Knowledge.md#session-overrides-and-effective-resolution) owns commits. No selection means select a session, never app fallback.

The independent model/reasoning/context pickers change only one sparse field from the current effective tuple; reasoning/context options come from the effective model, not the separate complete-profile draft. Review displays the field, old/new whole effective tuple, sparse changes and revisions. Missing/incompatible tuples require complete-profile recovery; picker changes invalidate native confirmation and cancel writes nothing.

Production supplies no models, so model-dependent save controls are unavailable. Desired service selection remains unresolved and cannot authenticate, dispatch, increase an envelope or reroute the buffered local reasoner. Headless controls/private-store assertions do not qualify native hardware/accessibility/provider installation.

### Delivered Authoritative Sessions Work Surface - 2026-10-09

**Optional prerequisite** is a separate same-session task metadata picker for all six Enqueue buttons, with an explicit **None** button and initial None default. It combines at most 50 nonqueue tasks and 50 already-observed queued tasks (current plus existing recent rows). Rows show immutable IDs, observed generation, real host task state/revision and UTC host source, never guessed queue-entry revisions. Work/question selection cannot choose a prerequisite.

The prospective consequence states that only exact Succeeded permits dispatch and other states can block FIFO; no automatic run is promised. Refresh/source/session/private changes revoke selection, and successful enqueue requires a new explicit choice for the next read.
Stale/missing revisions fail at the same audited enqueue COMMIT, not into None. See [bounded parity and qualification](Work_Management.md#delivered-optional-native-enqueue-prerequisite---2026-10-12).
Optional queued task metadata is projected in the same atomic work read, not a history scan or new source list. Missing/conflicting/foreign-generation metadata makes the picker unavailable and revokes exact choice; explicit None retains the original route. Work omissions and the complete 64 KiB failure remain truthful, without invented rows.

**Sessions > selected expired queue entry > Review expired removal and NEW tail task** shows the original retained history/UTC deadline and separate host-issued fresh IDs, same bounded profile/exact prerequisite and future pending lifetime/deadline.
Separate confirmation commits one atomic removal/new tail decision; cancel changes nothing. Stale selection/private closure clears the draft and late callbacks never retarget another session.
No replay, dependency retarget, auto-resume/dispatch or installed-native qualification is implied ([contract](Work_Management.md#delivered-reviewed-expired-pending-removal-and-fresh-tail-decision---2026-10-12)).

The [reviewed selected pending cancellation](Work_Management.md#delivered-atomic-selected-pending-cancellation---2026-10-12) uses an independent, explicitly empty-by-default current-run pending-only multi-selection. It cannot select question waits, running tasks, terminal receipts or another session.

Review displays the complete ordered count/IDs/revisions/profiles/original deadlines; separate **Cancel these pending entries only** confirms one atomic exact set. Cancel review keeps selection and draft.
Identical passive refresh preserves the draft; changed queue/control/session/selection/private authority requires fresh selection/review, never retargeting. Closure clears held IDs and late completions.

The shared host/store path retains cancelled task/history receipts and unselected order/deadlines/results. Dependents remain blocked by a cancelled prerequisite rather than disappearing. No pause/resume/dispatch, activity extension, effect termination, history deletion or installed-native/accessibility qualification is implied.

The native Sessions window places the bounded All/Active/Done session page
beside one selected exact session's authoritative work. Selection passively
reads `SessionWorkSnapshot`: one validated SQLite read transaction captures
session/generation, authority and queue revisions, stable FIFO positions,
pending count/capacity, host slot limit, deadline metadata, task observations
and exactly bound pending-question metadata. Queue records include all live
entries and up to 25 recent terminal/recovery receipts; nonqueue tasks and
pending questions are each limited to 50. Omitted counts are explicit; a
complete result exceeding 64 KiB is unavailable, never truncated as authority.
Nonterminal task receipts precede other task IDs; chronological sequence is
available only from the separate immutable history source.

Queued/current/waiting/blocked/cancelled/removed/interrupted/Unknown states
come from durable records. Eligibility reasons use the scheduler's same
`SessionQueuePolicy`, not a view-model guess. Ready is an observation, not
permission or a prediction of the next fair admission. Pending expiry is
the entry's captured lifetime (default 30 minutes); a recorded admission supplies
its captured precise active deadline (default 5 minutes, future-admission setting 1-60).
Unknown and retained terminal records keep that original deadline; legacy admissions
remain five minutes, never current defaults. Pre-dispatch question waits have their own expiry and no scheduler
slot/active clock; their cards remain visible while history/evidence is read.
Answer/review remains in the original separately owned Questions window.

The [content-free loaded local-event inventory](Proactive_Interaction.md#native-content-free-loaded-event-filters)
has keyboard-accessible category/reason/source/type and exact request-origin/
queue-eligibility-reason combos. Origin offers Any and every defined origin;
queue reason distinctly offers Any, No queue reason / not applicable, and
every exact eligibility reason. All six predicates are AND. Edits do not read:
they recompute loaded rows and retire exact selection/details and late callbacks.
Zero matches is truthful; a question/maintenance null queue reason confers no
success, eligibility, question-answering or maintenance-navigation authority.

Native Inspect, eligible Cancel, Remove pending ID, confirmed pending Clear,
six closed profile Enqueue buttons and manual fair Dispatch use the already admitted exact
command/host workflows. Displayed IDs/generations/revisions and the captured
selection epoch bind controls; stale records and session/selection/privacy
changes refuse. Admitted work cannot be cancelled by pretending its effect
terminated. Done/disposition/redacted citations and independent Perpetual
grants retain their existing gates.

The descriptor combo exposes only the current six catalogue IDs and exact schema/input/output/availability metadata. `capabilities.get` has no implicit target and clears its choice after enqueue or staleness; the recorded `local.inference` status button has no runtime-ID input. Both inherit the native prerequisite revision guard. Catalogue availability is not live provider/inference qualification.

Queue rows and targeted notices identify their fixed capability profile and closed target. Successful first-page/single-record data
is excluded from passive Work/AllWork/events; **Inspect selected work** reads
the exact private retained result without rerunning it. The
[fixed-profile contract](Work_Management.md#delivered-fixed-first-page-read-profiles---2026-10-12)
owns bounds, format compatibility and unchanged enablement/qualification gates.

An owned five-second passive refresh updates selected work only while idle;
it never disables/focuses controls or activates another window. Busy/changed
selection drops late observations. Refresh preserves selection by exact ID,
does not silently renew inspected question-cancellation authority, and stops/
detaches on close. Missing/disposed/corrupt/oversized authority is an explicit
unavailable recovery state. Browsing/filtering/history/refresh never records
meaningful activity, dispatches, reprioritizes, resumes, retargets voice or
questions, consumes grants or creates execution authority.

Immutable detail/script references, volatile file/skill/clipboard inspections,
captions, history and diagnostic/evidence sources remain distinct; none is a
queue payload. There is no composer, model management, Ask Evidence, export,
rich browser, new worker/effect/provider or full R14/A4 acceptance. Maintained
headless focus/accessible-name checks are not installed screen-reader/DPI proof.

### Delivered Retained Session Attachment Catalogue - 2026-10-11

**Tray > Retained session attachments** is a separate native metadata-only window, not the current-question recovery surface, All Work overview, model context or a conversation. Opening reads nothing. Refresh/Next/Cancel returns at most sixteen content-free summaries / 64 KiB, scanning sixty-four slots; optional removal provenance is independent of the sixteen retained-body cap.

Native default Browse and explicit canonical **ExactId** share the same bounded validator/scan, with Search/Enter, Next/Cancel and memory-only Clear. ID/mode/removal-filter edits retire selection, results, continuation and pending work; private/source closure erases the volatile query and fences late disclosure.
No keystroke reads, auto-selection/navigation, name/path/body search or query persistence exists. [Canonical interaction and recovery](../docs/windows-and-tray.md#retained-session-attachments-metadata-only) owns the user instructions; filtering grants no session authority or activity renewal.

Exact IDs, current stored lifecycle, capture/storage generations, source/revision/item/digest headers, byte count/type/captured UTC and copy-certification status are displayed without filenames, original paths or source text. Confirmed certification is not cheap body-integrity verification. Pending authority validation and empty filtered continuation remain explicit; changed authority/store/profile/query and two-minute expiry require Refresh.

Selecting metadata grants nothing. **Open exact session/attachment** uses fresh existing private workspace and full stored-byte inspection, refusing stale/replaced/removed/held references rather than opening newer content. Current Done inspection stays Done and never resumes, dispatches, renews meaningful activity, triggers cleanup or retargets a current question.

Publisher/inspection revocation, lifecycle/disposition/retention, query/selection/private control, cancellation/close/disposal clear page and exact navigation immediately; asynchronous callbacks remain bound to the original owned window and selected reference. Existing native file reviews/controls must be quiescent before navigation. Full inert source/citation lifetime remains owned by the existing attachment controller.

[Canonical limits and evidence](File_And_Folder_Ingestion.md#delivered-retained-attachment-metadata-catalogue---2026-10-11) qualify this consumer only; no managed knowledge, new source consent, body/capture/copy mutation, schema/index/cache, model/network or installed/native acceptance is claimed.

### Delivered Bounded Passive All Work / Attention Overview - 2026-10-11

**Tray > Sessions > All work / attention overview** opens a separate owned native read-only window. Opening performs no reads or grants; manual Refresh/Next/Cancel scans at most ten canonical session IDs per click through admitted metadata and reads independent current `SessionWorkSnapshot` sources, plus bounded metadata lookahead.

All/Active/Done is stored lifecycle; **Observed needs answer** filters observed pending-question metadata, not reply eligibility.

The native **Observation availability** combo offers exactly **Any**, **Observed**, **Source changed**, **Read unavailable**, **Output bound**, composed by AND with lifecycle, literal metadata query and Needs answer. Matching is content-free, exact typed availability under the [portable policy](../src/Kora.Core/Storage/SessionWorkOverviewFilter.cs), never an empty-work predicate. Undefined values are refused. Editing reads nothing and revokes current rows, selection, continuation and late callbacks immediately; explicit Search/Refresh/Next binds the exact choice with existing private/source/query/head fences. The keyboard-focusable native combo has an accessible name explaining its choices and no-read/unavailable boundary, remains editable during reads, and is disabled by private closure. Choices are volatile, clear on close and add no persistence, telemetry, authority or export route. Zero matches remain a truthful bounded scan, not all-clear.

One complete typed projection is limited to 64 KiB. It retains exact session IDs/generations/metadata revisions, per-session authority/queue revisions and observation time, confirmed fixed-profile slot/capacity limits, observed current fixed reads, all returned policy reasons/queue deadlines and up to fifty task/question records per session.

Queue receipt, task and question omissions remain explicit; full bodies/history/attachments/memory/diagnostic strings are excluded.

The collected page is **not globally atomic** and not provider/resource coordination. No global live/Unknown count, full inventory, unread tracking, ETA or all-clear is inferred.

Next consumes only scanned IDs, including nonmatches; a byte-deferred group is retried, and a single oversized group is explicitly OutputBound without silently truncating task details. Unavailable or metadata/work generation/lifecycle/revision mismatch groups contain no previous private work.

Continuations bind host, private admission, source epoch and filters. A ten-second total storage timeout or cancellation rejects the whole partial read. Confirmed policy/call/private ownership and retained-source admission run before/after reads; lifecycle/disposition/retention changes revoke the page and in-flight callbacks.

No browse-triggered cleanup, background heartbeat, input-disabling refresh or automatic collection is introduced. Close/privacy/dispose cancels reads and clears rows, filters, exact IDs and continuation; late callbacks cannot reopen.

**Open exact session with fresh passive read** resolves that immutable ID through the same private workspace service, rereads metadata/work/questions/tasks/retention, and publishes only while the source epoch remains admitted.

It never resumes, dispatches, enqueues, reprioritizes, cancels, consumes grants, renews activity, changes voice/question targets or renews inspected task cancellation authority. Native question actions remain separate.

[Portable bounds/race tests](../tests/Kora.Application.UnitTests/Hosting/SessionWorkspaceServiceTests.WorkOverview.cs) and [multiple real private SQLite sessions](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionQueueTests.WorkOverview.cs) evidence bounded sources.

[Headless native focus/scroll/navigation](../tests/Kora.Windows.IntegrationTests/AllWorkWindowTests.cs) is not installed accessibility/DPI, full conversation/composer, provider or general work-execution proof.

### Delivered Native Exact-Session Retention Controls - 2026-10-10

When due-content inspection is held, the existing canonical exact-ID field and **Read exact ID retention** provide metadata-only status/review without reading names/history/work or creating implicit conversation/resume authority. Removed/purged observations are nonmutable.

The selected-session **Retention: status and exact review** panel now shows actual UTC activity/due dates and kept/purged state. **Refresh retention status** is passive. **Keep this session** / **Use ordinary retention** first stage an exact ID/generation/state/audit-revision preview; **Confirm exact retention review** is a separate native action. Cancel, selection changes and close discard it.

Ordinary policy uses the displayed original due dates, never resets inactivity, and warns about soon/already-due background archive/delete. The session hold is explicitly not an operation grant, consent, memory, recall or execution. Observed work holds and unobserved copy/maintenance availability are distinguished. Stale/removed/purged, ownership/privacy/call changes and uncertain audit/commit/readback require fresh durable inspection, not blind checkbox-save or false rollback.

Browsing no longer invokes cleanup. Confirmed policy, copy inventory, due-candidate holds and source revisions remain read-time fail-closed checks; eligible due ordinary content waits for private background maintenance. See the [retention contract](Interaction_And_Sessions.md#r12-native-exact-session-retention-controls---2026-10-10) and [native/headless tests](../tests/Kora.Windows.IntegrationTests/SessionsRetentionViewModelTests.cs). No model, typed/voice mutation, apply-now or installed-native qualification is added.

### Delivered Passive Session-List Metadata Navigation - 2026-10-10

The Sessions window now has a separate metadata query and explicit **NameSubstring** / **ExactId** modes. **Search session metadata**, **Next metadata search**, **Cancel** and **Clear** navigate across the persisted list, not just its currently loaded page. Enter searches; Escape clears only the focused query. This is not the adjacent exact-session lexical history search or Evidence filtering.

NameSubstring is literal ordinal, case-sensitive NFC name containment, with the existing [SessionName limits](../src/Kora.Core/Hosting/SessionName.cs). ExactId requires a nonempty canonical lowercase D GUID and uses the existing exact metadata lookup.
Unknown/Removed IDs are unavailable, not a fallback inventory. All/Active/Done applies across the metadata scan; outside search it retains ordinary page-only filtering.

Each name-search click consumes at most 50 validated metadata rows plus the reader's one-row lookahead, returns at most 25 native matches and bounds complete serialized output to 64 KiB. Zero matches can still have Next.
Scanned/unnamed/nonmatching counts, result/byte limits and current end are explicit. A matching row deferred by an output limit is reconsidered on Next; no matching name or record is truncated or silently omitted.

This is mutable canonical-ID keyset navigation, not a sequence-bounded history snapshot. Continuations bind exact query, mode, state filter, host/store lifetime and private admission revision. Renames, lifecycle/deletion and additions behind the cursor require a fresh Search.
New later IDs may appear; an observed end is not a historical inventory claim. No schema, index, snapshot cache or new ownership authority is introduced.

Results retain actual names, immutable IDs, lifecycle and optimistic generation/metadata revisions, including explicitly unnamed records. Selection is preserved only when its exact visible record remains unchanged.
Query/source/selection/privacy changes, cancellation and closure suppress late results; disappearing or changed selected metadata clears stale work/detail/question-cancellation/memory presentation. Names never resolve actions, grants, model or voice targets.

[Native controls](../src/Kora/SessionsViewModel.ListSearch.cs), [passive workflow](../src/Kora.Application/Hosting/SessionWorkspaceService.ListSearch.cs), [private SQLite tests](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionListSearchTests.cs) and [headless bindings/preservation](../tests/Kora.Windows.IntegrationTests/SessionsListSearchTests.cs) evidence this slice.
Existing work refresh and memory proposal controls remain independent. Composer, model history, providers and installed-native qualification remain open.

### Delivered Passive Details Boundary

The [passive lexical increment](Interaction_And_Sessions.md#delivered-passive-exact-session-lexical-history-search---2026-10-09) adds exact-session query, Search/Next/Cancel/Clear controls beside the existing history ID. Enter searches; Escape clears within the focused field. Search results reuse the immutable receipt list and fresh detail viewer without changing selected work or pending-question identities. Complete 64 KiB results and explicit scanned/gap/omitted counts distinguish an empty bounded page from an exhausted snapshot ([controls](../src/Kora/SessionsWindow.axaml), [headless recovery](../tests/Kora.Windows.IntegrationTests/SessionsHistorySearchTests.cs)).

The [selected-session receipt increment](Information_Display.md#delivered-immutable-selected-session-receipt-details---2026-10-09)
now composes this same viewer with the Sessions history page. Select a listed
receipt and explicitly open its immutable details; the exact history subject
must match the selected session. Fresh resolution uses authoritative retained
records only. The list shows sequence/kind/availability/exact event ID; native
chrome and read-only source expose receipt provenance without replacing work,
pending questions or their separately bound controls. Private copy retains
explicit disclosure confirmation. Owned session details retire before native
logical disposition and inventoried retention deletion. General artifact/
response/script bodies remain unavailable, not substituted from volatile
file/skill/lexical sources. No new rendering or persistence pipeline exists.

Documentation now offers **Open details** for the explicitly selected embedded
page. The separate native window is bound to one host-admitted immutable
item/revision/digest, with trusted provenance and sensitivity outside its
content viewport. Rendered/Source, native search and exact-source Unicode
copy are passive controls. Reopening activates the exact reference's window;
a new revision never silently refreshes an existing viewer. At most eight
viewers are open.

The [delivered profile](Information_Display.md#delivered-native-profile---2026-10-06)
owns byte/block/node/nesting bounds and unsupported/error fallback.
The guide and grant document reuse that native pipeline. These viewers inherit
the shared theme, introduce no animation/topmost preference, and clear their
private content/selection on privacy closure. Closing one only releases its
presentation; it never delegates to response Dismiss, task cancellation,
session Done/delete or approval. Reading/search/copy do not call meaningful
session-activity notification.

Production entries are **Documentation > Open details** and the exact
selected-session history receipt route above, not compact response History.
The documentation page's process-local identity must not be portrayed as a
durable conversation. The typed finalized-response handoff exists, but its
durable resolver, native response dispatch, shared question/offer targeting and
general artifact integration remain separately owned and uncomposed. Rich clipboard,
HTML, diagrams, source/diff language highlighting and export are not delivered.
Native visual/accessibility/DPI/multimonitor observations remain pending
separate approval.

## Window and Surface Map

### Delivered Bounded Question Window

**Tray > Return to current question** restores the dismissed original current-run local-version window only while its host-owned original wait remains live. Close/Escape clears native controls, unsaved input and review, but does not submit, cancel or end that wait. The committed draft/current revision may return; there is no affirmative default.

Already-open exact presentation activates, independently of Sessions selection and voice/approval focus.

The original five-minute expiry continues without a window; return extends neither it nor meaningful activity. Exact task/wait/key/generation, live ownership/privacy and audited review must still agree. Expiry, cancellation, completion/Unknown, changed authority, storage/audit failure, closure or disposal removes eligibility and releases the host await/timers.

After restart, retained metadata is read-only **Unavailable** recovery with a fresh-request instruction, never old-task rehydration. No general question/approval composer or installed-native qualification is claimed.

The [synchronized terminal boundary](Interaction_Fallback.md#synchronized-native-terminal-publication---2026-10-12) keeps committed cancellation and original completion coherent without holding notifications under a lock. Later withdrawal still clears private controls after a receipt; late restore cannot undo withdrawal or a newer dismissal. Existing five-minute expiry, exact approval review and pre-dispatch privacy/source checks are unchanged.

Tray **Review local version (native question)** opens one owned, non-topmost
window for the original durable local-version request. Opening another while
it is pending activates that same target, not the foreground session. The
window has explicit identity/generation/revision/expiry and purpose/source
chrome, labelled radio/check/text inputs, visible bounds, status/recovery, and
separate Review exact record, Save draft, Submit, Cancel question and Close.
Native tab order, labelled controls, polite status announcements and Escape
closure are contract-tested; actual assistive-technology acceptance is open.
Single/multiple/text use the shared component, but only the harmless
single-choice version query is a shipped entry point in this slice.

Exact-record review reuses the bounded native plain-text renderer, with no
browser, resources, active content or automatic clipboard paths. Approval
chrome stays outside that reader. A rendered review must match the complete
host snapshot before approval becomes enabled. Reading and activation do not
approve, consume a grant, execute, mark Done or supply session authority.
Committed draft revisions invalidate previous review; revisiting or answering
a stale target disables it rather than silently following a newer revision.
Privacy/ownership loss clears private controls; timer/events are detached on
close. Presenter closure and durable question cancellation remain distinct.
The [interaction boundary](Interaction_Fallback.md#delivered-bounded-native-question---2026-10-07)
owns the exact service checks, audit outcomes and no-replay recovery limits.

These are UI roles, not a requirement to open every window or one window for every running session.
Shared native question/approval components can be hosted in compact or workspace views without creating separate copies of the underlying interaction.

| Surface | Primary content and controls | Relationship to session/work |
|---|---|---|
| Tray and ambient Presence | Actual listening state, aggregate activity/attention, Show Kora, Sessions, Questions, mute/stop, Settings, Exit | Always reachable without model/audio; Presence is optional presentation, not the work registry |
| Compact session interaction | Selected session identity/state, concise latest interaction, pending native cards, Details/History, session switcher, typed composer | One lightweight selected-session view; showing/hiding/pinning never stops work or marks Done |
| Sessions workspace | Active/Done session list and search beside full selected conversation; per-session work/queue, questions, history, Evidence mode, and management controls | Primary multi-session and retained-evidence UI; responsive while independent sessions run |
| Detail/artifact viewer | Exact answer, Markdown/static HTML, citations, script/diff source, provenance/revision; search/copy/export/source controls | Bound to an immutable session/artifact reference; stays on that item when workspace selection changes |
| Skills management and review | Source-qualified skill catalogue, metadata/capabilities/dependencies, immutable revision/file tree, rendered instructions and exact source/diff tabs, enablement state | Purpose-built management surface; file content reuses passive detail renderers, while save/enable/test/revision actions stay native and revision-bound |
| Instruction management and editing | Source-qualified Kora/profile instruction catalogue, metadata, rendered content, exact source/diff, dirty/conflict state, voice/typed refinement, Save and applicability review | Reuses the skill authoring workspace without its package tree, fixtures, dependency tests, or skill capability-enablement controls; profile writes remain exact-file, identity/base-digest-bound native operations and changed guidance is not automatically applied |
| Permissions and Approvals | Native grant inventory by scope/applicability; inspect, request/edit/remove, linked use evidence | Global inventory with exact session/operation references; not deleted with a conversation's perpetual grants |
| Settings and setup/recovery | Typed preferences, output/call modes, retention/concurrency, devices/providers/readiness, consent and recovery | Shared application state; optional session scope is explicit; usable without voice/model |
| Documentation | Trusted embedded guide, help, feature availability | Existing guide stays distinct from generated conversation/artifact content |
| Optional speech text / isolated web viewer | Current eligible speech caption or explicitly requested supported web content | Independent adjuncts; neither replaces compact/workspace nor owns approvals |

Use existing single-instance settings/guide behavior and activate an existing surface rather than opening duplicate copies.
The default conversation experience has one Sessions workspace and one compact view sharing selection.
Opening a retained detail activates the viewer for that reference; multiple explicit detail views may compare items, with distinct session/item titles and no composer or approval authority.
Do not open a new chat window automatically for each concurrent task.

Share passive content presenters, provenance headers, source tabs, search, copy,
and accessible renderer status across detail, documentation, skill review, and
evidence detail. Do not force their surrounding workflows into one generic
window: Sessions owns routing/lifecycle/queues, Skills owns revision/file-set
selection and save/enable state, and Permissions owns grant validation and
mutation. Rendered Markdown/HTML can explain those records but cannot provide
their authoritative buttons, forms, selection, or dirty state.

The delivered owned Instruction/Prompt editor adds per-kind metadata search and observed-use filters beside its existing native controls, not a new window or the planned full source/file-tree surface.
List/Refresh reconciles the existing bounded source pipeline; query/filter/Next only projects its admitted immutable owned metadata snapshot. Complete labels/details are bounded to 64 KiB UTF-8 with explicit omissions and Next when needed.
Selection alone never opens another file, grants use or retargets an unsaved draft. Inspect revalidates the original kind/filename/digest/native revision and catalogue/private epoch. Stale choices require Refresh/reselection; failed reload clears availability, while the original/draft remains subject to existing privacy guards.
Private closure clears query/rows/selection/editor text and suppresses late publication. Keyboard-native controls and current exact List/Inspect commands share the editor; broader profile editing, source filters and accessibility/installed qualification remain deferred. See [owned inventory controls](../docs/instruction-editor.md#search-observed-owned-metadata).

The owned Instruction/Prompt editor's explicit **Compare** opens only a Close-only passive native viewer: frozen inspected baseline beside exact current memory draft, or labelled missing/new baseline. Raw source and separately labelled prepared-Save validation are distinct. Identity/input/source/private changes withdraw and clear it; it has no mutation, consent, copy/export or use-review authority. See [Compare source presentation](../docs/instruction-editor.md#compare-the-open-draft-without-saving); this is not the planned full profile/diff workspace or installed/accessibility qualification.

## Compact Session Interaction

### Delivered compact selected-session projection

The explicit [native compact surface](../src/Kora/CompactSessionWindow.axaml) shares one selected session and memory composer with Sessions.
No target is chosen on open. Explicit Read/Next uses admitted conversation pages; stored USER text and typed HOST outcomes never become assistant output. Metadata catalogue navigation still imports no body.

The [shared presentation owner](../src/Kora/SessionsPresentationOwner.cs) preserves the surviving view, serializes dirty decisions, and releases timers/events only when the presentation is revoked or its last view closes.
Last dirty close and explicit switching use Save/Discard/Cancel with the original target and frozen buffer; stale or failed Save cannot close successfully. Private/source retirement clears both views and fences late callbacks.

The [shared controller](../src/Kora/SessionsWindowController.cs) keeps single-instance activation and exact navigation. Workspace provider/routine-quiet visibility remains workspace-owned.
Standalone question/approval/voice and legacy response presenters are unchanged. No per-task automatic popup, generation, relatedness, global/voice auto-routing, model context or new storage representation is added.

Maintained [shared-view tests](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.Compact.cs) and [controller tests](../tests/Kora.Windows.IntegrationTests/CompactSessionControllerTests.cs) cover this bounded projection, not full coordinated R14, installed accessibility, provider/hardware, physical-storage or STOP qualification.

Illustrative layout, not pixel dimensions or final styling:

```text
+-------------------------------------------------------+
| Kora | Session: Deployment notes v | Running | Sessions |
| Voice: listening          2 running / 1 needs attention |
|-------------------------------------------------------|
| Latest interaction / concise summary                  |
| "The draft is ready; the full result includes a table." |
| [Open details] [History]  Task: Prepare release notes   |
|-------------------------------------------------------|
| Question 1 of 2 - Choose destinations                 |
| [x] Team wiki     [ ] Email     [x] Release page        |
| [Add another answer...]                                |
| [Review answer] [Submit] [Cancel question]              |
|-------------------------------------------------------|
| Message Deployment notes...                   [Send]   |
| [New session] [Switch] [Work status] [Stop speaking]    |
+-------------------------------------------------------+
```

The session picker lists a bounded recent/active set plus All sessions; it is not an opaque task queue.
The checkmarks above illustrate a user-edited draft, not preselected consequential answers.
The header clearly distinguishes lifecycle, actual work state, listening/output state, and aggregate attention.
The latest response is concise, with retained full content behind a native Open
details action when the host presentation policy recommends detail; do not
stuff the entire transcript into this window. The action identifies and opens
the exact immutable item/revision, not whichever response is newest when it is
clicked. In a hands-free request, the same item can own one host question
asking whether to open it, without turning generated content into a control.
Question/approval cards have a dedicated interaction region that ordinary streaming/progress cannot overwrite.
Multiple pending cards expose count and explicit Next/Previous or a list, retaining each draft separately.
No presented approval or edited question disappears under the ordinary feedback auto-hide timer.
Unanswered/unseen items remain accessible through the workspace/attention entry even if the user explicitly hides the compact window.
The composer always names its session; New session is separate from sending to the selected session.
An explicit New request entry from tray/compact/workspace offers an untargeted composer labeled Auto-route; show the accepted new/related destination before presenting it as a session message.
The delivered [native New request fallback](Interaction_And_Sessions.md#delivered-native-new-request-fresh-session-fallback--2026-10-13) is narrower: tray and Sessions open/activate one isolated memory composer, explicitly disclose unavailable relatedness inference and offer only fresh **New conversation**.
Audited empty-target creation is acknowledged with its exact ID/name/generation/metadata revision before separately reviewing/submitting the frozen original body; these are not an atomic transaction.
The same confirmed created Active target permits a distinct revision-reviewed draft Save: show exact saved revision, confirmed memory baseline and complete old/new bodies in inert native text, then explicitly confirm the frozen proposal. Save and Submit reviews cannot authorize each other. Concurrent named-window revision/metadata changes fail stale rather than follow/overwrite newer data.
Eligible dirty close offers Save/Discard/Cancel and a separate frozen Save confirmation; only an exact receipt without newer edits/revocation permits closing. Before confirmed creation or while held, only Discard/Cancel is offered. No autosave/Reload or implicit text transfer is added. Privacy closes without saving. Cancel leaves the disclosed session and saved data; uncertainty retains exact holds without retry.
Explicit exact-ID navigation protects the existing named buffer; saved-draft catalogue discovery and explicit Sessions Reload reuse their existing authority. Memory buffers remain isolated even when viewing the same saved target. Compact/voice Auto-route, relatedness matching and production generation remain unavailable.
Explicit New session bypasses relatedness matching; a named-session composer never silently auto-routes elsewhere.
Unsent text and answer drafts belong to their session/question, not one global text box; switching away preserves them and switching back restores them.
Clear the composer only after the host has durably accepted the input; persistence/admission failure leaves the draft recoverable with an error.
Permitted non-secret drafts use the private-profile session/draft store; draft saving is not submission, approval, queue dispatch or a synthetic retention heartbeat. Copies outside that boundary are readable.

## Sessions Workspace

```text
+-------------------------+----------------------------------------+
| Sessions       [+ New]  | Deployment notes | Active | Running    |
| [Search sessions...]    | [Rename] [Done] [Delete...] [Details]   |
| Active | Done | All     | Voice answer target: named question    |
|-------------------------+----------------------------------------|
| > Deployment notes      | Full conversation and event timeline   |
|   Running | 1 question  | You: Prepare a release-note draft...   |
|   Repository cleanup    | Kora: Plan and source references...    |
|   Running | 2 updates   | Task: source read completed [Evidence] |
|   Budget analysis       | Kora: concise result [Expand]          |
|   Waiting for approval  | Question / approval card with controls |
|   Old investigation     | Accepted answer / action receipt...    |
|   Done | deletion due   |----------------------------------------|
|-------------------------| Session work: running task + queue     |
| 2/2 execution slots     | [Status] [Pause dispatch] [Cancel...]  |
| [All work] [Questions]  |----------------------------------------|
| [Settings] [Grants]     | Message Deployment notes...     [Send] |
+-------------------------+----------------------------------------+
```

### Session List

Show stable identity through accessible title/ID references; titles are editable and may collide without merging sessions.
Rows show Active/Done separately from queued/running/waiting/blocked/failed/interrupted/unknown work.
Include unread/needs-answer counts, last meaningful activity, and archive/deletion due dates in accessible row details.
Filters distinguish Active, Done, Needs attention, and work states; searches cover permitted metadata/history with evidence links.
Search/browsing/selection never refresh inactivity or resume Done sessions.
Sort/filter must not reorder authoritative per-session task queues or make row position an action target.
Do not rely on color/animation alone; expose state text and readable blocker reasons.
Basic list/new/select/Done/resume/delete/status controls do not require a model, internet, or working microphone.

### Conversation Pane

Show ordered user/assistant exchanges, structured questions, accepted answers, plans, artifacts, approval decisions, and observed actions/results.
Collapsible task/event groups reduce noise but cannot replace or discard full evidence.
Streaming responses are visibly provisional until finalized; failed/interrupted streams are retained with honest status.
New events follow the tail only while the user is already following it; historical browsing preserves position and shows a New updates indicator.
Keep pending interactions reachable via a dedicated attention region even when their chronological entries are off screen.
History approval/question entries show past outcome in read-only form, not live replayable buttons.
A current native card uses the host's current ID/revision; historical copies link to that current item only if it still exists and remains actionable.
Done-session conversation is readable without resume. Sending new work requires explicit Resume or New session; no silent lifecycle transition.

### Evidence Mode

The delivered [bounded native inspector](Information_Display.md#delivered-bounded-native-evidence-inspection)
has an optional exact **Span ID** field beside **Trace ID**. Empty means Any;
nonempty means exactly 16 lowercase hexadecimal characters, not all zero,
under the existing portable W3C policy. All existing filters compose with AND.
Edits revoke the current page/selection/continuation without reading.
Clear advanced preserves both basic W3C fields; emptying either unsets it,
and close clears both. Span ID is correlation only, not session or audit
authority. This adds no ParentSpanId predicate, navigation, export or model
route; the broader mode below remains a target contract, not qualification.

Evidence mode provides a bounded local timeline/search over permitted session,
diagnostic and security-audit records. It supports time range, source kind,
severity, event/category, correlation, session, task, invocation, approval,
action/outcome and safe-text filters. Each result shows source kind, timestamp,
stable event identity, relevant correlation links and whether it is an
authoritative audit/receipt, an ordinary diagnostic observation, or retained
session content. Selecting a result opens its session/history or immutable
detail without changing session lifecycle or executing anything.

The mode has **Logs**, **Audit**, and **All Evidence** source views. Each view
supports deterministic paginated List, Read and Search without a model:

- Logs lists/searches `application_log_events` and can open the corresponding
  retained daily-file record or range when available.
- Audit lists/searches typed `security_audit_events` and reads one immutable
  audit record with its request/terminal, approval, invocation and target links.
- All Evidence correlates permitted session, log and audit records by stable
  IDs and time while preserving their source/authority labels.

Log and Audit details show the event ID/name, level, category, original message
template, typed property names/values and scopes in addition to any rendered
message. Typed filters operate on those fields and preserve value kinds;
rendered text is clearly a display/search projection and is never used to infer
an audit outcome or reconstruct missing structure.

Every record exposes **View trace** and, when session-bound, **View session
evidence**. View trace presents the W3C parent/child tree plus explicit linked
activities, interleaving logs and audits by observation time while retaining
their source/authority labels. View session evidence applies the stable
host-owned session ID and shows Logs, Audit, or All Evidence across every trace
for that session. The selected session's History/Evidence action opens the same
filter directly. A session can have many traces; trace selection never limits
the session to one request.

If the session still exists, evidence links back to its conversation/task
timeline. If session content has expired or was deleted, independently retained
audit rows show an opaque deleted-session reference without a recovered title
or content. Expired diagnostic spans/logs appear as explicit gaps while retained
audit relationships remain navigable by trace, correlation and session ID.

An explicit **Ask Evidence** action accepts the current filters, selected
records, a bounded time range and the user's question. Before reasoning, show
the sources and estimated bounded payload; remote processing additionally uses
the normal exact egress preview/approval. The answer cites every supporting
record, separates observed facts from inference, identifies conflicting or
missing evidence, and links back to each source row. Follow-up questions retain
the evidence selection/revision, not an invisible unbounded database context.
Expired/deleted sources remain visibly unavailable rather than being replaced
with an uncited cached assertion.

Cross-source grouping may explain a sequence such as request, policy decision,
dispatch, diagnostic failure and observed receipt, but the UI must not upgrade
a diagnostic message into proof of authorization or success. Search shows
retention boundaries, redactions, unavailable sources and known ingestion gaps.
Log/audit text and rendered messages are untrusted historical data, never
instructions, approvals or tool arguments. Ask Evidence is read-only and cannot
execute, retry, approve, revoke or alter retention. It remains usable without a
model for list/read/search; unavailable reasoning reports that limitation
without hiding the matching records. Sending selected evidence to a remote
provider or exporting it requires the normal explicit preview and
destination/egress controls.

### Work and Queue Pane

Show the selected session's executing task, known planned steps, observed receipts, pending tasks, dependencies, and resource/provider/user blockers.
Reorder/remove/replace actions identify exact queued tasks and preview their scope; Cancel is not session Delete.
Pause dispatch prevents new work, not a fictitious pause of an uncancellable remote operation.
The delivered bounded passive All work view above groups observed tasks by session and shows confirmed fixed-profile slots, active/ready/blocked/Unknown reasons and omissions. General cross-session resource/provider conflict coordination remains proposed, not inferred from collected pages.
Controls default to the named session/task; Stop all work is a separate explicit all-session operation.
Sending another request to a busy session shows the management decision and target task/queue position; native Queue/Replace/Clarify choices remain available without inference. Replacement still resolves cancellation/unknown effects before dispatch.
Selecting a session never changes scheduler priority, cancels another task, or borrows its provider/approval context.
Do not invent remaining steps, completion percentages, or an ETA when the runtime exposes only stage observations.

## Detail, History, and Script Review

```text
+----------------------------------------------------------+
| Deployment notes / release.ps1 / revision and provenance  |
| [Back] [Rendered | Source] [Copy all] [Export]            |
|----------------------------------------------------------|
| Full answer, Markdown, static HTML or exact script/diff   |
| source; citations and immutable artifact identity         |
| Search within this item; accessible scroll/zoom           |
|----------------------------------------------------------|
| Referenced by: task / event / approval proposal           |
| Execution and approval are separate trusted workflows    |
+----------------------------------------------------------+
```

History lives in the workspace; this viewer expands a particular retained item or task evidence bundle.
Header/chrome carries session, item type, origin, revision/digest, and unavailable/redacted-source explanations.
Markdown/HTML rendering follows the bounded safe profiles. Scripts, code
fences, manifests, configuration, and diffs use the shared language-aware
highlighter when their host-resolved language is supported, with visible
language status and readable exact-source fallback otherwise.
Opening, expanding, copying, scrolling, or closing a script never runs it or approves it.
Copy all targets the complete current immutable answer/artifact or selected
skill file, including content outside the viewport. Rendered views place their
sanitized rich fragment and semantic Unicode plain-text fallback on the
clipboard together; admitted Markdown can also include its registered source
format. Source views always copy original admitted text and may additionally
provide host-generated safe syntax/diff formatting.
Partial selection supports native `Ctrl+C` and Copy selection across rendered
blocks and virtualized source lines. Copy never includes native chrome, line
numbers, syntax tokens, search decoration, or hidden adjacent files.
Review script from a current approval opens the exact reviewed revision; changed source invalidates the proposal rather than silently refreshing execution authority.
For bundled skills, expose every manifest-listed file through named read-only
tabs, including a separate exact-source tab for each entry script/shared helper.
Show definition/script-set identity, tracked dependencies and unresolved
transitive references alongside the approval. Explain best-effort tracking and
the granting user's responsibility; tab selection/display does not approve code
or imply that every runtime dependency was discovered.
A global selection change leaves an explicitly opened item attached to its original session; Back to conversation explicitly selects/navigates to that session/event.
Export is an explicit scoped write with preview; retention/deletion does not claim to remove user exports.
If a source/session is deleted or access is revoked while open, show unavailable and clear denied content under the store/access contract; no invisible stale copy remains model-readable.
Missing renderers disclose source fallback/unavailable capability without losing basic text or installing dependencies automatically.

## Skills and Permission Management

The proposed native **Definitions** surface manages skills, prompts,
instructions, and agent profiles from bundled, Kora-owned, and registered
profile sources. Skills remains a focused entry point. The complete control,
editing, applicability, and reload contract is
[Definition Management](Definition_Management.md#one-management-surface-equal-ui-and-voice-control);
every supported native action has a typed/activated-voice equivalent over the
same host service. Unqualified agent Run controls disclose unavailability.

The bounded delivered surface is one native Kora-owned instruction/versioned
prompt editor with an explicit kind selector, exact source/diff review,
Save/Delete, Reload/Overwrite/Cancel and dirty closing/shutdown Save/Discard/Cancel.
Typed/current-name activated `instructions` and `prompts` controls focus the
same native actions. Review only permits exact current-run selection for a new
request; prompts are user-request content, not system guidance or execution authority. Owned prompt v2 adds only bounded required literal `${input:name}` native fields and whole expanded USER REQUEST preview, followed by separate fresh Use admission.
Editing a field or source/review/input/call/private change retires old previews; close/cancel clears ephemeral values without history/autofill/export/speech or template autosave. Production generation remains unavailable; no rejected-provider fallback is used. Native v1 remains literal. Profile/skill/agent editing and the full Definitions
surface remain separately gated. See [native editor](../docs/instruction-editor.md).
**Validate** / exact `instructions validate` or `prompts validate` observes only the open same-kind supported owned draft, using the ordinary Save preparation including BOM/marker/header limits. It does not open/switch/discard another source.
The text-only body-free summary is historical structural compatibility, never approval, harmlessness or enablement. Source/name/kind/baseline/catalogue/private/input changes retire findings and suppress late results. Validation retains draft/dirty/inventory/use/uncertain-hold state without source I/O, reload or preference writes; Save separately re-prepares and exactly confirms.
**Disable reviewed use** and exact `instructions` / `prompts` `disable` or `stop-use` focus one separate native confirmation for the clean inspected supported native owned source/kind/digest/revision (instruction v1; prompt v1/v2).
The durable hold withdraws all new selections without writing/deleting the source, preserves inspection/editing and already captured reasoning, and retires pending unadmitted choices without replay. Dirty drafts remain intact with an explicit Save/Reload prerequisite.
Cancellation/stale review and failed audit/preference readback never invent completion or restore enablement. No-change reload/restart/restored bytes cannot re-enable use; fresh exact review is required. Profile-original editing and installed/accessibility qualification stay open.

The owned editor's native **Exact SHA-256** field narrows observed artifact
metadata for both owned kinds, ANDed with literal metadata query and use state.
Empty means Any; otherwise exactly 64 lowercase hexadecimal characters are
required by the shared portable artifact policy. No trimming, folding or partial
matching occurs; unknown digests match only Any. Predicate edits perform no
source/catalogue read and synchronously retire page/selection/details/continuation,
validation and pending review/use controls, preserving the open draft and fencing
late callbacks. Next binds the complete filter state and original inventory;
zero matches is truthful. Digest never supplies use authority or disk integrity,
and no additional source, persistence, export or activity renewal is introduced.

The Skills surface lists source-qualified skills and revisions without reading
arbitrary profile paths. Selecting a revision shows host-resolved identity,
origin, compatibility, enablement, requested capabilities, registered
tools/tasks, dependencies, validation findings, and immutable digest. A
host-owned file tree exposes every admitted manifest/instruction/source file;
the selected file uses the shared passive Markdown or language-highlighted
exact-source presenter. Switching files cancels stale tokenization; highlighted
spans never replace the bytes/digest bound to review.
Multi-file review always keeps complete revision/file-set identity and
unresolved dependency status visible. Selecting a file, reading instructions,
running a data-only simulation, or viewing a diff does not enable the skill,
approve execution, or grant its capabilities.
Copy all in this surface copies only the complete selected file, never a
concatenation of sibling files or the whole package. A separate future package
export must preserve file boundaries and receive its own explicit action.

Editing supported declarative definitions uses native editor/diff, validation,
dirty-state, conflict, save, and activation controls for every kind.
Bundled revisions remain immutable. Supported profile originals are directly
editable through the qualified writer; creating a Kora-owned fork is optional.
Every original-file mutation or fork is an explicit proposal, never a write
approved merely by discovery.
Save and enable are separately confirmed revision-bound operations. Closing
with unsaved changes follows an explicit save/discard/cancel workflow and never
silently publishes a draft.
Clean views disclose externally reloaded revisions; dirty editors retain the
draft/base and offer compare, explicit rebase, discard/reload, or save-copy.
UI/voice lookup and dependency status publish the same catalogue revision.
Reload cannot silently activate changed content or steal focus.

The Permissions & Approvals surface is a native filterable inventory and
editor over typed grant records. Its list and detail show scope, applicability,
identity/destination/resource, creation/edit/use history, policy revision, and
inapplicability/revocation reason. Read-only explanations and evidence may use
the passive detail presenter, but narrowing, revoke, bulk revoke, export,
scope-change, and new/broadened grant requests use native validated controls.
A displayed row, checkbox, Markdown link, or HTML element is not submission or
approval. Concurrent use/revocation and stale revisions surface a conflict and
re-resolve policy rather than applying an edit to a different grant.

## Shared Native Interaction Cards

| Card | Required UI behavior | Equivalent verbal interaction |
|---|---|---|
| Single choice / yes-no | Labeled options, optional permitted custom reply, explicit consequence/submit semantics | List/explain/select by unambiguous name or number; confirm when required |
| Multi-choice | Checkboxes, min/max selection, optional custom item, validation and explicit Submit | Select/deselect several values, review the common draft, submit/cancel |
| Typed form / clarification | Bounded labeled fields, required/type validation, accessible errors, text entry | Supply/correct fields individually, review and submit the same draft |
| Concise result / progress | Brief answer/stage, observed state, Details/History/citations | Ask for short/full explanation, evidence, or progress |
| Action approval | Exact operation/target/effects, current proposal, Once/Session/Perpetual scope where eligible, Review, approve/reject | Action-specific spoken approval/rejection under effective policy |
| Routing / session lifecycle | Exact candidates/session identity, New/Resume/Done/Delete consequence choices | Name/select/new/Done/resume/delete with equivalent scope and confirmation |
| Error / unknown outcome | Actual failure/certainty, evidence, retry/reconcile/cancel options where supported | Ask what failed or is unknown; choose the supported recovery |

Each card shows session/task/purpose and follows the canonical question/proposal revision contract.
Questions are not approvals: answering a planning question cannot grant execution.
No consequential answer is silently preselected; a checkbox edit is not submission.
Voice/UI edits publish to the same draft and validation state across visible hosts; accepted submission commits once and disables every duplicate live control.
Cancel/dismiss/expiry have distinct labels and effects; none means consent, Done, or deleting a session.
Do not replace a bounded choice/form with free text merely because the response originated from a model; preserve structured options when possible.
Generated content cannot draw authoritative controls; only native host components can submit answers or grants.
When policy ignores reusable grants during a call, explain why current approval needs Once without editing the stored Session/Perpetual record.
An in-call voice-origin settings rejection offers a fresh UI settings route, not an affirmative button that relabels the rejected request.

## Selection, Voice Focus, Notifications, and Concurrency

Workspace and compact share one selected UI session; explicit detail windows hold independent read-only item references.
UI selection, foreground voice question, task execution, and speech playback are separate identities.
Show the current voice-question session/purpose when one exists; never silently interpret a generic reply as an answer to whichever row was most recently painted.
User-requested question presentation or Answer by voice selects/revalidates that exact voice target and withdraws the previous target's generic eligibility.
Answer by voice selects an input target only; it does not change feedback, authorize a speak-once exception, or bypass call policy.
Passive background questions/progress never steal voice target, keyboard focus, or another session's compact interaction.
If switching/hiding/expiry makes the previous voice target no longer eligible under presentation policy, clear eligibility rather than retargeting a reply to another pending card.
Resolution/expiry clears the target; do not automatically advance generic reply eligibility to a different session's question.
Explicitly naming another session/question presents and revalidates it before acceptance; high-risk confirmations retain exact-action wording rather than generic yes.

Independent sessions continue running while the user reads history, answers another session, changes selection, or closes a window.
The proposed initial two-slot limit and one-task-per-session ordering are visible, configurable within verified capability, and never confused with the number of open windows.
Conflicting/dependent work shows Waiting for resource/dependency/outcome with the named reason; unrelated sessions remain eligible.
Background completion adds retained events and unread/attention status; notifications are session-labeled and follow configured output/call/proactive policy.
Shared speech is serialized and labeled by session; there is no interleaved TTS from independent tasks.
New session creation and related-session continuation both acknowledge the destination and provide a correction/switch route.
Typed input in a named session composer always targets it; a global/tray/untargeted voice request follows canonical relatedness routing instead of borrowing UI selection.

## Lifecycle and Presentation Persistence

| User intent | Presentation/work behavior |
|---|---|
| Hide/close compact or workspace | Change visibility only; retain work, history, drafts, and pending interactions |
| Switch session | Show that session's draft/history/state; continue other sessions independently |
| Mark Done | Resolve live work explicitly, archive readable history, end session-grant scope; do not imply task success |
| Read a Done session | Read/search existing evidence without resume or inactivity refresh |
| Resume | Explicitly reactivate; do not replay tasks or restore consumed/ended grants |
| Delete | Exact data/work preview and confirmation; purge session evidence under the store contract, preserve independent perpetual grants |
| Restart Kora | Restore readable sessions, selected identity, safe presentation preferences and drafts; show interrupted/unknown work without auto-dispatch |

Defaults remain archive after 24 inactive hours and delete after 30 inactive days, both configurable from last meaningful activity.
Show due dates, holds for live/unknown work, policy-change previews, and apply-now decisions through Settings/workspace; passive UI viewing is not activity.
Revalidate restored drafts against current question/revision/lifecycle; expired questions become read-only evidence, not live controls.
Persist window placement/size/pin preferences only where safe; missing displays/work-area changes rehome surfaces visibly and keep keyboard/tray access.
No geometry, theme, scroll, selection, or focus restoration replays answers, approvals, work, or microphone acquisition.
Storage failure is explicit; do not show a sent message, saved answer, or completed action when required durable acceptance failed.

## Accessibility and Degraded Operation

Provide keyboard access and screen-reader labels for session lists/search, tabs/filters, composer, forms, Details/History, queue, Done/resume/delete, and all trusted cards.
Maintain intentional focus/tab order; a background update never moves focus to an affirmative action.
Use shared theme, text scaling, reduced motion, contrast, and multi-display/DPI rules; animation is never the only progress/attention signal.
Missing voice/runtime/network does not remove the UI path. Native session/work controls remain deterministic; unavailable answer generation is explicitly labeled.
Unavailable management inference offers native routing/task choices; it does not silently merge sessions or lose a queued request.
Do not expose sensitive history on a locked/disconnected desktop; unlock recovery follows policy without forcing automatic voice capture.

## Coverage and Delivery Evidence

| Original requirement | Primary surface / required evidence |
|---|---|
| Pure voice, pure UI, or mixed interaction | Shared cards/services; repeat the same end-to-end workflow through each eligible channel |
| Structured questions and answers | Compact/workspace native choices/forms; mixed-channel draft, validation, and exactly-once submit |
| Abbreviated information | Compact concise result; full retained response remains available |
| Expanded Markdown/HTML/PowerShell review | Workspace expansion and immutable detail viewer with bounded supported rendering |
| Grants to continue | Native approval cards plus independent inventory, scoped decision and observed dispatch evidence |
| Concurrent chat/work sessions | Workspace/All work, two admitted independent tasks, isolated history/questions/queues |
| List and switch current/completed sessions | Deterministic tray/compact/workspace and verbal operations; selection does not cancel work |
| Full conversation, scripts, decisions, actions and grants | Ordered workspace history and artifact/receipt links; no summary-only replacement |
| Done/archive/resume/delete | Exact lifecycle controls, work-resolution choices, and truthful retained/deleted state |
| Configurable inactivity policies | Settings/workspace due dates; timer/startup/restart and passive-browsing tests |
| Related-context/new-session routing | Explicit composer precedence, destination acknowledgment, ambiguous-candidate question |
| Model access to active/archived evidence | Bounded session/history/artifact tools; citations, permission filtering, no implicit resume/replay |

Review these layouts and state transitions before choosing implementation components or treating a visual prototype as working session concurrency.
Evidence must include two simultaneously progressing sessions with a third waiting, independent question drafts, switching during streaming, no focus theft, script review while approval remains reachable, Done/deletion races, restart recovery, and voice/UI parity.
