using Kora.Application.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Configuration.RegisteredSettings;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private Task ordinaryDeadlineSuggestionWork = Task.CompletedTask;

    public Task<OrdinaryTurnDeadlineSuggestionReview> ReviewOrdinaryTurnDeadlineSuggestionAsync(ConversationMessage message,
        Func<bool> sourceCurrent, Func<CancellationToken, Task> validateSource, CancellationToken token) =>
        ReviewOrdinaryTurnDeadlineSuggestionAsync(message, sourceCurrent, validateSource, null, token);

    public async Task<OrdinaryTurnDeadlineSuggestionReview> ReviewOrdinaryTurnDeadlineSuggestionAsync(ConversationMessage message,
        Func<bool> sourceCurrent, Func<CancellationToken, Task> validateSource, RegisteredSetting? selectedSetting, CancellationToken token)
    {
        var service = ordinaryDeadlineConfiguration ?? throw new InvalidOperationException("Future task deadline configuration is unavailable.");
        if (disposed || ordinaryDeadlineControlActive || !ordinaryDeadlineSuggestionWork.IsCompleted)
        { throw new InvalidOperationException("Wait for the current deadline operation before a fresh review."); }
        var privacy = privacyObservation.Current.TopologyRevision;
        var call = CallPolicyRevision;
        var ownership = settingsOwnershipRevision();
        var binding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var control = Interlocked.Read(ref ordinaryDeadlineControlRevision);
        var recovery = Volatile.Read(ref voiceRecoveryRevision);
        var consent = HasVoiceConsent;
        var name = AssistantName;
        bool Eligible() => !disposed && IsCallMutationHostEligible && !IsProtectedCall && !ordinaryDeadlineControlActive
            && !IsBusy && !IsResponseInteractionPending && privacyObservation.Current.TopologyRevision == privacy
            && CallPolicyRevision == call && settingsOwnershipRevision() == ownership
            && Interlocked.Read(ref settingsOwnershipBindingRevision) == binding
            && Interlocked.Read(ref ordinaryDeadlineControlRevision) == control
            && Volatile.Read(ref voiceRecoveryRevision) == recovery && HasVoiceConsent == consent
            && string.Equals(AssistantName, name, StringComparison.Ordinal) && sourceCurrent();
        var work = OrdinaryTurnDeadlineSuggestionReview.ReviewAsync(service, communicationPolicy, TimeProvider.System,
            message, Eligible, validateSource, token, selectedSetting);
        ordinaryDeadlineSuggestionWork = work;
        try { return await work.ConfigureAwait(false); }
        finally { ordinaryDeadlineSuggestionWork = Task.CompletedTask; }
    }

    public async Task<bool> ConfirmOrdinaryTurnDeadlineSuggestionAsync(OrdinaryTurnDeadlineSuggestionReview review, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (disposed || ordinaryDeadlineControlActive || !ordinaryDeadlineSuggestionWork.IsCompleted
            || ordinaryDeadlineConfiguration is null || !review.BelongsTo(ordinaryDeadlineConfiguration))
        { throw new InvalidOperationException("Deadline confirmation requires this owning host and no pending operation."); }
        var work = review.ConfirmAsync(token);
        ordinaryDeadlineSuggestionWork = work;
        try { return await work; }
        finally
        {
            ordinaryDeadlineSuggestionWork = Task.CompletedTask;
            ordinaryDeadlineObservation = null;
            OnPropertyChanged(nameof(SavedOrdinaryDeadlineMinutes));
            OnPropertyChanged(nameof(IsOrdinaryDeadlineDraftChanged));
            OnPropertyChanged(nameof(CanChangeOrdinaryDeadlineNative));
        }
    }
}
