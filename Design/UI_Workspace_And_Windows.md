# Session Workspace and Coordinated Window Design

Status: adaptive session/conversation workspace, integrated passive text/Markdown
Details, exact-session composer/history and bounded question/review surfaces
are implemented. General result routing, consequential concurrent workers,
rich browser/diagram profiles and installed accessibility remain gated. See
the [current inventory](Implementation_Roadmap.md#current-delivery) and local
delivery boundaries below.

Related: [Interaction and Sessions](Interaction_And_Sessions.md), [Work Management](Work_Management.md), [Information Display](Information_Display.md), [Shared Questions and Recovery](Interaction_Fallback.md), [User Configuration](User_Configuration.md), [Internal Model Tools](Internal_Model_Tools.md).

## Product-facing workspace boundary

Apply the [approved realignment](MVP_Scope.md#approved-retirement-and-simplification): normal workspace surfaces serve conversation, useful work and readable results, not engineering metadata exploration. Prefer names, lifecycle/state, ordinary search and proportionate ordering over generation, authority, digest or revision-range query widgets.

Technical provenance and internal validation remain authoritative; Evidence retains its diagnostic purpose. Saved-draft and public diagnostic scheduling controls are removed. Unsent input follows the [conversation contract](Interaction_And_Sessions.md#conversation-input-realignment), not a document lifecycle.

## Adaptive primary interaction window - 2026-10-25

Approved outcome: participate in independent session conversations and inspect readable results.
The concrete interaction is Show sessions, select a presented conversation, explicitly read its
conversation/activity and send a message to its named composer. Kora owns window selection,
private disclosure and input admission; extensions and model prose cannot own these controls.

The primary [response window](../src/Kora/ResponseWindow.axaml) now expands in place into
a resizable two-column [session browser](../src/Kora/SessionInteractionView.axaml). Tray
**Show sessions**, Ctrl+Shift+S, **Show sessions** in the response header and admitted
show/expand commands reuse that same window. Collapse returns to the original response,
typed command and live interaction controls. The conversation view remains mounted:
expansion/collapse does not recreate the session model, move input, save, submit or replay.
The expanded window does not use the ordinary response inactivity timer.

- The left pane defaults to Active on its first list load, with All/Done filters and
  explicit Refresh/Next. Ordinary conversation-name substring Search/Clear/Next
  matches/Cancel reuses the validated bounded metadata search; edits read nothing,
  retire captured ordinal targets and never select or resume a match. Matching is
  case-sensitive ordinal text, not transcript search. A list request admits a
  bounded metadata page, never all transcripts. Opening does not select a default session.
- The right pane separates retained Conversation (original user text, genuine assistant
  answers and native action outcomes) from read-only Activity (the existing ordered
  host task/question/answer/decision receipts). Read/More are explicit source reads.
  Message/activity Details reuse the original admitted immutable-source resolver.
  This is not a new aggregate event schema, raw model-reasoning viewer, calculator,
  tool-trace store or complete exact-grant-change feed.
- A named session composer uses the existing admitted submit path and per-session
  memory-only input. Done browsing never resumes work. Advanced provider, retention,
  additional search, evidence and lifecycle controls remain under **Session management**.
- **Current interaction** reveals the original response/question/approval controls
  inside the same window. Pending legacy interaction is indicated in the header;
  the independent authoritative native question window remains independent.
  Historical cards cannot answer, approve or revive a question.
- Closing the last participating native view protects unsent input through the
  existing Discard/Cancel owner; privacy/ownership/session retirement revokes content
  and delayed publication without saving it.

Running-task notifications retain the exact selected source, selection lifetime and
private-control revision captured before native dispatch. Switching away and back,
replacing session metadata or closing the presenter retires that callback; it cannot
restore old task controls on a new selection or reopened presenter. Fresh explicit
running-work inspection remains available. This does not change stop/delete,
last-view-close, submitted-work ownership or physical-resource joins.

### Presented-list voice selection

Typed or current-name-activated **select the top/first/last/third session** and
first through twenty-fifth select one exact row of the currently presented loaded
page. Positions start at the page's top, not the scroll viewport; Last means last
on that page, not last in the entire retained catalogue. Background work observations
do not reorder the list. Refresh, filter/order/source replacement, collapse, privacy,
ownership or disposal invalidates the captured target; missing/stale positions refuse
instead of selecting a replacement. Selection acknowledges the conversation name in
native status and preserves other sessions' unsent input. It does not read conversation
bodies, start listening, dispatch work, resume Done or change question/approval authority.

### Routing is independent of window mode

The explicit `ask this conversation <original body>` command binds only the unique current foreground Active presenter, with readable exact native review and a separate one-use confirmation. The [shared presentation owner](../src/Kora/SessionsPresentationOwner.Foreground.cs) supplies this host snapshot; current selection is not permission for other utterances. The [complete operation](Interaction_And_Sessions.md#explicit-original-request-to-the-foreground-conversation) preserves independent unsent buffers and default routing.

The approved [relatedness contract](Interaction_And_Sessions.md#proposed-relatedness-routing)
applies in both modes. Selection supplies conversational context, not a routing lock.
Explicit session composers/dictation and named targets win. Untargeted general voice
may match the current Active session, another Active session or a new conversation;
several plausible matches require a choice including New. A user-requested accepted
route acknowledges its destination and reveals it in expanded mode while preserving
the previous input/reading position. Background events never navigate.

Untargeted original requests now compose the reviewed local relatedness operation:
one permitted metadata page, one admitted tools-free local inference, exact
destination acknowledgment/correction including New, then canonical original-body
submission and genuine final answering. Its routing control domain is not a user
destination; inference physically joins before review. No hosted matching,
model-callable window navigation or automatic destination submission is admitted.
Unqualified local inference still offers reviewed New rather than guessing.
No runtime, installed accessibility, physical display or provider
qualification is claimed by headless [adaptive tests](../tests/Kora.Windows.IntegrationTests/AdaptiveInteractionTests.cs).

## Integrated session content pane - 2026-10-25

Status: bounded native-text/Markdown, retained original/assistant answers, selected
activity and explicit local-UI session log/audit placement implemented. Rich
diagrams, static HTML and embedded browsing are not enabled by this increment.
This [product-owner decision](Decision_Register.md#integrated-session-content-pane---2026-10-25)
advances the approved readable-results and independent-conversation outcomes.
The concrete scenario is reading a report, Markdown table, diagram, admitted page
or session-command log/audit result while continuing its session conversation.
Kora owns layout, exact-target navigation,
private disclosure and trusted controls; extensions provide admitted content only.

Complex session results open in a contextual **Details** pane within the expanded
primary window, not another top-level window:

```text
+------------------+---------------------------+---------------------------+
| Sessions         | Conversation [Show details]| Details: report / diagram|
| Active/Done/All  | Earlier result [Open] ---->| Rendered | Source         |
| Selected session | Later result [Open]       | Find | Expand | Hide      |
| Other sessions   | Message this session...   | Passive content viewport  |
+------------------+---------------------------+---------------------------+
```

- **Open details/Open result** resolves one exact admitted session/item/revision,
  expands the primary window if compact, and reveals the Details pane. It does not
  rerun work, submit input or grant authority. Default hands-free Offer and explicit
  voice opening use the same route and policy as UI opening.
- Keep the pane closed until requested or allowed by the existing foreground-only
  automatic-detail preference. Background completion may indicate available content
  but never replaces the item being read, selects a session or steals focus.
- Keep the conversation/message pane and its composer visible beside Details,
  separated by a pointer- and keyboard-resizable divider. Enforce a minimum expanded
  window width and minimum pane widths so neither collapses into an unreadable strip.
  Do not replace conversation with a Details tab or stack the panes on narrow windows.
  **Expand details**, if offered, adjusts the split only within those minimums;
  **Back to conversation** focuses the originating message without hiding either pane.
  If the available work area cannot fit the minimum layout, report the limitation
  explicitly rather than placing controls off screen or silently using a different
  layout. The session and item remain named.
- Provide native **Show details** when hidden and **Hide details** when visible.
  Hide returns space to the persistent conversation pane and focus to the originating
  message link or Show control. It does not clear unsent input or delete result links.
  Show is disabled when there is no selected session or its currently admitted
  metadata has no available detail references; unknown/stale availability is disabled
  with an explanation and explicit refresh/recovery route, not treated as empty success.
  Availability checks do not read all result bodies, scan logs or run commands.
- Show restores the last explicitly chosen still-eligible reference for that session,
  repeating source/private admission before rendering. If none was chosen, show the
  known detail references in a native chooser within the pane, rather than silently
  selecting the newest result. A remembered title/reference alone is not disclosure
  authority. Failed admission clears unavailable content and reports the reason.
- Conversation entries can each expose one or more named native **Open details**
  links bound to their exact session, originating message/event, item and immutable
  revision. Activating an earlier or later result reveals the same pane and selects
  that exact item, even while another item is open. Duplicate titles and later
  completions cannot retarget a link. Replacement retires old text/Find state and
  fences delayed renders; unavailable items explain the failure without substituting
  the latest result. These are host-resolved references, not arbitrary executable
  links supplied by Markdown, HTML, model prose or log text.
- Opening, closing, resizing, expanding or changing layouts preserves the session's
  unsent input and conversation position. It never changes the voice-question target,
  hides required native interaction controls without a reachable route, or acts as
  an answer/approval. Keyboard and accessible UI support the same operations.
- Keep a bounded, memory-only item binding and reading position per session.
  Switching sessions hides the previous session's content and shows only the newly
  selected session's own still-admitted in-memory view, or an empty/reopen state.
  Selection alone performs no source read; restoration needing fresh resolution
  requires explicit Open. No unrelated item is relabelled as the new session's result.
- Hiding/closing Details releases its text, Find state and renderer resources, not session work or the
  composer. Reopening repeats current admission. Collapse hides the content area;
  hidden browser views must stop renderer-owned activity or be disposed, not continue
  invisible browsing. Privacy, ownership/source loss, deletion and lock revoke content
  and fence delayed publication under the existing viewer contract.

### Session-command log and audit details

Reuse the same pane for bounded read-only log/audit records or query-result pages
explicitly requested through admitted session commands. The command reply can carry
a named detail reference on its own conversation entry; opening it uses the same
exact-target route as an earlier report or later diagram. It never reruns the command,
silently refreshes the query or follows an unbounded live log stream. A fresh query
or next page is an explicit admitted read with its own source/query binding.

The [Evidence contract](#evidence-mode) remains authoritative for retrieval,
pagination, filters, typed fields, source/authority labels, redaction and gaps.
Diagnostics remain observations, audit records retain their typed authoritative
outcomes, and neither is promoted to a conversation instruction or new action.
The pane reuses those owners rather than introducing a second evidence store,
query engine, raw log dump or grant/approval surface. It need not be Markdown when
typed native record presentation is more faithful.

Every result names its actual session/query scope. A session command does not
implicitly broaden to other sessions or the whole diagnostic corpus. Independent
[retention domains](Security_Data_Flows.md#retention-defaults) still apply:
a conversation link cannot preserve expired log bodies, recover deleted session
content from longer-lived audit rows or renew access. Expired/revoked/unavailable
results clear content and show their status; copy/export/model egress retain their
separate admission. The delivered fixed `show session logs` / `show session audit`
routes use existing Evidence queries for the selected session only. They require
original LocalUi admission; activated voice is refused rather than relabelled.
Other command/retrieval routes remain unavailable.

### Delivery boundary

The [shared native reader](../src/Kora/DetailContentView.axaml) is composed into the
[expanded session surface](../src/Kora/SessionInteractionView.axaml) for exact
retained USER, separately retained full ASSISTANT, selected activity and ordinary
current-response details. The conversation/composer stays mounted. Minimum width
is 1040 device-independent units with minimum conversation/Details widths of
320/400; insufficient work area is explicitly unavailable. Hide/Escape releases
private reader/Find state, retains bounded memory-only reading positions and split
size, and permits explicit re-resolution. Show restores an eligible last choice
or presents a native chooser. Current-response details name their own original
source and are explicitly labelled as separate from the selected conversation.

Native **Back to conversation** now completes exact-origin return navigation for
the currently observed retained USER/ASSISTANT message page. It reveals the
Conversation tab, scrolls and focuses that exact original/answer link while
preserving the open reader, Find and split. Explicit native/typed/activated
**Hide details** shares origin-aware focus recovery, falling back to Show when
the origin is unavailable. Escape keeps its existing Show recovery. Choosers,
activity, evidence and unrelated current responses do not invent a message
origin; return is disabled with an explanation. Source/session/private retirement
fences delayed return without source reads, submission, session selection or
authority changes. These are deterministic native regressions, not installed
accessibility or provider qualification.

Completed complex answers expose a brief conversational projection and a native
offer. **Yes, show details** or `show me the detailed results` opens the exact full
answer without another model call; **Not now** declines only that offer. The
complete original answer remains unchanged. Generic prefix-free spoken Yes is
not an added question channel: activated voice uses the explicit details command.
Malformed/unstructured complex answers receive an honest availability notice,
not a fabricated or truncated summary. Ordinary speech caps still apply to the
complete brief response, host warnings and offer.

Session log/audit commands retain at most eight bounded, separately linked
in-memory result pages on the observed session page. Reopening revalidates their
exact source records, never substitutes a fresh query result. The visible evidence
page is also periodically revalidated; changed, removed, unavailable or newly
due source projections retire its displayed text. Session/private changes clear
pages and fence late completions. These HOST entries are not retained ASSISTANT
answers, durable conversation payloads, clipboard grants or a live log stream.
The [production-composed regressions](../tests/Kora.Windows.IntegrationTests/AdaptiveInteractionTests.Details.cs)
exercise same-window exact links, input preservation, source labels and retirement.

The smallest useful increment migrates the existing bounded passive native-text/
Markdown review into this shell, with exact-source fallback and no new producer,
automatic-routing implementation or renderer enablement. Mermaid, static HTML and
live websites use this placement only when their own capability/profile is admitted.
Live sites retain separately labelled native URL/navigation controls and an isolated
browser lifecycle; shared placement grants no Kora bridge, shared credentials or
implicit network/context permission.

The first integrated increment has no detachable content windows. Existing supporting
management and authoritative question/approval surfaces are unchanged. It does not
require migrating every specialized native preview or authoring workspace. Acceptance
is owned by the [information-display gate](Acceptance_Criteria.md#information-display-gate);
deterministic layout/navigation evidence is distinct from renderer enablement and
installed accessibility/display qualification.

## Delivered selected-conversation lexical search — 2026-10-13

Sessions and compact conversation share the selected session's bounded chronological conversation search and immutable message details. The [ordinary-search correction](https://github.com/roryprimrose/Kora/pull/428) preserves inclusive submitted-date bounds and meaningful observed task-state choices without restoring raw task/request IDs, sequence/count, generation or revision-range controls.

Explicit Search/Next uses current private/source admission and a query-bound continuation. Edits revoke results without reading, replacing unsent input, answering a question, resuming work or renewing activity ([conversation search](../src/Kora/SessionsViewModel.ConversationSearch.cs)).

Retained USER, optional ASSISTANT and typed HOST outcomes remain distinct inert data. Navigation to Details revalidates the original retained record; results are never promoted to instructions, approvals or execution.

## Delivered exact-session native composer — 2026-10-12

The [conversation owner](Interaction_And_Sessions.md#delivered-exact-session-conversationcomposer-foundation--2026-10-12) uses one in-memory unsent buffer per conversation, shared by Sessions and compact views. Switching topics preserves the independent buffers without Save/Discard prompts.

Done/Resume and exact-session retirement clear only the target's unsent input and invalidate its content/controls, not the shared native views or unrelated buffers. Returning to another conversation restores its exact unsent text in both views; the integrated interaction uses that same owner. Privacy/ownership closure still closes the shared presentation and clears all buffers.

Worker-thread lifecycle notifications complete exact-source retirement on the native dispatcher before the mutation call returns. The shared window remains available for fresh admitted inspection; stale selected controls cannot execute ([native regression](../tests/Kora.Windows.IntegrationTests/SessionsWorkWindowTests.cs)).

Explicit discard or voluntary last-view/application close protects unsent content with Discard/Cancel. Ordinary Save/Reload/Clear, catalogue, comparison and rebase/recovery windows are gone; only legacy exact-session read-only recovery into empty input remains ([buffers](../src/Kora/SessionsViewModel.Unsent.cs), [shutdown guard](../src/Kora/ConversationShutdownGuard.cs)).

Submission is a new admitted original-user turn, not a draft save, replay or grant. History reads do not implicitly read legacy saved text or start providers. Mandatory privacy/ownership/OS retirement and resource-quiescence rules remain independent.

## Delivered explicit expanded-prompt submission — 2026-10-13
The existing native required-input prompt window now also offers a distinct exact-session retention review. Refresh/Next navigates admitted bounded metadata, with no default selection. Separate native confirmation shows full expanded USER text and exact target/source observations; only that confirmed body enters the existing conversation authority.
This does not switch the Sessions window, voice focus, attention target or current composer, nor save/clear any existing draft. Review loses validity on edits, source/use/private/owner/target changes or focus loss. Known receipt retires the form; unconfirmed submission is held for conversation/task inspection, never retry. Preview and old Use remain ephemeral and generation stays unavailable.

## Product Goal and Ownership

Kora is a concurrent work-session application with voice, mouse, and keyboard interaction, not a single linear conversation with a transient answer popup.
The compact window stays simple, but is a view into a durable selected session.
The larger **Sessions workspace contains the session list beside the selected session's full conversation**.
The [integrated content pane](#integrated-session-content-pane---2026-10-25) supports deeper result/script review without losing the conversation or current question; existing bounded Details windows remain delivered behavior until migrated.
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

The [Sessions window](../src/Kora/SessionsWindow.axaml) observes an exact session's retained tasks, questions and queue receipts through the same [workspace service](../src/Kora.Application/Hosting/SessionWorkspaceService.cs) and validated private snapshot used by exact status commands.

New diagnostic enqueue/dispatch, prerequisite, reorder/replacement plans and fixed-read configuration controls are removed. Internal scheduler policy, stored identities/revisions/deadlines, audit and recovery are preserved; existing pending work can still be inspected and cancelled through its original host gate ([compatibility](Work_Management.md#delivered-deterministic-local-version-queue)).

Selected-subset cancellation retains exact current-run membership, complete review and separate atomic confirmation. It does not terminate running effects, replay work, retarget dependents, erase history or turn Unknown into success. Browsing status is not execution authority, meaningful activity or a voice/question target.

### Delivered Session Memory Scope Metadata Filter - 2026-10-15

The separate **Sessions > Session memories** surface exposes exact GUID plus
**Scope**, **Review** and **Retention** metadata filters over its already-loaded
bounded snapshot. Each enum starts with visibly selected **Any**; all predicates
are AND. The 2026-10-16 exact scope identity GUID field (empty for Any) and inclusive
revision/created-time ranges compose with those predicates. Scope kind/identity
are row metadata, not permission, ownership, scope or provider authority.
The identity field has an explicit automation name and keyboard focus; Escape
clears every filter in place, while Clear returns focus to the memory ID field.
Edits retire selection,
inspection, draft and pending callbacks without reads; explicit **List** alone
refreshes the source. Undefined filters or malformed row metadata show no results
and explicit recovery, not a partial list. See the
[memory filter contract](Model_Providers_Memory_And_Knowledge.md#delivered-native-content-free-session-memory-filters---2026-10-14).
Headless keyboard/focus and automation checks are not installed accessibility qualification.

The 2026-10-16 extension exposes inclusive revision minimum/maximum and Created
From/Until UTC text fields, each empty for Any, with keyboard focus and explicit
automation names. Revisions are positive decimal Int64 values; Created input uses
`yyyy-MM-ddTHH:mm:ss.fffffff+00:00`. Equal endpoints are included; reversed or
malformed ranges show validation recovery and no rows. All predicates compose
with AND over the same frozen snapshot. Clear or Escape in any text filter clears
all bounds without I/O. Edits still retire inspection, draft, selection, mutation
readiness and pending callbacks, even when a row remains visible. No permission,
provider, memory-content or mutation authority is added.

### Delivered Retained Session Attachment Catalogue - 2026-10-11

The [attachment owner](File_And_Folder_Ingestion.md#delivered-retained-attachment-metadata-catalogue---2026-10-11) defines the retained native catalogue and exact source/copy disclosure boundaries. Browse/lifecycle/type/status/byte filtering and selected metadata Find remain; engineering identity/digest/generation-range query controls do not.

This surface is metadata observation, not original-file access, retained-byte integrity proof, execution, model context or egress consent. Actual Inspect repeats the existing private/source checks.

### Delivered Bounded Passive All Work / Attention Overview - 2026-10-11

The [All work window](../src/Kora/AllWorkWindow.axaml) retains lifecycle, name/exact-session navigation, observed task state, Needs answer, loaded-page ordering and Find in displayed metadata. It observes bounded independent per-session snapshots; omissions, changed/unavailable sources and output bounds remain explicit, not an all-clear or atomic global inventory.

Raw retained task/request correlation queries, authority/queue/count ranges and technical availability/content query widgets are removed. Source identity and status remain visible where useful, and [query service](../src/Kora.Application/Hosting/SessionWorkspaceService.WorkOverview.cs) validation remains mandatory even for excluded rows.

Selecting a row does not answer a question, choose a voice target, dispatch/resume work, grant permission or renew activity. Exact navigation repeats current source/private admission; opening/typing/order/Find do not create background work.

### Delivered Native Exact-Session Retention Controls - 2026-10-10

When due-content inspection is held, the existing canonical exact-ID field and **Read exact ID retention** provide metadata-only status/review without reading names/history/work or creating implicit conversation/resume authority. Removed/purged observations are nonmutable.

The selected-session **Retention: status and exact review** panel now shows actual UTC activity/due dates and kept/purged state. **Refresh retention status** is passive. **Keep this session** / **Use ordinary retention** first stage an exact ID/generation/state/audit-revision preview; **Confirm exact retention review** is a separate native action. Cancel, selection changes and close discard it.

Ordinary policy uses the displayed original due dates, never resets inactivity, and warns about soon/already-due background archive/delete. The session hold is explicitly not an operation grant, consent, memory, recall or execution. Observed work holds and unobserved copy/maintenance availability are distinguished. Stale/removed/purged, ownership/privacy/call changes and uncertain audit/commit/readback require fresh durable inspection, not blind checkbox-save or false rollback.

Browsing no longer invokes cleanup. Confirmed policy, copy inventory, due-candidate holds and source revisions remain read-time fail-closed checks; eligible due ordinary content waits for private background maintenance. See the [retention contract](Interaction_And_Sessions.md#r12-native-exact-session-retention-controls---2026-10-10) and [native/headless tests](../tests/Kora.Windows.IntegrationTests/SessionsRetentionViewModelTests.cs). No model, typed/voice mutation, apply-now or installed-native qualification is added.

### Delivered Passive Session-List Metadata Navigation - 2026-10-10

The [session list](../src/Kora/SessionsViewModel.ListSearch.cs) supports bounded Browse, literal name substring, whole exact name and exact immutable session ID, with lifecycle and observed named/unnamed choices. Duplicate names remain separate conversations; names never select action or approval authority.

Generation, name-revision and Unicode-name-length query ranges are removed. Internal generation, name validation, current selection/source revision and authenticated continuation checks remain unchanged. Explicit Search/Next performs the admitted read; edits/Clear retire stale results without reads or implicit selection.

Finite scanned/nonmatching/omitted counts remain truthful. Every candidate is validated, including excluded rows; zero matches with Next are not an exhaustive absence claim. Exact navigation preserves per-session unsent input and revalidates privacy/ownership/source.

### Delivered Passive Details Boundary

Receipt-history search has five separately labelled native choices with explicit
**Any**: receipt kind, task state, decision, question status and answer channel.
They narrow only the older exact-session interaction receipts, not adjacent
conversation, session-list or Evidence searches. All selected values AND-compose
with the lexical OR query. Missing metadata is not an exact match. Editing any
choice performs no read and retires old receipt selection/details/continuation;
Search is explicit. Clear and query-field Escape restore every choice to Any
and retain query focus. Source/private/subject changes and closure clear these
volatile values and fence late callbacks. Accessible names distinguish each
historical field from current identity, approval, question or task authority.
The [receipt contract](Interaction_And_Sessions.md#delivered-passive-exact-session-lexical-history-search---2026-10-09)
owns admission, digest, bounds and passive-only semantics; deterministic headless
checks do not claim installed-native accessibility qualification.

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

**Settings > Readiness > Question diagnostics > Return to local-version question** restores the dismissed original current-run local-version window only while its host-owned original wait remains live. Close/Escape clears native controls, unsaved input and review, but does not submit, cancel or end that wait. The committed draft/current revision may return; there is no affirmative default.

Already-open exact presentation activates, independently of Sessions selection and voice/approval focus.

The original five-minute expiry continues without a window; return extends neither it nor meaningful activity. Exact task/wait/key/generation, live ownership/privacy and audited review must still agree. Expiry, cancellation, completion/Unknown, changed authority, storage/audit failure, closure or disposal removes eligibility and releases the host await/timers.

After restart, retained metadata is read-only **Unavailable** recovery with a fresh-request instruction, never old-task rehydration. No general question/approval composer or installed-native qualification is claimed.

The [synchronized terminal boundary](Interaction_Fallback.md#synchronized-native-terminal-publication---2026-10-12) keeps committed cancellation and original completion coherent without holding notifications under a lock. Later withdrawal still clears private controls after a receipt; late restore cannot undo withdrawal or a newer dismissal. Existing five-minute expiry, exact approval review and pre-dispatch privacy/source checks are unchanged.

**Settings > Readiness > Question diagnostics > Review local version (native question)** opens one owned, non-topmost
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
| Tray and ambient Presence | Tray: input status, Show Kora, New request, Sessions, Disable listening, Voice & audio, Settings, Customizations, Help, Exit. Presence: actual listening/activity/attention | Tray has no Stop speaking or specialist inspector shortcuts; Presence is optional presentation, not the work registry ([tray](../src/Kora/SystemTrayController.cs)) |
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
Digest/revision/observed-time query widgets are removed; those admitted values remain displayed and internally validated. Name/identity mode and observed use-state edits retire stale selection/details/page/Next and review callbacks without reads or replacing the open draft ([current controls](../docs/instruction-editor.md#search-observed-owned-metadata)).
List/Refresh reconciles the existing bounded source pipeline; query/filter/Next only projects its admitted immutable owned metadata snapshot. Complete labels/details are bounded to 64 KiB UTF-8 with explicit omissions and Next when needed.
Selection alone never opens another file, grants use or retargets an unsaved draft. Inspect revalidates the original kind/filename/digest/native revision and catalogue/private epoch. Stale choices require Refresh/reselection; failed reload clears availability, while the original/draft remains subject to existing privacy guards.
Private closure clears query/rows/selection/editor text and suppresses late publication. Keyboard-native controls and current exact List/Inspect commands share the editor; broader profile editing, source filters and accessibility/installed qualification remain deferred. See [owned inventory controls](../docs/instruction-editor.md#search-observed-owned-metadata).

The same editor has a separate native-only **Displayed owned buffer Find** toolbar: Ctrl+F, first/next/previous/clear, exact case-sensitive bounded literals, complete nonoverlapping count/ordinal and no wrap.
It selects only the exact occurrence in the existing displayed source control, including dirty drafts; metadata inventory selection and source editing/undo/accessibility stay native.
Buffer, chosen identity, loading and private/control lifetime changes retire matches and callbacks; focus failure reports recovery rather than a match.
No disk/discovery, mutation, validation, use or clipboard authority is added. See the [owning definition contract](Definition_Management.md) and [native buffer workflow](../docs/instruction-editor.md#find-in-the-displayed-owned-buffer).

The owned Instruction/Prompt editor's explicit **Compare** opens only a Close-only passive native viewer: frozen inspected baseline beside exact current memory draft, or labelled missing/new baseline. Raw source and separately labelled prepared-Save validation are distinct. Identity/input/source/private changes withdraw and clear it; it has no mutation, consent, copy/export or use-review authority. See [Compare source presentation](../docs/instruction-editor.md#compare-the-open-draft-without-saving); this is not the planned full profile/diff workspace or installed/accessibility qualification.

## Compact Session Interaction

### Delivered compact selected-session projection

The explicit [native compact surface](../src/Kora/CompactSessionWindow.axaml) shares one selected session and memory composer with Sessions.
No target is chosen on open. Explicit Read/Next uses admitted conversation pages; stored USER text and typed HOST outcomes never become assistant output. Metadata catalogue navigation still imports no body.

The [shared presentation owner](../src/Kora/SessionsPresentationOwner.cs) preserves the surviving view, serializes dirty decisions, and releases timers/events only when the presentation is revoked or its last view closes.
Switching sessions preserves each independent buffer without a discard prompt. Last dirty close protects Discard/Cancel without Save; private/source retirement releases both views' volatile text and fences callbacks ([shared input](../src/Kora/SessionsViewModel.Unsent.cs)).

The [shared controller](../src/Kora/SessionsWindowController.cs) keeps single-instance activation and exact navigation. Workspace provider/routine-quiet visibility remains workspace-owned.
Standalone question/approval/voice and legacy response presenters are unchanged. No per-task automatic popup, generation, relatedness, global/voice auto-routing, model context or new storage representation is added.

Maintained [shared-view tests](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.Compact.cs) and [controller tests](../tests/Kora.Windows.IntegrationTests/CompactSessionControllerTests.cs) cover this bounded projection, not full coordinated R14, installed accessibility, provider/hardware, physical-storage or STOP qualification.

### Workspace surface command evidence

The [typed navigation grammar](../src/Kora.Core/Commands/WorkspaceSurfaceCommand.cs) owns exact routes to existing surfaces, including adaptive Sessions and presented-page selection.
The [application event](../src/Kora.Application/ViewModels/MainViewModel.WorkspaceSurfaces.cs) precedes pending-answer routing and carries original admission; the [native adapter](../src/Kora/WorkspaceSurfaceCommandController.cs) rechecks epochs and compact presentation/selection before invoking the same composed controllers.
It uses a linked, content-free presentation activity, not model/session authority.

[Grammar tests](../tests/Kora.Core.UnitTests/Commands/WorkspaceSurfaceCommandTests.cs) cover exact/current-name routes, conflicts and malformed reserved input.
[Application tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.WorkspaceSurfaces.cs) cover recognition phrases, unavailable composition, busy input, pending-decision buffers and epoch retirement.
[Headless controller tests](../tests/Kora.Windows.IntegrationTests/WorkspaceSurfaceCommandControllerTests.cs) cover singleton reuse, shared dirty memory, inert opening, Done authority and queued source/ownership/closure/disposal.

Only consumed implementation contracts and deterministic fixtures are required. Model navigation remains unavailable: no bootstrap or six-read-only-descriptor expansion. No native-installed, hardware, provider, acoustic or containment qualification is claimed.
[Commands](../docs/commands.md#open-existing-workspace-surfaces) and [windows](../docs/windows-and-tray.md#workspace-surface-navigation) own user syntax and recovery.

### Compact layout illustration

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
Its [native draft Find](../src/Kora/NewRequestWindow.Find.cs) searches only the current
editable in-memory draft, even if dirty or invalid, using the authoritative
`SnapshotLiteralFind.FindCounted` bounded ordinal one-line grammar and full
nonoverlapping count/ordinal/no-wrap sequence. Explicit Ctrl+F/first/next/previous/clear
never searches frozen reviewed bodies, assistant answers, saved text or other composers.
Exact native editable-source selection/focus is fenced before and after reentrant
callbacks by buffer, composer, view/control, host input and control/privacy/ownership
lifetimes; stale operations cannot publish results. Composer/host notifications retire
volatile query/navigation/count; a disposable native observer also checks the captured
host lease for authority changes without notifications. Closing/detaching releases
observers and private query state, without restoring/collapsing user-owned selection.
Find has no I/O, composer mutation, review approval, transcript/speech/logging, capture,
model, navigation or new clipboard authority. Existing editing/copy/paste is unchanged;
missing focus is explicit. Deterministic headless tests are implementation evidence,
not installed accessibility/hardware/provider qualification.
Audited empty-target creation is acknowledged with its exact ID/name/generation/metadata revision before separately reviewing/submitting the frozen original body; these are not an atomic transaction.
Ordinary saved-draft Save/Reload/Clear and comparison/rebase/catalogue operations are removed. New request remains an isolated memory buffer; explicit review and Submit are separate from target creation. Native close/voluntary shutdown uses Discard/Cancel without writing a saved document.
Explicit exact-session navigation preserves the other conversations' unsent buffers without transferring this input. Privacy/ownership retirement releases volatile content; uncertain submissions retain their exact inspection holds without automatic retry ([New request owner](../src/Kora.Application/Hosting/NewRequestComposer.cs)).
Explicit New session bypasses relatedness matching; a named-session composer never silently auto-routes elsewhere.
Unsent text and answer drafts belong to their session/question, not one global text box; switching away preserves them and switching back restores them.
Clear the composer only after the host has durably accepted the input; persistence/admission failure leaves the draft recoverable with an error.
Question/definition editing drafts retain their own explicit contracts. Existing legacy conversation text is read-only recoverable under retention; it is not new autosave or submission authority ([conversation compatibility](Interaction_And_Sessions.md#conversation-input-realignment)).

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

The [deliberate native Reply operation](../docs/reviewed-context.md#reply-deliberately-to-one-exact-assistant-answer) is shared by the primary conversation, Sessions and compact surface. Its row action chooses one exact retained ASSISTANT answer; visible reply/source/cancel controls use the existing memory-only composer and separate full-source/new-input review followed by one-use local-only confirmation. Choosing Reply is not submission, and ordinary Send outside Reply remains unchanged ([native state](../src/Kora/ReviewedContext/SessionsViewModel.AssistantReply.cs), [three-surface execution evidence](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.AssistantReply.cs)).

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
authority. Advanced filters also offer exact **Parent Span ID**, explicit
**Host operation outcome** (Any plus every retained host outcome; null only
matches Any) and **Content availability** (Any/Present/Omitted, based solely on
`ContentOmitted`). Parent input uses the same strict W3C policy; all predicates
AND, with full record validation before exclusion. Clear advanced resets these
three choices to Any while preserving basic Trace/Span. Visible accessible Any
choices are real selections, not null placeholders. These are passive volatile
metadata, never identity, authority, integrity proof or content restoration.
No new navigation, export or model route is added; the broader mode below
remains a target contract, not qualification.

Advanced trace controls also offer exact source, operation name, defined
ActivityKind and exact None/Recorded flags, each with a visible accessible Any.
Source version has explicit Any/Absent/Present/Exact choices and a separate exact
text field; text is required only for Exact. Absent requires an existing trace
with null version, never an unknown/null trace. Exact source/version/name inputs
are case-sensitive plain valid Unicode, bounded to 128 characters without
surrounding whitespace or controls. All predicates AND after complete retained
trace validation, bind continuations and retire stale results/selection on edits
without reading. Clear advanced and close reset these controls. This is untrusted
diagnostic correlation, not session selection, identity, intent, permission,
approval or authority; no baggage, tags, paths, credentials, user or omitted
content is exposed. Deterministic headless checks are not installed accessibility
qualification; the detailed [Evidence contract](Information_Display.md#delivered-bounded-native-evidence-inspection)
owns these semantics.

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

An explicitly requested session-command result can use the
[integrated Details pane](#session-command-log-and-audit-details) for the same
admitted records/pages. Its conversation link names the exact result, not a new
query or command replay. The presentation location does not broaden source scope,
retention, model context, copy/export or action authority.

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

History lives in the workspace; this reader expands a particular retained item or task evidence bundle in the [integrated content pane](#integrated-session-content-pane---2026-10-25), not a mandatory separate window.
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
A selection change preserves the item's original session binding but hides it from the newly selected session's content area. Returning restores only that session's still-admitted in-memory view or an explicit reopen state; Back to conversation navigates to the exact bound session/event.
Export is an explicit scoped write with preview; retention/deletion does not claim to remove user exports.
If a source/session is deleted or access is revoked while open, show unavailable and clear denied content under the store/access contract; no invisible stale copy remains model-readable.
Missing renderers disclose source fallback/unavailable capability without losing basic text or installing dependencies automatically.

## Skills and Permission Management

The native **Customizations** surface manages skills, prompts, instructions,
agent profiles and MCP entries, grouped by Built-in (read-only), Kora roaming
and supported User profile sources. Skills remains a focused entry point. The complete control,
editing, applicability, and reload contract is
[Definition Management](Definition_Management.md#one-management-surface-equal-ui-and-voice-control);
native and original typed/activated-voice routes share the same admitted service.
The three-pane window supports exact source editing, complete file-set reviews,
skill component additions/renames/removals, recoverable writes to User originals,
persisted Kora exclusion and separately reviewed tool-free local-model proposals.
Foreground-editor voice binds one activation-time draft context and interprets
editing/discussion intent through separately reviewed local metadata. Native
clarification and complete-draft/proposal reviews never transfer failed or
stale input to another session; see the
[targeting contract](Definition_Management.md#foreground-editor-voice-targeting).
Saving leaves runtime use disabled; enabling removes only this exclusion, not
registration, compatibility, revision, authentication or qualification gates.
See [current routes and limitations](../docs/customizations.md).

The bounded delivered surface is one native Kora-owned instruction/versioned
prompt editor with an explicit kind selector, exact source/diff review,
Save/Delete, Reload/Overwrite/Cancel and dirty closing/shutdown Save/Discard/Cancel.
Typed/current-name activated `instructions` and `prompts` controls focus the
same native actions. Review only permits exact current-run selection for a new
request; prompts are user-request content, not system guidance or execution authority. Owned prompt v2 adds only bounded required literal `${input:name}` native fields and whole expanded USER REQUEST preview, followed by separate fresh Use admission.
Editing a field or source/review/input/call/private change retires old previews; close/cancel clears ephemeral values without history/autofill/export/speech or template autosave. Production prompt execution remains gated; no rejected-provider fallback is used. Native v1 remains literal. Broader applicability and runtime qualification remain separately gated. See [native editor](../docs/instruction-editor.md).
**Validate** / exact `instructions validate` or `prompts validate` observes only the open same-kind supported owned draft, using the ordinary Save preparation including BOM/marker/header limits. It does not open/switch/discard another source.
The text-only body-free summary is historical structural compatibility, never approval, harmlessness or enablement. Source/name/kind/baseline/catalogue/private/input changes retire findings and suppress late results. Validation retains draft/dirty/inventory/use/uncertain-hold state without source I/O, reload or preference writes; Save separately re-prepares and exactly confirms.
**Disable reviewed use** and exact `instructions` / `prompts` `disable` or `stop-use` focus one separate native confirmation for the clean inspected supported native owned source/kind/digest/revision (instruction v1; prompt v1/v2).
The durable hold withdraws all new selections without writing/deleting the source, preserves inspection/editing and already captured reasoning, and retires pending unadmitted choices without replay. Dirty drafts remain intact with an explicit Save/Reload prerequisite.
Cancellation/stale review and failed audit/preference readback never invent completion or restore enablement. No-change reload/restart/restored bytes cannot re-enable use; fresh exact review is required. Installed/accessibility qualification stays open. Customizations' universal exclusion is stronger than this older captured-use hold: it also retires pending captured-use eligibility.

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

Workspace and compact share one selected UI session; the integrated content pane holds exact read-only item references per session and never retargets one on selection change. Existing bounded Details windows retain their original independent binding until migrated.
UI selection supplies context for the mode-independent relatedness policy, not an unconditional voice destination. Explicit composer input remains addressed.
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
The [exact-grant history controls](../docs/settings.md#exact-operation-grants) additionally expose a focusable, named committed-event initiator selector: Any plus every defined typed initiator. It composes with committed outcome/exact reason, never Requested review or actionable inventory. Editing is passive and retires prior rows/continuation/selection/reviews without reading or renewing authority; only the explicitly inspected inert exact target remains.

The same bounded scrollable area exposes named, keyboard-focusable optional committed audit correlation ID, explicit committed approval metadata Any/Present/Absent/Exact and Exact-only approval ID inputs. Defaults are Any/empty; Clear restores them without reading. Strict canonical GUID admission, AND composition and digest-bound continuation follow the [committed metadata contract](Security_Data_Flows.md#grant-types-and-inheritance). Absent is not unknown, and IDs are correlation metadata only, never authority or execution approval. Deterministic headless keyboard/binding checks are not installed accessibility qualification.
Maintain intentional focus/tab order; a background update never moves focus to an affirmative action.
The same exact-history panel includes a named Any/Present/ExpiredButPresent retention selector and four named, focusable inclusive canonical UTC bound fields. Empty bounds mean Any; From/Until ordering and canonical validation fail explicitly. The bounded scrollable filter area preserves keyboard traversal without consuming the history list. All predicates compose on validated committed envelopes, never Requested-review details or action authority. Deterministic headless focus/binding checks are implementation evidence, not installed screen-reader qualification.
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
