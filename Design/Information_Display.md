# Information Display and Rich Content

Status: proposed for general model/result rendering. The trusted embedded
end-user guide now has a bounded native Markdig/Avalonia renderer; generated,
remote, and skill-provided Markdown remain governed by the proposed contract
below and are not accepted by that documentation surface.

Related: [Ambient UI](Ambient_UI.md), [User Configuration](User_Configuration.md), [Security](Security_Data_Flows.md), [Extensibility](Extensibility.md).

## Recommended Surfaces

Keep the ambient presence small; do not turn every answer into a browser window.
Use three independently controlled surfaces:

1. Optional speech-text overlay for the words Kora is currently speaking.
2. Expandable answer/detail panel for text, Markdown, diagrams, citations, and task information.
3. Dedicated content viewer for explicitly requested websites or generated HTML, with native trusted navigation/provenance controls.

Showing/hiding one surface does not cancel work, mute the microphone, or change another surface's visibility.
Approvals, safety warnings, and unresolved errors remain separate trusted host panels, not optional captions or content inside a webpage.
Native question cards support mouse answers and microphone selection/recovery as defined in [Interaction Fallback](Interaction_Fallback.md), even when speech text is off or voice is unavailable.
Content viewing is not browser automation or implicit permission to capture a webpage as model context.

## Shared Appearance Theme

Every Kora-owned visual surface uses the single persisted application theme:
System (default), Light, or Dark. System follows the effective Windows
light/dark preference while Kora is running. Light and Dark override it.
The constellation, chat and answer windows, speech text, native questions,
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

Expose a voice-settable `speechText` preference: off (initial ambient default), current sentence, or current utterance.
It displays the exact final text submitted to TTS, not the entire answer, user transcription, or a newly generated paraphrase.
Bind text to response ID, playback generation, and segment so interruption/replacement cannot leave stale captions attached to new speech.
Word highlighting is optional only when the selected engine provides verified alignment events; otherwise show sentence/utterance text without claiming word accuracy.

The overlay follows actual playback: do not show queued, suppressed, cancelled, or failed speech as though spoken.
On interruption/finish, clear after a configurable 5-second default delay (0-30 seconds), unless pinned.
Pinned content is labelled previous speech, remains memory-only, and follows conversation expiry.
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

The host accepts bounded items with task/response ID, item ID, revision, kind, content/source reference, provenance/classification, digest, and supported profile.
Kinds include plain text, Markdown, static HTML snapshot, and approved browser destination.
The host chooses surfaces and validates capabilities; model hints cannot force topmost windows, auto-open links, grant network access, or dismiss approval panels.
An arbitrary HTML "Approve" button is inert content, not a real approval.
Native chrome stays outside content so pages cannot cover or replace trust indicators.
Generated summaries/captions inherit source restrictions; viewing content is not permission to send it to another destination.

## Voice Settings and Navigation

All supported options use [User Configuration](User_Configuration.md), including:

| Preference | Default / choices | Example after "Kora" |
|---|---|---|
| Speech text | Off / sentence / utterance | "Show the words you're saying" |
| Speech-text dismissal | 5 seconds; 0-30 seconds | "Keep speech text visible for ten seconds" |
| Caption placement | Working-area corner/display; 24-DIP margin initially | "Put speech text in the top-left" |
| Text scale | 100%; 75-200% | "Make result text twenty percent larger" |
| Detailed result opening | Explicit request; may enable automatic detail for requested answers | "Show detailed answers automatically" |
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
Presentation preferences do not affect memory-only conversation retention or authorise modification of Kora implementation.
