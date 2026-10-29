using Kora.Application.Configuration.RegisteredSettings;
using Kora.Application.Diagnostics;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly RegisteredSettingUndo settingsUndo;
    private Task settingsUndoWork = Task.CompletedTask;
    private string? settingsUndoError;

    public string SettingsUndoDescription => settingsUndoError ?? settingsUndo.Description;
    public bool CanReviewSettingsUndo => !disposed && settingsUndoWork.IsCompleted && settingsUndo.CanReview
        && IsCallMutationHostEligible && !IsResponseInteractionPending;
    public bool CanConfirmSettingsUndo => !disposed && settingsUndoWork.IsCompleted && settingsUndo.CanConfirm;

    private void OnSettingsUndoChanged(object? sender, EventArgs args) => uiDispatcher.Post(() =>
    {
        if (disposed) { return; }
        settingsUndoError = null;
        NotifySettingsUndo();
    });

    private void NotifySettingsUndo()
    {
        OnPropertyChanged(nameof(SettingsUndoDescription));
        OnPropertyChanged(nameof(CanReviewSettingsUndo));
        OnPropertyChanged(nameof(CanConfirmSettingsUndo));
    }

    public async Task ExecuteSettingsUndoAsync(RegisteredSettingSuggestionOperation operation,
        SecurityAuditInitiator initiator, CancellationToken token, Func<bool>? nativeCurrent = null)
    {
        if (disposed || !settingsUndoWork.IsCompleted)
        { throw new InvalidOperationException("Settings undo is unavailable while closing or another undo operation is pending."); }
        if (IsResponseInteractionPending)
        {
            Transcript = "Finish the pending question or approval before reviewing settings undo. Nothing was applied.";
            return;
        }
        settingsUndoError = null;
        var privacy = privacyObservation.Current.TopologyRevision;
        var ownership = settingsOwnershipRevision();
        var binding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var recovery = Volatile.Read(ref voiceRecoveryRevision);
        var name = AssistantName;
        var origin = HostActivity.RequireCurrent().Request.Origin;
        bool Eligible() => !disposed && IsCallMutationHostEligible && !IsBusy && !IsResponseInteractionPending
            && !IsSpeechProviderOperationActive && !applyingAppearance && !responseModeControlActive
            && privacyObservation.Current.TopologyRevision == privacy && settingsOwnershipRevision() == ownership
            && Interlocked.Read(ref settingsOwnershipBindingRevision) == binding
            && Volatile.Read(ref voiceRecoveryRevision) == recovery
            && string.Equals(AssistantName, name, StringComparison.Ordinal)
            && (initiator != SecurityAuditInitiator.LocalUser || nativeCurrent is not null && nativeCurrent())
            && (origin != RequestOrigin.ActivatedVoice || IsVoiceEnabled && HasVoiceConsent
                && CallObservation.AllowActivation);
        try
        {
            var confirmed = true;
            if (!Eligible()) { throw new InvalidOperationException("Undo requires current original input and the owning private host, without pending decisions."); }
            if (operation == RegisteredSettingSuggestionOperation.Cancel) { settingsUndo.Cancel(); }
            else if (operation == RegisteredSettingSuggestionOperation.Review) { settingsUndo.ReviewLast(initiator, Eligible); }
            else
            {
                var work = settingsUndo.ConfirmAsync(initiator, token);
                settingsUndoWork = work;
                confirmed = await work;
            }
            if (!Eligible()) { return; }
            PresentResponse(confirmed ? AssistantState.Information : AssistantState.Failure, "Settings undo.", settingsUndo.Description,
                refreshOutput: false, sourceAssigned: () => forceVisualResponse = true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException
            or ArgumentException or OperationCanceledException)
        {
            ApplicationLog.Error(logger, exception, "Registered settings undo");
            settingsUndoError = "Settings undo not confirmed. " + exception.Message
                + " A write may have persisted if execution began; inspect saved state and audit. No automatic retry or rollback.";
            PresentResponse(AssistantState.Failure, "Settings undo not confirmed.", settingsUndoError,
                refreshOutput: false, sourceAssigned: () => forceVisualResponse = true);
        }
        finally
        {
            settingsUndoWork = Task.CompletedTask;
            NotifySettingsUndo();
        }
    }
}
