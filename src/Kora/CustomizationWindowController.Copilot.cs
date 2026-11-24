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
        if (copilotWorkflow is null)
        { throw new InvalidOperationException("Selected Copilot workflow is unavailable. No source was read or request submitted."); }
        if (window is not { } opened || source is not { } selected)
        { throw new InvalidOperationException("Select and load one saved exact source before running it with Copilot. No request was submitted."); }
        if (Dirty())
        { throw new InvalidOperationException("Save or reload the current customization draft before running the selected source with Copilot. No request was submitted."); }
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
