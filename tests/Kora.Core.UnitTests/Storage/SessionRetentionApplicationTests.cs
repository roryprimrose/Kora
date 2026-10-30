using AwesomeAssertions;

using Kora.Core.Configuration;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionRetentionApplicationTests
{
    [Theory]
    [InlineData(true, 0, SessionRetentionApplyEffect.DeadlinesOnly)]
    [InlineData(true, 1, SessionRetentionApplyEffect.Archive)]
    [InlineData(false, 1, SessionRetentionApplyEffect.DeadlinesOnly)]
    [InlineData(true, 3, SessionRetentionApplyEffect.Delete)]
    [InlineData(false, 3, SessionRetentionApplyEffect.Delete)]
    public void OriginalActivityAndInclusiveCutoffDetermineOnlyTheReviewedEffect(bool active, int days,
        SessionRetentionApplyEffect effect)
    {
        var clock = Clock();
        var settings = new SessionRetentionSettings(1, 3);
        var changed = SessionRetentionApplication.Recalculate(clock, settings, clock.LastMeaningfulActivity.AddDays(days));
        changed.LastMeaningfulActivity.Should().Be(clock.LastMeaningfulActivity);
        changed.ArchiveDue.Should().Be(clock.LastMeaningfulActivity.AddDays(1));
        changed.DeleteDue.Should().Be(clock.LastMeaningfulActivity.AddDays(3));
        var review = new SessionRetentionApplyReview(Guid.NewGuid(), new(clock, new(1), 0, false), changed,
            settings, 1, new(Guid.NewGuid(), new(new(clock.SessionId, new(1), active), null), "head", 0, 0, 0, 0),
            clock.LastMeaningfulActivity.AddDays(days), "head", "source");
        review.Effect.Should().Be(effect);
        review.ExpiresAt.Should().Be(review.Cutoff.AddSeconds(30));
        new SessionRetentionApplyReceipt(review, review.Before, effect).Review.Should().BeSameAs(review);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("settings")]
    [InlineData("id")]
    [InlineData("offset")]
    [InlineData("cutoff-offset")]
    [InlineData("future")]
    [InlineData("overflow")]
    public void InvalidOrFutureClockNeverProducesDeadlines(string fault)
    {
        var clock = Clock();
        var cutoff = clock.LastMeaningfulActivity;
        if (string.Equals(fault, "id", StringComparison.Ordinal)) { clock = clock with { SessionId = default }; }
        if (string.Equals(fault, "offset", StringComparison.Ordinal)) { clock = clock with { LastMeaningfulActivity = cutoff.ToOffset(TimeSpan.FromHours(1)) }; }
        if (string.Equals(fault, "cutoff-offset", StringComparison.Ordinal)) { cutoff = cutoff.ToOffset(TimeSpan.FromHours(-1)); }
        if (string.Equals(fault, "future", StringComparison.Ordinal)) { cutoff = cutoff.AddTicks(-1); }
        if (string.Equals(fault, "overflow", StringComparison.Ordinal)) { clock = clock with { LastMeaningfulActivity = DateTimeOffset.MaxValue }; cutoff = DateTimeOffset.MaxValue; }
        var action = () => SessionRetentionApplication.Recalculate(string.Equals(fault, "null", StringComparison.Ordinal) ? null! : clock,
string.Equals(fault, "settings", StringComparison.Ordinal) ? default : SessionRetentionSettings.Default, cutoff);
        action.Should().Throw<Exception>();
    }

    private static SessionRetentionState Clock()
    {
        var last = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        return new(new HostId<SessionIdentity>(Guid.NewGuid()), last, last.AddDays(10), last.AddDays(30), false, false);
    }
}
