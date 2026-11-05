using Kora.Core.Providers;

namespace Kora.Core.Tools;

public sealed record ProviderSettingsResult(ProviderSettingsSnapshot? Settings = null, ProviderModelPage? Models = null,
    ProviderModelDetails? Model = null, ProviderServiceAvailability? Service = null, ProviderProfilePreferences? Proposal = null)
{
    public bool RequiresOriginalUserConfirmation => Proposal is not null;
    public bool PreferenceWritten => false;
}
