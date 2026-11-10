using Avalonia.Controls;

namespace Kora;

internal sealed class SessionInstructionScopeWindow : Window
{
    internal SessionInstructionScopeView View { get; }
    internal ComboBox Sources => View.Sources;
    internal Button Refresh => View.Refresh;
    internal Button Review => View.Review;
    internal Button Confirm => View.Confirm;
    internal Button Stop => View.Stop;
    internal Button Status => View.Status;
    internal TextBox Preview => View.Preview;

    internal SessionInstructionScopeWindow()
    {
        Title = "Instruction for this Active session";
        Width = 780; Height = 680; MinWidth = 480; MinHeight = 400;
        View = new(this, static () => true);
        Content = View;
        Closed += (_, _) => View.Dispose();
    }
}
