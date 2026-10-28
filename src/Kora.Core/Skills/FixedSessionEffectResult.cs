namespace Kora.Core.Skills;

public sealed record FixedSessionEffectResult(
    FixedSessionEffectOutcome Outcome, int NativeError = 0, bool AuditConfirmed = true);