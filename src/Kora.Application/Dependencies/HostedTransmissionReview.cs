using System.Collections.Immutable;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Providers;

namespace Kora.Application.Dependencies;

/// <summary>Native-only Claude presentation of the shared one-send review state.</summary>
public sealed class HostedTransmissionReview
{
    internal HostedTransmissionReview(CopilotTransmissionReview review) { Review = review; }
    internal CopilotTransmissionReview Review { get; }
    public bool IsPending => Review.IsPending;
    public Uri Destination => Review.Transmission.Destination;
    public string Method => "POST";
    public ProviderProfileTuple Profile => Review.Transmission.Profile;
    public ImmutableArray<byte> Body => Review.Transmission.Body;
    public ImmutableDictionary<string, string> Headers => Review.Transmission.Headers;
    internal Func<HostActivity> Presentation => Review.Presentation;
}
