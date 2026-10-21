using System.Globalization;
using AwesomeAssertions;
using Kora.Core.Clock;
using Kora.Tools.Clock;

namespace Kora.Tools.UnitTests.Clock;

public sealed class LocalClockActionsTests
{
    [Fact]
    public void EveryActionUsesTheSharedClockAndSamplesExactlyOneInstantAndContext()
    {
        var time = new AdvancingClock();
        var context = new Context();
        var service = new LocalClockService(time, context);
        var answers = new[]
        {
            new LocalClockGetTime(service).Execute(),
            new LocalClockGetDate(service).Execute(),
            new LocalClockGetDateAndTime(service).Execute(),
        };
        time.Reads.Should().Be(3);
        context.Reads.Should().Be(3);
        answers[0].Text.Should().StartWith("Local time:");
        answers[1].Text.Should().StartWith("Local date:");
        answers[2].Text.Should().StartWith("Local date and time:");
        answers.Select(answer => answer.Instant).Should().OnlyHaveUniqueItems();
        answers.Should().OnlyContain(answer => answer.LocalTime.Offset == TimeSpan.FromHours(14)
            && answer.LocalTime.Year == 2026 && answer.TimeZoneId == "fixture/+14");
    }

    private sealed class AdvancingClock : TimeProvider
    {
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2025, 12, 31, 10, 0, 0, TimeSpan.Zero).AddSeconds(Reads++);
    }

    private sealed class Context : ILocalClockContextProvider
    {
        public int Reads { get; private set; }
        public LocalClockContext GetCurrent()
        {
            Reads++;
            return new(TimeZoneInfo.CreateCustomTimeZone("fixture/+14", TimeSpan.FromHours(14), "Fixture", "Fixture"),
                CultureInfo.InvariantCulture);
        }
    }
}
