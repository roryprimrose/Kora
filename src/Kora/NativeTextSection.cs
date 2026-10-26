using Kora.Core.Presentation.MarkdownStrikethrough;

namespace Kora;

internal sealed record NativeTextSection(string Text, NativeTextStyle Style)
{
    internal IReadOnlyList<MarkdownStrikethroughSpan> Strikethrough { get; init; } = [];
}
