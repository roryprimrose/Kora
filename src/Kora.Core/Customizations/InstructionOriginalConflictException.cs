namespace Kora.Core.Customizations;

/// <summary>A detected original-base conflict before any source publication.</summary>
public sealed class InstructionOriginalConflictException()
    : InvalidOperationException("The registered original changed. Compare the external source, explicitly rebase, discard/reload, or create a Kora copy; the draft is retained.");