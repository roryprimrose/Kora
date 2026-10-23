using System.Collections.Immutable;

namespace Kora.Core.Presentation;

public sealed class StaticHtmlDocument
{
    internal StaticHtmlDocument(string source, string semanticText, ImmutableArray<StaticHtmlSection> sections,
        StaticHtmlFailure failure, int blocks, int nodes, int depth, int entityReferences)
    {
        Source = source;
        SemanticText = semanticText;
        Sections = sections;
        Failure = failure;
        BlockCount = blocks;
        NodeCount = nodes;
        Depth = depth;
        EntityReferenceCount = entityReferences;
    }

    public string Source { get; }
    public string SemanticText { get; }
    public ImmutableArray<StaticHtmlSection> Sections { get; }
    public StaticHtmlFailure Failure { get; }
    public bool IsFallback => Failure != StaticHtmlFailure.None;
    public int BlockCount { get; }
    public int NodeCount { get; }
    public int Depth { get; }
    public int EntityReferenceCount { get; }
}
