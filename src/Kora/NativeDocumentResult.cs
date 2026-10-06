namespace Kora;

internal sealed record NativeDocumentResult(
    string Source,
    string SemanticText,
    string Status,
    bool IsFallback,
    IReadOnlyList<NativeTextSection> Sections,
    int BlockCount = 0,
    int NodeCount = 0,
    int Depth = 0);
