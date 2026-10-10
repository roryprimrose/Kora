using Kora.Application.Network;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Network;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Kora.Tools.Network;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly WebPageGet? webPageGet;
    private bool webPageRetrievalActive;
    private CancellationTokenSource? webPageCancellation;
    private Task webPageQuiescence = Task.CompletedTask;
    private AdmittedDetailContent? webResultDetails;
    private Func<bool> webResultAdmission = static () => false;
    private string? webResultResponseBody;
    private long webPresentationGeneration = 1;
    private long webControlGeneration = 1;
    private long webResponseRevision;

    public event EventHandler? WebResultDetailsChanged;
    public DetailContentReference? WebResultDetailsReference => CurrentWebResultDetails()?.Reference;
    public bool HasWebResultDetails => WebResultDetailsReference is not null;
    public IReadOnlyList<DetailContentReference> WebResultDetailActions =>
        WebResultDetailsReference is { } reference ? [reference] : [];
    public string WebResultDetailStatus { get; private set; } = string.Empty;

    internal void ReportWebResultDetailStatus(string status)
    {
        WebResultDetailStatus = status;
        OnPropertyChanged(nameof(WebResultDetailStatus));
    }

    internal AdmittedDetailContent? ResolveWebResultDetails(DetailContentReference reference)
    {
        var content = CurrentWebResultDetails();
        return content?.Reference == reference ? content : null;
    }

    private AdmittedDetailContent? CurrentWebResultDetails()
    {
        if (webResultDetails is null) { return null; }
        if (webResultAdmission()) { return webResultDetails; }
        RetireWebResultDetails();
        return null;
    }

    internal void RetireWebResultDetails(bool retireControl = true)
    {
        if (retireControl) { Interlocked.Increment(ref webControlGeneration); }
        Interlocked.Increment(ref webPresentationGeneration);
        var retiredBody = webResultResponseBody;
        var retiredResponseRevision = Interlocked.Read(ref webResponseRevision);
        webResultDetails = null;
        webResultAdmission = static () => false;
        webResultResponseBody = null;
        uiDispatcher.Post(() =>
        {
            // A queued retirement must not erase a later response with identical text.
            if (retiredBody is not null && Interlocked.Read(ref webResponseRevision) == retiredResponseRevision
                && string.Equals(ResponseBody, retiredBody, StringComparison.Ordinal))
            {
                ResponseBody = "The volatile web-result source was retired. A fresh explicit retrieval is required; opening details never fetches.";
            }

            OnPropertyChanged(nameof(WebResultDetailsReference));
            OnPropertyChanged(nameof(WebResultDetailActions));
            OnPropertyChanged(nameof(HasWebResultDetails));
            OnPropertyChanged(nameof(IsCancelTaskVisible));
            ReportWebResultDetailStatus(string.Empty);
            WebResultDetailsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private async Task StopWebPageRetrievalAsync()
    {
        if (webPageCancellation is { } cancellation) { await cancellation.CancelAsync(); }
#pragma warning disable VSTHRD003 // Exit yields the UI while the separately owned retrieval reaches its finally and signals the asynchronous release TCS.
        await webPageQuiescence;
#pragma warning restore VSTHRD003
    }

    public bool IsWebPageRetrievalActive
    {
        get => webPageRetrievalActive;
        private set
        {
            SetProperty(ref webPageRetrievalActive, value);
            OnPropertyChanged(nameof(IsCancelTaskVisible));
        }
    }

    internal async Task ExecuteWebPageCommandAsync(
        WebPageCommand command,
        SecurityAuditInitiator initiator,
        CancellationToken cancellationToken = default)
    {
        if (disposed || IsResponseInteractionPending) { return; }
        if (command.Error is not null)
        {
            ShowFailure("Clarify the web-page address.", command.Error);
            return;
        }
        if (webPageGet is null)
        {
            ShowFailure("Web-page retrieval is unavailable.", "The host retrieval action is not composed.");
            return;
        }
        if (!IsHostInputEligible || IsBusy || IsWebPageRetrievalActive)
        {
            ShowInformation("Web-page retrieval is unavailable.",
                "Use the current owning unlocked host and wait for the active operation to finish.");
            return;
        }

        var original = HostActivity.RequireCurrent().Request;
        RetireWebResultDetails();
        var controlGeneration = Interlocked.Read(ref webControlGeneration);
        var presentationGeneration = Interlocked.Read(ref webPresentationGeneration);
        var privacyRevision = privacyObservation.Current.TopologyRevision;
        var recoveryRevision = Interlocked.Read(ref voiceRecoveryRevision);
        var callRevision = CallPolicyRevision;
        bool Eligible() => IsCallMutationHostEligible && !IsResponseInteractionPending
            && original.Origin is RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice
            && !cancellationToken.IsCancellationRequested
            && Interlocked.Read(ref webControlGeneration) == controlGeneration
            && Interlocked.Read(ref webPresentationGeneration) == presentationGeneration
            && privacyObservation.Current.TopologyRevision == privacyRevision
            && Interlocked.Read(ref voiceRecoveryRevision) == recoveryRevision
            && CallPolicyRevision == callRevision
            && (original.Origin != RequestOrigin.ActivatedVoice || IsVoiceEnabled && HasVoiceConsent
                && CallObservation.AllowActivation);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        webPageCancellation = cancellation;
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        webPageQuiescence = released.Task;
        IsWebPageRetrievalActive = true;
        try
        {
            if (!Eligible())
            {
                ShowInformation("Web-page retrieval is unavailable.",
                    "The original input, host ownership, privacy or control generation is no longer eligible.");
                return;
            }
            var address = command.Address!;
            var result = await webPageGet.ExecuteAsync(
                new(address),
                initiator,
                static (_, _) => ValueTask.FromResult(true),
                cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!Eligible() || HostActivity.Current?.Request != original)
            {
                throw new InvalidOperationException(
                    "Host admission changed before the retrieved page could be presented.");
            }
            if (result.Outcome == WebPageGetOutcome.Succeeded)
            {
                using var presentation = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Presentation);
                var content = WebResultSnapshot.Capture(new(new(Guid.NewGuid()), 1), original, controlGeneration,
                    address, result, DateTimeOffset.UtcNow);
                var body = $"{result.FinalAddress}{Environment.NewLine}{Environment.NewLine}{result.Text}"
                    + (result.Truncated ? Environment.NewLine + Environment.NewLine + "[Result truncated]" : string.Empty);
                PresentResponse(
                    AssistantState.Information,
                    "Web page retrieved.",
                    body, sourceAssigned: () =>
                    {
                        // Publish before window/output callbacks: any later closure retires this
                        // actual source, rather than issuing a fresh snapshot after that closure.
                        presentationGeneration = Interlocked.Read(ref webPresentationGeneration);
                        var isExactResponse = string.Equals(ResponseTitle, "Web page retrieved.", StringComparison.Ordinal)
                            && string.Equals(ResponseBody, body, StringComparison.Ordinal);
                        if (!Eligible())
                        {
                            if (isExactResponse)
                            {
                                ResponseBody = "The web-result presentation was retired before admission completed. No native details were issued.";
                            }
                            return;
                        }
                        if (!isExactResponse)
                        {
                            return;
                        }
                        webResultAdmission = () => Eligible() && IsVisualResponseVisible && State != AssistantState.Hidden;
                        webResultResponseBody = body;
                        webResultDetails = content;
                        OnPropertyChanged(nameof(WebResultDetailsReference));
                        OnPropertyChanged(nameof(WebResultDetailActions));
                        OnPropertyChanged(nameof(HasWebResultDetails));
                        OnPropertyChanged(nameof(IsCancelTaskVisible));
                        WebResultDetailsChanged?.Invoke(this, EventArgs.Empty);
                    });
                presentation.Complete(ResolveWebResultDetails(content.Reference) is not null
                    ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            }
            else
            {
                ShowFailure(
                    "Web page was not retrieved.",
                    $"{result.Reason}. Address: {result.FinalAddress}");
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            RetireWebResultDetails();
        }
        catch (Exception exception) when (exception is HttpRequestException
            or IOException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException)
        {
            WebPagePresentationFailed(logger, exception.GetType().Name);
            var canReport = Interlocked.Read(ref webPresentationGeneration) == presentationGeneration;
            RetireWebResultDetails();
            if (canReport && !disposed && !IsResponseInteractionPending)
            {
                ShowFailure("Web page was not retrieved.", exception.Message);
            }
        }
        finally
        {
            IsWebPageRetrievalActive = false;
            webPageCancellation = null;
            released.TrySetResult();
        }
    }
}
