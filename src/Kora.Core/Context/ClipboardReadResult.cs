namespace Kora.Core.Context;

public sealed record ClipboardReadResult(
    ClipboardOutcome Outcome, string? Text = null, uint Version = 0, bool ResourcesReleased = true);
