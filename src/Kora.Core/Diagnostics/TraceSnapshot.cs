using System.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Hosting;

namespace Kora.Core.Diagnostics;

public sealed record TraceSnapshot(
    string TraceId, string SpanId, string? ParentSpanId, ActivityTraceFlags Flags,
    string Source, string? SourceVersion, string Name, ActivityKind Kind)
{
    public static TraceSnapshot? Capture(Activity? activity) => activity is null ? null :
        new(activity.TraceId.ToHexString(), activity.SpanId.ToHexString(),
            activity.ParentSpanId == default ? null : activity.ParentSpanId.ToHexString(),
            activity.ActivityTraceFlags, activity.Source.Name, activity.Source.Version,
            activity.OperationName, activity.Kind);
}
