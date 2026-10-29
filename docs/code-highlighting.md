# Offline code highlighting

Code blocks can have local syntax colors to make them easier to read. The
language and result label distinguish colored code from plain-text fallback.
No code is executed, compiled, validated,
formatted, sent to a server, or loaded as a plugin.

Use **Plain code** above a block to remove syntax colors for that view. In Details,
**Exact source** continues to show the complete admitted Markdown, including its
fences. Find, source copy, disclosure confirmation, digests and approval identity
continue to use the original admitted snapshot, not colored text. Private Details
code does not gain automatic clipboard access.

## Local final-code reading

When an exact final-answer Markdown profile is available, **Select literal code
locally (no copy)** focuses a read-only native code reader. Use Shift+arrows or
Ctrl+A there to select the literal code, including its original tabs, Unicode
and line breaks. **Wrap code** switches between wrapping and horizontal scrolling.
The same reader keeps its selection, caret and scroll position across resizing;
switching back restores the independently available syntax-colored view.

Selection is visual reading only. Ctrl+C, cut, paste, native context commands
and programmatic copy remain blocked; this does not grant clipboard permission
or change final-answer copy eligibility. Closing, replacing or withdrawing the
exact source clears its private text and selection. There is no editing, saved
buffer, script execution or network access.

Unavailable Markdown profiles still show the complete labelled source fallback,
not interactive code controls. Installed accessibility qualification remains
separate.

## Languages and limits

The offline alias table includes PowerShell (`ps1`), C# (`cs`), JSON, YAML (`yml`),
XML/XAML, Markdown (`md`), SQL, JavaScript (`js`), TypeScript (`ts`), HTML, CSS,
shell (`sh`, `bash`), batch (`bat`, `cmd`), INI/properties, and unified diff/patch.
These are bounded lexical colors, not complete compiler grammars. Diff keeps
literal added/removed/context markers; no underlying file language is inferred.
Inline code stays ordinary monospace text.

Only an allowlisted fence identifier chooses a language for Markdown. Unknown,
conflicting or absent identifiers remain plain code. Trusted host language and
immutable snapshot filename metadata have precedence in the shared policy;
document prose and document-supplied paths cannot provide that metadata. This
does not introduce a standalone file-opening route.

Each tokenization is limited to 32 KiB UTF-8, 1,024 lines, 2,048 tokens,
64 nested punctuation pairs and 100 ms. Invalid Unicode and exceeded limits
produce no partial token projection. The native view additionally limits
highlighting to 16 blocks and 32 Ki characters of code in total; larger views
remain ordinary monospace text. Native text admission still applies.

Work runs off the UI thread. Closing or retiring a view cancels its generation,
clears displayed code and rejects late results; owned workers remain joinable
until they actually finish. Light/Dark colors follow the effective native theme.
Plain text, punctuation, whitespace and diff markers remain available without
color. Screen readers receive source text, not a token-category narration.

Mermaid remains a separate, unavailable rendering profile; a Mermaid fence does
not load a diagram engine or select a syntax plugin. Syntax colors are
not installed screen-reader or accessibility qualification.
