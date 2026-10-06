# Session Workspace and Coordinated Window Design

Status: agreed window structure; bounded native-text-v1 document details
implemented, with a bounded durable native question/review window; coordinated
Sessions/history layouts remain proposed.

Related: [Interaction and Sessions](Interaction_And_Sessions.md), [Work Management](Work_Management.md), [Information Display](Information_Display.md), [Shared Questions and Recovery](Interaction_Fallback.md), [User Configuration](User_Configuration.md), [Internal Model Tools](Internal_Model_Tools.md).

## Product Goal and Ownership

Kora is a concurrent work-session application with voice, mouse, and keyboard interaction, not a single linear conversation with a transient answer popup.
The compact window stays simple, but is a view into a durable selected session.
The larger **Sessions workspace contains the session list beside the selected session's full conversation**.
A separate detail/script viewer supports deeper review without losing the conversation or current question.
This document owns cross-window layout/navigation; the interaction contract owns lifecycle, routing, concurrency, and typed question semantics.
Security controls constrain these workflows; they are not a substitute for designing the workflows.

Voice-only, UI-only, and mixed operation use the same services and retained state, subject to explicit call-origin/secure-workflow exceptions.
Every session/task/question/artifact control names its actual target; ambient animation, window focus, and selected session never supply execution authority.

### Delivered Passive Details Boundary

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

The useful production entry is **Documentation > Open details**, not compact
response History. Its process-local page identity must not be portrayed as a
durable conversation. The typed finalized-response handoff exists, but its
durable resolver, native response dispatch, shared question/offer targeting and
Sessions workspace remain separately owned and uncomposed. Rich clipboard,
HTML, diagrams, source/diff language highlighting and export are not delivered.
Native visual/accessibility/DPI/multimonitor observations remain pending
separate approval.

## Window and Surface Map

### Delivered Bounded Question Window

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

## Compact Session Interaction

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
Its draft is isolated from named-session drafts. Explicit New session bypasses relatedness matching; a named-session composer never silently auto-routes elsewhere.
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
An All work view groups tasks by session and shows configured/effective slots, active tasks, ready/blocked queues, and cross-session resource conflicts.
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

Editing a Kora-owned declarative skill uses native editor/diff, validation,
dirty-state, conflict, save, and enablement controls. Shared-profile and bundled
revisions remain read-only; creating a fork is an explicit separate proposal.
Save and enable are separately confirmed revision-bound operations. Closing
with unsaved changes follows an explicit save/discard/cancel workflow and never
silently publishes a draft.

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
