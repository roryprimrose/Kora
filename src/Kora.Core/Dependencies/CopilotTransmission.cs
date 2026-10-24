using System.Collections.Immutable;
using Kora.Core.Providers;

namespace Kora.Core.Dependencies;

/// <summary>The final HTTP payload, not the earlier context envelope. Credentials are never review content.</summary>
public sealed class CopilotTransmission
{
    public const int MaximumBodyBytes = 131072;

    public CopilotTransmission(ModelTurnProvenance provenance, ProviderProfileTuple profile,
        Uri destination, ReadOnlyMemory<byte> body, IEnumerable<KeyValuePair<string, string>>? headers = null)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.IsAbsoluteUri || !string.Equals(destination.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            || destination.UserInfo.Length != 0 || destination.Fragment.Length != 0
            || body.Length is 0 or > MaximumBodyBytes)
        {
            throw new InvalidDataException("A hosted transmission requires a bounded body and an exact HTTPS destination.");
        }
        Provenance = provenance;
        Profile = profile;
        Destination = destination;
        Body = ImmutableArray.Create(body.ToArray());
        Headers = (headers ?? []).ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);
    }

    public ModelTurnProvenance Provenance { get; }
    public ProviderProfileTuple Profile { get; }
    public Uri Destination { get; }
    public ImmutableArray<byte> Body { get; }
    public ImmutableDictionary<string, string> Headers { get; }
}
