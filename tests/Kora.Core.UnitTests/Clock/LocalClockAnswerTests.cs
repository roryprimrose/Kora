using System.Globalization;
using AwesomeAssertions;
using Kora.Core.Clock;

namespace Kora.Core.UnitTests.Clock;

public sealed class LocalClockAnswerTests
{
    [Theory]
    [InlineData(LocalClockOperation.Time, "UTC time: 10:00:00")]
    [InlineData(LocalClockOperation.Date, "UTC date: 2025-12-31")]
    [InlineData(LocalClockOperation.DateAndTime, "UTC date and time: 2025-12-31, 10:00:00")]
    public void ExplicitUtcNeverUsesTheHostLocalCalendarDateOrOffset(LocalClockOperation operation, string expected)
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.LongDatePattern = "yyyy-MM-dd";
        culture.DateTimeFormat.LongTimePattern = "HH:mm:ss";
        var local = TimeZoneInfo.CreateCustomTimeZone("fixture/+14", TimeSpan.FromHours(14), "Fixture", "Fixture");
        var instant = new DateTimeOffset(2025, 12, 31, 10, 0, 0, TimeSpan.Zero);
        var answer = new LocalClockAnswer(instant, new(local, culture), operation, LocalClockZone.Utc);
        answer.Instant.Should().Be(instant);
        answer.LocalTime.Should().Be(instant);
        answer.LocalTime.Offset.Should().Be(TimeSpan.Zero);
        answer.TimeZoneId.Should().Be("UTC");
        answer.Text.Should().Be(expected + " (UTC+00:00; UTC).");
    }

    [Fact]
    public void ExplicitUtcKeepsHostCultureAndUnknownZoneNeverDefaults()
    {
        var context = new LocalClockContext(TimeZoneInfo.Utc, CultureInfo.GetCultureInfo("fr-FR"));
        var instant = new DateTimeOffset(2026, 1, 1, 13, 14, 15, TimeSpan.Zero);
        new LocalClockAnswer(instant, context, LocalClockOperation.DateAndTime, LocalClockZone.Utc).Text
            .Should().Be("UTC date and time: jeudi 1 janvier 2026, 13:14:15 (UTC+00:00; UTC).");
        var invalid = () => new LocalClockAnswer(instant, context, LocalClockOperation.Time, (LocalClockZone)999);
        invalid.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("zone");
    }

    [Theory]
    [InlineData("2025-12-31T23:59:59Z", 0, "2025-12-31", "23:59:59", "+00:00")]
    [InlineData("2026-01-01T00:00:00Z", 0, "2026-01-01", "00:00:00", "+00:00")]
    [InlineData("2025-12-31T10:00:00Z", 840, "2026-01-01", "00:00:00", "+14:00")]
    [InlineData("2026-01-01T07:59:59Z", -480, "2025-12-31", "23:59:59", "-08:00")]
    [InlineData("2024-02-28T18:15:00Z", 345, "2024-02-29", "00:00:00", "+05:45")]
    [InlineData("2024-03-01T03:29:59Z", -210, "2024-02-29", "23:59:59", "-03:30")]
    public void AnswersUseTheConvertedInstantForEveryField(string utc, int minutes, string date, string time, string offset)
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.LongDatePattern = "yyyy-MM-dd";
        culture.DateTimeFormat.LongTimePattern = "HH:mm:ss";
        var zone = TimeZoneInfo.CreateCustomTimeZone("fixture/local", TimeSpan.FromMinutes(minutes), "Fixture", "Fixture");
        var context = new LocalClockContext(zone, culture);
        culture.DateTimeFormat.LongDatePattern = "'changed'";
        context.Culture.IsReadOnly.Should().BeTrue();
        context.TimeZone.Should().BeSameAs(zone);
        var instant = DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture);
        foreach (var operation in new[] { LocalClockOperation.Time, LocalClockOperation.Date, LocalClockOperation.DateAndTime })
        {
            var answer = new LocalClockAnswer(instant, context, operation);
            answer.Instant.Should().Be(instant);
            answer.LocalTime.Offset.Should().Be(TimeSpan.FromMinutes(minutes));
            answer.TimeZoneId.Should().Be("fixture/local");
            answer.LocalTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture).Should().Be(date);
            answer.LocalTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture).Should().Be(time);
            var text = operation switch
            {
                LocalClockOperation.Time => "Local time: " + time,
                LocalClockOperation.Date => "Local date: " + date,
                _ => "Local date and time: " + date + ", " + time,
            };
            answer.Text.Should().Be(text + " (UTC" + offset + "; fixture/local).");
        }
    }

    [Theory]
    [InlineData("2026-03-08T06:59:59Z", "01:59:59", -300)]
    [InlineData("2026-03-08T07:00:00Z", "03:00:00", -240)]
    [InlineData("2026-11-01T05:59:59Z", "01:59:59", -240)]
    [InlineData("2026-11-01T06:00:00Z", "01:00:00", -300)]
    public void DstGapsAndRepeatedTimesRetainTheExactOffset(string utc, string time, int offsetMinutes)
    {
        var start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday);
        var end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new(2020, 1, 1), new(2030, 12, 31),
            TimeSpan.FromHours(1), start, end);
        var zone = TimeZoneInfo.CreateCustomTimeZone("fixture/dst", TimeSpan.FromHours(-5), "Fixture", "Standard", "Summer", [rule]);
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.LongTimePattern = "HH:mm:ss";
        var answer = new LocalClockAnswer(DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture),
            new(zone, culture), LocalClockOperation.Time);
        answer.LocalTime.Offset.Should().Be(TimeSpan.FromMinutes(offsetMinutes));
        answer.Text.Should().StartWith("Local time: " + time).And.EndWith("; fixture/dst).");
        var utcAnswer = new LocalClockAnswer(answer.Instant, new(zone, culture), LocalClockOperation.Time, LocalClockZone.Utc);
        utcAnswer.LocalTime.Offset.Should().Be(TimeSpan.Zero);
        utcAnswer.Text.Should().Be("UTC time: " + answer.Instant.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " (UTC+00:00; UTC).");
    }

    [Fact]
    public void ExplicitCultureControlsDateAndTimeAndInvalidOperationsAreRejected()
    {
        var context = new LocalClockContext(TimeZoneInfo.Utc, CultureInfo.GetCultureInfo("fr-FR"));
        var answer = new LocalClockAnswer(new(2026, 1, 1, 13, 14, 15, TimeSpan.Zero), context, LocalClockOperation.DateAndTime);
        answer.Text.Should().Be("Local date and time: jeudi 1 janvier 2026, 13:14:15 (UTC+00:00; UTC).");
        var invalid = () => new LocalClockAnswer(answer.Instant, context, LocalClockOperation.Help);
        invalid.Should().Throw<ArgumentOutOfRangeException>();
    }
}
