namespace Kora.Application.Configuration;

public sealed record AppearanceApplyResult(AppearanceApplyOutcome Outcome, AppearanceOptionState State, string? Error = null)
{
    public bool Succeeded => Outcome is AppearanceApplyOutcome.Applied or AppearanceApplyOutcome.Unchanged;
}
