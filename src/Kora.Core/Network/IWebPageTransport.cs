using System.Net;

namespace Kora.Core.Network;

public interface IWebPageTransport
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);

    Task<WebPageResponse> SendAsync(
        Uri address,
        IPAddress endpoint,
        CancellationToken cancellationToken);
}
