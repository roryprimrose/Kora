using Kora.Application.Configuration;
using Kora.Core.Conversation;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private Task quietHoursSuggestionWork = Task.CompletedTask;

    public async Task<QuietHoursSuggestionReview> ReviewQuietHoursSuggestionAsync(ConversationMessage message,
        Func<bool> sourceCurrent, Func<CancellationToken, Task> validateSource, CancellationToken token)
    {
        var service = quietHours ?? throw new InvalidOperationException("Quiet-hours configuration is unavailable.");
        if (disposed || quietHoursActive || !quietHoursSuggestionWork.IsCompleted)
        { throw new InvalidOperationException("Wait for the current quiet-hours operation before a fresh review."); }
        var privacy = privacyObservation.Current.TopologyRevision;
        var call = CallPolicyRevision;
        var ownership = settingsOwnershipRevision();
        var binding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var recovery = Volatile.Read(ref voiceRecoveryRevision);
        var consent = HasVoiceConsent;
        var name = AssistantName;
        bool Eligible() => !disposed && IsCallMutationHostEligible && !IsProtectedCall && !quietHoursActive
            && !IsBusy && !IsResponseInteractionPending && privacyObservation.Current.TopologyRevision == privacy
            && CallPolicyRevision == call && settingsOwnershipRevision() == ownership
            && Interlocked.Read(ref settingsOwnershipBindingRevision) == binding
            && Volatile.Read(ref voiceRecoveryRevision) == recovery && HasVoiceConsent == consent
            && string.Equals(AssistantName, name, StringComparison.Ordinal)
            && sourceCurrent();
        var work = QuietHoursSuggestionReview.ReviewAsync(service, communicationPolicy, message, Eligible, validateSource, token);
        quietHoursSuggestionWork = work;
        try { return await work.ConfigureAwait(false); }
        finally { quietHoursSuggestionWork = Task.CompletedTask; }
    }

    public async Task<bool> ConfirmQuietHoursSuggestionAsync(QuietHoursSuggestionReview review, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (disposed || quietHoursActive || !quietHoursSuggestionWork.IsCompleted || quietHours is null || !review.BelongsTo(quietHours))
        { throw new InvalidOperationException("Quiet-hours confirmation requires this owning host and no pending operation."); }
        var work = review.ConfirmAsync(token);
        quietHoursSuggestionWork = work;
        try { return await work; }
        finally { quietHoursSuggestionWork = Task.CompletedTask; NotifyQuietHours(); }
    }
}
