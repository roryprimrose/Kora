using Kora.Core;

namespace Kora.Application.Visuals;

public static class PresenceVisibilityPolicy
{
    public static bool CanAutoHide(
        AssistantState state,
        bool isBusy,
        bool isSpeaking,
        bool hasPendingPrompt,
        bool hasResponseActions,
        bool isGrantEditorVisible) =>
        state is AssistantState.Information or AssistantState.Listening or AssistantState.Success
        && !isBusy
        && !isSpeaking
        && !hasPendingPrompt
        && !hasResponseActions
        && !isGrantEditorVisible;
}
