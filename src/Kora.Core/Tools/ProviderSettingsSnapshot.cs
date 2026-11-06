using Kora.Core.Providers;

namespace Kora.Core.Tools;

public sealed record ProviderSettingsSnapshot(ProviderProfilePreferences Desired, bool IsSaved,
    ProviderProfileTuple? Effective, LocalProviderInstallation Installation, IReadOnlyList<ProviderServiceAvailability> Services);
