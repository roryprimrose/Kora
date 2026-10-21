using Kora.Core.Context;

namespace Kora.Application.Presentation;

public sealed record AdmittedMetadataFindSnapshot(bool Available, string Query, string Status,
    SnapshotLiteralSpan? Match, bool CanNavigate, string ExactMatch, int Count, int Ordinal, bool HasCount)
{
    public static AdmittedMetadataFindSnapshot Unavailable { get; } = new(false, string.Empty, string.Empty,
        null, false, string.Empty, 0, 0, false);
}
