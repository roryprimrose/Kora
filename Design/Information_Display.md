# Information Display and Rich Content

Status: accepted design direction; bounded native-text-v1 passive viewer
implemented for explicitly opened embedded guide pages. General model/result
routing, full conversation/artifact resolution and isolated HTML remain gated.
The guide, bootstrap grant document and passive viewer share the bounded
native Markdig/Avalonia pipeline. This is a partial R14 delivery, not the
general artifact or permission-management experience described below.
The bounded native shared-question slice additionally reuses this passive
plain-text renderer for complete immutable host-record review, separate from
native answer/approval controls.

The separate [exact Settings-section navigation contract](User_Configuration.md#exact-existing-settings-section-navigation) selects an existing named native tab in the singleton Settings surface only.
Responses-tab navigation is not a retained-body, retired-response, model or private-content disclosure route ([native selection](../src/Kora/SettingsWindow.axaml.cs), [admission](../src/Kora.Application/ViewModels/MainViewModel.SettingsSectionNavigation.cs)).

Apply [product admission](MVP_Scope.md#product-admission-and-realignment) to display additions. Readable complex data, Markdown, admitted browser content, ordinary Find, wrapping and accessibility support the product. Standalone content-analysis buttons and engineering-only normal-workspace filters are not justified by the existence of a viewer or immutable snapshot.

## Delivered bounded response-window preference commands

The [exact response-window grammar](../src/Kora.Application/Configuration/ResponseWindowCommand.cs) admits inspect/set/per-option reset for existing device-local AlwaysShow and Topmost, plus four explicit natural on/off phrases.
Typed and current-name activated voice routes delegate to the same native setters and trusted audited preference writer; they add no tool/model/external-content or arbitrary window authority
([command workflow](../src/Kora.Application/ViewModels/MainViewModel.ResponseWindowCommands.cs), [native setters](../src/Kora.Application/ViewModels/MainViewModel.cs)).

Results use transcript status rather than response presentation: no immediate speech, response reveal, source restoration, question answer/cancellation or security-panel replacement. Reserved malformed commands clarify before those routes.
Mutation requires the original owning unlocked host admission; the existing writer rechecks privacy/owner/call/voice epochs around commit and suppresses stale success
([command workflow](../src/Kora.Application/ViewModels/MainViewModel.ResponseWindowCommands.cs), [writer](../src/Kora.Application/ViewModels/MainViewModel.Preferences.cs)).

AlwaysShow suppresses inactivity hiding only; dismissal and private/source/call revocation still win. Topmost changes only Kora's response preference, never OS-wide state, activation, foreground consent, geometry, retention or speech/caption mode.
Each off/reset preserves the companion Boolean, saved position and separate timeout ([defaults](../src/Kora.Core/Configuration/ResponseWindowSettings.cs), [native notifications](../src/Kora/ResponseWindow.axaml.cs), [show admission](../src/Kora/ResponseWindowController.cs)).

Unknown/malformed or externally changed saved state cannot be overwritten using cached defaults; failures and uncertain commits require explicit saved-state/audit inspection and a fresh request after repair/restart.
[Deterministic tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.ResponseWindowCommands.cs) are implementation evidence only: no new hardware, installed UI/accessibility, acoustic, provider or RC qualification is claimed.

## Delivered retained original USER / typed HOST Details - 2026-10-14

After an explicit conversation Read or Search, the [shared native row](../src/Kora/ConversationPresentation.axaml) offers **Details - exact original USER / separate HOST** in Sessions and compact wherever that row is projected. Opening either surface or selecting session metadata reads/opens no Details automatically.

The [canonical resolver](../src/Kora.Application/Hosting/SessionWorkspaceService.Conversation.cs) admits only the actual exact-session retained record: immutable request/task/sequence, record revision, original generation/time and byte-exact USER body. [RetainedConversation](../src/Kora.Core/Presentation/AdmittedDetailContent.cs) is a distinct typed origin, not `FinalizedResponse`, history receipts or model authority.

The existing [viewer controller](../src/Kora/DetailWindowController.cs) renders inert plain text, with the separate typed HOST outcome/reason/task state in [native chrome](../src/Kora/DetailWindow.axaml.cs). The body SHA-256 and reference/revision stay immutable. Reopening deduplicates the same snapshot under native-text-v1's existing eight-viewer/source/search limits; conflicts never follow newer content.

Exact storage/session/source/private/control/owner checks run at resolution and final publication. Only explicitly opened records receive bounded passive source revalidation. Selection/source loss, session disposition/retention, privacy and disposal revoke content/search and fence delayed publication. Retained Done stays Done without Resume or meaningful activity renewal.

This route grants no clipboard/export authority: exact-source, selection and automatic copy remain blocked by [viewer state policy](../src/Kora.Application/Presentation/DetailViewerState.cs), including keyboard/programmatic paths. Existing document/history/web copy policy is unchanged. No model, tool, provider, egress, generated content, auto-offer or broader runtime/hardware/accessibility qualification is delivered.

### Native admitted-text literal Find

The existing Detail window now exposes **Find first / Next / Previous / Cancel Find / Clear literal Find** over only its frozen in-memory displayed text. Exact source and continuous semantic text remain distinct projections; switching source resets navigation. Retained original USER is unchanged, with a separate explicit **Find separate typed HOST observation** target for the already displayed native observation, never a combined or generated body.

[Viewer state](../src/Kora.Application/Presentation/DetailViewerState.cs) uses the canonical [SnapshotLiteralFind.FindCounted](../src/Kora.Core/Context/SnapshotLiteralFind.cs) policy shared by clipboard/local-file Find: case-sensitive unchanged spaces, nonempty well-formed one-line query, at most 256 UTF-16 characters / 512 strict UTF-8 bytes, complete nonoverlapping count and **no wrap**. Previous follows the same forward nonoverlapping sequence, not an overlapping alternative. Invalid queries are rejected visibly, not normalized or shortened. One inert bounded span, end-exclusive UTF-16 location and selected ordinal (for example, **Match 2 of 5**) are exposed. Direction exhaustion hides the match but retains its cursor and ordinal for reverse navigation.

Counts are volatile presentation state over only the exact currently admitted bounded reader text, or only the separate typed HOST observation when explicitly selected. They do not claim complete source, corpus or historical coverage and never combine USER, ASSISTANT, HOST, hidden metadata, source and rendered projections. A completed zero-match search exposes **0 matches**; edited, cancelled, invalid, unavailable or retired searches expose no count/ordinal. All native Details origins share this state contract and their existing Find admission; no unavailable profile is enabled. The count is computed on an explicit Find action, never a property read or idle refresh, and requires no new source read.

Query edits, projection/target changes, render/load completion, Cancel and Clear retire the cursor, count and ordinal; Cancel retains the query, Clear releases it. Close, disposal, source/revision replacement or failed private/source admission release all viewer text and fence publication/focus, including reentrant retirement. Focus failure also retires the result rather than claiming success. Find is synchronous bounded in-memory work with no additional reader, task, timer or cancellation resource. Native Ctrl+F/Enter/F3/Shift+F3 and Escape preserve query/match/close focus behavior; the inert match follows existing reader text scale. A separate native accessible count status exposes the displayed count/ordinal without duplicating it in the match or outcome names, and is not a live region announcing each keystroke.

Find adds no admission/open route, clipboard/export, persistence/schema, logging/query telemetry, provider/egress, meaningful activity or execution authority. Existing independently admitted copy controls are unchanged; retained conversation copy stays blocked. Portable and headless tests are implementation evidence, not installed/accessibility/hardware or full qualification.

The inert match viewport scrolls within a 100-DIP height bound without shortening text. Headless checks exercise the complete maximum-length match at 75%, 100% and 200% reader scale in the minimum-size window while retaining a visible reader; they do not replace installed visual/accessibility qualification.

## Delivered passive exact-conversation search - 2026-10-13

The selected Sessions conversation pane now separately searches original retained USER bodies and typed HOST outcomes/reasons/task states. It shares the chronological native inert row template; full exact body and original request/task/sequence remain data, not model answers, generated summaries, ranked citations, rich links or execution controls.

Queries/results are volatile private presentation. Existing admitted conversation pages, exact query-bound snapshot continuations and final source/private revalidation own disclosure. No clipboard/export/speech/egress, logs-as-body source, hidden model history or cross-session content search is introduced.
The older Question/Answer receipt `HistorySearch` and diagnostic evidence remain separate. See the [canonical search contract](Interaction_And_Sessions.md#delivered-passive-exact-conversation-lexical-search--2026-10-13).

## Delivered Native Current-File Literal Find - 2026-10-12

The shared exact local-file viewer adds explicit **Find first / Find next / Find previous / Clear literal Find** for its currently displayed immutable file, captured-folder item or revalidated retained Session attachment. This is case-sensitive, unchanged one-line literal navigation, nonoverlapping and without wrap, not ranked retrieval or a model query.

Only one exact bounded match is focused in the existing inert `NamedTextBlock` panel, with distinct literal identity/end-exclusive UTF-16 location. Complete original text is unchanged; no selectable source `TextBox`, clipboard, export, link, speech or external rendering path is introduced. Query/source/view retirement suppresses late disclosure and private closure clears native text.

See [semantics, private source admission and maintained evidence](File_And_Folder_Ingestion.md#delivered-native-current-source-literal-find---2026-10-12). Existing lexical/Markdown retrieval and citation limits are unchanged; native fixtures do not qualify installed/accessibility, model grounding or broader display/source authority.

## Delivered Preparation-Only Local Search - 2026-10-20

Original typed/current-name ACTIVATED `prepare file search for "<query>"` / `prepare folder search for "<query>"` pre-populates the existing exact volatile inspection window, not this viewer's Literal Find or a retained attachment.
It resets run-only lexical mode and clears old citations, with **PREPARED / REVIEW NEEDED** and no search/result claim ([native hook](../src/Kora/LocalFilePreviewWindow.PreparedSearch.cs)).

Raw final-quoted input preserves query characters. The scoped handoff freezes the admitted immutable source and rechecks queued admission; stale, closed, replaced or wrong-kind sources refuse. Only a separate native Search gesture executes the existing LocalUi-only audited path.
No read/search/refresh/metadata capture, provider, clipboard, persistence, Attach or consent authority is added ([owning contract](File_And_Folder_Ingestion.md#delivered-preparation-only-native-lexical-query---2026-10-20)).

## Delivered Immutable Volatile Web-Result Details - 2026-10-10

An actual successful original-user `get web page <URI>` result now offers **Open exact web-result details** in the compact response. It opens or activates the same immutable host-issued item/revision in the existing native-text-v1 viewer.
Opening, reopening, searching, source inspection and closing make no retrieval request, navigate no URI and change no task, grant, question, approval or session target.

The new `RetrievedWebResult` classification is an **untrusted external observation**, not EmbeddedDocument, FinalizedResponse or SessionHistory. The host captures the original admitted request/session/task/origin and privacy/control generation.
These are volatile host-request correlation, not a fabricated durable workspace admission. URLs, incoming trace headers and serialized result/model JSON cannot resolve or mint the current snapshot.

Complete inert typed provenance is included in bounded source: requested/final URI, media type, redirect count, retrieval time, truncation flag, exact returned normalized text SHA-256/UTF-8 byte count and limits.
The digest identifies returned normalized text, never original HTML/body, server identity or a fresh/live URI. Native chrome uses fixed labels of at most 256 characters; long identities remain complete in source, not abbreviated into authority.

The unchanged retrieval bounds are 256 KiB raw content, 60 KiB normalized text, five redirects and the existing 64 KiB serialized tool-result envelope.
Metadata plus exact text must fit the native profile's complete 256 KiB UTF-8 source bound; details fail explicitly rather than shortening any field or text. Successful empty returned text remains empty and is eligible through honest provenance, without invented content.

Only one current volatile web snapshot is retained. Replacing/dismissing/closing the response, cancellation, disposal, privacy/ownership/call/control change or failed source admission retires its reference and clears associated viewers/search/copy targets.
A removed old control cannot retarget the latest result. Closing only the detail viewer permits explicit reopening of the same still-current source. Eight native viewers remain the shared limit. Retirement also clears the compact response's matching web body, never an unrelated later response or required question/card.

Rendering is strictly inert plain text: no HTML, active links/images/scripts/styles, resource loading, browser, autodownload or further network. Private source/selection copy retains explicit native disclosure confirmation and fresh admission to that original snapshot.
Fetch/details create no persistence, export, speech/caption, diagnostic content, model context or provider transmission. Explicit local summary below is not an implicit consequence of retrieval or viewing. Existing session-history copy and passive retention stay separate ([fetch](../src/Kora.Application/ViewModels/MainViewModel.WebPages.cs)).

Maintained Core/Application/native fake and headless-binding tests cover provenance/digests/empty/truncation/bounds, actual response-to-viewer reuse, zero additional transport calls, retired controls and private/late/native-failure paths.
They do not qualify installed visual/accessibility/DPI, transport security, durable conversation/knowledge, general details or the unavailable parameterized model tool loop.

### Explicit Local Summary Of One Current Web Snapshot

`summarize current web result`, optionally prefixed by the current assistant name, and the exact-source native button prepare a review, not a model call. Confirmation names its one-use ID or uses its native control. Retrieval consent is not model source-use consent ([parser](../src/Kora.Application/Network/WebSummaryCommand.cs), [workflow](../src/Kora.Application/Network/WebSummaryWorkflow.cs)).

The [web action](../src/Kora.Tools/Network/WebResultSummarize.cs) preserves complete normalized text and inert provenance as LocalOnly evidence and refuses complete serialized envelopes above 32,768 UTF-8 bytes. Its shared credential screening is narrow reject-only assistance, not comprehensive sensitivity detection or source-use approval. Addresses confer no authority.

The [view model](../src/Kora.Application/ViewModels/MainViewModel.WebSummary.cs) preserves the original response/details and displays admitted model text separately with fixed attribution. It adds no automatic inference, retrieval, speech, history, hosted fallback or model action. Source/reference, response/control/presentation, privacy/recovery/call epochs and review expiry fence late publication.

The [review](../src/Kora.Application/Network/WebSummaryReview.cs) discloses the exact fresh `Local context` session and source-free receipt retention. Confirmation preserves the original-input channel; it cannot silently select or retarget another session ([target](../src/Kora.Core/Dependencies/LocalContextSessionTarget.cs)).

The [web bridge](../src/Kora.Application/Network/WebSummaryModelExecution.cs) consumes the shared [Core runner](../src/Kora.Core/Dependencies/IModelContextRunner.cs). Review stores no envelope. A distinct exact target receives durable admission before evidence is bound to its [lease](../src/Kora.Core/Dependencies/IAdmittedModelContextTurn.cs), never to the volatile fetch/source request.

The [shared workflow](../src/Kora.Application/Dependencies/LocalContextTurnWorkflow.cs) owns source-free durable receipts and quiescence. The [web action](../src/Kora.Tools/Network/WebResultSummarize.cs) owns credential screening and complete-envelope construction, not sensitivity approval. The local-only envelope disables tools; shared source-current checks fence dispatch and late return.

[IWebResultModelUsePolicy](../src/Kora.Core/Network/IWebResultModelUsePolicy.cs) supplies a separate affirmative source-specific sensitivity/use decision over the complete exact snapshot. Retrieval consent, user confirmation, LocalOnly classification and narrow credential heuristics cannot replace it. Unknown or denied decisions prevent context/provider dispatch.

No production web-source sensitivity/use service is delivered. The composed [unavailable policy](../src/Kora.Application/Network/UnavailableWebResultModelUsePolicy.cs) returns Unavailable before durable admission or envelope construction, including for ordinary prose without recognized credentials. This is an incomplete production source-policy requirement, independent of model qualification.

The [web bridge](../src/Kora.Application/Network/WebSummaryModelExecution.cs) rechecks policy after asynchronous admission, through the shared execution validator and before late presentation. A denied/unknown observation permanently closes that use; later allowance cannot revive it. No retry, fallback, source rewrite or secret-free claim is introduced.

Unqualified production registrations stay unavailable; denied/uncertain results disclose no answer or fallback ([host](../src/Kora.Application/Dependencies/ModelTurnHost.cs), [bridge](../src/Kora.Application/Network/WebSummaryModelExecution.cs)).

The [protocol](../src/Kora.Core/Dependencies/ModelToolProtocol.Context.cs) and [native framing](../src/Kora.Windows/Dependencies/OllamaToolWire.cs) preserve `ModelToolMode.None`. Tool requests are denied before registry execution. Fake success is not provider qualification.

[Push-to-talk](../src/Kora.Application/ViewModels/MainViewModel.VoicePrivacy.cs) reports capture status without replacing an existing exact web response. Activated summary and confirmation can therefore name that same current snapshot; other response replacement still retires it ([voice/source tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.WebSummaryBoundaries.cs)).

[Tool tests](../tests/Kora.Tools.UnitTests/Network/WebResultSummarizeTests.cs), [workflow tests](../tests/Kora.Application.UnitTests/Network/WebSummaryWorkflowTests.cs) and [presentation tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.WebSummary.cs) use deterministic sources/adapters.

[Lifecycle](../tests/Kora.Application.UnitTests/Dependencies/LocalContextTurnWorkflowTests.WebSummary.cs), [tool denial](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.WebSummary.cs) and [native binding](../tests/Kora.Windows.IntegrationTests/WebResultDetailsBindingTests.cs) cover production seams. They do not qualify providers, offline/containment or summary quality.

[Lease-first web tests](../tests/Kora.Application.UnitTests/Network/WebSummaryModelExecutionTests.cs) prove evidence is bound only after admitted target resolution, foreign leases receive no source, and overflow/cancellation/source closure disposes the lease without model dispatch.

[Policy tests](../tests/Kora.Application.UnitTests/Network/WebSummaryModelExecutionTests.cs) use explicit allowed/denied/unavailable fakes; [shared tests](../tests/Kora.Application.UnitTests/Dependencies/LocalContextTurnWorkflowTests.WebSummary.cs) exercise production unavailability and policy revocation. Fake success demonstrates workflow behavior, not real source classification.

## Delivered Exact Native Provider-Handoff Review - 2026-10-10

The [native handoff window](../src/Kora/ModelHandoffWindow.axaml) displays actual audited host-issued offers: original session/task/request/origin, question/offer identities/revisions, generation/control/policy/destination/catalogue revisions, typed reason and validity. Production shows unavailable/no pending qualified offer.

The complete exact context/evidence/tool envelope retains the existing 32,768-byte/sixteen-evidence bounds. It is inert non-selectable text with provenance and typed disclosure labels, never Markdown/HTML, authority-bearing display text or fabricated samples.

No copy/export, speech, content logging/persistence, link, provider execution or send controls exist. Approval has no default selection and confirms only exact context. Selected evidence removal retires the old offer/question and requires a fresh complete reduced-envelope read; refresh cannot inherit approval.

Application owns cancellation, one-use and late-response suppression. Close/privacy/ownership/expiry and changed session/task/policy/control clear content rather than retargeting it. Unrelated questions/viewers remain independent. Installed native and provider acceptance stay open ([boundary](Model_Providers_Memory_And_Knowledge.md#delivered-bounded-exact-native-handoff-review---2026-10-10)).

## Delivered Immutable Selected-Session Receipt Details - 2026-10-09

**Open immutable receipt details** reuses the bounded native-text-v1 viewer
for an exact persisted host history event from the selected session's bounded
history page. There is no authoritative persisted artifact-body source; this
surface truthfully displays the existing complete typed receipt as inert plain
text, not reconstructed model output, file/skill content or an artifact body.
The Application resolver freshly reads the exact session/event under unchanged
private ownership admission; missing/foreign/corrupt/cancelled/over-budget or
changed-admission results are explicit failures. Availability, baseline/gap,
generation, source/provenance/audit metadata are not elevated into authority.

Event ID/sequence supplies the immutable reference. Native chrome includes its
retained session and source digest. Reopening activates the exact existing
viewer; conflicting content is never substituted. Private copy requires the
existing explicit disclosure confirmation and fresh ownership/privacy gate;
native search/source/close reuse the same passive services. The source remains
bounded to the existing complete 64 KiB history-result limit.

Reads do not disable focused history controls or change selected work/pending
questions, activity, priority, lifecycle, approval, execution or voice targets.
An explicit details open may activate its owned viewer, never a work session.
Native logical disposition retires owned viewers before its transaction;
automatic retention revokes them before inventoried deletion. Privacy closure
clears content/selection/search and stale copy targets. A later exact read
returns only actually retained redacted receipts after logical disposition.
Inventoried retention purges old event IDs (explicit unavailable), retaining
only a content-free redacted gap/tombstone. Restart performs no replay.

The delivered lexical file citation surface, volatile file/clipboard previews,
skill review, queue/status and diagnostic evidence are reused separately, not
duplicated or persisted here. Artifact bodies, general finalized-response/
script/diff routing, conversation composer, model history reasoning,
Ask Evidence, broad export and installed accessibility/DPI acceptance remain
gated. See [the roadmap/evidence assessment](Archive/Implementation_Roadmap_2026-10-11.md#r14-immutable-selected-session-receipt-details---2026-10-09).

## Delivered Bounded Passive Session History - 2026-10-09

The [coordinated work increment](UI_Workspace_And_Windows.md#delivered-authoritative-sessions-work-surface---2026-10-09)
places authoritative work and exactly bound pending-question metadata beside
this separate history reader. Reading history/evidence never replaces those
cards or changes their reply target. Stable work IDs/revisions, FIFO order,
observed deadlines/capacity and explicit eligibility/gap states come from a
single bounded authority snapshot, not logs or rendered detail text. Separate
immutable/volatile viewers remain uncomposed as queue content.

The native Sessions window now offers **Read exact history** and **Next history
snapshot page**, with an explicit immutable-session-ID field and accessible
button/field names. The same Application service provides exact typed/activated
`session history` and `session get` routes. A session name, selected window,
diagnostic record, trace ID or caption never resolves a history subject.
This inert native display has no reply, approval, attachment, playback, model,
export or replay action. Browsing changes neither focus/voice targets nor
meaningful activity, lifecycle or current approval/question state.

The single [typed history contract](Interaction_And_Sessions.md#delivered-bounded-ordered-interaction-history---2026-10-09)
owns host-committed question/final-answer, decision metadata and task-state
receipt projections, stable exact citations and session-local sequence.
The default 25/max 50-record, complete 64 KiB result preserves one exact
generation/sequence snapshot across pages and restart; later appends are excluded.
Changed lifecycle, unknown IDs/cross-session cursors, missing/corrupt storage
and privacy loss fail closed. Oversized content is explicitly unavailable;
metadata-only, baseline/gap and disposition-redacted records are not inferred
responses. Done sessions remain readable; Removed sessions allow only exact
redacted-citation inspection, not live-list membership or resume.

Bootstrap messages/response bodies were not admitted to this durable store and
remain unavailable. Shared-profile skill content and local file/clipboard
previews remain volatile inspections. No history content enters diagnostics,
activity tags or model context; only fresh admitted actual playback can create
captions. This is partial R12/R14 delivery, not a full conversation composer,
model history reasoning or Ask Evidence. The separately delivered fixed
queue/native work surface is not a general scheduler. Real installed
visual/screen-reader/DPI acceptance remains open.

Passive exact-session history search now uses the existing receipt list and detail action. The [search workflow](Interaction_And_Sessions.md#delivered-passive-exact-session-lexical-history-search---2026-10-09) consumes only delivered committed fields and preserves generation/query-bound pagination, exact provenance, gaps and complete byte bounds. Native selection still freshly resolves the immutable event through the existing detail service. Search text/results are volatile, cleared on query/subject/privacy retirement, and never become reply, model, replay or execution authority ([native state](../src/Kora/SessionsViewModel.Search.cs)).

## Delivered Exact Host-Record Review - 2026-10-07

### Delivered R07 Immutable Plain-Text Clipboard Preview

The explicit clipboard route opens a separate native inert-text window, not a
Markdown/rich renderer or a host authorization card. It shows the exact
snapshot, host source/snapshot IDs, `CF_UNICODETEXT` format, read version/time
and strict UTF-8 byte count. No copy/cut/paste/export, links, interpretation
or automatic refresh occurs. **Reuse this exact snapshot ID** selects only the
same immutable item; **Revoke and clear** and window close release it.
Host privacy/ownership/call-generation loss and cancellation retire content and
late callbacks. Preview is not a context egress grant or inference result.
See [the R07 security boundary](Security_Data_Flows.md#delivered-r07-local-clipboard-preview---2026-10-07);
full clipboard answering and real native acceptance remain unavailable/open.

### Delivered Bounded Native Evidence Inspection

#### Exact native Evidence trace preparation

Original typed/current-name ACTIVATED `prepare evidence trace <trace-id>` uses a
[dedicated parser](../src/Kora.Core/Commands/EvidenceTraceCommand.cs), the
authoritative `TraceSnapshot.IsValidW3CId(value, 32)` validator and an
[Application-only handoff](../src/Kora.Application/ViewModels/MainViewModel.EvidenceTrace.cs).
The original value is exactly 32 nonzero lowercase ASCII hex characters, never
trimmed, repaired, case-folded or inferred from GUIDs, spans, headers or links.
The reserved malformed family is handled before question/model routing.
`evidence trace help` is constant syntax, never current trace/source metadata.

The [existing singleton controller](../src/Kora/EvidenceWindowController.cs)
queues a native-only open/focus and
[exact filter preparation](../src/Kora/EvidenceWindow.axaml.cs), requiring native
focus and exact original value readback before success. Original input, assistant
name, host/privacy, ownership binding/revision, call policy, activated voice
generation, native controller/window, selected source and all filter/control
revisions remain fenced through dispatcher opening and preparation. Unbound,
disposed, denied, changed or unconfirmed presentation is explicitly unavailable,
not a latest/unfiltered or alternative-source fallback.

Only `TraceFilter` changes; **all other filters and the selected source remain
unchanged and AND-composed**. New-window defaults are unchanged. The
[same no-read filter invalidation](../src/Kora/EvidenceViewModel.Filters.cs)
retires rows, selection/details, Find, actions, cursors and pending pages, even
when preparing the same trace value. Late pages and old segment opens cannot
restore retired content. Separate explicit Search / refresh and Next alone use
the existing bounded retained reader under host-resolved admission; signed
continuations, full-record validation before exclusion, budgets, privacy and
cancellation are unchanged.

No read/probe/count/scan, retention/cleanup/export, source capture, session
selection, permission/approval response, audit promotion, model/provider/network,
clipboard/persistence or meaningful activity renewal is introduced. Trace
correlation remains untrusted diagnostic data, not Kora session/task/user
identity, ownership or authority. It never selects AuthorityAudit or supplies
an Activity parent, baggage, host session or current context. Existing admitted
content-free host correlation remains separate from the trace-filter value.

The consumed **I - Implementation** subset is the existing native Evidence
singleton, exact trace validator, filter retirement and bounded retained reader.
[Deterministic preparation tests](../tests/Kora.Windows.IntegrationTests/EvidenceTracePreparationTests.cs)
cover no-read native acknowledgment, preserved filters/source and retired/late
state. Portable parser/Application tests, maintained Release/analyzers and raw
100% portable line **and** branch coverage remain required for readiness.
Unrelated E/Q, providers, hardware, installed accessibility and retired
experiments are not implementation prerequisites or newly qualified here.

**Find first / Find next / Find previous / Clear literal Find** inspect only
the exact immutable serialized `ResultText` page already returned by an explicit
successful Search, Next, trace or segment read. They do not query a retained
source, reopen storage, expand omitted bodies, change filters/cursors or select
a different cited record/segment. Available complete pages alone enable Find;
failed, unsupported, unavailable, partial, scan-limited, changed, timed-out or
content-omitted results do not. All's disclosed unavailable Session/Conversation
sources remain unavailable, not searchable history.

The authoritative [SnapshotLiteralFind](../src/Kora.Core/Context/SnapshotLiteralFind.cs)
policy preserves exact case, spaces and serialized escapes, with a nonempty
well-formed one-line query bounded to 256 UTF-16 characters / 512 UTF-8 bytes.
Controls, Unicode format/line/paragraph separators and malformed/over-limit
queries are rejected visibly without truncation, normalization or regex.
One nonoverlapping match is presented as a focused inert native literal
selection with its end-exclusive UTF-16 location; the original serialized
page is unchanged. Direction ends never wrap or claim global absence; no-match
means only absence in that page, not retained-source absence or an all-clear.
Ctrl+F focuses the query, Enter finds first, F3/Shift+F3 moves next/previous.

The query is volatile presentation state: no persistence, content logging/audit,
clipboard transfer/export, speech, model context or authority. Query edits
retire the match; Clear retires query/match without reading. Every evidence read,
page replacement, basic/advanced filter or source edit, cancellation/failure,
private ownership revocation and close retires query/match/selection. Admission
and presentation revision checks suppress late publication and focus.
[Evidence Find tests](../tests/Kora.Windows.IntegrationTests/EvidenceLiteralFindTests.cs)
exercise native/headless behavior, not installed accessibility or full
qualification. General Ask Evidence, source search beyond existing filters,
copy/export and new audit authority remain unavailable.

The confirmed device-local [reader text scale](#bounded-native-reader-text-scale)
also applies to only the admitted complete immutable serialized page body and
its focused inert literal-Find span. New windows and live confirmed changes
use each control's original font size/line height, not cumulative scaling.
Filters, result navigation, provenance labels, status/errors, controls,
accessible names and window dimensions retain their typography. Incomplete
pages retain baseline typography; replacement, source/private closure,
failure/cancellation and close retire scaled match/content state, including
late callbacks. Unavailable preference evidence explicitly retires the reader,
never silently substitutes 100. Serialization bytes, content-owned line breaks,
UTF-16 offsets, record selection, continuations and evidence reads are unchanged.
This adds no content, copy/export, persistence or activity authority and makes
no installed accessibility claim.

**AuthorityAudit** is a separate opt-in source for the real schema-v3
interaction store's committed typed `security_audit_events`. It is not the
SQLite diagnostic **Audit** projection or a daily audit mirror. **All** remains
SQLite evidence-source-only and **CombinedLog** remains ordinary diagnostics
only. No sources are merged, deduplicated or promoted by text, trace IDs or a
`SecurityAudit` property. Source-qualified `kora-evidence:authorityaudit:...`
citations identify the committed audit correlation ID within that source.

The existing native inspector and complete 50-record/64-KiB page serializer
show the original typed event/outcome/request/session/task/approval metadata,
schema/table/sequence/commit digest, recorded trace/span IDs, intent revision,
session generation, question/grant revision and typed change references/digests.
These references are not historical payload reconstruction or a file graph.
Only events actually committed by this interaction store are included:
configuration/effect audits using other stores or the typed logging path are
not imported or promoted, and the task ledger is not a fabricated audit stream.
Only trace/span IDs were committed by this schema; diagnostic span metadata,
parentage and links are not invented. Trace inspection remains in this source.
If a single change-reference payload exceeds the complete page byte budget,
`ContentOmitted` explicitly suppresses its change list while retaining the
commit/source/outcome/revision metadata; no truncated trusted list is claimed.

Reads use the initialized current store's shared lease and read-only connection,
never its writing/migration API or the retired task ledger. Sequence ordering
provides deterministic ties even when commit times are equal or go backwards.
Each request scans at most 4,096 audit rows under existing five-second storage
admission/query limits. The signed query/session-bound 15-minute continuation
retains the original sequence/digest ceiling and native file/host-lifetime
identity; appends do not expand it. Missing/inconsistent rows, obsolete schema,
replacement, corruption, expired cursors and denied access are explicit errors,
not an empty success or silent restart. Audit reads never prune or write.
This is passive observation, not task/answer/grant admission, session activity
or proof that an effect occurred. Local hash consistency is not forensic tamper
resistance or an externally anchored checkpoint; files remain unencrypted and
user-modifiable. Native installed/accessibility acceptance remains open.

The tray's **Evidence (read-only)** entry opens a separate, non-topmost native
window using the existing theme resources. It is not the clipboard-capable
passive detail viewer. Source, safe-text, session/task GUID and W3C trace/span
filters provide deterministic current-user SQLite inspection without a model,
network, microphone or browser. Search starts a fresh bounded snapshot;
Next page continues only that snapshot. Selecting a cited record exposes its
parent/explicit link availability; Read selected trace and Open selected
segment use the same typed query service, never paths or SQL.

The basic **Span ID** field beside **Trace ID** binds the existing optional
exact `EvidenceQuery.SpanId`. Only empty input means Any. Nonempty input must
pass the existing portable W3C validator: exactly 16 lowercase hexadecimal
characters, not all zero. No trimming, case folding, prefix/partial matching,
repair or reinterpretation occurs. Span ID composes with every existing
source/text/identity/time/typed filter using AND; it is correlation only,
never identity, permission or audit authority. Link records retain the existing
source-or-target pair semantics: when both W3C IDs are specified, both must
match the same end, not separate ends.
Zero matches disclose only absence within the admitted filtered snapshot.
Editing or emptying the field performs no read and immediately retires the
page, selection, continuation and literal-Find state, including late callbacks.
**Clear advanced filters** preserves both basic W3C fields; empty either
field explicitly to unset it. Closing clears both. No filter-value logging,
persistence, export, clipboard, model/tool/speech route, schema/index/cache,
trace navigation, activity renewal or generated content is added. Complete
bounded record validation, private/source/authority fences, authenticated
snapshots/continuations and explicit content omission remain unchanged.

The optional **Advanced filters** area adds exact request, invocation, approval
and audit-correlation GUIDs, inclusive **From / Until** timestamps, diagnostic
severity and typed security-audit outcome. Blank fields are unset; GUIDs must
be nonempty hyphenated values without surrounding whitespace. ISO timestamps
require `Z` or an explicit offset and normalize to UTC; both edges include the
boundary instant. SQLite and AuthorityAudit compare commit time; DailyLog
compares observation time, and CombinedLog preserves those independent time
semantics. Correlation on an ordinary diagnostic is not a committed audit.
Severity requires All, Log, Audit, DailyLog or CombinedLog; audit outcome
requires All, Audit or AuthorityAudit. Unsupported choices fail visibly, not
as empty success. **Clear advanced filters** unsets the optional fields/choices
without reading. Malformed values, invalid ranges and undefined enum values
never fall back to an unfiltered read.

Optional **Retained due UTC from / until** fields filter `EvidenceRecord.DueUtc`
independently of committed/observed-time bounds; all predicates compose with AND
in the [authoritative metadata query](../src/Kora.Core/Diagnostics/EvidenceQuery.cs).
Edges are inclusive. Only empty means Any; the shared
[OptionalCanonicalUtc parser](../src/Kora.Core/Storage/OptionalCanonicalUtc.cs)
requires exact `yyyy-MM-ddTHH:mm:ss.fffffff+00:00`. Whitespace, nonzero offsets,
`Z`, noncanonical fractions, overflow and reversed ranges fail, never normalize.
Missing DueUtc is UNKNOWN and never matches a constrained due range; DailyLog
has no retained due deadline, including its independent part of CombinedLog.

Due is retained deadline metadata, not proof of physical deletion, current
expiry, policy, authority or retention permission. Complete source, record and
provenance validation precedes exclusion; scan/page-byte/time limits and private,
source, cancellation and snapshot fences remain unchanged. Both bounds bind the
versioned complete semantic digest and authenticated continuation
([admission](../src/Kora.Application/Diagnostics/DurableEvidenceQuery.cs)).
The [labelled native fields](../src/Kora/EvidenceWindow.axaml) use existing no-read
edit/Clear/Escape retirement of rows, selection/detail, cursor and stale callbacks
([headless tests](../tests/Kora.Windows.IntegrationTests/EvidenceLiteralFindTests.Due.cs)).
No body/path read, schema, retention write/prune/expiry/lifecycle or log capture
change is added; this is bounded I delivery, not E/Q or provider enablement.

Advanced filters also expose exact **Parent Span ID**, **Host operation outcome**
and **Content availability**. Only an empty parent span field means Any;
nonempty input uses the same portable W3C validator as Trace/Span IDs (16
lowercase hexadecimal characters, not all zero), without repair or case folding.
Parent matching uses only the record's retained `Trace.ParentSpanId`, not linked
targets or invented authority provenance. Outcome offers a visible accessible
**Any** plus every defined `HostOperationOutcome`, matching `EvidenceRecord.Outcome`
exactly; null outcomes match only Any, never an audit or interaction outcome.
Content availability is an explicit Any/Present/Omitted discriminator:
Present means `ContentOmitted == false`, Omitted means `true`. It does not infer
body existence from text, retention or source, restore content, or prove integrity.
These predicates AND with every existing filter, including Trace and Span IDs,
after full bounded envelope/provenance validation. Missing metadata cannot match
an exact predicate. A Present record exceeding the complete page byte budget
fails explicitly rather than silently changing to Omitted; Any retains existing
budget omission behavior. Empty results are only absence in this bounded snapshot.
All three predicates bind the authenticated query digest/continuation and retain
source, private, retention and snapshot fences. Editing reads nothing and cancels
and retires page, selection, continuation and late callbacks; Clear advanced and
close reset all three to Any. They are volatile correlation/presentation metadata,
never identity, authority or evidence-integrity claims. No new navigation, body
restoration, export/clipboard, persistence, filter logging/tags/audit payload,
schema/index/cache, model/tool/speech route or activity renewal is added.

**Observed trace parent** adds visible accessible **Any / Root / Child** choices.
Root (`ObservedRoot`) requires a valid retained trace with null `ParentSpanId`;
Child (`ObservedChild`) requires a valid retained nonnull parent ID. A null trace
is unknown and matches neither constrained choice. Full `TraceSnapshot`
validation precedes exclusion in the [shared metadata predicate](../src/Kora.Core/Diagnostics/EvidenceQuery.cs).
The choice AND-composes with exact parent ID and every existing filter; Root
plus an exact parent ID therefore returns no matches, not a repaired query.
It binds the versioned complete semantic query digest and authenticated
continuation in [query admission](../src/Kora.Application/Diagnostics/DurableEvidenceQuery.cs).
Existing scan/result/byte limits, source/privacy/cancellation/retention fences
remain unchanged. Native edits, Clear advanced and Escape read nothing and
retire rows, selection/detail, cursor and stale callbacks
([native filters](../src/Kora/EvidenceViewModel.Filters.cs), [headless tests](../tests/Kora.Windows.IntegrationTests/EvidenceLiteralFindTests.Metadata.cs)).
These are observed diagnostic correlation only, not complete traces, causality,
current session, identity, authority, approval or ownership. No schema, trace
tags/baggage, read authority, device/provider/model or runtime qualification is added.

Advanced trace metadata filters add exact **Trace source**, **Activity operation
name**, **ActivityKind**, and **Trace flags**, plus **Trace source version**
with explicit **Any / Absent / Present / Exact** semantics. Empty source/name
means Any. Exact text is ordinal case-sensitive, at most 128 UTF-16 characters,
valid Unicode without surrounding whitespace or control characters; nothing is
trimmed, repaired, case-folded or partially matched. Version Exact requires
nonempty valid text; the other three choices require empty version text.
Absent means a retained trace whose source version is null, Present means a
retained non-null valid version, and Any imposes no version predicate. A null
trace is unknown, not an absent source version, and never matches any set trace
predicate. Kind and flags have visible non-null Any choices plus every defined
value; flags are exact None or Recorded, not a bit-inclusion test.
Complete `TraceSnapshot` validation (W3C IDs, optional parent, admitted source,
optional version, operation name, defined kind and flags) precedes exclusion,
even for nonmatching records. The existing four-source capture allowlist stays
unchanged; unknown source predicates are valid searches but cannot invent data.
These predicates AND with every existing filter on every source, bind the
signed query fingerprint and continuation, and reset to Any with Clear advanced
or close. AuthorityAudit has only its committed trace/span provenance, not a
fabricated `TraceSnapshot`; it cannot match these new predicates.
Trace metadata is untrusted diagnostic correlation only: it cannot select a
session or establish identity, intent, permission, approval or authority.
No baggage, tags, user content, paths, credentials or omitted content is exposed,
searched or recovered by these predicates. No filter logging/persistence,
schema/index/cache, navigation, model/provider/device/network or experiment path
is added. Portable and headless/real-reader deterministic checks cover this I
subset; installed accessibility and E/Q qualification remain separate.

Every basic/advanced filter edit clears previous content, selection and cursor,
and cancels an in-flight read; late content cannot restore the retired query.
Search explicitly creates a fresh snapshot; Next uses the same immutable typed
query and signed continuation. Trace/segment reads retain their existing
source-specific navigation semantics rather than inheriting unrelated advanced
search restrictions. The [native filter/parser](../src/Kora/EvidenceViewModel.Filters.cs),
[query validation](../src/Kora.Application/Diagnostics/DurableEvidenceQuery.cs)
and [native/real-reader tests](../tests/Kora.Windows.IntegrationTests/EvidenceAdvancedFilterTests.cs)
are the maintained evidence for this deterministic filtering slice, not
installed/native accessibility, model reasoning or release qualification.

The native form also consumes the existing `EvidenceQuery.EventId`, `Category` and `ActionId`, not new storage or result-text predicates. Empty text alone unsets these fields; whitespace-only/surrounding whitespace, controls, malformed Unicode, over-limit text and noncanonical/overflow integers fail.
Event ID uses canonical invariant signed Int32 decimal with zero/negative/boundary values; Category/ActionId retain 256/128-character limits and exact ordinal text, without domain action-name constraints
([query contract](../src/Kora.Core/Diagnostics/EvidenceQuery.cs), [parser](../src/Kora/EvidenceViewModel.Filters.cs), [admission](../src/Kora.Application/Diagnostics/DurableEvidenceQuery.cs)).

Optional exact **Event name** (128 characters) and **Exception type** (256)
match only retained diagnostic metadata with ordinal case-sensitive comparison.
Empty native fields mean Any; nonempty values require bounded plain valid Unicode
without surrounding whitespace or control characters. No trimming, case folding,
substring matching, exception messages, stack traces, sensitive properties or
omitted content are introduced. These are not activity names or authority fields.
Optional **Audit sequence from/until** are inclusive positive Int64 bounds,
canonical invariant decimal in the native controls, ordered when both are set.
Either edge may be unset; null sequence never matches a set edge. Sequence is
committed ordering metadata only, not authority, completeness, freshness, replay
position or permission. Existing source support is unchanged: missing metadata
does not match, including sequence on ordinary/daily logs and names on activity
or authority records.

All four predicates use the authoritative retained-metadata matcher, AND-compose
with every existing search filter, and bind the serialized authenticated query
fingerprint. Full record validation precedes exclusion. Existing scan/page budgets,
explicit Present over-budget failure, privacy/cancellation and stale-result fences
remain unchanged. Edits retire results, selection and continuation without reads;
Clear advanced and close reset to accessible empty/Any. The advanced form scrolls
within a bounded height rather than pushing the result controls out of the window.
Navigation does not inherit
search-only predicates. No schema/index, filter persistence/logging, export,
content restoration, provider or execution path is added. Deterministic portable,
real-reader and headless-control checks are I evidence; installed accessibility
and E/Q qualification remain separate.

Category projects diagnostic category on Log/Audit/DailyLog, activity source on Span/Link, and typed audit-category name on AuthorityAudit. Event ID supports All/Log/Audit/DailyLog/CombinedLog; ActionId supports All/Audit/AuthorityAudit.
All remains SQLite-only; CombinedLog rejects ActionId because DailyLog cannot match it. Category rejects unavailable Session/Conversation; a citation under All narrows support to that source. Unsupported combinations fail before reading via existing native recovery
([SQLite](../src/Kora.Windows/Storage/WindowsSqliteEvidenceReader.cs), [Daily](../src/Kora.Windows/Storage/WindowsDailyEvidenceReader.cs), [Combined](../src/Kora.Windows/Storage/WindowsEvidenceReader.cs),
[AuthorityAudit](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.Inspection.cs), [source-admission tests](../tests/Kora.Application.UnitTests/Diagnostics/DurableEvidenceQueryTests.cs)).

The complete compact serialized page, including correlation, typed values,
stable citations and disclosure, is limited to 50 records / 64 KiB UTF-8.
Over-budget individual content is explicitly marked `ContentOmitted`, not
silently shortened. Missing records/trace segments, expired-but-present
records, unavailable sources and storage/access/validation errors are visible.
Due dates do not prove physical removal; absent segments may never have been
recorded. The query is a diagnostic projection, not the atomic interaction
audit or proof of authorization/effect. This evidence source is not session/conversation history; the separately
admitted bounded history source above is never synthesized from diagnostics.

The source selector also admits **DailyLog**, independently of SQLite.
**All** remains SQLite-only; daily mirrors do not alter its counts. Daily
records use independent file/offset/digest citations, retain their original
envelope evidence ID and typed observation/correlation fields, and have no
database commit/due time (`RetentionUnknown`). **Read selected trace** stays
within DailyLog; span/parent/link records are unavailable there. Audit mirrors
are unsupported, not promoted to database audit records. The serialized
`DailyReport` labels the snapshot, scanned files/bytes/lines, unsupported copies,
audit mirrors and ingestion gaps; skipped copies produce `Partial`.

Daily reads admit at most 32 files / 8 MiB of earliest complete-line prefix /
4,096 physical lines / 256 KiB per line (excluding LF) and five seconds per
request. The existing 50-record/64-KiB output limit still applies. Next page
uses one of eight bounded 15-minute host-held manifests; eviction requires a
fresh search. Active appends/new daily files do not expand it. Source changes,
replacement, pruning/rotation, corruption, incomplete final lines, expired
snapshots, timeout and access failure are visible. `ScanLimitReached` is not a
complete-file claim and does not imply paging beyond the admitted prefix.
No combined cross-source ranking, file audit/graph, export or Ask Evidence is
delivered by this slice; native installed/accessibility acceptance remains open.

Controls have native accessible labels and keyboard navigation. Result text
is inert and non-selectable. Copy/cut clipboard paths and context menus are
blocked; there are no export, Ask Evidence, execution or source-deletion
controls. Ownership/privacy is checked before and after the read; privacy
closure cancels the viewer and clears its page/filter state. Reading does not
refresh retention, consume grants, change runtime admission or resume work.
Automated real-store/view-model/XAML tests are not native visual, screen-reader,
DPI, contrast, installed or power-loss acceptance; those trials remain open.

The [bounded question window](UI_Workspace_And_Windows.md#delivered-bounded-question-window)
has an explicit **Review exact record** route through the audited
[review service](../src/Kora.Application/Interaction/HostQuestionReviewService.cs).
It resolves the original exact question key against live committed
intent/session/generation/policy/proposal, then displays that immutable
question/proposal snapshot as passive plain text. All actual host binding
digests and identities are shown, not a generated operation summary. No
operation/script bytes are invented: the current contracts do not supply
them, and the window states that limitation.

The record retains its original request origin; UI confirmation cannot
launder protected voice-origin requests. Approval requires the complete
current record to have been displayed and uses the authorization service.
Neither review nor a native checkbox supplies missing source, containment,
deployment or mandatory gate authority. Draft/revision changes invalidate
the reviewed target, and conflicts disable rather than retarget the card.
This is exact **record** review, not complete skill/script/diff review or a
general grant-management/effect-dispatch experience. The only new production
entry is the harmless durable local-version question, with no proposal/grant.
No retained artifact identity or finalized-response authority is fabricated
for a pending operation.

Related: [Interaction and Sessions](Interaction_And_Sessions.md), [Ambient UI](Ambient_UI.md), [User Configuration](User_Configuration.md), [Security](Security_Data_Flows.md), [Extensibility](Extensibility.md).

## Delivered Native Profile - 2026-10-06

The existing Documentation page has an explicit native **Open details**
action. It opens a separate passive native window for the exact embedded page
snapshot, not the latest response, a website, or a newly inferred answer.
The host assigns an opaque typed item reference, positive revision, SHA-256
digest, title, provenance and sensitivity. Embedded pages explicitly have
**no durable session authority**; their viewer identity is process-local.
Reopening the same reference activates its viewer. A different revision opens
separately, and conflicting source or chrome under the same reference is
rejected instead of replacing what the user was reading.

The owner approved these initial bounds after finding that #44 specified
measured native bounds but numeric limits only for the still-gated Mermaid
renderer. `native-text-v1` has one authoritative
[profile](../src/Kora.Core/Presentation/NativeDetailProfile.cs):

| Resource | Enforced bound / outcome |
|---|---|
| Complete admitted source | 256 KiB UTF-8, strict encoding; over-limit/invalid text is rejected visibly, never truncated |
| Parsed blocks | 512, including nested blocks |
| Parsed nodes | 4,096, including inline/container nodes |
| Nesting | 32 levels, with pre-parse protection as well as tree validation |
| Native content controls | At most 4,096; a structural violation uses labelled exact-source fallback rather than an unbounded visual tree |
| Open viewers | 8; existing-reference activation still works at capacity |
| Search query | 256 characters; over-limit input is rejected, not shortened |

There is no paging/download promise for rejected oversized content. Structural
and unsupported-feature failures retain the complete within-byte-bound source
and identity in a single bounded native source reader. Missing renderer and
render failure are explicit source-fallback outcomes, not empty success.
Supported native Markdown is CommonMark headings, paragraphs, lists, block
quotes, thematic breaks, inline/code-fence text, emphasis projected as plain
semantic text, and links (including reference links) with inert visible
destinations. Headings and code use native text styling; a continuous semantic
reader supports selection/search across blocks. This is not Markdig's
unrestricted advanced-extension set. Tables, strikethrough, task lists,
footnotes, raw HTML, images, unknown extensions and Mermaid show labelled exact
source rather than a partial rich result. No content can load resources or
acquire a renderer; links are inert display text, not arbitrary navigation.
Semantic expansion is UTF-8-budgeted while building, including repeated
reference-link destinations, before any control or joined projection is
published.

### Separately gated display-only Markdown checklists

The host-selected [native-markdown-checklists-v3 profile](../src/Kora.Core/Presentation/NativeMarkdownChecklistProfile.cs) adds only task-list state to the bounded CommonMark text subset. [The renderer](../src/Kora/NativeDetailRenderer.cs) projects `[ ]`, `[x]` and `[X]` list markers as **Unchecked:** / **Checked:** text, not actionable tasks, checkbox controls, execution or writes.

The profile preserves strict source/semantic UTF-8, block, node and nesting bounds from [native-text-v1](../src/Kora.Core/Presentation/NativeDetailProfile.cs). Escaped markers and inline/fenced/indented code remain literal. Images, raw HTML, tables and unsupported extensions retain the entire admitted exact source; no partial checklist is published ([maintained parser/accessibility contracts](../tests/Kora.Windows.IntegrationTests/NativeMarkdownChecklistTests.cs)).

[Native projection](../src/Kora/MarkdownDocumentRenderer.cs) uses inert text with explicit state in the accessible name, not toggle/invoke automation patterns. Original source and ordinary literal Find remain distinct, bounded in-memory projections. Source/privacy/session retirement and the existing no-copy policy for finalized responses remain enforced by [controller contracts](../tests/Kora.Windows.IntegrationTests/DetailWindowControllerTests.Checklists.cs).

[Details dispatch](../src/Kora/DetailWindowController.cs) selects this profile only for already host-classified Markdown finalized responses, never by inspecting model markup. Plain-text current responses and retained original USER records stay literal; embedded/history Markdown retain the independently gated table profile. Neither legacy profile acquires checklist support.

The [validated final-answer producer](../src/Kora.Application/ViewModels/MainViewModel.cs) freezes the exact original answer in host-typed state alongside the current ordinary response. Existing native, typed and activated-voice current-response Details pass only that answer body as Markdown, retaining the local-generation warning in native provenance. Visible response/speech and ordinary status PlainText remain unchanged ([actual-route tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.MarkdownAnswerDetails.cs)).

Implementation and deterministic headless contracts do not enable composition: checklist availability defaults to false, returning labelled entire-source fallback for an explicitly requested unqualified profile. This delivers the frozen **current** model-answer source route, not persisted retained assistant-answer Details; retained original USER remains PlainText. No public switch, provider/hardware/browser/resource access or installed accessibility/Enablement/RC qualification is added.

Native chrome contains provenance, revision/digest, sensitivity, renderer
status, Rendered/Source, continuous text, search, selection/exact-source copy
and close controls, outside
the passive content viewport. Native keyboard actions and selectable text
do not establish intent, grant authority or session activity. Closure releases
the window's source, selection and renderer state but does not mutate retained
content, cancel work, mark Done, approve anything or change compact response
pin/timeout behavior. The existing privacy/input gate is rechecked for open
and copy; privacy closure clears and closes viewers, and an old render
generation cannot restore denied content.

### Stable R04/R05 Handoff

[AdmittedDetailContent](../src/Kora.Core/Presentation/AdmittedDetailContent.cs)
is constructed only by an explicit trusted host resolver, not deserialized
from model prose. It accepts plain text or Markdown with a validated
[DetailContentReference](../src/Kora.Core/Presentation/DetailContentReference.cs),
immutable complete source and host-owned classification/chrome.
`FinalizedResponse` additionally requires existing host session/request/task
IDs through
[DetailSessionSource](../src/Kora.Core/Presentation/DetailSessionSource.cs).
Those IDs describe the admitted source; window identity is not durable session
authority and does not validate grants or operations.

[DetailViewerRegistry](../src/Kora.Application/Presentation/DetailViewerRegistry.cs)
owns bounded reference deduplication and privacy cleanup;
[DetailViewerState](../src/Kora.Application/Presentation/DetailViewerState.cs)
owns immutable generation-bound presentation, exact-source copy eligibility
and search. It has no effect dispatcher, task cancellation, session lifecycle,
question/grant store, model/context capture, filesystem or network service.
The durable interaction owner must resolve finalization, retention, revocation
and access before admitting a response and revoke the associated viewer when
its source becomes unavailable. The bootstrap `MainViewModel` response strings
are not silently upgraded into retained history. This delivery does not wire
general response Details, automatic offers/preferences, verbal viewer commands
or a Back-to-conversation route before that handoff exists.

### Bounded Copy Exception and Remaining Gates

This slice deliberately provides **Copy exact source** as Unicode plain text,
and native text selection, not generated HTML or a rich clipboard serializer.
It copies the complete admitted original text (including tabs/line endings),
independent of viewport/search, after native access/disclosure checks. A
platform failure is visible; success is reported only after the write.
Private copy warns that other applications and clipboard history/sync can read
the disclosed text; additional-confirmation items require native confirmation.
No clipboard reads, automatic context acquisition or clipboard clearing occur.
The richer multi-format clipboard behavior specified below remains a separate
serializer/platform gate; do not advertise it from these controls.
**Copy selection** / `Ctrl+C` copies only the selected nonempty range from
the current continuous semantic reader or exact-source reader, with the same
access/disclosure gate. It never substitutes the whole source. `Ctrl+A`
selects the continuous current content. `Ctrl+F`, `F3` / `Shift+F3`, `Ctrl+U`
and `Escape` target native find, match navigation, exact source and close.
Cut/paste and automatic TextBox/context clipboard paths are blocked; Find uses
the [admitted-text literal policy](#native-admitted-text-literal-find), not
case-insensitive wrapping search.

Remaining R14 gates include Sessions list/full conversation, durable
history/artifact resolution, exact response offers and voice targeting,
general shared question/approval routing beyond the bounded local-version
entry, skill/script/diff review and export. HTML/browser,
diagrams, images/assets and syntax grammar acquisition remain independent
gates; this delivery adds none. Automated tests are not native visual,
screen-reader, contrast, text-scale, DPI, multimonitor, focus-restoration or
clipboard-platform trials. Those observations require separate scoped
approval and remain pending.

## Recommended Surfaces

Keep the ambient presence small; do not turn every answer into a browser window.
The compact response surface evolves into a session-labelled latest-interaction view, paired with the Sessions workspace (session list plus full conversation/history) and a separately bound detail/artifact viewer.
The [coordinated window design](UI_Workspace_And_Windows.md) owns layouts/navigation and native card placement; [Interaction and Sessions](Interaction_And_Sessions.md) owns their data/lifecycle contracts.
Concise output links to retained full content; expansion never reruns a task. Structured questions and exact grants use host-owned cards, not generated markup.
Rich-content presentation adds these independently controlled roles within that wider window design:

1. Optional speech-text overlay for the words Kora is currently speaking.
2. Workspace history/inline expansion and separate detail/artifact viewing for text, Markdown, diagrams, citations, immutable `.ps1` source/diffs, and session/task evidence.
3. Static HTML in the detail window and a separate isolated web viewer for
   explicitly requested websites, each with native trusted
   navigation/provenance controls.

Showing/hiding one surface does not cancel work, mute the microphone, or change another surface's visibility.
Approvals, safety warnings, and unresolved errors remain separate trusted host panels, not optional captions or content inside a webpage.
Native question cards support mouse answers and microphone selection/recovery as defined in [Interaction Fallback](Interaction_Fallback.md), even when speech text is off or voice is unavailable.
Content viewing is not browser automation or implicit permission to capture a webpage as model context.

## Detailed Information Window

Use a host-owned detailed information window for a single retained answer,
document, report, list, or immutable artifact that is too long or structurally
rich for the compact response surface. This is the primary passive reading
surface for plain text, the supported Markdown profile, and isolated static
HTML. It can also host exact read-only source/diff tabs. It does not become a
generic host for settings, session management, permission editing, arbitrary
web applications, or model-defined controls.

Prefer the native Avalonia Markdown presenter for Markdown. Do not convert
Markdown to HTML and feed it to a general browser control merely to gain
formatting features; that unnecessarily expands the active-content, resource,
accessibility, theming, and lifecycle boundary. The isolated static-HTML
presenter is a separate adapter selected only for an admitted static-HTML item.

The conceptual shell is shared through composed view models and controls, not
through content inheriting authority from a base web page:

```text
+----------------------------------------------------------------+
| Native title / session / origin / revision / sensitivity        |
| [Back] [Rendered | Source] [Search] [Copy all] [Export] [...]  |
|----------------------------------------------------------------|
| Passive content viewport: text, Markdown, static HTML, source    |
| Local in-document navigation only; no authoritative controls     |
|----------------------------------------------------------------|
| Native status: renderer/fallback, truncation, source unavailable |
+----------------------------------------------------------------+
```

The native shell owns the window title, trust/provenance labels, keyboard
commands, renderer state, source toggle, copy/export requests, and navigation
back to the exact session event. Rendered content cannot cover, imitate, move,
or mutate that chrome. Opening the same immutable reference activates its
existing viewer; explicit comparison may open separately titled viewers bound
to different references. New revisions never silently replace a viewer's
current item. A user-requested refresh resolves and displays a new immutable
revision with the change made visible.

The window opens only from an explicit Details/Open action or a configured
automatic-detail preference for an eligible finalized response. Background
completion does not steal focus. Closing the window releases renderer
resources but does not cancel work, delete retained content, mark a session
Done, or count as approval. Merely reading, searching, scrolling, copying, or
changing tabs does not refresh session inactivity.

Long content must not require one unbounded visual tree or browser document.
The presentation profile defines measured byte/block/node limits and whether
large admitted content is virtualized, paged, or offered as source/download.
Limit failure is an explicit presentation outcome with retained source
identity; it is never silent truncation or a success-shaped empty view.

### Detail Routing and Offer Interaction

The delivered bounded R10 ordinary-speech boundary measures the complete spoken
title/body against device-local caps (default 3 sentences/80 words, independently
lowerable). Current results do not carry trusted safe-omission metadata, so
over-cap speech is explicitly refused with forced full visual recovery, never
truncated or model-shortened. Full result/detail source is unchanged. Mandatory
exact approval/proposal readback and required question/options retain their
existing bounds/privacy behavior. See [the delivered contract](User_Configuration.md#delivered-bounded-spoken-summary-limits-r10)
and [exact counting semantics](../docs/settings.md#spoken-summary-limits).
This is not delivery of all routing/rendering/explicit full-content reading
behavior proposed below.

The host, not model prose or generated markup, decides whether a finalized
response has a detailed representation. The response presenter evaluates the
typed immutable item against one versioned presentation policy. A model/tool
may supply content kind, structure, a concise-summary candidate, and a
non-authoritative presentation hint; it cannot open/focus a window, suppress
the compact result, or classify its own output as trusted.

An explicit user request such as "show the full answer", "open the code",
"show that table", "review the files", or the native Open details action always
requests the exact eligible item, independent of automatic-routing thresholds.
Otherwise the initial policy marks a finalized item as detail-recommended when
any of these apply:

- The kind is static HTML, Mermaid/diagram, exact source/script, diff, file,
  multi-file review, or another type without a faithful compact renderer.
- It contains a table, fenced/multiline code longer than 5 lines, more than
  8 list items, more than 3 citations, or more than one titled section.
- Its semantic text exceeds 120 words or 3 paragraphs after excluding hidden
  renderer metadata.
- The admitted accessibility/text-scale layout cannot present it within the
  compact surface's bounded reading region without discarding structure.
- A required exact/provenance-bearing representation would be misleading if
  replaced only by a generated summary.

These are routing thresholds, not retention or renderer-admission limits.
Record the policy version and matched reason codes on presentation state, not
the content itself. Exercise exact boundary values before changing them; tune
the versioned policy from usability evidence rather than silently allowing
each model/provider to choose. A user can still open any retained eligible
short response through History even when it was not recommended for detail.

Classification occurs after finalization. Streaming text can show a compact
provisional response, but cannot repeatedly open windows or ask on every
structural change. A corrected/replaced final item gets a new immutable
revision and offer identity. Renderer availability does not change whether
detail is warranted: missing rich rendering opens the labelled source/plain
fallback rather than hiding the full result.

For every detail-recommended item, the compact response UI shows a native,
keyboard/screen-reader-accessible **Open details** link/button beside a concise
summary. Its accessible label includes the content type and session/item
context, for example "Open details: 14-row settings table for Deployment
notes". The control binds the immutable item/revision, never "whatever response
is latest". Clicking or pressing Enter/Space opens or activates that exact
detail window without rerunning work. If the response UI later displays
another item, the retained conversation entry keeps its own Open details
action.

The default hands-free behavior is **Offer**. After speaking the concise
summary, Kora asks once, for example: "I have a detailed result with a table
and code. Would you like me to open it?" Ask when:

- the request was accepted from activated voice and there has been no
  pointer/keyboard interaction with that request, response, or selected
  session since acceptance; or
- the effective interaction preference is voice-first/voice-only and speech is
  currently permitted.

This is request-local observed interaction state, not ambient mouse tracking.
Do not infer a permanent preference from whether the user touched the mouse.
If speech is disallowed, unavailable, interrupted, or private-output policy
forbids the offer, retain the native Open details action and eligible visual
attention without claiming that a question was spoken.

The offer is a host-owned, non-consequential Yes/Not now question bound to
session, response item, immutable revision, and expiry. An unambiguous "yes" or
"show it" while that offer is the foreground voice target opens and activates
the exact detail window; "not now", dismissal, or expiry leaves the retained
link/history available. Silence is not consent and does not open anything.
Acceptance performs no model round trip. The window may take focus because the
user just clicked or accepted the exact offer; merely classifying or completing
a response never steals focus.

When conversational replies are enabled and eligible, speaking this offer
opens the bounded reply turn defined by [Interaction and
Sessions](Interaction_And_Sessions.md#conversational-voice-turns). The user can
answer `Yes` or `No` without repeating the activation name within the
configured timeout. Expiry closes prefix-free listening but preserves the
Open details action and exact activation-name command.

Detail offers are lower priority than approvals, required clarifications,
errors, device recovery, and other consequential questions. They never replace
or make generic "yes" eligible while another question is foreground. If an
offer cannot become the unique voice target, do not ask a yes/no question;
state only that details are available when output policy permits, retain the
native link, and allow an exact command such as "open details for Deployment
notes". Multiple eligible items require explicit session/item disambiguation.
A stale, deleted, revoked, replaced, inaccessible, or expired target fails
closed and explains that it cannot be opened.

The detail-presentation preference has three choices:

| Choice | Behavior |
|---|---|
| Offer (default) | Show Open details; ask once for hands-free eligible foreground responses |
| Open automatically | Open eligible foreground responses after finalization without another question; never auto-open background results, bypass lock/privacy, or open approvals as content |
| Link only | Show Open details and support exact commands, but do not proactively ask or open |

An exact "always open detailed results" or "stop asking about details" request
uses the normal typed preference workflow and confirmation appropriate to
settings; answering one offer changes no preference. Explicit Open/Close,
Rendered/Source, search, copy, and navigation commands target the current
viewer/item and remain available in every preference mode.

### Clipboard Copy

The detailed response window and every textual skill-file/source viewer provide
a native **Copy all** action for the current item or selected file. Copy all
uses the complete admitted immutable content, not only visible, loaded,
expanded, searched, or virtualized lines. It never silently truncates. If the
complete admitted text cannot be written, leave the clipboard unchanged and
show the size/format/platform failure with an export or source-view recovery
when available.

The copied representation follows the active view:

- Rendered Markdown/static HTML includes both semantic Unicode plain text and a
  host-generated sanitized HTML Clipboard Format fragment in one clipboard
  payload. The plain representation follows document reading order and
  includes all table cells, list markers, code text, image alternatives, and
  link labels plus destinations where omitting the destination would lose
  information. The rich fragment preserves supported headings, emphasis,
  lists, tables, links, quotes, code blocks, and safe syntax styling without
  active content, hidden text, Kora chrome, approval controls, or renderer
  diagnostics.
- Rendered Markdown may additionally include the original admitted Markdown in
  a versioned registered `text/markdown` clipboard format. This is source data,
  not trusted HTML. Static HTML never invents Markdown.
- Source, script, configuration, manifest, diff, and plain-text views copy the
  complete original admitted text as Unicode plain text. A code/source viewer
  may also include a sanitized HTML fragment preserving supported syntax and
  diff styling, while syntax token spans, line-number chrome, search marks,
  soft wrapping, and non-source decorations never alter the plain source.
- When both representations exist, **Copy all** copies the active
  representation and a clearly named **Copy source** action remains available.
  The completion notice states which representation and immutable revision was
  copied.
- Binary or invalidly decoded content cannot masquerade as an exact text copy.
  A hex/text fallback copies its complete labelled displayed representation,
  or reports that raw-byte export is required.

Users can select text within rendered documents, code, highlighted source,
diffs, and textual skill files. `Ctrl+C` and a native **Copy selection** context
action copy exactly the selected textual range from the active immutable
revision. `Ctrl+A` selects the active content rather than the window chrome;
the separate Copy all action remains available for keyboard and assistive
technology users. Selection can cross rendered blocks and virtualized lines
without losing intervening text. For source/code, copying preserves original
characters, tabs, and line breaks and excludes highlighting metadata. Empty,
stale, revoked, redacted, or no-longer-authorized selections fail visibly and
do not reuse a previous selection.

Every copy payload includes Unicode plain text as the canonical interoperable
fallback. Rich rendered selections/items additionally include host-generated
HTML Clipboard Format, and admitted Markdown can include registered
`text/markdown`; RTF or future formats require an explicit versioned host
serializer and equivalent security/accessibility tests. Publish all formats as
one logical clipboard update so consumers choose the richest format they
support without observing mismatched revisions.

Rich clipboard data is serialized from the admitted semantic document/source
model, not copied from arbitrary renderer DOM or page-authored clipboard data.
The HTML fragment has correct fragment boundaries and contains only an
allowlisted, self-contained subset: no scripts, event attributes, forms,
frames, plugins, embedded objects, metadata beacons, external/local resource
URLs, CSS imports/URLs, hidden content, clipboard directives, host identifiers,
sensitive source paths, or privileged URI schemes. Style is limited to
necessary portable formatting and theme-independent syntax/diff semantics.
Links retain a visible destination only when it passed the display profile;
images use admitted embedded data or alternative text and never cause a paste
destination to fetch a resource.

Static HTML and browser content never receive clipboard API access; the host
obtains an admitted selection/semantic projection through the isolated
renderer contract and performs the OS write. A rendered page cannot initiate
copy, provide or mutate clipboard formats, read existing clipboard data, or
detect the result. Clipboard format names and serializers are selected by the
host, not content, models, skills, or extensions.

Copy is a deliberate native UI/keyboard disclosure from content already
visible to the active unlocked user. Before the write, revalidate the exact
item/file revision, current access, Windows session, and privacy state. Explain
that copied private content leaves Kora's retention/access boundary and may be
read by other applications or clipboard-history/sync features. Content marked
as requiring an additional disclosure confirmation uses a native confirmation;
markup cannot suppress it. Lock, revocation, or deletion blocks new copy and
clears the viewer selection/content under its normal policy.

A successful OS write replaces the user's current clipboard, so report success
only after the platform confirms it. Failure leaves the prior clipboard and
viewer selection intact. Kora does not silently clear or restore the clipboard:
another application may have replaced it. Any optional timed clear must be a
separately designed user preference that verifies Kora still owns the exact
clipboard generation before clearing. Copying does not approve, execute,
enable, save, refresh session inactivity, or make clipboard content available
to a model.

## Passive Content Versus Complex Workspaces

Reuse the detailed-information shell and renderers for passive content, while
using purpose-built native workspaces for selection, mutation, validation, and
security decisions:

| Scenario | Primary surface | Reuse of detail rendering | Authority boundary |
|---|---|---|---|
| Long answer, documentation, commands/settings report, model result | Detailed information window | Full document or immutable item | Read-only; content links request native navigation |
| Session list, conversation, queue, lifecycle, evidence filters | Sessions workspace | Open one answer/artifact/evidence record in the detail window | Session selection and lifecycle controls are typed native controls |
| Skill discovery and review | Skills management/review surface | Render purpose/instructions Markdown and exact manifest/source/diff tabs | Enable, disable, save, test, and capability review are separate native workflows |
| Standalone instruction authoring | Skill authoring workspace in single-file mode | Render instruction Markdown plus exact source/diff | Voice/typed refinement changes only the proposal; exact native Save revalidates source identity and base digest, and profile editing is not general file-write authority |
| Multi-file skill or script review | Skill/revision view with host-owned file tree and tabs | Each selected file uses Markdown or exact language-highlighted source rendering; digest and dependency status remain visible | Tab selection is not approval; review binds the complete immutable revision/file set |
| Permission grant inventory and editing | Permissions & Approvals workspace | Optional inert explanation, provenance, and use-evidence detail | Filter, narrow, revoke, broaden/request, and confirm are typed native operations, never HTML/Markdown controls |
| Settings and setup/recovery | Dedicated typed native pages | Help/details may open passive documentation | Validation, persistence, consent, and recovery stay outside rendered content |
| Internet website | Isolated web viewer | None; a page is not converted into a trusted detail document | Browser navigation policy applies; reading/scraping/model context is separate |

A complex workspace may embed the same read-only content controls, but it owns
its navigation, selection, dirty state, validation, conflict handling, and
commands. Do not create a universal web-based "complex window". In particular,
grant editing and approval must remain available when the HTML renderer,
browser runtime, model, network, or microphone is unavailable.

## Content Origin and Admission

Origin changes admission policy, not whether content can be trusted as a
control. All Markdown and HTML is passive data, including text produced by a
trusted Kora component.

| Origin | Snapshot and profile | Network/local access | Script policy |
|---|---|---|---|
| Embedded Kora documentation | Build-pinned resource; documentation Markdown profile | None | No document script or raw HTML execution |
| Host-generated reports such as command/setting/grant summaries | Immutable generated text plus host provenance; Markdown profile | None | No document script |
| Final model, provider, tool, or skill output | Bound to session/task/item/revision and digest; untrusted Markdown or static-HTML profile | None unless separately admitted finite assets exist | No script, event handlers, forms, frames, or active content |
| Skill package files | Immutable reviewed revision and bounded file-set identity; Markdown or exact source profile | Relative references only inside the admitted snapshot and only when the profile allows them | Code fences/source are inert; no script execution |
| Explicitly selected local document | Copy/read into a bounded immutable snapshot with canonical source disclosure | No neighbouring-file, UNC, reparse, arbitrary loopback, or ambient file access | Same untrusted static policy; local origin confers no execution trust |
| Remote website | Exact validated HTTP(S) destination in the isolated web viewer | Subject to browser navigation/subresource policy and local-only mode | Page script is browser-origin code only; it receives no Kora bridge or authority |

Admission resolves current access and sensitivity before reading, captures the
permitted content into an immutable bounded snapshot, verifies its digest and
declared profile, parses/sanitises it, then publishes a render generation.
Late callbacks from superseded generations are ignored. Revocation, deletion,
lock, or source loss clears content according to the store/access contract
rather than leaving a stale renderer copy available.

Kora does not support document-supplied JavaScript in the detailed information
window. There is no "enable scripts" escape hatch, even for model-generated,
skill-provided, user-selected, or locally stored HTML. A pinned host-owned
implementation may internally use JavaScript to render a bounded format such
as Mermaid; that implementation receives validated data in its isolated
renderer and does not make document script trusted. Dynamic sites belong only
in the separately labelled isolated web viewer. A generated application that
requires JavaScript is unsupported until a distinct containment and product
workflow is designed and proven.

## Shared Appearance Theme

Every Kora-owned visual surface uses the single persisted application theme:
System (default), Light, or Dark. System follows the effective Windows
light/dark preference while Kora is running. Light and Dark override it.
The presence, chat and answer windows, speech text, native questions,
approvals, errors, Markdown, diagrams, task/result panels, Settings, browser
chrome, and generated-HTML chrome update immediately from the same observable
setting.

Native Avalonia surfaces use the shared application theme resources rather than
hard-coded local palettes. A new window or control cannot introduce an
independent default. Theme changes preserve content, selection, scroll
position, focus, approval identity, and task/result identity; they are
presentation changes only.

Embedded web content does not automatically inherit native resources. The host
passes the resolved effective Light or Dark variant to each controlled viewer
and updates its colour scheme and host-owned stylesheet when System changes.
Internet pages may retain author styling, but browser chrome and trusted Kora
overlays remain themed and readable. Generated/local HTML and Markdown use
host-owned accessible theme CSS; untrusted content cannot override trusted
chrome, infer a broader preference store, or create a separate persisted theme.
If a renderer cannot apply the effective theme accessibly, show escaped/native
content or mark that renderer unavailable rather than opening an unreadable
surface.

| Context | Default presentation |
|---|---|
| Short verbal answer | Ambient presence; speech text only if enabled; full answer available on request |
| Requested detailed answer | Readable detail panel; Markdown where available |
| Table/code/diagram | Detail panel, with brief spoken explanation rather than reading markup |
| Speech suppressed by call policy | Visual answer; no fictitious "currently speaking" caption |
| Approval/error/unknown outcome | Trusted native panel regardless of caption preference |
| Missing/unusable microphone or first-run device selection | Native readable question with mouse-selectable detected devices, consent, refresh/help, and continue-without-voice |
| User asks to view a website | Dedicated viewer if verified capability exists; explicitly offered external browser otherwise |
| User asks to preview generated HTML | Isolated static preview; source view available |
| Passive background result | No focus theft; eligible notification/detail through existing proactive policy |
| Windows lock/disconnect | Stop speech and hide sensitive surfaces; close/suspend controlled browser activity |

## Optional Speech Text

### Delivered bounded local utterance slice (R25)

`display.speech-text` is device-local, unsaved/default/reset **Off**. Native
Settings and exact typed/current-name activated commands share the admitted,
audited atomic configuration service. **CurrentUtterance** remains enabled:
an ephemeral selectable native window shows the exact text
passed by the host's admitted response speech route, only while the provider
reports that matching playback identity/generation and utterance segment 0 as
actually playing. Synthesis, queued work, failed/suppressed output and voice
preview do not acquire caption authority.

**CurrentSentence** is an implemented, explicitly saved request, not an enabled
Windows voice/profile claim. It uses the same native choices and exact
`set display.speech-text to CurrentSentence` typed/current-name activated route.
No exact Windows voice/rate/output profile is qualified in this build; sentence
captions therefore remain explicitly unavailable in production. Kokoro and
legacy providers do not attest sentence alignment. Ordinary speech is unchanged,
and the complete visual answer is retained even when sentence evidence is absent.

The bounded version-1 source map keeps exact UTF-16 substrings, including leading
and trailing whitespace. It accepts at most 32,768 characters and 256 ranges.
ASCII `.`, `!`, `?` runs, optional closing quotes/brackets and following whitespace
end a range; an unterminated final range is retained. Interior decimal punctuation
is not a delimiter. Initials and known ambiguous abbreviations (`Dr`, `Mr`, `Mrs`,
`Ms`, `Prof`, `etc`, `vs` before a period), malformed surrogate pairs and
non-whitespace control characters are unsupported, not normalized or guessed. This is conservative plain
text segmentation, not general linguistic or SSML support.

The Windows producer binds real `SpeakProgress` source character ranges and
`AudioPosition` to the exact active `Prompt` and output generation. It seals
before playback, requiring monotone nonnegative positions, complete lexical
source coverage, UTF-16 boundaries and output-duration bounds. Identical repeated
native ranges (including normalized numbers) keep their first time; overlapping,
cross-sentence or unmappable ranges invalidate the timeline. Callback `Text` is
never used as original source. The matching **actual device** position selects
the last reached sentence start; reader position, output level, elapsed wall time
and duration estimates cannot advance captions. Completion retains only the last
sentence actually observed, never an unplayed final sentence. This implementation
does not chunk/resynthesize speech, change prosody or add word highlighting.

Response replacement/retirement, playback stop/cancel/failure, configuration
revision, original request/session/channel, input recovery/activation generation,
call/privacy revision and current unlocked ownership gates retire or reject the
caption; late frames never restore it. The controller samples independently of
presence visibility at 50 ms, with immediate retirement on host transition paths.
No text enters preferences, logs, model requests or caption history. Required
native questions/approvals/errors and full visual fallback remain independent.
Changing this preference neither speaks/stops/replays output nor opens capture
or changes response/call policy. Unknown or unconfirmed preferences hold captions
off without disabling ordinary speech.

The [2026-10-09 bounded UX increment](User_Configuration.md#delivered-bounded-caption-ux-options-r25---2026-10-09)
retains only already-observed text after normal successful completion for a
validated 0-30-second delay (default 5), or while run-only pinned. Retained text
is labelled **PREVIOUS SPEECH**; unpin keeps the original completion deadline.
Interruption and every source/privacy retirement still clear immediately,
regardless of pinning. Placement persists one working-area corner with
a 24-DIP margin. It does not activate on show or span the desktop as an
input-catching overlay. No content is persisted.

The [run-only native display increment](User_Configuration.md#delivered-run-only-native-caption-display-choice-r25---2026-10-10) adds live host-issued display choices in the caption and Speech & audio settings, plus explicit **Return to primary**.
Primary remains the default; an explicit target stays bound to its actual source lifetime, not ordinal/name/coordinates. Snapshot/selection revisions expire stale choices on topology, working-area/DPI changes or native closure.

Geometry is bounded/clipped inside that source's current working area and scaling, including negative coordinates and tiny areas; native resize/reposition hides first. A missing/ambiguous explicit target hides and retires even pinned text, never silently moving it to another physical display.
Explicit recovery selects only future eligible text; it cannot repair preferences, clear privacy/call/ownership holds, resurrect a retired source, enable captions or extend completion/pinning.

The confirmed device-local [reader text scale](#bounded-native-reader-text-scale)
also applies to the actual observed caption content TextBox, in new and open
native windows, from its original typography. Caption labels, Pin/Unpin, display
selection/status, accessible names and native geometry remain independent.
Unavailable reader preference evidence explicitly retires the caption; late
frames cannot restore it. This changes only the former caption typography
exclusion, not caption enablement, speech, identity, privacy/call gates or pin
and display lifetimes.

**Current sentence** and word alignment remain open: the actual adapter supplies
utterance segment 0 only, so estimated boundaries are not advertised as
host-admitted playback alignment. Arbitrary coordinates and
caption/navigation commands outside the [bounded natural grammar](#bounded-natural-speech-caption-commands) remain proposed. Native
accessibility properties/selection/pin/settings are source-tested, not installed
accessibility, installed multimonitor or acoustic qualification. Rich HTML/browser/diagram rendering is
not delivered. The acoustic experiment remains maintained and is not retired.

### Broader proposed contract

Expose a voice-settable `speechText` preference: off (initial ambient default), current sentence, or current utterance.
It displays the exact final text submitted to TTS, not the entire answer, user transcription, or a newly generated paraphrase.
Bind text to response ID, playback generation, and segment so interruption/replacement cannot leave stale captions attached to new speech.
Word highlighting is optional only when the selected engine provides verified alignment events; otherwise show sentence/utterance text without claiming word accuracy.

The overlay follows actual playback: do not show queued, suppressed, cancelled, or failed speech as though spoken.
On interruption/finish, clear after a configurable 5-second default delay (0-30 seconds), unless pinned.
Pinned content is labelled previous speech; the playback overlay itself is ephemeral, while its permitted source response remains subject to session retention.
Speech text must be selectable, accessible, legible over any desktop background, and repositionable independently of the abstract presence.
It must not steal focus or use a transparent hit-test surface that intercepts unrelated desktop input.
Sensitive speech remains sensitive visible content; captions obey lock/privacy rules.

In visual-only/call-gated mode, show the answer panel rather than reusing a playback caption as an unsolicited notification.
Accessibility announcements must not duplicate TTS or announce rapidly changing streamed text by default.

## Browser Content

### Internet Pages

Support a dedicated embedded viewer as the target UX, with an external-browser choice.
No browser package is selected here: an Avalonia-compatible Windows integration, potentially WebView2-backed, must prove packaging, isolation, network control, lifecycle, and accessibility.
If that proof fails, report embedded viewing as unavailable and offer an explicit external-browser action; never silently open another app.

Display native URL/origin/title, back/forward/reload/close controls, loading/error state, and a clear "external website" label.
Approve the validated destination as a browser operation, distinct from model egress.
Kora-initiated internet navigation is unavailable in local-only/offline mode; show the link without silently switching modes.
Browsing may disclose IP/device/browser metadata; remote-enabled processing is not consent to upload conversation/clipboard content into a page.

Embedded browsing uses a dedicated profile without importing the system browser's cookies, password vault, or authenticated session.
Use an ephemeral profile initially; account persistence is a future separately reviewed capability.
Navigation, redirects, subresources, downloads, popups, external protocol handlers, permissions, service workers, and background traffic require an enforceable browsing policy.
Allow only validated HTTP(S) navigation: HTTPS for internet pages; HTTP only for an explicitly selected permitted loopback service.
Cross-origin dependencies need approved browsing scope, not an invisible "all network" grant.
Block unsupported cases with an explanation; do not claim isolation based only on disabling a toolbar.

Browser-origin content cannot call Kora APIs, approve actions, launch executables, obtain microphone/camera/clipboard/files, or invoke a skill.
No injected host-object bridge, credentials, local storage access outside its profile, or tools.
Downloads and exports require separate scoped host actions; no automatic opening of downloaded code.
Opening a page does not automatically scrape/read/summarise it. User-selected extraction requires context provenance, size checks, and normal egress consent.

For the external browser, validate/show the exact destination and launch only the registered browser with a URL, not a shell command.
Disclose that external browser identity/cookies/permissions/networking are user/browser-managed and not subject to Kora's embedded sandbox.
Once launched, Kora does not control its lifetime, close it on lock, or claim that its background requests stopped.

### Generated or Local HTML

Treat generated HTML as untrusted data even when produced by Kora's model.
Render a bounded immutable snapshot, not a file with access to neighbouring user files.
Prefer an isolated content origin served by the host from memory; no model-chosen listener/port or arbitrary `file:` navigation.
Explicit local-file selection is a scoped read whose references cannot escape into adjacent files, UNC paths, protected roots, or arbitrary loopback services.

Initial generated previews are static: HTML/CSS with sanitisation, a restrictive content security policy, and enforced navigation/resource interception.
Block scripts, handlers, forms/submissions, plugins, frames, `javascript:` links, external fonts/images/styles, CSS imports/URLs, and automatic network requests.
Strip or constrain document features that can obscure or imitate trusted chrome,
including top-layer/popover behavior, fullscreen, pointer lock, dialogs, and
unbounded fixed-position overlays. CSS parsing and resource interception must
cover encoded/redirected URL forms; string replacement is not a sanitizer.
Approved local assets are finite copied snapshots, resolved/revalidated within the selected source scope; no path traversal/reparse escape.
No persistent browser storage, host bridge, service worker, or access to Kora's filesystem/credentials.
Sanitisation, origin isolation, and CSP are layers, not substitutes for verified browser network/file enforcement.

Use a content digest/render generation; replace atomically after validation and ignore late render callbacks.
Present source/provenance and a visible static-preview limitation.
Malformed/unsupported content offers escaped source/plain text, explicitly labelled rather than falsely reported as a successful rich preview.
Dynamic model-generated JavaScript applications are outside initial scope; they need a separate containment proof, not an "enable scripts" preference.
Saving/exporting HTML is an exact user-selected write outside protected roots; opening exported content outside Kora is separately requested with limitations disclosed.

### Strict Native Static HTML Implementation Boundary

The additive `static-html-v1` implementation consumes only an explicitly admitted `DetailContentKind.StaticHtml` snapshot. It is **unqualified and unavailable by default**; production composition does not enable it ([renderer](../src/Kora/StaticHtmlDetailRenderer.cs), [composition](../src/Kora/App.axaml.cs)).
The actual detail controller uses the existing immutable viewer, privacy/access/generation and source-copy boundaries and returns labelled complete-source fallback, not a helper-only preview or inferred HTML classification ([routing](../src/Kora/DetailWindowController.cs)).

The approved initial grammar is an XML-compatible fragment, not HTML5 error recovery: lowercase `div`, `p`, `h1`-`h6`, `ul`, `ol`, `li`, `blockquote`, `pre`, `code`, `strong`, `em`, `b`, `i`, `span`, and self-closed `br`/`hr` ([parser](../src/Kora.Core/Presentation/StaticHtmlParser.cs)).
All other elements, every attribute (including namespaces), comments, processing instructions and CDATA are unsupported. Lists require direct `li` children; block content inside phrasing containers is rejected ([parser](../src/Kora.Core/Presentation/StaticHtmlParser.cs)).
Inline emphasis becomes semantic plain text, lists/quotes receive inert textual labels, and headings retain levels in labelled native text controls ([controls](../src/Kora/StaticHtmlDocumentRenderer.cs)).

One authoritative profile limits parsing to 65,536 strict UTF-8 source bytes, 65,536 semantic UTF-8 bytes, 256 block-element starts, 2,048 element/text nodes (including whitespace text, excluding closing tags), element depth 16, zero attributes and 2,048 entity-reference introducers ([limits](../src/Kora.Core/Presentation/StaticHtmlProfile.cs)).
Bounds are inclusive; separators and list/quote labels count toward semantic bytes. Only XML predefined and numeric entities are decoded, once; DTDs and resolvers are disabled. No additional public dependency or browser is used ([bounded parser](../src/Kora.Core/Presentation/StaticHtmlParser.cs), [exact-boundary tests](../tests/Kora.Core.UnitTests/Presentation/StaticHtmlParserTests.cs)).
Entity introducers are counted directly in source before decoding, independently of nodes. The immutable result records both counts; predefined and numeric references coalescing into one reader text node cannot bypass the separate 2,048-reference bound ([parser](../src/Kora.Core/Presentation/StaticHtmlParser.cs), [measured boundaries](../tests/Kora.Core.UnitTests/Presentation/StaticHtmlParserTests.cs)).

Unsupported, malformed, oversize-for-profile, unknown-profile and unqualified results never publish partial semantics. They retain the entire admitted Unicode source, including original line endings; rendering may collapse XML whitespace and normalize preformatted XML line endings without changing that source ([fallback](../src/Kora/StaticHtmlDetailRenderer.cs)).
The existing 256 KiB admission ceiling still applies: content above admission is explicitly refused, never truncated ([admission](../src/Kora.Core/Presentation/AdmittedDetailContent.cs)).
This increment adds no byte-ingestion route, asset resolver, rich clipboard serializer, active navigation or default content producer; existing byte decoders and source admission remain responsible for valid Unicode ([parser](../src/Kora.Core/Presentation/StaticHtmlParser.cs), [composition](../src/Kora/App.axaml.cs)).
Native/accessibility/theme/DPI qualification and complete rich-clipboard behavior remain separate enablement work, not implementation claims ([source/privacy tests](../tests/Kora.Windows.IntegrationTests/StaticHtmlDetailRendererTests.cs), [qualification policy](Acceptance_Criteria.md#three-tier-qualification-policy)).

## Markdown and Extensions

The implemented Documentation window renders only build-time embedded
`docs/*.md` resources. It parses them locally with Markdig and creates native
Avalonia heading, paragraph, list, quote, code, and divider controls. Links are
displayed as inert text and raw HTML is never executed. The renderer cannot
load network content, invoke commands, open the microphone, or dispatch tools.
Navigation is provided by the host-owned page list, and shared dynamic theme
resources apply System, Light, or Dark immediately.

Use a defined, versioned Markdown profile rather than "whatever the library supports".
Recommended initial profile:

- CommonMark-style headings, paragraphs, emphasis, lists, block quotes, links, and fenced code.
- Tables, strikethrough, and display-only task lists.
- Local bundled syntax highlighting; no execution buttons attached automatically to code fences.
- Mermaid fenced blocks using a pinned bundled renderer after its safety/integration gate.
- Accessible table/diagram/source alternatives.

Raw HTML is escaped or disabled in Markdown. Images are off by default and never fetched remotely just because a document includes a URL.
Explicit asset display requires approved local snapshots or a separately consented destination under browser/asset policy.
Links route through trusted native validation/navigation; a Markdown link cannot directly invoke tools or privileged URI handlers.
Relative references resolve only within the selected bounded content root, not the app/source directory or user's home.

Streaming Markdown is provisional. Render incomplete fences/diagrams as inert source until a complete bounded block validates.
Do not rerender an entire diagram on every token; bound/debounce rendering and discard stale results.
Retain the source alongside the rendered result for explainability/copy/accessibility.
Citations retain real source identifiers and permission boundaries; styled text does not become trusted evidence.

### Code and Language Syntax Highlighting

Every code context uses the same host-owned, versioned syntax-highlighting
service: Markdown fenced blocks, standalone script/source files, manifest and
configuration files, diffs, generated code, and code attached to evidence or
skill review. Inline code remains a clearly styled monospace span and does not
need language tokenization. Highlighting is presentation only: it never
executes, compiles, evaluates, formats, fixes, validates, or sends code to a
language server or model.

Language selection is deterministic and visible. Use this precedence:

1. A host-resolved language attached to an admitted typed item or exact
   manifest/file role.
2. A canonical extension or filename from the immutable source snapshot, such
   as `.ps1`, `.cs`, `.json`, `.yaml`, `Dockerfile`, or `.gitignore`.
3. A normalized allowlisted language identifier on a Markdown fence.
4. Plain text when absent, conflicting, unknown, or unsupported.

Content, model prose, and a document-supplied filename cannot select a parser
outside the bundled allowlist. Aliases such as `powershell`, `ps1`, `csharp`,
`cs`, `yml`, and `yaml` resolve through one versioned host table. Do not use
probabilistic language detection initially. If it is added later, label the
result as inferred, keep source/plain-text selection available, and never let
the inference affect execution, approval, file type, or policy.

The initial grammar set should cover Kora's reviewed and commonly displayed
contexts: PowerShell, C#, JSON, YAML, XML/XAML, Markdown, SQL, JavaScript,
TypeScript, HTML, CSS, shell, batch, INI/properties, and unified diff. Grammar
assets are pinned and bundled offline. A document, skill, model, package, CDN,
or user-selected file cannot load a grammar, theme, WebAssembly module, regex,
script, CSS, or plugin. Adding or updating a grammar is an application
dependency change with normal licence, security, packaging, and regression
review; it is not a content preference.

Token spans are a projection over exact immutable source. Selection, copy,
export, digest display, line numbers, search, and approval identity operate on
the original bytes/text, never reconstructed colored spans. Preserve
whitespace, line endings where exact-byte review requires them, Unicode, and
the distinction between tabs and spaces. Invalid encoding or binary content is
reported and shown through an appropriate bounded text/hex fallback rather
than decoded silently. Highlighting a diff applies syntax tokens only where
the underlying file language is known and retains native added/removed/context
semantics; color alone never communicates a change.

Tokenization runs off the UI thread where practical, is cancellable by render
generation, and has byte, line, token, nesting, time, and memory bounds.
Pathological or unsupported input falls back to selectable monospace plain
text with the language/failure status visible. Do not run unbounded regular
expressions or create one native control per token for large files; virtualize
lines and incrementally publish only generation-matching results. A
highlighter exception cannot crash the viewer or discard the exact source.

Highlight themes derive from Kora's effective Light/Dark theme and meet the
applicable contrast target. Syntax remains understandable with color disabled:
source text, punctuation, whitespace, diff markers, focus, selection, search
matches, diagnostics, and line numbers remain distinguishable. Screen readers
receive the original code and optional language label without announcing
every token category. Users can switch highlighting off or choose plain source
for the current view without changing the artifact or global execution policy.

### Mermaid

Enable only an allowlisted, tested diagram profile; initial flowchart, sequence, and class diagrams.
Use the renderer's strict security configuration, disable clickable links/HTML labels, and sanitise the resulting SVG.
Remove scripts, event attributes, `foreignObject`, unsafe links, and external image/font/resource references from output.
Do not use untrusted renderer directives to weaken host policy or load plugins.
The pinned renderer can execute its own trusted implementation in an isolated worker/view, but model diagram content cannot become executable host code.
Initial render limits: 256 KiB UTF-8 document, 32 KiB per diagram, 500 nodes, 1,000 edges, 2-second diagram budget.
Exceeding any limit stops that render and provides labelled source/error, leaving queue/status/cancel controls responsive.
Do not claim a worker is safely terminated unless the actual implementation proves cancellation/resource isolation.

### Other Extensions

Math notation, footnotes, definition lists, and richer diagram types are candidates, not automatically enabled features.
Kora owns profile negotiation, admission, renderer dispatch, policy, and fallback.
First-party rendering modules may supply optional implementations; third-party executable renderers require the same executable containment/integrity gate as other extensions.
Do not load JavaScript/CSS/plugins from a document, user skill, CDN, or arbitrary package URL.
A skill can emit typed Markdown/HTML data, never install a renderer or choose weaker renderer permissions.
Unknown extensions show source and a capability explanation; no silent dependency fetch.

## Typed Presentation Contract

The host accepts bounded immutable items with session, task/response, item,
revision, content-origin, sensitivity, kind, content/source reference,
provenance, digest, supported profile, optional host-resolved code language,
and admitted finite-asset references.
Kinds include plain text, Markdown, static HTML snapshot, read-only script/diff source, and approved browser destination.
Every item also carries session identity and an immutable history/artifact reference so delayed output cannot replace another session's selected interaction.
The contract reports admission and rendering state separately: resolving,
admitted, rendering, rendered, fallback, unavailable, cancelled, and failed.
Fallback preserves the item identity and explains which profile feature or
capability was unavailable. Cancellation and stale generation are not failure
or successful rendering.
Script/source viewers show exact bytes/digest and provenance; no automatic Run button or execution triggered by expansion.
The host chooses surfaces and validates capabilities; model hints cannot force topmost windows, auto-open links, grant network access, or dismiss approval panels.
An arbitrary HTML "Approve" button is inert content, not a real approval.
Native chrome stays outside content so pages cannot cover or replace trust indicators.
Generated summaries/captions inherit source restrictions; viewing content is not permission to send it to another destination.

Core owns portable presentation kinds, origins, identities, and validation.
Application owns admission orchestration, immutable viewer state, generation
cancellation, native action requests, and access revalidation. The Avalonia
desktop layer owns native Markdown/source controls and shared window chrome.
Windows owns any WebView/browser integration, profile/process lifecycle, and
OS session/lock behavior, including the platform clipboard adapter. A renderer
receives already admitted content and
cannot resolve arbitrary paths, query grants, dispatch tools, or select a
weaker profile.

Admission and rendering use the layer's versioned activity sources with stable
operation names and truthful terminal status. Safe tags can include host-owned
session/task/item/revision IDs, origin/profile, renderer kind, bounded content
size, generation, fallback reason, and outcome; never content, titles, local
paths, URLs with query data, or rendered model text. Rendering diagnostics are
not security-audit authority. Consequential native operations reached from the
shell, such as browser navigation, export, skill enablement, or grant mutation,
use their owning validated workflow and audit contract.

Viewer close, item replacement, access revocation, Windows lock, and application
shutdown cancel outstanding generations and deterministically dispose owned
streams, temporary snapshots, browser controllers/profiles, timers, and
subscriptions. A renderer crash or failed disposal reports the capability as
degraded and cannot leave an invisible browser process with continued content
or network access.

## Voice Settings and Navigation

Delivered caption options use [User Configuration](User_Configuration.md):

| ID | Default / choices | Exact example after current activation name |
|---|---|---|
| `display.speech-text` | Off / CurrentUtterance / CurrentSentence (alignment unavailable without exact-profile qualification) | `set display.speech-text to CurrentUtterance` |
| `display.speech-text-dismissal-delay` | 5 seconds; canonical integer 0-30 | `set display.speech-text-dismissal-delay to 10` |
| `display.speech-text-placement` | BottomRight / BottomLeft / TopRight / TopLeft; primary working area | `set display.speech-text-placement to TopLeft` |
| `display.speech-text-pin` | false / true; current observed caption only, run-only | `set display.speech-text-pin to true` |

All also have exact get/status/reset and `list speech text settings` discovery.

### Bounded natural speech-caption commands

Original typed input and current-name activated voice accept only the following whole utterances, case-insensitively, with an optional current assistant-name prefix. They map to the existing exact commands, not model inference; all phrases are included in recognition discovery and the configuration syntax help ([parser](../src/Kora.Application/Configuration/SpeechTextCommand.cs)).

| Whole utterance | Existing action and scope |
|---|---|
| `turn on speech captions`, `show speech captions`, `show speech text`, `show the words you're saying` | Save device-local `CurrentUtterance`; this is persistent opt-in, not immediate display or replay |
| `turn off speech captions`, `hide speech captions`, `hide speech text` | Save device-local `Off`; this is persistent opt-out, not one-shot hiding |
| `list speech caption settings`, `inspect speech caption settings`, `status speech captions` | Inspect existing saved/effective settings and recovery |
| `reset speech captions` | Save `Off` only; preserve placement and dismissal delay |
| `get/status/reset speech caption placement` | Inspect or reset only placement |
| `get/status/reset speech caption dismissal delay` | Inspect or reset only dismissal delay |
| `put speech captions in the bottom-right/bottom-left/top-right/top-left` | Save one of the four existing working-area corners; no automatic display selection |
| `keep speech text visible for <seconds> seconds` | Save canonical integer 0-30 seconds; use `1 second` for one, not number words |
| `pin current speech caption`, `unpin current speech caption` | Run-only pin/unpin of the eligible host-owned current caption; never a content selector |

The table abbreviates alternatives with `/`; a command contains one complete alternative. Unknown settings, trailing clauses, number words, noncanonical/out-of-range delays and vague `pin that text`/`unpin that text` clarify rather than falling through to inference or approval/question handling. Embedded or quoted instructions do not match ([grammar tests](../tests/Kora.Application.UnitTests/Configuration/NaturalSpeechCaptionCommandTests.cs)).

Aliases retain original input origin, saved-preference confirmation, session/generation/choice identity, ownership/privacy/call admission and typed audit/atomic/readback outcomes. Configuration does not speak, stop listening or change response mode; options do not opt in. Pinning uses the existing observed playback identity and never restores retired speech ([execution](../src/Kora.Application/ViewModels/MainViewModel.SpeechText.cs), [configuration service](../src/Kora.Application/Configuration/SpeechTextConfigurationService.cs)).

New speech, replacement, cancellation/failure, call/privacy/source/voice-generation changes and late callbacks retain the existing retirement rules. Activated input still uses the existing capture/interruption lifecycle; a caption retired by that lifecycle cannot be pinned afterward. No new playback, capture, re-reading, arbitrary response targeting, model/external-content authority or display-choice route is added ([caption lifetime](../src/Kora.Application/Voice/SpeechCaption.cs), [consumer tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.NaturalSpeechCaptions.cs)).

This is bounded Implementation command parity only. Installed-native/accessibility/display/DPI, hardware/acoustic/provider and Enablement/RC qualification remain separate; deterministic tests do not qualify those capabilities ([delivery policy](Implementation_Roadmap.md#delivery-and-qualification-dependency-policy)).

### Bounded native reader text scale

Appearance persistence runs off the dispatcher. During a known in-process save, new reader admission remains unavailable; existing Evidence content retains only its last confirmed typography and current private-content admission ([Evidence](../src/Kora/EvidenceWindow.axaml.cs)).

Captions temporarily hide until confirmation and independent source eligibility, without resetting retirement/failure state ([captions](../src/Kora/SpeechCaptionWindowController.cs)).

The delivered native reader preference is `appearance.reader-text-scale`: an absolute integer percentage, default **100**, inclusive **75–200**. It uses the appearance registry, exact `get`/`status`/`set`/`reset` and `list appearance settings`, and the native Settings Appearance editor ([registry](../src/Kora.Core/Configuration/AppearanceOptionRegistry.cs), [ID parser](../src/Kora.Application/Configuration/AppearanceCommand.cs)).

Delivered natural exact commands are `use dark mode`, `use light mode`, `use system theme`, `get/status reader text scale`, `reset reader text scale`, and `set reader text scale to <integer> percent`.
Original typed input or current-name-prefixed activated voice uses the same admitted appearance executor, not model interpretation
([grammar](../src/Kora.Application/Configuration/NaturalAppearanceCommand.cs), [executor](../src/Kora.Application/ViewModels/MainViewModel.Appearance.cs)).

`increase/decrease reader text scale by <positive integer> percentage points` reads fresh confirmed service state at execution and uses checked arithmetic plus registry validation.
The existing revision-bound proposal refuses concurrent changes rather than overwriting or recalculating. No clamp, guessed value, selected-window inference or new policy layer is introduced
([proposal](../src/Kora.Application/Configuration/AppearanceConfigurationService.cs), [exact examples](../docs/commands.md#natural-exact-app-theme-and-native-reader-text-scale)).

Only content typography scales: shared DetailWindow Rendered blocks, exact Source and continuous text; native original file/folder and retained attachment source bodies; exact source citation/literal-match spans; native volatile clipboard snapshot source body; admitted complete immutable serialized Evidence page body and its inert focused literal-Find span; currently observed native speech-caption content text. Newly created windows inherit the confirmed preference. Repeated changes apply against each control's original font/line-height, never cumulatively ([binding](../src/Kora/NativeReaderTypography.cs), [detail](../src/Kora/DetailWindow.axaml.cs), [file](../src/Kora/LocalFilePreviewWindow.cs), [span](../src/Kora/LocalFilePreviewWindow.Citations.cs), [clipboard](../src/Kora/ClipboardPreviewWindow.cs), [Evidence](../src/Kora/EvidenceWindow.axaml.cs), [caption](../src/Kora/SpeechCaptionWindow.axaml.cs)).

Trusted chrome, consent/approval/question controls, search inputs/results, provenance/citation locations, errors and renderer status retain their own typography and scrolling. This does not scale entire windows or transform content. Immutable source strings/bytes, search offsets and citations are not rewritten; content access, copy/export, caption enablement, call policy, permissions and persistence consent remain separate ([detail shell](../src/Kora/DetailWindow.axaml), [file reader](../src/Kora/LocalFilePreviewWindow.cs), [clipboard reader](../src/Kora/ClipboardPreviewWindow.cs)).

The preference is device-local atomic state under application data paths, with strict integer validation and pending evidence until exact readback and successful audit. Malformed, unknown, externally changed or unconfirmed evidence fails explicitly; it is not silently treated as 100. Existing preference files/migrations remain unchanged ([store](../src/Kora.Application/Configuration/LocalReaderTextScalePreferences.cs), [workflow](../src/Kora.Application/Configuration/AppearanceConfigurationService.cs)).

Excluded: inline guide/grant previews, compact response/conversation lists, Evidence filters/query fields/result navigation/provenance/status and incomplete pages, search/attachment catalogues, caption labels/Pin/Unpin/display selector/status/chrome, browser/generated HTML/Mermaid/PDF/image renderers, model/runtime interfaces and inferred voice commands. Caption scaling preserves exact text, response/playback/generation/segment identity, native geometry/display selection, original pin/dismissal lifetime and retirement. Preference/privacy/source/call/window closure, replacement, interruption, failure/cancellation and late callbacks clear stale scaled content. This is **not global text scaling**. Full accessibility, real hardware, Windows DPI/display and assistive-technology acceptance remain open; deterministic headless checks qualify only these bounded native readers.

The broader proposed registry includes:

| Preference | Default / choices | Example after "Kora" |
|---|---|---|
| Speech text | CurrentSentence exact controls/implementation are delivered but alignment is unqualified; word highlighting and a natural sentence alias remain proposed | "Show the current sentence" (proposed alias) |
| Speech-text dismissal | Number-word parsing remains proposed; canonical integer 0-30 commands are [delivered above](#bounded-natural-speech-caption-commands) | "Keep speech text visible for ten seconds" (proposed) |
| Caption placement | Arbitrary placement and voice display selection remain proposed; four corner aliases are [delivered above](#bounded-natural-speech-caption-commands) | "Put speech text on another display" (proposed) |
| Text scale | 100%; 75-200% | "Make result text twenty percent larger" |
| Detailed result opening | Offer (default) / Open automatically / Link only | "Always open detailed results" |
| Browser target | Embedded when verified; external only by explicit selection | "Use my default browser for websites" |
| Markdown mode | Rendered / source | "Show Markdown source by default" |
| Mermaid | Enabled only after integration gate; source alternative always available | "Show Mermaid diagrams as source" |

Broader proposed content commands include "show the full answer", "show this page", "preview that HTML", "show the source", "show the diagram", "zoom in", "go back", and "close the content viewer". `hide speech text` is a delivered persistent opt-out; caption pinning requires the explicit current-caption phrases above, never "pin that text" or "unpin that text" ([parser](../src/Kora.Application/Configuration/SpeechTextCommand.cs)).
Remote assets remain blocked by default; "load the images for this page" initiates an exact reviewed asset operation, not a blanket setting or approval for future documents.
Ambiguous "that" or multiple viewer targets requires clarification.
Scrolling/diagram zoom/navigation operate on displayed content only; they cannot automate page forms or execute webpage instructions.
An optional text overlay never hides mandatory approval/error UI.

## Delivery and Proof

Slice A includes optional speech text and bounded basic Markdown detail.
Mermaid follows its dedicated renderer gate. Embedded browsing/generated HTML are incremental display capabilities following isolation/network/lifecycle proof, not generic browser automation.
Basic text remains available if a rich renderer is missing; labelled fallback does not masquerade as successful rich rendering.
Delivery includes required renderer assets; runtime setup can offer only protected compatible browser-engine requirements with normal setup consent.
No cloud renderer/CDN is required for local Markdown/diagrams.
Presentation preferences do not change durable session retention, context-use eligibility, or authorize modification of Kora implementation.
