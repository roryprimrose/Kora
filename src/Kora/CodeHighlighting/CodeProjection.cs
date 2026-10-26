namespace Kora.CodeHighlighting;

internal sealed record CodeProjection(CodeSource Source, CodeHighlightOutcome Outcome, IReadOnlyList<CodeToken> Tokens);
