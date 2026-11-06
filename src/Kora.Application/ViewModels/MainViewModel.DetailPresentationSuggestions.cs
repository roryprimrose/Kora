using Kora.Application.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private Task detailPresentationSuggestionWork = Task.CompletedTask;

    public async Task<DetailPresentationSuggestionReview> ReviewDetailPresentationSuggestionAsync(ConversationMessage message,
        Func<bool> sourceCurrent, Func<CancellationToken, Task> validateSource, CancellationToken token)
    {
        var service = detailPresentation ?? throw new InvalidOperationException("Details configuration is unavailable.");
        if (!CanControlDetailPresentation || !detailPresentationSuggestionWork.IsCompleted)
        { throw new InvalidOperationException("Wait for the current Details operation before a fresh review."); }
        var host = CaptureConfigurationControlEligibility(RequestOrigin.LocalUi);
        var privacy = privacyObservation.Current.TopologyRevision;
        var call = CallPolicyRevision;
        var ownership = settingsOwnershipRevision();
        var binding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var name = assistantNameConfiguration.Get().Revision;
        bool Eligible() => CanControlDetailPresentation && host() && CanRevealPrivatePresentation
            && privacyObservation.Current.TopologyRevision == privacy && CallPolicyRevision == call
            && settingsOwnershipRevision() == ownership && Interlocked.Read(ref settingsOwnershipBindingRevision) == binding
            && assistantNameConfiguration.Get().Revision == name && sourceCurrent();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        detailPresentationSuggestionWork = completion.Task;
        try
        {
            return await DetailPresentationSuggestionReview.ReviewAsync(service, communicationPolicy,
                speechSuggestionBinding?.Time ?? TimeProvider.System, message, Eligible, validateSource, token).ConfigureAwait(false);
        }
        finally { detailPresentationSuggestionWork = Task.CompletedTask; completion.SetResult(); }
    }

    public async Task<bool> ConfirmDetailPresentationSuggestionAsync(DetailPresentationSuggestionReview review, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (!CanControlDetailPresentation || !detailPresentationSuggestionWork.IsCompleted
            || detailPresentation is null || !review.BelongsTo(detailPresentation))
        { throw new InvalidOperationException("Details confirmation requires this owning host and no pending operation."); }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        detailPresentationSuggestionWork = completion.Task;
        try { return await review.ConfirmAsync(token).ConfigureAwait(false); }
        finally
        {
            detailPresentationSuggestionWork = Task.CompletedTask;
            completion.SetResult();
            uiDispatcher.Post(() => { if (!disposed) { NotifyDetailPresentation(); } });
        }
    }
}
