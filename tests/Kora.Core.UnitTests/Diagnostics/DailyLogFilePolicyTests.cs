using AwesomeAssertions;

using Kora.Core.Diagnostics;

namespace Kora.Core.UnitTests.Diagnostics;

public sealed class DailyLogFilePolicyTests
{
    [Theory]
    [InlineData("kora-20261007.log", true)]
    [InlineData("kora-20260229.log", false)]
    [InlineData("Kora-20261007.log", false)]
    [InlineData("kora-20261007.LOG", false)]
    [InlineData("kora-20261007_001.log", false)]
    [InlineData("..\\kora-20261007.log", false)]
    [InlineData("kora-00000000.log", false)]
    [InlineData("", false)]
    public void Only_exact_calendar_daily_names_are_admitted(string name, bool valid)
    {
        DailyLogFilePolicy.TryParseName(name, out var date).Should().Be(valid);
        if (valid) { date.Should().Be(new DateOnly(2026, 10, 7)); }
    }
}
