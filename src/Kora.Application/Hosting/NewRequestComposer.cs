using System.Diagnostics;

using Kora.Application.Infrastructure;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Hosting;

/// <summary>One native intent, one fresh target attempt, and distinct reviewed draft saving and submission.</summary>
public sealed partial class NewRequestComposer(
    SessionWorkspaceService workspace, SessionConversationService conversation, ISessionWorkspaceAccess access,
    ILogger<NewRequestComposer> logger) : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private string body = string.Empty;
    private string? reviewed;
    private long epoch;
    private long reviewControl;
    private bool closed;
    private bool busy;
    private bool creationHeld;
    private bool submitted;
    private NewRequestCreationAttempt? creation;
    private HostRequest? submission;
    private SessionWorkspaceEntry? target;
    private ConversationResponse? assistantResponse;
    private ActivityLink[] creationLinks = [];
    private string status = "Relatedness inference is unavailable. Explicitly create a fresh New conversation; no selected, named, recent or Done session is inferred.";

    public string Body
    {
        get => body;
        set
        {
            if (!CanEdit) { return; }
            body = value;
            RetireReview();
            Notify();
        }
    }
    public string ReviewedBody => reviewed ?? string.Empty;
    public string Status => status;
    public NewRequestCreationAttempt? Creation => creation;
    public HostRequest? Submission => submission;
    public SessionWorkspaceEntry? Target => target;
    public ConversationResponse? AssistantResponse => !closed && access.CanInspect ? assistantResponse : null;
    public bool Dirty => !string.Equals(body, baseline, StringComparison.Ordinal);
    public bool Busy => busy;
    public bool CanEdit => !closed && access.CanInspect && !submitted;
    public bool CanCreate => CanEdit && access.CanControl && !busy && creation is null;
    public bool CanReview => CanEdit && access.CanControl && !busy && target?.Authority.IsActive == true && !creationHeld && !saveHeld && submission is null;
    public bool CanSubmit => CanReview && reviewed is not null && access.ControlRevision == reviewControl;
    public bool CanInspectAttempt => !closed && access.CanInspect && !busy && creation is not null;

    private void RetireReview()
    {
        Interlocked.Increment(ref epoch);
        reviewed = null;
        saveReview = null;
        saveBody = null;
    }

    public void Review()
    {
        RetireReview();
        if (CanReview)
        {
            reviewed = body;
            reviewControl = access.ControlRevision;
            status = "Review the exact frozen original body and accepted target below. Submit is separate from target creation; credential screening occurs before retention.";
        }
        Notify();
    }

    public void Cancel()
    {
        RetireReview();
        status = "Review/operation cancelled. Memory draft retained; any already created empty session remains. Inspect the exact attempt, never retry an unconfirmed write.";
        Notify();
    }

    public Task Create()
    {
        if (!CanCreate) { return Task.CompletedTask; }
        creation = new();
        targetControl = access.ControlRevision;
        return Run(async live =>
        {
            var accepted = await workspace.CreateAsync(creation, live, lifetime.Token);
            creation.RequireExactTarget(accepted);
            if (!live()) { throw new OperationCanceledException("Fresh-target admission changed after creation; inspect the disclosed empty session."); }
            target = accepted;
            creationLinks = [new(HostActivity.RequireCurrent().Activity!.Context)];
            status = "Exact empty target created and acknowledged. Review, then separately Submit. Cancelling does not mark Done or delete this session.";
        }, true, creating: true);
    }

    public Task Submit()
    {
        if (!CanSubmit) { return Task.CompletedTask; }
        var frozen = reviewed!;
        var exact = target!;
        RetireReview();
        return Run(async live =>
        {
            var receipt = await conversation.SubmitAsync(exact, frozen, live, lifetime.Token, links: creationLinks);
            submission = receipt.Message.Request;
            if (!live()) { throw new OperationCanceledException("Submission presentation changed after retention; inspect the exact request."); }
            submitted = true;
            assistantResponse = receipt.Message.AssistantResponse;
            body = string.Empty;
            baseline = string.Empty;
            status = "Exact USER message retained; HOST " + receipt.Message.Outcome + " / " + receipt.Message.Reason
                + " | task " + receipt.Task.State
                + (assistantResponse is null ? ". No assistant answer retained." : ". Genuine ASSISTANT answer retained separately.")
                + " No fallback or automatic retry.";
        }, true);
    }

    public Task Inspect() => Run(async live =>
    {
        var exact = creation!.Request;
        var observed = await workspace.ReadExactTaskAsync(submission ?? exact, lifetime.Token);
        var entry = await workspace.ReadExactMetadataAsync(exact.SessionId, lifetime.Token);
        if (!live()) { throw new OperationCanceledException("Exact-attempt inspection was revoked."); }
        if (entry.Authority.SessionId != exact.SessionId || observed?.Task.Request != (submission ?? exact))
        { throw new InvalidDataException("Exact attempt inspection returned conflicting authority."); }
        creationHeld |= target != entry;
        RetireReview();
        target = entry;
        status = "Observed exact retained target and task " + observed.Task.State
            + ". Observation grants no submission authority and changes no activity. Unconfirmed writes remain held; no retry or rollback.";
    }, CanInspectAttempt, inspecting: true);

    private async Task Run(Func<Func<bool>, Task> operation, bool allowed, bool creating = false, bool inspecting = false, bool saving = false)
    {
        if (!allowed) { return; }
        var admittedEpoch = Volatile.Read(ref epoch);
        var revision = access.ControlRevision;
        bool Live() => !closed && access.CanInspect && (inspecting || access.CanControl) && access.ControlRevision == revision
            && Volatile.Read(ref epoch) == admittedEpoch;
        busy = true;
        var origin = HostActivity.Current?.Request.Origin
            ?? (HostActivity.HasScope ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), creation!.Request.SessionId,
            new(Guid.NewGuid()), origin), HostActivityLayer.Application, HostOperation.Request, creationLinks);
        Notify();
        try
        {
            await operation(Live);
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            creationHeld |= creating || inspecting;
            saveHeld |= saving && exception is not ConversationCredentialException;
            if (exception is ConversationSubmissionUnconfirmedException uncertain) { submission = uncertain.Request; }
            if (!closed && access.CanInspect)
            {
                RetireReview();
                status = "Unconfirmed: " + exception.GetType().Name
                    + ". Memory draft retained. Inspect exact creation/request/task receipts; no rollback, second target or automatic retry.";
            }
            else { ClearPrivate(); }
            Failure(logger, exception.GetType().Name);
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        }
        finally
        {
            busy = false;
            Notify();
            if (closed) { lifetime.Dispose(); }
        }
    }

    private void Notify()
    {
        foreach (var name in new[] { nameof(Body), nameof(ReviewedBody), nameof(Status), nameof(Creation), nameof(Submission),
            nameof(Target), nameof(AssistantResponse), nameof(Dirty), nameof(Busy), nameof(CanEdit), nameof(CanCreate), nameof(CanReview), nameof(CanSubmit), nameof(CanInspectAttempt),
            nameof(CanReviewSave), nameof(CanSave), nameof(SavedRevision), nameof(SavedBodyReview), nameof(ProposedSavedBody),
            nameof(BaselineBody), nameof(CanCloseSaved), nameof(DraftMeasurement) })
        { OnPropertyChanged(name); }
    }

    private void ClearPrivate()
    {
        RetireReview();
        body = string.Empty;
        baseline = string.Empty;
        savedDraft = null;
        target = null;
        assistantResponse = null;
        status = "Private presentation revoked. No implicit Save, Submit, Done or delete.";
    }

    public void Dispose()
    {
        if (closed) { return; }
        closed = true;
        ClearPrivate();
        lifetime.Cancel();
        Notify();
        if (!busy) { lifetime.Dispose(); }
    }
}
