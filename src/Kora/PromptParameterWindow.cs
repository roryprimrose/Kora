using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Kora.Application.Definitions;
using Kora.Core.Artifacts;
using Kora.Core.Storage;

namespace Kora;

internal sealed class PromptParameterWindow : Window
{
    internal readonly Dictionary<string, TextBox> Inputs = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, Button> SpeakInputs = new(StringComparer.Ordinal);
    internal readonly Button CancelVoice = new() { Content = "Cancel spoken input" };
    internal readonly TextBlock VoiceStatus = new() { Text = "Voice input is unavailable.", TextWrapping = TextWrapping.Wrap };
    internal readonly Button Preview = new() { Content = "Preview complete expanded USER REQUEST" };
    internal readonly Button Use = new() { Content = "Use reviewed request (separate admission)", IsEnabled = false };
    internal readonly TextBlock Expanded = new() { TextWrapping = TextWrapping.Wrap };
    internal readonly TextBlock Status = new() { TextWrapping = TextWrapping.Wrap };
    internal readonly ListBox Targets = new() { MinHeight = 80, MaxHeight = 180 };
    internal readonly Button RefreshTargets = new() { Content = "Refresh session metadata" };
    internal readonly Button NextTargets = new() { Content = "Next metadata page", IsEnabled = false };
    internal readonly Button ReviewSubmit = new() { Content = "Review submission to exact Active session", IsEnabled = false };
    internal readonly Button ConfirmSubmit = new() { Content = "Submit this expanded request to exact session", IsEnabled = false };
    internal readonly Button CancelReview = new() { Content = "Cancel retention review", IsEnabled = false };
    internal readonly TextBlock RetentionReview = new() { TextWrapping = TextWrapping.Wrap };

    internal PromptParameterWindow(PromptParameterDraft draft, Action<string, string> edit, Action<string> run,
        Action<SessionWorkspaceEntry?>? select = null, Action<string>? speak = null, Action? cancelVoice = null)
    {
        Title = "Required prompt inputs — private preparation";
        Width = 880;
        Height = 720;
        var top = new StackPanel { Margin = new(12), Spacing = 8 };
        top.Children.Add(new TextBlock
        {
            Text = $"Owned prompt: {draft.Source.Id}\nOriginal digest: {draft.Source.Digest}\nNative revision: {draft.Source.OwnedInstructionRevision}\n"
                + "Native v2 required literal text only. No defaults, files, secrets, expressions, clipboard reads or recursive substitution. "
                + "Preview and Use are untrusted ephemeral user data, not retention approval. Editing an input retires review. "
                + "Only separate exact-session retention confirmation saves the expanded USER message; form values are not separately saved.\n"
                + "Production generation is unavailable: no qualified adapter/model. Use checks fresh request admission; it cannot install, authenticate or fall back.",
            TextWrapping = TextWrapping.Wrap,
        });
        foreach (var name in draft.RequiredNames)
        {
            var input = new TextBox { AcceptsReturn = true, MaxLength = OwnedPromptTemplate.MaximumValueLength, MinHeight = 42 };
            Inputs.Add(name, input);
            top.Children.Add(new TextBlock { Text = name + " (required; 512 UTF-16 / 2048 UTF-8 maximum)" });
            top.Children.Add(input);
            var voice = new Button { Content = "Speak input " + name, IsEnabled = speak is not null };
            SpeakInputs.Add(name, voice);
            voice.Click += (_, _) => speak?.Invoke(name);
            voice.LostFocus += (_, _) => cancelVoice?.Invoke();
            top.Children.Add(voice);
            input.PropertyChanged += (_, change) =>
            {
                if (change.Property == TextBox.TextProperty) { edit(name, input.Text ?? string.Empty); }
            };
        }
        CancelVoice.Click += (_, _) => cancelVoice?.Invoke();
        top.Children.Add(CancelVoice);
        top.Children.Add(VoiceStatus);
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
        top.Children.Add(Expanded);
        top.Children.Add(new TextBlock
        {
            Text = "Optional explicit retention: select ONE existing Active session. No default target, name/focus routing or implicit Resume. "
                + "Refresh/Next reads only bounded metadata (25 records, complete 64 KiB); pages can include no Active matches.",
            TextWrapping = TextWrapping.Wrap,
        });
        var targetButtons = new WrapPanel { Orientation = Orientation.Horizontal };
        RefreshTargets.Click += (_, _) => run("RefreshTargets");
        NextTargets.Click += (_, _) => run("NextTargets");
        targetButtons.Children.Add(RefreshTargets);
        targetButtons.Children.Add(NextTargets);
        top.Children.Add(targetButtons);
        Targets.SelectionChanged += (_, _) => select?.Invoke((Targets.SelectedItem as PromptConversationTarget)?.Entry);
        top.Children.Add(Targets);
        ReviewSubmit.Click += (_, _) => run("ReviewSubmit");
        ConfirmSubmit.Click += (_, _) => run("ConfirmSubmit");
        CancelReview.Click += (_, _) => run("CancelReview");
        top.Children.Add(ReviewSubmit);
        top.Children.Add(RetentionReview);
        top.Children.Add(ConfirmSubmit);
        top.Children.Add(CancelReview);
        Content = new ScrollViewer { Content = top };
    }

    internal void RetirePreview()
    {
        Expanded.Text = string.Empty;
        Use.IsEnabled = false;
        RetireReview();
    }

    internal void RetireReview()
    {
        RetentionReview.Text = string.Empty;
        ReviewSubmit.IsEnabled = false;
        ConfirmSubmit.IsEnabled = false;
        CancelReview.IsEnabled = false;
    }

    internal void ClearPrivateText()
    {
        foreach (var input in Inputs.Values) { input.Text = string.Empty; }
        RetirePreview();
        Status.Text = string.Empty;
        VoiceStatus.Text = string.Empty;
        Targets.ItemsSource = null;
        Content = null;
    }
}
