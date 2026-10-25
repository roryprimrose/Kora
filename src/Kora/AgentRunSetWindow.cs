using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

using Kora.Application.Agents;
using Kora.Core.Storage;

namespace Kora;

internal sealed class AgentRunSetWindow : Window
{
    internal sealed record Target(SessionWorkspaceEntry Entry, string Label)
    {
        public override string ToString() => Label;
    }

    internal sealed class RunPanel
    {
        internal TextBox Source { get; } = new() { PlaceholderText = "Existing user-owned .agent.md path", IsUndoEnabled = false };
        internal TextBox Input { get; } = new() { AcceptsReturn = true, Height = 80, IsUndoEnabled = false };
        internal ComboBox Targets { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
        internal TextBox Review { get; } = new() { IsReadOnly = true, AcceptsReturn = true, Height = 210, IsUndoEnabled = false };
        internal TextBox Result { get; } = new() { IsReadOnly = true, AcceptsReturn = true, Height = 120, IsUndoEnabled = false };
        internal TextBlock Status { get; } = new() { Text = "Not reviewed.", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        internal Button Resolve { get; } = new() { Content = "Consent to read declared instructions", IsEnabled = false };
        internal Button Confirm { get; } = new() { Content = "Confirm this exact invocation", IsEnabled = false };
        internal long Revision { get; set; }
        internal Kora.Core.Hosting.RequestOrigin InputOrigin { get; set; } = Kora.Core.Hosting.RequestOrigin.LocalUi;
        internal Guid? Next { get; set; }

        internal void ClearReview()
        {
            Review.Text = Result.Text = string.Empty;
            Resolve.IsEnabled = Confirm.IsEnabled = false;
        }

        internal void Clear()
        {
            Source.Text = Input.Text = string.Empty;
            ClearReview();
            Targets.ItemsSource = null;
            Status.Text = string.Empty;
        }
    }

    internal RunPanel[] Runs { get; } = [new(), new()];
    internal TabControl Selection { get; } = new();

    internal AgentRunSetWindow(Action<AgentRunSetCommand> execute)
    {
        Title = "Independent local agent runs";
        Width = 900;
        Height = 950;
        var tabs = new List<TabItem>();
        for (var index = 0; index < Runs.Length; index++)
        {
            var number = index + 1;
            var run = Runs[index];
            var panel = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
            panel.Children.Add(new TextBlock
            {
                Text = "Choose an existing local tool-free profile, a distinct existing Active conversation and an original task. "
                    + "Reading the profile requires explicit review consent; declared instructions require separate consent. "
                    + "Review is not invocation confirmation. Production concurrent capacity is unavailable: runs remain serial/resource-blocked. "
                    + "No automatic sessions, delegation, tools or hosted transmission.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            });
            panel.Children.Add(run.Source);
            Add("Refresh Active conversations", "Targets");
            Add("Next conversation page", "NextTargets");
            panel.Children.Add(run.Targets);
            panel.Children.Add(new TextBlock { Text = "Original task, retained only in this selected conversation" });
            panel.Children.Add(run.Input);
            Add("Consent to read profile and review", "Review");
            panel.Children.Add(run.Review);
            panel.Children.Add(run.Resolve);
            run.Resolve.Click += (_, _) => execute(new("Resolve", number));
            panel.Children.Add(run.Confirm);
            run.Confirm.Click += (_, _) => execute(new("Invoke", number));
            Add("Status", "Status");
            Add("Stop / revoke this run and join its resources", "Stop");
            panel.Children.Add(run.Status);
            panel.Children.Add(run.Result);
            tabs.Add(new() { Header = $"Run {number}", Content = new ScrollViewer { Content = panel } });
            void Add(string label, string action)
            {
                var button = new Button { Content = label };
                button.Click += (_, _) => execute(new(action, number));
                panel.Children.Add(button);
            }
        }
        Selection.ItemsSource = tabs;
        Selection.SelectedIndex = 0;
        Content = Selection;
    }

    internal void Clear()
    {
        foreach (var run in Runs) { run.Clear(); }
    }
}
