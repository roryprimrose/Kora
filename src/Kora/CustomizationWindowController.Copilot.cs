namespace Kora;

internal sealed partial class CustomizationWindowController
{
    private CancellationTokenSource? selectedCancellation;
    private string? selectedProvider;

    private void StopSelected()
    {
        selectedCancellation?.Cancel();
        window?.CancelReview();
        if (window is { } opened)
        {
            opened.Status.Text = selectedCancellation is null ? "No selected hosted operation is currently owned."
                : "Selected " + selectedProvider + " cancellation requested. Resources remain owned until the operation physically joins; no rollback is claimed.";
        }
    }

    private async Task RunSelectedCopilot(Func<bool> current, CancellationToken token)
    {
        if (copilotWorkflow is null)
        { throw new InvalidOperationException("Selected Copilot workflow is unavailable. No source was read or request submitted."); }
        await RunSelected("Copilot", async (selected, target, body, opened, live, cancellation) =>
        {
            await using var workflow = copilotWorkflow();
            await new CopilotCustomizationController(workflow).RunAsync(selected, target, body, opened, live, cancellation);
        }, current, token);
    }

    private async Task RunSelected(string provider,
        Func<Kora.Core.Customizations.CustomizationSource, string, string, CustomizationView, Func<bool>, CancellationToken, Task> run,
        Func<bool> current, CancellationToken token)
    {
        if (selectedCancellation is not null)
        { throw new InvalidOperationException("Cancel and physically join the owned selected operation before another provider."); }
        if (window is not { } opened || source is not { } selected)
        { throw new InvalidOperationException("Select and load one saved exact source before running it with " + provider + ". No request was submitted."); }
        if (Dirty())
        { throw new InvalidOperationException("Save or reload the current customization draft before running the selected source with " + provider + ". No request was submitted."); }
        var body = opened.Request.Text ?? string.Empty;
        var target = opened.CopilotTarget.Text ?? string.Empty;
        var capturedDraft = draftRevision;
        var capturedInput = opened.CopilotInputRevision;
        bool Live() => current() && source == selected && capturedDraft == draftRevision
            && capturedInput == opened.CopilotInputRevision;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        selectedCancellation = cancellation;
        selectedProvider = provider;
        try
        {
            await run(selected, target, body, opened, Live, cancellation.Token);
        }
        finally { selectedCancellation = null; selectedProvider = null; }
    }
}
