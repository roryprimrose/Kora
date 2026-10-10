namespace Kora.Core.Network;

public static class WebPageCapability
{
    public const string Id = "network.get_web_page";
    public const int SchemaVersion = 1;
    public const int MaximumInputUtf8Bytes = 2048;
    public const int MaximumOutputUtf8Bytes = 64 * 1024;
    public const int MaximumTextUtf8Bytes = 60 * 1024;
    public const int MaximumContentBytes = 256 * 1024;
    public const int MaximumRedirects = 5;
    public const int TimeoutSeconds = 20;

    public static WebPageCapabilityDescriptor Descriptor { get; } = new(
        Id,
        SchemaVersion,
        Available: false,
        AvailabilityReason: "parameterized-model-tool-loop-not-qualified",
        MaximumInputUtf8Bytes,
        MaximumOutputUtf8Bytes,
        MaximumContentBytes,
        MaximumRedirects);
}
