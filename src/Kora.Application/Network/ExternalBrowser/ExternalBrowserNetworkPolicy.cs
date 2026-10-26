using Kora.Application.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Network.ExternalBrowser;

namespace Kora.Application.Network.ExternalBrowser;

public sealed class ExternalBrowserNetworkPolicy(
    ProviderModeConfigurationService providerMode, IExternalBrowserInternetAccess internet) : IExternalBrowserNetworkPolicy
{
    public ExternalBrowserNetworkAdmission Capture() =>
        providerMode.WithInitialMode((mode, current) =>
        {
            var allowed = mode is ModelProviderMode.LocalFirst or ModelProviderMode.HostedPreferred
                && internet.IsInternetAvailable;
            return new ExternalBrowserNetworkAdmission(allowed, () => allowed && current() && internet.IsInternetAvailable);
        });
}
