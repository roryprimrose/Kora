using Avalonia.Controls;

namespace Kora;

internal sealed class NamedSkillInvocationWindow : Window
{
    internal NamedSkillInvocationView View { get; }
    internal ComboBox Choices => View.Choices;
    internal TextBox Instructions => View.Instructions;
    internal ComboBox Targets => View.Targets;
    internal TextBox Request => View.Request;
    internal TextBox Confirmation => View.Confirmation;
    internal TextBlock Status => View.Status;
    internal Button Confirm => View.Confirm;
    internal PrivateLiteralInputControls LiteralInputs => View.LiteralInputs;
    internal NamedSkillInvocationWindow(string name, Func<string, Task> run)
    {
        Title = "Review instruction-only skill by readable name";
        Width = 950;
        Height = 900;
        View = new(this, name, run, static () => true);
        Content = View;
        Closed += (_, _) => View.Dispose();
    }
    internal void SetBusy(bool busy) => View.SetBusy(busy);
    internal void ClearReview() => View.ClearReview();
    internal void Clear() => View.Clear();
}
