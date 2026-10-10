namespace Kora.Core.Network;

public sealed record WebPageCapabilityDescriptor(
    string Id,
    int SchemaVersion,
    bool Available,
    string AvailabilityReason,
    int MaximumInputUtf8Bytes,
    int MaximumOutputUtf8Bytes,
    int MaximumContentBytes,
    int MaximumRedirects);
