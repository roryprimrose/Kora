using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

namespace Kora.Core.Commands;

public sealed record SessionCommandResult(string Outcome, string Message)
{
    public ImmutableArray<SessionCommandSession> Sessions { get; init; } = [];
    public ImmutableArray<SessionCommandTask> Tasks { get; init; } = [];
    public ImmutableArray<SessionCommandQuestion> Questions { get; init; } = [];
    public Guid? Next { get; init; }
    public ImmutableArray<HostTaskObservation> TaskDetails { get; init; } = [];
    public SessionHistoryPage? History { get; init; }
    public SessionHistoryEvent? HistoryEvent { get; init; }
    public SessionHistorySearchPage? HistorySearch { get; init; }
    public SessionQueueSnapshot? Queue { get; init; }
    public SessionWorkSnapshot? Work { get; init; }
    public SessionQueueEntry? QueueEntry { get; init; }
    public IReadOnlyList<SessionQueueDispatchReceipt>? QueueDispatch { get; init; }
    public static byte[] Serialize(SessionCommandResult result)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(result, Json);
        if (bytes.Length > SessionCommand.MaximumResultBytes)
        {
            throw new InvalidDataException("Session result exceeds 64 KiB. Retry with a smaller explicit page limit.");
        }
        return bytes;
    }

    public static int GetSerializedSize(SessionCommandResult result) =>
        JsonSerializer.SerializeToUtf8Bytes(result, Json).Length;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter<HostTaskState>(), new JsonStringEnumConverter<RequestOrigin>(),
            new JsonStringEnumConverter<SessionHistoryKind>(), new JsonStringEnumConverter<SessionHistoryAvailability>(),
            new JsonStringEnumConverter<HostInteractionOutcome>(), new JsonStringEnumConverter<QuestionStatus>(),
            new JsonStringEnumConverter<SessionQueueState>(), new JsonStringEnumConverter<SessionQueueEligibility>(),
            new JsonStringEnumConverter<QuestionKind>() },
    };
}
