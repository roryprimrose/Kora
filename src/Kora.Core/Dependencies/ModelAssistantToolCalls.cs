using System.Collections.Immutable;

namespace Kora.Core.Dependencies;

public sealed class ModelAssistantToolCalls : ModelAssistantMessage
{
    internal ModelAssistantToolCalls(ImmutableArray<ModelToolCall> calls) => Calls = calls;
    public ImmutableArray<ModelToolCall> Calls { get; }
}
