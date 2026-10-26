using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Kora;

internal sealed class NamedSkillInvocationWindow : Window
{
    internal ComboBox Choices { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    internal TextBox Instructions { get; } = ReadOnly("Complete exact selected skill source and instructions", 200);
    internal ComboBox Targets { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    internal TextBox Request { get; } = new() { AcceptsReturn = true, IsUndoEnabled = false, Height = 100 };
    internal TextBox Confirmation { get; } = ReadOnly("Exact named skill invocation preview", 180);
    internal TextBlock Status { get; } = new() { TextWrapping = TextWrapping.Wrap };
    internal Button Confirm { get; } = new() { Content = "Confirm exact invocation once", IsEnabled = false };
    private readonly List<Button> operations = [];

    internal NamedSkillInvocationWindow(string name, Func<string, Task> run)
    {
        Title = "Review instruction-only skill by readable name";
        Width = 950;
        Height = 900;
        AutomationProperties.SetName(Choices, "Numbered exact-name source choices, no default");
        AutomationProperties.SetName(Targets, "Exact existing Active conversation target");
        AutomationProperties.SetName(Request, "Unchanged original user task, not a command");
        AutomationProperties.SetName(Status, "Named skill status and recovery");
        AutomationProperties.SetLiveSetting(Status, AutomationLiveSetting.Polite);
        var panel = new StackPanel { Margin = new(16), Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Requested literal name: " + name, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock
        {
            Text = "Names are untrusted data, not authority. Inspection below explicitly reads all bounded saved owned packages "
                + "and the SKILL.md files in every already registered shared root. No references, new roots or other context are read. "
                + "No model is selected or invoked. Errors invalidate the complete lookup; no source shadows another.",
            TextWrapping = TextWrapping.Wrap,
        });
        Add("Inspect saved and registered skill files locally (read consent)", "Inspect");
        panel.Children.Add(Choices);
        Add("Review exactly the selected source choice", "ReviewChoice");
        panel.Children.Add(Instructions);
        panel.Children.Add(new TextBlock
        {
            Text = "Review all inert instructions. LocalOnly; no tools, scripts, MCP, references, nested agents, memory or hosted transfer. "
                + "Separately enable one exact revision, choose one current Active conversation, enter the literal task, preview and confirm.",
            TextWrapping = TextWrapping.Wrap,
        });
        Add("Enable exact reviewed revision once", "Enable");
        Add("Refresh existing Active conversations", "Targets");
        Add("Next conversation page", "NextTargets");
        panel.Children.Add(Targets);
        panel.Children.Add(new TextBlock { Text = "Original user task (retained unchanged; never parsed as a command)" });
        panel.Children.Add(Request);
        Add("Review exact invocation", "Prepare");
        panel.Children.Add(Confirmation);
        panel.Children.Add(Confirm);
        Confirm.Click += async (_, _) => await run("Invoke");
        var close = new Button { Content = "Stop, join and close named skill use" };
        close.Click += async (_, _) => await run("Close");
        panel.Children.Add(close);
        panel.Children.Add(Status);
        Content = new ScrollViewer { Content = panel };

        void Add(string label, string action)
        {
            var button = new Button { Content = label };
            button.Click += async (_, _) => await run(action);
            operations.Add(button);
            panel.Children.Add(button);
        }
    }

    internal void SetBusy(bool busy)
    {
        foreach (var button in operations) { button.IsEnabled = !busy; }
        Choices.IsEnabled = !busy;
        Targets.IsEnabled = !busy;
        Request.IsEnabled = !busy;
        Confirm.IsEnabled = !busy && !string.IsNullOrEmpty(Confirmation.Text);
    }

    internal void ClearReview()
    {
        Instructions.Text = string.Empty;
        Confirmation.Text = string.Empty;
        Targets.ItemsSource = null;
        Confirm.IsEnabled = false;
    }

    internal void Clear()
    {
        ClearReview();
        Choices.ItemsSource = null;
        Request.Text = string.Empty;
        Status.Text = string.Empty;
    }

    private static TextBox ReadOnly(string name, double height)
    {
        var box = new TextBox
        {
            IsReadOnly = true, AcceptsReturn = true, IsUndoEnabled = false, Height = height,
            TextWrapping = TextWrapping.Wrap, ContextMenu = null, ContextFlyout = null,
        };
        AutomationProperties.SetName(box, name);
        return box;
    }
}
