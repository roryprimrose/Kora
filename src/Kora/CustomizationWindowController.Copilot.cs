namespace Kora;

internal sealed partial class CustomizationWindowController
{
    private CancellationTokenSource? selectedCopilotCancellation;

    private void StopSelectedCopilot()
    {
        selectedCopilotCancellation?.Cancel();
        window?.CancelReview();
        if (window is { } opened)
        {
            opened.Status.Text = selectedCopilotCancellation is null ? "No selected Copilot operation is currently owned."
                : "Selected Copilot cancellation requested. Resources remain owned until the operation physically joins; no rollback is claimed.";
        }
    }

    private async Task RunSelectedCopilot(Func<bool> current, CancellationToken token)
    {
        if (copilotWorkflow is null || window is not { } opened || source is not { } selected || Dirty())
        { throw new InvalidOperationException("Selected Copilot use requires a saved exact source, the current native selection and an available workflow. Save or reload any draft first."); }
        var body = opened.Request.Text ?? string.Empty;
        var target = opened.CopilotTarget.Text ?? string.Empty;
        var capturedDraft = draftRevision;
        var capturedInput = opened.CopilotInputRevision;
        bool Live() => current() && source == selected && capturedDraft == draftRevision
            && capturedInput == opened.CopilotInputRevision;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        selectedCopilotCancellation = cancellation;
        try
        {
            await using var workflow = copilotWorkflow();
            await new CopilotCustomizationController(workflow).RunAsync(selected, target, body, opened, Live, cancellation.Token);
        }
        finally { selectedCopilotCancellation = null; }
    }
}
