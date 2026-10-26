namespace Kora.Core.Tools;

/// <summary>Reviewed Active-session metadata only; names and IDs are lower-trust observations, not authority.</summary>
public sealed record SessionDescriptor(string SessionId, string? Name);
