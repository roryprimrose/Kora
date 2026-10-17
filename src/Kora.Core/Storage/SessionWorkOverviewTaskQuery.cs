using System.Runtime.InteropServices;

using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Optional positive correlation on retained task observations only; never host authority.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct SessionWorkOverviewTaskQuery(Guid? TaskId = null, Guid? RequestId = null)
{
    public static SessionWorkOverviewTaskQuery Parse(string taskId, string requestId) =>
        new(OptionalCanonicalGuid.Parse(taskId), OptionalCanonicalGuid.Parse(requestId));

    public void Validate()
    {
        OptionalCanonicalGuid.Validate(TaskId);
        OptionalCanonicalGuid.Validate(RequestId);
    }
}
