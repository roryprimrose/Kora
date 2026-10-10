using Kora.Core.Context;

namespace Kora.Core.Storage;

/// <summary>Native metadata review binding the exact old inventory and a fresh picked source, without old content.</summary>
public sealed record SessionFileReplacement(SessionFileRemoval Previous, LocalFileMetadata PreviousMetadata,
    DateTimeOffset PreviousCapturedAt, LocalFileReview Next);
