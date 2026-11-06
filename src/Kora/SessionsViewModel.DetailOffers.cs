using Kora.Application.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private DetailPresentationConfigurationService? detailPresentation;
    private sealed record TerminalDetailPublication(HostRequest Request, HostRevision Generation,
        long Sequence, long Revision, long? OfferEpoch, ConversationMessage Message, bool Dismissed = false);
    private readonly TerminalDetailPublication?[] terminalDetailPublications = new TerminalDetailPublication?[ConversationText.MaximumSessionMessages];
    private TerminalDetailPublication? completedDetailPublication;

    private bool TryCaptureDetailPublication(ConversationMessage message)
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
                detailPresentation is null ? 0 : detailPresentation.CaptureOfferEpoch(), message);
            terminalDetailPublications[index] = publication;
        }
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

    internal Task OpenOfferedDetailAsync(Kora.Application.Presentation.DetailsOfferRequest request,
        Kora.Application.Presentation.DetailsOfferService offers, Func<bool> admitted) =>
        RunConversationAsync(async (target, live) =>
        {
            if (target != request.Target || !ObservedConversationMessages.Any(message => ReferenceEquals(message, request.Receipt.Message)))
            { throw new InvalidOperationException("The exact offered answer is no longer presented in its original conversation."); }
            bool Current() => live() && admitted();
            var content = await offers.ReadDetailAsync(request, service, Current, lifetime.Token);
            var continuation = Kora.Core.Diagnostics.HostActivity.CaptureContinuation(
                Kora.Core.Diagnostics.HostActivityLayer.Desktop, Kora.Core.Diagnostics.HostOperation.Storage);
            async Task<Kora.Core.Presentation.AdmittedDetailContent> Resolve(CancellationToken token)
            {
                using var activity = continuation();
                var resolved = await offers.ReadDetailAsync(request, service, Current, token);
                activity.Complete(Kora.Core.Diagnostics.HostOperationOutcome.Completed);
                return resolved;
            }
            conversationStatus = await (openConversationDetail
                ?? throw new InvalidOperationException("The native exact Details pane is unavailable."))(content, Current, Resolve, lifetime.Token);
            if (!Current()) { throw new OperationCanceledException("The exact offered Details publication was withdrawn."); }
            RememberSelectedDetail(DetailPart.Assistant, request.Receipt.Message.Request.RequestId.Value);
        }, sourceRead: true, detailsOffer: request);
}