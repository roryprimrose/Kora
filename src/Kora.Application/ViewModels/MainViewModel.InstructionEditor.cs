using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public event Func<string, string?, Task>? OwnedInstructionEditorRequested;
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

    private bool IsInstructionEditorCommand(string input) =>
        Kora.Core.Artifacts.ArtifactCommandRouter.RemoveActivationPrefix(input.Trim(), AssistantName)
            .StartsWith("instructions ", StringComparison.OrdinalIgnoreCase);

    private async Task ExecuteInstructionEditorCommand(string input)
    {
        var normalized = Kora.Core.Artifacts.ArtifactCommandRouter.RemoveActivationPrefix(input.Trim(), AssistantName);
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string? operation = words[1].ToLowerInvariant() switch
        {
            "editor" or "list" => "List", "create" => "Create", "edit" or "inspect" => "Inspect",
            "save" => "Save", "reload" or "discard" => "Reload", "delete" => "Delete", "activate" => "Review use",
            "cancel" => "Cancel",
            _ => null,
        };
        if (operation is null)
        { ReportHostInteractionFailure("Use instructions editor/list/create/edit/inspect/save/reload/delete/activate/cancel. Body text and paths are not editor commands."); return; }
        if (operation is "Create" or "Inspect" ? words.Length != 3 : words.Length != 2)
        { ReportHostInteractionFailure("Use an exact bounded instruction ID; editor commands do not accept body text or paths."); return; }
        if (OwnedInstructionEditorRequested is { } handler)
        { await handler(operation, words.Length == 3 ? words[2] : null); }
        else { ReportHostInteractionFailure("The native owned instruction editor is unavailable."); }
    }
}
