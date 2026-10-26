using Kora.Core.Presentation;

using Markdig;
using Markdig.Extensions.EmphasisExtras;

namespace Kora;

internal sealed partial class NativeDetailRenderer
{
    private static readonly MarkdownPipeline StrikethroughPipeline = new MarkdownPipelineBuilder
    {
        MaximumNestingDepth = NativeDetailProfile.MaximumNesting,
    }.UseEmphasisExtras(EmphasisExtraOptions.Strikethrough).UsePreciseSourceLocation().Build();
}
