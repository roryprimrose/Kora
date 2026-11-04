using Kora.Core.Configuration;

namespace Kora.Application.Dependencies;

internal sealed class OrdinaryTurnActiveBudget(OrdinaryTurnDeadline preference, TimeSpan maximum,
    TimeProvider time)
{
    private readonly long dispatchedAt = time.GetTimestamp();
    internal OrdinaryTurnDeadline Preference { get; } = preference;
    internal TimeSpan Limit { get; } = preference.Duration < maximum ? preference.Duration : maximum;
    internal TimeSpan Remaining
    {
        get
        {
            var elapsed = time.GetElapsedTime(dispatchedAt);
            return elapsed < TimeSpan.Zero ? TimeSpan.Zero : Limit - elapsed;
        }
    }
    internal bool Expired => Remaining <= TimeSpan.Zero;
}
