using Kora.Core.Presentation.MarkdownStrikethrough;
using Kora.CodeHighlighting;
using System.Collections.Immutable;

namespace Kora;

internal sealed record NativeTextSection(string Text, NativeTextStyle Style)
{
    internal IReadOnlyList<MarkdownStrikethroughSpan> Strikethrough { get; init; } = [];
    internal CodeSource? Code { get; init; }
    internal string? HeadingId { get; init; }
    internal ImmutableArray<NativeDocumentLink> Links { get; init; } = [];
    internal NativeTablePart? Table { get; init; }
}
