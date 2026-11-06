using Kora.Core.Dependencies;

namespace Kora.Core.Tools;

public static class SessionMetricsTool
{
    public const string Id = "session.get_metrics";
    public const int MaximumModelRows = 4;

    public static bool Matches(CapabilityReply reply)
    {
        if (reply.SessionMetrics is not { } metrics) { return false; }
        metrics.Validate();
        if (metrics.Models.Length > MaximumModelRows
            || reply.Capabilities is not null || reply.Descriptor is not null || reply.Version is not null
            || reply.Readiness is not null || reply.Runtimes is not null || reply.Runtime is not null
            || reply.WebPage is not null || reply.Mcp is not null || reply.Sessions is not null
            || reply.Customizations is not null || reply.Settings is not null)
        { return false; }
        return true;
    }
}
