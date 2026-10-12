using System.Text.Json.Serialization;

using Kora.Core.Hosting;

namespace Kora.Core.Storage;

public sealed record SessionQueueObservation(SessionQueueEntry Entry,
    SessionQueueEligibility Eligibility, DateTimeOffset? ActiveDeadline)
{
    public int? PendingOrder { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HostTaskMetadata? TaskMetadata { get; init; }
}
