using Kora.Core.Dependencies;

namespace Kora.Core.Context;

public sealed record ClipboardExplanationResult(ClipboardExplanationOutcome Outcome,
    ClipboardOutcome? CaptureOutcome = null, ModelTurnReason? ModelReason = null);