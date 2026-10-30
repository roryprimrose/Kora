using Avalonia.Controls;
using Avalonia.Threading;

using Kora.Core.Artifacts;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Interaction;

namespace Kora;

internal sealed partial class QuestionWindowController
{
    internal Func<bool>? CaptureInstructionSaveVoiceEligibility() => main.CaptureInstructionSaveVoiceEligibility();

    internal async Task<bool> ConfirmInstructionSaveAsync(Window owner, string name, OwnedInstructionSnapshot expected,
        ArtifactFileDocument proposed, DateTimeOffset expiresAt, Func<bool> admitted, CancellationToken token)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        { return await Dispatcher.UIThread.InvokeAsync(() => ConfirmInstructionSaveAsync(owner, name, expected, proposed, expiresAt, admitted, token)); }
        token.ThrowIfCancellationRequested();
        if (disposed || IsOwnerInUse || !admitted() || !owner.IsVisible || main.IsProtectedCall)
        { throw new InvalidOperationException("Exact save question unavailable or occupied. Use native Save after recovery; no answer target was followed."); }
        expected.Document.RequireOwnedCopySource();
        proposed.RequireOwnedCopySource();
        OwnedInstructionNames.WriteName(name);
        if (expected.Document.Definition.Kind != ArtifactKind.Instruction || proposed.Definition.Kind != ArtifactKind.Instruction
            || !string.Equals(expected.Name, name, StringComparison.Ordinal)
            || !string.Equals(expected.CanonicalFileName, name + ".instructions.md", StringComparison.Ordinal)
            || !string.Equals(proposed.Definition.OwnedInstructionName, name, StringComparison.Ordinal)
            || !string.Equals(expected.Document.Definition.Name, name, StringComparison.Ordinal)
            || !string.Equals(proposed.Definition.Name, name, StringComparison.Ordinal)
            || LocalSourceRiskPolicy.Evaluate([expected.Document.Text, proposed.Text], token) != LocalSourceRiskOutcome.AdmittedPrivateLocalOnly)
        { throw new InvalidDataException("Spoken Save requires one unambiguous canonical native owned instruction without detected source risk. Use the complete native review; nothing was saved."); }
        var request = HostActivity.RequireCurrent().Request;
        var spec = new QuestionSpec(
            $"Save the completely display-reviewed replacement of Kora-owned instruction {name} to the same Kora roaming Instructions source, {name}.instructions.md. "
            + $"Replace the inspected version with the complete reviewed {proposed.ByteCount}-byte version, "
            + (proposed.HasBom ? "with UTF-8 BOM. " : "without UTF-8 BOM. ")
            + $"Changed fields: {ArtifactSourceComparison.ChangedFields(expected.Document, proposed)}. "
            + "This is an action and version description, not a reading of the source. It covers ALL displayed metadata, body and attribution. "
            + "Confirm only after reviewing the complete original and replacement, not this summary alone. "
            + "The final disk comparison is optimistic; later external edits may be overwritten. Save/readback leaves the new revision disabled. "
            + $"Say Save instruction {name}, or Cancel saving instruction {name}. No generic yes, number, grant, activation or other file is authorized.",
            QuestionKind.SingleChoice, [new("save", $"Save instruction {name}"), new("cancel", $"Cancel saving instruction {name}")],
            purpose: "owned-instruction-save", sourceId: "host-owned-instruction-editor");
        main.RetireCurrentResponseSpeech();
        var visible = 1;
        void VisibilityChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == Window.IsVisibleProperty)
            { Interlocked.Exchange(ref visible, owner.IsVisible ? 1 : 0); }
        }
        owner.PropertyChanged += VisibilityChanged;
        running = true;
        try
        {
            return await host.AskInstructionSaveAsync(request, spec, expiresAt,
                () => admitted() && Volatile.Read(ref visible) == 1 && !main.IsProtectedCall, model =>
                {
                    if (!admitted() || !owner.IsVisible) { throw new OperationCanceledException("Complete source review withdrawn.", token); }
                    OpenExact(owner, model);
                    if (window is null || !ReferenceEquals(state, model))
                    { throw new InvalidOperationException("The exact save question could not be displayed."); }
                    return Task.CompletedTask;
                }, token);
        }
        finally
        {
            owner.PropertyChanged -= VisibilityChanged;
            window?.Close();
            state = null;
            running = false;
        }
    }
}
