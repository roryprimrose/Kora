using Kora.Application.Definitions;
using Kora.Core.Artifacts;
using Kora.Core.Auditing;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public event Func<PromptParameterDraft, Task>? PromptPreparationRequested;
    public event EventHandler? PromptPreparationSuperseded;
    private long promptInputRevision;
    private Func<Task>? promptPreparationShutdown;
    private IReadOnlyList<BundledPromptSource> bundledPromptSources = [];

    public void BindBundledPromptSources(IReadOnlyList<BundledPromptSource> sources)
    {
        if (sources.Select(source => source.Definition.Id).Distinct(StringComparer.Ordinal).Count() != sources.Count)
        { throw new InvalidDataException("Bundled prompt registrations must have unique identities."); }
        bundledPromptSources = sources.ToArray();
        RetirePromptPreparationForNewInput();
    }

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
        var registered = bundledPromptSources.SingleOrDefault(source => source.Matches(invocation.Artifact));
        if (invocation.Artifact.Source is "bundled" && registered is null)
        { ReportHostInteractionFailure("The bundled prompt has no exact immutable source registration."); return; }
        var draft = new PromptParameterDraft(invocation.Artifact, artifactCatalogue, eligible, registered);
        await handler(draft);
    }

    public async Task<bool> IsPromptSourceCurrent(ArtifactDefinition source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (source.Source is "bundled")
        {
            return !artifactCatalogue.IsReloadUnavailable && source.IsUseEnabled
                && artifactCatalogue.Candidates.Any(candidate => ReferenceEquals(candidate, source))
                && bundledPromptSources.Any(registered => registered.Matches(source));
        }
        var current = ownedInstructionFreshness;
        return current is not null && await current(source, token) && !token.IsCancellationRequested;
    }

}
