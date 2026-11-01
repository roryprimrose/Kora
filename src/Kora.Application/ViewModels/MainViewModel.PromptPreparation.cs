using Kora.Application.Definitions;
using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public event Func<PromptParameterDraft, Task>? PromptPreparationRequested;
    public event EventHandler? PromptPreparationSuperseded;
    private long promptInputRevision;
    private Func<Task>? promptPreparationShutdown;

    public void BindPromptPreparationShutdown(Func<Task>? shutdown) => promptPreparationShutdown = shutdown;
    private Task ClosePromptPreparation() => promptPreparationShutdown?.Invoke() ?? Task.CompletedTask;

    private void RetirePromptPreparationForNewInput()
    {
        Interlocked.Increment(ref promptInputRevision);
        PromptPreparationSuperseded?.Invoke(this, EventArgs.Empty);
    }

    private Func<bool> CapturePromptSelection()
    {
        var input = CommandText;
        var inputRevision = Interlocked.Read(ref promptInputRevision);
        var current = CaptureInstructionEditorEligibility();
        return () => current() && inputRevision == Interlocked.Read(ref promptInputRevision)
            && string.Equals(CommandText, input, StringComparison.Ordinal);
    }

    private async Task PreparePrompt(ArtifactInvocation invocation, string originalInput,
        SecurityAuditInitiator initiator, Func<bool> eligible)
    {
        if (initiator == SecurityAuditInitiator.VoiceCommand
            && !HasCurrentNameActivation(originalInput))
        {
            ReportHostInteractionFailure("Use the current assistant name to select the prompt, then enter required inputs in the native form.");
            return;
        }
        if (!eligible() || PromptPreparationRequested is not { } handler)
        { ReportHostInteractionFailure("Native prompt preparation is unavailable in the current private host."); return; }
        if (!string.IsNullOrWhiteSpace(invocation.Request))
        { ReportHostInteractionFailure("Required prompt inputs must be entered in the native form, not inline command or voice text."); return; }
        var draft = new PromptParameterDraft(invocation.Artifact, artifactCatalogue, eligible);
        await handler(draft);
    }

    public async Task<bool> IsPromptSourceCurrent(ArtifactDefinition source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var current = ownedInstructionFreshness;
        return current is not null && await current(source, token) && !token.IsCancellationRequested;
    }

    public void ReportPromptUse(ModelTurnResult result) => ShowInformation(
        "Prompt generation unavailable.",
        $"The separately reviewed expanded user request was checked by the model admission boundary: {result.Outcome} / {result.Reason}. "
        + "No generation, installation, authentication fallback, HTTP request or action was started. No production adapter is qualified.");
}
