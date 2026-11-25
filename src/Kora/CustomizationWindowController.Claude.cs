namespace Kora;

internal sealed partial class CustomizationWindowController
{
    private async Task RunSelectedClaude(Func<bool> current, CancellationToken token)
    {
        if (claudeWorkflow is null)
        { throw new InvalidOperationException("Selected Claude workflow is unavailable. No source was read or request submitted."); }
        await RunSelected("Claude", async (selected, target, body, opened, live, cancellation) =>
        {
            await using var workflow = claudeWorkflow();
            await new HostedCustomizationController(workflow).RunAsync(selected, target, body, opened, live, cancellation);
        }, current, token);
    }
}
