namespace Kora;

internal sealed partial class PromptParameterController
{
    private void BindAnswerReadDecisionGate() =>
        main.BindRetainedAnswerReadDecisionGate(() => !disposed && !closing && !busy
            && draft is null && window is null);

    private void HoldRetiredPromptAnswerReadInput()
    {
        // The first edit closes the prompt before a complete command is available.
        if (draft is not null || window is not null)
        { main.RefuseSelectedAnswerReadForRetiredDecisionInput(); }
    }
}
