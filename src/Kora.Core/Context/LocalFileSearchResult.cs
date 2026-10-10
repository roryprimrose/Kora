namespace Kora.Core.Context;

public sealed record LocalFileSearchResult(
    LocalFileSearchOutcome Outcome, DateTimeOffset ObservedAt,
    IReadOnlyList<LocalFileCitation> Citations, int MatchingChunks, bool Truncated)
{
    public string PolicyVersion { get; init; } = LocalFileRetrievalPolicy.Version;
    public LocalFileRetrievalMode Mode { get; init; } = LocalFileRetrievalMode.LexicalLines;

    public static LocalFileSearchResult Empty(LocalFileSearchOutcome outcome, DateTimeOffset observedAt) =>
        new(outcome, observedAt, Array.Empty<LocalFileCitation>(), 0, false);

    public static LocalFileSearchResult Empty(LocalFileSearchOutcome outcome, DateTimeOffset observedAt,
        LocalFileRetrievalMode mode)
    {
        _ = LocalFileRetrievalPolicy.TryVersion(mode, out var version);
        return Empty(outcome, observedAt) with { Mode = mode, PolicyVersion = version };
    }
}
