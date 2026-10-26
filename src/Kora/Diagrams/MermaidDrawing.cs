namespace Kora.Diagrams;

internal sealed record MermaidDrawing(double Width, double Height,
    IReadOnlyList<MermaidBox> Boxes, IReadOnlyList<MermaidLine> Lines);
