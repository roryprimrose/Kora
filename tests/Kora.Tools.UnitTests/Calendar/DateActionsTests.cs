using AwesomeAssertions;
using Kora.Tools.Calendar;

namespace Kora.Tools.UnitTests.Calendar;

public sealed class DateActionsTests
{
    [Fact]
    public void Actions_use_portable_calendar_rules_without_dependencies()
    {
        var leap = new DateOnly(2000, 2, 29);
        new DateDifference().Execute(leap, leap.AddDays(-1)).Should().Be(-1);
        new DateAdd().Execute(leap, 1).Should().Be(new DateOnly(2000, 3, 1));
        var overflow = () => new DateAdd().Execute(DateOnly.MaxValue, 1);
        overflow.Should().Throw<InvalidDataException>();
        new DateHelp().Execute().Should().Contain("signed end minus start").And.Contain("start excluded")
            .And.Contain("no clock").And.Contain("not an inclusive count");
    }
}
