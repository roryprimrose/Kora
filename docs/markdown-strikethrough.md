# Passive Markdown strikethrough

Strikethrough is display-only wording, for example wording an answer describes as
removed or superseded. It is not a diff, completed task, approval, permission,
editing command or tool action.

## Availability

The bounded `native-markdown-strikethrough-v4` profile is implemented for the
immutable Details viewer. It is independently unavailable by default pending
exact-profile enablement evidence. An explicitly opened finalized Markdown
answer requests this host-selected profile and currently shows labelled exact
source fallback instead of claiming a qualified styled preview.

Documents, models, skills and extensions cannot enable the profile or install
renderers. Existing plain text, table and checklist profiles remain separate;
this profile does not enable tables, task lists, diagrams, HTML, images, assets,
content plugins or network requests.

## Reading and exact source

With an enabled host profile, double tildes around wording, such as
`~~old wording~~`, produce a line through that wording and the visible text
`[strikethrough: old wording]`. The same explicit label is available to assistive
technology and in Continuous text. Color alone never carries the distinction.
Ordinary emphasis remains plain semantic text. Unmatched tildes, escaped tildes,
inline code, indented code and fenced code are literal, not strikethrough.
Offline code highlighting remains a separate bounded annotation of unchanged
code source. Its plain-code fallback does not remove wording labels elsewhere
in the view, and neither annotation enables another Markdown profile.

Open the existing Details view explicitly. Use **Exact source** to inspect the
unchanged original text, including delimiters, Unicode and line endings.
**Continuous text** reads the labelled projection. Literal Find searches only
the selected projection; changing projections resets its match count and cursor.
The original item, revision and content digest do not change.

Copy remains subject to the existing native disclosure policy. A styled
projection grants no clipboard access; finalized-answer Details still block
copy. No execution or editing action is attached to the wording.

## Bounds and retirement

The existing limits remain: 256 KiB strict UTF-8 source and semantic text,
512 blocks, 4,096 nodes and nesting depth 32. Labels count toward the semantic
byte budget. Unsupported content, unknown or unavailable profiles and exceeded
parser/projection bounds show labelled whole-source fallback, never a partial
successful preview. Invalid Unicode or source above the admission ceiling is
refused without truncation.

Source loss, privacy closure and disposal retire the viewer's source, labelled
text, Find state and native inline/accessibility content. Nothing is saved,
sent to a provider, fetched, executed or copied by rendering.
Code blocks in the same view retire their readers and pending highlighting
alongside the strikethrough wording.

This is bounded deterministic implementation, not installed accessibility,
device, provider or release qualification.
