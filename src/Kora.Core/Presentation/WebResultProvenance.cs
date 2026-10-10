using Kora.Core.Hosting;

namespace Kora.Core.Presentation;

public sealed record WebResultProvenance(
    HostRequest OriginalRequest,
    long ControlGeneration,
    string RequestedAddress,
    string FinalAddress,
    string MediaType,
    int RedirectCount,
    bool Truncated,
    DateTimeOffset RetrievedAt,
    string ReturnedTextSha256,
    int ReturnedTextUtf8Bytes,
    int MaximumRawContentUtf8Bytes,
    int MaximumNormalizedTextUtf8Bytes,
    int MaximumSerializedToolResultUtf8Bytes,
    int MaximumNativeSourceUtf8Bytes);
