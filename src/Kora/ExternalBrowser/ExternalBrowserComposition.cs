using Kora.Application.Network.ExternalBrowser;
using Kora.Core.Network.ExternalBrowser;
using Kora.Windows.Network.ExternalBrowser;
using Microsoft.Extensions.DependencyInjection;

namespace Kora;

internal static class ExternalBrowserComposition
{
    internal static void Configure(IServiceCollection services)
    {
        services.AddSingleton<IExternalBrowserLauncher, WindowsExternalBrowserLauncher>();
        services.AddSingleton<IExternalBrowserInternetAccess, WindowsExternalBrowserInternetAccess>();
        services.AddSingleton<IExternalBrowserNetworkPolicy, ExternalBrowserNetworkPolicy>();
        services.AddSingleton<ExternalBrowserWorkflow>();
    }
}
