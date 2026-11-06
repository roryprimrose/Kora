using System.Globalization;
using AwesomeAssertions;
using Kora.Core.Configuration;

namespace Kora.Core.UnitTests.Configuration;

public sealed class QuietHoursScheduleTests
{
    [Theory]
    [InlineData("2026-11-02T08:59:59Z", false)]
    [InlineData("2026-11-02T09:00:00Z", true)]
    [InlineData("2026-11-02T16:59:59Z", true)]
    [InlineData("2026-11-02T17:00:00Z", false)]
    [InlineData("2026-11-07T12:00:00Z", false)]
    [InlineData("2026-11-08T12:00:00Z", false)]
    public void WeeklyUsesExplicitDaysAndHalfOpenLocalBoundaries(string instant, bool quiet)
    {
        var schedule = QuietHoursSchedule.Weekly("Mon,Tue,Wed,Thu,Fri", "09:00", "17:00", "UTC");
        schedule.IsQuiet(Utc(instant)).Should().Be(quiet);
        QuietHoursSchedule.Parse(schedule.ToString()).Should().Be(schedule);
        QuietHoursSchedule.Parse("Off").Should().Be(QuietHoursSchedule.Off);
        QuietHoursSchedule.Off.IsQuiet(Utc(instant)).Should().BeFalse();
    }

    [Theory]
    [InlineData("2026-11-01T22:00:00Z", true)]
    [InlineData("2026-11-02T00:00:00Z", true)]
    [InlineData("2026-11-02T05:59:59Z", true)]
    [InlineData("2026-11-02T06:00:00Z", false)]
    [InlineData("2026-11-02T22:00:00Z", false)]
    [InlineData("2026-11-01T05:00:00Z", false)]
    [InlineData("2026-11-01T21:59:59Z", false)]
    public void OvernightBelongsToTheSelectedStartingDayAcrossWeekRollover(string instant, bool quiet)
    {
        QuietHoursSchedule.Weekly("Sun", "22:00", "06:00", "UTC").IsQuiet(Utc(instant)).Should().Be(quiet);
    }

    [Theory]
    [InlineData("2026-11-01T05:15:00Z", false)]
    [InlineData("2026-11-01T05:30:00Z", true)]
    [InlineData("2026-11-01T05:59:59Z", true)]
    [InlineData("2026-11-01T06:15:00Z", false)]
    [InlineData("2026-11-01T06:30:00Z", true)]
    [InlineData("2026-11-01T06:59:59Z", true)]
    [InlineData("2026-11-01T07:00:00Z", false)]
    public void DstRepeatedHourUsesTheActualLocalClockOnBothOccurrences(string instant, bool quiet)
    {
        QuietHoursSchedule.Weekly("Sun", "01:30", "02:00", "America/New_York").IsQuiet(Utc(instant)).Should().Be(quiet);
    }

    [Theory]
    [InlineData("2026-03-08T06:59:59Z", false)]
    [InlineData("2026-03-08T07:00:00Z", true)]
    [InlineData("2026-03-08T07:29:59Z", true)]
    [InlineData("2026-03-08T07:30:00Z", false)]
    public void DstSkippedStartDoesNotInventMissingLocalInstants(string instant, bool quiet)
    {
        QuietHoursSchedule.Weekly("Sun", "02:30", "03:30", "America/New_York").IsQuiet(Utc(instant)).Should().Be(quiet);
        QuietHoursSchedule.Weekly("Sun", "02:00", "02:30", "America/New_York").IsQuiet(Utc(instant)).Should().BeFalse();
    }

    [Fact]
    public void SelectedZoneNotMachineZoneDeterminesEligibility()
    {
        var instant = Utc("2026-11-02T09:30:00Z");
        QuietHoursSchedule.Weekly("Mon", "09:00", "10:00", "UTC").IsQuiet(instant).Should().BeTrue();
        QuietHoursSchedule.Weekly("Mon", "09:00", "10:00", "Australia/Sydney").IsQuiet(instant).Should().BeFalse();
        QuietHoursSchedule.Weekly("Mon", "09:00", "10:00", "Eastern Standard Time").ZoneId.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("off")]
    [InlineData("weekly Mon 09:00 09:00 UTC")]
    [InlineData("weekly Mon,Mon 09:00 10:00 UTC")]
    [InlineData("weekly Tue,Mon 09:00 10:00 UTC")]
    [InlineData("weekly Funday 09:00 10:00 UTC")]
    [InlineData("weekly  09:00 10:00 UTC")]
    [InlineData("weekly Mon 9:00 10:00 UTC")]
    [InlineData("weekly Mon 09:00 24:00 UTC")]
    [InlineData("weekly Mon 09:00:00 10:00 UTC")]
    [InlineData("weekly Mon 09:00 10:00 missing-kora-zone")]
    [InlineData("weekly Mon 09:00 10:00 UTC\n")]
    [InlineData("weekly Mon 09:00 10:00 ")]
    [InlineData("weekly Mon 09:00 10:00 UTC ")]
    public void InvalidInputCannotDefaultOrExpandAnInterval(string value)
    {
        FluentActions.Invoking(() => QuietHoursSchedule.Parse(value)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void NullAndOversizeInputsAreRejected()
    {
        FluentActions.Invoking(() => QuietHoursSchedule.Parse(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => QuietHoursSchedule.Weekly(null!, "09:00", "10:00", "UTC")).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => QuietHoursSchedule.Weekly("Mon", "09:00", "10:00", null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => QuietHoursSchedule.Weekly("Mon", "09:00", "10:00", new string('x', 129))).Should().Throw<ArgumentException>();
    }

    private static DateTimeOffset Utc(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
}
