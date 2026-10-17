using System.Runtime.InteropServices;
using System.Text.Json;

using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Exact retained nullable receipt correlation, never current identity or authority.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct SessionHistoryCorrelationFilter(
    Guid? RequestId = null, Guid? TaskId = null, Guid? SourceId = null)
{
    public void Validate()
    {
        OptionalCanonicalGuid.Validate(RequestId);
        OptionalCanonicalGuid.Validate(TaskId);
        OptionalCanonicalGuid.Validate(SourceId);
    }

    public bool Matches(SessionHistoryEvent record) =>
        (RequestId is not { } request || record.RequestId == request)
        && (TaskId is not { } task || record.TaskId == task)
        && (SourceId is not { } source || record.SourceId == source);

    public static SessionHistoryCorrelationFilter Parse(string requestId, string taskId, string sourceId) =>
        new(OptionalCanonicalGuid.Parse(requestId), OptionalCanonicalGuid.Parse(taskId), OptionalCanonicalGuid.Parse(sourceId));

    internal string CanonicalValues => JsonSerializer.Serialize(this);
}
