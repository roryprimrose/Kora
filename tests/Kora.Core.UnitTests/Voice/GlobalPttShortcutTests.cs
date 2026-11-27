using AwesomeAssertions;
using Kora.Core.Voice;

namespace Kora.Core.UnitTests.Voice;

public sealed class GlobalPttShortcutTests
{
    [Fact]
    public void SavedChoicesRoundTripWithoutAdmittingOtherChords()
    {
        foreach (var choice in GlobalPttShortcut.Choices)
        { GlobalPttShortcut.Parse(choice.Label).Should().BeSameAs(choice); }
        foreach (var text in new[] { "", "control shift f9", "Control C", "Control Shift Space\n" })
        { FluentActions.Invoking(() => GlobalPttShortcut.Parse(text)).Should().Throw<InvalidDataException>(); }
    }
}
