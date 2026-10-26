using Kora.CodeHighlighting;

namespace Kora;

internal sealed record NativeTextSection(string Text, NativeTextStyle Style)
{
    internal CodeSource? Code { get; init; }
}
