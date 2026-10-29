using Kora.Application.ViewModels;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private MainViewModel? completionVoiceHost;
    internal void BindCompletionNotices(MainViewModel main)
    {
        completionVoiceHost = main;
    }
    public string CompletionNoticeDisclosure => completionVoiceHost?.CompletionNoticeDisclosure
        ?? "Task completion voice notices unavailable; visual notices remain.";
    public string CompletionNoticeStatus => completionVoiceHost?.CompletionNoticeStatus ?? "Task completion voice notices unavailable.";
    public bool CanEnableCompletionNotices => routineSurfaceOpen && CanChangeRoutineQuiet
        && completionVoiceHost?.CanEnableCompletionNotices == true;
    public bool CanDisableCompletionNotices => routineSurfaceOpen && !closed && completionVoiceHost is not null;
    public Task EnableCompletionNoticesAsync() => ChangeCompletionNoticesAsync(true);
    public Task DisableCompletionNoticesAsync() => ChangeCompletionNoticesAsync(false);

    private async Task ChangeCompletionNoticesAsync(bool enabled)
    {
        if (!enabled) { completionVoiceHost?.RetireCompletionNotices(); }
        var epoch = selectionEpoch;
        var surface = Volatile.Read(ref routineSurfaceRevision);
        var original = HostActivity.Current;
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), selected?.Authority.SessionId ?? viewer.SessionId,
            new(Guid.NewGuid()), RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Policy);
        try
        {
            if (original is not null && (original.Request.Origin != RequestOrigin.LocalUi || original.Activity!.IsStopped))
            { throw new InvalidOperationException("Completion notice consent cannot relabel non-native input."); }
            var target = RequireSelected();
            await (completionVoiceHost ?? throw new InvalidOperationException("The completion speech owner is unavailable."))
                .ChangeCompletionNoticesNativeAsync(target.Authority.SessionId, target.Authority.Generation, enabled,
                    () => !closed && routineSurfaceOpen && surface == Volatile.Read(ref routineSurfaceRevision)
                        && access.CanControl && selectionEpoch == epoch && selected?.Authority == target.Authority, lifetime.Token);
            status = enabled ? "Run-only completion voice consent recorded for this exact active voice conversation. Future new first deliveries only."
                : "Completion voice notices Off. Owned output cancellation requested; terminal status separately confirms physical join.";
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            status = "Completion voice notice choice unavailable/unconfirmed: " + exception.GetType().Name
                + ". No enabling or replay is inferred. Visual work remains independent.";
            activity.Complete(HostOperationOutcome.Failed);
        }
        finally { NotifyLocalEvents(); }
    }
}