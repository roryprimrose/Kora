using Kora.Core.Hosting;

namespace Kora.Core.Network;

/// <summary>Inert, lower-trust page text; neither its text nor provenance confers execution authority.</summary>
public sealed record LocalWebPageResult(
    HostRequest Request,
    Uri RequestedAddress,
    Uri FinalAddress,
    DateTimeOffset RetrievedAt,
    string Text,
    bool Truncated,
    string MediaType,
    bool LocalOnly = true);
