namespace Kora.Core.Tools;

public sealed record CapabilityInputResult(
    string? DenialReason, CapabilityPageInput? Page = null, CapabilityIdInput? Id = null);
