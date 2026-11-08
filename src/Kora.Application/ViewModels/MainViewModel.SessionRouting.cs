using Kora.Application.Diagnostics;
using Kora.Application.Hosting.SessionRouting;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private Func<string, RequestOrigin, Func<bool>, Task<SessionRoutingReceipt?>>? routeSessionRequest;
    private Func<IDisposable?>? retainRoutingPresentation;

    public void BindSessionRouting(Func<string, RequestOrigin, Func<bool>, Task<SessionRoutingReceipt?>>? route,
        Func<IDisposable?>? retainPresentation = null)
    {
        routeSessionRequest = route;
        retainRoutingPresentation = retainPresentation;
    }

    private async Task<bool> TryRouteSessionRequestAsync(string text, SecurityAuditInitiator initiator, ManualCallInput? original)
    {
        if (routeSessionRequest is not { } route) { return false; }
        var origin = OriginalOrigin(initiator);
        var wakeCurrent = original?.WakeActivationCurrent;
        if (wakeCurrent is not null && (origin != RequestOrigin.ActivatedVoice || !wakeCurrent()))
        {
            ShowInformation("Voice request retired.", "The original wake or host admission changed. No destination was selected.");
            return true;
        }
        if (origin == RequestOrigin.ActivatedVoice && wakeCurrent is null
            && !HasCurrentNameActivation(text))
        {
            ShowInformation("Activate me before routing a request.", "Use the current assistant name. No destination was selected.");
            return true;
        }
        var body = wakeCurrent is not null ? text
            : string.Equals(text.Trim().TrimEnd(',', ':', '-', '—').TrimEnd(), AssistantName, StringComparison.OrdinalIgnoreCase)
            ? string.Empty : commandRouter.IsActivationPrefixed(text, AssistantName)
                ? text[AssistantName.Length..].TrimStart(' ', ',', ':', '-', '—') : text;
        if (string.IsNullOrWhiteSpace(body))
        {
            ShowInformation("No request was heard.", "Provide a request after the assistant name. No destination was selected.");
            return true;
        }
        var input = original ?? CaptureManualCallInput(origin, CallPolicyRevision);
        var originalEligible = CaptureNewRequestOpeningEligibility(input);
        if (!originalEligible())
        {
            ShowInformation("Session routing stopped.", "The original input or host admission changed. Nothing was submitted.");
            return true;
        }
        var openingEligible = CaptureNewRequestOpeningEligibility(input with { Eligible = input.RoutingStableEligible });
        bool stable() => openingEligible()
            && input.OwnershipBindingRevision == Interlocked.Read(ref settingsOwnershipBindingRevision)
            && input.RoutingVoiceOwnerRevision == Interlocked.Read(ref voiceOwnershipBindingRevision);
        var voiceRequest = new SessionRoutingVoiceRequest(body, HostActivity.RequireCurrent().Request,
            stable, voiceRecognition as Kora.Core.Voice.IQuestionVoiceRecognitionService,
            input.RoutingWakeContinuation?.Invoke(), routingCaptureRelease);
        var eligible = CaptureNewRequestOpeningEligibility(input with { Eligible = () => voiceRequest.IsLive });
        var inputRevision = Interlocked.Read(ref workspaceSurfaceInputRevision);
        var composerRevision = Interlocked.Read(ref attachmentCatalogueComposerRevision);
        bool Live() => eligible() && Interlocked.Read(ref workspaceSurfaceInputRevision) == inputRevision
            && (origin != RequestOrigin.LocalUi || Interlocked.Read(ref attachmentCatalogueComposerRevision) == composerRevision)
            && (input.RoutingWakeContinuation is not null || wakeCurrent?.Invoke() != false);
        if (!Live())
        {
            voiceRequest.ReleaseOwner();
            ShowInformation("Session routing stopped.", "The original input or host admission changed. Nothing was submitted.");
            return true;
        }
        try
        {
            var automaticDetails = CaptureAutomaticResponse();
            sessionRoutingVoiceRequest = voiceRequest;
            PropertyChanged += RevalidateSessionRoutingVoice;
            PrivacyClosureRequested += RoutingVoicePrivacyClosed;
            var wasBusy = IsBusy;
            IsBusy = true;
            using var routingPresentationLease = retainRoutingPresentation?.Invoke();
            SessionRoutingReceipt? result;
            try { result = await route(body, origin, Live); }
            finally
            {
                await JoinSessionRoutingVoice();
                voiceRequest.ReleaseOwner();
                sessionRoutingVoiceRequest = null;
                PropertyChanged -= RevalidateSessionRoutingVoice;
                PrivacyClosureRequested -= RoutingVoicePrivacyClosed;
                IsBusy = wasBusy;
            }
            if (!stable()) { return true; }
            if (result is null)
            {
                ShowInformation("Session routing cancelled.", "No request was submitted. Other sessions' unsent input and permissions are unchanged.");
            }
            else
            {
                var name = result.Target.Metadata?.Name.Value ?? "Unnamed conversation";
                var status = "Original USER request retained in that exact session. HOST " + result.Submission.Message.Outcome
                    + " / " + result.Submission.Message.Reason + ". No other unsent input, grants or provider state was transferred.";
                if (result.Submission.Message.AssistantResponse is { } answer)
                {
                    var answerPresentation = AnswerPresentation.From(answer.Body);
                    PresentResponse(AssistantState.Information, "Request destination: " + name + ".",
                        status + "\n\nAssistant answer (generated locally; verify important details):\n" + answerPresentation.Brief,
                        requestWindow: automaticDetails is null,
                        modelAnswer: new(answer.Body, result.Submission.Message.Request));
                    PresentAutomaticResponse(automaticDetails);
                }
                else { ShowInformation("Request destination: " + name + ".", status); }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ApplicationLog.Error(logger, exception, "Routing an original session request");
            if (stable())
            {
                ShowFailure("Session routing stopped.",
                    "The reviewed destination or original admission is unavailable or changed. "
                    + (exception is SessionRoutingUnconfirmedException routingFailure
                        ? DescribeRoutingAttempt(routingFailure.Creation, "creation") : string.Empty)
                    + (exception is ConversationSubmissionUnconfirmedException submissionFailure
                        ? DescribeRoutingAttempt(submissionFailure.Request, "submission")
                        : exception.InnerException is ConversationSubmissionUnconfirmedException innerSubmission
                            ? DescribeRoutingAttempt(innerSubmission.Request, "submission") : string.Empty)
                    + "No fallback, replay or retry was performed.");
            }
        }
        return true;
    }

    private static string DescribeRoutingAttempt(HostRequest request, string kind) =>
        "Inspect exact " + kind + " session " + request.SessionId.Value.ToString("D")
        + ", request " + request.RequestId.Value.ToString("D") + ", task " + request.TaskId.Value.ToString("D") + ". ";
}
