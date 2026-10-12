using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Kora.Application.Definitions;
using Kora.Core.Artifacts;

namespace Kora;

internal sealed class PromptParameterWindow : Window
{
    internal readonly Dictionary<string, TextBox> Inputs = new(StringComparer.Ordinal);
    internal readonly Button Preview = new() { Content = "Preview complete expanded USER REQUEST" };
    internal readonly Button Use = new() { Content = "Use reviewed request (separate admission)", IsEnabled = false };
    internal readonly TextBlock Expanded = new() { TextWrapping = TextWrapping.Wrap };
    internal readonly TextBlock Status = new() { TextWrapping = TextWrapping.Wrap };

    internal PromptParameterWindow(PromptParameterDraft draft, Action<string, string> edit, Action<string> run)
    {
        Title = "Required prompt inputs — private preparation";
        Width = 880;
        Height = 720;
        var root = new DockPanel { Margin = new(12) };
        var top = new StackPanel { Spacing = 8 };
        top.Children.Add(new TextBlock
        {
            Text = $"Owned prompt: {draft.Source.Id}\nOriginal digest: {draft.Source.Digest}\nNative revision: {draft.Source.OwnedInstructionRevision}\n"
                + "Native v2 required literal text only. No defaults, files, secrets, expressions, clipboard reads or recursive substitution. "
                + "Preview is untrusted user data, not approval. Editing an input retires it. Nothing is saved or remembered.\n"
                + "Production generation is unavailable: no qualified adapter/model. Use checks fresh request admission; it cannot install, authenticate or fall back.",
            TextWrapping = TextWrapping.Wrap,
        });
        foreach (var name in draft.RequiredNames)
        {
            var input = new TextBox { AcceptsReturn = true, MaxLength = OwnedPromptTemplate.MaximumValueLength, MinHeight = 42 };
            Inputs.Add(name, input);
            top.Children.Add(new TextBlock { Text = name + " (required; 512 UTF-16 / 2048 UTF-8 maximum)" });
            top.Children.Add(input);
            input.PropertyChanged += (_, change) =>
            {
                if (change.Property == TextBox.TextProperty) { edit(name, input.Text ?? string.Empty); }
            };
        }
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        Preview.Click += (_, _) => run("Preview");
        Use.Click += (_, _) => run("Use");
        var cancel = new Button { Content = "Cancel", Margin = new(8, 0) };
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(Preview);
        buttons.Children.Add(Use);
        buttons.Children.Add(cancel);
        top.Children.Add(buttons);
        top.Children.Add(Status);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(new ScrollViewer { Content = Expanded });
        Content = root;
    }

    internal void RetirePreview()
    {
        Expanded.Text = string.Empty;
        Use.IsEnabled = false;
    }

    internal void ClearPrivateText()
    {
        foreach (var input in Inputs.Values) { input.Text = string.Empty; }
        RetirePreview();
        Status.Text = string.Empty;
        Content = null;
    }
}
