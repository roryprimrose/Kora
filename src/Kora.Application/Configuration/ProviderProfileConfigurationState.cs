using Kora.Core.Providers;

namespace Kora.Application.Configuration;

public sealed record ProviderProfileConfigurationState(long Revision, ProviderProfilePreferences? Saved,
    ProviderProfilePreferences? Desired, ProviderProfileTuple? Effective, string Status, string? Recovery,
    LocalProviderInstallation Installation, bool ConfigurationAvailable)
{
    public bool AuthenticationAvailable => false;
    public bool ExecutionEnabled => false;
}
