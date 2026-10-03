using AwesomeAssertions;

using Kora.Windows.Session;

namespace Kora.Windows.IntegrationTests.Session;

public sealed class WindowsSessionControllerTests
{
    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, true, false)]
    public void IsSessionUnlocked_combines_WTS_and_input_desktop_state(
        bool isActive,
        bool sessionFlagUnlocked,
        bool inputDesktopAvailable,
        bool expected)
    {
        var result = WindowsSessionController.IsSessionUnlocked(
            isActive,
            sessionFlagUnlocked,
            inputDesktopAvailable);

        result.Should().Be(expected);
    }
}
