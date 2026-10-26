using Kora.Core.Presentation.MarkdownStrikethrough;

using Markdig.Syntax.Inlines;

namespace Kora;

internal sealed class InlineStrikethroughProjection(int prefixLength)
{
    private readonly Dictionary<EmphasisInline, int> starts = [];
    private readonly List<MarkdownStrikethroughSpan> spans = [];

    internal IReadOnlyList<MarkdownStrikethroughSpan> Spans => spans.AsReadOnly();

    internal bool Enter(ContainerInline inline, BoundedNativeText text)
    {
        if (inline is not EmphasisInline { DelimiterChar: '~', DelimiterCount: 2 } strike) { return false; }
        text.Append(NativeMarkdownStrikethroughProfile.StartLabel);
        starts.Add(strike, text.Length);
        return true;
    }

    internal bool Exit(Inline inline, BoundedNativeText text)
    {
        if (inline is not EmphasisInline strike || !starts.Remove(strike, out var start)) { return false; }
        spans.Add(new(strike.Span.Start, strike.Span.Length, prefixLength + start, text.Length - start));
        text.Append(NativeMarkdownStrikethroughProfile.EndLabel);
        return true;
    }
}
