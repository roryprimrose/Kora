namespace Kora.Application.Network.ExternalBrowser;

public interface IExternalBrowserNetworkPolicy
{
    ExternalBrowserNetworkAdmission Capture();
}
