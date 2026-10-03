using Kora.Core;

namespace Kora.Application.ViewModels;

public static class AssistantStateLabels
{
    public static string GetLabel(this AssistantState state) => state switch
    {
        AssistantState.Hidden => "HIDDEN",
        AssistantState.Listening => "LISTENING",
        AssistantState.Calculating => "THINKING",
        AssistantState.Waiting => "NEEDS YOUR APPROVAL",
        AssistantState.Executing => "WORKING",
        AssistantState.Success => "COMPLETE",
        AssistantState.Failure => "COULDN'T FINISH",
        AssistantState.Information => "INFORMATION",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown assistant state."),
    };
}