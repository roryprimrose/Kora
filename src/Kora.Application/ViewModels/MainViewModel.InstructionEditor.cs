using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public event Func<string, string?, Task>? OwnedInstructionEditorRequested;
    public event Func<string, string?, Task>? OwnedPromptEditorRequested;
    private Func<Task<bool>>? instructionEditorShutdown;
    private Func<Kora.Core.Artifacts.ArtifactDefinition, Task<bool>>? ownedInstructionFreshness;

    public void BindInstructionEditorShutdown(Func<Task<bool>>? shutdown) => instructionEditorShutdown = shutdown;
    public void BindOwnedInstructionFreshness(Func<Kora.Core.Artifacts.ArtifactDefinition, Task<bool>>? validator) =>
        ownedInstructionFreshness = validator;

    public Func<bool> CaptureInstructionEditorEligibility()
    {
        var input = CaptureManualCallInput(RequestOrigin.LocalUi, CallPolicyRevision);
        var revision = CallPolicyRevision;
        return () => input.Eligible() && CallPolicyRevision == revision && !IsProtectedCall
            && CanRevealPrivatePresentation;
    }

    private bool IsInstructionEditorCommand(string input)
    {
        var normalized = Kora.Core.Artifacts.ArtifactCommandRouter.RemoveActivationPrefix(input.Trim(), AssistantName);
        return normalized.StartsWith("instructions ", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("prompts ", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ExecuteInstructionEditorCommand(string input)
    {
        var normalized = Kora.Core.Artifacts.ArtifactCommandRouter.RemoveActivationPrefix(input.Trim(), AssistantName);
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var prompt = string.Equals(words[0], "prompts", StringComparison.OrdinalIgnoreCase);
        string? operation = words[1].ToLowerInvariant() switch
        {
            "editor" or "list" => "List", "create" => "Create", "edit" or "inspect" => "Inspect",
            "save" => "Save", "reload" or "discard" => "Reload", "delete" => "Delete", "activate" => "Review use",
            "cancel" => "Cancel",
            "copy" => "Save as new copy",
            _ => null,
        };
        if (operation is null)
        { ReportHostInteractionFailure("Use instructions/prompts editor/list/create/edit/inspect/save/copy/reload/delete/activate/cancel. Body text and paths are not editor commands."); return; }
        if (operation is "Create" or "Inspect" or "Save as new copy" ? words.Length != 3 : words.Length != 2)
        { ReportHostInteractionFailure("Use an exact bounded owned ID; editor commands do not accept body text or paths."); return; }
        if ((prompt ? OwnedPromptEditorRequested : OwnedInstructionEditorRequested) is { } handler)
        { await handler(operation, words.Length == 3 ? words[2] : null); }
        else { ReportHostInteractionFailure("The native owned definition editor is unavailable."); }
    }
}
