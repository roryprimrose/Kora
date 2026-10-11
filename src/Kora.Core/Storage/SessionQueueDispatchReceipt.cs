using Kora.Core.Hosting;
using Kora.Core.Tools;

namespace Kora.Core.Storage;

public sealed record SessionQueueDispatchReceipt(SessionQueueEntry Entry, ApplicationVersionObservation? Version,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    CapabilityReply? Result = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool ResultAvailableByExactStatus { get; init; }
}
