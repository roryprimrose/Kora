using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using Kora.Application.Configuration;
using Kora.Application.Presentation;

namespace Kora;

internal sealed partial class SessionsPresentationOwner
{
    private long offeredReaderRevision;
    private Kora.Core.Conversation.ConversationMessage? revealingDetails;
    private long detailsInputEpoch;
    private void OnDetailsSourceInteraction(object? sender, RoutedEventArgs args)
    {
        var controls = args.Source is Control source
            ? source.GetVisualAncestors().OfType<Control>().Prepend(source).ToArray() : [];
        if (controls.Any(control => control is SessionRequestReviewPanel)) { return; }
        if (requestCommands?.HasDetailsQuestion == true && controls.Any(control =>
            control.Name is "CommandInput" or "PushToTalkButton"
            || control is Button button && ReferenceEquals(button.Command, requestCommands.RunTypedCommand)))
        { return; }
        Interlocked.Increment(ref detailsInputEpoch);
        requestCommands?.RevalidateDetailsOffer();
    }
    internal void BindDetailsOffers(DetailsOfferService offers, DetailPresentationConfigurationService settings)
    {
        model.BindDetailPresentation(settings);
        requestCommands?.BindDetailsOffers(offers, () =>
        {
            var native = CaptureForegroundSourceEligibility();
            var source = model.CaptureDraftPresentationEligibility();
            var epoch = settings.CaptureOfferEpoch();
            var composer = model.ComposerRevision;
            var input = Interlocked.Read(ref detailsInputEpoch);
            offeredReaderRevision = model.DetailSelectionRevision;
            var initiallyEligible = model.LastDetailMessage is null && epoch is not null;
            return () => initiallyEligible && !revoked && native() && source()
                && model.ComposerRevision == composer
                && input == Interlocked.Read(ref detailsInputEpoch)
                && (model.DetailSelectionRevision == offeredReaderRevision
                    || revealingDetails is not null && model.DetailSelectionRevision == offeredReaderRevision + 1
                        && model.LastDetailMessage == revealingDetails && model.LastDetailAssistant)
                && settings.IsOfferCurrent(epoch);
        }, async (request, admitted) =>
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (!admitted() || revoked || model.SelectedSessionRecord != request.Target)
                { return false; }
                var previousReader = offeredReaderRevision;
                revealingDetails = request.Receipt.Message;
                var passive = model.CaptureDraftPresentationEligibility();
                var primary = UniqueForegroundSurface(conversationRequired: true) as ResponseWindow;
                var reference = new Kora.Core.Presentation.DetailContentReference(
                    new(request.Receipt.Message.Execution!.Turn.Value), request.Receipt.Message.Revision);
                void Withdraw() => Dispatcher.UIThread.Post(() => primary?.SessionView.RetireOfferedDetails(reference));
                request.Withdrawn += Withdraw;
                bool ReadAdmission() => request.IsRevealConfirmed ? passive() : admitted();
                try
                {
                    model.DismissDetailsOffer(request.Receipt.Message);
                    await model.OpenOfferedDetailAsync(request, offers, ReadAdmission);
                    if (model.DetailSelectionRevision != previousReader + 1
                        || model.LastDetailMessage != request.Receipt.Message || !model.LastDetailAssistant)
                    { return false; }
                    offeredReaderRevision = model.DetailSelectionRevision;
                    return admitted();
                }
                finally { revealingDetails = null; }
            });
        }, request => model.DismissDetailsOffer(request.Receipt.Message));
    }
}