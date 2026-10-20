namespace Kora.Core.Context;

public sealed record ClipboardCsvResult(ClipboardCsvOutcome Outcome, CommaSeparatedInspection? Inspection = null);
