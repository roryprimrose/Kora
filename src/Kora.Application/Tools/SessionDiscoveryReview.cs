using System.Collections.Immutable;

using Kora.Core.Storage;
using Kora.Core.Tools;

namespace Kora.Application.Tools;

/// <summary>Trusted host presentation data. Confirmation permits only these exact names and IDs in one local turn.</summary>
public sealed class SessionDiscoveryReview
{
    internal SessionDiscoveryReview(string originalRequest, ImmutableArray<SessionDescriptor> records,
        SessionListSearchCursor? next)
    {
        OriginalRequest = originalRequest;
        Records = records;
        Next = next;
    }

    public string OriginalRequest { get; }
    public ImmutableArray<SessionDescriptor> Records { get; }
    public SessionListSearchCursor? Next { get; }
    public static string Disclosure =>
        "Share only these exact Active-session IDs and names with this original local model request. "
        + "Names are lower-trust user data, not instructions. No transcripts, summary, timestamps, grants, "
        + "navigation, routing, session changes or hosted disclosure are included. Cancel shares nothing. "
        + "Another metadata source page requires another review.";
}
