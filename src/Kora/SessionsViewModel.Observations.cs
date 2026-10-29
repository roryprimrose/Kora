namespace Kora;

internal sealed partial class SessionsViewModel
{
    public IReadOnlyList<ObservationDetailChoice> ObservationChoices => !CanOpenConversationDetails ? []
        : ConversationMessages.SelectMany(message => message.Observations?.Items.Select((item, index) =>
            new ObservationDetailChoice(message, item, index + 1)) ?? []).ToArray();

    public Task OpenObservationAsync(ObservationDetailChoice choice)
    {
        if (!ConversationMessages.Concat(ConversationSearchMessages).Any(message => ReferenceEquals(message, choice.Message))
            || choice.Message.Observations?.Items.Contains(choice.Observation) != true)
        { throw new InvalidOperationException("The exact observation choice is no longer on the presented conversation page."); }
        return OpenConversationDetailAsync(choice.Message, observationId: choice.Observation.Id);
    }

    internal async Task<bool> OpenNamedObservationAsync(string name, Func<bool> admitted)
    {
        if (!admitted()) { throw new OperationCanceledException("The foreground observation choices were retired."); }
        var choices = ObservationChoices.Where(choice =>
            Kora.Core.Commands.WorkspaceSurfaceCommand.MatchesObservationName(name, choice.Name)
            || Kora.Core.Commands.WorkspaceSurfaceCommand.MatchesObservationName(name, choice.Observation.CapabilityId)).ToArray();
        if (choices.Length != 1)
        {
            conversationStatus = choices.Length == 0
                ? "No exact observed result on this page. Read the conversation and name a presented result; nothing was queried again."
                : "Several observed results match. Choose one named native link or say open observed result followed by its full message/result name.";
            NotifyConversation();
            return false;
        }
        await OpenConversationDetailAsync(choices[0].Message, observationId: choices[0].Observation.Id, detailAdmission: admitted);
        return true;
    }
}
