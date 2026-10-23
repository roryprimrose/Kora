using Kora.Application.Voice;

namespace Kora;

internal sealed partial class PromptParameterController
{
    private void BindAnswerReadDecisionGate() =>
        main.BindRetainedAnswerReadDecisionGate(() => !disposed && !closing && !busy
            && draft is null && window is null);

    private void HoldRetiredPromptAnswerReadInput()
    {
        if (draft is not null && ResponseReadCommand.Parse(main.CommandText, main.AssistantName) is { SelectedAnswer: true })
        { main.RefuseSelectedAnswerReadForRetiredDecisionInput(); }
    }
}
