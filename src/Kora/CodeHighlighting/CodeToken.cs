namespace Kora.CodeHighlighting;

internal sealed record CodeToken(int Start, int Length, CodeTokenKind Kind);
