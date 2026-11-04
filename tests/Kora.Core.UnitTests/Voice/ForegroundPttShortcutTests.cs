using AwesomeAssertions;
using Kora.Core.Voice;

namespace Kora.Core.UnitTests.Voice;

public sealed class ForegroundPttShortcutTests
{
    [Fact]
    public void SupportedChoicesRoundTripExactlyAndAreUnassignedByDefault()
    {
        ForegroundPttShortcut.Choices[0].Should().Be(ForegroundPttShortcut.Unassigned);
        ForegroundPttShortcut.Choices.Should().HaveCount(3);
        foreach (var choice in ForegroundPttShortcut.Choices)
        { ForegroundPttShortcut.Parse(choice.Label).Should().BeSameAs(choice); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("unassigned")]
    [InlineData("Control Shift Space\n")]
    [InlineData("Space")]
    [InlineData("Control C")]
    [InlineData("Alt F4")]
    [InlineData("Windows L")]
    [InlineData("Control Alt Delete")]
    public void UnsupportedEditingAndReservedChordsAreRejected(string text) =>
        FluentActions.Invoking(() => ForegroundPttShortcut.Parse(text)).Should().Throw<InvalidDataException>();
}
