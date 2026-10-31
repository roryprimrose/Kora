using Kora.Application.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private DetailPresentationConfigurationService? detailPresentation;
    private sealed record TerminalDetailPublication(HostRequest Request, HostRevision Generation,
        long Sequence, long Revision, long? OfferEpoch, bool Dismissed = false);
    private readonly TerminalDetailPublication?[] terminalDetailPublications = new TerminalDetailPublication?[ConversationText.MaximumSessionMessages];
    private TerminalDetailPublication? completedDetailPublication;

    private bool PublishCompletedConversationMessage(ConversationMessage message)
    {
        if (message.Sequence is <= 0 or > ConversationText.MaximumSessionMessages)
        { throw new InvalidDataException("Conversation publication exceeds the exact session message bound."); }
        var index = checked((int)message.Sequence - 1);
        if (terminalDetailPublications[index] is { } published)
        {
            if (published.Request != message.Request || published.Generation != message.Generation
                || message.Outcome is not null && published.Revision != message.Revision)
            { throw new InvalidDataException("A published terminal conversation identity changed."); }
            return false;
        }
        TerminalDetailPublication? publication = null;
        if (message.Outcome is not null)
        {
            publication = new(message.Request, message.Generation, message.Sequence, message.Revision,
                detailPresentation is null ? 0 : detailPresentation.CaptureOfferEpoch());
            terminalDetailPublications[index] = publication;
        }
        conversationPage = null;
        completedConversationMessage = message;
        completedDetailPublication = publication;
        return true;
    }

    internal void BindDetailPresentation(DetailPresentationConfigurationService service)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        if (detailPresentation is not null) { detailPresentation.Changed -= OnDetailPresentationChanged; }
        for (var index = 0; index < terminalDetailPublications.Length; index++)
        {
            if (terminalDetailPublications[index] is { } publication)
            { terminalDetailPublications[index] = publication with { Dismissed = true }; }
        }
        if (completedDetailPublication is { } completed)
        { completedDetailPublication = terminalDetailPublications[checked((int)completed.Sequence - 1)]; }
        detailPresentation = service;
        service.Changed += OnDetailPresentationChanged;
        NotifyConversation();
    }

    private void OnDetailPresentationChanged(object? sender, EventArgs args)
    {
        conversationDispatcher.Post(() =>
        {
            if (closed || !ReferenceEquals(sender, detailPresentation)) { return; }
            if (detailPresentation?.IsOfferCurrent(completedDetailPublication?.OfferEpoch) == false)
            { RetireSessionDetailsOffer(); }
            NotifyConversation();
        });
    }

    private void RetireSessionDetailsOffer()
    {
        if (completedDetailPublication is { } publication)
        {
            completedDetailPublication = publication with { Dismissed = true };
            terminalDetailPublications[checked((int)publication.Sequence - 1)] = completedDetailPublication;
        }
    }

    private void ClearTerminalDetailPublications()
    {
        Array.Clear(terminalDetailPublications);
        completedDetailPublication = null;
    }
}
