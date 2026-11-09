using Kora.Core.Presentation;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private bool hasDocumentTabs;
    internal void SetDocumentTabAvailability(bool available)
    {
        if (hasDocumentTabs == available) { return; }
        hasDocumentTabs = available;
        OnPropertyChanged(nameof(CanShowDetails));
        OnPropertyChanged(nameof(DetailsAvailability));
    }

    internal Task ReopenDocumentTabAsync(SessionDocumentTab tab, Func<bool> admitted)
    {
        if (!admitted() || !CanPresentDetails || SelectedSessionRecord?.Authority.SessionId != tab.Session)
        { throw new InvalidOperationException("The original document tab session or presentation is no longer admitted."); }
        if (tab is SessionEvidenceTab evidenceTab)
        {
            var snapshot = EvidenceDetails.FirstOrDefault(detail => detail.Id == evidenceTab.Reference.ItemId.Value)
                ?? throw new InvalidOperationException("The original bounded evidence snapshot is no longer available. Read fresh evidence explicitly.");
            return OpenEvidenceDetailsAsync(snapshot, admitted);
        }
        if (tab is not SessionContentTab contentTab)
        { throw new InvalidOperationException("The original document tab source is unavailable."); }
        if (contentTab.Origin == DetailContentOrigin.SessionHistory)
        {
            var record = HistoryRecords.FirstOrDefault(record => record.Id == contentTab.Reference.ItemId.Value)
                ?? throw new InvalidOperationException("The original activity receipt is not on this admitted page. Read its exact history explicitly.");
            return OpenHistoryDetailAsync(admitted, contentTab.Reference, record);
        }
        var message = ObservedConversationMessages.Concat(ConversationSearchMessages)
            .FirstOrDefault(message => message.Request.RequestId.Value == contentTab.RequestId);
        if (message is null || contentTab.Origin is not (DetailContentOrigin.RetainedConversation or DetailContentOrigin.RetainedAssistant
            or DetailContentOrigin.RetainedLocalObservation or DetailContentOrigin.RetainedAnswerContext))
        { throw new InvalidOperationException("The original document tab source is not on this admitted page. Explicitly read and open its exact source."); }
        return OpenConversationDetailAsync(message, assistant: contentTab.Origin == DetailContentOrigin.RetainedAssistant,
            observationId: contentTab.Origin == DetailContentOrigin.RetainedLocalObservation ? contentTab.Reference.ItemId.Value : null,
            detailAdmission: admitted, submittedContext: contentTab.Origin == DetailContentOrigin.RetainedAnswerContext,
            expectedReference: contentTab.Reference);
    }
}
