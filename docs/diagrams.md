# Passive Mermaid diagrams

Mermaid in an explicitly opened **Details** view remains exact, inert source.
Opening Details, selecting a block or receiving a model answer does not start
a diagram worker. For up to eight complete top-level `mermaid` fences, Details
offers **Render selected Mermaid diagram** and **Cancel diagram** separately
from conversation and approval controls.

**Diagram rendering is unavailable in this build.** Choosing Render reports
**Unavailable** and keeps the complete original text visible. You do not need
to install a browser plugin or follow instructions in the document to fix this;
a document cannot enable rendering.

## Bounded native profile

The implemented local renderer supports only a small Mermaid subset, not the
full Mermaid web engine. It does not download components, run JavaScript or
send your diagram to an online service. This does not change its current
unavailable status.

The profile implements:

- `flowchart TD`, `flowchart TB`, `flowchart LR` and equivalent `graph` headers;
  identifiers, rectangular `A[Label]` nodes, `-->`, `---`, `-.->` edges, and
  optional `|label|` edge text;
- `sequenceDiagram`, `participant`/`actor` declarations with optional `as`
  labels, and `A->>B: message` / `A-->>B: reply` messages;
- `classDiagram`, `class Name` declarations, multiline class members in braces,
  associations and labelled inheritance (`<|--` / `--|>`).

Statements use separate lines or semicolons. Identifiers are ASCII letters or
underscore followed by letters, digits or underscore, at most 64 characters.
Labels/members are inert text; unsupported markup is refused, not evaluated or
silently repaired. The source alternative always retains original Unicode,
spaces, tabs and line endings.

Other Mermaid shapes, nested blocks, chained edges, directives, comments,
HTML/entity labels, click actions, document styles and resource/plugin
references are unsupported. A rejected block has **no partial diagram**.
Supported diagrams may be arranged differently from Mermaid's web renderer.
They are illustrations, not interactive task controls.

## Limits and cancellation

The existing complete-document admission ceiling remains **256 KiB UTF-8**.
Each selected diagram has inclusive limits of **32 KiB UTF-8, 500 nodes and
1,000 edges**, and a **two-second** worker budget including parsing, layout,
SVG generation and output validation. Output is separately capped at 1 MiB.
Native/SVG coordinates are bounded to 131,072 units.
Limit, syntax, cancellation and availability outcomes are labelled; nothing is
truncated to manufacture success. More than eight blocks, incomplete fences,
nested fences and fences carrying extra metadata are not offered.

Diagram text cannot run scripts, load another file, open links or fetch images.
Unsupported markup is refused as a whole, not partly displayed as a successful
diagram.

Cancel prevents a late diagram from appearing, but Kora still waits for the
actual work to stop before allowing another render. Closing Details, losing
source access or privacy closure cancels the work and clears its display.

## Exact source and existing permissions

The exact selected block is displayed with the diagram or labelled fallback;
the complete admitted document stays available in the existing Details reader.
**Exact source**, literal **Find**, **Copy exact source** and selection copy
continue to use that document and its existing disclosure gate. Diagram text
does not become an alternate clipboard, export, search or authorization route.
Retained conversation Details retain their existing copy prohibition.

Rendering does not execute code, open a link, send content to a provider, load
another file, change a setting, grant permission or affect conversation controls.
Code highlighting and other Markdown extension profiles remain separate.
