using Kora.Core.Conversation;
using Kora.Core.Diagnostics;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    public Task ReadRequestEvidenceAsync(ConversationMessage message, Func<bool> nativeAdmission,
        SessionEvidenceDetail? previous = null) => RunConversationAsync(async (target, live) =>
    {
        var epoch = selectionEpoch;
        var control = access.ControlRevision;
        var searchEpoch = conversationSearchEpoch;
        var searchMatch = ConversationSearchMessages.Any(record => ReferenceEquals(record, message));
        var originalAdmission = previous?.RequestAdmission;
        bool Current() => live() && nativeAdmission() && (originalAdmission?.Invoke() ?? true)
            && (!searchMatch || conversationSearchEpoch == searchEpoch)
            && ObservedConversationMessages.Concat(ConversationSearchMessages).Any(record => ReferenceEquals(record, message));
        if (!Current() || message.Request.SessionId != target.Authority.SessionId)
        { throw new InvalidOperationException("Select an actual current retained message before reading its request audit."); }
        if (previous is not null && (!ReferenceEquals(previous.RequestMessage, message)
            || !EvidenceDetailCurrent(previous, target, epoch, control) || previous.Page.Cursor is null))
        { throw new InvalidOperationException("This exact request-audit snapshot cannot continue. Read its message audit explicitly again."); }

        await conversation!.ReadDetailAsync(target, message, Current, lifetime.Token);
        var page = await evidence.QueryAsync(new()
        {
            Source = EvidenceSource.AuthorityAudit,
            SessionId = message.Request.SessionId,
            RequestId = message.Request.RequestId,
            TaskId = message.Request.TaskId,
        }, previous?.Page.Cursor, lifetime.Token);
        await conversation.ReadDetailAsync(target, message, Current, lifetime.Token);
        if (!Current()) { throw new OperationCanceledException("The original message request-audit selection was retired."); }

        var snapshot = new SessionEvidenceDetail(EvidenceSource.AuthorityAudit, page, message)
        { RequestAdmission = originalAdmission ?? Current };
        if (evidenceDetails.Count == 8) { evidenceDetails.RemoveAt(0); }
        evidenceDetails.Add(snapshot);
        conversationStatus = PresentEvidencePage(snapshot, target, epoch, control);
    }, sourceRead: true, searchDetailEpoch:
        ConversationSearchMessages.Any(record => ReferenceEquals(record, message)) ? conversationSearchEpoch : null);
}
