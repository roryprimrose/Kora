## Bounded pipe-table detail profile

The approved increment is readable complex Markdown in an existing immutable detail, not a browser, editor or parsing utility ([product admission](../../Design/MVP_Scope.md#product-admission-and-realignment)).

`native-text-v1` remains the default for the shared guide/grants renderer and other unchanged consumers. The detail controller requests the additive `native-pipe-tables-v2` profile, but composition leaves its availability false. Table sources therefore show explicit unavailable/exact-source fallback until affected-profile qualification is supplied ([renderer](../../src/Kora/NativeDetailRenderer.cs), [controller](../../src/Kora/DetailWindowController.cs)).

The candidate profile uses only Markdig pipe tables plus the existing CommonMark allowlist. It projects complete headers and numbered data rows into native text, repeating column number and header for each value. Empty headers/cells are labelled. It is not a visual grid, HTML or executable content ([table projection](../../src/Kora/NativeDetailRenderer.Tables.cs)).

The authoritative additive bounds are 32 rows per table including the header, eight columns, 128 cells per document including all headers, and 2,048 UTF-8 bytes per projected cell. The existing 256 KiB source/semantic, 512-block, 4,096-node/control and depth-32 bounds are unchanged ([table policy](../../src/Kora.Core/Presentation/NativeMarkdownTableProfile.cs), [shared policy](../../src/Kora.Core/Presentation/NativeDetailProfile.cs)).

The parser normalizes to maximum width without dropping excess cells. Padded cells, inconsistent delimiters, nested tables, unsupported nodes and exceeded bounds are rejected with complete source fallback ([validation](../../src/Kora/NativeDetailRenderer.Tables.cs), [shipped normalization](https://github.com/xoofx/markdig/blob/56e9c238584a44a169f174c881855c049768634c/src/Markdig/Extensions/Tables/Table.cs)).

Pipe-containing paragraphs outside cells conservatively fall back in the candidate profile, including malformed tables parsed as ordinary prose. This may reject innocent pipe prose; it does not change legacy rendering. No partial row projection is published on failure ([validation](../../src/Kora/NativeDetailRenderer.Tables.cs)).

Exact source, disclosure/copy policy, immutable reference deduplication, privacy closure and stale-generation rejection remain owned by the existing detail services. Table controls are inert accessible text, with no automatic clipboard surface in Details ([native controls](../../src/Kora/MarkdownDocumentRenderer.cs), [viewer state](../../src/Kora.Application/Presentation/DetailViewerState.cs), [closure](../../src/Kora/DetailWindow.axaml.cs)).

### Implementation and qualification

Deterministic tests cover table/source boundaries, malformed/unsupported fallback, accessible complete row names, native source switching, privacy closure and activity outcomes ([table tests](../../tests/Kora.Windows.IntegrationTests/NativeMarkdownTableTests.cs), [controller tests](../../tests/Kora.Windows.IntegrationTests/DetailWindowControllerTests.Tables.cs)).

These tests do not qualify the profile. Enablement requires applicable exact-profile native keyboard/screen-reader, theme/contrast, scaling and responsive bounded-content evidence. No installed accessibility or RC qualification is claimed, and no production composition enables tables in this increment ([qualification policy](../../Design/Acceptance_Criteria.md#three-tier-qualification-policy), [display acceptance](../../Design/Acceptance_Criteria.md)).
