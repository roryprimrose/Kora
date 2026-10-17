using Kora.Core.Auditing;

namespace Kora.Core.Authorization;

/// <summary>A host-defined exact input label paired with its committed-event predicate.</summary>
public sealed record ExactGrantHistoryInitiatorOption
{
    internal ExactGrantHistoryInitiatorOption(SecurityAuditInitiator? value)
    {
        Value = value;
        Label = value?.ToString() ?? "Any";
    }

    public SecurityAuditInitiator? Value { get; }
    public string Label { get; }
}
