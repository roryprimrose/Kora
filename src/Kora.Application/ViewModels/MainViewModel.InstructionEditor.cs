using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public event Func<string, string?, Task>? OwnedInstructionEditorRequested;
    public event Func<string, string?, Task>? OwnedPromptEditorRequested;
    private Func<Task<bool>>? instructionEditorShutdown;
    private Func<Kora.Core.Artifacts.ArtifactDefinition, CancellationToken, Task<bool>>? ownedInstructionFreshness;
    private Func<Kora.Core.Artifacts.ArtifactDefinition, CancellationToken, Task<bool>>? fileArtifactFreshness;
    private Func<bool>? instructionRefinementInput;

    public void BindInstructionEditorShutdown(Func<Task<bool>>? shutdown) => instructionEditorShutdown = shutdown;
    public void BindOwnedInstructionFreshness(Func<Kora.Core.Artifacts.ArtifactDefinition, Task<bool>>? validator) =>
        ownedInstructionFreshness = validator is null ? null : (candidate, _) => validator(candidate);
    public void BindOwnedInstructionFreshnessWithCancellation(
        Func<Kora.Core.Artifacts.ArtifactDefinition, CancellationToken, Task<bool>>? validator) =>
        ownedInstructionFreshness = validator;
    public void BindFileArtifactFreshness(
        Func<Kora.Core.Artifacts.ArtifactDefinition, CancellationToken, Task<bool>>? validator) =>
        fileArtifactFreshness = validator;

    private async Task<bool> IsArtifactSourceCurrent(Kora.Core.Artifacts.ArtifactDefinition candidate, CancellationToken token)
    {
        var validator = candidate.OwnedInstructionName is not null ? ownedInstructionFreshness
            : candidate.UseControlId is not null ? fileArtifactFreshness : null;
        if (candidate.OwnedInstructionName is null && candidate.UseControlId is null) { return true; }
        return validator is not null && await validator(candidate, token) && !token.IsCancellationRequested;
    }

    private async Task<bool> IsFileArtifactSourceCurrent(Kora.Core.Dependencies.LocalModelArtifact artifact, CancellationToken token)
    {
        var candidate = artifactCatalogue.Candidates.SingleOrDefault(item =>
            string.Equals(item.Id, artifact.Id, StringComparison.Ordinal)
            && string.Equals(item.Digest, artifact.Digest, StringComparison.Ordinal));
        if (candidate is null) { return false; }
        return candidate.OwnedInstructionName is not null || candidate.UseControlId is null
            || await IsArtifactSourceCurrent(candidate, token);
    }

    public Func<bool> CaptureInstructionEditorEligibility()
    {
        var input = CaptureManualCallInput(RequestOrigin.LocalUi, CallPolicyRevision);
        var revision = CallPolicyRevision;
        return () => input.Eligible() && CallPolicyRevision == revision && !IsProtectedCall
            && CanRevealPrivatePresentation;
    }

    public Func<bool> CaptureDefinitionObservationEligibility() => CapturePromptSelection();

    public Func<bool> CapturePromptAuthoringEligibility()
    {
        var input = CaptureDefinitionObservationEligibility();
        var channel = CaptureInstructionRefinementEligibility();
        var mode = providerModeConfiguration?.Get().Revision;
        var profile = providerProfileConfiguration?.Get();
        return () => input() && channel() && providerModeConfiguration?.Get().Revision == mode
            && providerProfileConfiguration?.Get() == profile;
    }

    public Func<bool> CaptureInstructionRefinementEligibility()
    {
        var input = CaptureManualCallInput(Kora.Core.Diagnostics.HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi, CallPolicyRevision);
        var revision = assistantNameConfiguration.Get().Revision;
        var current = instructionRefinementInput ?? CaptureSkillReviewEligibility(input);
        return () => current() && CanRevealPrivatePresentation
            && assistantNameConfiguration.Get().Revision == revision;
    }

    private bool IsInstructionEditorCommand(string input)
    {
        var normalized = Kora.Core.Artifacts.ArtifactCommandRouter.RemoveActivationPrefix(input.Trim(), AssistantName);
        return normalized.StartsWith("instructions ", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("prompts ", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ExecuteInstructionEditorCommand(string input, ManualCallInput? manualInput, Func<bool>? reviewEligibility)
    {
        if (Kora.Core.Commands.InstructionUseCommand.Parse(input, AssistantName) is { } request)
        {
            await ExecuteInstructionUseCommand(request, input, manualInput, reviewEligibility);
            return;
        }
        var normalized = Kora.Core.Artifacts.ArtifactCommandRouter.RemoveActivationPrefix(input.Trim(), AssistantName);
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var prompt = string.Equals(words[0], "prompts", StringComparison.OrdinalIgnoreCase);
        string? operation = words[1].ToLowerInvariant() switch
        {
            "editor" or "list" => "List", "create" => "Create", "edit" or "inspect" => "Inspect",
            "save" => "Save", "reload" or "discard" => "Reload", "delete" => "Delete", "activate" => "Review use",
            "cancel" => "Cancel",
            "copy" => "Save as new copy",
            "rename" when !prompt => "Rename",
            "recover-rename" when !prompt => "Recover rename",
            "validate" => "Validate",
            "compare" => "Compare",
            "rebase" => "Rebase",
            "refine" when !prompt => "Refine",
            "disable" or "stop-use" => "Disable reviewed use",
            _ => null,
        };
        if (operation is null)
        { ReportHostInteractionFailure("Use instructions/prompts editor/list/create/edit/inspect/validate/compare/rebase/save/copy/reload/delete/activate/disable/stop-use/cancel. Instructions also support refine, rename <new-id> and recover-rename. Body text and paths are not editor commands."); return; }
        if (operation is "Create" or "Inspect" or "Save as new copy" or "Rename" ? words.Length != 3 : words.Length != 2)
        { ReportHostInteractionFailure("Use an exact bounded owned ID; editor commands do not accept body text or paths."); return; }
        if (operation is "Refine" or "Rename" or "Recover rename" && Kora.Core.Diagnostics.HostActivity.RequireCurrent().Request.Origin == RequestOrigin.ActivatedVoice
            && !HasCurrentNameActivation(input))
        { ReportHostInteractionFailure("Use the current assistant-name prefix for this open instruction operation. No model disclosure or editor change occurred."); return; }
        if ((prompt ? OwnedPromptEditorRequested : OwnedInstructionEditorRequested) is { } handler)
        {
            if (operation is "Refine" or "Rename" or "Recover rename")
            {
                instructionRefinementInput = reviewEligibility ?? CaptureSkillReviewEligibility(manualInput
                    ?? CaptureManualCallInput(Kora.Core.Diagnostics.HostActivity.RequireCurrent().Request.Origin, CallPolicyRevision));
            }
            try { await handler(operation, words.Length == 3 ? words[2] : null); }
            finally { instructionRefinementInput = null; }
        }
        else { ReportHostInteractionFailure("The native owned definition editor is unavailable."); }
    }
}
