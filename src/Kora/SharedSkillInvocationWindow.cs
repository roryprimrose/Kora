using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Layout;
using Kora.Controls;

namespace Kora;

internal sealed class SharedSkillInvocationWindow : Window
{
    internal TextBox Instructions { get; } = new() { IsReadOnly = true, AcceptsReturn = true, IsUndoEnabled = false, Height = 220, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    internal TextBox Request { get; } = new() { AcceptsReturn = true, IsUndoEnabled = false, Height = 100 };
    internal TextBox Address { get; } = new() { IsUndoEnabled = false };
    internal TextBox SecondAddress { get; } = new() { IsUndoEnabled = false };
    internal ComboBox Targets { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    internal TextBlock Status { get; } = new NamedTextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    internal TextBox Confirmation { get; } = new() { IsReadOnly = true, AcceptsReturn = true, IsUndoEnabled = false, Height = 150 };
    internal Button Confirm { get; } = new() { Content = "Confirm exact skill, original request and target", IsEnabled = false };
    internal Button ReadReferences { get; } = new() { Content = "Authorize only the exact declared local Markdown reads", IsVisible = false };
    internal Button Stop { get; } = new() { Content = "Stop and close reviewed use" };
    private readonly List<Button> operationButtons = [];

    internal SharedSkillInvocationWindow(Action<string> run, bool toolCapable = false)
    {
        Title = toolCapable ? "Reviewed one-tool shared skill" : "Reviewed instruction-only shared skill";
        Width = 850;
        Height = 850;
        AutomationProperties.SetName(Instructions, "Complete inert shared skill instructions");
        AutomationProperties.SetName(Targets, "Existing Active conversation target");
        AutomationProperties.SetName(Confirmation, "Exact reviewed invocation and finite web scope");
        AutomationProperties.SetName(Status, "Reviewed skill status and recovery");
        AutomationProperties.SetLiveSetting(Status, AutomationLiveSetting.Polite);
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = (toolCapable
                ? "Complete inert untrusted instructions. Local model only; one or two explicit destinations, at most one host WebPageGet call per destination. "
                    + "Source reading, model context consent and per-action permission are separate. No scripts, agents, MCP or hosted disclosure. "
                    + "Select a target, then review the exact tool revision. "
                : "Complete inert untrusted instructions. Local model only; ZERO tools, network or side effects. ")
                + "Enablement is separate, memory-only, expires in two minutes and is single-use. Closing or admission/source changes revoke it.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        panel.Children.Add(Instructions);
        panel.Children.Add(ReadReferences);
        ReadReferences.Click += (_, _) => run("Prepare");
        AddButton("Enable exact reviewed revision", "Enable");
        AddButton("Refresh existing Active conversations", "Targets");
        AddButton("Next conversation page", "NextTargets");
        panel.Children.Add(Targets);
        if (toolCapable)
        {
            panel.Children.Add(new TextBlock { Text = "First explicit public page address (separately authorized per action)" });
            AutomationProperties.SetName(Address, "First explicit public page address");
            panel.Children.Add(Address);
            panel.Children.Add(new TextBlock { Text = "Second explicit public page address (optional; distinct URL for two-source use)" });
            AutomationProperties.SetName(SecondAddress, "Second explicit public page address, optional");
            panel.Children.Add(SecondAddress);
        }
        panel.Children.Add(new TextBlock { Text = "Original request (retained unchanged in the selected conversation)" });
        AutomationProperties.SetName(Request, "Unchanged original skill request");
        panel.Children.Add(Request);
        if (toolCapable) { AddButton("Review exact target and tool revision", "ReviewToolRevision"); }
        AddButton("Review exact invocation", "Prepare");
        panel.Children.Add(Confirmation);
        panel.Children.Add(Confirm);
        panel.Children.Add(Stop);
        panel.Children.Add(Status);
        Confirm.Click += (_, _) => run("Invoke");
        Stop.Click += (_, _) => run("Close");
        Content = new ScrollViewer { Content = panel };
        Instructions.ContextMenu = null;
        Instructions.ContextFlyout = null;
        Confirmation.ContextMenu = null;
        Confirmation.ContextFlyout = null;

        void AddButton(string text, string action)
        {
            var button = new Button { Content = text };
            button.Click += (_, _) => run(action);
            operationButtons.Add(button);
            panel.Children.Add(button);
        }
    }

    internal void SetReferenceConsent(bool pending)
    {
        ReadReferences.IsVisible = pending;
        ReadReferences.IsEnabled = pending;
        foreach (var button in operationButtons) { button.IsEnabled = !pending; }
        Request.IsEnabled = !pending;
        Targets.IsEnabled = !pending;
    }

    internal void Clear()
    {
        Instructions.Text = string.Empty;
        Request.Text = string.Empty;
        Address.Text = string.Empty;
        SecondAddress.Text = string.Empty;
        Confirmation.Text = string.Empty;
        Targets.ItemsSource = null;
        Status.Text = string.Empty;
        Confirm.IsEnabled = false;
        ReadReferences.IsEnabled = false;
    }
}
