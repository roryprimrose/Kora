using AwesomeAssertions;
using Kora.Core.Communication;
using Kora.Core.Voice;

namespace Kora.Core.UnitTests.Communication;

public sealed class InCallFeedbackRulesTests
{
    [Fact]
    public void Active_and_suspected_override_every_ordinary_scope_but_inherit_restores_precedence()
    {
        InCallFeedbackRules.Default.Should().Be(InCallFeedbackMode.UI);
        foreach (var state in Enum.GetValues<CallState>())
        foreach (var mode in Enum.GetValues<InCallFeedbackMode>())
        foreach (var device in Enum.GetValues<ResponseOutputMode>())
        foreach (var session in new ResponseOutputMode?[] { null, ResponseOutputMode.Hybrid, ResponseOutputMode.VoiceOnly, ResponseOutputMode.VisualOnly })
        foreach (var queue in new ResponseOutputMode?[] { null, ResponseOutputMode.Hybrid, ResponseOutputMode.VoiceOnly, ResponseOutputMode.VisualOnly })
        foreach (var task in new ResponseOutputMode?[] { null, ResponseOutputMode.Hybrid, ResponseOutputMode.VoiceOnly, ResponseOutputMode.VisualOnly })
        {
            var ordinary = ResponseOutputModeResolver.Resolve(device, queue, task, session);
            ordinary.Should().Be(task ?? queue ?? session ?? device);
            var protectedCall = state is CallState.Active or CallState.Suspected;
            var expected = protectedCall ? mode switch
            {
                InCallFeedbackMode.Voice => ResponseOutputMode.VoiceOnly,
                InCallFeedbackMode.UI => ResponseOutputMode.VisualOnly,
                InCallFeedbackMode.Both => ResponseOutputMode.Hybrid,
                _ => ordinary,
            } : ordinary;
            InCallFeedbackRules.Resolve(ordinary, mode, state).Should().Be(expected);
            InCallFeedbackRules.Parse(mode.ToString()).Should().Be(mode);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("ui")]
    [InlineData(" UI")]
    [InlineData("UI ")]
    [InlineData("0")]
    [InlineData("VisualOnly")]
    public void Unknown_values_never_become_defaults(string value) =>
        FluentActions.Invoking(() => InCallFeedbackRules.Parse(value)).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void Invalid_modes_are_rejected_even_when_masked()
    {
        FluentActions.Invoking(() => InCallFeedbackRules.Resolve((ResponseOutputMode)99, InCallFeedbackMode.UI, CallState.Clear))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => InCallFeedbackRules.Resolve(ResponseOutputMode.Hybrid, (InCallFeedbackMode)99, CallState.Clear))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => ResponseOutputModeResolver.Resolve(ResponseOutputMode.Hybrid, null, ResponseOutputMode.VisualOnly, (ResponseOutputMode)99))
            .Should().Throw<ArgumentOutOfRangeException>();
        InCallFeedbackRules.Resolve(ResponseOutputMode.VoiceOnly, InCallFeedbackMode.UI, (CallState)99).Should().Be(ResponseOutputMode.VoiceOnly);
    }
}
