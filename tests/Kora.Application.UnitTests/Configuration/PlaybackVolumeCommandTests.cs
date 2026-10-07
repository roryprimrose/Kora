using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Configuration;

namespace Kora.Application.UnitTests.Configuration;

public sealed class PlaybackVolumeCommandTests
{
    [Theory]
    [InlineData("list volume settings", AppearanceCommandOperation.List, null)]
    [InlineData("get speech.playback-volume", AppearanceCommandOperation.Get, null)]
    [InlineData("status speech.playback-volume", AppearanceCommandOperation.Get, null)]
    [InlineData("RESET speech.playback-volume", AppearanceCommandOperation.Reset, null)]
    [InlineData("Nova, set speech.playback-volume to 0", AppearanceCommandOperation.Set, "0")]
    [InlineData("Nova set speech.playback-volume to 100", AppearanceCommandOperation.Set, "100")]
    public void Exact_current_prefix_and_complete_grammar_have_typed_activated_parity(string text,
        AppearanceCommandOperation operation, string? value)
    {
        var command = PlaybackVolumeCommand.Parse(text, "Nova");
        command!.Operation.Should().Be(operation);
        command.Value.Should().Be(value);
        PlaybackVolumeCommand.FixedPhrases.Should().HaveCount(4);
    }

    [Theory]
    [InlineData("get speech.playback-volume extra")]
    [InlineData("status speech.playback-volume extra")]
    [InlineData("reset speech.playback-volume extra")]
    [InlineData("set speech.playback-volume to ")]
    [InlineData("list volume")]
    [InlineData("set speech.playback-volume\n to 1")]
    public void Partial_or_control_grammar_clarifies_without_execution(string text) =>
        PlaybackVolumeCommand.Parse(text, "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);

    [Fact]
    public void Input_and_result_limits_are_complete_utf8_bounds_and_not_truncation()
    {
        var text = "set speech.playback-volume to ";
        PlaybackVolumeCommand.Parse(text + new string('x', 1024 - text.Length), "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Set);
        PlaybackVolumeCommand.Parse(text + new string('x', 1025 - text.Length), "Kora")!.Error.Should().Contain("1024");
        PlaybackVolumeCommand.Parse(text + new string('界', 400), "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);
        var state = new PlaybackVolumeState(new(1), new(1), "saved", 2, null);
        var result = PlaybackVolumeState.Serialize(state, 3, "observed");
        result.Should().Contain("\"minimum\":0").And.Contain("\"maximum\":100").And.Contain("\"default\":100")
            .And.Contain("\"allowsSpeech\":true").And.Contain("Kora-owned").And.Contain("next eligible").And.Contain("\"callRevision\":3")
            .And.Contain("\"type\":\"integer-percent\"").And.Contain("\"schema\":1");
        var oversized = () => PlaybackVolumeState.Serialize(state with { Recovery = new string('界', 22000) }, 0, "observed");
        oversized.Should().Throw<InvalidDataException>();
        (state with { Effective = new(0) }).AllowsSpeech.Should().BeFalse();
        (state with { Effective = null }).Available.Should().BeFalse();
    }

    [Theory]
    [InlineData("Kora, get speech.playback-volume")]
    [InlineData("ordinary speech")]
    [InlineData("get speech.provider")]
    public void Old_name_and_unrelated_grammars_are_not_volume_aliases(string text) =>
        PlaybackVolumeCommand.Parse(text, "Nova").Should().BeNull();
}
