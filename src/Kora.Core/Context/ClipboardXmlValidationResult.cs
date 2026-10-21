namespace Kora.Core.Context;

public sealed record ClipboardXmlValidationResult(
    ClipboardSnapshot Source, ClipboardXmlOutcome Outcome, ClipboardXmlReason Reason, int Line, int Column);
