using System.Collections.Immutable;

namespace Kora.Core.Dependencies;

/// <summary>Explicit, finite selection of the original host-owned observation capabilities.</summary>
public static class ClaudeObservationTools
{
    public static ModelTurnConstraints Constrain(IEnumerable<string> ids) =>
        new(1, ids.Take(Tools.ReadOnlyCapabilityCatalog.MaximumRecords + 1).ToImmutableArray(),
            ModelToolProtocol.MaximumCalls, ModelToolProtocol.MaximumRounds,
            ModelToolProtocol.MaximumInputUtf8Bytes, ModelToolProtocol.MaximumOutputUtf8Bytes,
            (int)ModelToolProtocol.Deadline.TotalMilliseconds);

    public static bool Supports(ModelContextEnvelope context) =>
        context.Evidence.IsEmpty && context.McpTool is null
        && (context.ToolMode is ModelToolMode.None or ModelToolMode.HostedClarification
            || context.ToolMode == ModelToolMode.ReadOnly && context.Constraints is not null);
}
