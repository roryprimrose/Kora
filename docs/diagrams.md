# Passive Mermaid diagrams

Mermaid in an explicitly opened **Details** view remains exact, inert source.
Opening Details, selecting a block or receiving a model answer does not start
a diagram worker. For up to eight complete top-level `mermaid` fences, Details
offers **Render selected Mermaid diagram** and **Cancel diagram** separately
from conversation and approval controls.

**This build has no qualified native Mermaid runtime profile.** An explicit
render therefore reports **Unavailable** and retains the complete source.
The implementation and deterministic native tests do not claim installed,
accessibility, theme/DPI or release qualification. A document cannot enable it.

## Bounded native profile

The bundled `native-mermaid-v1` implementation is a deliberately small native
Mermaid grammar, not the full Mermaid JavaScript engine. Its parser, layout,
strict SVG serializer and output validator ship as application source in the
same pinned build. It needs no additional package, runtime download, browser,
JavaScript interpreter, font, plugin, CDN, remote renderer or host bridge.

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
Supported diagrams have a bounded deterministic native layout rather than
Mermaid.js layout fidelity. They are passive illustrations, not interactive
task controls.

## Limits and cancellation

The existing complete-document admission ceiling remains **256 KiB UTF-8**.
Each selected diagram has inclusive limits of **32 KiB UTF-8, 500 nodes and
1,000 edges**, and a **two-second** worker budget including parsing, layout,
SVG generation and output validation. Output is separately capped at 1 MiB.
Native/SVG coordinates are bounded to 131,072 units.
Limit, syntax, cancellation and availability outcomes are labelled; nothing is
truncated to manufacture success. More than eight blocks, incomplete fences,
nested fences and fences carrying extra metadata are not offered.

The native worker has no ambient I/O or executable document input. Its SVG
output is validated against a finite element/attribute/numeric allowlist;
scripts, events, HTML, foreign objects, namespaces, DTDs, links, styles, images
and external resources are refused as a whole. Native controls are constructed
from the same inert drawing, never by loading an arbitrary SVG/HTML document.

Cancel suppresses publication immediately but does not pretend the worker has
already stopped. Ownership remains held until it actually joins; another render
cannot overlap it. Closing Details, source/access loss or privacy closure
retires the generation, cancels the worker and clears native content before any
late completion can publish.

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
