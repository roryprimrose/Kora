using System.Net;

namespace Kora.Core.Network.ExternalBrowser;

public sealed class ExternalBrowserDestination
{
    private ExternalBrowserDestination(Uri address) => Address = address;

    public Uri Address { get; }
    public string ExactAddress => Address.AbsoluteUri;

    public static ExternalBrowserDestination? Validate(string candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.Length > 8192 || candidate.Any(character => char.IsWhiteSpace(character)
            || char.IsControl(character) || character is '\\' or '\u202a' or '\u202b' or '\u202d' or '\u202e' or '\u202c'
                or '\u2066' or '\u2067' or '\u2068' or '\u2069')
            || !Uri.TryCreate(candidate, UriKind.Absolute, out var address)
            || !address.IsWellFormedOriginalString()
            || !string.Equals(address.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) || address.IsLoopback
            || address.UserInfo.Length != 0 || address.Fragment.Length != 0)
        {
            return null;
        }
        if (IPAddress.TryParse(address.IdnHost, out var ip))
        {
            if (!WebPageNetworkPolicy.IsPublic(ip)) { return null; }
        }
        else if (!address.IdnHost.Contains('.', StringComparison.Ordinal)
            || address.IdnHost.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || address.IdnHost.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var canonical = new UriBuilder(address) { Host = address.IdnHost }.Uri;
        return new(canonical);
    }
}
