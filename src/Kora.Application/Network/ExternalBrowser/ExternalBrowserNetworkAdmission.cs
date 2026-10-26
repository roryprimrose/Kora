namespace Kora.Application.Network.ExternalBrowser;

public sealed record ExternalBrowserNetworkAdmission(bool Allowed, Func<bool> Current);
