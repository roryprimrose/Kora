using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Kora;

internal sealed class OwnedSkillInvocationWindow : Window
{
    internal TextBox Instructions { get; } = ReadOnly("Complete exact saved owned skill package", 200);
    internal TextBox Request { get; } = new() { AcceptsReturn = true, IsUndoEnabled = false, Height = 100 };
    internal TextBox Address { get; } = new() { IsUndoEnabled = false };
    internal ComboBox Targets { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    internal TextBox Confirmation { get; } = ReadOnly("Complete serialized local context preview", 200);
    internal TextBlock Status { get; } = new() { TextWrapping = TextWrapping.Wrap };
    internal Button Confirm { get; } = new() { Content = "Confirm exact invocation once", IsEnabled = false };
    internal PrivateLiteralInputControls LiteralInputs { get; } = new();

    internal OwnedSkillInvocationWindow(Action<string> run, bool toolMode = false)
    {
        Title = "Review one use of a saved owned skill";
        Width = 900;
        Height = 850;
        var panel = new StackPanel { Margin = new(16), Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = "Kora-owned private package; complete inert guidance. LocalOnly; "
                + (toolMode ? "at most ONE host WebPageGet call to ONE explicit URL with separate per-action approval; " : "tools none; ")
                + "references none. No scripts, hooks, MCP, nested agents or hosted transfer. "
                + "Saving never invokes. Enable this exact revision separately for one volatile use, expiring two minutes after review. "
                + "Close, edits after preparation, source or admission changes retire use. No automatic retry.",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(Instructions);
        panel.Children.Add(LiteralInputs);
        Add("Enable exact reviewed revision once", "enable");
        Add("Refresh existing Active conversations", "targets");
        Add("Next conversation page", "next-targets");
        AutomationProperties.SetName(Targets, "Exact current Active target conversation");
        panel.Children.Add(Targets);
        if (toolMode) { Add("Review exact target and tool revision", "review-tool"); }
        panel.Children.Add(new TextBlock { Text = "Original request, retained unchanged in the selected conversation" });
        AutomationProperties.SetName(Request, "Unchanged original request");
        panel.Children.Add(Request);
        if (toolMode)
        {
            panel.Children.Add(new TextBlock { Text = "One explicit public HTTP or HTTPS URL; never model-selected" });
            AutomationProperties.SetName(Address, "Exact user-selected WebPageGet destination");
            panel.Children.Add(Address);
        }
        Add("Preview complete local invocation", "review-use");
        panel.Children.Add(new TextBlock
        {
            Text = "The preview includes all serialized input. Host request/evidence correlation IDs are freshly stamped on admission; "
                + "policy, original request and both complete evidence files are unchanged. Nothing is truncated.",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(Confirmation);
        panel.Children.Add(Confirm);
        Confirm.Click += (_, _) => run("invoke");
        Add("Cancel and close use", "close-use");
        panel.Children.Add(Status);
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
        Instructions.Text = string.Empty;
        Request.Text = string.Empty;
        Address.Text = string.Empty;
        Confirmation.Text = string.Empty;
        Targets.ItemsSource = null;
        Status.Text = string.Empty;
        Confirm.IsEnabled = false;
        LiteralInputs.Clear();
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
