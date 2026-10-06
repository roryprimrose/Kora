using AwesomeAssertions;

using Kora.Application.Visuals;
using Kora.Core;

namespace Kora.Application.UnitTests.Visuals;

public sealed class PresenceVisibilityPolicyTests
{
    [Theory]
    [InlineData(AssistantState.Information, true)]
    [InlineData(AssistantState.Listening, true)]
    [InlineData(AssistantState.Success, true)]
    [InlineData(AssistantState.Hidden, false)]
    [InlineData(AssistantState.Calculating, false)]
    [InlineData(AssistantState.Executing, false)]
    [InlineData(AssistantState.Waiting, false)]
    [InlineData(AssistantState.Failure, false)]
    [InlineData((AssistantState)999, false)]
    public void Only_idle_listening_and_completed_presence_can_auto_hide(AssistantState state, bool expected)
    {
        PresenceVisibilityPolicy.CanAutoHide(state, false, false, false, false, false)
            .Should().Be(expected);
    }

    [Fact]
    public void Work_speech_and_every_prompt_boundary_prevent_auto_hiding()
    {
        foreach (var state in new[] { AssistantState.Information, AssistantState.Listening, AssistantState.Success })
        foreach (var busy in new[] { false, true })
        foreach (var speaking in new[] { false, true })
        foreach (var prompt in new[] { false, true })
        foreach (var actions in new[] { false, true })
        foreach (var grants in new[] { false, true })
        {
            PresenceVisibilityPolicy.CanAutoHide(state, busy, speaking, prompt, actions, grants)
                .Should().Be(!busy && !speaking && !prompt && !actions && !grants);
        }
    }
}
