using Avalonia.Controls;
using Kora.Application.Skills;
using Kora.Core.Skills;

namespace Kora;

internal sealed class ObservationWorkflowWindow : Window, IOwnedSkillSaveReview
{
    internal ObservationWorkflowView View { get; }
    internal TextBox IdInput => View.IdInput;
    internal TextBox VersionInput => View.VersionInput;
    internal TextBox NameInput => View.NameInput;
    internal TextBox DescriptionInput => View.DescriptionInput;
    internal TextBox Definition => View.Definition;
    internal ComboBox Saved => View.Saved;
    internal ComboBox Targets => View.Targets;
    internal TextBox Request => View.Request;
    internal TextBox ReviewText => View.ReviewText;
    internal TextBox Result => View.Result;
    internal TextBlock Status => View.Status;

    internal ObservationWorkflowWindow(Func<string, Task> run, Func<bool> eligible)
    {
        Title = "Kora-owned observation workflow";
        Width = 900;
        Height = 800;
        View = new(this, run, eligible, static () => true);
        Content = View;
        Closed += (_, _) => View.Dispose();
    }

    internal OwnedDeclarativeSkillDocument Document() => View.Document();
    public Task<bool> Review(OwnedDeclarativeSkillDocument document, string destination, CancellationToken token) =>
        View.Review(document, destination, token);
    public Task<bool> ConfirmPublication(OwnedDeclarativeSkillDocument document, string destination, CancellationToken token) =>
        View.ConfirmPublication(document, destination, token);
    internal Task<bool> Confirm(string title, string text, string confirm, Func<bool> current, CancellationToken token) =>
        View.Confirm(title, text, confirm, current, token);
    internal void CompleteApproval(bool approved) => View.CompleteApproval(approved);
    internal void ClearReview() => View.ClearReview();
}
