using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Kora;

internal sealed class SharedSkillInvocationWindow : Window
{
    internal TextBox Instructions { get; } = new() { IsReadOnly = true, AcceptsReturn = true, IsUndoEnabled = false, Height = 220, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    internal TextBox Request { get; } = new() { AcceptsReturn = true, IsUndoEnabled = false, Height = 100 };
    internal ComboBox Targets { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    internal TextBlock Status { get; } = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    internal TextBox Confirmation { get; } = new() { IsReadOnly = true, AcceptsReturn = true, IsUndoEnabled = false, Height = 150 };
    internal Button Confirm { get; } = new() { Content = "Confirm exact skill, original request and target", IsEnabled = false };

    internal SharedSkillInvocationWindow(Action<string> run)
    {
        Title = "Reviewed instruction-only shared skill";
        Width = 850;
        Height = 850;
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = "Complete inert untrusted instructions. Local model only; ZERO tools, network or side effects. "
                + "Enablement is separate, memory-only, expires in two minutes and is single-use. Closing or admission/source changes revoke it.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        panel.Children.Add(Instructions);
        AddButton("Enable exact reviewed revision", "Enable");
        AddButton("Refresh existing Active conversations", "Targets");
        AddButton("Next conversation page", "NextTargets");
        panel.Children.Add(Targets);
        panel.Children.Add(new TextBlock { Text = "Original request (retained unchanged in the selected conversation)" });
        panel.Children.Add(Request);
        AddButton("Review exact invocation", "Prepare");
        panel.Children.Add(Confirmation);
        panel.Children.Add(Confirm);
        panel.Children.Add(Status);
        Confirm.Click += (_, _) => run("Invoke");
        Content = new ScrollViewer { Content = panel };
        Instructions.ContextMenu = null;
        Instructions.ContextFlyout = null;
        Confirmation.ContextMenu = null;
        Confirmation.ContextFlyout = null;

        void AddButton(string text, string action)
        {
            var button = new Button { Content = text };
            button.Click += (_, _) => run(action);
            panel.Children.Add(button);
        }
    }

    internal void Clear()
    {
        Instructions.Text = string.Empty;
        Request.Text = string.Empty;
        Confirmation.Text = string.Empty;
        Targets.ItemsSource = null;
        Status.Text = string.Empty;
        Confirm.IsEnabled = false;
    }
}
