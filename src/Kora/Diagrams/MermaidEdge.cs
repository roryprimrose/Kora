namespace Kora.Diagrams;

internal sealed record MermaidEdge(int From, int To, string Label, bool Dashed, bool Arrow);
