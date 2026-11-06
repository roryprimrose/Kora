using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Configuration;

public sealed class ForegroundPttCommandTests
{
    [Theory]
    [InlineData("get push-to-talk shortcut", AppearanceCommandOperation.Get, null)]
    [InlineData("Kora, set push-to-talk to Control Shift Space", AppearanceCommandOperation.Set, "Control Shift Space")]
    [InlineData("set push-to-talk to control shift f9", AppearanceCommandOperation.Set, "Control Shift F9")]
    [InlineData("set push-to-talk to Unassigned", AppearanceCommandOperation.Set, "Unassigned")]
    [InlineData("reset push-to-talk shortcut", AppearanceCommandOperation.Reset, "Unassigned")]
    [InlineData("set push-to-talk to Control C", AppearanceCommandOperation.Clarify, null)]
    [InlineData("set push-to-talk to Alt F4", AppearanceCommandOperation.Clarify, null)]
    [InlineData("set push-to-talk to Control Shift Space\n", AppearanceCommandOperation.Clarify, null)]
    public void ExactSettingsOnlyRecognizeSupportedForegroundChords(string text, AppearanceCommandOperation operation, string? label)
    {
        var command = ForegroundPttCommand.Parse(text, "Kora")!;
        command.Operation.Should().Be(operation);
        command.Shortcut?.Label.Should().Be(label);
        if (operation == AppearanceCommandOperation.Clarify) { command.Error.Should().Be(ForegroundPttCommand.Syntax); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("help")]
    [InlineData("open microphone settings")]
    public void UnrelatedInputIsNotConsumed(string text) => ForegroundPttCommand.Parse(text, "Kora").Should().BeNull();

    [Fact]
    public void OversizeCommandsAreClarifiedAndFixedPhrasesRoundTrip()
    {
        ForegroundPttCommand.Parse("push-to-talk " + new string('a', 256), "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);
        foreach (var phrase in ForegroundPttCommand.FixedPhrases)
        { ForegroundPttCommand.Parse(phrase, "Kora")!.Operation.Should().NotBe(AppearanceCommandOperation.Clarify); }
        ForegroundPttShortcut.ControlShiftF9.Label.Should().Be("Control Shift F9");
    }
}
