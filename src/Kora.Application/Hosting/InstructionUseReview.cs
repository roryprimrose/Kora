using System.Globalization;

namespace Kora.Application.Hosting;

/// <summary>Volatile host binding, never an identity supplied by instruction or model content.</summary>
public sealed class InstructionUseReview
{
    internal InstructionUseReview(ScopedInstructionUseBroker.InstructionContextReview source, DateTimeOffset expiresAt)
    { Source = source; ExpiresAt = expiresAt; }

    internal ScopedInstructionUseBroker.InstructionContextReview Source { get; }
    public Guid Id { get; } = Guid.NewGuid();
    public DateTimeOffset ExpiresAt { get; }
    public bool CanConfirm => Source.CanSubmit;
    public string Text => "ONE local request in the exact Active conversation: "
        + Source.Target.Metadata!.Name.Value + "\nOriginal channel: " + Source.Origin
        + "\nSource: " + Source.Name + " (enabled Kora-owned instruction)"
        + "\nLocal-only; no tools, hosted transfer, history, memory, attachments or future applicability."
        + "\nInstruction is lower-trust guidance, not host policy. Confirmation retains the original USER request "
        + "and a genuine local ASSISTANT answer or an explicit HOST terminal. No automatic retry."
        + "\nReview expires no later than " + ExpiresAt.ToString("O", CultureInfo.InvariantCulture)
        + "\n\nCOMPLETE PINNED INSTRUCTION\n" + Source.InstructionText
        + "\n\nCOMPLETE ORIGINAL REQUEST\n" + Source.Body;
}
