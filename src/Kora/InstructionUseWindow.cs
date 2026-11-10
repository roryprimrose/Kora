using Avalonia.Controls;

namespace Kora;

internal sealed class InstructionUseWindow : Window
{
    internal InstructionUseView View { get; }
    internal TextBox Review => View.Review;
    internal TextBox Result => View.Result;
    internal Button Confirm => View.Confirm;
    internal Button Cancel => View.Cancel;
    internal TextBlock Status => View.Status;
    internal InstructionUseWindow(Action<string> run)
    {
        Title = "Review one owned instruction request";
        Width = 850;
        Height = 760;
        View = new(this, run, static () => true);
        Content = View;
        Closed += (_, _) => View.Dispose();
    }
    internal void Clear() => View.Clear();
}
