using Avalonia.Controls;

using Kora.Application.Agents;

namespace Kora;

internal sealed class AgentRunSetWindow : Window
{
    internal AgentRunSetView View { get; }
    internal AgentRunSetView.RunPanel[] Runs => View.Runs;
    internal TabControl Selection => View.Selection;
    internal AgentRunSetWindow(Action<AgentRunSetCommand> execute)
    {
        Title = "Independent local agent runs";
        Width = 900;
        Height = 950;
        View = new(this, execute, static () => true);
        Content = View;
        Closed += (_, _) => View.Dispose();
    }
    internal void Clear() => View.Clear();
}
