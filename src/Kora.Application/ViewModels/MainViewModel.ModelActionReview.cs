using System.Diagnostics.CodeAnalysis;
using Kora.Application.Diagnostics;
using Kora.Application.Presentation;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private const string ReviewPendingActionPhrase = "review pending action";
    private ModelActionReviewTarget? pendingModelActionReview;
    private long modelActionReviewEpoch;
    private string modelActionReviewStatus = string.Empty;
    private Func<ModelActionReviewTarget, bool>? presentModelActionReview;

    public void BindModelActionReviewPresentation(Func<ModelActionReviewTarget, bool>? present) =>
        presentModelActionReview = present;

    public ModelActionReviewTarget? PendingModelActionReview => pendingModelActionReview;
    public IReadOnlyList<ModelActionReviewTarget> ModelActionReviews =>
        pendingModelActionReview is { } target ? [target] : [];
    public AssistantState PresenceState => IsModelActionApprovalPending ? AssistantState.Waiting : State;
    public bool CanConfirmModelActionReview => ApproveModelActionCommand.CanExecute(null);
    public bool CanConfirmReusableModelActionReview => ApproveModelActionForSessionCommand.CanExecute(null);
    public bool CanRejectModelActionReview => RejectModelActionCommand.CanExecute(null);
    public string ModelActionReviewStatus
    {
        get => modelActionReviewStatus;
        private set => SetProperty(ref modelActionReviewStatus, value);
    }

    public ModelActionReviewTarget? CaptureModelActionReview() =>
        IsModelActionReviewCurrent(pendingModelActionReview) ? pendingModelActionReview : null;

    public bool IsModelActionReviewCurrent([NotNullWhen(true)] ModelActionReviewTarget? target) =>
        target is not null && ReferenceEquals(target, pendingModelActionReview)
        && ReferenceEquals(target.Audit, pendingModelActionAudit)
        && target.Epoch == modelActionReviewEpoch && pendingModelAction is not null
        && IsCallMutationHostEligible && target.Eligible()
        && (pendingModelActionArtifact is not { } artifact || artifactCatalogue.IsCurrent(artifact.Id, artifact.Digest));

    public void ReportModelActionReviewUnavailable() =>
        ModelActionReviewStatus = "Original action review unavailable or changed. No replacement was selected. Inspect the original outcome before a fresh request.";

    public bool ActivateModelActionReview(ModelActionReviewTarget? target)
    {
        var origin = OriginalOrigin();
        using var activity = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Presentation, origin);
        if (origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || !IsModelActionReviewCurrent(target) || presentModelActionReview is not { } present)
        {
            ReportModelActionReviewUnavailable();
            activity.Complete(HostOperationOutcome.Cancelled);
            return false;
        }
        try
        {
            PresentPendingModelActionReview(requestWindow: false);
            if (!IsModelActionReviewCurrent(target) || !present(target) || !IsModelActionReviewCurrent(target))
            {
                ReportModelActionReviewUnavailable();
                activity.Complete(HostOperationOutcome.Cancelled);
                return false;
            }
            ModelActionReviewStatus = "Original action review opened. Confirm or reject separately; opening it did not approve anything.";
            activity.Complete(HostOperationOutcome.Completed);
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            ApplicationLog.Error(logger, exception, "Opening the original model-action review");
            ReportModelActionReviewUnavailable();
            activity.Complete(HostOperationOutcome.Failed);
            return false;
        }
    }

    public Task ConfirmModelActionReviewAsync(ModelActionReviewTarget? target, ModelApprovalScope scope)
    {
        if (OriginalOrigin() != RequestOrigin.LocalUi || !IsModelActionReviewCurrent(target))
        {
            ReportModelActionReviewUnavailable();
            return Task.CompletedTask;
        }
        var command = scope switch
        {
            ModelApprovalScope.Once => ApproveModelActionCommand,
            ModelApprovalScope.Session => ApproveModelActionForSessionCommand,
            ModelApprovalScope.Always => ApproveModelActionAlwaysCommand,
            _ => throw new ArgumentOutOfRangeException(nameof(scope)),
        };
        if (!command.CanExecute(null))
        {
            ReportModelActionReviewUnavailable();
            return Task.CompletedTask;
        }
        return command.ExecuteAsync();
    }

    public Task RejectModelActionReviewAsync(ModelActionReviewTarget? target)
    {
        if (OriginalOrigin() != RequestOrigin.LocalUi || !IsModelActionReviewCurrent(target))
        {
            ReportModelActionReviewUnavailable();
            return Task.CompletedTask;
        }
        if (!RejectModelActionCommand.CanExecute(null))
        {
            ReportModelActionReviewUnavailable();
            return Task.CompletedTask;
        }
        return RejectModelActionCommand.ExecuteAsync();
    }

    private bool IsModelActionReviewCommand(string text)
    {
        var body = commandRouter.IsActivationPrefixed(text, AssistantName)
            ? text[AssistantName.Length..].TrimStart(' ', ',', ':', '-', '\u2014')
            : text.Trim();
        return string.Equals(body.Trim(), ReviewPendingActionPhrase, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryActivateModelActionReview(string text, SecurityAuditInitiator initiator, ManualCallInput input)
    {
        if (!IsModelActionReviewCommand(text)) { return false; }
        var origin = OriginalOrigin(initiator);
        if (!isAssistantNameAvailable || initiator is not (SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || input.Origin != origin || !input.Eligible()
            || input.Revision != CallPolicyRevision
            || input.AssistantNameRevision != assistantNameConfiguration.Get().Revision
            || origin == RequestOrigin.ActivatedVoice && !HasCurrentNameActivation(text))
        {
            ReportModelActionReviewUnavailable();
            return true;
        }
        ActivateModelActionReview(input.ModelActionReview);
        return true;
    }

    private void NotifyModelActionReviewChanged()
    {
        OnPropertyChanged(nameof(PendingModelActionReview));
        OnPropertyChanged(nameof(ModelActionReviews));
        OnPropertyChanged(nameof(PresenceState));
        NotifyModelActionReviewAvailability();
    }

    private void NotifyModelActionReviewAvailability()
    {
        OnPropertyChanged(nameof(CanConfirmModelActionReview));
        OnPropertyChanged(nameof(CanConfirmReusableModelActionReview));
        OnPropertyChanged(nameof(CanRejectModelActionReview));
    }

    private void OnModelActionReviewAvailabilityChanged(object? sender, EventArgs e) => NotifyModelActionReviewAvailability();

    private Func<bool> CaptureModelActionReviewHost()
    {
        var admitted = IsCallMutationHostEligible;
        var privacy = privacyObservation.Current.TopologyRevision;
        var owner = auditImpactOwnerRevision();
        var binding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        return () => admitted && IsCallMutationHostEligible
            && privacy == privacyObservation.Current.TopologyRevision
            && owner == auditImpactOwnerRevision()
            && binding == Interlocked.Read(ref settingsOwnershipBindingRevision);
    }

    private void PresentPendingModelActionReview(bool requestWindow = true)
    {
        var action = pendingModelAction!.Value;
        var command = commandCatalog.GetCommands(AssistantName).Single(item => item.Action == action);
        var voicePrefix = RequireAssistantNameForVoiceApproval ? $"{AssistantName}, " : string.Empty;
        var approvalChoices = pendingModelActionArtifact is not { } artifact
            ? $"Say \u201c{voicePrefix}approve once\u201d, \u201c{voicePrefix}approve for this session\u201d, \u201c{voicePrefix}always allow this\u201d, or \u201c{voicePrefix}reject\u201d."
            : $"The proposal came from artifact revision {artifact.Digest[..12]}. Only a single-use approval is valid. Say \u201c{voicePrefix}approve once\u201d or \u201c{voicePrefix}reject\u201d.";
        ShowInformation("Approve a model-suggested action?", $"{command.Description} {approvalChoices} You can also use the buttons below.", requestWindow);
    }
}
