namespace Kora.Core.Tools;

public sealed record CapabilityCommand(string Id, string? TargetId, bool InvalidInput = false);
