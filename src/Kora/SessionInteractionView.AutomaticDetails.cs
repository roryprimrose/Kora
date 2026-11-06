using Avalonia.Input;
using Avalonia.Interactivity;
using Kora.Application.Presentation;

namespace Kora;

internal sealed partial class SessionInteractionView
{
    private long automaticInputEpoch;
    private long automaticReaderEpoch;
    private bool publishingAutomaticDetails;

    private void ObserveAutomaticDetailsInput()
    {
        AddHandler(KeyDownEvent, (_, _) => automaticInputEpoch++, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, (_, _) => automaticInputEpoch++, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerWheelChangedEvent, (_, _) => automaticReaderEpoch++, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    internal AutomaticDetailLease? CaptureAutomaticDetails(SessionsViewModel model)
    {
        if (!ReferenceEquals(observedModel, model) || !IsConversationPresented || DetailsVisible
            || model.SelectedSessionRecord is not { } target || !model.CanAutomaticallyPresentDetails)
        { return null; }
        var selection = model.AutomaticDetailsSelectionEpoch;
        var reader = automaticReaderEpoch;
        var input = automaticInputEpoch;
        var presentation = ConversationPresentationRevision;
        var composer = model.AutomaticDetailsComposerEpoch;
        var control = model.ConversationReaderControl;
        bool SourceCurrent() => ReferenceEquals(observedModel, model) && model.CanPresentDetails
            && control == model.ConversationReaderControl && model.SelectedSessionRecord == target
            && selection == model.AutomaticDetailsSelectionEpoch;
        return new(target.Authority.SessionId, target.Authority.Generation, selection, reader, input,
            () => SourceCurrent() && IsConversationPresented && model.CanAutomaticallyPresentDetails
                && selection == model.AutomaticDetailsSelectionEpoch && composer == model.AutomaticDetailsComposerEpoch
                && reader == automaticReaderEpoch && input == automaticInputEpoch
                && presentation == ConversationPresentationRevision, SourceCurrent);
    }

    internal bool ShowAutomaticDetails(SessionDetailView view, AutomaticDetailIntent intent)
    {
        if (!intent.IsCurrent() || DetailsVisible) { return false; }
        publishingAutomaticDetails = true;
        try { return ShowDetails(view, intent.Content); }
        finally { publishingAutomaticDetails = false; }
    }

    internal void RemoveAutomaticDetails(SessionDetailView view)
    {
        publishingAutomaticDetails = true;
        try { RemoveDetails(view); }
        finally { publishingAutomaticDetails = false; }
    }
}
