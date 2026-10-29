namespace Kora.Core.Configuration;

/// <summary>An opaque platform observation, not permission to launch or acquire resources.</summary>
public sealed record LogonStartupRegistration(bool Available, bool? Enabled, string Identity, string? UnavailableReason);
