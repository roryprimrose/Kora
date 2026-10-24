using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Commands;
using Kora.Core.Configuration;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Configuration;

public sealed class WindowsSpeechRateCommandTests
{
    [Theory]
    [InlineData("list rate settings", AppearanceCommandOperation.List)]
    [InlineData("Nova, get speech.windows-rate", AppearanceCommandOperation.Get)]
    [InlineData("Nova status speech.windows-rate", AppearanceCommandOperation.Get)]
    [InlineData("reset speech.windows-rate", AppearanceCommandOperation.Reset)]
    [InlineData("set speech.windows-rate to -10", AppearanceCommandOperation.Set)]
    [InlineData("set speech.windows-rate to 10", AppearanceCommandOperation.Set)]
    [InlineData("list rate", AppearanceCommandOperation.Clarify)]
    [InlineData("get speech.windows-rate.extra", AppearanceCommandOperation.Clarify)]
    [InlineData("status speech.windows-rate extra", AppearanceCommandOperation.Clarify)]
    [InlineData("reset speech.windows-rate extra", AppearanceCommandOperation.Clarify)]
    [InlineData("set speech.windows-rate", AppearanceCommandOperation.Clarify)]
    [InlineData("speak faster please", AppearanceCommandOperation.Clarify)]
    [InlineData("speak slower by 2", AppearanceCommandOperation.Clarify)]
    [InlineData("speak faster\n", AppearanceCommandOperation.Clarify)]
    [InlineData("speak slower and reset speech.windows-rate", AppearanceCommandOperation.Clarify)]
    public void Exact_current_name_grammar_is_local(string input, AppearanceCommandOperation operation)
    {
        WindowsSpeechRateCommand.Parse(input, "Nova")!.Operation.Should().Be(operation);
        WindowsSpeechRateCommand.FixedPhrases.Should().HaveCount(6);
    }

    [Theory]
    [InlineData("Kora, list rate settings")]
    [InlineData("NovaX list rate settings")]
    [InlineData("Nova")]
    [InlineData("make speech faster")]
    [InlineData("Kora, speak faster")]
    [InlineData("NovaX speak slower")]
    [InlineData("please speak faster")]
    [InlineData("speak louder")]
    [InlineData("speak quieter")]
    public void Unrelated_or_old_prefix_is_not_rate_authority(string input) =>
        WindowsSpeechRateCommand.Parse(input, "Nova").Should().BeNull();

    [Theory]
    [InlineData("speak faster", AudioPreferenceAdjustment.Increase)]
    [InlineData("Nova, speak slower", AudioPreferenceAdjustment.Decrease)]
    [InlineData("nova SPEAK FASTER", AudioPreferenceAdjustment.Increase)]
    public void RelativeAliasesAreExactNativeSteps(string input, AudioPreferenceAdjustment expected)
    {
        var command = WindowsSpeechRateCommand.Parse(input, "Nova")!;
        command.Operation.Should().Be(AppearanceCommandOperation.Set);
        command.Adjustment.Should().Be(expected);
        command.Value.Should().BeNull();
        PlaybackVolumeCommand.Parse(input, "Nova").Should().BeNull();
        SpeechTextCommand.Parse(input, "Nova").Should().BeNull();
        new BuiltInCommandRouter(new BuiltInCommandCatalog()).Match(input, "Nova").IsMatch.Should().BeFalse();
    }

    [Theory]
    [InlineData("speak faster", AudioPreferenceAdjustment.Increase)]
    [InlineData("speak slower", AudioPreferenceAdjustment.Decrease)]
    [InlineData("Speak speak faster", AudioPreferenceAdjustment.Increase)]
    [InlineData("Speak, speak slower", AudioPreferenceAdjustment.Decrease)]
    public void RelativeAliasIsNotConsumedByOverlappingAssistantName(string input, AudioPreferenceAdjustment expected) =>
        WindowsSpeechRateCommand.Parse(input, "Speak")!.Adjustment.Should().Be(expected);

    [Fact]
    public void Bounded_input_controls_and_complete_result_size_fail_explicitly()
    {
        WindowsSpeechRateCommand.Parse("get speech.windows-rate\n", "Nova")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);
        WindowsSpeechRateCommand.Parse("set speech.windows-rate to " + new string('1', SessionCommand.MaximumInputBytes), "Nova")!
            .Operation.Should().Be(AppearanceCommandOperation.Clarify);
        var state = new WindowsSpeechRateState(new(1), new(1), SpeechProviderIds.Windows, SpeechRateSupport.WindowsNative, 1, "saved", 1, null);
        var result = WindowsSpeechRateState.Serialize(state, 2, "saved");
        state.AllowsProviderOutput(SpeechProviderIds.Windows).Should().BeTrue();
        state.AllowsProviderOutput(SpeechProviderIds.Kokoro).Should().BeTrue();
        state.AllowsProviderOutput("unknown").Should().BeFalse();
        (state with { Effective = null }).AllowsProviderOutput(SpeechProviderIds.Windows).Should().BeFalse();
        result.Should().Contain("\"minimum\":-10").And.Contain("\"maximum\":10").And.Contain("\"default\":0")
            .And.Contain("Kokoro unchanged").And.Contain("\"available\":true");
        FluentActions.Invoking(() => WindowsSpeechRateState.Serialize(state with { Recovery = new string('x', SessionCommand.MaximumResultBytes) }, 2, "saved"))
            .Should().Throw<InvalidDataException>();
    }
}
