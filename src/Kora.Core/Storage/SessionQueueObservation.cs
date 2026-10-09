using Kora.Core.Hosting;

namespace Kora.Core.Storage;

public sealed record SessionQueueObservation(SessionQueueEntry Entry,
    SessionQueueEligibility Eligibility, DateTimeOffset? ActiveDeadline);
