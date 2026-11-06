using Kora.Application.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private string managementQuestion = "What work remains in the selected source conversation?";
    private SessionWorkspaceEntry? managementDestination;
    private IReadOnlyList<SessionWorkspaceEntry> managementDestinations = [];
    private RemainingWorkManagementReview? managementReview;
    private long managementEpoch;
    private long managementControl;
    private bool managementBusy;
    private string managementText = string.Empty;
    private bool managementEnabled;

    public bool ManagementEnabled
    {
        get => managementEnabled;
        set
        {
            if (value == managementEnabled) { return; }
            CancelManagementAnswer();
            managementEnabled = value;
            OnPropertyChanged();
            NotifyManagementAnswer();
        }
    }

    public string ManagementQuestion
    {
        get => managementQuestion;
        set
        {
            if (string.Equals(value, managementQuestion, StringComparison.Ordinal)) { return; }
            CancelManagementAnswer();
            managementQuestion = value;
            OnPropertyChanged();
        }
    }
    public IReadOnlyList<SessionWorkspaceEntry> ManagementDestinations => managementDestinations;
    public SessionWorkspaceEntry? ManagementDestination
    {
        get => managementDestination;
        set
        {
            if (value == managementDestination) { return; }
            CancelManagementAnswer();
            managementDestination = value;
            OnPropertyChanged();
        }
    }
    public string ManagementText => !closed && access.CanInspect && managementControl == access.ControlRevision ? managementText : string.Empty;
    public bool CanReviewManagementAnswer => managementEnabled && workManagement is not null && CanReadConversation && access.CanControl
        && !managementBusy && managementDestination is { } destination && ManagementDestinations.Contains(destination);
    public bool CanConfirmManagementAnswer => CanReviewManagementAnswer && managementReview is not null;

    public async Task ReviewManagementAnswerAsync()
    {
        if (!CanReviewManagementAnswer) { return; }
        CancelManagementAnswer();
        var target = RequireSelected();
        var destination = managementDestination!;
        var epoch = managementEpoch;
        var selection = selectionEpoch;
        var control = access.ControlRevision;
        managementControl = control;
        var question = managementQuestion;
        bool Live() => !closed && access.CanInspect && access.CanControl && access.ControlRevision == control && managementEpoch == epoch
            && selected == target && selectionEpoch == selection && managementDestination == destination
            && string.Equals(question, managementQuestion, StringComparison.Ordinal);
        managementBusy = true;
        NotifyManagementAnswer();
        try
        {
            var review = await workManagement!.ReviewAsync(target, destination, question, Live, lifetime.Token);
            if (!Live()) { review.Dispose(); return; }
            managementReview = review;
            managementText = review.Disclosure;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            if (Live()) { managementText = "Management review unavailable: " + exception.Message; }
        }
        finally { managementBusy = false; NotifyManagementAnswer(); }
    }

    public async Task ConfirmManagementAnswerAsync()
    {
        if (!CanConfirmManagementAnswer) { return; }
        var review = managementReview!;
        var epoch = managementEpoch;
        var control = managementControl;
        bool Live() => !closed && access.CanInspect && access.CanControl && access.ControlRevision == control && managementEpoch == epoch;
        managementBusy = true;
        managementText = "One confirmed local management attempt. No answer is confirmed yet; manual status and stop remain independent.";
        NotifyManagementAnswer();
        try
        {
            var receipt = await workManagement!.ConfirmAsync(review, Live, lifetime.Token);
            if (Live())
            {
                managementText = "Answer destination " + receipt.Message.Request.SessionId.Value.ToString("D")
                    + " | HOST " + receipt.Message.Outcome + " / " + receipt.Message.Reason
                    + " | task " + receipt.Task.State + "\n"
                    + (receipt.Message.AssistantResponse?.Body ?? "No retained ASSISTANT answer. No fallback or automatic retry.");
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            if (Live()) { managementText = "Management answer unconfirmed: " + exception.Message; }
        }
        finally
        {
            review.Dispose();
            if (ReferenceEquals(managementReview, review)) { managementReview = null; }
            managementBusy = false;
            NotifyManagementAnswer();
        }
    }

    public void CancelManagementAnswer()
    {
        managementEpoch++;
        managementReview?.Dispose();
        managementReview = null;
        managementText = string.Empty;
        NotifyManagementAnswer();
    }

    private void NotifyManagementAnswer()
    {
        var destinations = Sessions.Where(item => item.Authority.IsActive && item.Authority.SessionId != selected?.Authority.SessionId).ToArray();
        if (!managementDestinations.SequenceEqual(destinations))
        {
            managementDestinations = destinations;
            OnPropertyChanged(nameof(ManagementDestinations));
        }
        foreach (var name in new[] { nameof(ManagementText),
            nameof(CanReviewManagementAnswer), nameof(CanConfirmManagementAnswer) })
        { OnPropertyChanged(name); }
    }
}