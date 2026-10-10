namespace Kora.Core.Dependencies;

/// <summary>The host-owned session's provider preference, not permission to disclose content.</summary>
public enum ModelProviderMode
{
    Unknown,
    LocalOnly,
    LocalFirst,
    HostedPreferred,
}
