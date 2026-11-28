namespace Kora.Core.Interaction;

/// <summary>
/// Lifetime policy for blocking host questions and permission proposals.
/// </summary>
/// <remarks>
/// A permission card created with <see cref="UntilAnswered"/> as its proposal or question expiry has no time
/// limit. It retires only when it is answered or cancelled, or when its owner retires it on loss of request,
/// session, generation, privacy, call or host ownership. Every existing admission and revalidation check still
/// applies; only time-based expiry is removed. Owners waiting on such a card should also pause their turn's
/// clocks with <c>ModelTurnLifetime.BeginHostWait()</c>.
/// </remarks>
public static class HostInteractionLifetime
{
    /// <summary>The sentinel expiry of a card that stays open until answered or retired by its owner.</summary>
    public static DateTimeOffset UntilAnswered => DateTimeOffset.MaxValue;

    public static bool IsUntilAnswered(DateTimeOffset expiresAt) => expiresAt == DateTimeOffset.MaxValue;

    /// <summary>
    /// Time left before <paramref name="expiresAt"/> for timers and cancellation sources:
    /// <see cref="Timeout.InfiniteTimeSpan"/> for <see cref="UntilAnswered"/>, otherwise never negative.
    /// </summary>
    public static TimeSpan Remaining(DateTimeOffset expiresAt, DateTimeOffset now)
    {
        if (IsUntilAnswered(expiresAt)) { return Timeout.InfiniteTimeSpan; }
        var remaining = expiresAt - now;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
