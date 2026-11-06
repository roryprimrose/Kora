using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

/// <summary>Volatile inert values, not a preference preview or permission to write.</summary>
internal sealed record ProviderSettingsSuggestion(ProviderProfilePreferences Proposed, ProviderProfilePreferences? Base,
    ProviderSettingsInput Input, DateTimeOffset ExpiresAt, HostRequest Source);
