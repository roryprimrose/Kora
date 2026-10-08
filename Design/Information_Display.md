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

## Delivered Bounded Passive Session History - 2026-10-09

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
history search/model reasoning, Ask Evidence, queue or scheduler. Real installed
visual/screen-reader/DPI acceptance remains open.

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
passive detail viewer. Source, safe-text, session/task GUID and W3C trace
filters provide deterministic current-user SQLite inspection without a model,
network, microphone or browser. Search starts a fresh bounded snapshot;
Next page continues only that snapshot. Selecting a cited record exposes its
parent/explicit link availability; Read selected trace and Open selected
segment use the same typed query service, never paths or SQL.

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
Cut/paste and automatic TextBox/context clipboard paths are blocked; a query
over 256 characters is rejected visibly rather than silently shortened.

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
audited atomic configuration service. **CurrentUtterance** is the only enabled
mode in this slice: an ephemeral selectable native window shows the exact text
passed by the host's admitted response speech route, only while the provider
reports that matching playback identity/generation and utterance segment 0 as
actually playing. Synthesis, queued work, failed/suppressed output and voice
preview do not acquire caption authority.

Response replacement/retirement, playback finish/stop/cancel, configuration
revision, original request/session/channel, input recovery/activation generation,
call/privacy revision and current unlocked ownership gates retire or reject the
caption; late frames never restore it. The controller samples independently of
presence visibility at 50 ms, with immediate retirement on host transition paths.
No text enters preferences, logs, model requests or caption history. Required
native questions/approvals/errors and full visual fallback remain independent.
Changing this preference neither speaks/stops/replays output nor opens capture
or changes response/call policy. Unknown or unconfirmed preferences hold captions
off without disabling ordinary speech.

This intentionally clears immediately on finish/interruption rather than
retaining sensitive stale text. **Current sentence**, word alignment, pinning,
configurable dismissal delay, persisted placement/display selection and broad
caption/navigation commands below remain proposed. The initial window is fixed
at the primary working-area lower-right with a 24-DIP margin; it does not activate
on show or span the desktop as an input-catching overlay. Native accessibility
properties and selection are source-tested, not installed/accessibility or
acoustic qualification. Rich HTML/browser/diagram rendering is not delivered.
The acoustic speech experiment remains maintained and is not retired by R25.

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

All supported options use [User Configuration](User_Configuration.md), including:

| Preference | Default / choices | Example after "Kora" |
|---|---|---|
| Speech text | Off / sentence / utterance | "Show the words you're saying" |
| Speech-text dismissal | 5 seconds; 0-30 seconds | "Keep speech text visible for ten seconds" |
| Caption placement | Working-area corner/display; 24-DIP margin initially | "Put speech text in the top-left" |
| Text scale | 100%; 75-200% | "Make result text twenty percent larger" |
| Detailed result opening | Offer (default) / Open automatically / Link only | "Always open detailed results" |
| Browser target | Embedded when verified; external only by explicit selection | "Use my default browser for websites" |
| Markdown mode | Rendered / source | "Show Markdown source by default" |
| Mermaid | Enabled only after integration gate; source alternative always available | "Show Mermaid diagrams as source" |

Content commands include "show the full answer", "hide speech text", "pin that text", "unpin that text", "show this page", "preview that HTML", "show the source", "show the diagram", "zoom in", "go back", and "close the content viewer".
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
