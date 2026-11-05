using Kora.Core.Dependencies;
using Kora.Core.Tools;

namespace Kora.Tools.Sessions;

/// <summary>Projects only the exact host-bound session observation; accepts no model-selected session identity.</summary>
public sealed class SessionMetricsGet
{
    internal static CapabilityReply Execute(SessionModelMetrics metrics, string input)
    {
        var descriptor = ReadOnlyCapabilityCatalog.FindExact(SessionMetricsTool.Id)!;
        if (CapabilityInputPolicy.Read(descriptor, input).DenialReason is { } denial)
        { return new(CapabilityOutcome.Denied, denial); }
        metrics.Validate();
        var bounded = metrics with
        {
            Models = [.. metrics.Models.Take(SessionMetricsTool.MaximumModelRows)],
            OmittedModels = Math.Max(0, metrics.Models.Length - SessionMetricsTool.MaximumModelRows),
        };
        var reply = new CapabilityReply(CapabilityOutcome.Succeeded, "observed-since-process-start", SessionMetrics: bounded);
        return CapabilityReplyWire.IsWithinLimit(reply) ? reply : new(CapabilityOutcome.Failed, "output-limit-exceeded");
    }
}
