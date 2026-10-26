using Kora.Core.Presentation;

using Markdig;
using Markdig.Syntax;

namespace Kora.Diagrams;

internal static class MermaidFences
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder
    {
        MaximumNestingDepth = NativeDetailProfile.MaximumNesting,
    }.Build();

    internal static IReadOnlyList<MermaidSource> Read(AdmittedDetailContent content)
    {
        // Admission, Unicode and the complete-document limit belong to the existing host snapshot.
        if (content.Kind != DetailContentKind.Markdown) { return []; }
        MarkdownDocument document;
        try { document = Markdown.Parse(content.Source, Pipeline); }
        catch (InvalidOperationException) { return []; }
        List<MermaidSource> sources = [];
        foreach (var block in document.OfType<FencedCodeBlock>())
        {
            if (!string.Equals(block.Info, "mermaid", StringComparison.OrdinalIgnoreCase)) { continue; }
            var span = content.Source.AsSpan(block.Span.Start, block.Span.Length);
            var firstNewline = span.IndexOf('\n');
            var lastNewline = span.TrimEnd("\r\n").LastIndexOf('\n');
            if (firstNewline < 0 || lastNewline < firstNewline) { continue; }
            var openingInfo = span[..firstNewline].Trim().TrimStart(block.FencedChar);
            if (!openingInfo.Equals("mermaid", StringComparison.OrdinalIgnoreCase)) { continue; }
            var closing = span[(lastNewline + 1)..].Trim();
            if (closing.Length < block.OpeningFencedCharCount
                || closing.IndexOfAnyExcept(block.FencedChar) >= 0) { continue; }
            var start = block.Span.Start + firstNewline + 1;
            var length = lastNewline + 1 - (firstNewline + 1);
            sources.Add(new(content.Source.Substring(start, length), start, length));
            if (sources.Count > MermaidProfile.MaximumDiagrams) { return []; }
        }
        return sources.AsReadOnly();
    }
}
