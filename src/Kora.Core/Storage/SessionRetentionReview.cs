using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>A memory-only exact retention review, not authority to change a hold.</summary>
public sealed record SessionRetentionReview(Guid Id, SessionRetentionObservation Observation,
    bool Keep, RequestOrigin Origin, DateTimeOffset ExpiresAt);
