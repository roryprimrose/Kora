using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private string remainingWorkAnswer = string.Empty;
    private SessionWorkspaceEntry? remainingWorkTarget;
    private long remainingWorkControl;
    internal Func<SessionWorkspaceEntry, Func<bool>, CancellationToken, Task<string>>? RemainingWorkAnswer { get; set; }
    internal Func<bool> RemainingWorkAdmission { get; set; } = static () => true;
    public bool CanAnswerRemainingWork => CanReadConversation && RemainingWorkAdmission();
    public string RemainingWorkStatus => !closed && access.CanInspect && remainingWorkControl == access.ControlRevision
        && remainingWorkTarget == selected ? remainingWorkAnswer : string.Empty;

    public Task ShowRemainingWorkAsync(Func<bool> presenter) => RunAsync(async () =>
    {
        var target = RequireSelected();
        var epoch = selectionEpoch;
        var control = access.ControlRevision;
        var input = composerBufferRevision;
        bool Live() => !closed && access.CanInspect && access.ControlRevision == control && selected == target
            && selectionEpoch == epoch && composerBufferRevision == input && presenter() && RemainingWorkAdmission();
        remainingWorkTarget = target;
        remainingWorkControl = control;
        remainingWorkAnswer = "Remaining-work answer unavailable/changed; no completion is inferred.";
        OnPropertyChanged(nameof(RemainingWorkStatus));
        if (!Live()) { throw new InvalidOperationException("The original native remaining-work presenter or decision admission changed."); }
        string body;
        if (RemainingWorkAnswer is { } answer)
        { body = await answer(target, Live, lifetime.Token); }
        else
        {
            var ordinary = conversation ?? throw new InvalidOperationException("The ordinary conversation owner is unavailable.");
            var observation = await ordinary.ReadRemainingWorkAsync(target, Live, lifetime.Token);
            await ordinary.ValidateRemainingWorkAsync(observation, Live, lifetime.Token);
            if (!ordinary.IsRemainingWorkCurrent(observation, Live))
            { throw new InvalidOperationException("Remaining-work sources changed before native publication."); }
            body = observation.Format();
        }
        if (!Live()) { throw new OperationCanceledException("The original remaining-work presenter was retired."); }
        remainingWorkAnswer = body;
        OnPropertyChanged(nameof(RemainingWorkStatus));
    }, passive: true);

    private void ClearRemainingWork()
    {
        remainingWorkAnswer = string.Empty;
        remainingWorkTarget = null;
        OnPropertyChanged(nameof(RemainingWorkStatus));
    }
}