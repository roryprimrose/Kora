namespace Kora.Core.Context;

public sealed record LocalFileSearchResult(
    LocalFileSearchOutcome Outcome, DateTimeOffset ObservedAt,
    IReadOnlyList<LocalFileCitation> Citations, int MatchingChunks, bool Truncated)
{
    public string PolicyVersion => LocalFileRetrievalPolicy.Version;

    public static LocalFileSearchResult Empty(LocalFileSearchOutcome outcome, DateTimeOffset observedAt) =>
        new(outcome, observedAt, Array.Empty<LocalFileCitation>(), 0, false);
}
