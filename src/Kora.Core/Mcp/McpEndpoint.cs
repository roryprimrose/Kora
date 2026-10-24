using System.Security.Cryptography;
using System.Text;
using Kora.Core.Network;

namespace Kora.Core.Mcp;

/// <summary>An exact HTTPS destination, not connection, account or egress authority.</summary>
public sealed record McpEndpoint
{
    public McpEndpoint(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!address.IsAbsoluteUri || !string.Equals(address.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            || !string.IsNullOrEmpty(address.UserInfo) || !string.IsNullOrEmpty(address.Query)
            || !string.IsNullOrEmpty(address.Fragment) || address.AbsoluteUri.Length > 2048
            || address.IsLoopback)
        { throw new InvalidDataException("MCP requires an exact non-loopback HTTPS endpoint without credentials, query or fragment."); }
        if (System.Net.IPAddress.TryParse(address.Host, out var literal) && !WebPageNetworkPolicy.IsPublic(literal))
        { throw new InvalidDataException("The MCP endpoint is not a public destination."); }
        Address = address;
        Digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(address.AbsoluteUri)));
    }

    public Uri Address { get; }
    public string Digest { get; }

    // Destination details belong only in explicit native review, not implicit record formatting.
    public override string ToString() => nameof(McpEndpoint);
}
