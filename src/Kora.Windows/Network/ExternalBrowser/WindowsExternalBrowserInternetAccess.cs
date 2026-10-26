using Kora.Core.Network.ExternalBrowser;
using Windows.Networking.Connectivity;

namespace Kora.Windows.Network.ExternalBrowser;

public sealed class WindowsExternalBrowserInternetAccess : IExternalBrowserInternetAccess
{
    public bool IsInternetAvailable => NetworkInformation.GetInternetConnectionProfile()?.GetNetworkConnectivityLevel()
        == NetworkConnectivityLevel.InternetAccess;
}
