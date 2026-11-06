using Kora.Application.Presentation;
using Kora.Core.Auditing;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Commands;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private DetailsOfferService? detailsOfferService;
    private Func<Func<bool>>? captureDetailsAdmission;
    private Func<DetailsOfferRequest, Func<bool>, Task<bool>>? revealOfferedDetails;
    private Action<DetailsOfferRequest>? retireDetailsCue;
    private TaskCompletionSource detailsPresented = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private HostQuestionKey? activatedDetailsQuestion;
    private void CaptureDetailsReply(HostQuestionKey? key) => Volatile.Write(ref activatedDetailsQuestion, key);
    private Func<bool> detailsOfferPriorityEligible = static () => false;
    public void BindDetailsOfferPriority(Func<bool> eligible)
    { RetireDetailsOffer(); detailsOfferPriorityEligible = eligible; }
    public HostQuestionRecord? CurrentDetailsOffer { get; private set; }
    public bool HasDetailsQuestion => CurrentDetailsOffer is not null;
    public string DetailsQuestionText => CurrentDetailsOffer is { } question
        ? question.Spec.Text + "\nComplete retained ASSISTANT answer in the selected conversation."
            + "\nExpires: " + question.ExpiresAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
            + "\nThis offer does not start capture or additional model work."
        : string.Empty;
    public Task DetailsOfferOperation { get; private set; } = Task.CompletedTask;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003",
        Justification = "Exposes this owner's presentation acknowledgement for exact native operation coordination.")]
    public Task DetailsOfferPresented => detailsPresented.Task;
    public bool OfferDetailsOnce
    {
        get => foregroundRequestReview?.OfferDetailsOnce == true;
        set
        {
            if (foregroundRequestReview is null) { return; }
            SetForegroundRequestReview(sessionRequests!.SetDetailsOfferChoice(value));
            OnPropertyChanged();
        }
    }

    public void BindDetailsOffers(DetailsOfferService service, Func<Func<bool>> capture,
        Func<DetailsOfferRequest, Func<bool>, Task<bool>> reveal, Action<DetailsOfferRequest>? retireCue = null)
    {
        detailsOfferService?.Withdraw();
        detailsOfferService = service;
        captureDetailsAdmission = capture;
        revealOfferedDetails = reveal;
        retireDetailsCue = retireCue;
        BindDetailsOfferRequestAdmission();
        PrivacyClosureRequested -= OnDetailsOfferPrivacyClosure;
        PrivacyClosureRequested += OnDetailsOfferPrivacyClosure;
    }

    private void OnDetailsOfferPrivacyClosure(object? sender, EventArgs args) => RetireDetailsOffer();
    public void RetireDetailsOffer()
    {
        if (detailsOfferService is null) { return; }
        CurrentDetailsOffer = null;
        CaptureDetailsReply(null);
        uiDispatcher.Post(NotifyDetailsQuestion);
        detailsOfferService.Withdraw();
    }
    public void RevalidateDetailsOffer()
    {
        if (detailsOfferService?.Revalidate() == false && CurrentDetailsOffer is not null)
        { RetireDetailsOffer(); }
    }
    private void NotifyDetailsQuestion()
    {
        if (CurrentDetailsOffer is null) { CaptureDetailsReply(null); }
        OnPropertyChanged(nameof(HasDetailsQuestion));
        OnPropertyChanged(nameof(IsResponseInteractionPending));
        OnPropertyChanged(nameof(CommandInputPlaceholder));
        OnPropertyChanged(nameof(CommandSubmitLabel));
        OnPropertyChanged(nameof(HasSessionOperationReview));
        OnPropertyChanged(nameof(DetailsQuestionText));
    }

    private void BindDetailsOfferRequestAdmission() =>
        sessionRequests?.BindDetailsOfferAdmission(() =>
        {
            var native = captureDetailsAdmission?.Invoke() ?? (static () => false);
            var input = CaptureManualCallInput(RequestOrigin.LocalUi, CallPolicyRevision);
            var privacy = CaptureCustomizationVoiceEligibility();
            var revision = Interlocked.Read(ref workspaceSurfaceInputRevision);
            var name = assistantNameConfiguration.Get().Revision;
            return () => !disposed && IsHostInputEligible && !IsProtectedCall
                && input.SkillQuestionEligible!() && input.Revision == CallPolicyRevision
                && privacy() && native() && name == assistantNameConfiguration.Get().Revision
                && !IsApprovalPending && !IsModelQuestionPending
                && revision == Interlocked.Read(ref workspaceSurfaceInputRevision);
        });

    internal void PresentDetailsOffer(DetailsOfferRequest request)
    {
        if (detailsOfferService is null) { return; }
        detailsPresented = new(TaskCreationOptions.RunContinuationsAsynchronously);
        DetailsOfferOperation = RunDetailsOffer(request);
    }

    private async Task RunDetailsOffer(DetailsOfferRequest request)
    {
        try
        {
            await detailsOfferService!.RunAsync(request,
                () => !disposed && CanRevealPrivatePresentation && detailsOfferPriorityEligible() && pendingModelQuestion is null
                    && !IsGrantChangePending && !IsModelActionApprovalPending
                    && (CurrentDetailsOffer?.DetailsOffer!.OriginalRequest == request.Receipt.Message.Request
                        || (voiceRecognition.IsCaptureQuiescent || CanRetireWakeForManualInput) && IsReviewedVoiceCaptureReleased
                            && promptInputVoice?.IsActive != true && composerDictation?.IsActive != true),
                (question, live) => uiDispatcher.InvokeAsync(() =>
                {
                    if (!live()) { throw new OperationCanceledException("The exact queued Details presenter was retired."); }
                    CurrentDetailsOffer = question;
                    NotifyDetailsQuestion();
                    detailsPresented.TrySetResult();
                    return Task.CompletedTask;
                }),
                live => revealOfferedDetails!(request, live), CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { ReportHostInteractionFailure("Details offer failed; no retry or replacement was admitted. Failure type: " + exception.GetType().Name); }
        finally
        {
            await uiDispatcher.InvokeAsync(() =>
            {
                if (CurrentDetailsOffer?.DetailsOffer!.OriginalRequest == request.Receipt.Message.Request)
                { CurrentDetailsOffer = null; NotifyDetailsQuestion(); }
                retireDetailsCue?.Invoke(request);
                return Task.CompletedTask;
            });
        }
    }

    public Task AnswerDetailsOfferAsync(bool yes)
    {
        var exact = CurrentDetailsOffer?.Key;
        return exact is null ? Task.CompletedTask : detailsOfferService!.AnswerAsync(exact, yes, RequestOrigin.LocalUi, CancellationToken.None);
    }

    public Func<Task> CaptureDetailsOfferAnswer(bool yes)
    {
        var key = CurrentDetailsOffer?.Key;
        return () => key is null ? Task.CompletedTask
            : detailsOfferService!.AnswerAsync(key, yes, RequestOrigin.LocalUi, CancellationToken.None);
    }

    private async Task<bool> TryAnswerDetailsOffer(string text, SecurityAuditInitiator initiator)
    {
        var exact = CurrentDetailsOffer?.Key;
        if (exact is null) { return false; }
        var safety = commandRouter.Match(text, AssistantName).Command?.Action;
        if (safety is BuiltInAction.DisableListening or BuiltInAction.StopSpeaking
            || IsWakeCommand(text) && string.Equals(AssistantName, "Stop", StringComparison.OrdinalIgnoreCase)
                && string.Equals(text.Trim(), "listening", StringComparison.OrdinalIgnoreCase))
        { return false; }
        if (initiator == SecurityAuditInitiator.VoiceCommand)
        {
            if (Volatile.Read(ref activatedDetailsQuestion) != exact || !HasCurrentNameActivation(text))
            {
                Transcript = "This voice input was not admitted for the exact Details question. Use native controls or deliberately activate that question again.";
                return true;
            }
            text = commandRouter.Match(text, AssistantName).NormalizedTranscript;
        }
        var yes = text.Equals("yes", StringComparison.OrdinalIgnoreCase) || text.Equals("show it", StringComparison.OrdinalIgnoreCase);
        var no = text.Equals("no", StringComparison.OrdinalIgnoreCase) || text.Equals("not now", StringComparison.OrdinalIgnoreCase);
        if (!yes && !no)
        {
            Transcript = "Answer the exact Details question with Yes or Not now, or dismiss it. No other request was submitted.";
            return true;
        }
        await detailsOfferService!.AnswerAsync(exact, yes, initiator == SecurityAuditInitiator.VoiceCommand
            ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi, CancellationToken.None);
        return true;
    }
}