namespace Kora.Core.Maintenance;

public sealed record ReleaseCheck(ReleaseAvailability Status, string Reason, DateTimeOffset AttemptedAt,
    DateTimeOffset? VerifiedAt = null, ReleaseMetadata? Release = null, DateTimeOffset? RetryAt = null);
