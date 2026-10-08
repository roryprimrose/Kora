namespace Kora.Core.Context;

public sealed record LocalFileCitation(
    LocalFileReference Source, string DisplayIdentity, int Start, int Length,
    int StartLine, int StartColumn, int EndLine, int EndColumn,
    string? Heading, string Excerpt, int MatchedTerms, int BoundedFrequency);
