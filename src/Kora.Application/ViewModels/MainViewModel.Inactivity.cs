using Kora.Application.Visuals;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public bool CanAutoHidePresence => PresenceVisibilityPolicy.CanAutoHide(
        State,
        IsBusy || IsLocalTaskCancellable || IsLocalModelSetupActive || IsPowerShellSetupActive,
        IsSpeaking,
        IsResponseInteractionPending,
        HasResponseActions,
        IsGrantEditorVisible);
}
