using Avalonia.Controls;

namespace Kora;

internal sealed class OwnedSkillInvocationWindow : Window
{
    internal OwnedSkillInvocationView View { get; }
    internal TextBox Instructions => View.Instructions;
    internal TextBox Request => View.Request;
    internal TextBox Address => View.Address;
    internal ComboBox Targets => View.Targets;
    internal TextBox Confirmation => View.Confirmation;
    internal TextBlock Status => View.Status;
    internal Button Confirm => View.Confirm;
    internal PrivateLiteralInputControls LiteralInputs => View.LiteralInputs;
    internal OwnedSkillInvocationWindow(Action<string> run, bool toolMode = false)
    {
        Title = "Review one use of a saved owned skill";
        Width = 900;
        Height = 850;
        View = new(this, run, static () => true, toolMode);
        Content = View;
        Closed += (_, _) => View.Dispose();
    }
    internal void Clear() => View.Clear();
}
