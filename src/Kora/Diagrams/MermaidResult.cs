namespace Kora.Diagrams;

internal sealed record MermaidResult(MermaidFailure Failure, MermaidDrawing? Drawing = null, string? Svg = null);
