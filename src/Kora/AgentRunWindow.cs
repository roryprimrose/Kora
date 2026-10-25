using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Kora;

internal sealed partial class AgentRunWindow : Window
{
    internal TextBox Source { get; } = new() { PlaceholderText = "Full path to an existing user-owned .agent.md file", IsUndoEnabled = false };
    internal TextBox Input { get; } = new() { AcceptsReturn = true, Height = 90, IsUndoEnabled = false };
    internal ComboBox Targets { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    internal TextBox Review { get; } = new() { IsReadOnly = true, AcceptsReturn = true, Height = 260, IsUndoEnabled = false };
    internal TextBox Result { get; } = new() { IsReadOnly = true, AcceptsReturn = true, Height = 150, IsUndoEnabled = false };
    internal Button Confirm { get; } = new() { Content = "Consent to this exact bounded invocation", IsEnabled = false };
    internal Button Resolve { get; } = new() { Content = "Consent to read this exact declared instruction set", IsEnabled = false };
    internal TextBlock Status { get; } = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    internal AgentRunWindow(Action<string> run)
    {
        Title = "One reviewed local bounded agent";
        Width = 850;
        Height = 950;
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = "Select an EXISTING user-owned .agent.md source. Review reads at most 64 KiB and pins the exact native source. "
                + "v2 permits up to four exact enabled Kora-owned instruction IDs after separate resolution consent, then full bundle review. "
                + "No arbitrary files, executable skills, scripts, recursion, delegation, MCP or hosted transmission. v2 is tool-free; v1 observation tools retain their own permissions. "
                + "Review is not invocation consent. The local model may be unavailable.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        panel.Children.Add(Source);
        Add("Refresh existing Active conversations", "Targets");
        Add("Next conversation page", "NextTargets");
        panel.Children.Add(Targets);
        panel.Children.Add(new TextBlock { Text = "Original bounded task (retained in the selected conversation)" });
        panel.Children.Add(Input);
        AddSharedSkillControls(panel, run);
        Add("Read source and review exact invocation", "Review");
        panel.Children.Add(Review);
        panel.Children.Add(Resolve);
        Resolve.Click += (_, _) => run("Resolve");
        panel.Children.Add(Confirm);
        Confirm.Click += (_, _) => run("Invoke");
        Add("Stop / revoke and join owned execution", "Stop");
        panel.Children.Add(Status);
        panel.Children.Add(Result);
        Content = new ScrollViewer { Content = panel };
        void Add(string label, string action)
        {
            var button = new Button { Content = label };
            button.Click += (_, _) => run(action);
            panel.Children.Add(button);
        }
    }
    internal void Clear()
    {
        Source.Text = Input.Text = Review.Text = Result.Text = string.Empty;
        Targets.ItemsSource = null;
        Status.Text = string.Empty;
        Confirm.IsEnabled = false;
        Resolve.IsEnabled = false;
        ClearSharedSkills();
    }
}
