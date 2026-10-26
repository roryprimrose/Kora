namespace Kora.Diagrams;

internal sealed record MermaidGraph(MermaidKind Kind, bool Horizontal,
    IReadOnlyList<MermaidNode> Nodes, IReadOnlyList<MermaidEdge> Edges);
