using AwesomeAssertions;
using Kora.Core.Diagnostics;

namespace Kora.Core.UnitTests.Diagnostics;

public sealed class PresenceSwitchTimingTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Unknown_phases_are_rejected_without_emitting_measurements(int value)
    {
        var mark = () => PresenceSwitchTiming.Mark((PresenceSwitchPhase)value);
        mark.Should().Throw<ArgumentOutOfRangeException>();
    }
}
