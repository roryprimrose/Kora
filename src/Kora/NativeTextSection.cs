using Kora.Core.Presentation.MarkdownStrikethrough;
using Kora.CodeHighlighting;

namespace Kora;

internal sealed record NativeTextSection(string Text, NativeTextStyle Style)
{
    internal IReadOnlyList<MarkdownStrikethroughSpan> Strikethrough { get; init; } = [];
    internal CodeSource? Code { get; init; }
    internal IReadOnlyList<NativeExternalDocumentLink> ExternalLinks { get; init; } = [];
}
