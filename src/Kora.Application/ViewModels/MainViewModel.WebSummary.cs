using Kora.Application.Hosting;
using Kora.Application.Network;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private WebSummaryWorkflow? webSummary;
    private WebSummaryReview? webSummaryReview;
    private CancellationTokenSource? webSummaryCancellation;
    private Task webSummaryQuiescence = Task.CompletedTask;
    public bool IsWebSummaryActive { get; private set; }
    public string WebSummaryStatus { get; private set; } = string.Empty;
    public string WebSummaryText { get; private set; } = string.Empty;
    public IReadOnlyList<WebSummaryReview> WebSummaryConfirmations => webSummaryReview is { } review ? [review] : [];

    public void BindWebSummaryWorkflow(WebSummaryWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (webSummary is not null) { throw new InvalidOperationException("The web-summary workflow is already composed."); }
        webSummary = workflow;
    }

    public Task PrepareWebSummaryAsync(DetailContentReference reference) =>
        HostRequestRunner.RunAsync(RequestOrigin.LocalUi, () => ExecuteWebSummary(new(), SecurityAuditInitiator.LocalUser, reference));

    public Task ConfirmWebSummaryAsync(Guid reviewId) =>
        HostRequestRunner.RunAsync(RequestOrigin.LocalUi, () => ExecuteWebSummary(new(reviewId), SecurityAuditInitiator.LocalUser));

    public void ReportWebSummaryFailure(Exception exception)
    {
        WebSummaryFailed(exception.GetType().Name);
        RetireWebSummary();
        WebSummaryStatus = "Local web summary unavailable: native host admission failed.";
        NotifyWebSummary();
    }

    private void RetireWebSummary()
    {
        webSummary?.Retire();
        webSummaryReview = null;
        WebSummaryText = string.Empty;
        WebSummaryStatus = string.Empty;
        uiDispatcher.Post(NotifyWebSummary);
    }

    private void NotifyWebSummary()
    {
        OnPropertyChanged(nameof(WebSummaryText));
        OnPropertyChanged(nameof(WebSummaryStatus));
        OnPropertyChanged(nameof(WebSummaryConfirmations));
        OnPropertyChanged(nameof(IsWebSummaryActive));
        OnPropertyChanged(nameof(IsCancelTaskVisible));
    }

    private async Task StopWebSummary()
    {
        if (webSummaryCancellation is { } cancellation) { await cancellation.CancelAsync(); }
#pragma warning disable VSTHRD003 // Exit waits for the separately owned summary workflow to release its host request.
        await webSummaryQuiescence;
#pragma warning restore VSTHRD003
    }

    private async Task ExecuteWebSummary(WebSummaryCommand command, SecurityAuditInitiator initiator,
        DetailContentReference? exactReference = null)
    {
        if (command.Error is { } error)
        {
            RetireWebSummary();
            WebSummaryStatus = error;
            NotifyWebSummary();
            return;
        }
        if (command.Cancel)
        {
            RetireWebSummary();
            if (webSummaryCancellation is { } active) { await active.CancelAsync(); }
            WebSummaryStatus = "Local web summary cancelled. No new retrieval occurred.";
            NotifyWebSummary();
            return;
        }
        if (disposed || !IsCallMutationHostEligible || IsResponseInteractionPending || IsBusy || IsWebPageRetrievalActive
            || IsWebSummaryActive)
        {
            WebSummaryStatus = "Local web summary unavailable: current host, privacy, call or operation admission is closed.";
            NotifyWebSummary();
            return;
        }
        var snapshot = CurrentWebResultDetails()?.WebResult;
        if (snapshot is null || exactReference is { } reference && snapshot.Reference != reference)
        {
            WebSummaryStatus = "No exact current web-result source. This request does not fetch or follow an address.";
            NotifyWebSummary();
            return;
        }
        if (webSummary is null)
        {
            WebSummaryStatus = "Local web summary unavailable: the host workflow is not composed.";
            NotifyWebSummary();
            return;
        }
        var issuer = HostActivity.RequireCurrent();
        var origin = issuer.Request.Origin;
        var control = Interlocked.Read(ref webControlGeneration);
        var presentation = Interlocked.Read(ref webPresentationGeneration);
        var response = Interlocked.Read(ref webResponseRevision);
        var privacy = privacyObservation.Current.TopologyRevision;
        var recovery = Interlocked.Read(ref voiceRecoveryRevision);
        var call = CallPolicyRevision;
        bool Current() => SourceCurrent() && !issuer.Activity!.IsStopped && issuer.Outcome == HostOperationOutcome.Unknown
            && origin is RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice
            && (origin != RequestOrigin.ActivatedVoice || IsVoiceEnabled && HasVoiceConsent && CallObservation.AllowActivation);
        // Review outlives its request activity, but not its captured source/privacy/control epochs.
        bool SourceCurrent() => !disposed && IsCallMutationHostEligible && !IsResponseInteractionPending
            && control == Interlocked.Read(ref webControlGeneration) && presentation == Interlocked.Read(ref webPresentationGeneration)
            && response == Interlocked.Read(ref webResponseRevision) && privacy == privacyObservation.Current.TopologyRevision
            && recovery == Interlocked.Read(ref voiceRecoveryRevision) && call == CallPolicyRevision
            && ReferenceEquals(CurrentWebResultDetails()?.WebResult, snapshot);
        try
        {
            if (command.Confirmation is not { } confirmation)
            {
                if (!Current()) { throw new InvalidOperationException("Original input or source admission is closed."); }
                webSummaryReview = webSummary.Prepare(snapshot, SourceCurrent);
                WebSummaryText = string.Empty;
                WebSummaryStatus = $"Review local-only source use: exact snapshot {snapshot.Reference.ItemId.Value:D}, revision {snapshot.Reference.Revision}. "
                    + "Send its complete normalized text and provenance to the admitted local model only. Page text is untrusted data, not instructions. "
                    + "Network retrieval consent is not source-use consent. No refetch, hosted fallback, tools, source/answer retention or speech. "
                    + $"{LocalContextSessionTarget.FreshSessionDisclosure} Exact session: {webSummaryReview.SessionId:D}. "
                    + "Confirmation must use the same original input channel. "
                    + "Independent source sensitivity/use admission is also required; narrow credential checks are not comprehensive detection. "
                    + $"Confirm with the exact native control or '{WebSummaryCommand.ConfirmPrefix}{webSummaryReview.Id:D}'.";
                NotifyWebSummary();
                return;
            }
            using var cancellation = new CancellationTokenSource();
            webSummaryCancellation = cancellation;
            var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            webSummaryQuiescence = released.Task;
            IsWebSummaryActive = true;
            webSummaryReview = null;
            WebSummaryText = string.Empty;
            WebSummaryStatus = "Requesting an admitted local-only summary of the exact reviewed web-result snapshot.";
            NotifyWebSummary();
            try
            {
                var result = await webSummary.Confirm(confirmation, Current, initiator, cancellation.Token);
                if (!Current() || cancellation.IsCancellationRequested) { return; }
                WebSummaryStatus = result.Reason == ModelTurnReason.LocalEvidenceNotDisclosable
                    ? result.Outcome == ModelTurnOutcome.Unavailable
                        ? "Local web summary unavailable: source-specific sensitivity/source-use policy is missing or unknown. No answer, retry or fallback is admitted."
                        : "Local web summary denied by source-specific sensitivity/source-use policy. No answer is disclosed."
                    : result.Outcome == ModelTurnOutcome.Succeeded
                    ? $"Local model summary of exact retrieved snapshot {snapshot.Reference.ItemId.Value:D}, revision {snapshot.Reference.Revision}. Not a live page."
                    : $"Local web summary not produced: {result.Outcome} ({result.Reason}). No new retrieval or hosted fallback occurred.";
                WebSummaryText = result.Outcome == ModelTurnOutcome.Succeeded ? result.Response!.Answer! : string.Empty;
                NotifyWebSummary();
                if (!Current()) { RetireWebSummary(); }
            }
            finally
            {
                IsWebSummaryActive = false;
                webSummaryCancellation = null;
                released.TrySetResult();
                NotifyWebSummary();
            }
        }
        catch (OperationCanceledException)
        {
            RetireWebSummary();
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or IOException or ArgumentException)
        {
            WebSummaryFailed(exception.GetType().Name);
            RetireWebSummary();
            if (SourceCurrent())
            {
                WebSummaryStatus = "Local web summary refused: the complete source failed bounds, credential, or host admission checks.";
                NotifyWebSummary();
            }
        }
    }
}
