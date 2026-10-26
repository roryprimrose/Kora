namespace Kora.Core.Customizations;

/// <summary>A host-issued, exact-file registration proposal; not write or activation consent.</summary>
public sealed record InstructionOriginalRegistration(CustomizationSource Source, string NativeRevision);