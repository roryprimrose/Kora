namespace Kora.Application.Dependencies;

/// <summary>A trusted claim on a canonical physical resource.</summary>
internal sealed record RuntimeResourceClaim(RuntimeResourceId Resource, bool Exclusive);