using System.Runtime.CompilerServices;
using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
    private readonly ConditionalWeakTable<ModelTurn, CapabilityUseRevision> capabilityUse = new();
    private sealed record CapabilityUseRevision(string Value);

    private void CaptureCapabilityUse(ModelTurn turn)
    {
        if (capabilities is not null && UsesBuiltInCapabilities(turn))
        { capabilityUse.Add(turn, new(capabilities.Revision)); }
    }

    private bool CapabilityUseCurrent(ModelTurn turn) => capabilities is null
        || !UsesBuiltInCapabilities(turn)
        || capabilityUse.TryGetValue(turn, out var captured)
            && string.Equals(captured.Value, capabilities.Revision, StringComparison.Ordinal);

    private static bool UsesBuiltInCapabilities(ModelTurn turn) =>
        turn.Context.ToolMode is not (ModelToolMode.None or ModelToolMode.McpReadOnly);
}
