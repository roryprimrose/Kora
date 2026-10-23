using System.Collections.Immutable;

using Kora.Core.Tools;

namespace Kora.Core.Dependencies;

/// <summary>Versioned host constraints for one serial local turn; content cannot widen them.</summary>
public sealed class ModelTurnConstraints
{
    public ModelTurnConstraints(int version, ImmutableArray<string> toolIds, int maximumCalls, int maximumRounds,
        int inputUtf8Bytes, int outputUtf8Bytes, int deadlineMilliseconds)
    {
        if (version != 1 || toolIds.IsDefault || toolIds.Length > ReadOnlyCapabilityCatalog.Descriptors.Count
            || toolIds.Distinct(StringComparer.Ordinal).Count() != toolIds.Length
            || toolIds.Any(id => ReadOnlyCapabilityCatalog.FindExact(id) is null)
            || maximumCalls < 0 || maximumCalls > ModelToolProtocol.MaximumCalls
            || toolIds.IsEmpty != (maximumCalls == 0)
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
        if (mode != (ToolIds.IsEmpty ? ModelToolMode.None : ModelToolMode.ReadOnly))
        { throw new InvalidDataException("Bounded local constraints do not admit this tool mode."); }
        return ToolIds.Select(id => ReadOnlyCapabilityCatalog.FindExact(id)!).ToImmutableArray();
    }
}
