using System.Collections.Immutable;

using Kora.Core.Tools;

namespace Kora.Core.Dependencies;

/// <summary>Versioned host constraints for one serial local turn; content cannot widen them.</summary>
public sealed class ModelTurnConstraints
{
    public ModelTurnConstraints(int version, ImmutableArray<string> toolIds, int maximumCalls, int maximumRounds,
        int inputUtf8Bytes, int outputUtf8Bytes, int deadlineMilliseconds)
    {
        if (version is not (1 or 2) || toolIds.IsDefault || toolIds.Length > ReadOnlyCapabilityCatalog.Descriptors.Count
            || toolIds.Distinct(StringComparer.Ordinal).Count() != toolIds.Length
            || toolIds.Any(id => version == 1 ? ReadOnlyCapabilityCatalog.FindExact(id) is null
                : !Mcp.KoraMcpCatalog.ToolIds.Contains(id, StringComparer.Ordinal)
                    || string.Equals(id, ModelToolProtocol.AskUserToolName, StringComparison.Ordinal))
            || maximumCalls < 0 || maximumCalls > ModelToolProtocol.MaximumCalls
            || (version == 1 ? toolIds.IsEmpty != (maximumCalls == 0) : !toolIds.IsEmpty && maximumCalls == 0)
            || maximumRounds < 1 || maximumRounds > maximumCalls + 1
            || inputUtf8Bytes < 1 || inputUtf8Bytes > ModelToolProtocol.MaximumInputUtf8Bytes
            || outputUtf8Bytes < 1 || outputUtf8Bytes > ModelToolProtocol.MaximumOutputUtf8Bytes
            || deadlineMilliseconds < 1 || deadlineMilliseconds > ModelToolProtocol.Deadline.TotalMilliseconds)
        { throw new InvalidDataException("The complete turn constraints exceed the supported host contract."); }
        Version = version;
        ToolIds = toolIds;
        MaximumCalls = maximumCalls;
        MaximumRounds = maximumRounds;
        InputUtf8Bytes = inputUtf8Bytes;
        OutputUtf8Bytes = outputUtf8Bytes;
        DeadlineMilliseconds = deadlineMilliseconds;
    }

    public int Version { get; }
    public ImmutableArray<string> ToolIds { get; }
    public int MaximumCalls { get; }
    public int MaximumRounds { get; }
    public int InputUtf8Bytes { get; }
    public int OutputUtf8Bytes { get; }
    public int DeadlineMilliseconds { get; }

    public IReadOnlyList<CapabilityDescriptor> GetTools(ModelToolMode mode)
    {
        if (Version == 1 && mode != (ToolIds.IsEmpty ? ModelToolMode.None : ModelToolMode.ReadOnly)
            || Version == 2 && mode is ModelToolMode.LocalWebPage or ModelToolMode.McpReadOnly or ModelToolMode.HostedClarification)
        { throw new InvalidDataException("Bounded constraints do not admit this tool mode."); }
        var available = ModelToolProtocol.GetTools(mode);
        if (ToolIds.Any(id => !available.Any(tool => string.Equals(tool.Id, id, StringComparison.Ordinal)))
            || mode == ModelToolMode.None && MaximumCalls != 0)
        { throw new InvalidDataException("Bounded local constraints do not admit this tool mode."); }
        return ToolIds.Select(id => available.Single(tool => string.Equals(tool.Id, id, StringComparison.Ordinal))).ToImmutableArray();
    }
}
