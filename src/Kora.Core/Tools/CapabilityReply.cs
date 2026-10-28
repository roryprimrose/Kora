namespace Kora.Core.Tools;

public sealed record CapabilityReply(
    CapabilityOutcome Outcome,
    string Reason,
    CapabilityDescriptorPage? Capabilities = null,
    CapabilityDescriptor? Descriptor = null,
    ApplicationVersionObservation? Version = null,
    ReadinessPage? Readiness = null,
    RuntimePage? Runtimes = null,
    RuntimeObservation? Runtime = null,
    Kora.Core.Network.LocalWebPageResult? WebPage = null,
    Kora.Core.Mcp.McpModelResult? Mcp = null,
    SessionDescriptorPage? Sessions = null,
    CustomizationMetadataPage? Customizations = null);
