using AwesomeAssertions;

using Kora.Application.ViewModels;
using Kora.Core;

namespace Kora.Application.UnitTests.ViewModels;

public sealed class AssistantStateLabelsTests
{
    [Theory]
    [InlineData(AssistantState.Hidden, "HIDDEN")]
    [InlineData(AssistantState.Listening, "LISTENING")]
    [InlineData(AssistantState.Calculating, "THINKING")]
    [InlineData(AssistantState.Waiting, "NEEDS YOUR APPROVAL")]
    [InlineData(AssistantState.Executing, "WORKING")]
    [InlineData(AssistantState.Success, "COMPLETE")]
    [InlineData(AssistantState.Failure, "COULDN'T FINISH")]
    [InlineData(AssistantState.Information, "INFORMATION")]
    public void GetLabel_maps_every_assistant_state(AssistantState state, string expected)
    {
        state.GetLabel().Should().Be(expected);
    }

    [Fact]
    public void GetLabel_rejects_unknown_state()
    {
        var action = () => ((AssistantState)int.MaxValue).GetLabel();

        action.Should().Throw<ArgumentOutOfRangeException>();
    }
}