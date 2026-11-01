using Kora.Application.Memory;
using Kora.Core.Memory;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private SessionMemoryCopyReview? sessionMemoryCopy;
    private MemoryContentClass? sessionMemoryCopyClass;
    public bool CanKeepSessionMemory => CanReadConversation && CanInspectMemory && !deviceProfileMemory
        && inspectedMemory is { Review: MemoryReviewState.Admitted, Retention: MemoryRetentionState.Enabled, Candidate: not null };
    public bool CanClassifySessionMemoryCopy => CanReadMemories && sessionMemoryCopy?.CanPropose == true;
    public bool CanProposeSessionMemoryCopy => CanClassifySessionMemoryCopy && sessionMemoryCopyClass is not null;
    public bool CanAcceptSessionMemoryCopy => CanReadMemories && sessionMemoryCopy?.CanAccept == true;
    public bool CanAdmitSessionMemoryCopy => CanReadMemories && sessionMemoryCopy?.CanAdmit == true;
    public string SessionMemoryCopyDescription => MemorySourceCurrent ? sessionMemoryCopy?.Description ?? string.Empty : string.Empty;
    public MemoryContentClass? SessionMemoryCopyClass
    {
        get => sessionMemoryCopyClass;
        set
        {
            if (notifying || !CanClassifySessionMemoryCopy) { return; }
            sessionMemoryCopyClass = value;
            NotifySessionMemoryCopy();
        }
    }

    public Task KeepSessionMemoryAsync() => RunConversationAsync(async (target, live) =>
    {
        CancelSessionMemoryCopy();
        var source = inspectedMemory;
        if (deviceProfileMemory || source is null || selectedMemory?.Id != source.Id.Value
            || !MemorySourceCurrent || memories is null)
        { throw new InvalidOperationException("Select and inspect ONE current reviewed Session memory first."); }
        var epoch = memoryEpoch;
        bool Current() => live() && access.CanControl && !deviceProfileMemory && memoryEpoch == epoch
            && inspectedMemory == source && MemorySourceCurrent;
        sessionMemoryCopy = await memories.InspectSessionCopyAsync(target, source, Current, lifetime.Token);
        conversationStatus = "Inspect the complete source and independent profile lifetime below. "
            + "Choose a fresh classification, then explicitly propose a NEW unadmitted copy.";
    }, requireOriginalInput: true);

    public Task ProposeSessionMemoryCopyAsync() => RunSessionMemoryCopyAsync(MemoryOperation.Propose);
    public Task AcceptSessionMemoryCopyAsync() => RunSessionMemoryCopyAsync(MemoryOperation.Review);
    public Task AdmitSessionMemoryCopyAsync() => RunSessionMemoryCopyAsync(MemoryOperation.Admit);

    private Task RunSessionMemoryCopyAsync(MemoryOperation operation) => RunConversationAsync(async (_, _) =>
    {
        var review = sessionMemoryCopy ?? throw new InvalidOperationException("Inspect the exact scope change first.");
        var result = operation switch
        {
            MemoryOperation.Propose => await memories!.ProposeSessionCopyAsync(review,
                sessionMemoryCopyClass ?? throw new InvalidOperationException("Choose a fresh classification explicitly."), lifetime.Token),
            MemoryOperation.Review => await memories!.ReviewSessionCopyAsync(review, true, lifetime.Token),
            _ => await memories!.AdmitSessionCopyAsync(review, lifetime.Token),
        };
        conversationStatus = result.Outcome != MemoryOutcome.Succeeded
            ? "Scope copy held: " + result.Outcome + " / " + result.Reason
                + ". Inspect profile memories and audit receipts. No rollback or retry is claimed."
            : operation == MemoryOperation.Admit
                ? "NEW Device-profile memory admitted independently. Original Session memory unchanged. "
                    + "Choose Device profile, list and inspect the new record. Later one-shot local use requires its own separate review."
                : operation == MemoryOperation.Propose
                    ? "NEW volatile Device-profile candidate only. Inspect its complete content, fresh classification and scope below; separately accept, then admit."
                    : "Fresh copy review accepted; still unadmitted. Separately admit, or cancel.";
        if (result.Outcome != MemoryOutcome.Succeeded || operation == MemoryOperation.Admit) { CancelSessionMemoryCopy(); }
    }, requireOriginalInput: true);

    internal Func<bool> CaptureSessionMemoryCopyPresentation()
    {
        var epoch = memoryEpoch;
        var selection = selectionEpoch;
        var control = access.ControlRevision;
        return () => MemorySourceCurrent && !deviceProfileMemory && memoryEpoch == epoch
            && selectionEpoch == selection && access.ControlRevision == control;
    }

    internal void ReportSessionMemoryCopyPresentationFailure(Exception exception)
    {
        Failure(logger, exception.GetType().Name);
        if (closed || !access.CanInspect) { return; }
        conversationStatus += " Native scope-copy review/outcome presentation unavailable. "
            + "Read the exact scope-copy status and profile/audit state; no admission, rollback or retry is inferred.";
        NotifyConversation();
    }

    public void CancelSessionMemoryCopy(bool reportCancellation = false)
    {
        sessionMemoryCopy?.Dispose();
        sessionMemoryCopy = null;
        sessionMemoryCopyClass = null;
        NotifySessionMemoryCopy();
        if (reportCancellation && !closed && access.CanInspect)
        {
            conversationStatus = "Scope-copy review cancelled. No admission, rollback or retry is inferred. "
                + "Inspect profile memories and audit receipts if a save was in progress.";
            NotifyConversation();
        }
    }

    private void NotifySessionMemoryCopy()
    {
        if (sessionMemoryCopy is { Current: false })
        {
            sessionMemoryCopy.Dispose();
            sessionMemoryCopy = null;
            sessionMemoryCopyClass = null;
        }
        OnPropertyChanged(nameof(CanKeepSessionMemory));
        OnPropertyChanged(nameof(CanClassifySessionMemoryCopy));
        OnPropertyChanged(nameof(CanProposeSessionMemoryCopy));
        OnPropertyChanged(nameof(CanAcceptSessionMemoryCopy));
        OnPropertyChanged(nameof(CanAdmitSessionMemoryCopy));
        OnPropertyChanged(nameof(SessionMemoryCopyDescription));
        OnPropertyChanged(nameof(SessionMemoryCopyClass));
    }
}
