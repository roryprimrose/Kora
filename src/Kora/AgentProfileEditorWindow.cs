using Avalonia.Controls;

using Kora.Application.Agents;

namespace Kora;

internal sealed class AgentProfileEditorWindow : Window, IDisposable
{
    internal AgentProfileEditorView View { get; }
    internal TextBox NameInput => View.NameInput;
    internal TextBox Source => View.Source;
    internal TextBox DescriptionRequest => View.DescriptionRequest;
    internal ComboBox Inventory => View.Inventory;
    internal TextBox Review => View.Review;
    internal TextBlock Status => View.Status;
    internal Button Confirm => View.Confirm;
    internal AgentProfileEditorWindow(Action<string> run)
    {
        Title = "Kora-owned agent profile";
        Width = 900; Height = 900;
        View = new(this, run, static () => true);
        Content = View;
        View.CloseRequested += (_, _) => Close();
        Closed += (_, _) => View.Dispose();
    }
    internal void ShowReview(AgentProfileSaveReview review) => View.ShowReview(review);
    internal Task<int> Choose(string title, string message, string[] choices, CancellationToken token) =>
        View.Choose(title, message, choices, token);
    internal void RetireReview() => View.RetireReview();
    internal void ClearPrivateText() => View.ClearPrivateText();
    public void Dispose() { View.Dispose(); Close(); }
}
