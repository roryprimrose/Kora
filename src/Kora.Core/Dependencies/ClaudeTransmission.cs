using System.Collections.Immutable;
using Kora.Core.Providers;

namespace Kora.Core.Dependencies;

/// <summary>Immutable actual Claude POST bytes and complete noncredential headers.</summary>
public sealed class ClaudeTransmission
{
    public const int MaximumBodyBytes = CopilotTransmission.MaximumBodyBytes;

    public ClaudeTransmission(ModelTurnProvenance provenance, ProviderProfileTuple profile, Uri destination,
        ReadOnlyMemory<byte> body, IEnumerable<KeyValuePair<string, string>> headers)
    {
        if (profile.Service != ProviderService.Claude || !AllowedDestination(destination))
        { throw new InvalidDataException("An exact Claude Messages destination and profile are required."); }
        var snapshot = new CopilotTransmission(provenance, profile, destination, body, headers);
        if (snapshot.Headers.TryGetValue("Host", out var host) && !string.Equals(host, destination.Authority, StringComparison.Ordinal)
            || snapshot.Headers.Keys.Any(key => key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
            || key.Equals("X-Api-Key", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Cookie", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            || snapshot.Headers.Any(pair => pair.Key.Any(char.IsControl) || pair.Value.Any(char.IsControl)))
        { throw new InvalidDataException("Credential, routing and cookie headers cannot enter disclosure data."); }
        Provenance = provenance; Profile = profile; Destination = destination;
        Body = snapshot.Body; Headers = snapshot.Headers;
    }

    public ModelTurnProvenance Provenance { get; }
    public ProviderProfileTuple Profile { get; }
    public Uri Destination { get; }
    public string Method => "POST";
    public ImmutableArray<byte> Body { get; }
    public ImmutableDictionary<string, string> Headers { get; }
    public static bool AllowedDestination(Uri uri) => uri.IsAbsoluteUri
        && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && uri.Query.Length == 0 && uri.Fragment.Length == 0
        && string.Equals(uri.Host, "api.anthropic.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath is "/v1/messages" or "/v1/messages/count_tokens";
}
