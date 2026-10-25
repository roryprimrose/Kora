using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private Func<SessionCommand, RequestOrigin, Func<bool>, CancellationToken, Task<string>>? sessionInstructionScopes;
    private Action? retireSessionInstructionScopes;

    public void BindSessionInstructionScopes(
        Func<SessionCommand, RequestOrigin, Func<bool>, CancellationToken, Task<string>>? execute, Action? retire)
    { retireSessionInstructionScopes?.Invoke(); sessionInstructionScopes = execute; retireSessionInstructionScopes = retire; }

    public Func<bool> CaptureSessionInstructionScopeEligibility()
    {
        var input = CaptureManualCallInput(RequestOrigin.LocalUi, CallPolicyRevision);
        var nameRevision = assistantNameConfiguration.Get().Revision;
        var captured = CaptureSkillReviewEligibility(input);
        return () => captured() && !disposed && IsHostInputEligible && !IsProtectedCall
            && nameRevision == assistantNameConfiguration.Get().Revision;
    }

    private async Task ExecuteSessionInstructionScopeAsync(SessionCommand command, RequestOrigin origin,
        Func<bool> eligible, bool activationPrefixed)
    {
        if (origin == RequestOrigin.ActivatedVoice && !activationPrefixed || !eligible())
        { throw new InvalidOperationException("Use fresh original typed input or the current assistant-name voice prefix."); }
        var text = await (sessionInstructionScopes
            ?? throw new InvalidOperationException("The native session instruction scope workflow is unavailable."))
            (command, origin, eligible, CancellationToken.None);
        if (eligible())
        {
            ShowInformation("Session instruction scope.", command.Operation == SessionCommandOperation.InstructionScopeReview
                ? "Complete pinned source and exact Active target are displayed in the private native scope window. "
                    + "Inspect them, then confirm separately within two minutes. Nothing has been applied or submitted."
                : text);
        }
    }
}
