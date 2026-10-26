using Kora.Core.Network.ExternalBrowser;
using Kora.Core.Presentation;

namespace Kora.Application.Network.ExternalBrowser;

public sealed class ExternalBrowserReview
{
    internal ExternalBrowserReview(WebResultSnapshot snapshot, ExternalBrowserDestination destination,
        Func<bool> current, ExternalBrowserNetworkAdmission network, DateTimeOffset expiresAt)
    {
        Snapshot = snapshot;
        Destination = destination;
        Current = current;
        Network = network;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public ExternalBrowserDestination Destination { get; }
    public const string Disclosure = "External website. Open this exact HTTPS destination once in the Windows registered browser. "
        + "Your external browser's identity, signed-in accounts, cookies, permissions and networking are user/browser-managed, "
        + "not protected by Kora's embedded sandbox. The website may receive IP, device and browser metadata and URL parameters. "
        + "No conversation, clipboard or page text is sent by Kora. This does not read, scrape or summarise the page. "
        + "Kora cannot stop or close the external browser or its background requests, including on lock, cancellation or privacy closure.";
    internal WebResultSnapshot Snapshot { get; }
    internal Func<bool> Current { get; }
    internal ExternalBrowserNetworkAdmission Network { get; }
    internal DateTimeOffset ExpiresAt { get; }
}
